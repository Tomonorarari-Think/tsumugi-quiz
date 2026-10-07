using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.UI.Views.Game;

namespace TsumugiQuiz.Tests.EditMode.UI.Game
{
    /// <summary>
    /// <see cref="ChoiceShuffle"/> のテスト（issue #17、docs/question-data.md §6
    /// 「シャッフルはクライアント表示のみ。元インデックスへの対応表を保持する」）。
    /// </summary>
    public class ChoiceShuffleTests
    {
        [Test]
        public void BuildDisplayOrder_ShuffleFalse_KeepsOriginalOrder()
        {
            var order = ChoiceShuffle.BuildDisplayOrder(4, shuffle: false, seed: 12345);

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, order);
        }

        [Test]
        public void BuildDisplayOrder_SameSeed_IsDeterministic()
        {
            var first = ChoiceShuffle.BuildDisplayOrder(8, shuffle: true, seed: 42);
            var second = ChoiceShuffle.BuildDisplayOrder(8, shuffle: true, seed: 42);

            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void BuildDisplayOrder_DifferentSeed_CanDiffer()
        {
            var first = ChoiceShuffle.BuildDisplayOrder(8, shuffle: true, seed: 1);
            var second = ChoiceShuffle.BuildDisplayOrder(8, shuffle: true, seed: 2);

            CollectionAssert.AreNotEqual(first, second);
        }

        [Test]
        public void BuildDisplayOrder_ShuffleTrue_IsPermutationOfOriginalIndices()
        {
            var order = ChoiceShuffle.BuildDisplayOrder(6, shuffle: true, seed: 999);

            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5 }, order);
        }

        [Test]
        public void BuildDisplayOrder_AllowsReverseLookupToOriginalIndex()
        {
            // 「表示上の選択肢 → 元インデックス」の対応表として使えることを確認する
            // （docs/question-data.md §6: 判定は常に元インデックスで送る）。
            var order = ChoiceShuffle.BuildDisplayOrder(4, shuffle: true, seed: 7);
            var seen = new HashSet<int>();

            foreach (var originalIndex in order)
            {
                Assert.IsTrue(originalIndex >= 0 && originalIndex < 4);
                Assert.IsTrue(seen.Add(originalIndex), "同じ元インデックスが重複してはいけない。");
            }
        }

        [Test]
        public void BuildDisplayOrder_ChoiceCountLessThanTwo_ReturnsIdentityRegardlessOfShuffle()
        {
            CollectionAssert.AreEqual(new[] { 0 }, ChoiceShuffle.BuildDisplayOrder(1, shuffle: true, seed: 5));
            CollectionAssert.AreEqual(new int[0], ChoiceShuffle.BuildDisplayOrder(0, shuffle: true, seed: 5));
        }

        [Test]
        public void BuildSeed_SameQuestionAndClient_IsDeterministic()
        {
            var first = ChoiceShuffle.BuildSeed(3, 7);
            var second = ChoiceShuffle.BuildSeed(3, 7);

            Assert.AreEqual(first, second);
        }

        [Test]
        public void BuildSeed_DifferentClient_YieldsDifferentSeed()
        {
            Assert.AreNotEqual(ChoiceShuffle.BuildSeed(3, 1), ChoiceShuffle.BuildSeed(3, 2));
        }
    }
}
