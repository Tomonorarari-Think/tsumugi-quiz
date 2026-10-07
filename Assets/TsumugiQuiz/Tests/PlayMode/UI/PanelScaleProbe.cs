using UnityEngine;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// PanelSettings（panel-settings.asset）の拡縮の前提と、実解像度から論理ビューポートを求める計算を
    /// PlayMode テストで共有する補助（docs/architecture.md §10.2）。Game 画面の縦幅テスト（#187 / #193）と
    /// 文字の折り返しテスト（#189）の両方から使う。
    /// </summary>
    internal static class PanelScaleProbe
    {
        /// <summary>PanelSettings の基準解像度（Assets/TsumugiQuiz/Settings/panel-settings.asset、#132）。</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1600f, 900f);

        /// <summary>PanelSettings の MatchWidthOrHeight の match（panel-settings.asset）。</summary>
        public const float Match = 0.5f;

        /// <summary>想定最小ウィンドウ 900x750 の論理ビューポート（約 1289.6x1074.6）。</summary>
        public static readonly Vector2 ViewportMinimumWindow = LogicalViewportFor(900f, 750f);

        /// <summary>16:9（1280x720 ウィンドウ・1600x900 / 1920x1080 フルスクリーン）の論理ビューポート（1600x900）。</summary>
        public static readonly Vector2 Viewport16By9 = LogicalViewportFor(1600f, 900f);

        /// <summary>21:9（2560x1080 フルスクリーン）の論理ビューポート（約 1828.6x771.4）。</summary>
        public static readonly Vector2 ViewportUltraWide = LogicalViewportFor(2560f, 1080f);

        /// <summary>
        /// 実解像度から論理ビューポートを求める。UI Toolkit の <c>PanelScaleMode.ScaleWithScreenSize</c> +
        /// <c>MatchWidthOrHeight</c> は、幅の比と高さの比を match で<b>線形補間</b>した値で割る
        /// （UnityCsReference の <c>PanelSettingsUtility.ResolveScale</c>:
        /// <c>denominator = Mathf.Lerp(size.x / ref.x, size.y / ref.y, match)</c>、
        /// https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/UIElements/Core/GameObjects/PanelSettingsUtility.cs 。
        /// uGUI の CanvasScaler の対数補間＝幾何平均とは異なる。docs/architecture.md §10.2）。
        /// </summary>
        public static Vector2 LogicalViewportFor(float physicalWidth, float physicalHeight)
        {
            var denominator = Mathf.Lerp(
                physicalWidth / ReferenceResolution.x, physicalHeight / ReferenceResolution.y, Match);
            return new Vector2(physicalWidth / denominator, physicalHeight / denominator);
        }
    }
}
