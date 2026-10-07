using System.Collections.Generic;

namespace TsumugiQuiz.Core.TextLayout
{
    /// <summary>
    /// 日本語の文を「改行してよい位置」で区切る（文節に近い単位。issue #199、docs/architecture.md §10.10）。
    ///
    /// UI Toolkit の Advanced Text Generator は日本語を文字単位で折り返し、<c>&lt;nobr&gt;</c> タグも
    /// WORD JOINER（U+2060）も折り返しの抑止に効かない（#199 の実測）。そのため動的な文言は、ここで求めた区切りの
    /// 位置にだけ UI 側で改行を入れる（<see cref="PhraseLineComposer"/>）。
    ///
    /// 辞書を持たない簡易な規則なので、必ず文節で区切れるわけではない。区切ってよいのは次の位置だけ:
    /// <list type="number">
    /// <item>句読点（、。，．！？）の後（後ろが閉じ括弧・句読点・空白のときを除く）</item>
    /// <item>開き括弧の前（前が開き括弧・空白のときを除く）</item>
    /// <item>閉じ括弧の後（後ろがひらがな・閉じ括弧・句読点・空白のときを除く。「表示」から のように助詞は前に付ける）</item>
    /// <item>空白の後（後ろが英字のときだけ。「ONNX Runtime の」「最大 60 秒」は区切らない）</item>
    /// <item>助詞・活用語尾になりやすいひらがな（<see cref="IsParticleLike"/>）の後で、後ろが漢字・カタカナ・英数字のとき
    ///       （「参加コードの|長さが」。「読み上げ」「起動し直して」のように送り仮名の後では区切らない）</item>
    /// </list>
    /// 上のどれにも当たらない位置（漢字・カタカナの連続、ひらがなの連続など）では区切らない。
    /// 閉じ括弧・句読点・<see cref="NoBreakBeforeSymbols"/> の記号の前と、サロゲートペアの間では区切らない（行頭禁則）。
    /// 区切った各片を連結すると元の文字列に戻る。純粋関数で、Unity API に依存しない。
    /// </summary>
    public static class PhraseSegmenter
    {
        private enum CharClass
        {
            Other,
            Hiragana,
            Katakana,
            Kanji,
            LatinLetter,
            Digit,
            Space,
            OpeningBracket,
            ClosingBracket,
            SentencePunctuation,

            /// <summary>行頭に置かないが、後ろで区切る理由にはならない記号（<see cref="NoBreakBeforeSymbols"/>）。</summary>
            NoBreakBefore,
        }

        private const string OpeningBrackets = "（「『【〔［｛〈《〘〖([{‘“";
        private const string ClosingBrackets = "）」』】〕］｝〉》〙〗)]}’”";
        private const string SentencePunctuations = "、。，．！？";

        /// <summary>
        /// 前で区切らない記号（行頭禁則。#199 レビュー L-1）: ASCII の . , ! ? : ; と … ： ％ ・。
        /// 中黒「・」はカタカナの範囲（U+30FB）にあるが、カタカナではなくこの記号として扱う。
        /// </summary>
        private const string NoBreakBeforeSymbols = ".,!?:;…：％・";

        /// <summary>
        /// 後ろに漢字・カタカナ・英数字が続くとき、その前で区切ってよいひらがな（助詞・接続助詞と、連用形の「く」
        /// （「しばらく|待って」「正しく|入力」）になりやすい字）。
        /// 「し」「み」「げ」「い」などの送り仮名は含めない（「起動し直して」「読み上げ」「使い方」を分けないため）。
        /// </summary>
        private const string ParticleLikeHiragana = "をがはのでにとへもやかてばらく";

        /// <summary>
        /// <paramref name="text"/> を改行してよい位置で区切った片の一覧を返す。null・空文字なら空の一覧。
        /// 改行文字（\n）はそのまま片の中に残る（呼び出し側で段落ごとに分けてから渡すこと）。
        /// </summary>
        public static IReadOnlyList<string> Segment(string text)
        {
            var segments = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return segments;
            }

