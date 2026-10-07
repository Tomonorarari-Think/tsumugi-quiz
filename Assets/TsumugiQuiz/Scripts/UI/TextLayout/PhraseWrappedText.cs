using System.Runtime.CompilerServices;
using TsumugiQuiz.Core.TextLayout;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.TextLayout
{
    /// <summary>
    /// 動的な文言（エラー・状態表示）を、語の途中ではなく文節に近い区切りの位置で改行して表示する（issue #199、
    /// docs/architecture.md §10.10）。
    ///
    /// UI Toolkit の Advanced Text Generator は日本語を文字単位で折り返し、<c>&lt;nobr&gt;</c> タグも WORD JOINER
    /// （U+2060）も効かない（#199 の実測）。そこで要素の幅で 1 行に収まる区切りを <see cref="PhraseLineComposer"/> で
    /// 求め、その位置に改行文字を入れた文字列を <see cref="TextElement.text"/> に設定する。要素の幅が変わったら
    /// （<see cref="GeometryChangedEvent"/>）元の文言から組み直す。
    ///
    /// 前提: 要素の幅が文言によって変わらないこと（親の幅いっぱいに伸びる要素に使う）。文言に合わせて縮む要素に使うと、
    /// 改行を入れるたびに幅が変わって組み直しが止まらなくなるおそれがある。
    ///
    /// 文言は平文として表示する。<see cref="SetText"/> は要素のリッチテキストの解釈を止める（<see cref="PlainText"/>、#206）。
    /// 表示する文言にはホストから届く切断理由も含まれ、タグを解釈させると画面を崩せるため。タグを解釈させないので、
    /// <c>&lt;</c> を含む文言も区切りの位置で改行できる。
    /// </summary>
    public static class PhraseWrappedText
    {
        /// <summary>幅の変化とみなす差（px）。</summary>
        private const float WidthChangeThreshold = 0.5f;

        private sealed class State
        {
            /// <summary>呼び出し側が渡した元の文言。</summary>
            public string Source = string.Empty;

            /// <summary>最後に <see cref="TextElement.text"/> に設定した文字列（外から書き換えられたかの判定に使う）。</summary>
            public string Composed = string.Empty;

            /// <summary>最後に組んだときの幅。</summary>
            public float Width = float.NaN;
        }

        private static readonly ConditionalWeakTable<TextElement, State> States = new ConditionalWeakTable<TextElement, State>();

        /// <summary>
        /// <paramref name="element"/> に <paramref name="text"/> を平文として設定する。要素の幅に合わせて区切りの位置に改行を入れる。
        /// パネルに載っていない・幅が決まっていないときはそのまま設定し、幅が決まった時点で組み直す。
        /// 要素の <see cref="TextElement.enableRichText"/> は false にする（#206）。
        /// </summary>
        public static void SetText(TextElement element, string text)
        {
            if (element == null)
            {
                return;
            }

            // 幅を測る前に止める（タグを解釈する状態で測ると、表示と幅が合わない）。
            PlainText.Apply(element);

            if (!States.TryGetValue(element, out var state))
            {
                state = new State();
                States.Add(element, state);
                element.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            }

            state.Source = text ?? string.Empty;
            Apply(element, state);
        }

        /// <summary>
        /// <see cref="SetText"/> で設定した元の文言（改行を入れる前）を返す。<see cref="SetText"/> を経ずに
        /// <see cref="TextElement.text"/> が書き換えられていれば、その値を返す。
        /// </summary>
        public static string GetSourceText(TextElement element)
        {
            if (element == null)
            {
                return null;
            }

            return States.TryGetValue(element, out var state) && element.text == state.Composed
                ? state.Source
                : element.text;
        }

        private static void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (!(evt.target is TextElement element) || !States.TryGetValue(element, out var state))
            {
                return;
            }

            // SetText を経ずに text が書き換えられていたら、古い文言で上書きしない。
            if (element.text != state.Composed)
            {
                return;
            }

            var width = AvailableWidth(element);
            if (float.IsNaN(width) || (!float.IsNaN(state.Width) && System.Math.Abs(width - state.Width) <= WidthChangeThreshold))
            {
                return;
            }

            Apply(element, state);
        }

        private static void Apply(TextElement element, State state)
        {
            var width = AvailableWidth(element);
            state.Width = width;
            var composed = element.panel == null || float.IsNaN(width)
                ? state.Source
                : PhraseLineComposer.Compose(state.Source, line => MeasureSingleLine(element, line), width);
            state.Composed = composed;
            if (element.text != composed)
            {
                element.text = composed;
            }
        }

        private static float MeasureSingleLine(TextElement element, string line)
            => element.MeasureTextSize(line, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;

        /// <summary>
        /// 文字を組める幅。要素が表示されて大きさが決まっていればその内容領域の幅、非表示（display: none）の間は
        /// 親の内容領域の幅から要素の外側・内側の余白と枠線を引いた値（親の幅いっぱいに伸びる要素を前提とする）。
        /// </summary>
        private static float AvailableWidth(TextElement element)
        {
            var width = element.contentRect.width;
            if (!float.IsNaN(width) && width > 0f && element.resolvedStyle.display != DisplayStyle.None)
            {
                return width;
            }

            var parent = element.hierarchy.parent;
            if (parent == null || element.panel == null)
            {
                return float.NaN;
            }

            var parentWidth = parent.contentRect.width;
            if (float.IsNaN(parentWidth) || parentWidth <= 0f)
            {
                return float.NaN;
            }

            var style = element.resolvedStyle;
            return parentWidth - style.marginLeft - style.marginRight - style.borderLeftWidth - style.borderRightWidth
                   - style.paddingLeft - style.paddingRight;
        }
    }
}
