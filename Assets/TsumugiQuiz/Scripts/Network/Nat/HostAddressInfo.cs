using System.Collections.Generic;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// ホストの到達性情報（不変）。#5 の HostSetup 画面がこの値だけを見て
    /// 「参加コード」「手動ポート開放案内」「CGNAT 警告」を出せるようにする。
    ///
    /// 値の差し替えは <see cref="WithManualPublicIpAddress"/> / <see cref="WithPortMapping"/> のように
    /// 新しいインスタンスを返す形で行う。
    ///
    /// <c>default(HostAddressInfo)</c>（未解決の初期値）でも全プロパティが安全に読める
    /// （文字列は空文字、<see cref="Warnings"/> は「未取得」の案内を返す）。
    /// </summary>
    public readonly struct HostAddressInfo
    {
        private readonly string _lanIpAddress;
        private readonly string _manualPublicIpAddress;

        private HostAddressInfo(
            PortMappingResult portMapping,
            PublicIpResult publicIp,
            string lanIpAddress,
            ushort internalPort,
            string manualPublicIpAddress)
        {
            PortMapping = portMapping;
            PublicIp = publicIp;
            _lanIpAddress = lanIpAddress;
            InternalPort = internalPort;
            _manualPublicIpAddress = manualPublicIpAddress;
        }

        /// <summary>自動ポート開放の結果。</summary>
        public PortMappingResult PortMapping { get; }

        /// <summary>グローバル IP の取得結果（自動取得分）。</summary>
        public PublicIpResult PublicIp { get; }

        /// <summary>このPCの LAN IPv4（判明しなければ空文字）。</summary>
        public string LanIpAddress => _lanIpAddress ?? string.Empty;

        /// <summary>ホストが待ち受けている内部ポート。未解決なら 0。</summary>
        public ushort InternalPort { get; }

        /// <summary>ユーザーが手入力したグローバル IP（未入力なら空文字）。</summary>
        public string ManualPublicIpAddress => _manualPublicIpAddress ?? string.Empty;

        /// <summary>
        /// インターネット用参加コードに使うアドレス。手入力があればそちらを優先する。
        /// </summary>
        public string PublicIpAddress
        {
            get
            {
                var manual = ManualPublicIpAddress;
                return manual.Length > 0 ? manual : PublicIp.Address;
            }
        }

        /// <summary>
        /// インターネット用参加コードに使う外部ポート。
        /// 自動開放できた場合はルーターが割り当てた値、できなかった場合は内部ポートと同じ値
        /// （手動開放案内でも同じ値を開けてもらう）。
        /// </summary>
        public ushort ExternalPort => PortMapping.Success && PortMapping.ExternalPort != 0
            ? PortMapping.ExternalPort
            : InternalPort;

        /// <summary>
        /// CGNAT 配下で直接接続できないか。
        /// **手入力されたアドレスには適用しない**（Tailscale の 100.x を使う手順を塞がないため。
        /// docs/network-nat.md §1.5 の注記）。
        /// </summary>
        public bool IsCarrierGradeNat => ManualPublicIpAddress.Length == 0 && PublicIp.IsCarrierGradeNat;

        /// <summary>二重 NAT の疑いがあるか（ルーターの外部 IP と確認サービスの結果が不一致）。</summary>
        public bool IsDoubleNatSuspected => ManualPublicIpAddress.Length == 0 && PublicIp.AddressesDisagree;

        /// <summary>手動ポート開放の案内が必要か。</summary>
        public bool RequiresManualPortForwarding => !PortMapping.Success;

        /// <summary>インターネット用の参加コードを作れるか。</summary>
        public bool CanCreateInternetCode
            => ExternalPort != 0
               && !IsCarrierGradeNat
               && IpRangeClassifier.TryParseIpv4(PublicIpAddress, out _);

        /// <summary>LAN 用の参加コードを作れるか。</summary>
        public bool CanCreateLanCode => InternalPort != 0 && LanIpResolver.IsUsableLanAddress(LanIpAddress);

        /// <summary>
        /// 手動ポート開放案内の内容（docs/network-nat.md §1.5）。
        /// 内部ポートが 0 の場合（未解決）は既定値を返す。
        /// </summary>
        public ManualPortMappingGuide ManualGuide
            => InternalPort == 0
                ? default
                : ManualPortMappingGuide.Create(ExternalPort, InternalPort, LanIpAddress);

        /// <summary>
        /// 画面に出す警告文（0〜3 件）。深刻な順に並べる。
        /// </summary>
        public IReadOnlyList<string> Warnings
        {
            get
            {
                var warnings = new List<string>(3);

                if (IsCarrierGradeNat)
                {
                    warnings.Add(
                        "このインターネット回線は CGNAT（大規模 NAT）のため、外部から直接接続できません。"
                        + "同じ LAN 内の人は「LAN 用コード」で参加できます。インターネット越しに遊ぶには Tailscale をお使いください（README 参照）。");
                }

                if (RequiresManualPortForwarding)
                {
                    warnings.Add(PortMapping.Message.Length > 0
                        ? PortMapping.Message
                        : "自動ポート開放に失敗しました。ルーターの設定画面でポートを開放してください。");
                }

                if (IsDoubleNatSuspected)
                {
                    warnings.Add(
                        $"ルーターが認識している外部 IP（{PublicIp.NatDeviceAddress}）と、"
                        + $"インターネット側から見えるアドレス（{PublicIp.LookupServiceAddress}）が異なります。"
                        + "二重 NAT の可能性があり、自動ポート開放が効かないことがあります。");
                }

                if (!PublicIp.Found && ManualPublicIpAddress.Length == 0)
                {
                    warnings.Add(PublicIp.Message.Length > 0
                        ? PublicIp.Message
                        : "グローバル IP を自動取得できませんでした。手入力してください。");
                }

                return warnings.AsReadOnly();
            }
        }

        /// <summary>
        /// 情報を組み立てる。
        /// </summary>
        /// <param name="portMapping">自動ポート開放の結果。</param>
        /// <param name="publicIp">グローバル IP の取得結果。</param>
        /// <param name="lanIpAddress">LAN IPv4。</param>
        /// <param name="internalPort">内部ポート。</param>
        public static HostAddressInfo Create(
            PortMappingResult portMapping,
            PublicIpResult publicIp,
            string lanIpAddress,
            ushort internalPort)
            => new HostAddressInfo(portMapping, publicIp, lanIpAddress, internalPort, string.Empty);

        /// <summary>
        /// まだ何も解決していない状態（すべて未取得）を表す値。
        /// <c>default(HostAddressInfo)</c> と等価だが、意図を明示するために使う。
        /// </summary>
        public static HostAddressInfo NotResolved { get; } = new HostAddressInfo(
            PortMappingResult.NotAttempted, PublicIpResult.NotResolved, string.Empty, 0, string.Empty);

        /// <summary>
        /// 手入力されたグローバル IP を反映した新しいインスタンスを返す（docs/network-nat.md §2 の段 3）。
        /// IPv4 として読めない値、および外部から到達できない帯（プライベート・ループバック・リンクローカル・
        /// 予約・マルチキャスト・ブロードキャスト）は無視して空扱いにする。
        /// **CGNAT 帯（100.64.0.0/10）は受け付ける**（Tailscale IP を手入力する手順のため。§1.5 の注記）。
        /// </summary>
        /// <param name="address">ユーザーが入力したアドレス。空文字で手入力を取り消す。</param>
        public HostAddressInfo WithManualPublicIpAddress(string address)
        {
            var normalized = string.IsNullOrWhiteSpace(address) ? string.Empty : address.Trim();
            if (normalized.Length > 0 && !IsAcceptableManualAddress(normalized))
            {
                normalized = string.Empty;
            }

            return new HostAddressInfo(PortMapping, PublicIp, LanIpAddress, InternalPort, normalized);
        }

        /// <summary>
        /// 自動ポート開放の結果だけを差し替えた新しいインスタンスを返す
        /// （マッピング更新時に <see cref="HostConnectivityService"/> が使う）。
        /// </summary>
        /// <param name="portMapping">新しい結果。</param>
        public HostAddressInfo WithPortMapping(PortMappingResult portMapping)
            => new HostAddressInfo(portMapping, PublicIp, LanIpAddress, InternalPort, ManualPublicIpAddress);

        /// <summary>
        /// 手入力のアドレスとして受け付けられるか。
        /// グローバルアドレスと CGNAT 帯（Tailscale）だけを許可する。
        /// </summary>
        /// <param name="address">検証するアドレス。</param>
        public static bool IsAcceptableManualAddress(string address)
        {
            switch (IpRangeClassifier.Classify(address))
            {
                case IpAddressCategory.Public:
                case IpAddressCategory.CarrierGradeNat:
                    return true;
                default:
                    return false;
            }
        }
    }
}
