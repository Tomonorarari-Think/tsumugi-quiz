namespace TsumugiQuiz.Core.Reveal
{
    /// <summary>
    /// 問題文の文字送り表示（issue #144）の進め方。
    /// </summary>
    public enum QuestionRevealMode
    {
        /// <summary>全文を表示する（選択式・再同期・司会画面・判定後・文字送り無効）。</summary>
        Full = 0,

        /// <summary>
        /// 読み上げの再生開始時刻（<c>PlayAtRpc</c>）を待っている。まだ 1 文字も出さない。
        /// 待ちの上限を過ぎたら <see cref="FixedSpeed"/> に切り替える。
        /// </summary>
        AwaitingReading = 1,

        /// <summary>固定速度（ルーム設定 <c>question.revealMsPerChar</c>）で送る。</summary>
        FixedSpeed = 2,

        /// <summary>読み上げの再生時間を文字数で按分して、読み上げの進行に合わせて送る。</summary>
        Synced = 3,
    }
}
