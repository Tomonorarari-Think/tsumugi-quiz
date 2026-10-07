using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// <see cref="RoomPreset"/> と docs/room-settings.md §3 のプリセット JSON
    /// （<c>{ schemaVersion, name, settings }</c>）の相互変換。
    /// </summary>
    /// <remarks>
    /// <c>TsumugiQuiz.Room</c> は <c>Newtonsoft.Json</c>（<c>com.unity.nuget.newtonsoft-json</c>）を
    /// 素の Unity プラグイン DLL として参照できる（<c>TsumugiQuiz.Questions</c> と同様、asmdef の
    /// <c>references</c> への追加は不要。プリコンパイル済み DLL は既定で全アセンブリから参照可能なため）。
    /// </remarks>
    public static class RoomPresetJson
    {
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        /// <summary>
        /// プリセットを JSON 文字列にシリアライズする（保存時は全項目を明示的に書き出す）。
        /// <c>schemaVersion</c> は <paramref name="preset"/> の値に関わらず、必ず
        /// <see cref="RoomPreset.CurrentSchemaVersion"/> を書き出す（#26 統括判断 H5）。
        /// </summary>
        public static string Serialize(RoomPreset preset)
        {
            var dto = new PresetFileDto
            {
                SchemaVersion = RoomPreset.CurrentSchemaVersion,
                Name = preset.Name,
                Settings = ToInput(preset.Settings),
            };

            return JsonConvert.SerializeObject(dto, Formatting.Indented, SerializerSettings);
        }

        /// <summary>
        /// JSON 文字列からプリセットを読み込む。JSON が空・構文エラーの場合は例外を投げず、
        /// 既定値ベースのプリセット（名前は <paramref name="fallbackName"/>）と警告を返す。
        /// <c>schemaVersion</c> が <see cref="RoomPreset.CurrentSchemaVersion"/> 以外（欠落を含む）の場合は
        /// 移行手段が無いため <c>settings</c> の中身を信用せず、既定値へフォールバックする（#26 統括判断 H5）。
        /// </summary>
        /// <param name="json">プリセット JSON 文字列。</param>
        /// <param name="fallbackName">JSON 内に有効な名前が無い場合に使う名前。</param>
        public static RoomPresetParseResult Parse(string json, string fallbackName)
        {
            JObject obj;
            try
            {
                var token = string.IsNullOrWhiteSpace(json) ? null : JToken.Parse(json);
                if (token == null || token.Type != JTokenType.Object)
                {
                    return BuildFallback(fallbackName, "プリセット JSON の内容が空、またはオブジェクトではありません。既定値を使用しました。");
                }

                obj = (JObject)token;
            }
            catch (JsonException ex)
            {
                return BuildFallback(fallbackName, $"プリセット JSON の解析に失敗しました。既定値を使用しました: {ex.GetType().Name}: {ex.Message}");
            }

            var warnings = new List<string>();
            var name = obj.TryGetValue("name", out var nameToken) && nameToken.Type == JTokenType.String
                ? nameToken.Value<string>()
                : null;
            name = string.IsNullOrWhiteSpace(name) ? fallbackName : name;

            var schemaVersion = obj.TryGetValue("schemaVersion", out var schemaToken) && schemaToken.Type == JTokenType.Integer
                ? schemaToken.Value<int>()
                : 0;

            if (schemaVersion != RoomPreset.CurrentSchemaVersion)
            {
                warnings.Add(
                    $"schemaVersion が {schemaVersion}（期待値 {RoomPreset.CurrentSchemaVersion}）だったため、" +
                    "settings の内容は信用せず既定値を使用しました。");
                var fallbackPreset = new RoomPreset(
                    string.IsNullOrWhiteSpace(name) ? RoomPreset.StandardName : name, RoomSettings.Default);
                return RoomPresetParseResult.Success(fallbackPreset, warnings.AsReadOnly());
            }

            var settingsObj = obj.TryGetValue("settings", out var settingsToken) && settingsToken.Type == JTokenType.Object
                ? (JObject)settingsToken
                : new JObject();

            var input = RoomSettingsInput.FromJson(settingsObj, warnings);
            var validation = RoomSettingsValidator.Validate(input);
            warnings.AddRange(validation.Warnings);

            var preset = new RoomPreset(
                string.IsNullOrWhiteSpace(name) ? RoomPreset.StandardName : name, validation.Settings, schemaVersion);

            return RoomPresetParseResult.Success(preset, warnings.AsReadOnly());
        }

        private static RoomPresetParseResult BuildFallback(string fallbackName, string warning)
        {
            var name = string.IsNullOrWhiteSpace(fallbackName) ? RoomPreset.StandardName : fallbackName;
            var preset = new RoomPreset(name, RoomSettings.Default);
            return RoomPresetParseResult.Success(preset, new[] { warning });
        }

        private static RoomSettingsInput ToInput(RoomSettings settings)
        {
            return new RoomSettingsInput
            {
                HostRole = HostRoles.ToKey(settings.HostRole),
                MaxPlayers = settings.MaxPlayers,
                QuestionsSetIds = new List<string>(settings.Questions.SetIds),
                QuestionsTypeFilter = ToTypeFilterKey(settings.Questions.TypeFilter),
                QuestionsImageOnly = settings.Questions.ImageOnly,
                QuestionsTagFilter = new List<string>(settings.Questions.TagFilter),
                QuestionsCount = settings.Questions.Count,
                QuestionsShuffleOrder = settings.Questions.ShuffleOrder,
                ChoicesShuffleDisplay = settings.ShuffleChoiceDisplay,
                BuzzTimeLimitSec = settings.TimeLimits.BuzzTimeLimitSec,
                BuzzAllowDuringReading = settings.AllowDuringReading,
                BuzzCollectWindowMs = (int)Math.Round(settings.TimeLimits.CollectWindowSec * 1000.0),
                BuzzReopenAfterWrongAnswer = settings.Scoring.ReopenAfterWrongAnswer,
                AnswerFreeTextTimeLimitSec = settings.TimeLimits.AnswerTimeLimitSec,
                AnswerChoiceTimeLimitSec = settings.ChoiceTimeLimitSec,
                AnswerSingleAttemptOnly = settings.Scoring.SingleAttemptOnly,
                ScoreCorrectPoints = settings.Scoring.CorrectPoints,
                ScoreIncorrectPoints = settings.Scoring.IncorrectPoints,
                ScorePenaltyType = ToPenaltyTypeKey(settings.Scoring.PenaltyType),
                ScorePenaltyMinusPoints = settings.Scoring.PenaltyMinusPoints,
                TtsEnabled = settings.TtsEnabled,
                TtsSpeed = settings.TtsSpeed,
                TtsReadyTimeoutMs = settings.TtsReadyTimeoutMs,
                TtsLeadTimeSec = settings.TtsLeadTimeSec,
                NetworkAllowLateJoin = settings.AllowLateJoin,
                ResultAutoAdvanceSec = settings.ResultAutoAdvanceSec,
                QuestionRevealMsPerChar = settings.QuestionRevealMsPerChar,
                DisplayShowScores = settings.ShowScores,
            };
        }

        private static string ToTypeFilterKey(QuestionTypeFilter typeFilter) => typeFilter switch
        {
            QuestionTypeFilter.FreeText => "freeText",
            QuestionTypeFilter.Choice => "choice",
            _ => "both",
        };

        private static string ToPenaltyTypeKey(PenaltyKind penaltyKind) => penaltyKind switch
        {
            PenaltyKind.MinusPoints => "minusPoints",
            PenaltyKind.None => "none",
            _ => "skipNext",
        };

        private sealed class PresetFileDto
        {
            [JsonProperty("schemaVersion")]
            public int SchemaVersion { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("settings")]
            public RoomSettingsInput Settings { get; set; }
        }
    }
}
