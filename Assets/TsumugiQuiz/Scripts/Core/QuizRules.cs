namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 1 問の進行に関わる規則のうち、制限時間（<see cref="QuizTimeLimits"/>）以外のもの
    /// （得点・誤答後の受付再開放・回答の再入力可否）。
    /// 不変オブジェクトとして扱い、変更は新しいインスタンスの生成で表す。
    /// </summary>
    /// <remarks>
    /// 対応するルーム設定は <c>score.*</c>、<c>buzz.reopenAfterWrongAnswer</c>、
    /// <c>answer.singleAttemptOnly</c>（docs/room-settings.md §1）。
    /// 生成は <c>TsumugiQuiz.Room.ScoringSettings.ToQuizRules()</c>、
    /// フル <c>RoomSettings</c> との接続は #26。
    /// </remarks>
    public sealed class QuizRules
    {
        /// <summary>誤答・お手つき後に受付を再開放するかの既定値（<c>buzz.reopenAfterWrongAnswer</c>）。</summary>
        public const bool DefaultReopenAfterWrongAnswer = true;

        /// <summary>早押し後の回答入力を 1 回に限るかの既定値（<c>answer.singleAttemptOnly</c>）。</summary>
        public const bool DefaultSingleAttemptOnly = true;

        private static readonly QuizRules DefaultInstance = new QuizRules();

        /// <summary>
        /// 規則を指定して生成する。
        /// </summary>
        /// <param name="score">得点規則。null なら <see cref="ScoreRules.Default"/>。</param>
        /// <param name="reopenAfterWrongAnswer">誤答後に残り時間で受付を再開放するか。</param>
        /// <param name="singleAttemptOnly">早押し後の回答入力を 1 回に限るか。</param>
        public QuizRules(
            ScoreRules score = null,
            bool reopenAfterWrongAnswer = DefaultReopenAfterWrongAnswer,
            bool singleAttemptOnly = DefaultSingleAttemptOnly)
        {
            Score = score ?? ScoreRules.Default;
            ReopenAfterWrongAnswer = reopenAfterWrongAnswer;
            SingleAttemptOnly = singleAttemptOnly;
        }

        /// <summary>docs/room-settings.md の既定値。</summary>
        public static QuizRules Default => DefaultInstance;

        /// <summary>得点規則。</summary>
        public ScoreRules Score { get; }

        /// <summary>
        /// 誤答・お手つき後、早押し受付の残り時間があれば他プレイヤーに再開放するか
        /// （<c>buzz.reopenAfterWrongAnswer</c>、docs/network.md §6.6）。
        /// </summary>
        public bool ReopenAfterWrongAnswer { get; }

        /// <summary>
        /// 早押し後の回答入力を 1 回に限るか（<c>answer.singleAttemptOnly</c>、既定 true）。
        /// </summary>
        /// <remarks>
        /// false（制限時間内なら誤答後に送信し直せる）の挙動は #18 では実装していない。
        /// 本人に「誤答なのでもう一度」を伝える通知が必要になるため、#26 で通知 RPC と合わせて実装する
        /// （docs/room-settings.md §1 の注記）。それまで <see cref="QuizStateMachine"/> は
        /// この値に関わらず常に 1 回のみとして扱う。
        /// </remarks>
        public bool SingleAttemptOnly { get; }

        /// <summary>得点規則だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="score">得点規則。</param>
        /// <returns>新しいインスタンス。</returns>
        public QuizRules WithScore(ScoreRules score) =>
            new QuizRules(score, ReopenAfterWrongAnswer, SingleAttemptOnly);

        /// <summary>再開放の可否だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="reopenAfterWrongAnswer">誤答後に受付を再開放するか。</param>
        /// <returns>新しいインスタンス。</returns>
        public QuizRules WithReopenAfterWrongAnswer(bool reopenAfterWrongAnswer) =>
            new QuizRules(Score, reopenAfterWrongAnswer, SingleAttemptOnly);

        /// <summary>回答の再入力可否だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="singleAttemptOnly">回答入力を 1 回に限るか。</param>
        /// <returns>新しいインスタンス。</returns>
        public QuizRules WithSingleAttemptOnly(bool singleAttemptOnly) =>
            new QuizRules(Score, ReopenAfterWrongAnswer, singleAttemptOnly);
    }
}
