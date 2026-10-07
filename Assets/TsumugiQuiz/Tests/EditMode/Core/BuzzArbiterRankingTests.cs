using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="BuzzResolution.Ranking"/>（集計窓の中の押下順位、#194）のテスト。
    /// </summary>
    public class BuzzArbiterRankingTests
    {
        private const double T0 = 1000.0;
        private const double Window = 0.15;

        private static BuzzResolution Resolve(BuzzArbiter arbiter, double serverNow, IRandom rng = null)
        {
            Assert.IsTrue(arbiter.TryResolve(serverNow, rng ?? new DeterministicRandom(1), out var resolution));
            Assert.IsTrue(resolution.HasValue);
            return resolution.Value;
        }

        [Test]
        public void Ranking_SingleCandidate_HasOnlyTheWinner()
        {
            var arbiter = new BuzzArbiter(T0, Window);
            arbiter.Accept(5, T0 + 0.4, T0 + 0.41, out _);

            var resolution = Resolve(arbiter, T0 + 0.41 + Window);

            Assert.AreEqual(1, resolution.Ranking.Count);
            Assert.AreEqual(5UL, resolution.Ranking[0].ClientId);
            Assert.IsFalse(resolution.Ranking[0].TiedWithWinner);
        }

        [Test]
        public void Ranking_OrdersByDtNotByArrival_WithWinnerFirst()
        {
            var arbiter = new BuzzArbiter(T0, Window);

            // 届いた順は 1 → 2 → 3 だが、押下時刻（dt）は 3 → 1 → 2 の順に速い。
            arbiter.Accept(1, T0 + 0.30, T0 + 0.40, out _);
            arbiter.Accept(2, T0 + 0.35, T0 + 0.45, out _);
            arbiter.Accept(3, T0 + 0.25, T0 + 0.50, out _);

            var resolution = Resolve(arbiter, T0 + 0.40 + Window);

            Assert.AreEqual(3UL, resolution.WinnerClientId);
            CollectionAssert.AreEqual(
                new ulong[] { 3, 1, 2 },
                new[] { resolution.Ranking[0].ClientId, resolution.Ranking[1].ClientId, resolution.Ranking[2].ClientId });
            Assert.AreEqual(resolution.CandidateCount, resolution.Ranking.Count);
            Assert.IsFalse(resolution.Ranking[1].TiedWithWinner, "同着でない候補に同着の印は付かない。");
        }

        [Test]
        public void Ranking_Tie_PutsLotteryWinnerFirstAndMarksTheTiedLoser()
        {
            var arbiter = new BuzzArbiter(T0, Window);
            arbiter.Accept(1, T0 + 0.30, T0 + 0.40, out _);
            arbiter.Accept(2, T0 + 0.30, T0 + 0.41, out _);
            arbiter.Accept(3, T0 + 0.33, T0 + 0.42, out _);

            var resolution = Resolve(arbiter, T0 + 0.40 + Window);

            Assert.IsTrue(resolution.WasTie);
            Assert.AreEqual(resolution.WinnerClientId, resolution.Ranking[0].ClientId, "先頭は抽選の勝者。");
            Assert.IsTrue(resolution.Ranking[0].TiedWithWinner);
            Assert.IsTrue(resolution.Ranking[1].TiedWithWinner, "抽選で負けた同着の候補は 2 位で同着の印が付く。");
            Assert.AreNotEqual(resolution.Ranking[0].ClientId, resolution.Ranking[1].ClientId);
            Assert.AreEqual(3UL, resolution.Ranking[2].ClientId);
            Assert.IsFalse(resolution.Ranking[2].TiedWithWinner);
        }

        [Test]
        public void Ranking_DefaultResolution_IsEmptyNotNull()
        {
            var resolution = default(BuzzResolution);

            Assert.IsNotNull(resolution.Ranking);
            Assert.AreEqual(0, resolution.Ranking.Count);
        }
    }
}
