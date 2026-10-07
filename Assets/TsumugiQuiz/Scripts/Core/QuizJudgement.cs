namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 1 問の判定結果（docs/network.md §6.6 の Correct / Wrong / TimedOut と、#200 の NoEligibleBuzzers）。
    /// </summary>
    public enum QuizJudgement
    {
        /// <summary>未判定。</summary>
        None = 0,

        /// <summary>正解（正規化後に正解候補のいずれかと一致）。</summary>
        Correct = 1,

        /// <summary>誤答（不一致、または回答の制限時間切れ）。</summary>
        Wrong = 2,

        /// <summary>誰も押さないまま早押し受付がタイムアウトした。</summary>
        TimedOut = 3,

        /// <summary>
        /// 早押し受付中に押せる参加者が居なくなったため、時間切れを待たずに締めた（誰も正解しなかった、#200）。
        /// 結果の表示は時間切れと区別する（効果音・立ち絵は <see cref="TimedOut"/> と同じ）。
        /// </summary>
        NoEligibleBuzzers = 4,
    }
}
