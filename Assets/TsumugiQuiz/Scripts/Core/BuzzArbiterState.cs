namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="BuzzArbiter"/> の受付状態。docs/network.md §6.6 の BuzzOpen / Collecting / Locked に対応する。
    /// </summary>
    public enum BuzzArbiterState
    {
        /// <summary>受付中。押下を受理し、最初の 1 件で集計窓が開く。</summary>
        Open = 0,

        /// <summary>集計窓が閉じ、勝者が確定した（Locked）。</summary>
        Resolved = 1,

        /// <summary>勝者を決めずに受付を閉じた（誰も押さずにタイムアウト、フェーズ中断など）。</summary>
        Closed = 2,
    }
}
