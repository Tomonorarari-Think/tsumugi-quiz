using System;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="DisplayTextSanitizer"/>（切断理由と名簿の名前で共通の表示用の整形、issue #206）を検証する。
    /// </summary>
    public class DisplayTextSanitizerTests
    {
        [TestCase(0)]
        [TestCase(-1)]
        public void Sanitize_MaxLengthBelowOne_Throws(int maxLength)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DisplayTextSanitizer.Sanitize("あ", maxLength));
        }

        [TestCase("あいう", 3, "あいう")]
        [TestCase("あいうえ", 3, "あい…")]
        [TestCase("あい", 1, "…")]
        [TestCase("あ", 1, "あ")]
        public void Sanitize_TruncatesToTheGivenLength(string text, int maxLength, string expected)
        {
            Assert.That(DisplayTextSanitizer.Sanitize(text, maxLength), Is.EqualTo(expected));
        }

        [Test]
        public void Sanitize_AppliesTheLimitAfterCleaning()
        {
            Assert.That(DisplayTextSanitizer.Sanitize("  あ\n\nい  ", 3), Is.EqualTo("あ い"));
        }

        [Test]
        public void Sanitize_CountsCodePoints_NotTextElements()
        {
            // #206 M-2: 結合記号もコードポイントとして数える（テキスト要素で数えると、積み重ねた結合記号が上限を素通りする）。
            // 「a + U+0301」は 2 文字。上限 5 なら 4 文字以内で、結合記号の途中ではない位置で切る。
            var text = string.Concat(Enumerable.Repeat("a\u0301", 5));

            Assert.That(DisplayTextSanitizer.Sanitize(text, 5), Is.EqualTo("a\u0301a\u0301" + DisplayTextSanitizer.Ellipsis));
        }

        [Test]
        public void Sanitize_DoesNotCutInsideSurrogatePairs()
        {
            Assert.That(DisplayTextSanitizer.Sanitize("𠮷𠮷𠮷𠮷", 3), Is.EqualTo("𠮷𠮷" + DisplayTextSanitizer.Ellipsis));
        }

        [Test]
        public void Sanitize_CombiningMarks_AreLimitedToFourPerBase()
        {
            var text = "a" + new string('\u0301', 10) + "b" + new string('\u0308', 3);

            Assert.That(
                DisplayTextSanitizer.Sanitize(text, 100),
                Is.EqualTo("a" + new string('\u0301', DisplayTextSanitizer.MaxCombiningMarksPerBase) + "b" + new string('\u0308', 3)));
        }

        [TestCase("\u202A")]
        [TestCase("\u202B")]
        [TestCase("\u202C")]
        [TestCase("\u202D")]
        [TestCase("\u202E")]
        [TestCase("\u2066")]
        [TestCase("\u2067")]
        [TestCase("\u2068")]
        [TestCase("\u2069")]
        [TestCase("\u200E")]
        [TestCase("\u200F")]
        [TestCase("\u061C")]
        [TestCase("\u200B")]
        [TestCase("\u2060")]
        [TestCase("\uFEFF")]
        public void Sanitize_BidiControlsAndZeroWidthBreaks_AreRemoved(string removed)
        {
            Assert.That(DisplayTextSanitizer.Sanitize("つむ" + removed + "ぎ", 16), Is.EqualTo("つむぎ"));
        }

        [TestCase("👨\u200D👩")]
        [TestCase("a\u200Cb")]
        [TestCase("葛\uFE00")]
        [TestCase("❤\uFE0F")]
        public void Sanitize_JoinersAndVariationSelectors_AreKept(string text)
        {
            Assert.That(DisplayTextSanitizer.Sanitize(text, 16), Is.EqualTo(text));
        }

        [Test]
        public void Sanitize_TagCharactersOfAFlag_AreKept()
        {
            // スコットランドの旗: U+1F3F4 + タグ文字（U+E0067 U+E0062 U+E0073 U+E0063 U+E0074）+ U+E007F。
            var flag = string.Concat(new[] { 0x1F3F4, 0xE0067, 0xE0062, 0xE0073, 0xE0063, 0xE0074, 0xE007F }.Select(char.ConvertFromUtf32));

            Assert.That(DisplayTextSanitizer.Sanitize(flag, 16), Is.EqualTo(flag));
        }

        [TestCase("\u3000つむぎ\u3000", "つむぎ")]
        [TestCase("\u00A0つむぎ\u00A0", "つむぎ")]
        [TestCase("つ\u00A0むぎ", "つ\u00A0むぎ")]
        public void Sanitize_TrimsUnicodeWhiteSpaceAtBothEnds(string text, string expected)
        {
            Assert.That(DisplayTextSanitizer.Sanitize(text, 16), Is.EqualTo(expected));
        }

        [TestCase("あい うえお", 4, "あい…")]
        [TestCase("あい\u3000うえお", 4, "あい…")]
        public void Sanitize_Truncation_RemovesWhiteSpaceBeforeTheEllipsis(string text, int maxLength, string expected)
        {
            Assert.That(DisplayTextSanitizer.Sanitize(text, maxLength), Is.EqualTo(expected));
        }

        [Test]
        public void Sanitize_CombiningMarksAcrossRemovedOrFormatCharacters_StillCountForTheSameBase()
        {
            // 除く文字（U+200B）や残す書式文字（ZWJ U+200D）を挟んでも、結合記号の数は戻らない。
            var text = "a" + new string('\u0301', 4) + "\u200B" + new string('\u0301', 4) + "\u200D" + new string('\u0301', 4) + "b";

            Assert.That(DisplayTextSanitizer.Sanitize(text, 100), Is.EqualTo("a" + new string('\u0301', 4) + "\u200Db"));
        }

        [TestCase("\u00AD")]
        [TestCase("\u2061")]
        [TestCase("\u206A")]
        [TestCase("\u180E")]
        [TestCase("\uFFF9")]
        [TestCase("\u034F")]
        [TestCase("\u3164")]
        [TestCase("\u115F")]
        [TestCase("\u1160")]
        [TestCase("\uFFA0")]
        public void Sanitize_OtherInvisibleCharacters_AreRemoved(string removed)
        {
            // #209: 双方向の並べ替えは起こさないが、見た目が同じ名前を作れる文字も除く（規則は TextRules と共有）。
            Assert.That(DisplayTextSanitizer.Sanitize("つむ" + removed + "ぎ", 16), Is.EqualTo("つむぎ"));
        }

        [TestCase("つむぎ\u200D", "つむぎ")]
        [TestCase("\u200Cつむぎ", "つむぎ")]
        [TestCase(" \u200D つむぎ \u200C ", "つむぎ")]
        [TestCase("  \u0301つむぎ", "つむぎ")]
        [TestCase("\n\u0301\u0301つむぎ", "つむぎ")]
        public void Sanitize_JoinersAndMarksLeftAtTheEdges_AreRemoved(string text, string expected)
        {
            // #209: 前後の空白を除いた後に、つなぐ相手のない ZWJ / ZWNJ や、基底文字のない結合記号を残さない。
            Assert.That(DisplayTextSanitizer.Sanitize(text, 16), Is.EqualTo(expected));
        }

        [Test]
        public void Sanitize_Truncation_BacksOffOverSeveralCombiningMarks()
        {
            // #209 コメント: 切る位置から結合記号の上を 2 つ以上戻る。
            Assert.That(DisplayTextSanitizer.Sanitize("ab\u0301\u0301\u0301c", 4), Is.EqualTo("a" + DisplayTextSanitizer.Ellipsis));
        }

        [Test]
        public void Sanitize_Truncation_BackingOffToTheStart_LeavesOnlyTheEllipsis()
        {
            // #209 コメント: 戻った結果、先頭まで戻ったら「…」だけになる。
            Assert.That(DisplayTextSanitizer.Sanitize("a\u0301\u0301\u0301b", 3), Is.EqualTo(DisplayTextSanitizer.Ellipsis));
        }

        [TestCase("あ👍\U0001F3FDい", 3, "あ…")]
        [TestCase("あ🇯🇵🇺🇸", 3, "あ…")]
        [TestCase("あ🇯🇵🇺🇸", 4, "あ🇯🇵…")]
        [TestCase("あ👨\u200D👩い", 4, "あ👨…")]
        public void Sanitize_Truncation_DoesNotSplitEmojiSequences(string text, int maxLength, string expected)
        {
            // #209 コメント（見た目）: 肌色の修飾の前・地域指示記号の対の間・ZWJ の直後では切らない。
            Assert.That(DisplayTextSanitizer.Sanitize(text, maxLength), Is.EqualTo(expected));
        }

        [Test]
        public void Sanitize_Truncation_DoesNotSplitAFlagTagSequence()
        {
            // 旗のタグ文字の途中で切らず、旗ごと落とす。
            var flag = string.Concat(new[] { 0x1F3F4, 0xE0067, 0xE0062, 0xE0073, 0xE0063, 0xE0074, 0xE007F }.Select(char.ConvertFromUtf32));

            Assert.That(DisplayTextSanitizer.Sanitize("あ" + flag, 5), Is.EqualTo("あ" + DisplayTextSanitizer.Ellipsis));
        }

        [TestCase("ab\u0301\u0301\u0301c", 4)]
        [TestCase("a\u0301\u0301\u0301b", 3)]
        [TestCase("あ👍\U0001F3FDい", 3)]
        [TestCase("あ🇯🇵🇺🇸", 4)]
        [TestCase("あ👨\u200D👩い", 4)]
        [TestCase("  つむ\u200B \u200D ぎ\u0301\u0301\u0301\u0301\u0301\u0301 か", 6)]
        [TestCase("あい うえお", 4)]
        [TestCase("\u3164\u200D \u0301", 3)]
        public void Sanitize_IsIdempotent_IncludingTruncatedResults(string text, int maxLength)
        {
            // PR #211 レビュー M-2: 切り詰めた結果をもう一度整えても変わらない。
            var once = DisplayTextSanitizer.Sanitize(text, maxLength);

            Assert.That(DisplayTextSanitizer.Sanitize(once, maxLength), Is.EqualTo(once));
        }
    }
}
