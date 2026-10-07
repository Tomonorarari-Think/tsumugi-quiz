namespace TsumugiQuiz.Core
{
    /// <summary>
    /// お手つき（誤答）時のペナルティ種別（<c>score.penaltyType</c>、docs/room-settings.md §1「得点」、仮決め K19）。
    /// </summary>
    public enum PenaltyKind
    {
        /// <summary>次の 1 問を休む（次問の早押し受付で棄却される、docs/network.md §6.4 の「ペナルティ中」）。既定。</summary>
        SkipNext = 0,

        /// <summary>その場で減点する（<c>score.penaltyMinusPoints</c>、既定 -5）。</summary>
        MinusPoints = 1,

        /// <summary>
        /// ペナルティなし（誤答しても <c>score.incorrectPoints</c> の変化だけで、次問休みにも減点にもしない）。
        /// 組み込みプリセット「のんびり」で使う（docs/room-settings.md §3）。
        /// </summary>
        None = 2,
    }
}
