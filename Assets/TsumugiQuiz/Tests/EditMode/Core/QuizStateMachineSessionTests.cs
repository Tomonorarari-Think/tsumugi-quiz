using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 複数問の進行（<see cref="QuizStateMachine.StartSession"/> /
    /// <see cref="QuizStateMachine.AdvanceToNextQuestion"/>、#19）のテスト。
    /// 仕様は docs/network.md §6.6（<c>Result --&gt; Idle</c> / <c>Result --&gt; [*]</c>）。
    /// </summary>
    public class QuizStateMachineSessionTests : QuizStateMachineTestBase
    {
        private const double SessionStart = 900.0;

        /// <summary>2 問目以降の正解候補（1 問目の <see cref="QuizStateMachineTestBase.Answers"/> と区別する）。</summary>
        private static readonly string[] SecondAnswers = { "きょうと", "京都" };

        [Test]
        public void StartSession_SetsTotalQuestionsAndReturnsToLobby()
        {
            var machine = new QuizStateMachine();

            Assert.IsTrue(machine.StartSession(3, SessionStart, out var reason));
            Assert.AreEqual(QuizReject.None, reason);
            Assert.AreEqual(3, machine.TotalQuestions);
            Assert.AreEqual(QuizPhase.Lobby, machine.Phase);
            Assert.AreEqual(-1, machine.QuestionIndex);
            Assert.IsTrue(machine.HasNextQuestion, "未出題の時点では「次の問題」は 0 問目を指す。");
            Assert.AreEqual(0, machine.NextQuestionIndex);
            Assert.IsNull(machine.FinalScores, "終了前に最終得点は確定しない。");
        }

        [Test]
        public void StartSession_ZeroQuestions_IsRejected()
        {
            var machine = new QuizStateMachine();

            Assert.IsFalse(machine.StartSession(0, SessionStart, out var reason));
            Assert.AreEqual(QuizReject.InvalidTotalQuestions, reason);
            Assert.AreEqual(QuizStateMachine.NoSession, machine.TotalQuestions);
        }

        [Test]
        public void StartSession_DuringQuestion_IsRejected()
        {
            var machine = StartedSession(2);
            Assert.IsTrue(machine.StartQuestion(0, Answers, T0, out _));

            Assert.IsFalse(machine.StartSession(5, T0 + 1.0, out var reason), "進行中に出題列を差し替えない。");
            Assert.AreEqual(QuizReject.InvalidPhase, reason);
            Assert.AreEqual(2, machine.TotalQuestions);
        }

        [Test]
        public void StartSession_NonFiniteTime_IsRejected()
        {
            var machine = new QuizStateMachine();

            Assert.IsFalse(machine.StartSession(2, double.NaN, out var reason));
            Assert.AreEqual(QuizReject.NonFiniteTime, reason);
        }

        [Test]
        public void StartQuestion_BeyondTotalQuestions_IsRejected()
        {
            var machine = StartedSession(2);

            Assert.IsFalse(machine.StartQuestion(2, Answers, T0, out var reason), "出題列の外は出題できない。");
            Assert.AreEqual(QuizReject.InvalidQuestionIndex, reason);
            Assert.AreEqual(QuizPhase.Lobby, machine.Phase);
        }

        [Test]
        public void AdvanceToNextQuestion_FromResult_StartsNextQuestion()
        {
            var machine = StartedSession(3);
            var now = AnswerCorrectly(machine, 0, Answers);
            Assert.AreEqual(QuizPhase.Result, machine.Phase);
            Assert.IsTrue(machine.HasNextQuestion);
            Assert.AreEqual(1, machine.NextQuestionIndex);

            var advance = machine.AdvanceToNextQuestion(SecondAnswers, now + 1.0, out var reason);

            Assert.AreEqual(QuizAdvance.NextQuestionStarted, advance);
            Assert.AreEqual(QuizReject.None, reason);
            Assert.AreEqual(QuizPhase.Reading, machine.Phase, "次の問題は Reading から始まる。");
            Assert.AreEqual(1, machine.QuestionIndex);
            Assert.AreEqual("きょうと", machine.CorrectAnswer, "正解候補が次の問題のものに差し替わるはず。");
            Assert.AreEqual(QuizJudgement.None, machine.LastJudgement, "前問の判定は持ち越さない。");
            Assert.AreEqual(QuizStateMachine.NoClientId, machine.LockedClientId);
        }

        [Test]
        public void AdvanceToNextQuestion_AfterLastQuestion_FinishesSession()
        {
            var machine = StartedSession(1);
            var now = AnswerCorrectly(machine, 0, Answers);

            Assert.IsFalse(machine.HasNextQuestion);
            Assert.AreEqual(-1, machine.NextQuestionIndex);

            var advance = machine.AdvanceToNextQuestion(null, now + 1.0, out var reason);

            Assert.AreEqual(QuizAdvance.SessionFinished, advance);
            Assert.AreEqual(QuizReject.None, reason);
            Assert.AreEqual(QuizPhase.Finished, machine.Phase);
            Assert.IsNotNull(machine.FinalScores, "全問終了で最終得点が確定するはず。");
            Assert.AreEqual(
                ScoreRules.DefaultCorrectPoints,
                machine.FinalScores.GetScore(Client1),
                "最終得点は終了時点の得点表と一致するはず。");
        }

        [Test]
        public void StartQuestion_FromResult_CanSkipUnplayableQuestion()
        {
            // 配信できない問題を呼び出し側（GameSession）が読み飛ばせるよう、
            // 状態機械は Result から「次の次」以降の index も受け付ける。
            var machine = StartedSession(3);
            var now = AnswerCorrectly(machine, 0, Answers);

            Assert.IsTrue(machine.StartQuestion(2, SecondAnswers, now + 1.0, out var reason));
            Assert.AreEqual(QuizReject.None, reason);
            Assert.AreEqual(2, machine.QuestionIndex);
            Assert.AreEqual(QuizPhase.Reading, machine.Phase);
            Assert.IsFalse(machine.HasNextQuestion, "最後まで飛ばしたので次の問題は無い。");
        }

        [Test]
        public void AdvanceToNextQuestion_OutsideResult_IsRejected()
        {
            var machine = StartedSession(2);
            Assert.IsTrue(machine.StartQuestion(0, Answers, T0, out _));

            var advance = machine.AdvanceToNextQuestion(SecondAnswers, T0 + 1.0, out var reason);

            Assert.AreEqual(QuizAdvance.Rejected, advance);
            Assert.AreEqual(QuizReject.InvalidPhase, reason);
            Assert.AreEqual(QuizPhase.Reading, machine.Phase);
        }

        [Test]
        public void AdvanceToNextQuestion_WithoutAnswers_IsRejectedAndStaysInResult()
        {
            var machine = StartedSession(2);
            var now = AnswerCorrectly(machine, 0, Answers);

            var advance = machine.AdvanceToNextQuestion(new string[0], now + 1.0, out var reason);

            Assert.AreEqual(QuizAdvance.Rejected, advance);
            Assert.AreEqual(QuizReject.NoAnswers, reason);
            Assert.AreEqual(QuizPhase.Result, machine.Phase, "失敗しても Result に留まる（次問の出題は始まらない）。");
            Assert.AreEqual(0, machine.QuestionIndex);
        }

        [Test]
        public void MultiQuestionSession_AccumulatesScoresUntilFinished()
        {
            var machine = StartedSession(3);

            var now = AnswerCorrectly(machine, 0, Answers);
            Assert.AreEqual(ScoreRules.DefaultCorrectPoints, machine.GetScore(Client1));

            Assert.AreEqual(
                QuizAdvance.NextQuestionStarted,
                machine.AdvanceToNextQuestion(SecondAnswers, now + 1.0, out _));
            now = AnswerCorrectly(machine, 1, SecondAnswers, now + 1.0);
            Assert.AreEqual(ScoreRules.DefaultCorrectPoints * 2, machine.GetScore(Client1), "得点は問題をまたいで累積する。");

            Assert.AreEqual(
                QuizAdvance.NextQuestionStarted,
                machine.AdvanceToNextQuestion(Answers, now + 1.0, out _));
            now = AnswerCorrectly(machine, 2, Answers, now + 1.0);

            Assert.AreEqual(
                QuizAdvance.SessionFinished,
                machine.AdvanceToNextQuestion(null, now + 1.0, out _),
                "3 問目のあとは終了するはず。");
            Assert.AreEqual(QuizPhase.Finished, machine.Phase);
            Assert.AreEqual(ScoreRules.DefaultCorrectPoints * 3, machine.FinalScores.GetScore(Client1));
        }

        [Test]
        public void SkipNextPenalty_CarriesOverToNextQuestion()
        {
            // 再開放されると同じ問題の中で進んでしまうので、お手つき → 即 Result になる設定で確かめる。
            var rules = QuizRules.Default.WithReopenAfterWrongAnswer(false);
            var machine = StartedSession(2, rules: rules);

            var now = AnswerWrongly(machine, 0);
            Assert.AreEqual(QuizPhase.Result, machine.Phase);
            Assert.IsTrue(machine.Penalties.IsPending(Client1), "お手つきは「次問休み」として積まれる。");

            Assert.AreEqual(
                QuizAdvance.NextQuestionStarted,
                machine.AdvanceToNextQuestion(SecondAnswers, now + 1.0, out _));

            Assert.IsTrue(machine.Penalties.IsSuspended(1, Client1), "次の問題で休みが確定するはず。");
            Assert.IsTrue(machine.IsPenalized(Client1));
            Assert.IsFalse(machine.Penalties.IsPending(Client1), "確定したので予定からは消える。");

            // 休み中のクライアントは押下を棄却され、押していない他のクライアントは受理される。
            var buzzOpen = now + 1.0;
            Assert.AreEqual(QuizEvent.BuzzOpened, machine.Tick(buzzOpen, Rng()));
            Assert.IsFalse(machine.AcceptBuzz(Client1, buzzOpen + 0.1, buzzOpen + 0.12, out var reason));
            Assert.AreEqual(BuzzReject.Penalized, reason);
            Assert.IsTrue(machine.AcceptBuzz(Client2, buzzOpen + 0.2, buzzOpen + 0.22, out _));
        }

        [Test]
        public void WrongAnswerers_AreResetForNextQuestion()
        {
            var rules = QuizRules.Default.WithReopenAfterWrongAnswer(false);
            var machine = StartedSession(2, rules: rules, scoreRules: NoPenaltyScoreRules());

            var now = AnswerWrongly(machine, 0);
            CollectionAssert.Contains(machine.WrongAnswerers, Client1);

            Assert.AreEqual(
                QuizAdvance.NextQuestionStarted,
                machine.AdvanceToNextQuestion(SecondAnswers, now + 1.0, out _));

            CollectionAssert.IsEmpty(machine.WrongAnswerers, "誤答者は問題ごとにリセットされる。");
            Assert.IsFalse(machine.IsPenalized(Client1), "ペナルティなし設定なら次問は押せる。");
        }

        [Test]
        public void EachQuestion_AppliesBuzzTimeLimitFromItsOwnT0()
        {
            var limits = new QuizTimeLimits(
                buzzTimeLimitSec: 4.0, answerTimeLimitSec: 3.0, collectWindowSec: Window);
            var machine = StartedSession(2, limits: limits);

            // 1 問目: 誰も押さずにタイムアウト（T0 + 4 秒）。
            Assert.IsTrue(machine.StartQuestion(0, Answers, T0, out _));
            Assert.AreEqual(QuizEvent.BuzzOpened, machine.Tick(T0, Rng()));
            Assert.AreEqual(QuizEvent.None, machine.Tick(T0 + 3.9, Rng()), "制限時間内はタイムアウトしない。");
            Assert.AreEqual(QuizEvent.BuzzTimedOut, machine.Tick(T0 + 4.0, Rng()));
            Assert.AreEqual(QuizJudgement.TimedOut, machine.LastJudgement);

            // 2 問目: 制限時間は 2 問目の T0 から測り直す。
            var secondStart = T0 + 10.0;
            Assert.AreEqual(
                QuizAdvance.NextQuestionStarted,
                machine.AdvanceToNextQuestion(SecondAnswers, secondStart, out _));
            Assert.AreEqual(QuizEvent.BuzzOpened, machine.Tick(secondStart, Rng()));
            Assert.AreEqual(secondStart, machine.BuzzOpenServerTime, 1e-9, "T0 は 2 問目の読み上げ完了時刻になる。");
            Assert.AreEqual(QuizEvent.None, machine.Tick(secondStart + 3.9, Rng()));
            Assert.AreEqual(QuizEvent.BuzzTimedOut, machine.Tick(secondStart + 4.0, Rng()));
        }

        [Test]
        public void EachQuestion_AppliesAnswerTimeLimitFromItsOwnStart()
        {
            var limits = new QuizTimeLimits(
                buzzTimeLimitSec: 30.0, answerTimeLimitSec: 3.0, collectWindowSec: Window);
            var rules = QuizRules.Default.WithReopenAfterWrongAnswer(false);
            var machine = StartedSession(2, limits: limits, rules: rules);

            var now = AnswerCorrectly(machine, 0, Answers);
            Assert.AreEqual(
                QuizAdvance.NextQuestionStarted,
                machine.AdvanceToNextQuestion(SecondAnswers, now + 1.0, out _));

            var secondStart = now + 1.0;
            Assert.AreEqual(QuizEvent.BuzzOpened, machine.Tick(secondStart, Rng()));
            Assert.IsTrue(machine.AcceptBuzz(Client2, secondStart + 0.2, secondStart + 0.22, out _));
            Assert.AreEqual(QuizEvent.BuzzResolved, machine.Tick(secondStart + 0.22 + Window, Rng()));

            var answeringStart = secondStart + 0.5;
            Assert.AreEqual(QuizEvent.AnswerOpened, machine.Tick(answeringStart, Rng()));
            Assert.AreEqual(QuizEvent.None, machine.Tick(answeringStart + 2.9, Rng()), "回答の制限時間内。");
            Assert.AreEqual(QuizEvent.AnswerTimedOut, machine.Tick(answeringStart + 3.0, Rng()));
            Assert.AreEqual(QuizJudgement.Wrong, machine.LastJudgement, "回答の時間切れは誤答扱い。");
        }

        [Test]
        public void StartSession_AfterFinished_ResetsScoresAndPenalties()
        {
            var machine = StartedSession(1);
            var now = AnswerCorrectly(machine, 0, Answers);
            Assert.AreEqual(QuizAdvance.SessionFinished, machine.AdvanceToNextQuestion(null, now + 1.0, out _));
            Assert.AreEqual(ScoreRules.DefaultCorrectPoints, machine.GetScore(Client1));

            Assert.IsTrue(machine.StartSession(2, now + 2.0, out _), "終了後は新しいセッションを始められる。");

            Assert.AreEqual(QuizPhase.Lobby, machine.Phase);
            Assert.AreEqual(2, machine.TotalQuestions);
            Assert.AreEqual(0, machine.GetScore(Client1), "前のセッションの得点は持ち越さない。");
            Assert.AreEqual(0, machine.Scores.Count);
            Assert.IsNull(machine.FinalScores);
            Assert.AreEqual(-1, machine.QuestionIndex);
        }

        [Test]
        public void SingleQuestionMode_HasNoNextQuestion()
        {
            // StartSession を呼ばない従来の使い方（#12 / #18）では自動進行の判断材料を持たない。
            var machine = new QuizStateMachine();
            Assert.IsTrue(machine.StartQuestion(0, Answers, T0, out _));

            Assert.AreEqual(QuizStateMachine.NoSession, machine.TotalQuestions);
            Assert.IsFalse(machine.HasNextQuestion);
        }

        /// <summary>セッションを開始した（Lobby の）マシンを返す。</summary>
        private static QuizStateMachine StartedSession(
            int totalQuestions,
            QuizTimeLimits limits = null,
            QuizRules rules = null,
            ScoreRules scoreRules = null)
        {
            var effectiveRules = rules ?? QuizRules.Default;
            if (scoreRules != null)
            {
                effectiveRules = effectiveRules.WithScore(scoreRules);
            }

            var machine = new QuizStateMachine(limits, effectiveRules);
            Assert.IsTrue(machine.StartSession(totalQuestions, SessionStart, out _));
            return machine;
        }

        /// <summary>お手つきペナルティも減点もしない得点規則。</summary>
        private static ScoreRules NoPenaltyScoreRules() =>
            new ScoreRules(
                ScoreRules.DefaultCorrectPoints,
                ScoreRules.DefaultWrongPoints,
                PenaltyKind.None,
                ScoreRules.DefaultPenaltyPoints);

        /// <summary>
        /// 指定した問題を <see cref="QuizStateMachineTestBase.Client1"/> の正解で Result まで進める。
        /// </summary>
        /// <returns>Result に入ったサーバー時刻。</returns>
        private static double AnswerCorrectly(
            QuizStateMachine machine, int questionIndex, string[] answers, double? startAt = null)
        {
            var start = startAt ?? T0;
            if (machine.Phase == QuizPhase.Lobby)
            {
                Assert.IsTrue(machine.StartQuestion(questionIndex, answers, start, out _));
            }

            Assert.AreEqual(QuizPhase.Reading, machine.Phase);
            Assert.AreEqual(QuizEvent.BuzzOpened, machine.Tick(start, Rng()));
            Assert.IsTrue(machine.AcceptBuzz(Client1, start + 0.4, start + 0.42, out _));
            Assert.AreEqual(QuizEvent.BuzzResolved, machine.Tick(start + 0.42 + Window, Rng()));
            Assert.AreEqual(QuizEvent.AnswerOpened, machine.Tick(start + 0.7, Rng()));
            Assert.IsTrue(machine.SubmitAnswer(Client1, answers[0], start + 0.8, out _));

            var resultAt = start + 0.9;
            Assert.AreEqual(QuizEvent.Judged, machine.Tick(resultAt, Rng()));
            Assert.AreEqual(QuizJudgement.Correct, machine.LastJudgement);
            return resultAt;
        }

        /// <summary>
        /// 指定した問題を <see cref="QuizStateMachineTestBase.Client1"/> の誤答で Result まで進める
        /// （再開放しない設定でのみ使う）。
        /// </summary>
        /// <returns>Result に入ったサーバー時刻。</returns>
        private static double AnswerWrongly(QuizStateMachine machine, int questionIndex, double? startAt = null)
        {
            var start = startAt ?? T0;
            Assert.IsTrue(machine.StartQuestion(questionIndex, Answers, start, out _));
            Assert.AreEqual(QuizEvent.BuzzOpened, machine.Tick(start, Rng()));
            Assert.IsTrue(machine.AcceptBuzz(Client1, start + 0.4, start + 0.42, out _));
            Assert.AreEqual(QuizEvent.BuzzResolved, machine.Tick(start + 0.42 + Window, Rng()));
            Assert.AreEqual(QuizEvent.AnswerOpened, machine.Tick(start + 0.7, Rng()));
            Assert.IsTrue(machine.SubmitAnswer(Client1, "おおさか", start + 0.8, out _));

            var resultAt = start + 0.9;
            Assert.AreEqual(QuizEvent.Judged, machine.Tick(resultAt, Rng()));
            Assert.AreEqual(QuizJudgement.Wrong, machine.LastJudgement);
            return resultAt;
        }
    }
}
