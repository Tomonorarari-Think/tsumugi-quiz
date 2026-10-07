using System.Globalization;
using System.Text;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 自由入力回答の正規化ロジック。
    /// 手順（docs/question-data.md §5）:
    /// 1. Unicode 正規化 NFKC
    /// 2. カタカナ（U+30A1〜U+30F6）をひらがなへ変換（-0x60。ヴ→ゔ も同じ規則でカバーされる）
    /// 3. 英字を小文字化する
    /// 4. 空白文字および不可視の書式制御文字（ゼロ幅スペース・BOM 等、Unicode カテゴリ Cf）を
    ///    すべて除去する
    ///
    /// 長音記号「ー」・濁点/半濁点・小書き文字はそれぞれ独立した文字として保持し、
    /// あいまい一致（同一視）は行わない。
    /// </summary>
    public static class AnswerNormalizer
    {
        private const char KatakanaRangeStart = 'ァ';
        private const char KatakanaRangeEnd = 'ヶ';
        private const int KatakanaToHiraganaOffset = 0x60;

        // ソフトハイフン（U+00AD）。Unicode 上は Cf（Format）カテゴリだが、
        // Unity（Mono）のランタイムでは char.GetUnicodeCategory がこれを Format と
        // 判定しないため、明示的に除去対象へ加える。
        private const char SoftHyphen = '\u00AD';

        /// <summary>
        /// 入力文字列を正規化する。null は空文字として扱い、例外は送出しない。
        /// </summary>
        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            // 1. NFKC 正規化（全角英数・記号を半角へ、互換文字を統合）
            string nfkc = value.Normalize(NormalizationForm.FormKC);

            // 2〜4. カタカナ→ひらがな変換、小文字化、空白除去を1パスで行う
            var builder = new StringBuilder(nfkc.Length);
            foreach (char c in nfkc)
            {
                if (char.IsWhiteSpace(c)
                    || char.GetUnicodeCategory(c) == UnicodeCategory.Format
                    || c == SoftHyphen)
                {
                    // 4. 空白文字（半角/全角スペース・タブ等）および不可視の書式制御文字
                    //    （ゼロ幅スペース U+200B・ZWNJ・ZWJ・BOM 等の Cf、ソフトハイフンは
                    //    ランタイムによって Cf 判定されないため個別に除去する）を除去する
                    continue;
                }

                char converted = c;
                if (converted >= KatakanaRangeStart && converted <= KatakanaRangeEnd)
                {
                    // 2. カタカナをひらがなへ変換する（ヴ→ゔ もこの範囲・規則に含まれる）
                    converted = (char)(converted - KatakanaToHiraganaOffset);
                }

                // 3. 英字を小文字化する
                converted = char.ToLowerInvariant(converted);

                builder.Append(converted);
            }

            return builder.ToString();
        }
    }
}
