using System;
using System.Collections.Generic;
using System.Text;

namespace TsumugiQuiz.Core.TextLayout
{
    /// <summary>
    /// 動的な文言を、<see cref="PhraseSegmenter"/> の区切りの位置でだけ改行するよう、改行文字（\n）を入れた文字列を作る
    /// （issue #199、docs/architecture.md §10.10）。1 行に収まる片をできるだけ詰め、収まらなくなった片の前で改行する。
    ///
    /// 幅の測り方は呼び出し側から受け取る（UI 側は <c>TextElement.MeasureTextSize</c> で 1 行の幅を測る）。
    /// 1 つの片だけで幅を超えるときは、その片を 1 行に置く（片の中の折り返しは描画側に任せる。語の途中の改行は避けられない）。
    /// 文言は平文として扱う（<c>&lt;</c> も 1 文字として組む）。表示の側はリッチテキストを解釈させないこと
    /// （<c>PhraseWrappedText</c> が <c>enableRichText = false</c> にする。#206）。
    /// 純粋関数で、Unity API に依存しない。
    /// </summary>
    public static class PhraseLineComposer
    {
        /// <summary>
        /// 1 行に収まるかの判定に残す余白（px）。描画側は測った幅ちょうどの枠では折り返すことがある
        /// （#199 の実測: 見出しを自身の幅で測ると 2 行になった）ため、幅から差し引いて判定する。
        /// </summary>
        public const float FitTolerance = 1f;

        /// <summary>
        /// <paramref name="text"/> を幅 <paramref name="maxWidth"/> に収まるよう、区切りの位置に改行を入れて返す。
        /// </summary>
        /// <param name="text">表示する文言。既存の改行（\n）は段落の区切りとしてそのまま残す。</param>
        /// <param name="measureSingleLine">1 行で描いたときの幅を返す関数。</param>
        /// <param name="maxWidth">1 行の最大幅。0 以下・非数なら <paramref name="text"/> をそのまま返す。</param>
        /// <returns>
        /// 改行を入れた文字列。改行を入れた位置の行末の空白は削る（行頭・行末に空白を残さないため。
        /// そのため改行を除いても元の文言とは一致しないことがある）。null・空文字はそのまま返す。
        /// </returns>
        public static string Compose(string text, Func<string, float> measureSingleLine, float maxWidth)
        {
            if (measureSingleLine == null)
            {
                throw new ArgumentNullException(nameof(measureSingleLine));
            }

            if (string.IsNullOrEmpty(text) || float.IsNaN(maxWidth) || maxWidth <= 0f)
            {
                return text;
            }

            var paragraphs = text.Split('\n');
            var builder = new StringBuilder(text.Length + 8);
            for (var i = 0; i < paragraphs.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                AppendParagraph(builder, paragraphs[i], measureSingleLine, maxWidth - FitTolerance);
            }

            return builder.ToString();
        }

        private static void AppendParagraph(
            StringBuilder builder, string paragraph, Func<string, float> measureSingleLine, float limit)
        {
            var lines = new List<string>();
            var current = string.Empty;
            foreach (var segment in PhraseSegmenter.Segment(paragraph))
            {
                if (current.Length == 0)
                {
                    current = segment;
                    continue;
                }

                var candidate = current + segment;
                if (measureSingleLine(candidate.TrimEnd()) <= limit)
                {
                    current = candidate;
                }
                else
                {
                    lines.Add(current.TrimEnd());
                    current = segment;
                }
            }

            lines.Add(current);
            builder.Append(string.Join("\n", lines));
        }
    }
}
