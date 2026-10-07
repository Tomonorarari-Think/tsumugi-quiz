using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// UPnP / NAT-PMP とグローバル IP 取得の設定（不変）。
    /// docs/room-settings.md §2 の <c>upnp.*</c> と <c>network.ipLookupUrls</c> に対応する
    /// **アプリ設定**（各 PC ローカル。クライアントへは同期しない）。
    ///
    /// アプリ設定ファイル（<c>app-settings.json</c>）の読み書きは別 issue のため、
    /// ここでは既定値と検証だけを持ち、<see cref="With"/> で部分的に差し替えた新しいインスタンスを返す。
    /// </summary>
    public sealed class NatOptions
    {
        /// <summary><c>upnp.enabled</c> の既定値（<see cref="SettingsDefaults.UpnpEnabled"/> と同じ、#26 統括判断 M6）。</summary>
        public const bool DefaultEnabled = SettingsDefaults.UpnpEnabled;

        /// <summary><c>upnp.discoveryTimeoutMs</c> の既定値（5 秒、<see cref="SettingsDefaults.UpnpDiscoveryTimeoutMs"/> と同じ）。</summary>
        public const int DefaultDiscoveryTimeoutMs = SettingsDefaults.UpnpDiscoveryTimeoutMs;

        /// <summary><c>upnp.discoveryTimeoutMs</c> の下限。</summary>
        public const int MinDiscoveryTimeoutMs = 1000;

        /// <summary><c>upnp.discoveryTimeoutMs</c> の上限。</summary>
        public const int MaxDiscoveryTimeoutMs = 30000;

        /// <summary><c>upnp.mappingLifetimeSec</c> の既定値（3600 秒、<see cref="SettingsDefaults.UpnpMappingLifetimeSec"/> と同じ）。</summary>
        public const int DefaultMappingLifetimeSec = SettingsDefaults.UpnpMappingLifetimeSec;

        /// <summary><c>upnp.mappingLifetimeSec</c> の上限（0 = 無期限）。</summary>
        public const int MaxMappingLifetimeSec = 86400;

        /// <summary><c>upnp.renewIntervalMs</c> の既定値（30 分、<see cref="SettingsDefaults.UpnpRenewIntervalMs"/> と同じ）。</summary>
        public const int DefaultRenewIntervalMs = SettingsDefaults.UpnpRenewIntervalMs;

        /// <summary><c>upnp.renewIntervalMs</c> の下限（過剰なリクエストでルーターを詰まらせないため）。</summary>
        public const int MinRenewIntervalMs = 10000;

        /// <summary>IP 確認サービスの 1 件あたりのタイムアウト（docs/network-nat.md §2）。</summary>
        public const int IpLookupTimeoutMs = 5000;

        /// <summary>IP 確認サービスの応答として受け付ける最大バイト数（docs/network-nat.md §2）。</summary>
        public const int IpLookupMaxResponseBytes = 64;

        /// <summary>ポートマッピングに付ける説明。古いマッピングの掃除にも使う（docs/network-nat.md §1.4）。</summary>
        public const string MappingDescription = "TsumugiQuiz";

        /// <summary><c>network.ipLookupUrls</c> の既定値（<see cref="SettingsDefaults.IpLookupUrls"/> と同じ）。</summary>
        public static readonly IReadOnlyList<string> DefaultIpLookupUrls = SettingsDefaults.IpLookupUrls;

        private NatOptions(
            bool enabled,
            int discoveryTimeoutMs,
            int mappingLifetimeSec,
            int renewIntervalMs,
            IReadOnlyList<string> ipLookupUrls)
        {
            Enabled = enabled;
            DiscoveryTimeoutMs = discoveryTimeoutMs;
            MappingLifetimeSec = mappingLifetimeSec;
            RenewIntervalMs = renewIntervalMs;
            IpLookupUrls = ipLookupUrls;
        }

        /// <summary>すべて既定値の設定。</summary>
        public static NatOptions Default { get; } = new NatOptions(
            DefaultEnabled,
            DefaultDiscoveryTimeoutMs,
            DefaultMappingLifetimeSec,
            DefaultRenewIntervalMs,
            DefaultIpLookupUrls);

        /// <summary><c>upnp.enabled</c>。false なら自動ポート開放を試みない。</summary>
        public bool Enabled { get; }

        /// <summary><c>upnp.discoveryTimeoutMs</c>。NAT デバイス探索のタイムアウト。</summary>
        public int DiscoveryTimeoutMs { get; }

        /// <summary><c>upnp.mappingLifetimeSec</c>。0 は無期限。</summary>
        public int MappingLifetimeSec { get; }

        /// <summary><c>upnp.renewIntervalMs</c>。マッピングを作り直す間隔。</summary>
        public int RenewIntervalMs { get; }

        /// <summary><c>network.ipLookupUrls</c>。先頭から順に試す。</summary>
        public IReadOnlyList<string> IpLookupUrls { get; }

        /// <summary>
        /// 一部の値だけを差し替えた新しい設定を返す（元のインスタンスは変更しない）。
        /// 範囲外の値は例外にせず、docs/room-settings.md §2 の範囲へ丸める
        /// （壊れた設定ファイルでホストを起動不能にしないため）。
        /// </summary>
        /// <param name="enabled"><c>upnp.enabled</c>。null なら現在値。</param>
        /// <param name="discoveryTimeoutMs"><c>upnp.discoveryTimeoutMs</c>。null なら現在値。</param>
        /// <param name="mappingLifetimeSec"><c>upnp.mappingLifetimeSec</c>。null なら現在値。</param>
        /// <param name="renewIntervalMs"><c>upnp.renewIntervalMs</c>。null なら現在値。</param>
        /// <param name="ipLookupUrls"><c>network.ipLookupUrls</c>。null なら現在値。空配列は既定値に戻す。</param>
        public NatOptions With(
            bool? enabled = null,
            int? discoveryTimeoutMs = null,
            int? mappingLifetimeSec = null,
            int? renewIntervalMs = null,
            IReadOnlyList<string> ipLookupUrls = null)
        {
            var lifetimeSec = Clamp(mappingLifetimeSec ?? MappingLifetimeSec, 0, MaxMappingLifetimeSec);

            return new NatOptions(
                enabled ?? Enabled,
                Clamp(discoveryTimeoutMs ?? DiscoveryTimeoutMs, MinDiscoveryTimeoutMs, MaxDiscoveryTimeoutMs),
                lifetimeSec,
                ClampRenewInterval(renewIntervalMs ?? RenewIntervalMs, lifetimeSec),
                NormalizeUrls(ipLookupUrls) ?? IpLookupUrls);
        }

        /// <summary>
        /// 更新間隔を「lifetime の半分」以下に丸める。
        /// 更新間隔が lifetime を超えていると、更新が走る前にルーター側でマッピングが失効し、
        /// ホストが気づかないまま接続不能になるため（余裕を見て半分にする）。
        /// <c>lifetimeSec == 0</c>（無期限）のときは上限を設けない。
        /// </summary>
        /// <param name="renewIntervalMs">要求された更新間隔（ミリ秒）。</param>
        /// <param name="lifetimeSec">マッピングの有効期間（秒）。0 は無期限。</param>
        public static int ClampRenewInterval(int renewIntervalMs, int lifetimeSec)
        {
            var value = Math.Max(renewIntervalMs, MinRenewIntervalMs);
            if (lifetimeSec <= 0)
            {
                return value;
            }

            // lifetime が極端に短い場合でも下限（MinRenewIntervalMs）は割らない。
            var upperBound = Math.Max(MinRenewIntervalMs, lifetimeSec * 1000 / 2);
            return Math.Min(value, upperBound);
        }

        /// <summary>
        /// URL 配列を検証して正規化する。
        /// <c>https</c> の絶対 URL だけを残す（設定ファイル経由で平文 HTTP を差し込まれないようにする）。
        /// </summary>
        /// <param name="urls">検証する URL 配列。null なら null を返す。</param>
        /// <returns>有効な URL だけの配列。1 件も残らなければ既定値。</returns>
        private static IReadOnlyList<string> NormalizeUrls(IReadOnlyList<string> urls)
        {
            if (urls == null)
            {
                return null;
            }

            var normalized = new List<string>(urls.Count);
            foreach (var url in urls)
            {
                if (IsAllowedLookupUrl(url))
                {
                    normalized.Add(url.Trim());
                }
            }

            return normalized.Count > 0 ? normalized.AsReadOnly() : DefaultIpLookupUrls;
        }

        /// <summary>
        /// IP 確認サービスの URL として使えるか（絶対 URL かつ https）。
        /// </summary>
        /// <param name="url">検証する URL。</param>
        public static bool IsAllowedLookupUrl(string url)
            => !string.IsNullOrWhiteSpace(url)
               && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)
               && parsed.Scheme == Uri.UriSchemeHttps;

        private static int Clamp(int value, int min, int max)
            => value < min ? min : value > max ? max : value;
    }
}
