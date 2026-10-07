using System;
using System.Globalization;

namespace TsumugiQuiz.Core.Reveal
{
    /// <summary>
    /// 文字送り表示のための「文字」の数え方と切り出し（issue #144）。
    /// </summary>
    /// <remarks>
    /// C# の <c>string.Length</c> は UTF-16 のコード単位数なので、サロゲートペア（例: 「𠮷」）や
    /// 結合文字を途中で切ると表示が壊れる。ここでは <see cref="StringInfo.ParseCombiningCharacters"/>
    /// が返すテキスト要素（利用者から見た 1 文字）の単位で数え、切り出す。
    /// </remarks>
    public static class RevealText
    {
        /// <summary>
        /// テキスト要素ごとの開始位置（UTF-16 インデックス）を返す。null・空文字なら空配列。
        /// </summary>
        /// <param name="text">対象の文字列。</param>
        /// <returns>各テキスト要素の開始位置（昇順）。長さが「文字数」になる。</returns>
        public static int[] GetTextElementStarts(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Array.Empty<int>();
            }

            return StringInfo.ParseCombiningCharacters(text);
        }

        /// <summary>
        /// 先頭から <paramref name="count"/> 文字（テキスト要素）を切り出す。
        /// </summary>
        /// <param name="text">対象の文字列。</param>
        /// <param name="elementStarts"><see cref="GetTextElementStarts"/> が返した開始位置。</param>
        /// <param name="count">表示する文字数。0 以下なら空文字、要素数以上なら全文。</param>
        /// <returns>切り出した文字列。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="elementStarts"/> が null のとき。</exception>
        public static string Take(string text, int[] elementStarts, int count)
        {
            if (elementStarts == null)
            {
                throw new ArgumentNullException(nameof(elementStarts));
            }

            if (string.IsNullOrEmpty(text) || count <= 0)
            {
                return string.Empty;
            }

            if (count >= elementStarts.Length)
            {
                return text;
            }

            return text.Substring(0, elementStarts[count]);
        }
    }
}
