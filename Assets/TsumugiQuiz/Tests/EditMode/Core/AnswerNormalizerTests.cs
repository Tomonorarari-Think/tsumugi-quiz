using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// docs/question-data.md §5 の正規化ルールを検証する。
    /// </summary>
    public class AnswerNormalizerTests
    {
        // docs/question-data.md §5 「正規化の例」表の11例をそのままテストケース化する。
        [TestCase("東京", "東京", TestName = "Normalize_01_KanjiIsUnchanged")]
        [TestCase("トウキョウ", "とうきょう", TestName = "Normalize_02_KatakanaToHiragana")]
        [TestCase("とうきょう", "とうきょう", TestName = "Normalize_03_HiraganaIsUnchanged")]
        [TestCase(" Tokyo ", "tokyo", TestName = "Normalize_04_TrimAndLowercase")]
        [TestCase("ｔｏｋｙｏ", "tokyo", TestName = "Normalize_05_FullWidthAlphaToLowercase")]
        [TestCase("パソコン", "ぱそこん", TestName = "Normalize_06_DakutenHandakutenPreserved")]
        [TestCase("ヴァイオリン", "ゔぁいおりん", TestName = "Normalize_07_VuToHiraganaVu")]
        [TestCase("コーヒー", "こーひー", TestName = "Normalize_08_ChoonKeptAsIs")]
        [TestCase("がっこう", "がっこう", TestName = "Normalize_09_SmallTsuPreserved")]
        [TestCase("東京　です", "東京です", TestName = "Normalize_10_FullWidthSpaceRemoved")]
        [TestCase("とう​きょう", "とうきょう", TestName = "Normalize_11_ZeroWidthSpaceRemoved")]
        public void Normalize_QuestionDataExamples_MatchExpected(string input, string expected)
        {
            Assert.That(AnswerNormalizer.Normalize(input), Is.EqualTo(expected));
        }

        [Test]
        public void Normalize_Null_ReturnsEmptyWithoutThrowing()
        {
            Assert.That(AnswerNormalizer.Normalize(null), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Normalize_Empty_ReturnsEmpty()
        {
            Assert.That(AnswerNormalizer.Normalize(string.Empty), Is.EqualTo(string.Empty));
        }

        // 半角カナは NFKC で全角へ正規化された上でひらがな変換される（濁点・半濁点・長音は結合される）。
        [TestCase("ｶﾞｯｺｳ", "がっこう", TestName = "Normalize_HalfWidthKatakana_Gakkou")]
        [TestCase("ﾊﾟｿｺﾝ", "ぱそこん", TestName = "Normalize_HalfWidthKatakana_Pasokon")]
        [TestCase("ｺｰﾋｰ", "こーひー", TestName = "Normalize_HalfWidthKatakana_Koohii")]
        public void Normalize_HalfWidthKatakana_ConvertsToHiragana(string input, string expected)
        {
            Assert.That(AnswerNormalizer.Normalize(input), Is.EqualTo(expected));
        }

        // カタカナ→ひらがな変換範囲（U+30A1〜U+30F6）の境界確認。
        // 長音記号「ー」（U+30FC）は範囲外のため変換されない。
        // 「ヶ」（U+30F6、範囲の終端）は「ゖ」（U+3096）へ、「ァ」（U+30A1、範囲の始端）は「ぁ」（U+3041）へ変換される。
        // 「ヷ」（U+30F7）は範囲外のためカタカナのまま変換されない。
        [TestCase("ー", "ー", TestName = "Normalize_RangeBoundary_ChoonIsUnchanged")]
        [TestCase("ヶ", "ゖ", TestName = "Normalize_RangeBoundary_KeToSmallKe")]
        [TestCase("ァ", "ぁ", TestName = "Normalize_RangeBoundary_SmallAToHiragana")]
        [TestCase("ヷ", "ヷ", TestName = "Normalize_RangeBoundary_VaIsUnchanged")]
        public void Normalize_KatakanaRangeBoundary_MatchesExpected(string input, string expected)
        {
            Assert.That(AnswerNormalizer.Normalize(input), Is.EqualTo(expected));
        }

        [Test]
        public void Normalize_TabAndNewline_AreRemoved()
        {
            Assert.That(AnswerNormalizer.Normalize("と\tう\nきょう"), Is.EqualTo("とうきょう"));
        }

        // ゼロ幅系・BOM 等の不可視の書式制御文字（Unicode カテゴリ Cf）が除去されることの確認。
        [TestCase("とう​きょう", "とうきょう", TestName = "Normalize_InvisibleFormat_ZeroWidthSpace")]
        [TestCase("とう‌きょう", "とうきょう", TestName = "Normalize_InvisibleFormat_ZeroWidthNonJoiner")]
        [TestCase("とう‍きょう", "とうきょう", TestName = "Normalize_InvisibleFormat_ZeroWidthJoiner")]
        [TestCase("﻿とうきょう", "とうきょう", TestName = "Normalize_InvisibleFormat_Bom")]
        [TestCase("と­うきょう", "とうきょう", TestName = "Normalize_InvisibleFormat_SoftHyphen")]
        public void Normalize_InvisibleFormatCharacters_AreRemoved(string input, string expected)
        {
            Assert.That(AnswerNormalizer.Normalize(input), Is.EqualTo(expected));
        }

        // 濁点・半濁点・小書き文字・長音表記の差はあいまい一致にしない（区別する）ことの確認。
        [TestCase("がっこう", "がこう", TestName = "Normalize_SmallTsuVsPlain_AreDifferent")]
        [TestCase("は", "ば", TestName = "Normalize_PlainVsDakuten_AreDifferent")]
        [TestCase("は", "ぱ", TestName = "Normalize_PlainVsHandakuten_AreDifferent")]
        [TestCase("こーひー", "こうひい", TestName = "Normalize_ChoonVsVowelSpelledOut_AreDifferent")]
        public void Normalize_DistinctInputs_DoNotNormalizeToSameValue(string a, string b)
        {
            Assert.That(AnswerNormalizer.Normalize(a), Is.Not.EqualTo(AnswerNormalizer.Normalize(b)));
        }
    }
}
