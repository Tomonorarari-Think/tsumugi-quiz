using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="JoinCodeInputFormatter"/> のテスト（issue #6 レビュー M-9）。
    /// キャレット位置の復元（H-2）と、12 文字超過・不正 Unicode 時の表示（M-4）を中心に検証する。
    /// </summary>
    public class JoinCodeInputFormatterTests
    {
        [Test]
        public void Format_EmptyInput_ReturnsEmptyWithNoError()
        {
            var result = JoinCodeInputFormatter.Format(string.Empty, 0);

            Assert.That(result.DisplayText, Is.Empty);
            Assert.That(result.CaretIndex, Is.EqualTo(0));
            Assert.That(result.Error, Is.Null);
            Assert.That(result.DecodedEndpoint, Is.Null);
        }

        [Test]
        public void Format_NullInput_TreatedAsEmpty()
        {
            var result = JoinCodeInputFormatter.Format(null, 5);

            Assert.That(result.DisplayText, Is.Empty);
            Assert.That(result.CaretIndex, Is.EqualTo(0));
            Assert.That(result.Error, Is.Null);
        }

        // ---------- H-2: キャレット位置の復元 ----------

        [TestCase(0, 0, TestName = "Caret_AtStart")]
        [TestCase(1, 1, TestName = "Caret_WithinFirstGroup")]
        [TestCase(3, 3, TestName = "Caret_JustBeforeFirstHyphen")]
        [TestCase(4, 4, TestName = "Caret_RightAfterFourthChar_BeforeHyphen")]
        [TestCase(5, 6, TestName = "Caret_WithinSecondGroup")]
        [TestCase(8, 9, TestName = "Caret_RightAfterEighthChar_BeforeSecondHyphen")]
        [TestCase(9, 11, TestName = "Caret_WithinThirdGroup")]
        [TestCase(12, 14, TestName = "Caret_AtEnd")]
        public void Format_NoHyphensYet_PlacesCaretAtExpectedPhysicalIndex(int logicalCaretIndex, int expectedPhysicalIndex)
        {
            // 入力はまだハイフンなしの生の 12 文字（例: ペーストしたばかり）。
            const string raw = "6B01RGA7K1K4";

            var result = JoinCodeInputFormatter.Format(raw, logicalCaretIndex);

            Assert.That(result.DisplayText, Is.EqualTo("6B01-RGA7-K1K4"));
            Assert.That(result.CaretIndex, Is.EqualTo(expectedPhysicalIndex));
        }

        [Test]
        public void Format_CaretWithinAlreadyFormattedInput_IsIdempotent()
        {
            // 既に正しくハイフン整形済みの入力に対しては、見た目上の変化がないはず。
            const string raw = "6B01-RGA7-K1K4";

            // 物理位置 6（"6B01-R" の直後、'G' の直前）は論理位置 5 文字目に対応する。
            var result = JoinCodeInputFormatter.Format(raw, 6);

            Assert.That(result.DisplayText, Is.EqualTo(raw));
            Assert.That(result.CaretIndex, Is.EqualTo(6));
        }

        [Test]
        public void Format_LowercaseWithMisplacedHyphen_ReformatsAndKeepsCaretLogicalPosition()
        {
            // "6b0-1rga7k1k4" は 12 文字の内容だが途中に紛れたハイフンを含む。
            // キャレットが 5 文字目（"6b0-1" の直後）にあるとき、区切り文字を除いた論理位置は 4。
            const string raw = "6b0-1rga7k1k4";

            var result = JoinCodeInputFormatter.Format(raw, 5);

            Assert.That(result.DisplayText, Is.EqualTo("6B01-RGA7-K1K4"));
            Assert.That(result.CaretIndex, Is.EqualTo(4));
        }

        // ---------- M-4: 12 文字超過時の表示 ----------

        [Test]
        public void Format_MoreThanTwelveCharacters_ShowsInvalidLength_ButTruncatesDisplay()
        {
            const string raw = "6B01RGA7K1K4XYZ"; // 15 文字

            var result = JoinCodeInputFormatter.Format(raw, raw.Length);

            Assert.That(result.Error, Is.EqualTo(JoinCodeError.InvalidLength));
            Assert.That(result.DisplayText, Is.EqualTo("6B01-RGA7-K1K4"), "13 文字目以降は表示上切り捨てるはずです。");
            Assert.That(result.CaretIndex, Is.EqualTo(result.DisplayText.Length), "超過分の入力後はキャレットが末尾に収まるはずです。");
            Assert.That(result.DecodedEndpoint, Is.Null);
        }

        [Test]
        public void Format_MoreThanTwelveCharacters_CaretBeforeOverflow_ClampsToTwelfthCharacter()
        {
            const string raw = "6B01RGA7K1K4XYZ"; // 15 文字、うち末尾 3 文字が超過分

            // キャレットは 12 文字目の直後（まだ超過分より前）。
            var result = JoinCodeInputFormatter.Format(raw, 12);

            Assert.That(result.Error, Is.EqualTo(JoinCodeError.InvalidLength));
            Assert.That(result.CaretIndex, Is.EqualTo(14), "12 文字ちょうどの直後は整形後の末尾（14文字目）になるはずです。");
        }

        [Test]
        public void Format_ExactlyTwelveCharacters_ValidCode_DecodesSuccessfully()
        {
            var result = JoinCodeInputFormatter.Format("6b01-rga7-k1k4", 14);

            Assert.That(result.Error, Is.Null);
            Assert.That(result.DecodedEndpoint, Is.Not.Null);
            Assert.That(result.DecodedEndpoint.Value.Ip, Is.EqualTo("203.0.113.5"));
            Assert.That(result.DecodedEndpoint.Value.Port, Is.EqualTo(7777));
        }

        [Test]
        public void Format_ExactlyTwelveCharacters_ChecksumMismatch_ReturnsError()
        {
            var result = JoinCodeInputFormatter.Format("6B01RGA7K1K5", 12);

            Assert.That(result.Error, Is.EqualTo(JoinCodeError.ChecksumMismatch));
            Assert.That(result.DecodedEndpoint, Is.Null);
        }

        [Test]
        public void Format_FewerThanTwelveCharacters_NoErrorYet()
        {
            var result = JoinCodeInputFormatter.Format("6B01", 4);

            Assert.That(result.Error, Is.Null);
            Assert.That(result.DecodedEndpoint, Is.Null);
            Assert.That(result.DisplayText, Is.EqualTo("6B01"));
        }

        // ---------- M-4: 不正な Unicode（TryNormalizePartial 失敗）時は入力値を保持 ----------

        [Test]
        public void Format_InvalidUnicode_PreservesRawInput_AndShowsInvalidCharacter()
        {
            // 対になっていない高サロゲート単体は string.Normalize が例外を投げる不正な Unicode。
            const string raw = "6B01\uD800RGA7";

            var result = JoinCodeInputFormatter.Format(raw, 5);

            Assert.That(result.Error, Is.EqualTo(JoinCodeError.InvalidCharacter));
            Assert.That(result.DisplayText, Is.EqualTo(raw), "不正な Unicode のときは入力値を無言で消してはいけません。");
            Assert.That(result.CaretIndex, Is.EqualTo(5), "キャレット位置も入力時のまま保持するはずです。");
            Assert.That(result.DecodedEndpoint, Is.Null);
        }
    }
}
