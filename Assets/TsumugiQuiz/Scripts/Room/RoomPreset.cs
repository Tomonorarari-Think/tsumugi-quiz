using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// ルーム設定のプリセット（docs/room-settings.md §3）。<c>%USERPROFILE%\Documents\TsumugiQuiz\Presets\&lt;name&gt;.json</c>
    /// に保存する（読み書きは <see cref="RoomPresetStore"/> / <see cref="RoomPresetJson"/>）。
    /// </summary>
    public sealed class RoomPreset
    {
        /// <summary>現在のプリセット JSON スキーマバージョン。</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>組み込みプリセット「標準」の名前。</summary>
        public const string StandardName = "標準";

        /// <summary>組み込みプリセット「早押し重視」の名前。</summary>
        public const string BuzzFocusedName = "早押し重視";

        /// <summary>組み込みプリセット「のんびり」の名前。</summary>
        public const string RelaxedName = "のんびり";

        /// <summary>
        /// プリセットを生成する。
        /// </summary>
        /// <param name="name">プリセット名。空白不可（ファイル名としても使う）。</param>
        /// <param name="settings">設定値。</param>
        /// <param name="schemaVersion">スキーマバージョン。既定は <see cref="CurrentSchemaVersion"/>。</param>
        /// <exception cref="ArgumentException"><paramref name="name"/> が空白のとき。</exception>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> が null のとき。</exception>
        public RoomPreset(string name, RoomSettings settings, int schemaVersion = CurrentSchemaVersion)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("プリセット名を指定してください。", nameof(name));
            }

            Name = name;
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            SchemaVersion = schemaVersion;
        }

        /// <summary>プリセット名。</summary>
        public string Name { get; }

        /// <summary>設定値。</summary>
        public RoomSettings Settings { get; }

        /// <summary>プリセット JSON のスキーマバージョン。</summary>
        public int SchemaVersion { get; }

        /// <summary>組み込みプリセット「標準」（docs §3: 本書の既定値そのまま）。</summary>
        public static RoomPreset Standard { get; } = new RoomPreset(StandardName, RoomSettings.Default);

        /// <summary>
        /// 組み込みプリセット「早押し重視」（docs §3: 読み上げ完了後のみ受付、短い制限時間でテンポよく）。
        /// </summary>
        public static RoomPreset BuzzFocused { get; } = new RoomPreset(BuzzFocusedName, BuildBuzzFocusedSettings());

        /// <summary>組み込みプリセット「のんびり」（docs §3: 長い制限時間、誤答ペナルティなし）。</summary>
        public static RoomPreset Relaxed { get; } = new RoomPreset(RelaxedName, BuildRelaxedSettings());

        /// <summary>組み込み 3 プリセット（標準・早押し重視・のんびり）。</summary>
        public static IReadOnlyList<RoomPreset> BuiltIns { get; } = new[] { Standard, BuzzFocused, Relaxed };

        private static RoomSettings BuildBuzzFocusedSettings()
        {
            var timeLimits = new QuizTimeLimits(
                buzzTimeLimitSec: 6.0,
                answerTimeLimitSec: 8.0,
                choiceTimeLimitSec: 10.0,
                collectWindowSec: QuizTimeLimits.DefaultCollectWindowSec);

            var scoring = new ScoringSettings(
                correctPoints: ScoreRules.DefaultCorrectPoints,
                incorrectPoints: ScoreRules.DefaultWrongPoints,
                penaltyType: PenaltyKind.MinusPoints,
                penaltyMinusPoints: -5,
                reopenAfterWrongAnswer: QuizRules.DefaultReopenAfterWrongAnswer,
                singleAttemptOnly: QuizRules.DefaultSingleAttemptOnly);

            var session = new SessionSettings(
                questions: QuestionSelectionSettings.Default,
                timeLimits: timeLimits,
                scoring: scoring,
                resultAutoAdvanceSec: 3.0,
                allowLateJoin: SessionSettings.DefaultAllowLateJoin);

            return new RoomSettings(
                hostRole: RoomSettings.DefaultHostRole,
                maxPlayers: RoomSettings.DefaultMaxPlayers,
                session: session,
                shuffleChoiceDisplay: RoomSettings.DefaultShuffleChoiceDisplay,
                allowDuringReading: false,
                ttsEnabled: RoomSettings.DefaultTtsEnabled,
                ttsSpeed: RoomSettings.DefaultTtsSpeed,
                ttsReadyTimeoutMs: RoomSettings.DefaultTtsReadyTimeoutMs,
                ttsLeadTimeSec: RoomSettings.DefaultTtsLeadTimeSec);
        }

        private static RoomSettings BuildRelaxedSettings()
        {
            var timeLimits = new QuizTimeLimits(
                buzzTimeLimitSec: 20.0,
                answerTimeLimitSec: 30.0,
                choiceTimeLimitSec: 40.0,
                collectWindowSec: QuizTimeLimits.DefaultCollectWindowSec);

            var scoring = new ScoringSettings(
                correctPoints: ScoreRules.DefaultCorrectPoints,
                incorrectPoints: ScoreRules.DefaultWrongPoints,
                penaltyType: PenaltyKind.None,
                penaltyMinusPoints: ScoreRules.DefaultPenaltyPoints,
                reopenAfterWrongAnswer: true,
                singleAttemptOnly: QuizRules.DefaultSingleAttemptOnly);

            var session = new SessionSettings(
                questions: QuestionSelectionSettings.Default,
                timeLimits: timeLimits,
                scoring: scoring,
                resultAutoAdvanceSec: 8.0,
                allowLateJoin: SessionSettings.DefaultAllowLateJoin);

            return new RoomSettings(
                hostRole: RoomSettings.DefaultHostRole,
                maxPlayers: RoomSettings.DefaultMaxPlayers,
                session: session,
                shuffleChoiceDisplay: RoomSettings.DefaultShuffleChoiceDisplay,
                ttsEnabled: RoomSettings.DefaultTtsEnabled,
                ttsSpeed: RoomSettings.DefaultTtsSpeed,
                ttsReadyTimeoutMs: RoomSettings.DefaultTtsReadyTimeoutMs,
                ttsLeadTimeSec: RoomSettings.DefaultTtsLeadTimeSec);
        }
    }
}
