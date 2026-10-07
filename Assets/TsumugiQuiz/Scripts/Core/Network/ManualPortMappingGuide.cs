using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 自動ポート開放に失敗したときに画面へ出す「手動ポート開放案内」の内容（不変）。
    /// docs/network-nat.md §1.5 で必須とされている 4 点
    /// （プロトコル / 外部ポート / 内部ポート / このPCの LAN IP）だけを持つ。
    ///
    /// 画面（#5 の HostSetup View）はこの値を読んで表示するだけにし、文言の組み立てはここに閉じる。
    /// </summary>
    public readonly struct ManualPortMappingGuide : IEquatable<ManualPortMappingGuide>
    {
        /// <summary>案内に載せるプロトコル名。Unity Transport は UDP のみを使う。</summary>
        public const string UdpProtocolName = "UDP";

        // default(ManualPortMappingGuide) でも null 参照にならないよう、文字列はプロパティ側で畳む。
        private readonly string _protocol;
        private readonly string _lanIpAddress;

        private ManualPortMappingGuide(string protocol, ushort externalPort, ushort internalPort, string lanIpAddress)
        {
            _protocol = protocol;
            ExternalPort = externalPort;
            InternalPort = internalPort;
            _lanIpAddress = lanIpAddress;
        }

        /// <summary>プロトコル名（常に <see cref="UdpProtocolName"/>）。</summary>
        public string Protocol => _protocol ?? UdpProtocolName;

        /// <summary>ルーターに設定してもらう外部ポート。</summary>
        public ushort ExternalPort { get; }

        /// <summary>転送先の内部ポート。</summary>
        public ushort InternalPort { get; }

        /// <summary>転送先（このPC）の LAN IP。判明しなかった場合は空文字。</summary>
        public string LanIpAddress => _lanIpAddress ?? string.Empty;

        /// <summary>LAN IP を表示できるか（不明なら「このPCのIPアドレス」と出すしかない）。</summary>
        public bool HasLanIpAddress => !string.IsNullOrEmpty(LanIpAddress);

        /// <summary>
        /// 案内を作る。
        /// </summary>
        /// <param name="externalPort">外部ポート。0 は指定できない。</param>
        /// <param name="internalPort">内部ポート。0 は指定できない。</param>
        /// <param name="lanIpAddress">このPCの LAN IP。null / 空 / IPv4 として読めない値は「不明」として扱う。</param>
        /// <exception cref="ArgumentOutOfRangeException">ポートが 0。</exception>
        public static ManualPortMappingGuide Create(ushort externalPort, ushort internalPort, string lanIpAddress)
        {
            if (externalPort == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(externalPort), externalPort, "外部ポートに 0 は指定できません。");
            }

            if (internalPort == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(internalPort), internalPort, "内部ポートに 0 は指定できません。");
            }

            var normalizedIp = IpRangeClassifier.TryParseIpv4(lanIpAddress, out _)
                ? lanIpAddress.Trim()
                : string.Empty;

            return new ManualPortMappingGuide(UdpProtocolName, externalPort, internalPort, normalizedIp);
        }

        /// <summary>
        /// 画面にそのまま並べられる「項目名: 値」の行を返す。
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> ToDisplayRows()
            => Array.AsReadOnly(new[]
            {
                new KeyValuePair<string, string>("プロトコル", Protocol),
                new KeyValuePair<string, string>("外部ポート", ExternalPort.ToString()),
                new KeyValuePair<string, string>("内部ポート", InternalPort.ToString()),
                new KeyValuePair<string, string>("宛先 IP", HasLanIpAddress ? LanIpAddress : "（このPCのLAN IPを確認してください）"),
            });

        /// <inheritdoc />
        public override string ToString()
        {
            var rows = ToDisplayRows();
            var lines = new string[rows.Count];
            for (var i = 0; i < rows.Count; i++)
            {
                lines[i] = rows[i].Key + ": " + rows[i].Value;
            }

            return string.Join("\n", lines);
        }

        /// <inheritdoc />
        public bool Equals(ManualPortMappingGuide other)
            => Protocol == other.Protocol
               && ExternalPort == other.ExternalPort
               && InternalPort == other.InternalPort
               && LanIpAddress == other.LanIpAddress;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is ManualPortMappingGuide other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Protocol != null ? Protocol.GetHashCode() : 0;
                hash = (hash * 397) ^ ExternalPort.GetHashCode();
                hash = (hash * 397) ^ InternalPort.GetHashCode();
                hash = (hash * 397) ^ (LanIpAddress != null ? LanIpAddress.GetHashCode() : 0);
                return hash;
            }
        }
    }
}
