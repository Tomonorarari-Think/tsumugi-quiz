using System;
using System.Collections.Generic;
using System.Globalization;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 文字列の共通検証規則（docs/network.md §9）。
    /// プレイヤー名・回答など、クライアントから届く文字列の境界検証で使う。
    ///
    /// 見えない文字と積み重ねた結合記号の規則（<see cref="RemoveHiddenCharacters(string)"/>、issue #209）は、
    /// 承認時の名前の検証（<c>PlayerNameValidator</c>）・表示の整形（<see cref="DisplayTextSanitizer"/>）・
    /// 問題データの表示（<c>GameViewPresenter.ToQuestionDisplayText</c>）が共有する。判定をここ 1 か所にまとめ、
    /// 承認を通った名前が表示で別の名前に変わらないようにする。
    /// </summary>
    public static class TextRules
    {
        /// <summary>基底文字 1 つあたりに残す（承認する）結合記号の数の上限。</summary>
        public const int MaxCombiningMarksPerBase = 4;

        private const int ZeroWidthNonJoiner = 0x200C;
        private const int ZeroWidthJoiner = 0x200D;
        private const int WavingBlackFlag = 0x1F3F4;
        private const int FirstTagSpec = 0xE0020;
        private const int CancelTag = 0xE007F;

        /// <summary>
        /// 残すタグの並び。推奨される絵文字のタグの並び（Unicode 18.0 `emoji-sequences.txt` の RGI_Emoji_Tag_Sequence）の
        /// 3 つだけで、どれも U+1F3F4 の後にこの 6 文字が続く（PR #211 レビュー L-3）。
        /// </summary>
        private static readonly int[][] RgiFlagTagSequences =
        {
            new[] { 0xE0067, 0xE0062, 0xE0065, 0xE006E, 0xE0067, CancelTag }, // イングランド（gbeng）
            new[] { 0xE0067, 0xE0062, 0xE0073, 0xE0063, 0xE0074, CancelTag }, // スコットランド（gbsct）
            new[] { 0xE0067, 0xE0062, 0xE0077, 0xE006C, 0xE0073, CancelTag }, // ウェールズ（gbwls）
        };

        /// <summary>
        /// Prepended_Concatenation_Mark（UCD 18.0 `PropList.txt`）。カテゴリ Cf だが、後ろの数字の上に付く目に見える記号なので残す
        /// （PR #211 レビュー L-1）。
        /// </summary>
        private static bool IsPrependedConcatenationMark(int scalar)
            => (scalar >= 0x0600 && scalar <= 0x0605)
               || scalar == 0x06DD
               || scalar == 0x070F
               || (scalar >= 0x0890 && scalar <= 0x0891)
               || scalar == 0x08E2
               || scalar == 0x110BD
               || scalar == 0x110CD;

        /// <summary>
        /// 制御文字（U+0000〜U+001F、U+007F、および Unicode カテゴリ Control）を含むか。
        /// </summary>
        /// <param name="value">検証対象。null / 空文字は false。</param>
        /// <returns>制御文字を含むなら true。</returns>
        public static bool ContainsControlCharacter(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (var c in value)
            {
                if (c <= '\u001F' || c == '\u007F')
                {
                    return true;
                }

                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Control)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// <see cref="RemoveHiddenCharacters(string)"/> で取り除かれる文字を含むか（承認時の名前の検証で使う）。
        /// </summary>
        /// <param name="value">検証対象。null / 空文字は false。</param>
        public static bool ContainsHiddenCharacters(string value)
            => !string.IsNullOrEmpty(value)
               && !string.Equals(RemoveHiddenCharacters(value), value, StringComparison.Ordinal);

        /// <summary>
        /// 見えない文字と積み重ねた結合記号を取り除く（issue #209、docs/network.md §9）。
        /// 改行・空白・制御文字には手を入れず、長さも変えない（それは呼び出し側の役目）。
        /// <list type="number">
        /// <item>見えない文字（<see cref="IsInvisible"/>）を除く: 書式文字（カテゴリ Cf。ZWJ・ZWNJ・タグ文字・
        /// Prepended_Concatenation_Mark を除く）、見えない結合記号（U+034F、U+17B4、U+17B5）、空白に見えるハングルの字母
        /// （U+115F、U+1160、U+3164、U+FFA0）など</item>
        /// <item>タグ文字（U+E0020〜U+E007F）は、RGI の旗の 3 つの並び（U+1F3F4 + 6 文字）と完全に一致する場合だけ残し、
        /// ほかはすべて除く</item>
        /// <item>つなぐ相手のない ZWJ / ZWNJ（先頭・末尾・空白の隣）を除く</item>
        /// <item>結合記号（Mn / Mc / Me）は、基底文字 1 つあたり <see cref="MaxCombiningMarksPerBase"/> 個まで残す。
        /// 基底文字のない結合記号（先頭・空白の直後）は除く。残す ZWJ / ZWNJ / タグ文字は基底文字に数えない</item>
        /// </list>
        /// 同じ文字列に 2 回当てても結果は変わらない。
        /// </summary>
        /// <param name="value">対象。null 可。</param>
        /// <returns>取り除いた文字列。null なら空文字。</returns>
        public static string RemoveHiddenCharacters(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return string.Concat(RemoveHiddenCharacters(SplitCodePoints(value)));
        }

        /// <summary>
        /// <see cref="RemoveHiddenCharacters(string)"/> のコードポイント列版（<see cref="DisplayTextSanitizer"/> が使う）。
        /// </summary>
        /// <param name="codePoints">1 要素 1 コードポイント（サロゲートペアは 1 要素、単独のサロゲートは 1 文字の要素）。</param>
        internal static List<string> RemoveHiddenCharacters(IReadOnlyList<string> codePoints)
            => RemoveTrailingDanglingJoiners(RemoveInvisibleAndExcessMarks(codePoints));

        /// <summary>文字列をコードポイントの列に分ける。単独のサロゲートはそのまま 1 要素にする。</summary>
        internal static List<string> SplitCodePoints(string value)
        {
            var result = new List<string>(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    result.Add(value.Substring(i, 2));
                    i++;
                }
                else
                {
                    result.Add(value[i].ToString());
                }
            }

            return result;
        }

        /// <summary>結合記号（Unicode カテゴリ Mn / Mc / Me）か。</summary>
        internal static bool IsCombiningMark(string codePoint)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(codePoint, 0);
            return category == UnicodeCategory.NonSpacingMark
                   || category == UnicodeCategory.SpacingCombiningMark
                   || category == UnicodeCategory.EnclosingMark;
        }

        /// <summary>ZWJ（U+200D）か ZWNJ（U+200C）か。</summary>
        internal static bool IsJoiner(string codePoint)
        {
            var scalar = ToScalar(codePoint);
            return scalar == ZeroWidthJoiner || scalar == ZeroWidthNonJoiner;
        }

        /// <summary>タグ文字（U+E0020〜U+E007F）か。</summary>
        internal static bool IsTagCharacter(string codePoint)
        {
            var scalar = ToScalar(codePoint);
            return scalar >= FirstTagSpec && scalar <= CancelTag;
        }

        /// <summary>空白（<see cref="char.IsWhiteSpace(char)"/>。改行・全角空白・NBSP を含む）か。</summary>
        internal static bool IsWhiteSpace(string codePoint) => codePoint.Length == 1 && char.IsWhiteSpace(codePoint[0]);

        /// <summary>コードポイントの値。単独のサロゲートはその UTF-16 の値。</summary>
        internal static int ToScalar(string codePoint)
            => codePoint.Length == 2 ? char.ConvertToUtf32(codePoint[0], codePoint[1]) : codePoint[0];

        /// <summary>
        /// 取り除く見えない文字か。Unicode の Default_Ignorable_Code_Point（DerivedCoreProperties.txt）を基準に、
        /// 名前や絵文字に要るもの（ZWJ・ZWNJ・異体字セレクタ・旗のタグ文字）を残す。
        /// 実行環境の Unicode のカテゴリ表が古くても判定が変わらないよう、主なものは値で挙げる。
        /// </summary>
        private static bool IsInvisible(string codePoint)
        {
            var scalar = ToScalar(codePoint);
            switch (scalar)
            {
                case 0x00AD: // SOFT HYPHEN（行末でだけ見える）
                case 0x034F: // COMBINING GRAPHEME JOINER
                case 0x061C: // ARABIC LETTER MARK
                case 0x115F: // HANGUL CHOSEONG FILLER
                case 0x1160: // HANGUL JUNGSEONG FILLER
                case 0x17B4: // KHMER VOWEL INHERENT AQ
                case 0x17B5: // KHMER VOWEL INHERENT AA
                case 0x180E: // MONGOLIAN VOWEL SEPARATOR
                case 0x200B: // ZERO WIDTH SPACE
                case 0x200E: // LEFT-TO-RIGHT MARK
                case 0x200F: // RIGHT-TO-LEFT MARK
                case 0x3164: // HANGUL FILLER
                case 0xFEFF: // ZERO WIDTH NO-BREAK SPACE（BOM）
                case 0xFFA0: // HALFWIDTH HANGUL FILLER
                    return true;
            }

            if ((scalar >= 0x202A && scalar <= 0x202E) // 双方向の埋め込み・上書き
                || (scalar >= 0x2060 && scalar <= 0x206F) // WORD JOINER・見えない演算子・双方向の分離・廃止された書式文字
                || (scalar >= 0xFFF0 && scalar <= 0xFFFB) // 行間注記の記号など
                || (scalar >= 0x1BCA0 && scalar <= 0x1BCA3) // 速記の書式文字
                || (scalar >= 0x1D173 && scalar <= 0x1D17A)) // 楽譜の書式文字
            {
                return true;
            }

            if (scalar >= 0xE0000 && scalar <= 0xE0FFF)
            {
                // タグ文字（旗）と異体字セレクタの補助（U+E0100〜U+E01EF）以外は見えない。
                var isTag = scalar >= FirstTagSpec && scalar <= CancelTag;
                var isVariationSelector = scalar >= 0xE0100 && scalar <= 0xE01EF;
                return !isTag && !isVariationSelector;
            }

            // 上に挙げていない書式文字（カテゴリ Cf）も、ZWJ・ZWNJ・目に見える Prepended_Concatenation_Mark 以外は除く。
            return CharUnicodeInfo.GetUnicodeCategory(codePoint, 0) == UnicodeCategory.Format
                   && scalar != ZeroWidthJoiner
                   && scalar != ZeroWidthNonJoiner
                   && !IsPrependedConcatenationMark(scalar);
        }

        /// <summary>規則 1・2・4 と、直前につなぐ相手のない ZWJ / ZWNJ の除去（規則 3 の前半）。</summary>
        private static List<string> RemoveInvisibleAndExcessMarks(IReadOnlyList<string> codePoints)
        {
            var result = new List<string>(codePoints.Count);
            var hasBase = false;
            var marksAfterBase = 0;
            for (var i = 0; i < codePoints.Count; i++)
            {
                var codePoint = codePoints[i];
                if (IsInvisible(codePoint))
                {
                    continue;
                }

                if (IsTagCharacter(codePoint))
                {
                    // RGI の旗の並びは U+1F3F4 のところでまとめて足す。それ以外のタグ文字は見えないので除く。
                    continue;
                }

                if (ToScalar(codePoint) == WavingBlackFlag && TryMatchRgiFlagTags(codePoints, i + 1, out var tagCount))
                {
                    result.Add(codePoint);
                    for (var t = 1; t <= tagCount; t++)
                    {
                        result.Add(codePoints[i + t]);
                    }

                    i += tagCount;
                    hasBase = true;
                    marksAfterBase = 0;
                    continue;
                }

                if (IsJoiner(codePoint))
                {
                    if (result.Count > 0 && !IsWhiteSpace(result[result.Count - 1]))
                    {
                        result.Add(codePoint);
                    }

                    continue;
                }

                if (IsWhiteSpace(codePoint))
                {
                    hasBase = false;
                    marksAfterBase = 0;
                }
                else if (IsCombiningMark(codePoint))
                {
                    if (!hasBase || marksAfterBase >= MaxCombiningMarksPerBase)
                    {
                        continue;
                    }

                    marksAfterBase++;
                }
                else
                {
                    hasBase = true;
                    marksAfterBase = 0;
                }

                result.Add(codePoint);
            }

            return result;
        }

        /// <summary><paramref name="start"/> から RGI の旗のタグの並びのどれかと完全に一致するか。</summary>
        private static bool TryMatchRgiFlagTags(IReadOnlyList<string> codePoints, int start, out int tagCount)
        {
            foreach (var sequence in RgiFlagTagSequences)
            {
                if (start + sequence.Length > codePoints.Count)
                {
                    continue;
                }

                var matched = true;
                for (var k = 0; k < sequence.Length && matched; k++)
                {
                    matched = ToScalar(codePoints[start + k]) == sequence[k];
                }

                if (matched)
                {
                    tagCount = sequence.Length;
                    return true;
                }
            }

            tagCount = 0;
            return false;
        }

        /// <summary>規則 3 の後半: 直後につなぐ相手のない（末尾・空白の直前の）ZWJ / ZWNJ を除く。</summary>
        private static List<string> RemoveTrailingDanglingJoiners(List<string> codePoints)
        {
            var reversed = new List<string>(codePoints.Count);
            var nextCanBeJoined = false;
            for (var i = codePoints.Count - 1; i >= 0; i--)
            {
                var codePoint = codePoints[i];
                if (IsJoiner(codePoint) && !nextCanBeJoined)
                {
                    continue;
                }

                reversed.Add(codePoint);
                nextCanBeJoined = !IsWhiteSpace(codePoint);
            }

            reversed.Reverse();
            return reversed;
        }
    }
}
