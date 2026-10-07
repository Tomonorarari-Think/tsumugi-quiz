using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="BuildDisplayNumber"/>（ビルドの識別子を拒否の文言に入れる番号に写す、#204）を検証する。
    /// </summary>
    public class BuildDisplayNumberTests
    {
        /// <summary>
        /// #208 と #209 のクライアントの <see cref="DisconnectReasonLocalizer"/> がバージョン不一致の理由と認める書式（凍結した写し）。
        /// 書式の数字は ASCII の 1〜5 桁。番号がこの範囲を外れると、その版のクライアントでは汎用の文言になる。
        /// </summary>
        private static readonly Regex PreviousReleaseVersionMismatchPattern = new Regex(
            @"\Aバージョンが異なります（ホスト: [0-9]{1,5} / あなた: [0-9]{1,5}）。\z", RegexOptions.CultureInvariant);

        [TestCase(null)]
        [TestCase("")]
        public void From_EmptyHash_ReturnsUnknown(string buildHash)
        {
            Assert.AreEqual(BuildDisplayNumber.Unknown, BuildDisplayNumber.From(buildHash));
            Assert.AreEqual(0, BuildDisplayNumber.Unknown);
        }

        [Test]
        public void From_UsesFnv1aOfUtf16CodeUnits()
        {
            // FNV-1a（32bit）の "abc" は 0x1A47E90B = 440920331（ASCII なので UTF-16 の符号単位とバイトが同じ値）。
            // 440920331 % 65535 + 1 = 852。string.GetHashCode のように実行環境で変わる値を使っていないことの確認も兼ねる。
            Assert.AreEqual(852, BuildDisplayNumber.From("abc"));
        }

        [TestCase("0")]
        [TestCase("abc")]
        [TestCase("0123456789abcdef0123456789abcdef")]
        [TestCase("max-14621")]
        public void From_NonEmptyHash_IsBetweenOneAndMax(string buildHash)
        {
            var number = BuildDisplayNumber.From(buildHash);

            Assert.That(number, Is.InRange(1, (int)BuildDisplayNumber.MaxNumber));
            Assert.AreEqual(number, BuildDisplayNumber.From(buildHash), "同じ識別子からは常に同じ番号が出るはず。");
        }

        [Test]
        public void From_CanReachMaxNumber()
        {
            // 番号の最大値（65535）も、旧版のクライアントの書式（5 桁まで）に収まる。
            Assert.AreEqual(BuildDisplayNumber.MaxNumber, BuildDisplayNumber.From("max-14621"));
            StringAssert.IsMatch(
                PreviousReleaseVersionMismatchPattern.ToString(),
                ConnectionRejectionMessages.CreateBuildMismatch("max-14621", string.Empty));
        }

        [Test]
        public void ForMismatch_SameHash_ReturnsSameNumbers()
        {
            var numbers = BuildDisplayNumber.ForMismatch("abc", "abc");

            Assert.AreEqual(852, numbers.Host);
            Assert.AreEqual(852, numbers.Client);
        }

        [Test]
        public void ForMismatch_EmptyClientHash_ReturnsZeroForClient()
        {
            var numbers = BuildDisplayNumber.ForMismatch("abc", string.Empty);

            Assert.AreEqual(852, numbers.Host);
            Assert.AreEqual(0, numbers.Client);
        }

        [Test]
        public void ForMismatch_CollidingHashes_ShiftsClientNumber()
        {
            // "build-118" と "build-408" は別の識別子だが、どちらも番号 51531 になる（事前に総当たりで見つけた組）。
            Assert.AreEqual(51531, BuildDisplayNumber.From("build-118"));
            Assert.AreEqual(51531, BuildDisplayNumber.From("build-408"));

            var numbers = BuildDisplayNumber.ForMismatch("build-118", "build-408");

            Assert.AreEqual(51531, numbers.Host);
            Assert.AreEqual(51532, numbers.Client, "拒否の文言で 2 つの番号が同じに見えないよう、クライアント側をずらすはず。");
        }

        [Test]
        public void ForMismatch_CollisionAtMaxNumber_WrapsToOne()
        {
            Assert.AreEqual(BuildDisplayNumber.MaxNumber, BuildDisplayNumber.From("max-14621"));
            Assert.AreEqual(BuildDisplayNumber.MaxNumber, BuildDisplayNumber.From("max-75084"));

            var numbers = BuildDisplayNumber.ForMismatch("max-14621", "max-75084");

            Assert.AreEqual(BuildDisplayNumber.MaxNumber, numbers.Host);
            Assert.AreEqual(1, numbers.Client, "最大値の次は 1（0 は識別子が無いときの番号なので使わない）。");
        }
    }
}
