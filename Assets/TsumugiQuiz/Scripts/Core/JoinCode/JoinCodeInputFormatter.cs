using System;
using System.Text;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// Join View の参加コード入力欄が、入力のたびに行う「正規化 → 長さ判定 → ハイフン整形 →
    /// キャレット位置の再計算」をまとめて行う（issue #6 レビュー M-9）。Unity API に依存しない純 C# で
    /// 実装し、EditMode でキャレット位置（H-2）・12 文字超過時の表示（M-4）を直接検証できるようにする。
    /// </summary>
    public static class JoinCodeInputFormatter
    {
        /// <summary>
        /// 入力中の参加コードを整形する。
        /// </summary>
        /// <param name="rawInput">入力欄の現在値（整形前）。null は空文字として扱う。</param>
        /// <param name="rawCaretIndex">整形前の <paramref name="rawInput"/> 上でのキャレット位置。</param>
        /// <returns>整形結果（表示文字列・キャレット位置・エラー・デコード結果）。</returns>
        public static JoinCodeInputFormatResult Format(string rawInput, int rawCaretIndex)
        {
            rawInput ??= string.Empty;
            rawCaretIndex = Clamp(rawCaretIndex, 0, rawInput.Length);

            if (!JoinCodeCodec.TryNormalizePartial(rawInput, out var normalized))
            {
                // M-4: 不正な Unicode（対になっていないサロゲートなど）で正規化自体に失敗した場合は、
                // 入力値を勝手に消さず、キャレット位置もそのままに InvalidCharacter を表示する。
                return new JoinCodeInputFormatResult(rawInput, rawCaretIndex, JoinCodeError.InvalidCharacter, null);
            }

            // H-2: キャレットの「論理位置」（区切り文字を除いた、正規化後の文字を何文字分読んだか）を、
            // 整形前の生入力上でのキャレット位置から求める。ハイフン・空白の除去は文字数を変えるだけで
            // 順序を変えないため、生入力側で区切り文字を除いて数えれば正規化後の位置と一致する。
            var logicalCaretIndex = CountNonSeparatorChars(rawInput, rawCaretIndex);

            JoinCodeError? error;
            (int Version, string Ip, int Port)? decodedEndpoint = null;

            if (normalized.Length > JoinCodeCodec.CodeLength)
            {
                // M-4: 12 文字を超えて入力された場合も、切り捨てて表示するだけでなく
                // InvalidLength として明示的にエラー表示する。
                error = JoinCodeError.InvalidLength;
            }
            else if (normalized.Length < JoinCodeCodec.CodeLength)
            {
                // 入力途中。まだエラーとしては扱わない。
                error = null;
            }
            else if (JoinCodeCodec.TryDecode(normalized, out var result, out var decodeError))
            {
                error = null;
                decodedEndpoint = result;
            }
            else
            {
                error = decodeError;
            }

            var truncated = normalized.Length > JoinCodeCodec.CodeLength
                ? normalized.Substring(0, JoinCodeCodec.CodeLength)
                : normalized;

            var clampedLogicalIndex = Math.Min(logicalCaretIndex, truncated.Length);

            var displayText = FormatWithHyphens(truncated);
            var caretIndex = ToPhysicalIndex(clampedLogicalIndex);

            return new JoinCodeInputFormatResult(displayText, caretIndex, error, decodedEndpoint);
        }

        /// <summary>
        /// <paramref name="raw"/> の先頭から <paramref name="exclusiveEndIndex"/> 文字目までのうち、
        /// 参加コードの区切り文字（ハイフン・空白）でない文字数を数える。
        /// </summary>
        private static int CountNonSeparatorChars(string raw, int exclusiveEndIndex)
        {
            var count = 0;
            var limit = Math.Min(exclusiveEndIndex, raw.Length);
            for (var i = 0; i < limit; i++)
            {
                var ch = raw[i];
                if (ch != JoinCodeCodec.GroupSeparator && !char.IsWhiteSpace(ch))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>正規化済みの文字列を表示形式（XXXX-XXXX-XXXX）へ整形する。</summary>
        private static string FormatWithHyphens(string normalized)
        {
            var separatorCount = normalized.Length == 0 ? 0 : (normalized.Length - 1) / JoinCodeCodec.GroupLength;
            var builder = new StringBuilder(normalized.Length + separatorCount);

            for (var i = 0; i < normalized.Length; i++)
            {
                if (i > 0 && i % JoinCodeCodec.GroupLength == 0)
                {
                    builder.Append(JoinCodeCodec.GroupSeparator);
                }

                builder.Append(normalized[i]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// 正規化後の文字を <paramref name="logicalIndex"/> 文字分読み終えた位置が、
        /// ハイフンを含む表示文字列上では何文字目になるかを求める（H-2）。
        /// <see cref="FormatWithHyphens"/> と同じ規則（4 文字ごとにハイフンを 1 つ挿入）に従う。
        /// </summary>
        private static int ToPhysicalIndex(int logicalIndex)
        {
            var separatorsBefore = logicalIndex > 0 ? (logicalIndex - 1) / JoinCodeCodec.GroupLength : 0;
            return logicalIndex + separatorsBefore;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
