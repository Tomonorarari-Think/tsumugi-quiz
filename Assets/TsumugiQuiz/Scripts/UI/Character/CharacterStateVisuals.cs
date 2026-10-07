namespace TsumugiQuiz.UI
{
    /// <summary>立ち絵に掛けるティントの種類（#192。どの色を掛けるかは theme.uss のトークンで決まる）。</summary>
    public enum CharacterTint
    {
        /// <summary>無着色。</summary>
        None = 0,

        /// <summary>正解（<c>--color-character-tint-correct</c>）。</summary>
        Correct = 1,

        /// <summary>不正解・時間切れ（<c>--color-character-tint-wrong</c>）。</summary>
        Wrong = 2,
    }

    /// <summary>
    /// 状態ごとのマーク（○ / ×）とティントの対応（issue #212）。Unity API に依存しない純 C#。
    /// </summary>
    /// <remarks>
    /// <para>
    /// マークは #24 からの挙動を保つ: 正解は ○、不正解の確定は ×、時間切れと「回答できる人がいない」は
    /// 従来の時間切れと同じく ×。誤答の瞬間（<see cref="CharacterState.WrongMoment"/>）は結果がまだ確定して
    /// いないのでマークを出さない。
    /// </para>
    /// <para>
    /// ティント（#192）は、その状態専用の表情差分が読めたときは掛けない（表情で伝わるため）。
    /// 差分が無く、別の表情や従来の全身 PNG へフォールバックしたときだけ、結果の手がかりとして掛ける。
    /// </para>
    /// </remarks>
    public static class CharacterStateVisuals
    {
        /// <summary>○ マークを出すか。</summary>
        public static bool ShowsCorrectMark(CharacterState state) => state == CharacterState.Correct;

        /// <summary>× マークを出すか。</summary>
        public static bool ShowsWrongMark(CharacterState state) => IsNegativeResult(state);

        /// <summary>
        /// 掛けるティントを返す。
        /// </summary>
        /// <param name="state">立ち絵の状態。</param>
        /// <param name="hasDedicatedImage">その状態専用の表情差分を表示しているか（フォールバックしていないか）。</param>
        public static CharacterTint GetTint(CharacterState state, bool hasDedicatedImage)
        {
            if (hasDedicatedImage)
            {
                return CharacterTint.None;
            }

            if (state == CharacterState.Correct)
            {
                return CharacterTint.Correct;
            }

            return IsNegativeResult(state) ? CharacterTint.Wrong : CharacterTint.None;
        }

        /// <summary>確定した「正解ではない」結果か（不正解・時間切れ・回答できる人がいない）。</summary>
        private static bool IsNegativeResult(CharacterState state) =>
            state == CharacterState.Wrong
            || state == CharacterState.TimedOut
            || state == CharacterState.NoEligibleBuzzers;
    }
}
