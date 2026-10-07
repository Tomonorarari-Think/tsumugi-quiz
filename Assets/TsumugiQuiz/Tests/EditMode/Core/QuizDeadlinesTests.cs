using System;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="QuizDeadlines"/> のテスト（issue #154）。
    /// フェーズごとに対応する制限時間の設定値で締め切りを求めること、
    /// およびサーバーの状態機械のタイムアウトと同じ時刻を指すことを確かめる。
    /// </summary>
    public class QuizDeadlinesTests : QuizStateMachineTestBase
    {
        private const double PhaseStart = 500.0;
        private const double BuzzOpen = 520.0;

        /// <summary>既定値（10 / 15 / 20 秒）と全部違う値にした制限時間。</summary>
        private static QuizTimeLimits CustomLimits =>
            new QuizTimeLimits(
                buzzTimeLimitSec: 42.0, answerTimeLimitSec: 7.0, choiceTimeLimitSec: 33.0, collectWindowSec: Window);

        [Test]
        public void DeadlineServerTime_BuzzOpen_UsesBuzzTimeLimitFromT0()
        {
            var deadline = QuizDeadlines.DeadlineServerTime(QuizPhase.BuzzOpen, PhaseStart, BuzzOpen, CustomLimits);

            Assert.AreEqual(BuzzOpen + 42.0, deadline, 1e-9);
        }

        [Test]
        public void DeadlineServerTime_Answering_UsesAnswerTimeLimitFromPhaseStart()
        {
            var deadline = QuizDeadlines.DeadlineServerTime(QuizPhase.Answering, PhaseStart, BuzzOpen, CustomLimits);

            Assert.AreEqual(PhaseStart + 7.0, deadline, 1e-9);
        }

        [Test]
        public void DeadlineServerTime_ChoiceAnswering_UsesChoiceTimeLimitFromBuzzOpen()
        {
            var deadline = QuizDeadlines.DeadlineServerTime(
                QuizPhase.ChoiceAnswering, PhaseStart, BuzzOpen, CustomLimits);

            Assert.AreEqual(BuzzOpen + 33.0, deadline, 1e-9);
        }

        [TestCase(QuizPhase.Lobby)]
        [TestCase(QuizPhase.Reading)]
        [TestCase(QuizPhase.Locked)]
        [TestCase(QuizPhase.Judging)]
        [TestCase(QuizPhase.Result)]
        [TestCase(QuizPhase.Finished)]
        public void DeadlineServerTime_PhaseWithoutDeadline_ReturnsNaN(QuizPhase phase)
        {
            Assert.IsNaN(QuizDeadlines.DeadlineServerTime(phase, PhaseStart, BuzzOpen, CustomLimits));
            Assert.IsNaN(QuizDeadlines.RemainingSeconds(phase, PhaseStart, BuzzOpen, CustomLimits, PhaseStart));
        }

        [Test]
        public void DeadlineServerTime_NullLimits_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => QuizDeadlines.DeadlineServerTime(QuizPhase.BuzzOpen, PhaseStart, BuzzOpen, null));
        }

        // issue #178: 旧 GameViewPresenter.SelectIntervalStart の起点選択ロジックをこちらへ一本化した。
        // DeadlineServerTime の起点選択（BuzzOpen / ChoiceAnswering は T0、それ以外はフェーズ開始時刻）と同じ規則。

        [Test]
        public void IntervalStartServerTime_ReturnsBuzzOpenServerTime_DuringBuzzOpen()
        {
            Assert.AreEqual(
                BuzzOpen,
                QuizDeadlines.IntervalStartServerTime(QuizPhase.BuzzOpen, phaseStartServerTime: PhaseStart, buzzOpenServerTime: BuzzOpen));
        }

        [TestCase(QuizPhase.Answering)]
        [TestCase(QuizPhase.Reading)]
        [TestCase(QuizPhase.Locked)]
        public void IntervalStartServerTime_ReturnsPhaseStartServerTime_OutsideBuzzOpenAndChoiceAnswering(QuizPhase phase)
        {
            Assert.AreEqual(
                PhaseStart,
                QuizDeadlines.IntervalStartServerTime(phase, phaseStartServerTime: PhaseStart, buzzOpenServerTime: BuzzOpen));
        }

        [Test]
        public void IntervalStartServerTime_ReturnsBuzzOpenServerTime_DuringChoiceAnswering()
        {
            // レビュー M5（#178 で移設）: 選択式（issue #17）は DeadlineServerTime が締め切りの起点に
            // BuzzOpenServerTime を使う（早押しの T0 と同じ枠を使い回している）ため、
            // 残り時間バーの開始位置もそれに合わせる。
            Assert.AreEqual(
                BuzzOpen,
                QuizDeadlines.IntervalStartServerTime(
                    QuizPhase.ChoiceAnswering, phaseStartServerTime: PhaseStart, buzzOpenServerTime: BuzzOpen));
        }

        [Test]
        public void RemainingSeconds_AtStart_EqualsConfiguredLimit_NotDefault()
        {
            var remaining = QuizDeadlines.RemainingSeconds(
                QuizPhase.BuzzOpen, PhaseStart, BuzzOpen, CustomLimits, BuzzOpen);

            Assert.AreEqual(42.0, remaining, 1e-9);
            Assert.AreNotEqual(QuizTimeLimits.DefaultBuzzTimeLimitSec, remaining);
        }

        [Test]
        public void RemainingSeconds_CountsDownAndClampsAtZero()
        {
            Assert.AreEqual(
                4.5,
                QuizDeadlines.RemainingSeconds(QuizPhase.Answering, PhaseStart, BuzzOpen, CustomLimits, PhaseStart + 2.5),
                1e-9);
            Assert.AreEqual(
                0.0,
                QuizDeadlines.RemainingSeconds(QuizPhase.Answering, PhaseStart, BuzzOpen, CustomLimits, PhaseStart + 100.0));
        }

        [Test]
        public void DeadlineServerTime_BuzzOpen_MatchesStateMachineTimeout()
        {
            var limits = CustomLimits;
            var machine = OpenBuzz(limits);
            var deadline = QuizDeadlines.DeadlineServerTime(
                machine.Phase, machine.PhaseStartServerTime, machine.BuzzOpenServerTime, limits);

            Assert.AreEqual(QuizEvent.None, machine.Tick(deadline - 0.01, Rng()));
            Assert.AreEqual(QuizPhase.BuzzOpen, machine.Phase);
            Assert.AreEqual(QuizEvent.BuzzTimedOut, machine.Tick(deadline, Rng()));
        }

        [Test]
        public void DeadlineServerTime_Answering_MatchesStateMachineTimeout()
        {
            var limits = CustomLimits;
            var machine = LockAndOpenAnswer(out _, limits: limits);
            var deadline = QuizDeadlines.DeadlineServerTime(
                machine.Phase, machine.PhaseStartServerTime, machine.BuzzOpenServerTime, limits);

            Assert.AreEqual(QuizEvent.None, machine.Tick(deadline - 0.01, Rng()));
            Assert.AreEqual(QuizPhase.Answering, machine.Phase);
            Assert.AreEqual(QuizEvent.AnswerTimedOut, machine.Tick(deadline, Rng()));
        }

        [Test]
        public void DeadlineServerTime_AfterPauseAndResume_ShiftsByPausedDuration()
        {
            var limits = CustomLimits;
            var machine = OpenBuzz(limits);
            var before = QuizDeadlines.DeadlineServerTime(
                machine.Phase, machine.PhaseStartServerTime, machine.BuzzOpenServerTime, limits);

            Assert.IsTrue(machine.Pause(T0 + 1.0, out _));
            Assert.IsTrue(machine.Resume(T0 + 31.0, out _));

            var after = QuizDeadlines.DeadlineServerTime(
                machine.Phase, machine.PhaseStartServerTime, machine.BuzzOpenServerTime, limits);
            Assert.AreEqual(before + 30.0, after, 1e-9, "一時停止していた 30 秒ぶん締め切りが後ろへずれるはず。");
        }

        [Test]
        public void DeadlineServerTime_ChoiceAnswering_MatchesStateMachineTimeout()
        {
            var limits = CustomLimits;
            var machine = new QuizStateMachine(limits);
            Assert.IsTrue(machine.StartQuestion(0, 1, 4, T0, out _));
            Assert.AreEqual(QuizEvent.ChoiceAnsweringOpened, machine.Tick(T0, Rng()));
            var deadline = QuizDeadlines.DeadlineServerTime(
                machine.Phase, machine.PhaseStartServerTime, machine.BuzzOpenServerTime, limits);

            Assert.AreEqual(T0 + 33.0, deadline, 1e-9);
            Assert.AreEqual(QuizEvent.None, machine.Tick(deadline - 0.01, Rng()));
            Assert.AreEqual(QuizPhase.ChoiceAnswering, machine.Phase);
            Assert.AreEqual(QuizEvent.ChoiceTimedOut, machine.Tick(deadline, Rng()));
        }

        [Test]
        public void DeadlineServerTime_AfterBuzzReopened_StaysAtT0PlusBuzzLimitAndMatchesTimeout()
        {
            // 誤答後の再開放（buzz.reopenAfterWrongAnswer、既定 true）は T0 を据え置くので、
            // 締め切りも最初の受付と同じ T0 + buzz.timeLimitSec のまま（docs/network.md §6.6）。
            var limits = CustomLimits;
            var machine = LockAndOpenAnswer(out var answeringStart, limits: limits);
            var originalDeadline = T0 + 42.0;

            Assert.IsTrue(machine.SubmitAnswer(Client1, "おおさか", answeringStart + 0.2, out _));
            Assert.AreEqual(QuizEvent.BuzzReopened, machine.Tick(answeringStart + 0.3, Rng()));
            Assert.AreEqual(QuizPhase.BuzzOpen, machine.Phase);

            var deadline = QuizDeadlines.DeadlineServerTime(
                machine.Phase, machine.PhaseStartServerTime, machine.BuzzOpenServerTime, limits);
            Assert.AreEqual(originalDeadline, deadline, 1e-9, "再開放しても締め切りは T0 + buzz.timeLimitSec のまま。");

            Assert.AreEqual(QuizEvent.None, machine.Tick(deadline - 0.01, Rng()));
            Assert.AreEqual(QuizPhase.BuzzOpen, machine.Phase);
            Assert.AreEqual(QuizEvent.BuzzTimedOut, machine.Tick(deadline, Rng()));
        }

        [TestCase(QuizPhase.BuzzOpen, double.NaN)]
        [TestCase(QuizPhase.BuzzOpen, double.PositiveInfinity)]
        [TestCase(QuizPhase.Answering, double.NegativeInfinity)]
        [TestCase(QuizPhase.ChoiceAnswering, double.PositiveInfinity)]
        public void DeadlineServerTime_NonFiniteAnchor_ReturnsNaN(QuizPhase phase, double anchor)
        {
            Assert.IsNaN(QuizDeadlines.DeadlineServerTime(phase, anchor, anchor, CustomLimits));
            Assert.IsNaN(QuizDeadlines.RemainingSeconds(phase, anchor, anchor, CustomLimits, PhaseStart));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void RemainingSeconds_NonFiniteCurrentTime_ReturnsNaN(double currentServerTime)
        {
            // 現在時刻が壊れていても「残り ∞ 秒」や誤った 0 を出さず、不明（NaN）として返す。
            Assert.IsNaN(
                QuizDeadlines.RemainingSeconds(
                    QuizPhase.BuzzOpen, PhaseStart, BuzzOpen, CustomLimits, currentServerTime));
        }
    }
}
