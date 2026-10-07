using System;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="ResultRanking"/>（Result View の順位計算、#20）のテスト。
    /// </summary>
    public class ResultRankingTests
    {
        [Test]
        public void Compute_DistinctScores_AssignsSequentialRanks()
        {
            var entries = new List<(ulong ClientId, int Score)>
            {
                (1UL, 10),
                (2UL, 30),
                (3UL, 20),
            };

            var ranked = ResultRanking.Compute(entries);

            Assert.AreEqual(3, ranked.Count);
            Assert.AreEqual((1, 2UL, 30), (ranked[0].Rank, ranked[0].ClientId, ranked[0].Score));
            Assert.AreEqual((2, 3UL, 20), (ranked[1].Rank, ranked[1].ClientId, ranked[1].Score));
            Assert.AreEqual((3, 1UL, 10), (ranked[2].Rank, ranked[2].ClientId, ranked[2].Score));
        }

        [Test]
        public void Compute_TiedScores_ShareRankAndSkipNext()
        {
            // 100, 100, 80 点 → 1 位, 1 位, 3 位（同点の人数ぶん次の順位を飛ばす）。
            var entries = new List<(ulong ClientId, int Score)>
            {
                (1UL, 100),
                (2UL, 80),
                (3UL, 100),
            };

            var ranked = ResultRanking.Compute(entries);

            var byClientId = new Dictionary<ulong, RankedScore>();
            foreach (var entry in ranked)
            {
                byClientId[entry.ClientId] = entry;
            }

            Assert.AreEqual(1, byClientId[1UL].Rank);
            Assert.AreEqual(1, byClientId[3UL].Rank, "同点は同順位のはず。");
            Assert.AreEqual(3, byClientId[2UL].Rank, "同点者の人数ぶん次の順位を飛ばすはず。");
        }

        [Test]
        public void Compute_AllTied_AllShareFirstRank()
        {
            var entries = new List<(ulong ClientId, int Score)> { (1UL, 5), (2UL, 5), (3UL, 5) };

            var ranked = ResultRanking.Compute(entries);

            foreach (var entry in ranked)
            {
                Assert.AreEqual(1, entry.Rank);
            }
        }

        [Test]
        public void Compute_EmptyList_ReturnsEmpty()
        {
            var ranked = ResultRanking.Compute(new List<(ulong ClientId, int Score)>());

            Assert.IsEmpty(ranked);
        }

        [Test]
        public void Compute_DisconnectedClientStillRanked_ScoreOnlyDeterminesRank()
        {
            // 切断中かどうかは呼び出し側（UI 層）の表示上の関心事で、順位計算そのものは
            // 得点だけを見る（クライアント ID しか受け取らないため、切断者を除外・特別扱いしない）。
            var entries = new List<(ulong ClientId, int Score)>
            {
                (1UL, 40), // 切断中を想定
                (2UL, 60),
            };

            var ranked = ResultRanking.Compute(entries);

            Assert.AreEqual(1, ranked[0].Rank);
            Assert.AreEqual(2UL, ranked[0].ClientId);
            Assert.AreEqual(2, ranked[1].Rank);
            Assert.AreEqual(1UL, ranked[1].ClientId);
        }

        [Test]
        public void Compute_NegativeScores_AreOrderedCorrectly()
        {
            var entries = new List<(ulong ClientId, int Score)> { (1UL, -5), (2UL, 0), (3UL, -5) };

            var ranked = ResultRanking.Compute(entries);

            Assert.AreEqual(0, ranked[0].Score);
            Assert.AreEqual(1, ranked[0].Rank);
            Assert.AreEqual(-5, ranked[1].Score);
            Assert.AreEqual(2, ranked[1].Rank);
            Assert.AreEqual(-5, ranked[2].Score);
            Assert.AreEqual(2, ranked[2].Rank);
        }

        [Test]
        public void Compute_NullEntries_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ResultRanking.Compute(null));
        }
    }
}
