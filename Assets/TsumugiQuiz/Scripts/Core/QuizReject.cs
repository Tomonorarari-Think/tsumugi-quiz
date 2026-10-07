namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine"/> の進行操作（出題・読み上げ完了・終了）を拒否した理由。
    /// サーバーのログに残す用途で、クライアントへはそのまま返さない（docs/network.md §9）。
    /// </summary>
    public enum QuizReject
    {
        /// <summary>拒否していない。</summary>
        None = 0,

        /// <summary>現在のフェーズでは実行できない操作。</summary>
        InvalidPhase = 1,

        /// <summary>問題インデックスが負。</summary>
        InvalidQuestionIndex = 2,

        /// <summary>正解候補が 1 件も無い（freeText の必須項目）。</summary>
        NoAnswers = 3,

        /// <summary>時刻が有限の値でない（NaN / Infinity）。</summary>
        NonFiniteTime = 4,

        /// <summary>
        /// 時刻が現実的な範囲の外（早押し受付開始 T0 が出題より前、または出題から
        /// <see cref="QuizStateMachine.MaxReadingDurationSec"/> 秒を超えて先）。
        /// </summary>
        InvalidTime = 5,

        /// <summary>セッションの総問題数が 1 未満（#19、<c>questions.count</c> の結果が 0 件）。</summary>
        InvalidTotalQuestions = 6,

        /// <summary>
        /// 選択式の出題（<c>correctChoiceIndex</c> / <c>choiceCount</c>）が不正
        /// （件数が 2〜8 件の範囲外、または正解インデックスが範囲外）。
        /// </summary>
        InvalidChoice = 7,

        /// <summary>司会が一時停止中（#20、docs/tasks/setup-brief.md K18）。</summary>
        Paused = 8,
    }
}
