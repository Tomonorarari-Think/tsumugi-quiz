using System;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 決定的な乱数源（<see cref="SeededRandom"/>、#19）のテスト。
    /// 出題順シャッフルの再現性がこれに依存する。
    /// </summary>
    public class SeededRandomTests
    {
        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            var first = new SeededRandom(12345);
            var second = new SeededRandom(12345);

            for (var i = 0; i < 100; i++)
            {
                Assert.AreEqual(first.NextInt(1000), second.NextInt(1000), $"{i} 番目の値が一致するはず。");
            }
        }

        [Test]
        public void DifferentSeed_ProducesDifferentSequence()
        {
            var first = new SeededRandom(1);
            var second = new SeededRandom(2);

            var differences = 0;
            for (var i = 0; i < 50; i++)
            {
                if (first.NextInt(1000) != second.NextInt(1000))
                {
                    differences++;
                }
            }

            Assert.Greater(differences, 40, "別のシードならほとんどの値が違うはず。");
        }

        [Test]
        public void NextInt_StaysInRange()
        {
            var random = new SeededRandom(-98765);

            for (var i = 0; i < 500; i++)
            {
                var value = random.NextInt(7);
                Assert.GreaterOrEqual(value, 0);
                Assert.Less(value, 7);
            }
        }

        [Test]
        public void NextInt_One_AlwaysReturnsZero()
        {
            var random = new SeededRandom(0);

            for (var i = 0; i < 10; i++)
            {
                Assert.AreEqual(0, random.NextInt(1));
            }
        }

        [Test]
        public void NextInt_CoversEveryValue()
        {
            var random = new SeededRandom(777);
            var seen = new bool[4];

            for (var i = 0; i < 500; i++)
            {
                seen[random.NextInt(4)] = true;
            }

            for (var i = 0; i < seen.Length; i++)
            {
                Assert.IsTrue(seen[i], $"{i} が 500 回中 1 度も出ないのは偏りすぎ。");
            }
        }

        [Test]
        public void NextInt_NonPositiveBound_Throws()
        {
            var random = new SeededRandom(1);

            Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(-1));
        }
    }
}
