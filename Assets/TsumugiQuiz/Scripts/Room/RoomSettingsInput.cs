using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// プリセット JSON / 設定ファイルから読み込んだ、検証前の生のルーム設定値（docs/room-settings.md §1/§2）。
    /// すべて任意項目（null 許容）で、未指定のキーは <see cref="RoomSettingsValidator"/> が既定値を補う
    /// （docs §3: 「settings は変更したいものだけを含めばよい」）。JSON 側のキー名は
    /// <see cref="JsonPropertyAttribute"/> でドット記法のまま対応させる。
    /// </summary>
    public sealed class RoomSettingsInput
    {
        [JsonProperty("host.role")]
        public string HostRole { get; set; }

        [JsonProperty("room.maxPlayers")]
        public int? MaxPlayers { get; set; }

        [JsonProperty("questions.setIds")]
        public List<string> QuestionsSetIds { get; set; }

        [JsonProperty("questions.typeFilter")]
        public string QuestionsTypeFilter { get; set; }

        [JsonProperty("questions.imageOnly")]
        public bool? QuestionsImageOnly { get; set; }

        [JsonProperty("questions.tagFilter")]
        public List<string> QuestionsTagFilter { get; set; }

        [JsonProperty("questions.count")]
        public int? QuestionsCount { get; set; }

        [JsonProperty("questions.shuffleOrder")]
        public bool? QuestionsShuffleOrder { get; set; }

        [JsonProperty("choices.shuffleDisplay")]
        public bool? ChoicesShuffleDisplay { get; set; }

        [JsonProperty("buzz.timeLimitSec")]
        public double? BuzzTimeLimitSec { get; set; }

        [JsonProperty("buzz.allowDuringReading")]
        public bool? BuzzAllowDuringReading { get; set; }

        [JsonProperty("buzz.collectWindowMs")]
        public int? BuzzCollectWindowMs { get; set; }

        [JsonProperty("buzz.reopenAfterWrongAnswer")]
        public bool? BuzzReopenAfterWrongAnswer { get; set; }

        [JsonProperty("answer.freeTextTimeLimitSec")]
        public double? AnswerFreeTextTimeLimitSec { get; set; }

        [JsonProperty("answer.choiceTimeLimitSec")]
        public double? AnswerChoiceTimeLimitSec { get; set; }

        [JsonProperty("answer.singleAttemptOnly")]
        public bool? AnswerSingleAttemptOnly { get; set; }

        [JsonProperty("score.correctPoints")]
        public int? ScoreCorrectPoints { get; set; }

        [JsonProperty("score.incorrectPoints")]
        public int? ScoreIncorrectPoints { get; set; }

        [JsonProperty("score.penaltyType")]
        public string ScorePenaltyType { get; set; }

        [JsonProperty("score.penaltyMinusPoints")]
        public int? ScorePenaltyMinusPoints { get; set; }

        [JsonProperty("tts.enabled")]
        public bool? TtsEnabled { get; set; }

        [JsonProperty("tts.speed")]
        public double? TtsSpeed { get; set; }

        [JsonProperty("tts.readyTimeoutMs")]
        public int? TtsReadyTimeoutMs { get; set; }

        [JsonProperty("tts.leadTimeSec")]
        public double? TtsLeadTimeSec { get; set; }

        [JsonProperty("network.allowLateJoin")]
        public bool? NetworkAllowLateJoin { get; set; }

        [JsonProperty("result.autoAdvanceSec")]
        public double? ResultAutoAdvanceSec { get; set; }

        /// <summary><c>question.revealMsPerChar</c>（問題文の文字送り速度、issue #144）。</summary>
        [JsonProperty("question.revealMsPerChar")]
        public int? QuestionRevealMsPerChar { get; set; }

        /// <summary><c>display.showScores</c>（参加者パネルに全員の得点を表示するか、issue #194）。</summary>
        [JsonProperty("display.showScores")]
        public bool? DisplayShowScores { get; set; }

        /// <summary>
        /// <paramref name="obj"/> からキー単位で読み取って組み立てる。型が不正なキーはそのキーだけ
        /// 既定値扱いにして <paramref name="warnings"/> に警告を追加し、JSON 全体は失敗させない（M10）。
        /// スキーマに無い未知キーも <paramref name="warnings"/> に追加する（M9）。
        /// </summary>
        internal static RoomSettingsInput FromJson(JObject obj, List<string> warnings)
        {
            var reader = new JsonInputReader(obj, warnings);
            var input = new RoomSettingsInput
            {
                HostRole = reader.GetString("host.role"),
                MaxPlayers = reader.GetInt("room.maxPlayers"),
                QuestionsSetIds = reader.GetStringList("questions.setIds"),
                QuestionsTypeFilter = reader.GetString("questions.typeFilter"),
                QuestionsImageOnly = reader.GetBool("questions.imageOnly"),
                QuestionsTagFilter = reader.GetStringList("questions.tagFilter"),
                QuestionsCount = reader.GetInt("questions.count"),
                QuestionsShuffleOrder = reader.GetBool("questions.shuffleOrder"),
                ChoicesShuffleDisplay = reader.GetBool("choices.shuffleDisplay"),
                BuzzTimeLimitSec = reader.GetDouble("buzz.timeLimitSec"),
                BuzzAllowDuringReading = reader.GetBool("buzz.allowDuringReading"),
                BuzzCollectWindowMs = reader.GetInt("buzz.collectWindowMs"),
                BuzzReopenAfterWrongAnswer = reader.GetBool("buzz.reopenAfterWrongAnswer"),
                AnswerFreeTextTimeLimitSec = reader.GetDouble("answer.freeTextTimeLimitSec"),
                AnswerChoiceTimeLimitSec = reader.GetDouble("answer.choiceTimeLimitSec"),
                AnswerSingleAttemptOnly = reader.GetBool("answer.singleAttemptOnly"),
                ScoreCorrectPoints = reader.GetInt("score.correctPoints"),
                ScoreIncorrectPoints = reader.GetInt("score.incorrectPoints"),
                ScorePenaltyType = reader.GetString("score.penaltyType"),
                ScorePenaltyMinusPoints = reader.GetInt("score.penaltyMinusPoints"),
                TtsEnabled = reader.GetBool("tts.enabled"),
                TtsSpeed = reader.GetDouble("tts.speed"),
                TtsReadyTimeoutMs = reader.GetInt("tts.readyTimeoutMs"),
                TtsLeadTimeSec = reader.GetDouble("tts.leadTimeSec"),
                NetworkAllowLateJoin = reader.GetBool("network.allowLateJoin"),
                ResultAutoAdvanceSec = reader.GetDouble("result.autoAdvanceSec"),
                QuestionRevealMsPerChar = reader.GetInt("question.revealMsPerChar"),
                DisplayShowScores = reader.GetBool("display.showScores"),
            };

            foreach (var unknownKey in reader.GetUnknownKeys())
            {
                warnings.Add($"未知のキー \"{unknownKey}\" は無視しました。");
            }

            return input;
        }
    }
}
