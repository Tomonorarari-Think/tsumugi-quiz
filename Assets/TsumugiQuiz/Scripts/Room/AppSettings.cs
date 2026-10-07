using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// アプリ設定（docs/room-settings.md §0/§2）。各 PC がローカルに持つ値で、クライアントへは同期しない。
    /// 保存先は既定で <see cref="AppSettingsStore.GetDefaultFilePath"/>（<c>TsumugiQuiz.Core.AppPaths.DataRoot</c>/app-settings.json）。
    /// </summary>
    /// <remarks>
    /// 値の検証は <see cref="Create"/> でクランプする方式を取る（<c>TsumugiQuiz.Tts.TtsSettings</c> と同様）。
    /// 設定ファイル 1 行の不備でアプリが起動できなくなるのを避けるため、例外は投げない。
    /// </remarks>
    public sealed class AppSettings
    {
        /// <summary><c>player.name</c> の既定値（issue #6）。</summary>
        public const string DefaultPlayerName = "";

        /// <summary><c>network.port</c> の既定値（<see cref="SettingsDefaults.NetworkPort"/> と同じ、#26 統括判断 M6）。</summary>
        public const int DefaultNetworkPort = SettingsDefaults.NetworkPort;

        /// <summary><c>network.port</c> の下限。</summary>
        public const int MinNetworkPort = 1024;

        /// <summary><c>network.port</c> の上限。</summary>
        public const int MaxNetworkPort = 65535;

        /// <summary>
        /// NGO の <c>NetworkConfig.TickRate</c> の固定値（<c>Boot.unity</c> の <c>NetworkManager</c> と同じ 30）。
        /// <c>network.tickRate</c> は設定項目から外した（統括判断、PR #92 Phase 2）。
        /// 理由: NGO 2.13.2 の <c>NetworkConfig.GetConfig()</c> は接続時の設定ハッシュに <c>TickRate</c> を含めており
        /// （<c>Runtime/Configuration/NetworkConfig.cs</c>）、値が違う参加者は接続できない。アプリ設定は
        /// 各 PC ローカルで同期されないため、ユーザーが変更できると事故になる（docs/room-settings.md §2）。
        /// 本定数は <see cref="AppSettingsValidator"/> の警告メッセージのためだけに残してある。
        /// </summary>
        public const int FixedNetworkTickRate = 30;

        /// <summary><c>upnp.enabled</c> の既定値（<see cref="SettingsDefaults.UpnpEnabled"/> と同じ）。</summary>
        public const bool DefaultUpnpEnabled = SettingsDefaults.UpnpEnabled;

        /// <summary><c>upnp.discoveryTimeoutMs</c> の既定値（<see cref="SettingsDefaults.UpnpDiscoveryTimeoutMs"/> と同じ）。</summary>
        public const int DefaultUpnpDiscoveryTimeoutMs = SettingsDefaults.UpnpDiscoveryTimeoutMs;

        /// <summary><c>upnp.discoveryTimeoutMs</c> の下限。</summary>
        public const int MinUpnpDiscoveryTimeoutMs = 1000;

        /// <summary><c>upnp.discoveryTimeoutMs</c> の上限。</summary>
        public const int MaxUpnpDiscoveryTimeoutMs = 30000;

        /// <summary><c>upnp.mappingLifetimeSec</c> の既定値（<see cref="SettingsDefaults.UpnpMappingLifetimeSec"/> と同じ）。</summary>
        public const int DefaultUpnpMappingLifetimeSec = SettingsDefaults.UpnpMappingLifetimeSec;

        /// <summary><c>upnp.mappingLifetimeSec</c> の上限（0 = 無期限）。</summary>
        public const int MaxUpnpMappingLifetimeSec = 86400;

        /// <summary><c>upnp.renewIntervalMs</c> の既定値（<see cref="SettingsDefaults.UpnpRenewIntervalMs"/> と同じ）。</summary>
        public const int DefaultUpnpRenewIntervalMs = SettingsDefaults.UpnpRenewIntervalMs;

        /// <summary><c>upnp.renewIntervalMs</c> の下限。</summary>
        public const int MinUpnpRenewIntervalMs = 10000;

        /// <summary><c>question.prefetchCount</c> の既定値（<see cref="SettingsDefaults.QuestionPrefetchCount"/> と同じ）。</summary>
        public const int DefaultQuestionPrefetchCount = SettingsDefaults.QuestionPrefetchCount;

        /// <summary><c>question.prefetchCount</c> の上限。</summary>
        public const int MaxQuestionPrefetchCount = 3;

        /// <summary><c>tts.speakerName</c> の既定値（仮決め K16、<see cref="SettingsDefaults.TtsSpeakerName"/> と同じ）。</summary>
        public const string DefaultTtsSpeakerName = SettingsDefaults.TtsSpeakerName;

        /// <summary><c>tts.styleName</c> の既定値（仮決め K16、<see cref="SettingsDefaults.TtsStyleName"/> と同じ）。</summary>
        public const string DefaultTtsStyleName = SettingsDefaults.TtsStyleName;

        /// <summary><c>tts.cacheMaxBytes</c> の既定値（200MB、<see cref="SettingsDefaults.TtsCacheMaxBytes"/> と同じ）。</summary>
        public const long DefaultTtsCacheMaxBytes = SettingsDefaults.TtsCacheMaxBytes;

        /// <summary><c>tts.cacheMaxEntries</c> の既定値（<see cref="SettingsDefaults.TtsCacheMaxEntries"/> と同じ）。</summary>
        public const int DefaultTtsCacheMaxEntries = SettingsDefaults.TtsCacheMaxEntries;

        /// <summary><c>network.ipLookupUrls</c> の既定値（<see cref="SettingsDefaults.IpLookupUrls"/> と同じ）。</summary>
        public static readonly IReadOnlyList<string> DefaultIpLookupUrls = SettingsDefaults.IpLookupUrls;

        /// <summary><c>character.enabled</c> の既定値（統括判断、2026-09-14）。</summary>
        public const bool DefaultCharacterEnabled = true;

        /// <summary><c>room.lastApplied</c> の既定値（未編集）。</summary>
        public const string DefaultRoomLastApplied = "";

        /// <summary>
        /// <c>host.role</c> の既定値（issue #155。従来の <c>PlayerPrefs</c> 暫定保存から移行した）。
        /// </summary>
        public const HostRole DefaultHostRole = HostRole.Player;

        private AppSettings(
            string playerName,
            int networkPort,
            IReadOnlyList<string> ipLookupUrls,
            bool upnpEnabled,
            int upnpDiscoveryTimeoutMs,
            int upnpMappingLifetimeSec,
            int upnpRenewIntervalMs,
            int questionPrefetchCount,
            string ttsSpeakerName,
            string ttsStyleName,
            long ttsCacheMaxBytes,
            int ttsCacheMaxEntries,
            string ttsAssetPathOverride,
            bool characterEnabled,
            string roomLastApplied,
            HostRole hostRole)
        {
            PlayerName = playerName;
            NetworkPort = networkPort;
            IpLookupUrls = ipLookupUrls;
            UpnpEnabled = upnpEnabled;
            UpnpDiscoveryTimeoutMs = upnpDiscoveryTimeoutMs;
            UpnpMappingLifetimeSec = upnpMappingLifetimeSec;
            UpnpRenewIntervalMs = upnpRenewIntervalMs;
            QuestionPrefetchCount = questionPrefetchCount;
            TtsSpeakerName = ttsSpeakerName;
            TtsStyleName = ttsStyleName;
            TtsCacheMaxBytes = ttsCacheMaxBytes;
            TtsCacheMaxEntries = ttsCacheMaxEntries;
            TtsAssetPathOverride = ttsAssetPathOverride;
            CharacterEnabled = characterEnabled;
            RoomLastApplied = roomLastApplied;
            HostRole = hostRole;
        }

        /// <summary>すべて既定値の設定。</summary>
        public static AppSettings Default { get; } = Create();

        /// <summary><c>player.name</c>。前回入力値。</summary>
        public string PlayerName { get; }

        /// <summary><c>network.port</c>。</summary>
        public int NetworkPort { get; }

        /// <summary><c>network.ipLookupUrls</c>。先頭から順に試す。</summary>
        public IReadOnlyList<string> IpLookupUrls { get; }

        /// <summary><c>upnp.enabled</c>。</summary>
        public bool UpnpEnabled { get; }

        /// <summary><c>upnp.discoveryTimeoutMs</c>。</summary>
        public int UpnpDiscoveryTimeoutMs { get; }

        /// <summary><c>upnp.mappingLifetimeSec</c>（0 = 無期限）。</summary>
        public int UpnpMappingLifetimeSec { get; }

        /// <summary><c>upnp.renewIntervalMs</c>。</summary>
        public int UpnpRenewIntervalMs { get; }

        /// <summary><c>question.prefetchCount</c>。</summary>
        public int QuestionPrefetchCount { get; }

        /// <summary><c>tts.speakerName</c>。</summary>
        public string TtsSpeakerName { get; }

        /// <summary><c>tts.styleName</c>。</summary>
        public string TtsStyleName { get; }

        /// <summary><c>tts.cacheMaxBytes</c>。</summary>
        public long TtsCacheMaxBytes { get; }

        /// <summary><c>tts.cacheMaxEntries</c>。</summary>
        public int TtsCacheMaxEntries { get; }

        /// <summary><c>tts.assetPathOverride</c>。空なら既定の探索順を使う。</summary>
        public string TtsAssetPathOverride { get; }

        /// <summary>
        /// 立ち絵を表示するか（<c>character.enabled</c>、#24 の <c>CharacterView</c>）。
        /// 各 PC のローカルな表示設定でクライアントへは同期しない。#28 で
        /// <c>TsumugiQuiz.UI.Views.Game.GameView</c>（<c>GameView.Character.cs</c>）に接続済み。
        /// </summary>
        public bool CharacterEnabled { get; }

        /// <summary>
        /// 直近に Settings View（#28）で編集・適用した <c>RoomSettings</c> のスナップショット
        /// （<c>room.lastApplied</c>）。空文字なら「編集中の設定が無い」。
        /// </summary>
        public string RoomLastApplied { get; }

        /// <summary>
        /// <c>host.role</c>（司会専任トグル、issue #155）。従来 <c>PlayerPrefs</c> に暫定保存していたが、
        /// 同一 PC の複数インスタンス・別データルート（<c>-tq-data-root</c> / <c>-IsolateDocuments</c>）で
        /// 共有されてしまう不具合があったため、他のアプリ設定と同じく本クラス（<c>app-settings.json</c>）へ移した。
        /// 権威は <c>TsumugiQuiz.Network.RoomSettingsSync</c> に一本化されているため
        /// （docs/room-settings.md §7.4）、本プロパティは「ホストを開始する前」の初期値としてのみ使う。
        /// </summary>
        public HostRole HostRole { get; }

        /// <summary>
        /// 値を検証・クランプして設定を作る。境界（設定ファイル）から来た値は必ずここを通す。
        /// <paramref name="playerName"/> は <see cref="PlayerNameValidator.TryNormalize"/> で書式検証し、
        /// 失敗（1〜16 文字の範囲外・制御文字を含む等）した場合は <see cref="DefaultPlayerName"/> にする
        /// （#26 統括判断 M2。警告の生成は <see cref="AppSettingsValidator"/> が別途、クランプ前後の差分から行う）。
        /// </summary>
        public static AppSettings Create(
            string playerName = DefaultPlayerName,
            int networkPort = DefaultNetworkPort,
            IReadOnlyList<string> ipLookupUrls = null,
            bool upnpEnabled = DefaultUpnpEnabled,
            int upnpDiscoveryTimeoutMs = DefaultUpnpDiscoveryTimeoutMs,
            int upnpMappingLifetimeSec = DefaultUpnpMappingLifetimeSec,
            int upnpRenewIntervalMs = DefaultUpnpRenewIntervalMs,
            int questionPrefetchCount = DefaultQuestionPrefetchCount,
            string ttsSpeakerName = DefaultTtsSpeakerName,
            string ttsStyleName = DefaultTtsStyleName,
            long ttsCacheMaxBytes = DefaultTtsCacheMaxBytes,
            int ttsCacheMaxEntries = DefaultTtsCacheMaxEntries,
            string ttsAssetPathOverride = null,
            bool characterEnabled = DefaultCharacterEnabled,
            string roomLastApplied = DefaultRoomLastApplied,
            HostRole hostRole = DefaultHostRole)
        {
            var lifetimeSec = Clamp(upnpMappingLifetimeSec, 0, MaxUpnpMappingLifetimeSec);

            return new AppSettings(
                NormalizePlayerName(playerName),
                Clamp(networkPort, MinNetworkPort, MaxNetworkPort),
                NormalizeIpLookupUrls(ipLookupUrls),
                upnpEnabled,
                Clamp(upnpDiscoveryTimeoutMs, MinUpnpDiscoveryTimeoutMs, MaxUpnpDiscoveryTimeoutMs),
                lifetimeSec,
                ClampRenewInterval(upnpRenewIntervalMs, lifetimeSec),
                Clamp(questionPrefetchCount, 0, MaxQuestionPrefetchCount),
                string.IsNullOrWhiteSpace(ttsSpeakerName) ? DefaultTtsSpeakerName : ttsSpeakerName.Trim(),
                string.IsNullOrWhiteSpace(ttsStyleName) ? DefaultTtsStyleName : ttsStyleName.Trim(),
                ttsCacheMaxBytes < 0 ? DefaultTtsCacheMaxBytes : ttsCacheMaxBytes,
                ttsCacheMaxEntries < 0 ? DefaultTtsCacheMaxEntries : ttsCacheMaxEntries,
                ttsAssetPathOverride == null ? string.Empty : ttsAssetPathOverride.Trim(),
                characterEnabled,
                roomLastApplied ?? DefaultRoomLastApplied,
                hostRole);
        }

        /// <summary><c>player.name</c> だけを差し替えた新しい設定を返す。</summary>
        public AppSettings WithPlayerName(string playerName) => Create(
            playerName, NetworkPort, IpLookupUrls, UpnpEnabled, UpnpDiscoveryTimeoutMs,
            UpnpMappingLifetimeSec, UpnpRenewIntervalMs, QuestionPrefetchCount, TtsSpeakerName, TtsStyleName,
            TtsCacheMaxBytes, TtsCacheMaxEntries, TtsAssetPathOverride, CharacterEnabled, RoomLastApplied, HostRole);

        /// <summary><c>character.enabled</c> だけを差し替えた新しい設定を返す。</summary>
        public AppSettings WithCharacterEnabled(bool characterEnabled) => Create(
            PlayerName, NetworkPort, IpLookupUrls, UpnpEnabled, UpnpDiscoveryTimeoutMs,
            UpnpMappingLifetimeSec, UpnpRenewIntervalMs, QuestionPrefetchCount, TtsSpeakerName, TtsStyleName,
            TtsCacheMaxBytes, TtsCacheMaxEntries, TtsAssetPathOverride, characterEnabled, RoomLastApplied, HostRole);

        /// <summary>
        /// <c>network.port</c> だけを差し替えた新しい設定を返す（issue #28 H2、HostSetup View の
        /// 保存先を <c>PlayerPrefs</c> から本クラスへ移行するために追加）。
        /// <see cref="MinNetworkPort"/>〜<see cref="MaxNetworkPort"/> の範囲外は <see cref="Create"/> がクランプする。
        /// </summary>
        public AppSettings WithNetworkPort(int networkPort) => Create(
            PlayerName, networkPort, IpLookupUrls, UpnpEnabled, UpnpDiscoveryTimeoutMs,
            UpnpMappingLifetimeSec, UpnpRenewIntervalMs, QuestionPrefetchCount, TtsSpeakerName, TtsStyleName,
            TtsCacheMaxBytes, TtsCacheMaxEntries, TtsAssetPathOverride, CharacterEnabled, RoomLastApplied, HostRole);

        /// <summary>
        /// <c>room.lastApplied</c> だけを差し替えた新しい設定を返す（issue #28 H4、
        /// <see cref="RoomSettingsDraft"/> が使う）。
        /// </summary>
        public AppSettings WithRoomLastApplied(string roomLastApplied) => Create(
            PlayerName, NetworkPort, IpLookupUrls, UpnpEnabled, UpnpDiscoveryTimeoutMs,
            UpnpMappingLifetimeSec, UpnpRenewIntervalMs, QuestionPrefetchCount, TtsSpeakerName, TtsStyleName,
            TtsCacheMaxBytes, TtsCacheMaxEntries, TtsAssetPathOverride, CharacterEnabled, roomLastApplied, HostRole);

        /// <summary>
        /// <c>host.role</c> だけを差し替えた新しい設定を返す（issue #155、<c>HostSetupPreferences</c> /
        /// <c>TsumugiQuiz.Network.HostRolePreference</c> が使う）。
        /// </summary>
        public AppSettings WithHostRole(HostRole hostRole) => Create(
            PlayerName, NetworkPort, IpLookupUrls, UpnpEnabled, UpnpDiscoveryTimeoutMs,
            UpnpMappingLifetimeSec, UpnpRenewIntervalMs, QuestionPrefetchCount, TtsSpeakerName, TtsStyleName,
            TtsCacheMaxBytes, TtsCacheMaxEntries, TtsAssetPathOverride, CharacterEnabled, RoomLastApplied, hostRole);

        /// <summary>
        /// 更新間隔を「lifetime の半分」以下に丸める（<c>TsumugiQuiz.Network.Nat.NatOptions</c> と同じ規則。
        /// <c>Room</c> は <c>Network</c> を参照できないためロジックを複製している）。
        /// </summary>
        private static int ClampRenewInterval(int renewIntervalMs, int lifetimeSec)
        {
            var value = Math.Max(renewIntervalMs, MinUpnpRenewIntervalMs);
            if (lifetimeSec <= 0)
            {
                return value;
            }

            var upperBound = Math.Max(MinUpnpRenewIntervalMs, lifetimeSec * 1000 / 2);
            return Math.Min(value, upperBound);
        }

        /// <summary>
        /// <see cref="PlayerNameValidator.TryNormalize"/> で書式検証する。未指定（null/空）は既定値のまま、
        /// 検証に失敗した場合も既定値（空文字列）に戻す（M2）。
        /// </summary>
        private static string NormalizePlayerName(string playerName)
        {
            if (string.IsNullOrEmpty(playerName))
            {
                return DefaultPlayerName;
            }

            return PlayerNameValidator.TryNormalize(playerName, out var normalized) ? normalized : DefaultPlayerName;
        }

        private static IReadOnlyList<string> NormalizeIpLookupUrls(IReadOnlyList<string> urls)
        {
            if (urls == null)
            {
                return DefaultIpLookupUrls;
            }

            var normalized = urls.Where(IsAllowedLookupUrl).Select(url => url.Trim()).ToList();
            return normalized.Count > 0 ? new ReadOnlyCollection<string>(normalized) : DefaultIpLookupUrls;
        }

        /// <summary>
        /// IP 確認サービスの URL として使えるか（絶対 URL かつ https）。
        /// <see cref="AppSettingsValidator"/> が個々の URL の妥当性を警告に出すためにも使う。
        /// </summary>
        public static bool IsAllowedLookupUrl(string url)
            => !string.IsNullOrWhiteSpace(url)
               && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)
               && parsed.Scheme == Uri.UriSchemeHttps;

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    }
}
