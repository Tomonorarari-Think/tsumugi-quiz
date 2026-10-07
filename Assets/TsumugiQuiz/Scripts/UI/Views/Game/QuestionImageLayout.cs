namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// 問題画像（issue #185 / #193）の表示に関する定数と判定。Unity API に依存しない（EditMode でテスト）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// #193 で画像は問題文とは別のエリア（中央列の上、<c>question-image-container</c>）に置くようにした。
    /// エリアの大きさは USS（theme-views-game.uss の <c>.game-question-image-area</c>）だけで決まる。
    /// エリアは中央列の残りの高さを埋め（<c>flex-grow: 1</c>）、その内側に画像を広げて
    /// <c>scale-mode="ScaleToFit"</c> で縦横比を保って収める（小さい画像も拡大する。クイズの画像は
    /// 「見せて答えさせる」ためのものであり、元画像の画素数が少なくても読み取れる大きさを優先する）。
    /// </para>
    /// <para>
    /// ここの定数は USS の値と一致させること（PlayMode テストがエリアの実寸をこれらと比べて検証する）。
    /// </para>
    /// </remarks>
    internal static class QuestionImageLayout
    {
        /// <summary>
        /// 画像エリアの padding（論理 px、上下左右とも）。USS の <c>.game-question-image-area</c> の
        /// <c>padding: var(--spacing-s)</c>（8px）と一致させること。
        /// </summary>
        public const float AreaPadding = 8f;

        /// <summary>
        /// 画像の最小の表示高さ（論理 px）。作業領域が低い（21:9 の約 707px など）ときも画像が判別できる大きさを残す。
        /// USS の <c>min-height</c> はこれに上下の <see cref="AreaPadding"/> を足した 176px。
        /// これを下回るほど縦幅が足りないときは、問題・解答のスクロール領域（#187）が縮んでスクロールする。
        /// </summary>
        public const float MinDisplayHeight = 160f;

        /// <summary>
        /// 画像の最大の表示高さ（論理 px）。作業領域が高い（900x750 ウィンドウの約 1011px など）ときの上限。
        /// USS の <c>max-height</c> はこれに上下の <see cref="AreaPadding"/> を足した 496px。
        /// </summary>
        public const float MaxDisplayHeight = 480f;

        /// <summary>画像エリアの最小の高さ（padding 込み、論理 px）。USS の <c>min-height</c>。</summary>
        public const float MinAreaHeight = MinDisplayHeight + AreaPadding * 2f;

        /// <summary>画像エリアの最大の高さ（padding 込み、論理 px）。USS の <c>max-height</c>。</summary>
        public const float MaxAreaHeight = MaxDisplayHeight + AreaPadding * 2f;

        /// <summary>
        /// 画像の寸法が表示できるものか（幅・高さとも正）。不正な寸法の画像は表示せず、画像エリアを畳む。
        /// </summary>
        /// <param name="textureWidth">画像の幅（px）。</param>
        /// <param name="textureHeight">画像の高さ（px）。</param>
        /// <returns>表示してよければ true。</returns>
        public static bool IsDisplayableSize(int textureWidth, int textureHeight) =>
            textureWidth > 0 && textureHeight > 0;
    }
}
