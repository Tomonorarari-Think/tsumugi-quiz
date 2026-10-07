using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// <c>app-settings.json</c> から読み込んだ、検証前の生のアプリ設定値（docs/room-settings.md §0/§2）。
    /// すべて任意項目（null 許容）で、未指定のキーは <see cref="AppSettings.Create"/> が既定値を補う。
    /// </summary>
    public sealed class AppSettingsInput
    {
        [JsonProperty("player.name")]
        public string PlayerName { get; set; }

        [JsonProperty("network.port")]
        public int? NetworkPort { get; set; }

        /// <summary>
        /// <c>network.tickRate</c>。設定項目からは外したが（統括判断、PR #92 Phase 2）、
        /// 既存の <c>app-settings.json</c> に残っていることがあるため「既知だが未対応のキー」として読み、
        /// <see cref="AppSettingsValidator"/> が警告付きで無視する（未知のキー扱いにしない）。
        /// </summary>
        [JsonProperty("network.tickRate", NullValueHandling = NullValueHandling.Ignore)]
        public int? NetworkTickRate { get; set; }

        [JsonProperty("network.ipLookupUrls")]
        public List<string> NetworkIpLookupUrls { get; set; }

        [JsonProperty("upnp.enabled")]
        public bool? UpnpEnabled { get; set; }

        [JsonProperty("upnp.discoveryTimeoutMs")]
        public int? UpnpDiscoveryTimeoutMs { get; set; }

        [JsonProperty("upnp.mappingLifetimeSec")]
        public int? UpnpMappingLifetimeSec { get; set; }

        [JsonProperty("upnp.renewIntervalMs")]
        public int? UpnpRenewIntervalMs { get; set; }

        [JsonProperty("question.prefetchCount")]
        public int? QuestionPrefetchCount { get; set; }

        [JsonProperty("tts.speakerName")]
        public string TtsSpeakerName { get; set; }

        [JsonProperty("tts.styleName")]
        public string TtsStyleName { get; set; }

        [JsonProperty("tts.cacheMaxBytes")]
        public long? TtsCacheMaxBytes { get; set; }

        [JsonProperty("tts.cacheMaxEntries")]
        public int? TtsCacheMaxEntries { get; set; }

        [JsonProperty("tts.assetPathOverride")]
        public string TtsAssetPathOverride { get; set; }

        [JsonProperty("character.enabled")]
        public bool? CharacterEnabled { get; set; }

        /// <summary>
        /// 直近に Settings View（#28）で編集・適用した <c>RoomSettings</c> のスナップショット。
        /// <see cref="RoomPresetJson.Serialize"/> と同じ形式の JSON 文字列（プリセット名は使わない）。
        /// 空・欠落なら「編集中の設定が無い」とみなし、既定値（docs §3「標準」）から始める
        /// （<see cref="RoomSettingsDraft"/>、PR #92 レビュー H4）。
        /// </summary>
        [JsonProperty("room.lastApplied")]
        public string RoomLastApplied { get; set; }

        /// <summary>
        /// <c>host.role</c>（司会専任トグル、issue #155）。<c>"player"</c> / <c>"moderator"</c> の
        /// いずれとも一致しない値は <see cref="AppSettingsValidator"/> が警告付きで既定値に戻す。
        /// </summary>
        [JsonProperty("host.role")]
        public string HostRole { get; set; }

        /// <summary>
        /// <paramref name="obj"/> からキー単位で読み取って組み立てる。型が不正なキーはそのキーだけ
        /// 既定値扱いにして <paramref name="warnings"/> に警告を追加する（M10）。
        /// スキーマに無い未知キーも <paramref name="warnings"/> に追加する（M9）。
        /// </summary>
        internal static AppSettingsInput FromJson(JObject obj, List<string> warnings)
        {
            var reader = new JsonInputReader(obj, warnings);
            var input = new AppSettingsInput
            {
                PlayerName = reader.GetString("player.name"),
                NetworkPort = reader.GetInt("network.port"),
                NetworkTickRate = reader.GetInt("network.tickRate"),
                NetworkIpLookupUrls = reader.GetStringList("network.ipLookupUrls"),
                UpnpEnabled = reader.GetBool("upnp.enabled"),
                UpnpDiscoveryTimeoutMs = reader.GetInt("upnp.discoveryTimeoutMs"),
                UpnpMappingLifetimeSec = reader.GetInt("upnp.mappingLifetimeSec"),
                UpnpRenewIntervalMs = reader.GetInt("upnp.renewIntervalMs"),
                QuestionPrefetchCount = reader.GetInt("question.prefetchCount"),
                TtsSpeakerName = reader.GetString("tts.speakerName"),
                TtsStyleName = reader.GetString("tts.styleName"),
                TtsCacheMaxBytes = reader.GetLong("tts.cacheMaxBytes"),
                TtsCacheMaxEntries = reader.GetInt("tts.cacheMaxEntries"),
                TtsAssetPathOverride = reader.GetString("tts.assetPathOverride"),
                CharacterEnabled = reader.GetBool("character.enabled"),
                RoomLastApplied = reader.GetString("room.lastApplied"),
                HostRole = reader.GetString("host.role"),
            };

            foreach (var unknownKey in reader.GetUnknownKeys())
            {
                warnings.Add($"未知のキー \"{unknownKey}\" は無視しました。");
            }

            return input;
        }
    }
}
