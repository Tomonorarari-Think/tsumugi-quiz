using TsumugiQuiz.Core;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// ルーム設定のうち得点・お手つきペナルティ・誤答後の受付再開放に関する部分
    /// （docs/room-settings.md §1「得点」「早押し詳細」、仮決め K19）。
    /// 不変オブジェクトとして扱い、変更は新しいインスタンスの生成で表す。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本 issue（#18）はフル <c>RoomSettings</c>（#26）より先に得点計算を実装するため、
    /// 必要な 6 項目だけを持つ小さな不変レコードとして切り出している。
    /// #26 で <c>RoomSettings</c> を作るときは、本クラスをその一部として内包し、
    /// プリセット JSON からの読み込み・範囲外値のクランプ（docs/room-settings.md §5）を追加する。
    /// </para>
    /// <para>
    /// <see cref="ToQuizRules"/> で <c>TsumugiQuiz.Core</c> の <see cref="QuizRules"/> に変換し、
    /// <c>QuizStateMachine</c> / <c>GameSession.Configure</c> に渡す。
    /// 範囲検証は <see cref="ScoreRules"/> のコンストラクタに委ねる（範囲外は例外）。
    /// </para>
    /// </remarks>
    public sealed class ScoringSettings
    {
        /// <summary><c>score.correctPoints</c> の既定値。</summary>
        public const int DefaultCorrectPoints = ScoreRules.DefaultCorrectPoints;

        /// <summary><c>score.incorrectPoints</c> の既定値。</summary>
        public const int DefaultIncorrectPoints = ScoreRules.DefaultWrongPoints;

        /// <summary><c>score.penaltyType</c> の既定値。</summary>
        public const PenaltyKind DefaultPenaltyType = ScoreRules.DefaultPenaltyKind;

        /// <summary><c>score.penaltyMinusPoints</c> の既定値。</summary>
        public const int DefaultPenaltyMinusPoints = ScoreRules.DefaultPenaltyPoints;

        /// <summary><c>buzz.reopenAfterWrongAnswer</c> の既定値。</summary>
        public const bool DefaultReopenAfterWrongAnswer = QuizRules.DefaultReopenAfterWrongAnswer;

        /// <summary><c>answer.singleAttemptOnly</c> の既定値。</summary>
        public const bool DefaultSingleAttemptOnly = QuizRules.DefaultSingleAttemptOnly;

        private static readonly ScoringSettings DefaultInstance = new ScoringSettings();

        private readonly ScoreRules _score;

        /// <summary>
        /// 設定値を指定して生成する。
        /// </summary>
        /// <param name="correctPoints"><c>score.correctPoints</c>（0〜100）。</param>
        /// <param name="incorrectPoints"><c>score.incorrectPoints</c>（-100〜100）。</param>
        /// <param name="penaltyType"><c>score.penaltyType</c>。</param>
        /// <param name="penaltyMinusPoints"><c>score.penaltyMinusPoints</c>（-100〜0）。</param>
        /// <param name="reopenAfterWrongAnswer"><c>buzz.reopenAfterWrongAnswer</c>。</param>
        /// <param name="singleAttemptOnly"><c>answer.singleAttemptOnly</c>。</param>
        /// <exception cref="System.ArgumentOutOfRangeException">いずれかの値が範囲外のとき。</exception>
        public ScoringSettings(
            int correctPoints = DefaultCorrectPoints,
            int incorrectPoints = DefaultIncorrectPoints,
            PenaltyKind penaltyType = DefaultPenaltyType,
            int penaltyMinusPoints = DefaultPenaltyMinusPoints,
            bool reopenAfterWrongAnswer = DefaultReopenAfterWrongAnswer,
            bool singleAttemptOnly = DefaultSingleAttemptOnly)
        {
            _score = new ScoreRules(correctPoints, incorrectPoints, penaltyType, penaltyMinusPoints);
            ReopenAfterWrongAnswer = reopenAfterWrongAnswer;
            SingleAttemptOnly = singleAttemptOnly;
        }

        /// <summary>docs/room-settings.md の既定値（組み込みプリセット「標準」に相当）。</summary>
        public static ScoringSettings Default => DefaultInstance;

        /// <summary>正解時の加点。</summary>
        public int CorrectPoints => _score.CorrectPoints;

        /// <summary>誤答時の得点変化。</summary>
        public int IncorrectPoints => _score.WrongPoints;

        /// <summary>お手つきペナルティ種別。</summary>
        public PenaltyKind PenaltyType => _score.PenaltyKind;

        /// <summary>減点ペナルティの幅。</summary>
        public int PenaltyMinusPoints => _score.PenaltyPoints;

        /// <summary>誤答・お手つき後に残り時間で受付を再開放するか。</summary>
        public bool ReopenAfterWrongAnswer { get; }

        /// <summary>早押し後の回答入力を 1 回に限るか。</summary>
        public bool SingleAttemptOnly { get; }

        /// <summary>得点規則（<c>TsumugiQuiz.Core</c>）に変換する。</summary>
        /// <returns>得点規則。</returns>
        public ScoreRules ToScoreRules() => _score;

        /// <summary>進行規則（<c>TsumugiQuiz.Core</c>）に変換する。</summary>
        /// <returns>進行規則。</returns>
        public QuizRules ToQuizRules() => new QuizRules(_score, ReopenAfterWrongAnswer, SingleAttemptOnly);

        /// <summary>ペナルティ設定だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="penaltyType">ペナルティ種別。</param>
        /// <param name="penaltyMinusPoints">減点幅（-100〜0）。</param>
        /// <returns>新しいインスタンス。</returns>
        public ScoringSettings WithPenalty(PenaltyKind penaltyType, int penaltyMinusPoints) =>
            new ScoringSettings(
                CorrectPoints, IncorrectPoints, penaltyType, penaltyMinusPoints, ReopenAfterWrongAnswer, SingleAttemptOnly);

        /// <summary>得点の加減だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="correctPoints">正解時の加点（0〜100）。</param>
        /// <param name="incorrectPoints">誤答時の得点変化（-100〜100）。</param>
        /// <returns>新しいインスタンス。</returns>
        public ScoringSettings WithPoints(int correctPoints, int incorrectPoints) =>
            new ScoringSettings(
                correctPoints, incorrectPoints, PenaltyType, PenaltyMinusPoints, ReopenAfterWrongAnswer, SingleAttemptOnly);

        /// <summary>誤答後の再開放の可否だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="reopenAfterWrongAnswer">再開放するか。</param>
        /// <returns>新しいインスタンス。</returns>
        public ScoringSettings WithReopenAfterWrongAnswer(bool reopenAfterWrongAnswer) =>
            new ScoringSettings(
                CorrectPoints, IncorrectPoints, PenaltyType, PenaltyMinusPoints, reopenAfterWrongAnswer, SingleAttemptOnly);

        /// <summary>回答の再入力可否だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="singleAttemptOnly">回答入力を 1 回に限るか。</param>
        /// <returns>新しいインスタンス。</returns>
        public ScoringSettings WithSingleAttemptOnly(bool singleAttemptOnly) =>
            new ScoringSettings(
                CorrectPoints, IncorrectPoints, PenaltyType, PenaltyMinusPoints, ReopenAfterWrongAnswer, singleAttemptOnly);
    }
}
