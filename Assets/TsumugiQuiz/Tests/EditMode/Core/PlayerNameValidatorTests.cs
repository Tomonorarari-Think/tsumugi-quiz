using NUnit.Framework;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// プレイヤー名の検証・正規化（docs/network.md §9）のテスト。
    /// </summary>
    public class PlayerNameValidatorTests
    {
        [TestCase("つむぎ", "つむぎ")]
        [TestCase("  つむぎ  ", "つむぎ")]
        [TestCase("a", "a")]
        [TestCase("0123456789abcdef", "0123456789abcdef")]
        [TestCase("あいうえおかきくけこさしすせそた", "あいうえおかきくけこさしすせそた")]
        [TestCase("Player 1", "Player 1")]
        public void TryNormalize_AcceptsValidNames(string input, string expected)
        {
            Assert.IsTrue(PlayerNameValidator.TryNormalize(input, out var normalized));
            Assert.AreEqual(expected, normalized);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\t")]
        public void TryNormalize_RejectsBlankNames(string input)
        {
            Assert.IsFalse(PlayerNameValidator.TryNormalize(input, out var normalized));
            Assert.AreEqual(string.Empty, normalized);
        }

        [Test]
        public void TryNormalize_RejectsNameLongerThanLimit()
        {
            var tooLong = new string('a', ProtocolConstants.MaxPlayerNameLength + 1);

            Assert.IsFalse(PlayerNameValidator.TryNormalize(tooLong, out _));
        }

        [Test]
        public void TryNormalize_AcceptsNameAtLimit()
        {
            var atLimit = new string('a', ProtocolConstants.MaxPlayerNameLength);

            Assert.IsTrue(PlayerNameValidator.TryNormalize(atLimit, out var normalized));
            Assert.AreEqual(atLimit, normalized);
        }

        [TestCase("name\u0000here")]
        [TestCase("name\u0001here")]
        [TestCase("name\u001Fhere")]
        [TestCase("name\u007Fhere")]
        [TestCase("line\nbreak")]
        [TestCase("carriage\rreturn")]
        public void TryNormalize_RejectsControlCharacters(string input)
        {
            Assert.IsFalse(PlayerNameValidator.TryNormalize(input, out _));
        }

        [Test]
        public void TryNormalize_RejectsLoneSurrogate()
        {
            var loneHighSurrogate = "ab\uD800cd";

            Assert.IsFalse(PlayerNameValidator.TryNormalize(loneHighSurrogate, out _));
        }

        [Test]
        public void TryNormalize_CountsSurrogatePairAsOneCharacter()
        {
            // U+1F600 (絵文字) を 16 個。UTF-16 では 32 要素だがコードポイントは 16 個。
            var sixteenEmoji = string.Concat(System.Linq.Enumerable.Repeat("\U0001F600", ProtocolConstants.MaxPlayerNameLength));

            Assert.IsTrue(PlayerNameValidator.TryNormalize(sixteenEmoji, out var normalized));
            Assert.AreEqual(sixteenEmoji, normalized);

            var seventeenEmoji = sixteenEmoji + "\U0001F600";
            Assert.IsFalse(PlayerNameValidator.TryNormalize(seventeenEmoji, out _));
        }

        [TestCase("つむ\u200Bぎ")]
        [TestCase("つむ\u202Eぎ")]
        [TestCase("\u2066つむぎ\u2069")]
        [TestCase("つむ\u00ADぎ")]
        [TestCase("つむ\u034Fぎ")]
        [TestCase("つむ\u3164ぎ")]
        [TestCase("\u3164")]
        [TestCase("\uFFA0\uFFA0")]
        [TestCase("\u200Dつむぎ")]
        [TestCase("つむぎ\u200D")]
        [TestCase("つむぎ\u200C")]
        [TestCase("つむ\u200D ぎ")]
        [TestCase("\u0301つむぎ")]
        [TestCase("つむ \u0301ぎ")]
        [TestCase("つむぎ\U000E0067")]
        public void TryNormalize_RejectsHiddenCharacters(string input)
        {
            // #209: 見えない文字で「つむぎ」と同じ見た目の別の名前を作れないよう、承認時に拒否する
            // （規則は表示の整形と同じ TextRules.RemoveHiddenCharacters。docs/network.md §9）。
            Assert.IsFalse(PlayerNameValidator.TryNormalize(input, out var normalized));
            Assert.AreEqual(string.Empty, normalized);
        }

        [Test]
        public void TryNormalize_RejectsMoreThanFourCombiningMarksOnOneBase()
        {
            var four = "つ" + new string('\u0301', TsumugiQuiz.Core.TextRules.MaxCombiningMarksPerBase);
            var five = "つ" + new string('\u0301', TsumugiQuiz.Core.TextRules.MaxCombiningMarksPerBase + 1);

            Assert.IsTrue(PlayerNameValidator.TryNormalize(four, out _));
            Assert.IsFalse(PlayerNameValidator.TryNormalize(five, out _));
        }

        [TestCase("👨\u200D👩\u200D👧")]
        [TestCase("❤\uFE0Fつむぎ")]
        [TestCase("1\uFE0F\u20E3")]
        [TestCase("👍\U0001F3FD")]
        [TestCase("🇯🇵つむぎ")]
        [TestCase("か\u3099き")]
        [TestCase("a\u200Cb")]
        [TestCase("Chloe\u0301")]
        public void TryNormalize_AcceptsEmojiSequencesVariationSelectorsAndCombiningMarks(string input)
        {
            // #209: 絵文字の ZWJ 連結・異体字セレクタ・肌色の修飾・国旗・結合文字（4 個まで）は正当な名前として通す。
            Assert.IsTrue(PlayerNameValidator.TryNormalize(input, out var normalized));
            Assert.AreEqual(input, normalized);
        }

        [Test]
        public void TryNormalize_AcceptsSubdivisionFlag()
        {
            // スコットランドの旗（U+1F3F4 + タグ文字 + U+E007F）。タグ文字は旗の中なら残す。
            var flag = string.Concat(System.Linq.Enumerable.Select(
                new[] { 0x1F3F4, 0xE0067, 0xE0062, 0xE0073, 0xE0063, 0xE0074, 0xE007F }, char.ConvertFromUtf32));

            Assert.IsTrue(PlayerNameValidator.TryNormalize(flag, out var normalized));
            Assert.AreEqual(flag, normalized);
        }

        [Test]
        public void RuleSummary_DescribesTheRules()
        {
            // #209: 拒否の文言に、長さ・制御文字に加えて見えない文字と重ねすぎた記号の規則を含める。
            StringAssert.Contains("1〜16文字", PlayerNameValidator.RuleSummary);
            StringAssert.Contains("見えない文字", PlayerNameValidator.RuleSummary);
            StringAssert.Contains("重ねすぎた記号", PlayerNameValidator.RuleSummary);
        }

        [TestCase("Vie\u0323\u0302t")] // ベトナム語（分解形 NFD）
        [TestCase("\u0645\u064F\u062D\u064E\u0645\u0651\u064E\u062F")] // アラビア語（母音記号とシャッダ）
        [TestCase("\u05D8\u05BC\u05B5\u0595")] // SBL Hebrew の例（結合記号 3 個）
        [TestCase("\u0915\u094D\u200D\u0937")] // デーヴァナーガリーの半字形の指定
        [TestCase("\U0001F469\U0001F3FD\u200D\U0001F4BB")] // 職業 + 肌色の ZWJ 連結
        [TestCase("#\uFE0F\u20E3")] // キーキャップ
        public void TryNormalize_AcceptsLegitimateNamesInOtherScripts(string input)
        {
            // PR #211 レビュー L-8: 正当な表記の名前は通す。
            Assert.IsTrue(PlayerNameValidator.TryNormalize(input, out var normalized));
            Assert.AreEqual(input, normalized);
        }

        [Test]
        public void TryNormalize_AcceptsEnglandAndWalesFlags()
        {
            var england = string.Concat(System.Linq.Enumerable.Select(
                new[] { 0x1F3F4, 0xE0067, 0xE0062, 0xE0065, 0xE006E, 0xE0067, 0xE007F }, char.ConvertFromUtf32));
            var wales = string.Concat(System.Linq.Enumerable.Select(
                new[] { 0x1F3F4, 0xE0067, 0xE0062, 0xE0077, 0xE006C, 0xE0073, 0xE007F }, char.ConvertFromUtf32));

            Assert.IsTrue(PlayerNameValidator.TryNormalize(england, out _));
            Assert.IsTrue(PlayerNameValidator.TryNormalize(wales, out _));
        }
    }
}
