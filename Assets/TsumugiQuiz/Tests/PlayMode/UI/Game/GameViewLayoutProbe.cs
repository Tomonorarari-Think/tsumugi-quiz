using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.Tests.PlayMode.UI.Game
{
    /// <summary>
    /// Game 画面の縦幅（docs/architecture.md §10.2、#185 / #187 / #193）を実測・検証する PlayMode テスト用の補助。
    /// 基準解像度・match・論理ビューポートの計算は、文字の折り返しテスト（#189）と共有する <see cref="PanelScaleProbe"/> にある。
    /// </summary>
    /// <remarks>
    /// #187 で本体（<c>game-content-main</c>）は「縦スクロール領域 <c>game-scroll-view</c>（問題〜判定結果）」と
    /// 「縮まない下段（司会操作・退出確認・『次へ』『退出』の行）」に分かれた。スクロール領域は作業領域に
    /// 収まらないときに縮むため、<b>本来の縦幅</b>（スクロールしないで並べたときの高さ）はスクロール領域の
    /// 中身（<c>contentContainer</c>）の高さ + 下段の高さで測る。
    /// </remarks>
    internal static class GameViewLayoutProbe
    {
        /// <summary>
        /// レイアウト値の許容誤差（論理 px）。UI Toolkit は配置を物理ピクセルへ丸めるため、
        /// パネルの倍率が 1 未満（バッチ実行の画面サイズ）だと論理 px で 1〜2px ずれる。
        /// </summary>
        public const float LayoutTolerance = 2.5f;

        /// <summary>
        /// 本体の本来の縦幅（スクロールせずに並べたときの高さ）。スクロール領域の中身の高さ + その上下 margin +
        /// スクロール領域以外の表示中の子（下段）の高さ + 上下 margin。
        /// </summary>
        public static float NaturalHeight(VisualElement main, ScrollView scroll)
        {
            var total = 0f;
            foreach (var child in main.Children())
            {
                if (child.resolvedStyle.display == DisplayStyle.None)
                {
                    continue;
                }

                var height = ReferenceEquals(child, scroll) ? scroll.contentContainer.layout.height : child.layout.height;
                total += height + child.resolvedStyle.marginTop + child.resolvedStyle.marginBottom;
            }

            return total;
        }

        /// <summary>
        /// <c>game-root</c> を目的の論理ビューポートの大きさに固定する（バッチ実行の画面サイズは固定なので、
        /// これで各解像度の論理ビューポートを再現する。パネルより大きくてもよい。レイアウトは game-root の大きさで解決される）。
        /// </summary>
        public static void SetLogicalViewport(VisualElement gameRoot, Vector2 viewport)
        {
            gameRoot.style.flexGrow = 0f;
            gameRoot.style.flexShrink = 0f;
            gameRoot.style.width = viewport.x;
            gameRoot.style.height = viewport.y;
        }

        /// <summary>作業領域（<c>.screen-root</c> の padding の内側）の世界座標での上端・下端。</summary>
        public static (float Top, float Bottom) WorkingArea(VisualElement gameRoot)
        {
            var bound = gameRoot.worldBound;
            return (bound.yMin + gameRoot.resolvedStyle.paddingTop, bound.yMax - gameRoot.resolvedStyle.paddingBottom);
        }

        /// <summary>
        /// 要素が作業領域の中にあり、スクロール領域の中の要素ならスクロールの表示範囲（viewport）の中にも
        /// 収まっている（＝スクロールせずに見えて押せる）こと。
        /// </summary>
        public static void AssertFullyVisible(VisualElement element, VisualElement gameRoot, ScrollView scroll, string label)
        {
            Assert.AreEqual(DisplayStyle.Flex, element.resolvedStyle.display, $"{label}: 表示されているはず。");
            var bound = element.worldBound;
            var (top, bottom) = WorkingArea(gameRoot);
            Assert.That(bound.yMax, Is.LessThanOrEqualTo(bottom + LayoutTolerance),
                $"{label}: 下端 {bound.yMax:F1} が作業領域の下端 {bottom:F1} を超えている。");
            Assert.That(bound.yMin, Is.GreaterThanOrEqualTo(top - LayoutTolerance),
                $"{label}: 上端 {bound.yMin:F1} が作業領域の上端 {top:F1} より上にある。");

            if (scroll.contentContainer.Contains(element))
            {
                var viewport = scroll.contentViewport.worldBound;
                Assert.That(bound.yMax, Is.LessThanOrEqualTo(viewport.yMax + LayoutTolerance),
                    $"{label}: 下端 {bound.yMax:F1} がスクロールの表示範囲の下端 {viewport.yMax:F1} を超えている（隠れている）。");
                Assert.That(bound.yMin, Is.GreaterThanOrEqualTo(viewport.yMin - LayoutTolerance),
                    $"{label}: 上端 {bound.yMin:F1} がスクロールの表示範囲の上端 {viewport.yMin:F1} より上にある（隠れている）。");
            }
        }

        /// <summary>
        /// 中央列（<c>game-content-main</c>）の最大幅（theme-views-game.uss の <c>.game-content-main</c>）。
        /// #213 で立ち絵の有無にかかわらず付けるようにした（以前は立ち絵なしの修飾クラスにだけ付けていた）。
        /// </summary>
        public const float MainMaxWidth = 880f;

        /// <summary>
        /// 3 列（参加者パネル・中央列・立ち絵の右列）を出したときの中央列の幅と配置を確かめる（#193 / #213）。
        /// 中央列は左右の列の残り（行の内側の幅 − 左右の列 − 列の間隔）を埋めるが <see cref="MainMaxWidth"/> で頭打ちにし、
        /// 頭打ちになって余った幅は、行の <c>justify-content: center</c> で 3 列ごと中央に寄せる（左右の余白が等しい）。
        /// 頭打ちにならないビューポート（16:9・900x750）では余白が 0 なので、左右の列が行の両端に付く。
        /// 左右の余白の比較は <see cref="AssertCenteredInRow"/>（物理ピクセル基準の誤差）で行う。
        /// </summary>
        /// <returns>期待した中央列の幅（ログ用）。</returns>
        public static float AssertMainColumnWithCharacter(
            VisualElement row, VisualElement participant, VisualElement main, VisualElement slot, string label)
        {
            Assert.AreEqual(DisplayStyle.Flex, slot.resolvedStyle.display, $"{label}: 立ち絵ありでは右列を出すはず。");
            var gaps = participant.resolvedStyle.marginRight + slot.resolvedStyle.marginLeft;
            var remaining = row.contentRect.width - participant.layout.width - slot.layout.width - gaps;
            var expected = Mathf.Min(MainMaxWidth, remaining);
            Assert.That(
                main.layout.width, Is.EqualTo(expected).Within(LayoutTolerance),
                $"{label}: 中央列は左右の列の残り（{remaining:F1}px）を埋め、最大 {MainMaxWidth}px で頭打ちになるはず。");

            var leftSpace = participant.worldBound.xMin - row.worldBound.xMin;
            var rightSpace = row.worldBound.xMax - slot.worldBound.xMax;
            var reachesMaxWidth = remaining > MainMaxWidth + LayoutTolerance;
            AssertCenteredInRow(row, leftSpace, rightSpace, expectSpace: reachesMaxWidth, $"{label}: 3 列");
            Assert.That(main.worldBound.xMin, Is.GreaterThan(participant.worldBound.xMax), $"{label}: 中央列は参加者パネルの右のはず。");
            Assert.That(slot.worldBound.xMin, Is.GreaterThan(main.worldBound.xMax), $"{label}: 右列は中央列の右のはず。");
            return expected;
        }

        /// <summary>
        /// 1 物理ピクセルが何論理 px に当たるか（<see cref="IPanel.scaledPixelsPerPoint"/> の逆数）。
        /// UI Toolkit は配置を物理ピクセルへ丸めるので、配置どうしの差はこの整数倍でずれうる
        /// （バッチ実行のパネルでは 2.142857 論理 px。PR #215 レビュー M-2）。
        /// </summary>
        public static float PhysicalPixel(VisualElement element)
        {
            Assert.IsNotNull(element.panel, $"{element.name} がパネルに載っていません。");
            var scaledPixelsPerPoint = element.panel.scaledPixelsPerPoint;
            Assert.That(scaledPixelsPerPoint, Is.GreaterThan(0f), "パネルの scaledPixelsPerPoint が正でない。");
            return 1f / scaledPixelsPerPoint;
        }

        /// <summary>
        /// 行の中で、並んだ列が中央に寄っている（左右の余白が等しい）こと。左端・右端はそれぞれ物理ピクセルへ
        /// 丸められるので、差は 2 物理 px まで許す。<paramref name="expectSpace"/> が true（中央列が最大幅で
        /// 頭打ちになり、幅が余る）なら、左右とも余白があることも確かめる。false なら余白は無い（列が行の両端に付く）。
        /// </summary>
        public static void AssertCenteredInRow(VisualElement row, float leftSpace, float rightSpace, bool expectSpace, string label)
        {
            var physicalPixel = PhysicalPixel(row);
            var tolerance = 2f * physicalPixel + 0.01f;
            var detail = $"左の余白 {leftSpace:F2}px、右の余白 {rightSpace:F2}px、1 物理 px = {physicalPixel:F6} 論理 px"
                         + $"（visualTree の幅 / Screen.width = {row.panel.visualTree.layout.width / Screen.width:F6}）";
            Debug.Log($"[中央寄せ] {label}: {detail}");
            Assert.That(
                Mathf.Abs(leftSpace - rightSpace), Is.LessThanOrEqualTo(tolerance),
                $"{label}は行の中央に寄るはず（差は 2 物理 px = {tolerance:F2}px まで。{detail}）。");
            if (expectSpace)
            {
                Assert.That(leftSpace, Is.GreaterThan(0f), $"{label}: 最大幅で頭打ちなので左に余白があるはず（{detail}）。");
                Assert.That(rightSpace, Is.GreaterThan(0f), $"{label}: 最大幅で頭打ちなので右に余白があるはず（{detail}）。");
            }
            else
            {
                Assert.That(leftSpace, Is.LessThanOrEqualTo(tolerance), $"{label}: 幅が余らないので左右の列は行の両端に付くはず（{detail}）。");
            }
        }

        /// <summary>スクロールせずに中身が全部見えている（中身の高さが表示範囲以下）こと。</summary>
        public static bool FitsWithoutScrolling(ScrollView scroll)
            => scroll.contentContainer.layout.height <= scroll.contentViewport.layout.height + LayoutTolerance;

        /// <summary>縦幅の内訳をログに残す（docs/architecture.md §10.2 の表の根拠）。</summary>
        public static void LogBreakdown(string tag, string label, VisualElement gameRoot, VisualElement main, ScrollView scroll)
        {
            var (top, bottom) = WorkingArea(gameRoot);
            Debug.Log(
                $"[{tag}] {label}: 本来の縦幅 {NaturalHeight(main, scroll):F1}px / 作業領域 {bottom - top:F1}px"
                + $"（スクロール領域 {scroll.layout.height:F1}px、中身 {scroll.contentContainer.layout.height:F1}px、"
                + $"スクロール位置 {scroll.scrollOffset.y:F1}、スクロール不要={FitsWithoutScrolling(scroll)}）"
                + $" 内訳: 本体{Children(main, scroll)} / スクロール内{Children(scroll.contentContainer, null)}"
                + $" / 問題パネル{Children(main.Q<VisualElement>("game-header"), null)}");
        }

        private static string Children(VisualElement parent, ScrollView scroll)
        {
            if (parent == null)
            {
                return " (なし)";
            }

            var lines = new StringBuilder();
            foreach (var child in parent.Children())
            {
                if (child.resolvedStyle.display == DisplayStyle.None)
                {
                    continue;
                }

                var height = scroll != null && ReferenceEquals(child, scroll) ? scroll.layout.height : child.layout.height;
                lines.Append($" {child.name}={height:F1}(+{child.resolvedStyle.marginTop + child.resolvedStyle.marginBottom:F0})");
            }

            return lines.ToString();
        }
    }
}
