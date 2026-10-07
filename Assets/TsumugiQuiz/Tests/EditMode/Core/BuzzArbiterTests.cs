using System;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="BuzzArbiter"/> の受理・棄却・補正・裁定のテスト（docs/network.md §6.3〜§6.4）。
    /// </summary>
    public class BuzzArbiterTests
    {
        private const double T0 = 1000.0;
        private const double Window = 0.15;   // buzz.collectWindowMs 既定 150ms

        private static BuzzArbiter NewArbiter(params ulong[] penalized)
        {
            return new BuzzArbiter(T0, Window, penalized);
        }

        // --- Accept: 正常系 ---

        [Test]
        public void Accept_FirstBuzz_IsAcceptedAndOpensWindow()
        {
            var arbiter = NewArbiter();

            var accepted = arbiter.Accept(1, T0 + 0.4, T0 + 0.42, out var reason);

            Assert.IsTrue(accepted);
            Assert.AreEqual(BuzzReject.None, reason);
            Assert.AreEqual(1, arbiter.Candidates.Count);
            Assert.AreEqual(0.4, arbiter.Candidates[0].Dt, 1e-9);
            Assert.AreEqual(BuzzClamp.None, arbiter.Candidates[0].Clamp);
            Assert.AreEqual(T0 + 0.42 + Window, arbiter.DeadlineServerTime.Value, 1e-9);
        }

        [Test]
        public void Accept_BeforeAnyBuzz_DeadlineIsNull()
        {
            var arbiter = NewArbiter();

            Assert.IsNull(arbiter.DeadlineServerTime);
            Assert.AreEqual(BuzzArbiterState.Open, arbiter.State);
        }

        [Test]
        public void Accept_SecondBuzz_DoesNotExtendWindow()
        {
            var arbiter = NewArbiter();
            arbiter.Accept(1, T0 + 0.4, T0 + 0.42, out _);
            var deadline = arbiter.DeadlineServerTime.Value;

            arbiter.Accept(2, T0 + 0.45, T0 + 0.5, out _);

            Assert.AreEqual(deadline, arbiter.DeadlineServerTime.Value, 1e-12);
        }

        [Test]
        public void Candidates_IsReadOnlyView()
        {
            var arbiter = NewArbiter();

            Assert.IsTrue(((System.Collections.Generic.ICollection<BuzzCandidate>)arbiter.Candidates).IsReadOnly);
            Assert.Throws<NotSupportedException>(
                () => ((System.Collections.Generic.ICollection<BuzzCandidate>)arbiter.Candidates)
                    .Add(new BuzzCandidate(9, 0.0, BuzzClamp.None)));
        }

        // --- Accept: 棄却系（§6.4） ---

        [Test]
        public void Accept_SameClientTwice_RejectsAsDuplicate()
        {
            var arbiter = NewArbiter();
            Assert.IsTrue(arbiter.Accept(1, T0 + 0.4, T0 + 0.42, out _));

            var accepted = arbiter.Accept(1, T0 + 0.41, T0 + 0.43, out var reason);

            Assert.IsFalse(accepted);
            Assert.AreEqual(BuzzReject.Duplicate, reason);
            Assert.AreEqual(1, arbiter.Candidates.Count);
        }

        [Test]
        public void Accept_PenalizedClient_RejectsAsPenalized()
        {
            var arbiter = NewArbiter(7UL);

            var accepted = arbiter.Accept(7, T0 + 0.4, T0 + 0.42, out var reason);

            Assert.IsFalse(accepted);
            Assert.AreEqual(BuzzReject.Penalized, reason);
            Assert.AreEqual(0, arbiter.Candidates.Count);
            Assert.IsNull(arbiter.DeadlineServerTime);
        }

        [Test]
        public void Accept_AfterResolved_RejectsAsNotOpen()
        {
            var arbiter = NewArbiter();
            arbiter.Accept(1, T0 + 0.4, T0 + 0.42, out _);
            Assert.IsTrue(arbiter.TryResolve(T0 + 0.42 + Window, new QueuedRandom(), out _));

            var accepted = arbiter.Accept(2, T0 + 0.45, T0 + 0.6, out var reason);

            Assert.IsFalse(accepted);
            Assert.AreEqual(BuzzReject.NotOpen, reason);
            Assert.AreEqual(BuzzArbiterState.Resolved, arbiter.State);
        }

        [Test]
        public void Accept_AfterClose_RejectsAsNotOpen()
        {
            var arbiter = NewArbiter();
            arbiter.Close();

            var accepted = arbiter.Accept(1, T0 + 0.4, T0 + 0.42, out var reason);

            Assert.IsFalse(accepted);
            Assert.AreEqual(BuzzReject.NotOpen, reason);
            Assert.AreEqual(BuzzArbiterState.Closed, arbiter.State);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void Accept_NonFiniteReportedTime_Rejects(double reportedTime)
        {
            var arbiter = NewArbiter();

            var accepted = arbiter.Accept(1, reportedTime, T0 + 0.42, out var reason);

            Assert.IsFalse(accepted);
            Assert.AreEqual(BuzzReject.NonFiniteTimestamp, reason);
            Assert.AreEqual(0, arbiter.Candidates.Count);
        }

        [Test]
        public void Accept_MoreThanOneSecondInFuture_Rejects()
        {
            var arbiter = NewArbiter();
            var serverNow = T0 + 0.42;

            var accepted = arbiter.Accept(1, serverNow + BuzzArbiter.MaxFutureSec + 0.001, serverNow, out var reason);

            Assert.IsFalse(accepted);
            Assert.AreEqual(BuzzReject.TooFarInFuture, reason);
            Assert.AreEqual(0, arbiter.Candidates.Count);
            Assert.IsNull(arbiter.DeadlineServerTime);
        }

        [Test]
        public void Accept_ExactlyOneSecondInFuture_IsClampedAndAccepted()
        {
            var arbiter = NewArbiter();
            var serverNow = T0 + 0.42;

            var accepted = arbiter.Accept(1, serverNow + BuzzArbiter.MaxFutureSec, serverNow, out var reason);

            Assert.IsTrue(accepted);
            Assert.AreEqual(BuzzReject.None, reason);
            Assert.AreEqual(0.42, arbiter.Candidates[0].Dt, 1e-9);
            Assert.AreEqual(BuzzClamp.ToServerNow, arbiter.Candidates[0].Clamp);
        }

        [Test]
        public void Accept_MoreThanMaxPastBeforeT0_Rejects()
        {
            var arbiter = NewArbiter();

            var accepted = arbiter.Accept(1, T0 - BuzzArbiter.MaxPastSec - 0.001, T0 + 0.02, out var reason);

            Assert.IsFalse(accepted);
            Assert.AreEqual(BuzzReject.TooFarInPast, reason);
            Assert.AreEqual(0, arbiter.Candidates.Count);
            Assert.IsNull(arbiter.DeadlineServerTime);
        }

        [Test]
        public void Accept_ExactlyMaxPastBeforeT0_IsClampedAndAccepted()
        {
            var arbiter = NewArbiter();

            var accepted = arbiter.Accept(1, T0 - BuzzArbiter.MaxPastSec, T0 + 0.02, out var reason);

            Assert.IsTrue(accepted);
            Assert.AreEqual(BuzzReject.None, reason);
            Assert.AreEqual(0.0, arbiter.Candidates[0].Dt, 1e-12);
            Assert.AreEqual(BuzzClamp.ToT0, arbiter.Candidates[0].Clamp);
        }

        [Test]
        public void Accept_NonFiniteServerNow_Throws()
        {
            var arbiter = NewArbiter();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => arbiter.Accept(1, T0 + 0.4, double.NaN, out _));
        }

        // --- Accept: 集計窓の締め切りガード ---

        [Test]
        public void Accept_AfterDeadline_RejectsAsWindowClosed_EvenBeforeTryResolve()
        {
            var arbiter = NewArbiter();
            var firstArrival = T0 + 0.42;
            Assert.IsTrue(arbiter.Accept(1, T0 + 0.40, firstArrival, out _));

            // TryResolve をまだ呼んでいなくても、締め切りを過ぎた押下は候補にしない。
            var accepted = arbiter.Accept(2, T0 + 0.10, firstArrival + Window + 0.001, out var reason);

            Assert.IsFalse(accepted);
            Assert.AreEqual(BuzzReject.WindowClosed, reason);
            Assert.AreEqual(1, arbiter.Candidates.Count);
            Assert.AreEqual(BuzzArbiterState.Open, arbiter.State);
        }

        [Test]
        public void Accept_AfterDeadline_LateCandidateDoesNotWinLaterResolve()
        {
            var arbiter = NewArbiter();
            var firstArrival = T0 + 0.42;
            arbiter.Accept(1, T0 + 0.40, firstArrival, out _);

            // 締め切り後に「より速い dt」を名乗る押下が届いても勝者にならない。
            arbiter.Accept(2, T0 + 0.05, firstArrival + Window, out var reason);
            var resolved = arbiter.TryResolve(firstArrival + Window + 0.05, new QueuedRandom(), out var resolution);

            Assert.AreEqual(BuzzReject.WindowClosed, reason);
            Assert.IsTrue(resolved);
            Assert.AreEqual(1UL, resolution.Value.WinnerClientId);
            Assert.AreEqual(1, resolution.Value.CandidateCount);
        }

        [Test]
        public void Accept_ExactlyAtDeadline_RejectsAsWindowClosed()
        {
            var arbiter = NewArbiter();
            var firstArrival = T0 + 0.42;
            arbiter.Accept(1, T0 + 0.40, firstArrival, out _);

            var accepted = arbiter.Accept(2, T0 + 0.41, arbiter.DeadlineServerTime.Value, out var reason);

            Assert.IsFalse(accepted);
            Assert.AreEqual(BuzzReject.WindowClosed, reason);
        }

        [Test]
        public void Accept_JustBeforeDeadline_IsAccepted()
        {
            var arbiter = NewArbiter();
            var firstArrival = T0 + 0.42;
            arbiter.Accept(1, T0 + 0.40, firstArrival, out _);

            var accepted = arbiter.Accept(2, T0 + 0.41, arbiter.DeadlineServerTime.Value - 0.001, out var reason);

            Assert.IsTrue(accepted);
            Assert.AreEqual(BuzzReject.None, reason);
            Assert.AreEqual(2, arbiter.Candidates.Count);
        }

        // --- Accept: 丸め補正（§6.4） ---

        [Test]
        public void Accept_SlightlyBeforeT0_ClampsToT0()
        {
            var arbiter = NewArbiter();

            var accepted = arbiter.Accept(1, T0 - 0.03, T0 + 0.02, out var reason);

            Assert.IsTrue(accepted);
            Assert.AreEqual(BuzzReject.None, reason);
            Assert.AreEqual(0.0, arbiter.Candidates[0].Dt, 1e-12);
            Assert.AreEqual(BuzzClamp.ToT0, arbiter.Candidates[0].Clamp);
        }

        [Test]
        public void Accept_AfterServerNow_ClampsToServerNow()
        {
            var arbiter = NewArbiter();
            var serverNow = T0 + 0.30;

            var accepted = arbiter.Accept(1, serverNow + 0.5, serverNow, out var reason);

            Assert.IsTrue(accepted);
            Assert.AreEqual(BuzzReject.None, reason);
            Assert.AreEqual(0.30, arbiter.Candidates[0].Dt, 1e-9);
            Assert.AreEqual(BuzzClamp.ToServerNow, arbiter.Candidates[0].Clamp);
        }

        [Test]
        public void Accept_ServerNowBeforeT0_ClampsDtToZero()
        {
            var arbiter = NewArbiter();

            // 受付開始より前にサーバーが受信するという異常系。dt が負にならないこと。
            var accepted = arbiter.Accept(1, T0 - 0.02, T0 - 0.01, out var reason);

            Assert.IsTrue(accepted);
            Assert.AreEqual(BuzzReject.None, reason);
            Assert.AreEqual(0.0, arbiter.Candidates[0].Dt, 1e-12);
            Assert.AreEqual(BuzzClamp.ToT0, arbiter.Candidates[0].Clamp);
        }

        // --- TryResolve ---

        [Test]
        public void TryResolve_BeforeDeadline_ReturnsFalseAndNullResolution()
        {
            var arbiter = NewArbiter();
            var serverNow = T0 + 0.42;
            arbiter.Accept(1, T0 + 0.40, serverNow, out _);

            var resolved = arbiter.TryResolve(serverNow + Window - 0.001, new QueuedRandom(), out var resolution);

            Assert.IsFalse(resolved);
            Assert.IsNull(resolution);
            Assert.AreEqual(BuzzArbiterState.Open, arbiter.State);
        }

        [Test]
        public void TryResolve_WithoutAnyCandidate_ReturnsFalseAndNullResolution()
        {
            var arbiter = NewArbiter();

            var resolved = arbiter.TryResolve(T0 + 10.0, new QueuedRandom(), out var resolution);

            Assert.IsFalse(resolved);
            Assert.IsNull(resolution);
            Assert.AreEqual(BuzzArbiterState.Open, arbiter.State);
        }

        [Test]
        public void TryResolve_AtDeadline_SmallestDtWinsRegardlessOfArrivalOrder()
        {
            var arbiter = NewArbiter();
            var firstArrival = T0 + 0.42;

            // クライアント 1 が先に届くが dt は大きい。クライアント 2 は遅れて届くが dt は小さい。
            arbiter.Accept(1, T0 + 0.40, firstArrival, out _);
            arbiter.Accept(2, T0 + 0.30, firstArrival + 0.12, out _);

            var resolved = arbiter.TryResolve(firstArrival + Window, new QueuedRandom(), out var resolution);

            Assert.IsTrue(resolved);
            Assert.AreEqual(2UL, resolution.Value.WinnerClientId);
            Assert.AreEqual(0.30, resolution.Value.WinnerDt, 1e-9);
            Assert.IsFalse(resolution.Value.WasTie);
            Assert.AreEqual(1, resolution.Value.TiedCount);
            Assert.AreEqual(2, resolution.Value.CandidateCount);
            Assert.AreEqual(BuzzArbiterState.Resolved, arbiter.State);
        }

        [TestCase(0, 1UL)]
        [TestCase(1, 2UL)]
        public void TryResolve_Tie_DrawsWithRandom(int drawnIndex, ulong expectedWinner)
        {
            var arbiter = NewArbiter();
            var firstArrival = T0 + 0.42;
            arbiter.Accept(1, T0 + 0.4000, firstArrival, out _);
            arbiter.Accept(2, T0 + 0.4005, firstArrival + 0.01, out _);   // 差 0.5ms → 同着
            var rng = new QueuedRandom(drawnIndex);

            var resolved = arbiter.TryResolve(firstArrival + Window, rng, out var resolution);

            Assert.IsTrue(resolved);
            Assert.IsTrue(resolution.Value.WasTie);
            Assert.AreEqual(2, resolution.Value.TiedCount);
            Assert.AreEqual(expectedWinner, resolution.Value.WinnerClientId);
            Assert.AreEqual(1, rng.CallCount);
            Assert.AreEqual(2, rng.LastExclusiveMax);
        }

        [Test]
        public void TryResolve_DifferenceAboveTieEpsilon_IsNotTie()
        {
            var arbiter = NewArbiter();
            var firstArrival = T0 + 0.42;
            arbiter.Accept(1, T0 + 0.400, firstArrival, out _);
            arbiter.Accept(2, T0 + 0.402, firstArrival + 0.01, out _);   // 差 2ms（閾値 1ms 超）
            var rng = new QueuedRandom();

            var resolved = arbiter.TryResolve(firstArrival + Window, rng, out var resolution);

            Assert.IsTrue(resolved);
            Assert.IsFalse(resolution.Value.WasTie);
            Assert.AreEqual(1UL, resolution.Value.WinnerClientId);
            Assert.AreEqual(0, rng.CallCount);
        }

        [Test]
        public void TryResolve_DifferenceJustBelowTieEpsilon_IsTie()
        {
            var arbiter = NewArbiter();
            var firstArrival = T0 + 0.42;
            arbiter.Accept(1, T0 + 0.4000, firstArrival, out _);
            arbiter.Accept(2, T0 + 0.4009, firstArrival + 0.01, out _);   // 差 0.9ms（閾値未満）
            var rng = new QueuedRandom(0);

            var resolved = arbiter.TryResolve(firstArrival + Window, rng, out var resolution);

            Assert.IsTrue(resolved);
            Assert.IsTrue(resolution.Value.WasTie);
            Assert.AreEqual(2, resolution.Value.TiedCount);
            Assert.AreEqual(1, rng.CallCount);
        }

        [Test]
        public void TryResolve_ThreeWayTie_DrawsFromAllTied()
        {
            var arbiter = NewArbiter();
            var firstArrival = T0 + 0.42;
            arbiter.Accept(1, T0 + 0.4000, firstArrival, out _);
            arbiter.Accept(2, T0 + 0.4003, firstArrival + 0.01, out _);
            arbiter.Accept(3, T0 + 0.4006, firstArrival + 0.02, out _);
            var rng = new QueuedRandom(2);

            var resolved = arbiter.TryResolve(firstArrival + Window, rng, out var resolution);

            Assert.IsTrue(resolved);
            Assert.IsTrue(resolution.Value.WasTie);
            Assert.AreEqual(3, resolution.Value.TiedCount);
            Assert.AreEqual(3, rng.LastExclusiveMax);
            Assert.AreEqual(3UL, resolution.Value.WinnerClientId);
        }

        [Test]
        public void TryResolve_WinnerClampedToT0_ReportsClampKind()
        {
            var arbiter = NewArbiter();
            var firstArrival = T0 + 0.42;
            arbiter.Accept(1, T0 - 0.03, firstArrival, out _);            // T0 に丸められ dt = 0
            arbiter.Accept(2, T0 + 0.4, firstArrival + 0.01, out _);

            var resolved = arbiter.TryResolve(firstArrival + Window, new QueuedRandom(), out var resolution);

            Assert.IsTrue(resolved);
            Assert.AreEqual(1UL, resolution.Value.WinnerClientId);
            Assert.AreEqual(BuzzClamp.ToT0, resolution.Value.WinnerClamp);
        }

        [Test]
        public void TryResolve_CalledTwice_SecondCallReturnsFalse()
        {
            var arbiter = NewArbiter();
            var firstArrival = T0 + 0.42;
            arbiter.Accept(1, T0 + 0.4, firstArrival, out _);
            Assert.IsTrue(arbiter.TryResolve(firstArrival + Window, new QueuedRandom(), out _));

            var resolvedAgain = arbiter.TryResolve(firstArrival + Window + 1.0, new QueuedRandom(), out var resolution);

            Assert.IsFalse(resolvedAgain);
            Assert.IsNull(resolution);
        }

        [Test]
        public void TryResolve_AfterClose_ReturnsFalse()
        {
            var arbiter = NewArbiter();
            var firstArrival = T0 + 0.42;
            arbiter.Accept(1, T0 + 0.4, firstArrival, out _);
            arbiter.Close();

            var resolved = arbiter.TryResolve(firstArrival + Window, new QueuedRandom(), out var resolution);

            Assert.IsFalse(resolved);
            Assert.IsNull(resolution);
            Assert.AreEqual(BuzzArbiterState.Closed, arbiter.State);
        }

        [Test]
        public void Close_AfterResolve_KeepsResolvedState()
        {
            var arbiter = NewArbiter();
            var firstArrival = T0 + 0.42;
            arbiter.Accept(1, T0 + 0.4, firstArrival, out _);
            arbiter.TryResolve(firstArrival + Window, new QueuedRandom(), out _);

            arbiter.Close();

            Assert.AreEqual(BuzzArbiterState.Resolved, arbiter.State);
        }

        [Test]
        public void TryResolve_NullRandom_Throws()
        {
            var arbiter = NewArbiter();
            arbiter.Accept(1, T0 + 0.4, T0 + 0.42, out _);

            Assert.Throws<ArgumentNullException>(
                () => arbiter.TryResolve(T0 + 0.42 + Window, null, out _));
        }

        [Test]
        public void TryResolve_RandomOutOfRange_Throws()
        {
            var arbiter = NewArbiter();
            var firstArrival = T0 + 0.42;
            arbiter.Accept(1, T0 + 0.4000, firstArrival, out _);
            arbiter.Accept(2, T0 + 0.4005, firstArrival + 0.01, out _);

            Assert.Throws<InvalidOperationException>(
                () => arbiter.TryResolve(firstArrival + Window, new QueuedRandom(5), out _));
        }

        [Test]
        public void TryResolve_NonFiniteServerNow_Throws()
        {
            var arbiter = NewArbiter();
            arbiter.Accept(1, T0 + 0.4, T0 + 0.42, out _);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => arbiter.TryResolve(double.PositiveInfinity, new QueuedRandom(), out _));
        }

        [Test]
        public void TryResolve_ZeroWindow_ResolvesImmediatelyOnSameTimestamp()
        {
            var arbiter = new BuzzArbiter(T0, 0.0);
            var arrival = T0 + 0.42;
            arbiter.Accept(1, T0 + 0.4, arrival, out _);

            var resolved = arbiter.TryResolve(arrival, new QueuedRandom(), out var resolution);

            Assert.IsTrue(resolved);
            Assert.AreEqual(1UL, resolution.Value.WinnerClientId);
        }

        // --- コンストラクタの入力検証 ---

        [TestCase(double.NaN, 0.15)]
        [TestCase(double.PositiveInfinity, 0.15)]
        [TestCase(1000.0, -0.001)]
        [TestCase(1000.0, double.NaN)]
        public void Constructor_InvalidArguments_Throws(double t0, double windowSec)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuzzArbiter(t0, windowSec));
        }

        [TestCase(0.0)]
        [TestCase(-0.001)]
        [TestCase(double.NaN)]
        public void Constructor_NonPositiveTieEpsilon_Throws(double tieEpsilonSec)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuzzArbiter(T0, Window, null, tieEpsilonSec));
        }

        [Test]
        public void Constructor_NullPenalizedList_IsAllowed()
        {
            var arbiter = new BuzzArbiter(T0, Window, null);

            Assert.IsTrue(arbiter.Accept(1, T0 + 0.4, T0 + 0.42, out var reason));
            Assert.AreEqual(BuzzReject.None, reason);
        }
    }
}
