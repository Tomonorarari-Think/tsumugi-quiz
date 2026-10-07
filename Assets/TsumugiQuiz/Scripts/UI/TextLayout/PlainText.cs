using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.TextLayout
{
    /// <summary>
    /// 文字列をリッチテキストとして解釈させずに、書かれたとおりの文字で表示する（issue #206、docs/architecture.md §10.10）。
    ///
    /// UI Toolkit のテキスト要素は既定でリッチテキストのタグ（<c>&lt;size&gt;</c>・<c>&lt;color&gt;</c>・<c>&lt;br&gt;</c> など）を
    /// 解釈する。ほかの参加者やホストから届いた文字列（切断理由・プレイヤー名）をそのまま表示すると、
    /// 大きな文字や色・改行で画面を崩せてしまうため、それらを表示する要素には必ずこれを通す。
    /// このアプリの文言はタグを使っていない（#206 で確認）ので、自前の文言と混ぜて表示する要素に使っても見た目は変わらない。
    /// </summary>
    public static class PlainText
    {
        /// <summary>
        /// <paramref name="element"/> がリッチテキストのタグを解釈しないようにする（<see cref="TextElement.enableRichText"/> = false）。
        /// </summary>
        /// <returns><paramref name="element"/>（null ならそのまま null）。</returns>
        public static T Apply<T>(T element) where T : TextElement
        {
            if (element != null)
            {
                element.enableRichText = false;
            }

            return element;
        }

        /// <summary>リッチテキストのタグを解釈しない <see cref="Label"/> を作る。</summary>
        public static Label CreateLabel(string text) => Apply(new Label(text));
    }
}
