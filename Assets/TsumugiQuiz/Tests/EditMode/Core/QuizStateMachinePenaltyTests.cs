using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 誤答・お手つきの扱い（受付の再開放・次問休み・減点・回答の再入力）のテスト
    /// （docs/network.md §6.6、docs/room-settings.md §1、仮決め K19、#18）。
    /// </summary>
    public class QuizStateMachinePenaltyTests : QuizStateMachineTestBase
    {
        private const double BuzzLimit = QuizTimeLimits.DefaultBuzzTimeLimitSec;   // 10

        /// <summary>誤答で Judging に入った状態のマシンを返す（<see cref="QuizStateMachineTestBase.Client1"/> が誤答）。</summary>
        private static QuizStateMachine WrongAnswer(out double wrongAt, QuizRules rules = null)
        {
            var machine = LockAndOpenAnswer(out var answeringStart, rules);
            wrongAt = answeringStart + 0.2;
            Assert.IsTrue(machine.SubmitAnswer(Client1, "おおさか", wrongAt, out _));
            Assert.AreEqual(QuizPhase.Judging, machine.Phase);
            Assert.AreEqual(QuizJudgement.Wrong, machine.LastJudgement);
            return machine;
        }

        // --- 誤答後の再開放（buzz.reopenAfterWrongAnswer） ---

        [Test]
        public void Wrong_WithReopenEnabled_ReturnsToBuzzOpenWithSameT0()
        {
            var machine = WrongAnswer(out var wrongAt);

            var reopenAt = wrongAt + 0.1;
            Assert.AreEqual(QuizEvent.BuzzReopened, machine.Tick(reopenAt, Rng()));

            Assert.AreEqual(QuizPhase.BuzzOpen, machine.Phase);
            Assert.AreEqual(T0, machine.BuzzOpenServerTime, 1e-9, "T0 は据え置き（残り時間は減り続ける）。");
            Assert.AreEqual(reopenAt, machine.PhaseStartServerTime, 1e-9);
            Assert.AreEqual(QuizStateMachine.NoClientId, machine.LockedClientId, "ロックは外れる。");
            Assert.AreEqual(QuizJudgement.None, machine.LastJudgement, "判定はやり直しになる。");
            Assert.AreEqual(string.Empty, machine.LastAnswerText);
            Assert.IsNull(machine.LastResolution);
            Assert.AreEqual(0, machine.GetScore(Client1), "既定では誤答で得点は動かない。");
            Assert.AreEqual(0, machine.LastScoreDelta);
            Assert.AreEqual(Client1, machine.LastScoredClientId);
        }

        [Test]
        public void Wrong_WithReopenEnabled_RejectsWrongAnswererAndAcceptsOthers()
        {
            var machine = WrongAnswer(out var wrongAt);
            var reopenAt = wrongAt + 0.1;
            machine.Tick(reopenAt, Rng());

            Assert.IsFalse(
                machine.AcceptBuzz(Client1, reopenAt + 0.1, reopenAt + 0.11, out var reason),
                "誤答した本人は再開放後に押せない。");
            Assert.AreEqual(BuzzReject.Penalized, reason);
            Assert.IsTrue(machine.IsPenalized(Client1));

            Assert.IsTrue(machine.AcceptBuzz(Client2, reopenAt + 0.2, reopenAt + 0.21, out var ok));
            Assert.AreEqual(BuzzReject.None, ok);
        }

        [Test]
        public void Wrong_ThenOtherPlayerAnswersCorrectly_ScoresOnlyTheWinner()
        {
            var machine = WrongAnswer(out var wrongAt);
            var reopenAt = wrongAt + 0.1;
            Assert.AreEqual(QuizEvent.BuzzReopened, machine.Tick(reopenAt, Rng()));

            Assert.IsTrue(machine.AcceptBuzz(Client2, reopenAt + 0.2, reopenAt + 0.21, out _));
            Assert.AreEqual(QuizEvent.BuzzResolved, machine.Tick(reopenAt + 0.21 + Window, Rng()));
            Assert.AreEqual(Client2, machine.LockedClientId);

            Assert.AreEqual(QuizEvent.AnswerOpened, machine.Tick(reopenAt + 0.5, Rng()));
            Assert.IsTrue(machine.SubmitAnswer(Client2, "東京", reopenAt + 0.6, out _));
            Assert.AreEqual(QuizEvent.Judged, machine.Tick(reopenAt + 0.7, Rng()));

            Assert.AreEqual(QuizPhase.Result, machine.Phase);
            Assert.AreEqual(QuizJudgement.Correct, machine.LastJudgement);
            Assert.AreEqual(ScoreRules.DefaultCorrectPoints, machine.GetScore(Client2));
            Assert.AreEqual(ScoreRules.DefaultCorrectPoints, machine.LastScoreDelta);
            Assert.AreEqual(0, machine.GetScore(Client1));
            CollectionAssert.AreEqual(new ulong[] { Client1, Client2 }, machine.Scores.ClientIds);
        }

        [Test]
        public void Wrong_AfterReopen_TimesOutAtOriginalDeadline()
        {
            var machine = WrongAnswer(out var wrongAt);
            machine.Tick(wrongAt + 0.1, Rng());
            Assert.AreEqual(QuizPhase.BuzzOpen, machine.Phase);

            // 残り時間は T0 基準のまま（再開放で伸びない）。
            Assert.AreEqual(QuizEvent.None, machine.Tick(T0 + BuzzLimit - 0.01, Rng()));
            Assert.AreEqual(QuizEvent.BuzzTimedOut, machine.Tick(T0 + BuzzLimit, Rng()));
            Assert.AreEqual(QuizPhase.Result, machine.Phase);
            Assert.AreEqual(QuizJudgement.TimedOut, machine.LastJudgement);
        }

        [Test]
        public void Wrong_WithoutRemainingBuzzTime_GoesToResult()
        {
            // 早押しの持ち時間を 1 秒にすると、回答が終わった時点で残り時間が無い。
            var limits = new QuizTimeLimits(buzzTimeLimitSec: 1.0, answerTimeLimitSec: 15.0, collectWindowSec: Window);
            var machine = LockAndOpenAnswer(out var answeringStart, rules: null, limits: limits);

            Assert.IsTrue(machine.SubmitAnswer(Client1, "おおさか", answeringStart + 0.2, out _));
            Assert.AreEqual(QuizEvent.Judged, machine.Tick(answeringStart + 0.3, Rng()));

            Assert.AreEqual(QuizPhase.Result, machine.Phase, "残り時間が無ければ再開放しない。");
            Assert.AreEqual(QuizJudgement.Wrong, machine.LastJudgement);
        }

        [Test]
        public void Wrong_WithReopenDisabled_GoesToResult()
        {
            var rules = QuizRules.Default.WithReopenAfterWrongAnswer(false);
            var machine = WrongAnswer(out var wrongAt, rules);

            Assert.AreEqual(QuizEvent.Judged, machine.Tick(wrongAt + 0.1, Rng()));
            Assert.AreEqual(QuizPhase.Result, machine.Phase);
            Assert.AreEqual(QuizJudgement.Wrong, machine.LastJudgement, "判定は Result で提示するので残る。");
        }

        [Test]
        public void AnswerTimeout_WithReopenEnabled_AlsoReopens()
        {
            // 回答の制限時間切れも誤答扱い（docs/network.md §6.6）なので、同じく再開放する。
            var limits = new QuizTimeLimits(buzzTimeLimitSec: 30.0, answerTimeLimitSec: 2.0, collectWindowSec: Window);
            var machine = LockAndOpenAnswer(out var answeringStart, rules: null, limits: limits);

            Assert.AreEqual(QuizEvent.AnswerTimedOut, machine.Tick(answeringStart + 2.0, Rng()));
            Assert.AreEqual(QuizEvent.BuzzReopened, machine.Tick(answeringStart + 2.1, Rng()));
            Assert.IsTrue(machine.IsPenalized(Client1));
        }

        // --- 次問休み（score.penaltyType = skipNext） ---

        [Test]
        public void SkipNext_IsRejectedOnNextQuestionAndClearedAfterwards()
        {
            var machine = WrongAnswer(out var wrongAt);
            machine.Tick(wrongAt + 0.1, Rng());
            Assert.AreEqual(QuizEvent.BuzzTimedOut, machine.Tick(T0 + BuzzLimit, Rng()));
            CollectionAssert.AreEqual(new ulong[] { Client1 }, machine.Penalties.Pending);

            // --- 次の問題: Client1 は休み ---
            var second = T0 + BuzzLimit + 2.0;
            Assert.IsTrue(machine.StartQuestion(1, new[] { "おおさか" }, second, out _));
            Assert.AreEqual(QuizEvent.BuzzOpened, machine.Tick(second, Rng()));
            Assert.IsTrue(machine.Penalties.IsSuspended(1, Client1));

            Assert.IsFalse(machine.AcceptBuzz(Client1, second + 0.3, second + 0.31, out var reason));
            Assert.AreEqual(BuzzReject.Penalized, reason);
            Assert.IsTrue(machine.AcceptBuzz(Client2, second + 0.4, second + 0.41, out _));

            Assert.AreEqual(QuizEvent.BuzzResolved, machine.Tick(second + 0.41 + Window, Rng()));
            Assert.AreEqual(QuizEvent.AnswerOpened, machine.Tick(second + 0.6, Rng()));
            Assert.IsTrue(machine.SubmitAnswer(Client2, "おおさか", second + 0.7, out _));
            Assert.AreEqual(QuizEvent.Judged, machine.Tick(second + 0.8, Rng()));

            // --- さらに次の問題: ペナルティは 1 問で解ける ---
            var third = second + 2.0;
            Assert.IsTrue(machine.StartQuestion(2, Answers, third, out _));
            Assert.AreEqual(QuizEvent.BuzzOpened, machine.Tick(third, Rng()));
            Assert.IsFalse(machine.IsPenalized(Client1));
            Assert.IsTrue(machine.AcceptBuzz(Client1, third + 0.3, third + 0.31, out var ok));
            Assert.AreEqual(BuzzReject.None, ok);
        }

        [Test]
        public void SkipNext_DoesNotChangeScore()
        {
            var machine = WrongAnswer(out var wrongAt);
            machine.Tick(wrongAt + 0.1, Rng());

            Assert.AreEqual(0, machine.GetScore(Client1));
            Assert.IsTrue(machine.Scores.Contains(Client1), "得点 0 でも得点表には載る。");
        }

        // --- 減点（score.penaltyType = minusPoints） ---

        [Test]
        public void MinusPoints_SubtractsAndDoesNotSuspendNextQuestion()
        {
            var rules = new QuizRules(
                ScoreRules.Default.WithPenalty(PenaltyKind.MinusPoints, ScoreRules.DefaultPenaltyPoints),
                reopenAfterWrongAnswer: false);
            var machine = WrongAnswer(out var wrongAt, rules);

            Assert.AreEqual(QuizEvent.Judged, machine.Tick(wrongAt + 0.1, Rng()));
            Assert.AreEqual(ScoreRules.DefaultPenaltyPoints, machine.GetScore(Client1));
            Assert.AreEqual(ScoreRules.DefaultPenaltyPoints, machine.LastScoreDelta);
            Assert.IsEmpty(machine.Penalties.Pending, "減点したので次問は休まない。");

            var second = wrongAt + 2.0;
            Assert.IsTrue(machine.StartQuestion(1, Answers, second, out _));
            Assert.AreEqual(QuizEvent.BuzzOpened, machine.Tick(second, Rng()));
            Assert.IsTrue(machine.AcceptBuzz(Client1, second + 0.3, second + 0.31, out _), "次問も押せる。");
            Assert.AreEqual(ScoreRules.DefaultPenaltyPoints, machine.GetScore(Client1), "得点は問題をまたいで保持する。");
        }

        [Test]
        public void MinusPoints_WrongThenReopenThenTimeout_DoesNotSubtractTwice()
        {
            // 再開放 → 誰も押さずタイムアウト、で同じお手つきの減点をもう一度配らないこと（PR #58 レビュー H1）。
            var rules = new QuizRules(
                ScoreRules.Default.WithPenalty(PenaltyKind.MinusPoints, ScoreRules.DefaultPenaltyPoints));
            var machine = WrongAnswer(out var wrongAt, rules);

            Assert.AreEqual(QuizEvent.BuzzReopened, machine.Tick(wrongAt + 0.1, Rng()));
            Assert.AreEqual(ScoreRules.DefaultPenaltyPoints, machine.LastScoreDelta);
            Assert.AreEqual(Client1, machine.LastScoredClientId);

            Assert.AreEqual(QuizEvent.BuzzTimedOut, machine.Tick(T0 + BuzzLimit, Rng()));

            Assert.AreEqual(QuizPhase.Result, machine.Phase);
            Assert.AreEqual(QuizJudgement.TimedOut, machine.LastJudgement);
            Assert.AreEqual(
                ScoreRules.DefaultPenaltyPoints,
                machine.GetScore(Client1),
                "減点は 1 回だけ（二重減点しない）。");
            Assert.AreEqual(0, machine.LastScoreDelta, "タイムアウトでは得点が動かないので増減は 0。");
            Assert.AreEqual(
                QuizStateMachine.NoClientId,
                machine.LastScoredClientId,
                "誤答時の得点通知が残っていると Result で同じ増減をもう一度配ってしまう。");
        }

        // --- 押せる参加者が居ないときの判定シーム（#200 で GameSession から接続。詳細は QuizStateMachineEligibilityTests） ---

        [Test]
        public void Wrong_WithNoEligibleBuzzers_GoesToResultInsteadOfReopening()
        {
            var machine = new QuizStateMachine(hasEligibleBuzzers: () => false);
            Assert.IsTrue(machine.StartQuestion(0, Answers, T0, out _));
            Assert.AreEqual(QuizEvent.BuzzOpened, machine.Tick(T0, Rng()));
            Assert.IsTrue(machine.AcceptBuzz(Client1, T0 + 0.4, T0 + 0.42, out _));
            Assert.AreEqual(QuizEvent.BuzzResolved, machine.Tick(T0 + 0.42 + Window, Rng()));
            Assert.AreEqual(QuizEvent.AnswerOpened, machine.Tick(T0 + 0.7, Rng()));
            Assert.IsTrue(machine.SubmitAnswer(Client1, "おおさか", T0 + 0.9, out _));

            Assert.AreEqual(QuizEvent.Judged, machine.Tick(T0 + 1.0, Rng()));
            Assert.AreEqual(QuizPhase.Result, machine.Phase, "押せる参加者が居なければ再開放しない。");
        }

        // --- ペナルティなし（score.penaltyType = none） ---

        [Test]
        public void NoPenalty_DoesNotSuspendOrSubtract()
        {
            var rules = new QuizRules(
                ScoreRules.Default.WithPenalty(PenaltyKind.None, ScoreRules.DefaultPenaltyPoints),
                reopenAfterWrongAnswer: false);
            var machine = WrongAnswer(out var wrongAt, rules);

            Assert.AreEqual(QuizEvent.Judged, machine.Tick(wrongAt + 0.1, Rng()));
            Assert.AreEqual(0, machine.GetScore(Client1), "減点しない。");
            Assert.AreEqual(0, machine.LastScoreDelta);
            Assert.IsEmpty(machine.Penalties.Pending, "次問休みにもしない。");

            var second = wrongAt + 2.0;
            Assert.IsTrue(machine.StartQuestion(1, Answers, second, out _));
            Assert.AreEqual(QuizEvent.BuzzOpened, machine.Tick(second, Rng()));
            Assert.IsTrue(machine.AcceptBuzz(Client1, second + 0.3, second + 0.31, out _), "次問も押せる。");
        }

        // --- タイムアウト（誰も押さなかった） ---

        [Test]
        public void BuzzTimeout_LeavesScoreBoardUntouched()
        {
            var machine = OpenBuzz();

            Assert.AreEqual(QuizEvent.BuzzTimedOut, machine.Tick(T0 + BuzzLimit, Rng()));

            Assert.AreEqual(0, machine.Scores.Count, "誰も答えていないので得点表は空のまま。");
            Assert.AreEqual(QuizStateMachine.NoClientId, machine.LastScoredClientId);
            Assert.AreEqual(0, machine.LastScoreDelta);
        }

        // --- 回答の再入力（answer.singleAttemptOnly） ---

        [Test]
        public void SingleAttemptOnly_RejectsSecondSubmission()
        {
            var machine = LockAndOpenAnswer(out var answeringStart);

            Assert.IsTrue(machine.SubmitAnswer(Client1, "おおさか", answeringStart + 0.2, out _));
            Assert.AreEqual(1, machine.AnswerAttemptCount);

            Assert.IsFalse(
                machine.SubmitAnswer(Client1, "とうきょう", answeringStart + 0.3, out var reason),
                "1 回のみの設定では送信し直せない。");
            Assert.AreEqual(AnswerReject.NotAnswering, reason, "既に判定へ進んでいる。");
        }

        /// <summary>
        /// <c>answer.singleAttemptOnly = false</c>（制限時間内の再送信）の挙動は #18 では実装せず、
        /// 常に 1 回のみとして扱う（統括判断。#26 で本人向け通知 RPC と合わせて実装する）。
        /// </summary>
        [Test]
        public void SingleAttemptOnlyFalse_IsNotImplementedAndStillJudgesImmediately()
        {
            var rules = QuizRules.Default.WithSingleAttemptOnly(false).WithReopenAfterWrongAnswer(false);
            var machine = LockAndOpenAnswer(out var answeringStart, rules);

            Assert.IsTrue(machine.SubmitAnswer(Client1, "おおさか", answeringStart + 0.2, out _));
            Assert.AreEqual(QuizPhase.Judging, machine.Phase, "設定に関わらず最初の送信で判定が確定する。");
            Assert.AreEqual(QuizJudgement.Wrong, machine.LastJudgement);

            Assert.IsFalse(machine.SubmitAnswer(Client1, "とうきょう", answeringStart + 0.3, out var reason));
            Assert.AreEqual(AnswerReject.NotAnswering, reason);
        }

        // --- 席が消えたときの掃除（#84。切断しただけでは捨てない） ---

        [Test]
        public void ForgetClient_ClearsPenalties()
        {
            var machine = WrongAnswer(out var wrongAt);
            machine.Tick(wrongAt + 0.1, Rng());
            Assert.IsTrue(machine.IsPenalized(Client1));

            machine.ForgetClient(Client1);

            Assert.IsFalse(machine.IsPenalized(Client1));
            Assert.IsEmpty(machine.Penalties.Pending);
        }

        // --- 規則の受け渡し ---

        [Test]
        public void Rules_DefaultsToRoomSettingsDefaults()
        {
            var machine = new QuizStateMachine();

            Assert.AreEqual(ScoreRules.DefaultCorrectPoints, machine.Rules.Score.CorrectPoints);
            Assert.IsTrue(machine.Rules.ReopenAfterWrongAnswer);
            Assert.IsTrue(machine.Rules.SingleAttemptOnly);
            Assert.AreSame(machine.Rules.Score, machine.Scores.Rules, "得点表は同じ規則を使う。");
        }
    }
}
