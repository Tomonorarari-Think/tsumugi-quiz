using System;
using System.Collections.Generic;
using System.Globalization;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// ほかの参加者・ホストから届いた文字列を、画面・ログに出せる形に整える共通の規則（issue #206・#209、docs/network.md §9）。
    /// 切断理由（<see cref="Network.DisconnectReasonSanitizer"/>）と名簿の名前（<see cref="Network.PlayerDisplayNameSanitizer"/>）が使う。
    ///
    /// <list type="number">
    /// <item>改行・タブ（U+0009〜U+000D、U+0085、U+2028、U+2029）は半角空白にする
    /// （表示欄を改行だらけにさせない。語がつながらないよう、消さずに空白にする）</item>
    /// <item>それ以外の制御文字（<see cref="TextRules.ContainsControlCharacter"/> と同じ範囲）と、対になっていない
    /// サロゲートは取り除く</item>
    /// <item>見えない文字と積み重ねた結合記号を、承認時の名前の検証と同じ規則（<see cref="TextRules.RemoveHiddenCharacters(string)"/>）で
    /// 取り除く: 書式文字（双方向の制御・幅のない区切り・ソフトハイフンなど。絵文字や字形に要る ZWJ・ZWNJ・旗のタグ文字は残す）、
    /// 空白に見えるハングルの字母、つなぐ相手のない ZWJ / ZWNJ、基底文字のない結合記号、基底文字 1 つあたり
    /// <see cref="MaxCombiningMarksPerBase"/> 個を超えた結合記号（表示欄の上下にはみ出させないため）。異体字セレクタは残す</item>
    /// <item>半角空白の連続は 1 つにまとめる</item>
    /// <item>前後の空白（<see cref="char.IsWhiteSpace(string, int)"/>。全角空白 U+3000・NBSP を含む）を除く</item>
    /// <item>上限の文字数（<b>コードポイントの数</b>。承認時のプレイヤー名の数え方と同じ）を超えたら、上限から 1 引いた
    /// 文字数以内の位置で切り、切った位置の直前の空白を除いてから <see cref="Ellipsis"/> を付ける。サロゲートペア・
    /// 結合記号（Mn / Mc / Me）・肌色の修飾・旗のタグ文字の前、地域指示記号の対の間、ZWJ / ZWNJ の直後では切らない</item>
    /// </list>
    /// リッチテキストのタグ（<c>&lt;</c>）はここでは残す。タグを解釈させないのは表示の側の役目で、
    /// 表示先のラベルは <c>enableRichText = false</c> にする（docs/architecture.md §10.10）。
    /// 純粋関数で、Unity API に依存しない。
    /// </summary>
    public static class DisplayTextSanitizer
    {
        /// <summary>切り詰めたときに末尾へ付ける記号。</summary>
        public const string Ellipsis = "…";

        /// <summary>基底文字 1 つあたりに残す結合記号の数の上限（<see cref="TextRules.MaxCombiningMarksPerBase"/> と同じ）。</summary>
        public const int MaxCombiningMarksPerBase = TextRules.MaxCombiningMarksPerBase;

        private const int FirstEmojiModifier = 0x1F3FB;
        private const int LastEmojiModifier = 0x1F3FF;
        private const int FirstRegionalIndicator = 0x1F1E6;
        private const int LastRegionalIndicator = 0x1F1FF;

        /// <summary>
        /// 文字列を整える。
        /// </summary>
        /// <param name="text">受け取った文字列。null 可。</param>
        /// <param name="maxLength">上限の文字数（コードポイントの数）。1 以上。</param>
        /// <returns>整えた文字列。何も残らなければ空文字。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLength"/> が 1 未満のとき。</exception>
        public static string Sanitize(string text, int maxLength)
        {
            if (maxLength < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "上限は 1 以上にしてください。");
            }

            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var visible = TextRules.RemoveHiddenCharacters(SplitWithoutControls(text));
            return Truncate(TrimWhiteSpace(CollapseSpaces(visible)), maxLength);
        }

        /// <summary>規則 1・2 を当て、コードポイント（サロゲートペアは 1 要素）の列にする。</summary>
        private static List<string> SplitWithoutControls(string text)
        {
            var result = new List<string>(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        result.Add(text.Substring(i, 2));
                        i++;
                    }

                    continue;
                }

                if (char.IsLowSurrogate(c))
                {
                    continue;
                }

                if (IsSpaceOrLineBreak(c))
                {
                    result.Add(" ");
                }
                else if (!IsControl(c))
                {
                    result.Add(c.ToString());
                }
            }

            return result;
        }

        /// <summary>半角空白と、行を分ける文字（改行・タブ・改ページ・NEL・行区切り・段落区切り）。</summary>
        private static bool IsSpaceOrLineBreak(char c)
            => c == ' ' || (c >= '\u0009' && c <= '\u000D') || c == '\u0085' || c == '\u2028' || c == '\u2029';

        /// <summary><see cref="TextRules.ContainsControlCharacter"/> と同じ範囲の制御文字。</summary>
        private static bool IsControl(char c)
            => c <= '\u001F' || c == '\u007F' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Control;

        /// <summary>規則 4: 半角空白の連続を 1 つにまとめる（見えない文字を除いた後に当て、除いた文字を挟んだ空白もまとめる）。</summary>
        private static List<string> CollapseSpaces(List<string> codePoints)
        {
            var result = new List<string>(codePoints.Count);
            foreach (var codePoint in codePoints)
            {
                if (codePoint == " " && result.Count > 0 && result[result.Count - 1] == " ")
                {
                    continue;
                }

                result.Add(codePoint);
            }

            return result;
        }

        private static List<string> TrimWhiteSpace(List<string> codePoints)
        {
            var start = 0;
            while (start < codePoints.Count && TextRules.IsWhiteSpace(codePoints[start]))
            {
                start++;
            }

            var end = codePoints.Count;
            while (end > start && TextRules.IsWhiteSpace(codePoints[end - 1]))
            {
                end--;
            }

            return codePoints.GetRange(start, end - start);
        }

        private static string Truncate(List<string> codePoints, int maxLength)
        {
            if (codePoints.Count <= maxLength)
            {
                return string.Concat(codePoints);
            }

            // 上限から 1 引いた数（「…」の分）以内で、1 つの字形の途中ではない最後の位置で切る。
            var cut = maxLength - 1;
            while (cut > 0 && ContinuesPreviousCharacter(codePoints, cut))
            {
                cut--;
            }

            var head = TrimWhiteSpace(codePoints.GetRange(0, cut));
            return string.Concat(head) + Ellipsis;
        }

        /// <summary>
        /// <paramref name="index"/> の位置で切ると、直前の文字と組になる字形（結合文字・絵文字の並び）を分けてしまうか。
        /// </summary>
        private static bool ContinuesPreviousCharacter(List<string> codePoints, int index)
        {
            var codePoint = codePoints[index];
            if (TextRules.IsCombiningMark(codePoint)
                || TextRules.IsTagCharacter(codePoint)
                || TextRules.IsJoiner(codePoints[index - 1]))
            {
                return true;
            }

            var scalar = TextRules.ToScalar(codePoint);
            if (scalar >= FirstEmojiModifier && scalar <= LastEmojiModifier)
            {
                return true;
            }

            // 地域指示記号は 2 つで 1 つの旗になる。直前に奇数個続いていれば、対の途中。
            return IsRegionalIndicator(codePoint) && CountRegionalIndicatorsBefore(codePoints, index) % 2 == 1;
        }

        private static bool IsRegionalIndicator(string codePoint)
        {
            var scalar = TextRules.ToScalar(codePoint);
            return scalar >= FirstRegionalIndicator && scalar <= LastRegionalIndicator;
        }

        private static int CountRegionalIndicatorsBefore(List<string> codePoints, int index)
        {
            var count = 0;
            for (var i = index - 1; i >= 0 && IsRegionalIndicator(codePoints[i]); i--)
            {
                count++;
            }

            return count;
        }
    }
}
