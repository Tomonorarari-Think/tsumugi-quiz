using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// アプリ設定・ルーム設定の既定値のうち、複数レイヤー（<c>TsumugiQuiz.Network.Nat.NatOptions</c> /
    /// <c>TsumugiQuiz.Tts.TtsSettings</c> / <c>TsumugiQuiz.Tts.VoicevoxStyleResolver</c> /
    /// <c>TsumugiQuiz.Network.NetworkConstants</c> / <c>TsumugiQuiz.Network.QuestionDistributor</c> /
    /// <c>TsumugiQuiz.Room.AppSettings</c>）で同じ値を持っていたものを 1 か所に集約する（#26 統括判断 M6）。
    /// <c>Core</c> はすべてのレイヤーから参照できるため、ここを唯一の既定値の出典にする。
    /// </summary>
    /// <remarks>
    /// 範囲（Min/Max）やクランプの計算式はここでは持たない（各層の呼び出し元に既に実装があり、
    /// 統括判断でも「既定値の重複」の解消に限定しているため）。将来的に範囲もここへ集約する場合は
    /// 別途 issue で判断すること。
    /// </remarks>
    public static class SettingsDefaults
    {
        /// <summary><c>network.port</c> の既定値（docs/room-settings.md §2、docs/network.md §2.1）。</summary>
        public const int NetworkPort = 7777;

        /// <summary><c>question.prefetchCount</c> の既定値（docs/room-settings.md §2、docs/network.md §8.1）。</summary>
        public const int QuestionPrefetchCount = 1;

        /// <summary><c>upnp.enabled</c> の既定値。</summary>
        public const bool UpnpEnabled = true;

        /// <summary><c>upnp.discoveryTimeoutMs</c> の既定値（ミリ秒）。</summary>
        public const int UpnpDiscoveryTimeoutMs = 5000;

        /// <summary><c>upnp.mappingLifetimeSec</c> の既定値（秒）。</summary>
        public const int UpnpMappingLifetimeSec = 3600;

        /// <summary><c>upnp.renewIntervalMs</c> の既定値（ミリ秒、30分）。</summary>
        public const int UpnpRenewIntervalMs = 1800000;

        /// <summary><c>network.ipLookupUrls</c> の既定値。</summary>
        public static readonly IReadOnlyList<string> IpLookupUrls =
            new ReadOnlyCollection<string>(new[] { "https://api.ipify.org" });

        /// <summary><c>tts.speakerName</c> の既定値（仮決め K16）。</summary>
        public const string TtsSpeakerName = "春日部つむぎ";

        /// <summary><c>tts.styleName</c> の既定値（仮決め K16）。</summary>
        public const string TtsStyleName = "ノーマル";

        /// <summary><c>tts.cacheMaxBytes</c> の既定値（200MB）。</summary>
        public const long TtsCacheMaxBytes = 200L * 1024 * 1024;

        /// <summary><c>tts.cacheMaxEntries</c> の既定値。</summary>
        public const int TtsCacheMaxEntries = 5000;
    }
}
