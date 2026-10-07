using System;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine"/> の回答・判定・得点・1 問の通しのテスト
    /// （docs/network.md §6.6、§9 の入力検証）。
    /// </summary>
    public class QuizStateMachineAnswerTests : QuizStateMachineTestBase
    {
        // --- 回答 ---

        [Test]
        public void SubmitAnswer_FromNonWinner_IsRejectedAndKeepsAnswering()
        {
            var machine = LockAndOpenAnswer(out var answeringStart);

            Assert.IsFalse(machine.SubmitAnswer(Client2, "とうきょう", answeringStart + 0.1, out var reason));
            Assert.AreEqual(AnswerReject.NotLockedPlayer, reason);
            Assert.AreEqual(QuizPhase.Answering, machine.Phase);
            Assert.AreEqual(QuizJudgement.None, machine.LastJudgement);
        }

        [Test]
        public void SubmitAnswer_OutsideAnswering_IsRejected()
        {
            var machine = OpenBuzz();

            Assert.IsFalse(machine.SubmitAnswer(Client1, "とうきょう", T0 + 0.1, out var reason));
            Assert.AreEqual(AnswerReject.NotAnswering, reason);
        }

        [Test]
        public void SubmitAnswer_EmptyText_IsRejected()
        {
            var machine = LockAndOpenAnswer(out var answeringStart);

            Assert.IsFalse(machine.SubmitAnswer(Client1, "   ", answeringStart + 0.1, out var reason));
            Assert.AreEqual(AnswerReject.Empty, reason);
            Assert.AreEqual(QuizPhase.Answering, machine.Phase);
        }

        [Test]
        public void SubmitAnswer_WithControlCharacter_IsRejected()
        {
            var machine = LockAndOpenAnswer(out var answeringStart);

            Assert.IsFalse(machine.SubmitAnswer(Client1, "とう\nきょう", answeringStart + 0.1, out var reason));
            Assert.AreEqual(AnswerReject.InvalidCharacter, reason);
            Assert.AreEqual(QuizPhase.Answering, machine.Phase);
        }

        [Test]
        public void SubmitAnswer_TooLongText_IsRejected()
        {
            var machine = LockAndOpenAnswer(out var answeringStart);
            var tooLong = new string('あ', QuizStateMachine.MaxAnswerLength + 1);

            Assert.IsFalse(machine.SubmitAnswer(Client1, tooLong, answeringStart + 0.1, out var reason));
            Assert.AreEqual(AnswerReject.TooLong, reason);

            var atLimit = new string('あ', QuizStateMachine.MaxAnswerLength);
            Assert.IsTrue(machine.SubmitAnswer(Client1, atLimit, answeringStart + 0.2, out var ok));
            Assert.AreEqual(AnswerReject.None, ok);
        }

        [Test]
        public void SubmitAnswer_WithNonFiniteTime_IsRejected()
        {
            var machine = LockAndOpenAnswer(out _);

            Assert.IsFalse(machine.SubmitAnswer(Client1, "とうきょう", double.NaN, out var reason));
            Assert.AreEqual(AnswerReject.NonFiniteTime, reason);
            Assert.AreEqual(QuizPhase.Answering, machine.Phase, "棄却したので回答フェーズのまま。");
            Assert.AreEqual(QuizJudgement.None, machine.LastJudgement);
            Assert.AreEqual(string.Empty, machine.LastAnswerText);
        }

        [Test]
        public void SubmitAnswer_CorrectAfterNormalization_ScoresTenAtResult()
        {
            var machine = LockAndOpenAnswer(out var answeringStart);

            // カタカナで入力しても AnswerNormalizer 経由で一致する（docs/question-data.md §5）。
            Assert.IsTrue(machine.SubmitAnswer(Client1, "トウキョウ", answeringStart + 0.5, out _));
            Assert.AreEqual(QuizPhase.Judging, machine.Phase);
            Assert.AreEqual(QuizJudgement.Correct, machine.LastJudgement);
            Assert.AreEqual(0, machine.GetScore(Client1), "得点は Result への遷移で反映する。");

            var result = machine.Tick(answeringStart + 0.6, Rng());

            Assert.AreEqual(QuizEvent.Judged, result);
            Assert.AreEqual(QuizPhase.Result, machine.Phase);
            Assert.AreEqual(QuizStateMachine.DefaultCorrectPoints, machine.GetScore(Client1));
        }

        [Test]
        public void SubmitAnswer_Wrong_KeepsScoreAtZero()
        {
            // 誤答後の受付再開放（#18、既定 true）は別テスト（QuizStateMachinePenaltyTests）で見るので、
            // ここでは Result までの得点だけを確かめる。
            var machine = LockAndOpenAnswer(out var answeringStart, QuizRules.Default.WithReopenAfterWrongAnswer(false));

            Assert.IsTrue(machine.SubmitAnswer(Client1, "おおさか", answeringStart + 0.5, out _));
            Assert.AreEqual(QuizJudgement.Wrong, machine.LastJudgement);

            machine.Tick(answeringStart + 0.6, Rng());

            Assert.AreEqual(QuizPhase.Result, machine.Phase);
            Assert.AreEqual(QuizStateMachine.DefaultIncorrectPoints, machine.GetScore(Client1));
        }

        [Test]
        public void Tick_AnswerTimeLimitExceeded_JudgesAsWrong()
        {
            var machine = LockAndOpenAnswer(out var answeringStart);
            var limit = QuizTimeLimits.DefaultAnswerTimeLimitSec;

            Assert.AreEqual(QuizEvent.None, machine.Tick(answeringStart + limit - 0.01, Rng()));
            Assert.AreEqual(QuizPhase.Answering, machine.Phase);

            Assert.AreEqual(QuizEvent.AnswerTimedOut, machine.Tick(answeringStart + limit, Rng()));
            Assert.AreEqual(QuizPhase.Judging, machine.Phase);
            Assert.AreEqual(QuizJudgement.Wrong, machine.LastJudgement);

            Assert.AreEqual(QuizEvent.Judged, machine.Tick(answeringStart + limit + 0.1, Rng()));
            Assert.AreEqual(QuizPhase.Result, machine.Phase);
        }

        // --- 1 問の通し ---

        [Test]
        public void FullQuestion_FollowsStateDiagramAndFinishes()
        {
            var machine = new QuizStateMachine();
            var rng = Rng();

            Assert.IsTrue(machine.StartQuestion(0, Answers, T0, out _));
            Assert.AreEqual(QuizEvent.BuzzOpened, machine.Tick(T0, rng));
            Assert.IsTrue(machine.AcceptBuzz(Client1, T0 + 0.4, T0 + 0.42, out _));
            Assert.AreEqual(QuizEvent.BuzzResolved, machine.Tick(T0 + 0.42 + Window, rng));
            Assert.AreEqual(QuizEvent.AnswerOpened, machine.Tick(T0 + 0.6, rng));
            Assert.IsTrue(machine.SubmitAnswer(Client1, "東京", T0 + 1.2, out _));
            Assert.AreEqual(QuizEvent.Judged, machine.Tick(T0 + 1.25, rng));
            Assert.IsTrue(machine.Finish(T0 + 5.0, out _));

            CollectionAssert.AreEqual(
                new[]
                {
                    QuizPhase.Lobby,
                    QuizPhase.Reading,
                    QuizPhase.BuzzOpen,
                    QuizPhase.Locked,
                    QuizPhase.Answering,
                    QuizPhase.Judging,
                    QuizPhase.Result,
                    QuizPhase.Finished,
                },
                machine.PhaseHistory);
            Assert.AreEqual(QuizJudgement.Correct, machine.LastJudgement);
            Assert.AreEqual(QuizStateMachine.DefaultCorrectPoints, machine.GetScore(Client1));
        }

        [Test]
        public void Tick_PerformsAtMostOneTransition()
        {
            var machine = OpenBuzz();
            machine.AcceptBuzz(Client1, T0 + 0.4, T0 + 0.42, out _);

            // 同じ時刻で何度呼んでも 1 tick = 1 遷移。Locked を飛ばして Answering にはしない。
            var late = T0 + 10.0;
            Assert.AreEqual(QuizEvent.BuzzResolved, machine.Tick(late, Rng()));
            Assert.AreEqual(QuizPhase.Locked, machine.Phase);
            Assert.AreEqual(QuizEvent.AnswerOpened, machine.Tick(late, Rng()));
            Assert.AreEqual(QuizPhase.Answering, machine.Phase);
        }

        [Test]
        public void Finish_OutsideResult_IsRejected()
        {
            var machine = OpenBuzz();

            Assert.IsFalse(machine.Finish(T0 + 1.0, out var reason));
            Assert.AreEqual(QuizReject.InvalidPhase, reason);
            Assert.AreEqual(QuizPhase.BuzzOpen, machine.Phase);
        }

        [Test]
        public void Tick_InLobbyOrFinished_DoesNothing()
        {
            var machine = new QuizStateMachine();
            Assert.AreEqual(QuizEvent.None, machine.Tick(T0, Rng()));

            machine.StartQuestion(0, Answers, T0, out _);
            machine.Tick(T0, Rng());
            machine.Tick(T0 + QuizTimeLimits.DefaultBuzzTimeLimitSec, Rng());
            Assert.IsTrue(machine.Finish(T0 + 11.0, out _));

            Assert.AreEqual(QuizEvent.None, machine.Tick(T0 + 12.0, Rng()));
            Assert.AreEqual(QuizPhase.Finished, machine.Phase);
        }

        [Test]
        public void StartQuestion_FromResult_ResetsPerQuestionState()
        {
            var machine = LockAndOpenAnswer(out var answeringStart);
            machine.SubmitAnswer(Client1, "東京", answeringStart + 0.2, out _);
            machine.Tick(answeringStart + 0.3, Rng());
            Assert.AreEqual(QuizPhase.Result, machine.Phase);

            var next = answeringStart + 1.0;
            Assert.IsTrue(machine.StartQuestion(1, new[] { "おおさか" }, next, out var reason));

            Assert.AreEqual(QuizReject.None, reason);
            Assert.AreEqual(QuizPhase.Reading, machine.Phase);
            Assert.AreEqual(1, machine.QuestionIndex);
            Assert.AreEqual(QuizStateMachine.NoClientId, machine.LockedClientId);
            Assert.AreEqual(QuizJudgement.None, machine.LastJudgement);
            Assert.IsNull(machine.LastResolution);
            Assert.AreEqual("おおさか", machine.CorrectAnswer);
            Assert.AreEqual(0.0, machine.BuzzOpenServerTime, 1e-9);

            // 得点は問題をまたいで保持する。
            Assert.AreEqual(QuizStateMachine.DefaultCorrectPoints, machine.GetScore(Client1));
        }

        [Test]
        public void Tick_WithNonFiniteTime_Throws()
        {
            var machine = OpenBuzz();

            Assert.Throws<ArgumentOutOfRangeException>(() => machine.Tick(double.NaN, Rng()));
        }

        [Test]
        public void Tick_WithoutRandom_Throws()
        {
            var machine = OpenBuzz();

            Assert.Throws<ArgumentNullException>(() => machine.Tick(T0 + 0.1, null));
        }

        [Test]
        public void Limits_CanBeOverriddenForShortRounds()
        {
            var limits = new QuizTimeLimits(buzzTimeLimitSec: 1.0, answerTimeLimitSec: 2.0, collectWindowSec: 0.05);
            var machine = OpenBuzz(limits);

            Assert.AreEqual(QuizEvent.BuzzTimedOut, machine.Tick(T0 + 1.0, Rng()));
        }

        [Test]
        public void QuizTimeLimits_RejectsOutOfRangeValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new QuizTimeLimits(0.0, 15.0, 0.15));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QuizTimeLimits(10.0, double.NaN, 0.15));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QuizTimeLimits(10.0, 15.0, -0.01));
        }
    }
}
