using System.Collections.Generic;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// <see cref="RoomSettingsInput"/>（プリセット JSON / 設定ファイル由来の生の値）を検証し、
    /// 範囲外の値を既定値へクランプした <see cref="RoomSettings"/> を組み立てる（docs/room-settings.md §5）。
    /// </summary>
    /// <remarks>
    /// <c>questions.setIds</c> に存在しない <c>setId</c> が含まれる場合の無視・警告と、
    /// <c>questions.count</c> がフィルタ後の候補数を超える場合の丸めは、実行時の問題データが必要なため
    /// ここでは行わない（<c>TsumugiQuiz.Network.QuestionSelector</c> が出題選択時に処理する）。
    /// </remarks>
    public static class RoomSettingsValidator
    {
        /// <summary>
        /// <c>buzz.collectWindowMs</c> の下限（ミリ秒、docs/network.md §6.3）。
        /// <see cref="QuizTimeLimits.MinCollectWindowMs"/> をそのまま使い、既定値の出典を 1 か所にする
        /// （#26 統括判断 M7/M8/L2。ミリ秒が JSON/UI 側の単位のため、ミリ秒の定数を一次情報にする）。
        /// </summary>
        public const int MinCollectWindowMs = QuizTimeLimits.MinCollectWindowMs;

        /// <summary><c>buzz.collectWindowMs</c> の上限（ミリ秒、docs/network.md §6.3）。</summary>
        public const int MaxCollectWindowMs = QuizTimeLimits.MaxCollectWindowMs;

        /// <summary>
        /// <c>buzz.collectWindowMs</c> の既定値（ミリ秒）。<see cref="QuizTimeLimits.DefaultCollectWindowMs"/>
        /// をそのまま使い、既定値の重複を避ける（#26 統括判断 M8）。
        /// </summary>
        public const int DefaultCollectWindowMs = QuizTimeLimits.DefaultCollectWindowMs;

        /// <summary>
        /// 生の入力値を検証・クランプし、<see cref="RoomSettings"/> と警告一覧を返す。
        /// null が渡された場合はすべて既定値として扱う。
        /// </summary>
        public static RoomSettingsValidationResult Validate(RoomSettingsInput input)
        {
            input ??= new RoomSettingsInput();
            var warnings = new List<string>();

            var hostRole = ParseHostRole(input.HostRole, warnings);
            var maxPlayers = ClampInt(
                input.MaxPlayers, RoomSettings.DefaultMaxPlayers,
                RoomSettings.MinMaxPlayers, RoomSettings.MaxMaxPlayers, "room.maxPlayers", warnings);

            var typeFilter = ParseTypeFilter(input.QuestionsTypeFilter, warnings);
            var count = input.QuestionsCount ?? QuestionSelectionSettings.DefaultCount;
            if (count < 0)
            {
                warnings.Add($"questions.count は 0 以上である必要があります。既定値 {QuestionSelectionSettings.DefaultCount} を使用します。");
                count = QuestionSelectionSettings.DefaultCount;
            }

            var questions = new QuestionSelectionSettings(
                input.QuestionsSetIds,
                typeFilter,
                input.QuestionsImageOnly ?? QuestionSelectionSettings.DefaultImageOnly,
                input.QuestionsTagFilter,
                count,
                input.QuestionsShuffleOrder ?? QuestionSelectionSettings.DefaultShuffleOrder);

            var buzzTimeLimitSec = ClampDouble(
                input.BuzzTimeLimitSec, QuizTimeLimits.DefaultBuzzTimeLimitSec,
                QuizTimeLimits.MinBuzzOrAnswerTimeLimitSec, QuizTimeLimits.MaxBuzzOrAnswerTimeLimitSec,
                "buzz.timeLimitSec", warnings);
            var answerTimeLimitSec = ClampDouble(
                input.AnswerFreeTextTimeLimitSec, QuizTimeLimits.DefaultAnswerTimeLimitSec,
                QuizTimeLimits.MinBuzzOrAnswerTimeLimitSec, QuizTimeLimits.MaxBuzzOrAnswerTimeLimitSec,
                "answer.freeTextTimeLimitSec", warnings);
            var choiceTimeLimitSec = ClampDouble(
                input.AnswerChoiceTimeLimitSec, RoomSettings.DefaultChoiceTimeLimitSec,
                RoomSettings.MinChoiceTimeLimitSec, RoomSettings.MaxChoiceTimeLimitSec, "answer.choiceTimeLimitSec", warnings);
            var collectWindowMs = ClampInt(
                input.BuzzCollectWindowMs, DefaultCollectWindowMs, MinCollectWindowMs, MaxCollectWindowMs,
                "buzz.collectWindowMs", warnings);
            var timeLimits = new QuizTimeLimits(
                buzzTimeLimitSec, answerTimeLimitSec, choiceTimeLimitSec, collectWindowMs / 1000.0);

            var correctPoints = ClampInt(
                input.ScoreCorrectPoints, ScoreRules.DefaultCorrectPoints,
                ScoreRules.MinCorrectPoints, ScoreRules.MaxCorrectPoints, "score.correctPoints", warnings);
            var incorrectPoints = ClampInt(
                input.ScoreIncorrectPoints, ScoreRules.DefaultWrongPoints,
                ScoreRules.MinWrongPoints, ScoreRules.MaxWrongPoints, "score.incorrectPoints", warnings);
            var penaltyKind = ParsePenaltyKind(input.ScorePenaltyType, warnings);
            var penaltyMinusPoints = ClampInt(
                input.ScorePenaltyMinusPoints, ScoreRules.DefaultPenaltyPoints,
                ScoreRules.MinPenaltyPoints, ScoreRules.MaxPenaltyPoints, "score.penaltyMinusPoints", warnings);
            var reopenAfterWrongAnswer = input.BuzzReopenAfterWrongAnswer ?? QuizRules.DefaultReopenAfterWrongAnswer;
            var singleAttemptOnly = input.AnswerSingleAttemptOnly ?? QuizRules.DefaultSingleAttemptOnly;

            var scoring = new ScoringSettings(
                correctPoints, incorrectPoints, penaltyKind, penaltyMinusPoints,
                reopenAfterWrongAnswer, singleAttemptOnly);

            var resultAutoAdvanceSec = ClampDouble(
                input.ResultAutoAdvanceSec, SessionSettings.DefaultResultAutoAdvanceSec,
                SessionSettings.ManualAdvance, SessionSettings.MaxResultAutoAdvanceSec, "result.autoAdvanceSec", warnings);
            var allowLateJoin = input.NetworkAllowLateJoin ?? SessionSettings.DefaultAllowLateJoin;

            var session = new SessionSettings(questions, timeLimits, scoring, resultAutoAdvanceSec, allowLateJoin);

            var shuffleChoiceDisplay = input.ChoicesShuffleDisplay ?? RoomSettings.DefaultShuffleChoiceDisplay;
            var allowDuringReading = input.BuzzAllowDuringReading ?? RoomSettings.DefaultAllowDuringReading;

            var ttsEnabled = input.TtsEnabled ?? RoomSettings.DefaultTtsEnabled;
            var ttsSpeed = ClampDouble(
                input.TtsSpeed, RoomSettings.DefaultTtsSpeed,
                RoomSettings.MinTtsSpeed, RoomSettings.MaxTtsSpeed, "tts.speed", warnings);
            var ttsReadyTimeoutMs = ClampInt(
                input.TtsReadyTimeoutMs, RoomSettings.DefaultTtsReadyTimeoutMs,
                RoomSettings.MinTtsReadyTimeoutMs, RoomSettings.MaxTtsReadyTimeoutMs, "tts.readyTimeoutMs", warnings);
            var ttsLeadTimeSec = ClampDouble(
                input.TtsLeadTimeSec, RoomSettings.DefaultTtsLeadTimeSec,
                RoomSettings.MinTtsLeadTimeSec, RoomSettings.MaxTtsLeadTimeSec, "tts.leadTimeSec", warnings);
            var questionRevealMsPerChar = ClampInt(
                input.QuestionRevealMsPerChar, RoomSettings.DefaultQuestionRevealMsPerChar,
                RoomSettings.MinQuestionRevealMsPerChar, RoomSettings.MaxQuestionRevealMsPerChar,
                "question.revealMsPerChar", warnings);
            var showScores = input.DisplayShowScores ?? RoomSettings.DefaultShowScores;

            var settings = new RoomSettings(
                hostRole, maxPlayers, session, shuffleChoiceDisplay, allowDuringReading,
                ttsEnabled, ttsSpeed, ttsReadyTimeoutMs, ttsLeadTimeSec, questionRevealMsPerChar, showScores);

            return new RoomSettingsValidationResult(settings, warnings.AsReadOnly());
        }

        private static HostRole ParseHostRole(string value, List<string> warnings)
        {
            if (value == null)
            {
                return RoomSettings.DefaultHostRole;
            }

            if (HostRoles.TryParse(value, out var role))
            {
                return role;
            }

            warnings.Add($"host.role の値 \"{value}\" は不明です。既定値 \"{HostRoles.PlayerKey}\" を使用します。");
            return RoomSettings.DefaultHostRole;
        }

        private static QuestionTypeFilter ParseTypeFilter(string value, List<string> warnings)
        {
            if (value == null)
            {
                return QuestionSelectionSettings.DefaultTypeFilter;
            }

            if (QuestionTypeFilters.TryParse(value, out var filter))
            {
                return filter;
            }

            warnings.Add(
                $"questions.typeFilter の値 \"{value}\" は不明です。既定値 \"{QuestionTypeFilters.BothKey}\" を使用します。");
            return QuestionSelectionSettings.DefaultTypeFilter;
        }

        private static PenaltyKind ParsePenaltyKind(string value, List<string> warnings)
        {
            if (value == null)
            {
                return ScoreRules.DefaultPenaltyKind;
            }

            if (PenaltyKinds.TryParse(value, out var kind))
            {
                return kind;
            }

            warnings.Add(
                $"score.penaltyType の値 \"{value}\" は不明です。既定値 \"{PenaltyKinds.SkipNextKey}\" を使用します。");
            return ScoreRules.DefaultPenaltyKind;
        }

        private static int ClampInt(int? rawValue, int defaultValue, int min, int max, string keyName, List<string> warnings)
        {
            if (rawValue == null)
            {
                return defaultValue;
            }

            var value = rawValue.Value;
            if (value < min || value > max)
            {
                var clamped = value < min ? min : max;
                warnings.Add($"{keyName} は {min}〜{max} の範囲外（{value}）だったため {clamped} にクランプしました。");
                return clamped;
            }

            return value;
        }

        private static double ClampDouble(
            double? rawValue, double defaultValue, double min, double max, string keyName, List<string> warnings)
        {
            if (rawValue == null)
            {
                return defaultValue;
            }

            var value = rawValue.Value;
            if (!double.IsFinite(value))
            {
                warnings.Add($"{keyName} が有限の数値ではありません（{value}）。既定値 {defaultValue} を使用します。");
                return defaultValue;
            }

            if (value < min || value > max)
            {
                var clamped = value < min ? min : max;
                warnings.Add($"{keyName} は {min}〜{max} の範囲外（{value}）だったため {clamped} にクランプしました。");
                return clamped;
            }

            return value;
        }
    }
}
