using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine"/> の選択式（<c>choice</c>）進行のテスト（issue #17、
    /// docs/question-data.md §6、docs/room-settings.md <c>answer.choiceTimeLimitSec</c>
    /// 「早押しなしで全員が回答する形式」）。
    /// </summary>
    public class QuizStateMachineChoiceTests : QuizStateMachineTestBase
    {
        private const int CorrectIndex = 2;
        private const int ChoiceCount = 4;

        /// <summary>選択式の問題を出題し、<see cref="QuizPhase.ChoiceAnswering"/> まで進めたマシンを返す。</summary>
        private static QuizStateMachine OpenChoiceAnswering(QuizTimeLimits limits = null)
        {
            var machine = new QuizStateMachine(limits);
            Assert.IsTrue(machine.StartQuestion(0, CorrectIndex, ChoiceCount, T0, out _));
            Assert.AreEqual(QuizEvent.ChoiceAnsweringOpened, machine.Tick(T0, Rng()));
            Assert.AreEqual(QuizPhase.ChoiceAnswering, machine.Phase);
            return machine;
        }

        // --- 出題 ---

        [Test]
        public void StartQuestion_Choice_SetsChoiceMetadata()
        {
            var machine = new QuizStateMachine();

            Assert.IsTrue(machine.StartQuestion(0, CorrectIndex, ChoiceCount, T0, out var reason));
            Assert.AreEqual(QuizReject.None, reason);
            Assert.IsTrue(machine.IsChoiceQuestion);
            Assert.AreEqual(CorrectIndex, machine.CorrectChoiceIndex);
            Assert.AreEqual(ChoiceCount, machine.ChoiceCount);
        }

        [Test]
        public void StartQuestion_Choice_CorrectIndexOutOfRange_IsRejected()
        {
            var machine = new QuizStateMachine();

            Assert.IsFalse(machine.StartQuestion(0, ChoiceCount, ChoiceCount, T0, out var reason));
            Assert.AreEqual(QuizReject.InvalidChoice, reason);
            Assert.AreEqual(QuizPhase.Lobby, machine.Phase);
        }

        [Test]
        public void StartQuestion_Choice_TooFewChoices_IsRejected()
        {
            var machine = new QuizStateMachine();

            Assert.IsFalse(machine.StartQuestion(0, 0, 1, T0, out var reason));
            Assert.AreEqual(QuizReject.InvalidChoice, reason);
        }

        [Test]
        public void StartQuestion_Choice_TooManyChoices_IsRejected()
        {
            var machine = new QuizStateMachine();

            Assert.IsFalse(machine.StartQuestion(0, 0, QuizStateMachine.MaxChoiceCount + 1, T0, out var reason));
            Assert.AreEqual(QuizReject.InvalidChoice, reason);
        }

        [Test]
        public void Tick_Reading_ChoiceQuestion_SkipsBuzzOpenAndEntersChoiceAnswering()
        {
            var machine = new QuizStateMachine();
            Assert.IsTrue(machine.StartQuestion(0, CorrectIndex, ChoiceCount, T0, out _));

            var evt = machine.Tick(T0, Rng());

            Assert.AreEqual(QuizEvent.ChoiceAnsweringOpened, evt);
            Assert.AreEqual(QuizPhase.ChoiceAnswering, machine.Phase);
        }

        // --- 選択の送信 ---

        [Test]
        public void SubmitChoice_ValidIndex_IsAcceptedAndDoesNotTransitionYet()
        {
            var machine = OpenChoiceAnswering();

            Assert.IsTrue(machine.SubmitChoice(Client1, CorrectIndex, T0 + 1.0, out var reason));
            Assert.AreEqual(AnswerReject.None, reason);
            Assert.AreEqual(QuizPhase.ChoiceAnswering, machine.Phase, "判定は時間切れまで行わない。");
        }

        [Test]
        public void SubmitChoice_OutOfRange_IsRejected()
        {
            var machine = OpenChoiceAnswering();

            Assert.IsFalse(machine.SubmitChoice(Client1, ChoiceCount, T0 + 1.0, out var reason));
            Assert.AreEqual(AnswerReject.InvalidChoiceIndex, reason);

            Assert.IsFalse(machine.SubmitChoice(Client1, -1, T0 + 1.0, out reason));
            Assert.AreEqual(AnswerReject.InvalidChoiceIndex, reason);
        }

        [Test]
        public void SubmitChoice_NotInChoiceAnsweringPhase_IsRejected()
        {
            var machine = OpenBuzz();

            Assert.IsFalse(machine.SubmitChoice(Client1, 0, T0 + 1.0, out var reason));
            Assert.AreEqual(AnswerReject.NotAnswering, reason);
        }

        [Test]
        public void SubmitChoice_SecondAttemptBySameClient_IsRejected()
        {
            var machine = OpenChoiceAnswering();

            Assert.IsTrue(machine.SubmitChoice(Client1, 0, T0 + 1.0, out _));
            Assert.IsFalse(machine.SubmitChoice(Client1, CorrectIndex, T0 + 1.1, out var reason));
            Assert.AreEqual(AnswerReject.AlreadyAttempted, reason);
        }

        [Test]
        public void SubmitChoice_DifferentClients_AreBothAccepted()
        {
            var machine = OpenChoiceAnswering();

            Assert.IsTrue(machine.SubmitChoice(Client1, 0, T0 + 1.0, out _));
            Assert.IsTrue(machine.SubmitChoice(Client2, CorrectIndex, T0 + 1.1, out _));
        }

        [Test]
        public void SubmitChoice_AfterDeadline_IsRejectedEvenBeforeTickProcessesTimeout()
        {
            // レビュー M1: サーバー tick が TickChoiceAnswering を処理する前に締切後の送信が届いても、
            // Phase がまだ ChoiceAnswering のままなので、締切そのものをサーバー時刻で判定して弾く必要がある。
            var machine = OpenChoiceAnswering();
            var deadline = T0 + QuizTimeLimits.Default.ChoiceTimeLimitSec;

            Assert.IsFalse(machine.SubmitChoice(Client1, CorrectIndex, deadline, out var reason));
            Assert.AreEqual(AnswerReject.Expired, reason, "NotAnswering ではなく締切超過専用の理由になるはず（レビュー R-2）。");
            Assert.AreEqual(QuizPhase.ChoiceAnswering, machine.Phase, "Tick を挟むまでフェーズ自体は変わらない。");
        }

        [Test]
        public void SubmitChoice_JustBeforeDeadline_IsAccepted()
        {
            var machine = OpenChoiceAnswering();
            var justBefore = T0 + QuizTimeLimits.Default.ChoiceTimeLimitSec - 0.01;

            Assert.IsTrue(machine.SubmitChoice(Client1, CorrectIndex, justBefore, out var reason));
            Assert.AreEqual(AnswerReject.None, reason);
        }

        [Test]
        public void SubmitChoice_ClientPenalizedFromPreviousWrongChoice_IsRejectedNextQuestion()
        {
            // レビュー M3（統括判断 b）: 選択式でも score.penaltyType = "skipNext" を適用する。
            var machine = new QuizStateMachine();
            Assert.IsTrue(machine.StartSession(2, T0, out _));
            Assert.IsTrue(machine.StartQuestion(0, CorrectIndex, ChoiceCount, T0, out _));
            Assert.AreEqual(QuizEvent.ChoiceAnsweringOpened, machine.Tick(T0, Rng()));

            Assert.IsTrue(machine.SubmitChoice(Client1, 0, T0 + 1.0, out _)); // 不正解（CorrectIndex は 2）

            var deadline = T0 + QuizTimeLimits.Default.ChoiceTimeLimitSec;
            Assert.AreEqual(QuizEvent.ChoiceTimedOut, machine.Tick(deadline, Rng()));
            Assert.AreEqual(QuizEvent.ChoiceJudged, machine.Tick(deadline, Rng()));
            Assert.AreEqual(QuizPhase.Result, machine.Phase);

            // 次の問題（選択式）を開始すると、誤答した Client1 は「次問休み」の対象になる。
            Assert.IsTrue(machine.StartQuestion(1, CorrectIndex, ChoiceCount, deadline, out _));
            Assert.AreEqual(QuizEvent.ChoiceAnsweringOpened, machine.Tick(deadline, Rng()));

            Assert.IsFalse(machine.SubmitChoice(Client1, CorrectIndex, deadline + 0.1, out var reason));
            Assert.AreEqual(AnswerReject.Penalized, reason);

            // 誤答していない Client2 は通常どおり選択できる。
            Assert.IsTrue(machine.SubmitChoice(Client2, CorrectIndex, deadline + 0.1, out _));
        }

        // --- 一斉判定（時間切れ） ---

        [Test]
        public void TimeUp_MultipleClients_JudgesAllAndAppliesScore()
        {
            var machine = OpenChoiceAnswering();
            Assert.IsTrue(machine.SubmitChoice(Client1, CorrectIndex, T0 + 1.0, out _)); // 正解
            Assert.IsTrue(machine.SubmitChoice(Client2, 0, T0 + 1.1, out _)); // 不正解

            var deadline = T0 + QuizTimeLimits.Default.ChoiceTimeLimitSec;
            Assert.AreEqual(QuizEvent.ChoiceTimedOut, machine.Tick(deadline, Rng()));
            Assert.AreEqual(QuizPhase.Judging, machine.Phase);

            Assert.AreEqual(QuizEvent.ChoiceJudged, machine.Tick(deadline, Rng()));
            Assert.AreEqual(QuizPhase.Result, machine.Phase);

            Assert.AreEqual(2, machine.LastChoiceResults.Count);

            var client1Result = Find(machine, Client1);
            Assert.AreEqual(QuizJudgement.Correct, client1Result.Judgement);
            Assert.AreEqual(CorrectIndex, client1Result.ChoiceIndex);
            Assert.AreEqual(ScoreRules.DefaultCorrectPoints, client1Result.ScoreDelta);
            Assert.AreEqual(ScoreRules.DefaultCorrectPoints, machine.GetScore(Client1));

            var client2Result = Find(machine, Client2);
            Assert.AreEqual(QuizJudgement.Wrong, client2Result.Judgement);
            Assert.AreEqual(0, client2Result.ChoiceIndex);
            Assert.AreEqual(ScoreRules.DefaultWrongPoints, client2Result.ScoreDelta);
        }

        [Test]
        public void TimeUp_NoOneAnswered_StillProgressesToResultWithEmptyResults()
        {
            var machine = OpenChoiceAnswering();

            var deadline = T0 + QuizTimeLimits.Default.ChoiceTimeLimitSec;
            Assert.AreEqual(QuizEvent.ChoiceTimedOut, machine.Tick(deadline, Rng()));
            Assert.AreEqual(QuizEvent.ChoiceJudged, machine.Tick(deadline, Rng()));

            Assert.AreEqual(QuizPhase.Result, machine.Phase);
            Assert.AreEqual(0, machine.LastChoiceResults.Count);
        }

        [Test]
        public void TimeUp_BeforeDeadline_DoesNotTransition()
        {
            var machine = OpenChoiceAnswering();

            var evt = machine.Tick(T0 + QuizTimeLimits.Default.ChoiceTimeLimitSec - 0.5, Rng());

            Assert.AreEqual(QuizEvent.None, evt);
            Assert.AreEqual(QuizPhase.ChoiceAnswering, machine.Phase);
        }

        private static ChoiceAnswerResult Find(QuizStateMachine machine, ulong clientId)
        {
            foreach (var result in machine.LastChoiceResults)
            {
                if (result.ClientId == clientId)
                {
                    return result;
                }
            }

            Assert.Fail($"クライアント {clientId} の結果が見つかりません。");
            return default;
        }
    }
}
