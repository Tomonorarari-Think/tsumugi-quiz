using System;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 本番用の乱数源 <see cref="CryptoRandom"/> のテスト（docs/network.md §6.3 の同着抽選）。
    /// </summary>
    public class CryptoRandomTests
    {
        [Test]
        public void NextInt_AlwaysWithinRange()
        {
            using (var rng = new CryptoRandom())
            {
                for (var i = 0; i < 5000; i++)
                {
                    var value = rng.NextInt(5);
                    Assert.GreaterOrEqual(value, 0);
                    Assert.Less(value, 5);
                }
            }
        }

        [Test]
        public void NextInt_One_AlwaysReturnsZero()
        {
            using (var rng = new CryptoRandom())
            {
                for (var i = 0; i < 100; i++)
                {
                    Assert.AreEqual(0, rng.NextInt(1));
                }
            }
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void NextInt_NonPositiveMax_Throws(int exclusiveMax)
        {
            using (var rng = new CryptoRandom())
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(exclusiveMax));
            }
        }

        [Test]
        public void NextInt_TwoOutcomes_AreRoughlyBalanced()
        {
            const int draws = 20000;
            var zeros = 0;

            using (var rng = new CryptoRandom())
            {
                for (var i = 0; i < draws; i++)
                {
                    if (rng.NextInt(2) == 0)
                    {
                        zeros++;
                    }
                }
            }

            var rate = (double)zeros / draws;

            // 期待値 0.5、標準偏差 約 0.0035。0.03 の許容幅は約 8.5σ 相当で、偶然の失敗はまず起きない。
            Assert.That(rate, Is.EqualTo(0.5).Within(0.03), $"偏りがあります: {rate:F4}");
        }

        [Test]
        public void NextInt_ThreeOutcomes_AreRoughlyBalanced()
        {
            const int draws = 30000;
            var counts = new int[3];

            using (var rng = new CryptoRandom())
            {
                for (var i = 0; i < draws; i++)
                {
                    counts[rng.NextInt(3)]++;
                }
            }

            for (var value = 0; value < counts.Length; value++)
            {
                var rate = (double)counts[value] / draws;

                // 期待値 1/3、標準偏差 約 0.0027。0.02 の許容幅は約 7σ 相当。
                Assert.That(rate, Is.EqualTo(1.0 / 3.0).Within(0.02),
                    $"値 {value} の出現率が偏っています: {rate:F4}");
            }
        }

        [Test]
        public void NextInt_ValueOutsideAcceptableRange_IsRetried()
        {
            // exclusiveMax = 3 のとき 2^32 % 3 = 1 なので、採用区間は [0, 4294967295)。
            // 1 個目の 0xFFFFFFFF は区間外なので棄却され、2 個目の 7 が採用される（7 % 3 = 1）。
            var source = new QueuedByteRandomNumberGenerator(uint.MaxValue, 7u);

            using (var rng = new CryptoRandom(source))
            {
                var value = rng.NextInt(3);

                Assert.AreEqual(1, value);
                Assert.AreEqual(2, source.CallCount, "採用区間外の値が棄却され、引き直されること");
            }
        }

        [Test]
        public void NextInt_ValueAtUpperEdgeOfAcceptableRange_IsUsedWithoutRetry()
        {
            // 4294967294 は採用区間内（< 4294967295）。4294967294 % 3 = 2。
            var source = new QueuedByteRandomNumberGenerator(4294967294u);

            using (var rng = new CryptoRandom(source))
            {
                var value = rng.NextInt(3);

                Assert.AreEqual(2, value);
                Assert.AreEqual(1, source.CallCount);
            }
        }

        [Test]
        public void NextInt_AfterDispose_Throws()
        {
            var rng = new CryptoRandom();
            rng.Dispose();

            Assert.Throws<ObjectDisposedException>(() => rng.NextInt(2));
        }

        [Test]
        public void NextInt_AfterDispose_ReportsDisposalBeforeArgumentValidation()
        {
            var rng = new CryptoRandom();
            rng.Dispose();

            Assert.Throws<ObjectDisposedException>(() => rng.NextInt(0));
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var rng = new CryptoRandom();

            rng.Dispose();

            Assert.DoesNotThrow(() => rng.Dispose());
        }

        [Test]
        public void Constructor_NullGenerator_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CryptoRandom(null));
        }
    }
}