            var start = 0;
            var previous = Classify(text[0], CharClass.Other);
            for (var i = 1; i < text.Length; i++)
            {
                var current = Classify(text[i], previous);

                // サロゲートペアの間では区切らない（下位サロゲートの前は区切りにしない）。
                if (!char.IsLowSurrogate(text[i]) && CanBreakBetween(text[i - 1], previous, current))
                {
                    segments.Add(text.Substring(start, i - start));
                    start = i;
                }

                previous = current;
            }

            segments.Add(text.Substring(start));
            return segments;
        }

        private static bool CanBreakBetween(char before, CharClass left, CharClass right)
        {
            switch (left)
            {
                case CharClass.SentencePunctuation:
                    return !IsTrailing(right) && right != CharClass.Space;
                case CharClass.ClosingBracket:
                    return !IsTrailing(right) && right != CharClass.Hiragana && right != CharClass.Space;
                case CharClass.Space:
                    return right == CharClass.LatinLetter;
                case CharClass.Hiragana:
                    if (right == CharClass.OpeningBracket)
                    {
                        return true;
                    }

                    return IsParticleLike(before) && IsPhraseHead(right);
                case CharClass.OpeningBracket:
                    return false;
                default:
                    return right == CharClass.OpeningBracket;
            }
        }

        /// <summary>行頭に置かない（前の片に付ける）文字の種類。</summary>
        private static bool IsTrailing(CharClass c)
            => c == CharClass.ClosingBracket || c == CharClass.SentencePunctuation || c == CharClass.NoBreakBefore;

        /// <summary>助詞の後で新しい文節の頭になりうる文字の種類。</summary>
        private static bool IsPhraseHead(CharClass c)
            => c == CharClass.Kanji || c == CharClass.Katakana || c == CharClass.LatinLetter || c == CharClass.Digit;

        /// <summary>助詞・接続助詞になりやすいひらがなか（<see cref="ParticleLikeHiragana"/>）。</summary>
        private static bool IsParticleLike(char c) => ParticleLikeHiragana.IndexOf(c) >= 0;

        /// <summary>
        /// 文字の種類を返す。長音符「ー」と繰り返し記号「々」は直前の文字と同じ種類として扱う
        /// （「ルーム」「人々」を分けないため）。
        /// </summary>
        private static CharClass Classify(char c, CharClass previous)
        {
            if (c == 'ー' || c == '々' || c == 'ゝ' || c == 'ゞ' || c == 'ヽ' || c == 'ヾ')
            {
                return previous == CharClass.Other ? CharClass.Kanji : previous;
            }

            if (c == ' ' || c == '　')
            {
                return CharClass.Space;
            }

            if (OpeningBrackets.IndexOf(c) >= 0)
            {
                return CharClass.OpeningBracket;
            }

            if (ClosingBrackets.IndexOf(c) >= 0)
            {
                return CharClass.ClosingBracket;
            }

            if (SentencePunctuations.IndexOf(c) >= 0)
            {
                return CharClass.SentencePunctuation;
            }

            if (NoBreakBeforeSymbols.IndexOf(c) >= 0)
            {
                return CharClass.NoBreakBefore;
            }

            if (c >= 'ぁ' && c <= 'ゟ')
            {
                return CharClass.Hiragana;
            }

            if ((c >= '゠' && c <= 'ヿ') || (c >= 'ㇰ' && c <= 'ㇿ') || (c >= 'ｦ' && c <= 'ﾟ'))
            {
                return CharClass.Katakana;
            }

            if ((c >= '一' && c <= '鿿') || (c >= '㐀' && c <= '䶿') || (c >= '豈' && c <= '﫿')
                || c == '〆' || c == '〇')
            {
                return CharClass.Kanji;
            }

            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= 'Ａ' && c <= 'Ｚ') || (c >= 'ａ' && c <= 'ｚ'))
            {
                return CharClass.LatinLetter;
            }

            if ((c >= '0' && c <= '9') || (c >= '０' && c <= '９'))
            {
                return CharClass.Digit;
            }

            return CharClass.Other;
        }
    }
}
