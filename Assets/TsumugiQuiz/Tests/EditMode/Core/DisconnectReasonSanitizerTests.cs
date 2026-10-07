using System;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="DisconnectReasonSanitizer"/>（ホストから届く切断理由の受信側の検証、issue #206）を検証する。
    /// </summary>
    public class DisconnectReasonSanitizerTests
    {
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\r\n\t")]
        [TestCase("\u0000\u0007\u007F\u0085")]
        public void Sanitize_NullEmptyOrOnlyWhitespaceAndControls_ReturnsEmpty(string reason)
        {
            Assert.That(DisconnectReasonSanitizer.Sanitize(reason), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Sanitize_AllReasonsThisAppSends_AreUnchanged()
        {
            // 拒否理由（全理由。バージョン表示は最大桁の 65535 で最長にする）。承認後に切断するときの理由（Network 層の定数）は
            // EditMode/Network の NetworkDisconnectReasonTests で確かめる。
            foreach (ConnectionRejectionReason reason in Enum.GetValues(typeof(ConnectionRejectionReason)))
            {
                var message = ConnectionRejectionMessages.Create(reason, ushort.MaxValue, ushort.MaxValue);
                Assert.That(DisconnectReasonSanitizer.Sanitize(message), Is.EqualTo(message), reason.ToString());
                Assert.That(CountCodePoints(message), Is.LessThanOrEqualTo(DisconnectReasonSanitizer.MaxLength / 2),
                    $"{reason}: 自前の文言は上限の半分以下に収める（上限の根拠。docs/network.md §9）。");
            }

        }

        [Test]
        public void Sanitize_TrimsSurroundingWhitespace()
        {
            Assert.That(DisconnectReasonSanitizer.Sanitize("  満室のため参加できません。 "), Is.EqualTo("満室のため参加できません。"));
        }

        [TestCase("満室の\nため", "満室の ため")]
        [TestCase("満室の\r\nため", "満室の ため")]
        [TestCase("満室の\n\n\n\nため", "満室の ため")]
        [TestCase("満室の\tため", "満室の ため")]
        [TestCase("満室の\u2028ため", "満室の ため")]
        [TestCase("満室の\u2029ため", "満室の ため")]
        [TestCase("満室の\u0085ため", "満室の ため")]
        [TestCase("満室の \n ため", "満室の ため")]
        public void Sanitize_LineBreaksAndTabs_BecomeASingleSpace(string reason, string expected)
        {
            Assert.That(DisconnectReasonSanitizer.Sanitize(reason), Is.EqualTo(expected));
        }

        [TestCase("満\u0000室", "満室")]
        [TestCase("満\u0007室", "満室")]
        [TestCase("満\u001B[31m室", "満[31m室")]
        [TestCase("満\u007F室", "満室")]
        [TestCase("満\u009B室", "満室")]
        public void Sanitize_OtherControlCharacters_AreRemoved(string reason, string expected)
        {
            Assert.That(DisconnectReasonSanitizer.Sanitize(reason), Is.EqualTo(expected));
        }

        [TestCase(0xD800)]
        [TestCase(0xDBFF)]
        [TestCase(0xDC00)]
        [TestCase(0xDFFF)]
        public void Sanitize_LoneSurrogates_AreRemoved(int codeUnit)
        {
            // 単独のサロゲートは属性の文字列に書くと置換文字に化けるため、コードで組み立てる。
            var lone = ((char)codeUnit).ToString();

            Assert.That(DisconnectReasonSanitizer.Sanitize("満" + lone + "室"), Is.EqualTo("満室"));
            Assert.That(DisconnectReasonSanitizer.Sanitize(lone), Is.EqualTo(string.Empty));
            Assert.That(DisconnectReasonSanitizer.Sanitize("満室" + lone), Is.EqualTo("満室"));
        }

        [Test]
        public void Sanitize_SurrogatePair_IsKept()
        {
            Assert.That(DisconnectReasonSanitizer.Sanitize("𠮷野家"), Is.EqualTo("𠮷野家"));
        }

        [Test]
        public void Sanitize_RichTextTags_AreKeptAsText()
        {
            // タグの無害化は表示の側（enableRichText = false）で行う。ここでは文字として残す。
            const string reason = "<size=300><color=red>満室</color></size>";
            Assert.That(DisconnectReasonSanitizer.Sanitize(reason), Is.EqualTo(reason));
        }

        [Test]
        public void Sanitize_TextAtTheLimit_IsUnchanged()
        {
            var reason = new string('あ', DisconnectReasonSanitizer.MaxLength);
            Assert.That(DisconnectReasonSanitizer.Sanitize(reason), Is.EqualTo(reason));
        }

        [Test]
        public void Sanitize_TextOverTheLimit_IsTruncatedWithEllipsis()
        {
            var reason = new string('あ', DisconnectReasonSanitizer.MaxLength + 1);

            var sanitized = DisconnectReasonSanitizer.Sanitize(reason);

            Assert.That(sanitized.Length, Is.EqualTo(DisconnectReasonSanitizer.MaxLength));
            Assert.That(sanitized, Is.EqualTo(new string('あ', DisconnectReasonSanitizer.MaxLength - 1) + DisconnectReasonSanitizer.Ellipsis));
        }

        [Test]
        public void Sanitize_VeryLongText_IsTruncated()
        {
            var sanitized = DisconnectReasonSanitizer.Sanitize(new string('x', 100000));
            Assert.That(sanitized.Length, Is.EqualTo(DisconnectReasonSanitizer.MaxLength));
        }

        [Test]
        public void Sanitize_Truncation_DoesNotSplitSurrogatePairsOrCombiningMarks()
        {
            // 上限ちょうどの位置に 2 単位の文字（サロゲートペア・結合文字）が来ても、途中で切らない。
            var head = new string('あ', DisconnectReasonSanitizer.MaxLength - 2);
            var sanitizedPair = DisconnectReasonSanitizer.Sanitize(head + "𠮷𠮷𠮷");
            var sanitizedCombining = DisconnectReasonSanitizer.Sanitize(head + "か\u3099か\u3099か\u3099");

            Assert.That(sanitizedPair, Is.EqualTo(head + "𠮷" + DisconnectReasonSanitizer.Ellipsis));
            // コードポイントで数えるので「か」+ U+3099 は 2 文字。上限から 1 引いた位置が結合記号の直前（途中）になるため、1 つ前で切る。
            Assert.That(sanitizedCombining, Is.EqualTo(head + DisconnectReasonSanitizer.Ellipsis));
        }

        [Test]
        public void Sanitize_CountsLengthAfterRemovingControls()
        {
            // 制御文字を除いた後の長さで数える（除く前に数えると、制御文字で水増しした文言が不当に切られる）。
            var reason = new string('あ', DisconnectReasonSanitizer.MaxLength) + new string('\u0000', 50);
            Assert.That(DisconnectReasonSanitizer.Sanitize(reason), Is.EqualTo(new string('あ', DisconnectReasonSanitizer.MaxLength)));
        }

        [Test]
        public void Sanitize_StackedCombiningMarks_AreLimitedPerBase()
        {
            // #206 M-2: 結合記号を積み重ねた文字列は、基底文字 1 つあたり 4 個までにする。
            var stacked = "拒否a" + new string('\u0301', 50);

            Assert.That(
                DisconnectReasonSanitizer.Sanitize(stacked),
                Is.EqualTo("拒否a" + new string('\u0301', DisplayTextSanitizer.MaxCombiningMarksPerBase)));
        }

        [TestCase("満\u202E室", "満室")]
        [TestCase("満\u200B室", "満室")]
        [TestCase("\uFEFF満室", "満室")]
        public void Sanitize_InvisibleFormatCharacters_AreRemoved(string reason, string expected)
        {
            Assert.That(DisconnectReasonSanitizer.Sanitize(reason), Is.EqualTo(expected));
        }

        private static int CountCodePoints(string text)
        {
            var count = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;
                }

                count++;
            }

            return count;
        }
    }
}
