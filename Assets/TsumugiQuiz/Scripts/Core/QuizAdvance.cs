namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine.AdvanceToNextQuestion"/> の結果
    /// （docs/network.md §6.6 の <c>Result --&gt; Idle</c> / <c>Result --&gt; [*]</c>）。
    /// </summary>
    public enum QuizAdvance
    {
        /// <summary>進めなかった（フェーズ違い・時刻異常・正解候補が無い）。理由は <see cref="QuizReject"/>。</summary>
        Rejected = 0,

        /// <summary>次の問題の出題を開始した（Result → Reading）。</summary>
        NextQuestionStarted = 1,

        /// <summary>次の問題が無いのでセッションを終了した（Result → Finished）。</summary>
        SessionFinished = 2,
    }
}
