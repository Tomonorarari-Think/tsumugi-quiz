namespace TsumugiQuiz.UI
{
    /// <summary>
    /// 立ち絵（<see cref="CharacterView"/>）の表示状態（issue #24、docs/tts.md §8）。
    /// </summary>
    /// <remarks>
    /// #212 で場面ごとの表情（回答権の獲得・誤答の瞬間・時間切れ・回答できる人がいない）を追加した。
    /// 既存の値（0〜3）の番号は変えない。状態ごとの画像ファイルは <see cref="CharacterImagePaths"/>、
    /// マークとティントは <see cref="CharacterStateVisuals"/> を参照。
    /// </remarks>
    public enum CharacterState
    {
        /// <summary>待機。</summary>
        Idle = 0,

        /// <summary>読み上げ中。</summary>
        Reading = 1,

        /// <summary>正解表示中（一定時間後に <see cref="Idle"/> / <see cref="Reading"/> へ戻る）。</summary>
        Correct = 2,

        /// <summary>不正解の確定表示中（一定時間後に <see cref="Idle"/> / <see cref="Reading"/> へ戻る）。</summary>
        Wrong = 3,

        /// <summary>自分が回答権を得た（#212。結果が出るまで保持する）。</summary>
        BuzzSelf = 4,

        /// <summary>他の参加者が回答権を得た（#212。結果が出るまで保持する）。</summary>
        BuzzOther = 5,

        /// <summary>
        /// 誤答して早押しの受付が開き直された瞬間（#212、<c>GameSession.BuzzReopened</c>）。
        /// 結果はまだ確定していない。一定時間後に <see cref="Idle"/> / <see cref="Reading"/> へ戻る。
        /// </summary>
        WrongMoment = 6,

        /// <summary>誰も正解しないまま時間切れになった（#212。一定時間後に戻る）。</summary>
        TimedOut = 7,

        /// <summary>押せる参加者が居なくなって受付を締めた（#200 / #212。一定時間後に戻る）。</summary>
        NoEligibleBuzzers = 8,
    }
}
