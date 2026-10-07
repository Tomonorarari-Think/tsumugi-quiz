using System;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine"/> の出題・読み上げ（T0 の指定）・早押しのテスト
    /// （docs/network.md §6.3 / §6.6）。回答以降は <see cref="QuizStateMachineAnswerTests"/>。
    /// </summary>
    public class QuizStateMachineTests : QuizStateMachineTestBase
    {
        // --- 初期状態・出題 ---

        [Test]
        public void NewMachine_IsInLobby()
        {
            var machine = new QuizStateMachine();

            Assert.AreEqual(QuizPhase.Lobby, machine.Phase);
            Assert.AreEqual(-1, machine.QuestionIndex);
            Assert.AreEqual(QuizStateMachine.NoClientId, machine.LockedClientId);
            Assert.AreEqual(QuizJudgement.None, machine.LastJudgement);
            Assert.AreEqual(string.Empty, machine.CorrectAnswer);
            CollectionAssert.AreEqual(new[] { QuizPhase.Lobby }, machine.PhaseHistory);
        }

        [Test]
        public void StartQuestion_FromLobby_EntersReading()
        {
            var machine = new QuizStateMachine();

            var started = machine.StartQuestion(3, Answers, T0, out var reason);

            Assert.IsTrue(started);
            Assert.AreEqual(QuizReject.None, reason);
            Assert.AreEqual(QuizPhase.Reading, machine.Phase);
            Assert.AreEqual(3, machine.QuestionIndex);
            Assert.AreEqual(T0, machine.PhaseStartServerTime, 1e-9);
            Assert.AreEqual("とうきょう", machine.CorrectAnswer);
        }

        [Test]
        public void StartQuestion_WithoutAnswers_IsRejected()
        {
            var machine = new QuizStateMachine();

            Assert.IsFalse(machine.StartQuestion(0, Array.Empty<string>(), T0, out var empty));
            Assert.AreEqual(QuizReject.NoAnswers, empty);

            Assert.IsFalse(machine.StartQuestion(0, null, T0, out var missing));
            Assert.AreEqual(QuizReject.NoAnswers, missing);

            Assert.IsFalse(machine.StartQuestion(0, new[] { "  " }, T0, out var blank));
            Assert.AreEqual(QuizReject.NoAnswers, blank);

            Assert.AreEqual(QuizPhase.Lobby, machine.Phase);
        }

        [Test]
        public void StartQuestion_WithNegativeIndex_IsRejected()
        {
            var machine = new QuizStateMachine();

            Assert.IsFalse(machine.StartQuestion(-1, Answers, T0, out var reason));
            Assert.AreEqual(QuizReject.InvalidQuestionIndex, reason);
        }

        [Test]
        public void StartQuestion_WhileReading_IsRejected()
        {
            var machine = new QuizStateMachine();
            machine.StartQuestion(0, Answers, T0, out _);

            Assert.IsFalse(machine.StartQuestion(1, Answers, T0 + 1.0, out var reason));
            Assert.AreEqual(QuizReject.InvalidPhase, reason);
            Assert.AreEqual(0, machine.QuestionIndex);
        }

        [Test]
        public void StartQuestion_WithNonFiniteTime_IsRejected()
        {
            var machine = new QuizStateMachine();

            Assert.IsFalse(machine.StartQuestion(0, Answers, double.NaN, out var reason));
            Assert.AreEqual(QuizReject.NonFiniteTime, reason);
        }

        // --- Reading → BuzzOpen（読み上げ完了フック） ---

        [Test]
        public void Tick_WithoutTts_OpensBuzzImmediatelyWithT0AtQuestionStart()
        {
            var machine = new QuizStateMachine();
            machine.StartQuestion(0, Answers, T0, out _);

            var result = machine.Tick(T0, Rng());

            Assert.AreEqual(QuizEvent.BuzzOpened, result);
            Assert.AreEqual(QuizPhase.BuzzOpen, machine.Phase);
            Assert.AreEqual(T0, machine.BuzzOpenServerTime, 1e-9);
        }

        [Test]
        public void SetBuzzOpenTime_PostponesBuzzOpenUntilReadingEnds()
        {
            var machine = new QuizStateMachine();
            machine.StartQuestion(0, Answers, T0, out _);

            Assert.IsTrue(machine.SetBuzzOpenTime(T0 + 4.0, out var reason));
            Assert.AreEqual(QuizReject.None, reason);

            Assert.AreEqual(QuizEvent.None, machine.Tick(T0 + 3.9, Rng()));
            Assert.AreEqual(QuizPhase.Reading, machine.Phase);

            Assert.AreEqual(QuizEvent.BuzzOpened, machine.Tick(T0 + 4.05, Rng()));

            // T0 は「読み上げ完了時刻」であり、tick が回った時刻ではない（docs/network.md §6.3）。
            Assert.AreEqual(T0 + 4.0, machine.BuzzOpenServerTime, 1e-9);
        }

        [Test]
        public void SetBuzzOpenTime_OutsideReading_IsRejected()
        {
            var machine = OpenBuzz();

            Assert.IsFalse(machine.SetBuzzOpenTime(T0 + 1.0, out var reason));
            Assert.AreEqual(QuizReject.InvalidPhase, reason);
        }

        [Test]
        public void SetBuzzOpenTime_BeforeQuestionStart_IsRejected()
        {
            var machine = new QuizStateMachine();
            machine.StartQuestion(0, Answers, T0, out _);

            Assert.IsFalse(machine.SetBuzzOpenTime(T0 - 0.001, out var reason));
            Assert.AreEqual(QuizReject.InvalidTime, reason);

            // 出題時刻ちょうどは受理する（読み上げ無しの既定と同じ）。
            Assert.IsTrue(machine.SetBuzzOpenTime(T0, out var ok));
            Assert.AreEqual(QuizReject.None, ok);
        }

        [Test]
        public void SetBuzzOpenTime_BeyondMaxReadingDuration_IsRejected()
        {
            var machine = new QuizStateMachine();
            machine.StartQuestion(0, Answers, T0, out _);

            var limit = QuizStateMachine.MaxReadingDurationSec;
            Assert.IsTrue(machine.SetBuzzOpenTime(T0 + limit, out var atLimit), "上限ちょうどは受理する。");
            Assert.AreEqual(QuizReject.None, atLimit);

            Assert.IsFalse(machine.SetBuzzOpenTime(T0 + limit + 0.001, out var reason));
            Assert.AreEqual(QuizReject.InvalidTime, reason);
        }

        [Test]
        public void SetBuzzOpenTime_WithNonFiniteTime_IsRejected()
        {
            var machine = new QuizStateMachine();
            machine.StartQuestion(0, Answers, T0, out _);

            Assert.IsFalse(machine.SetBuzzOpenTime(double.PositiveInfinity, out var reason));
            Assert.AreEqual(QuizReject.NonFiniteTime, reason);
        }

        // --- 早押し ---

        [Test]
        public void AcceptBuzz_BeforeBuzzOpen_IsRejectedAsNotOpen()
        {
            var machine = new QuizStateMachine();
            machine.StartQuestion(0, Answers, T0, out _);

            Assert.IsFalse(machine.AcceptBuzz(Client1, T0, T0, out var reason));
            Assert.AreEqual(BuzzReject.NotOpen, reason);
        }

        [Test]
        public void AcceptBuzz_SameClientTwice_IsRejectedAsDuplicate()
        {
            var machine = OpenBuzz();

            Assert.IsTrue(machine.AcceptBuzz(Client1, T0 + 0.4, T0 + 0.42, out _));
            Assert.IsFalse(machine.AcceptBuzz(Client1, T0 + 0.5, T0 + 0.52, out var reason));
            Assert.AreEqual(BuzzReject.Duplicate, reason);
        }

        [Test]
        public void Tick_AfterCollectWindow_LocksFastestClient()
        {
            var machine = OpenBuzz();
            machine.AcceptBuzz(Client1, T0 + 0.40, T0 + 0.45, out _);
            machine.AcceptBuzz(Client2, T0 + 0.30, T0 + 0.50, out _);

            Assert.AreEqual(QuizEvent.None, machine.Tick(T0 + 0.45 + Window - 0.01, Rng()));
            Assert.AreEqual(QuizPhase.BuzzOpen, machine.Phase);

            var result = machine.Tick(T0 + 0.45 + Window, Rng());

            Assert.AreEqual(QuizEvent.BuzzResolved, result);
            Assert.AreEqual(QuizPhase.Locked, machine.Phase);
            Assert.AreEqual(Client2, machine.LockedClientId);
            Assert.IsTrue(machine.LastResolution.HasValue);
            Assert.AreEqual(0.30, machine.LastResolution.Value.WinnerDt, 1e-9);
            Assert.IsFalse(machine.LastResolution.Value.WasTie);
        }

        [Test]
        public void AcceptBuzz_AfterLocked_IsRejectedAsNotOpen()
        {
            var machine = OpenBuzz();
            machine.AcceptBuzz(Client1, T0 + 0.4, T0 + 0.42, out _);
            machine.Tick(T0 + 0.42 + Window, Rng());

            Assert.IsFalse(machine.AcceptBuzz(Client2, T0 + 0.5, T0 + 0.7, out var reason));
            Assert.AreEqual(BuzzReject.NotOpen, reason);
        }

        [Test]
        public void Tick_NobodyBuzzedUntilTimeLimit_GoesToResultAsTimedOut()
        {
            var machine = OpenBuzz();
            var limit = QuizTimeLimits.DefaultBuzzTimeLimitSec;

            Assert.AreEqual(QuizEvent.None, machine.Tick(T0 + limit - 0.01, Rng()));

            var result = machine.Tick(T0 + limit, Rng());

            Assert.AreEqual(QuizEvent.BuzzTimedOut, result);
            Assert.AreEqual(QuizPhase.Result, machine.Phase);
            Assert.AreEqual(QuizJudgement.TimedOut, machine.LastJudgement);
            Assert.AreEqual(QuizStateMachine.NoClientId, machine.LockedClientId);
            CollectionAssert.AreEqual(
                new[] { QuizPhase.Lobby, QuizPhase.Reading, QuizPhase.BuzzOpen, QuizPhase.Result },
                machine.PhaseHistory);
        }

        [Test]
        public void Tick_BuzzedJustBeforeTimeLimit_ResolvesInsteadOfTimingOut()
        {
            var machine = OpenBuzz();
            var limit = QuizTimeLimits.DefaultBuzzTimeLimitSec;

            // 締め切り直前の押下。集計窓は制限時間を越えるが、タイムアウトにしてはいけない。
            machine.AcceptBuzz(Client1, T0 + limit - 0.02, T0 + limit - 0.01, out var reason);
            Assert.AreEqual(BuzzReject.None, reason);

            var result = machine.Tick(T0 + limit + 0.2, Rng());

            Assert.AreEqual(QuizEvent.BuzzResolved, result);
            Assert.AreEqual(Client1, machine.LockedClientId);
        }

        /// <summary>ホスト自身（クライアント ID 0）も押下できることの確認。</summary>
        [Test]
        public void AcceptBuzz_HostClientId_IsAccepted()
        {
            var machine = OpenBuzz();

            Assert.IsTrue(machine.AcceptBuzz(Host, T0 + 0.2, T0 + 0.21, out var reason));
            Assert.AreEqual(BuzzReject.None, reason);

            machine.Tick(T0 + 0.21 + Window, Rng());
            Assert.AreEqual(Host, machine.LockedClientId);
        }

        /// <summary>フェーズ履歴は診断用に読み取り専用で公開する。</summary>
        [Test]
        public void PhaseHistory_IsReadOnly()
        {
            var machine = new QuizStateMachine();

            Assert.IsInstanceOf<IReadOnlyList<QuizPhase>>(machine.PhaseHistory);
            Assert.Throws<NotSupportedException>(() => ((IList<QuizPhase>)machine.PhaseHistory).Add(QuizPhase.Result));
        }
    }
}
