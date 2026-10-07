using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.Tests.PlayMode.UI.TextLayout
{
    /// <summary>
    /// issue #189: 文字のはみ出し・改行位置の崩れを検出するための計測ヘルパー。
    /// 実際に描画される折り返しは <see cref="TextElement.MeasureTextSize"/> と同じテキスト生成器
    /// （Unity 6.5 以降の既定 = Advanced Text Generator）で決まるため、これで行数を数える。
    /// </summary>
    internal static class TextLayoutProbe
    {
        /// <summary>
        /// docs/architecture.md §10.2 の代表条件（900x750 / 16:9 / 21:9）に対応する論理ビューポート（px）。
        /// 値は共通の補助 <see cref="PanelScaleProbe"/> で計算する（約 1289.6x1074.6 / 1600x900 / 約 1828.6x771.4）。
        /// </summary>
        internal static readonly Vector2[] LogicalViewports =
        {
            PanelScaleProbe.ViewportMinimumWindow,
            PanelScaleProbe.Viewport16By9,
            PanelScaleProbe.ViewportUltraWide,
        };

        /// <summary>
        /// View のルート要素を論理ビューポートの大きさに固定する（GameViewVerticalFitSceneTests と同じ作法。
        /// バッチ実行の画面サイズは固定なので、倍率は変えずに論理ピクセルの大きさだけを再現する）。
        /// </summary>
        internal static IEnumerator ResizeRoot(VisualElement root, Vector2 size)
        {
            root.style.width = size.x;
            root.style.height = size.y;
            root.style.flexGrow = 0;
            root.style.flexShrink = 0;
            for (var i = 0; i < 3; i++)
            {
                yield return null;
            }
        }

        /// <summary>1 行で描いたときの文字列の幅（改行を含む場合は最も長い行）。</summary>
        internal static float SingleLineWidth(TextElement te, string text)
        {
            var max = 0f;
            foreach (var segment in text.Split('\n'))
            {
                if (segment.Length == 0)
                {
                    continue;
                }

                var size = te.MeasureTextSize(
                    segment, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                max = Mathf.Max(max, size.x);
            }

            return max;
        }

        /// <summary>
        /// 明示的な改行（\n）で区切った各行のうち、1 行に収まらずに自動で折り返す行を返す（#189 レビュー L-5）。
        /// 設定・ホスト設定・問題エディタの静的な文言は「自動では折り返さず、改行が要る位置には明示改行を入れる」
        /// 方針（docs/architecture.md §10.10）なので、自動の折り返しがあれば語の途中や末尾だけの改行が起きうる
        /// （例「…開放してく / ださい。」）。明示改行を外すと、その文言がここで検出される。
        /// </summary>
        internal static IEnumerable<string> FindImplicitlyWrappedLines(TextElement te)
        {
            var width = te.contentRect.width;
            if (string.IsNullOrEmpty(te.text) || width <= 0.5f)
            {
                yield break;
            }

            foreach (var segment in te.text.Split('\n'))
            {
                if (segment.Length > 0 && SingleLineWidth(te, segment) > width + 0.5f)
                {
                    yield return segment;
                }
            }
        }

        /// <summary>
        /// 実際に組まれた各行を返す（issue #199）。<see cref="ITextSelection.GetCursorPositionFromStringIndex"/> で各文字の前の
        /// カーソル位置を求め、y が変わった位置を行の区切りとする（明示改行の行は末尾に \n を含む）。
        /// 添字は <see cref="TextElement.parsedText"/> の位置（#199 の実測。タグを含まない文言ではそのまま <see cref="TextElement.text"/> の位置）。
        /// </summary>
        internal static List<string> VisualLines(TextElement te)
        {
            var lines = new List<string>();
            var text = te.text ?? string.Empty;
            if (text.Length == 0)
            {
                return lines;
            }

            var selection = te.selection;
            var start = 0;
            var previousY = selection.GetCursorPositionFromStringIndex(0).y;
            for (var i = 1; i < text.Length; i++)
            {
                var y = selection.GetCursorPositionFromStringIndex(i).y;
                if (y > previousY + 0.5f)
                {
                    lines.Add(text.Substring(start, i - start));
                    start = i;
                }

                previousY = y;
            }

            lines.Add(text.Substring(start));
            return lines;
        }

        /// <summary>
        /// 明示改行（\n）以外の位置で折り返している行（<see cref="VisualLines"/> のうち、最後の行以外で \n で終わらない行）を返す。
        /// </summary>
        internal static IEnumerable<string> FindAutoWrappedVisualLines(TextElement te)
        {
            var lines = VisualLines(te);
            for (var i = 0; i < lines.Count - 1; i++)
            {
                if (!lines[i].EndsWith("\n"))
                {
                    yield return lines[i];
                }
            }
        }

        /// <summary><paramref name="inner"/> の横方向の範囲が <paramref name="outer"/> の内側にあるか（許容 1px）。</summary>
        internal static bool IsHorizontallyInside(Rect inner, Rect outer)
            => inner.xMin >= outer.xMin - 1f && inner.xMax <= outer.xMax + 1f;

        /// <summary>要素と祖先がすべて表示中（display: none でない）か。</summary>
        internal static bool IsDisplayed(VisualElement ve)
        {
            for (var e = ve; e != null; e = e.parent)
            {
                if (e.resolvedStyle.display == DisplayStyle.None)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
