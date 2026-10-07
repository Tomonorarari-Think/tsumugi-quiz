using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 名簿の名前を表示の直前に整える <see cref="PlayerDisplayNameSanitizer"/> を検証する（issue #206）。
    /// <c>PlayerEntry</c> への組み込みは EditMode/Network の <c>PlayerEntryDisplayNameTests</c> で確かめる。
    /// </summary>
    public class PlayerDisplayNameSanitizerTests
    {
        [TestCase("つむぎ")]
        [TestCase("<b>つむぎ</b>")]
        [TestCase("Tsumugi Kasukabe")]
        [TestCase("（切断中）")]
        public void Sanitize_OrdinaryNames_AreUnchanged(string name)
        {
            // 連続した空白・行区切り類・除く書式文字・5 個目以降の結合記号を含まない名前は、表示でも変わらない。
            Assert.That(PlayerDisplayNameSanitizer.Sanitize(name), Is.EqualTo(name));
        }

        [TestCase("a  b", "a b")]
        [TestCase("つむ  ぎ", "つむ ぎ")]
        [TestCase("つむ\u2028ぎ", "つむ ぎ")]
        [TestCase("つむ\u2029ぎ", "つむ ぎ")]
        public void Sanitize_NamesThatPassApprovalButChangeForDisplay(string name, string expected)
        {
            // #206 M-1: 承認は通るが、表示では空白 1 つにまとめる。
            // 見えない文字（U+200B・双方向の制御など）は #209 で承認時に拒否するようになったので、ここには含めない。
            Assert.That(PlayerNameValidator.TryNormalize(name, out var normalized), Is.True, "前提: 承認を通る名前。");
            Assert.That(normalized, Is.EqualTo(name));
            Assert.That(PlayerDisplayNameSanitizer.Sanitize(name), Is.EqualTo(expected));
        }

        [Test]
        public void Sanitize_SixteenCodePointNames_AreNeverTruncated()
        {
            // 承認時の上限（16 コードポイント）ちょうどの名前は、サロゲートペア・結合記号を含んでも長さで切り詰めない。
            var surrogatePairs = string.Concat(Enumerable.Repeat("𠮷", PlayerDisplayNameSanitizer.MaxLength));
            var combining = string.Concat(Enumerable.Repeat("か\u3099", PlayerDisplayNameSanitizer.MaxLength / 2));

            foreach (var name in new[] { surrogatePairs, combining })
            {
                Assert.That(PlayerNameValidator.TryNormalize(name, out _), Is.True, "前提: 承認を通る名前。");
                Assert.That(PlayerDisplayNameSanitizer.Sanitize(name), Is.EqualTo(name));
            }
        }

        [TestCase("つむ\nぎ", "つむ ぎ")]
        [TestCase("つむ\r\n\nぎ", "つむ ぎ")]
        [TestCase("つむ\tぎ", "つむ ぎ")]
        [TestCase("つむ\u0007ぎ", "つむぎ")]
        [TestCase("つむ\u001B[2Jぎ", "つむ[2Jぎ")]
        [TestCase(" \nつむぎ\n ", "つむぎ")]
        [TestCase("\n\u0000", "")]
        public void Sanitize_ControlsAndLineBreaks_AreCleaned(string name, string expected)
        {
            Assert.That(PlayerDisplayNameSanitizer.Sanitize(name), Is.EqualTo(expected));
        }

        [Test]
        public void Sanitize_LoneSurrogate_IsRemoved()
        {
            Assert.That(PlayerDisplayNameSanitizer.Sanitize("つむ" + (char)0xD800 + "ぎ"), Is.EqualTo("つむぎ"));
        }

        [Test]
        public void Sanitize_NameOverTheApprovalLimit_IsTruncatedWithEllipsis()
        {
            var name = new string('あ', PlayerDisplayNameSanitizer.MaxLength + 4);

            Assert.That(
                PlayerDisplayNameSanitizer.Sanitize(name),
                Is.EqualTo(new string('あ', PlayerDisplayNameSanitizer.MaxLength - 1) + DisplayTextSanitizer.Ellipsis));
        }

        [Test]
        public void Sanitize_StackedCombiningMarks_AreLimitedPerBase()
        {
            // #206 M-2: 1 文字に結合記号を 30 個積んだ名前（テキスト要素では 1 文字）も、4 個までに減らす。
            var name = "つ" + new string('\u0301', 30);

            Assert.That(
                PlayerDisplayNameSanitizer.Sanitize(name),
                Is.EqualTo("つ" + new string('\u0301', DisplayTextSanitizer.MaxCombiningMarksPerBase)));
        }

        [Test]
        public void ForDisplay_EmptyAfterCleaning_FallsBackToThePlayerNumber()
        {
            Assert.That(PlayerDisplayNameSanitizer.ForDisplay("\n\u0007", 5), Is.EqualTo("プレイヤー5"));
            Assert.That(PlayerDisplayNameSanitizer.ForDisplay(null, 5), Is.EqualTo(PlayerDisplayNameSanitizer.FallbackName(5)));
            Assert.That(PlayerDisplayNameSanitizer.ForDisplay("つむぎ", 5), Is.EqualTo("つむぎ"));
        }

        [TestCase("つむ\u200Bぎ", "つむぎ")]
        [TestCase("つむ\u202Eぎ", "つむぎ")]
        [TestCase("つむ\u3164ぎ", "つむぎ")]
        [TestCase("つむぎ\u200D", "つむぎ")]
        public void Sanitize_NamesRejectedByApproval_AreStillCleanedForDisplay(string name, string expected)
        {
            // #209: 見えない文字を含む名前は承認時に拒否する。改変されたホストが名簿に載せた場合に備えて、表示でも除く。
            Assert.That(PlayerNameValidator.TryNormalize(name, out _), Is.False, "前提: 承認を通らない名前。");
            Assert.That(PlayerDisplayNameSanitizer.Sanitize(name), Is.EqualTo(expected));
        }

        [TestCase("つむぎ")]
        [TestCase("👨\u200D👩\u200D👧")]
        [TestCase("❤\uFE0Fつむぎ")]
        [TestCase("🇯🇵つむぎ")]
        [TestCase("か\u3099き")]
        [TestCase("a\u200Cb")]
        [TestCase("Chloe\u0301\u0301\u0301\u0301")]
        [TestCase("\u00A0つむ\u00A0ぎ")]
        public void Sanitize_NamesThatPassApproval_KeepTheirHiddenCharacterRules(string name)
        {
            // #209: 承認と表示は同じ規則（TextRules.RemoveHiddenCharacters）を使うので、承認を通った名前は、
            // 空白の扱い（連続をまとめる・前後を除く）以外では表示で変わらない。
            Assert.That(PlayerNameValidator.TryNormalize(name, out var normalized), Is.True, "前提: 承認を通る名前。");
            Assert.That(PlayerDisplayNameSanitizer.Sanitize(normalized), Is.EqualTo(normalized.Trim()));
        }
    }
}
