using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine.ForceJudge"/>（司会の「強制正解」「強制不正解」、#20、
    /// docs/tasks/setup-brief.md K18）のテスト。
    /// </summary>
    public class QuizStateMachineModeratorTests : QuizStateMachineTestBase
    {
        [Test]
        public void ForceJudge_Correct_ScoresAsNormalCorrectAnswer()
        {
            var machine = LockAndOpenAnswer(out var answeringStart);

            Assert.IsTrue(machine.ForceJudge(QuizJudgement.Correct, answeringStart + 0.3, out var reason));
            Assert.AreEqual(AnswerReject.None, reason);
            Assert.AreEqual(QuizPhase.Judging, machine.Phase);
            Assert.AreEqual(QuizJudgement.Correct, machine.LastJudgement);

            Assert.AreEqual(QuizEvent.Judged, machine.Tick(answeringStart + 0.4, Rng()));
            Assert.AreEqual(QuizPhase.Result, machine.Phase);
            Assert.AreEqual(QuizStateMachine.DefaultCorrectPoints, machine.GetScore(Client1), "既存の正解の得点付与と同じ経路を通るはず。");
        }

        [Test]
        public void ForceJudge_Wrong_ScoresAsNormalWrongAnswer()
        {
            var machine = LockAndOpenAnswer(
                out var answeringStart, QuizRules.Default.WithReopenAfterWrongAnswer(false));

            Assert.IsTrue(machine.ForceJudge(QuizJudgement.Wrong, answeringStart + 0.3, out _));
            Assert.AreEqual(QuizJudgement.Wrong, machine.LastJudgement);

            machine.Tick(answeringStart + 0.4, Rng());

            Assert.AreEqual(QuizPhase.Result, machine.Phase);
            Assert.AreEqual(QuizStateMachine.DefaultIncorrectPoints, machine.GetScore(Client1));
        }

        [Test]
        public void ForceJudge_OutsideAnswering_IsRejected()
        {
            var machine = OpenBuzz();

            Assert.IsFalse(machine.ForceJudge(QuizJudgement.Correct, T0 + 0.1, out var reason));
            Assert.AreEqual(AnswerReject.NotAnswering, reason);
        }

        [Test]
        public void ForceJudge_WithoutLockedPlayer_IsRejected()
        {
            // BuzzOpen 中（まだロック保持者が居ない）は Answering にすら入っていないため NotAnswering になる。
            // ロック保持者不在で Answering に到達することは通常の進行では起こらないため、
            // ここでは Answering 外の代表例として BuzzOpen を確認する。
            var machine = OpenBuzz();

            Assert.IsFalse(machine.ForceJudge(QuizJudgement.Correct, T0 + 0.1, out var reason));
            Assert.AreEqual(AnswerReject.NotAnswering, reason);
            Assert.AreEqual(QuizStateMachine.NoClientId, machine.LockedClientId);
        }

        [Test]
        public void ForceJudge_WithNoneOrTimedOut_IsRejected()
        {
            var machine = LockAndOpenAnswer(out var answeringStart);

            Assert.IsFalse(machine.ForceJudge(QuizJudgement.None, answeringStart + 0.1, out var noneReason));
            Assert.AreEqual(AnswerReject.InvalidJudgement, noneReason);

            Assert.IsFalse(machine.ForceJudge(QuizJudgement.TimedOut, answeringStart + 0.1, out var timedOutReason));
            Assert.AreEqual(AnswerReject.InvalidJudgement, timedOutReason);
            Assert.AreEqual(QuizPhase.Answering, machine.Phase, "棄却したので回答フェーズのまま。");
        }

        [Test]
        public void ForceJudge_WithNonFiniteTime_IsRejected()
        {
            var machine = LockAndOpenAnswer(out _);

            Assert.IsFalse(machine.ForceJudge(QuizJudgement.Correct, double.NaN, out var reason));
            Assert.AreEqual(AnswerReject.NonFiniteTime, reason);
        }

        [Test]
        public void ForceJudge_WhilePaused_IsRejected()
        {
            var machine = LockAndOpenAnswer(out var answeringStart);
            Assert.IsTrue(machine.Pause(answeringStart + 0.1, out _));

            Assert.IsFalse(machine.ForceJudge(QuizJudgement.Correct, answeringStart + 0.2, out var reason));
            Assert.AreEqual(AnswerReject.Paused, reason);
        }
    }
}
