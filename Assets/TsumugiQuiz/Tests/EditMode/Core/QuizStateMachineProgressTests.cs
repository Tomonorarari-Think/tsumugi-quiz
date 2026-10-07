using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Participants;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine.BuildProgress"/>（参加者パネルへ配る現在の問題の進行状態、#194）のテスト。
    /// </summary>
    public class QuizStateMachineProgressTests : QuizStateMachineTestBase
    {
        private const ulong Reconnected = 41;

        private static ParticipantProgress Get(QuestionProgress progress, ulong clientId)
        {
            progress.TryGet(clientId, out var entry);
            return entry;
        }

        /// <summary>Client1 と Client2 が集計窓の中で押し、Client1 が勝った状態（Locked）。</summary>
        private static QuizStateMachine TwoBuzzersResolved()
        {
            var machine = OpenBuzz();
            Assert.IsTrue(machine.AcceptBuzz(Client1, T0 + 0.40, T0 + 0.42, out _));
            Assert.IsTrue(machine.AcceptBuzz(Client2, T0 + 0.45, T0 + 0.46, out _));
            Assert.AreEqual(QuizEvent.BuzzResolved, machine.Tick(T0 + 0.42 + Window, Rng()));
            return machine;
        }

        [Test]
        public void BuildProgress_BeforeAnyQuestion_IsEmpty()
        {
            var progress = new QuizStateMachine().BuildProgress();

            Assert.AreEqual(QuestionProgress.NoQuestion, progress.QuestionIndex);
            Assert.AreEqual(0, progress.Entries.Count);
        }

        [Test]
        public void BuildProgress_WhileCollectWindowIsOpen_HasNoRanksYet()
        {
            var machine = OpenBuzz();
            machine.AcceptBuzz(Client1, T0 + 0.40, T0 + 0.42, out _);

            var progress = machine.BuildProgress();

            Assert.AreEqual(0, progress.QuestionIndex);
            Assert.AreEqual(0, progress.Entries.Count, "押下順は勝者の確定時にまとめて出す（途中経過は出さない）。");
        }

        [Test]
        public void BuildProgress_AfterResolution_HasBuzzRanksAndAnswerOrder()
        {
            var progress = TwoBuzzersResolved().BuildProgress();

            Assert.AreEqual(1, Get(progress, Client1).BuzzRank);
            Assert.AreEqual(1, Get(progress, Client1).AnswerOrder);
            Assert.AreEqual(2, Get(progress, Client2).BuzzRank, "集計窓の中の 2 番手も順位が付く。");
            Assert.AreEqual(ParticipantProgress.NoRank, Get(progress, Client2).AnswerOrder, "2 番手は回答権を得ていない。");
        }

        [Test]
        public void BuildProgress_WrongThenReopen_ClearsRanksKeepsAnswerOrderAndMarksWrong()
        {
            var machine = TwoBuzzersResolved();
            Assert.AreEqual(QuizEvent.AnswerOpened, machine.Tick(T0 + 0.7, Rng()));
            Assert.IsTrue(machine.SubmitAnswer(Client1, "おおさか", T0 + 1.0, out _));

            var judging = machine.BuildProgress();
            Assert.IsFalse(
                Get(judging, Client1).Flags.Has(ParticipantProgressFlags.WrongAnswered),
                "判定の反映（Judging の tick）より前には誤答の印を付けない。");

            Assert.AreEqual(QuizEvent.BuzzReopened, machine.Tick(T0 + 1.0, Rng()));
            var reopened = machine.BuildProgress();

            Assert.IsTrue(Get(reopened, Client1).Flags.Has(ParticipantProgressFlags.WrongAnswered));
            Assert.AreEqual(ParticipantProgress.NoRank, Get(reopened, Client1).BuzzRank, "開き直した受付では前の押下順を消す。");
            Assert.AreEqual(ParticipantProgress.NoRank, Get(reopened, Client2).BuzzRank);
            Assert.AreEqual(1, Get(reopened, Client1).AnswerOrder, "回答順は問題の間ずっと残す。");
            Assert.IsTrue(reopened.IsExcludedFromBuzzing(Client1));
            Assert.IsFalse(reopened.IsExcludedFromBuzzing(Client2));
        }

        [Test]
        public void BuildProgress_SecondAnswererCorrect_GetsAnswerOrderTwoAndCorrectOnlyAtResult()
        {
            var machine = TwoBuzzersResolved();
            machine.Tick(T0 + 0.7, Rng());
            machine.SubmitAnswer(Client1, "おおさか", T0 + 1.0, out _);
            machine.Tick(T0 + 1.0, Rng());

            Assert.IsTrue(machine.AcceptBuzz(Client2, T0 + 1.5, T0 + 1.51, out _));
            Assert.AreEqual(QuizEvent.BuzzResolved, machine.Tick(T0 + 1.51 + Window, Rng()));
            Assert.AreEqual(1, Get(machine.BuildProgress(), Client2).BuzzRank);
            Assert.AreEqual(2, Get(machine.BuildProgress(), Client2).AnswerOrder);

            machine.Tick(T0 + 1.8, Rng());
            Assert.IsTrue(machine.SubmitAnswer(Client2, "東京", T0 + 2.0, out _));
            Assert.AreEqual(QuizPhase.Judging, machine.Phase);
            Assert.IsFalse(
                Get(machine.BuildProgress(), Client2).Flags.Has(ParticipantProgressFlags.Correct),
                "Judging の間は正誤を載せない。");

            Assert.AreEqual(QuizEvent.Judged, machine.Tick(T0 + 2.0, Rng()));
            var result = machine.BuildProgress();

            Assert.IsTrue(Get(result, Client2).Flags.Has(ParticipantProgressFlags.Correct));
            Assert.IsTrue(Get(result, Client1).Flags.Has(ParticipantProgressFlags.WrongAnswered));
        }

        [Test]
        public void BuildProgress_NextQuestion_ResetsAndMarksSuspendedPlayer()
        {
            var machine = TwoBuzzersResolved();
            machine.Tick(T0 + 0.7, Rng());
            machine.SubmitAnswer(Client1, "おおさか", T0 + 1.0, out _);
            machine.Tick(T0 + 1.0, Rng());
            machine.Tick(T0 + 20.0, Rng()); // 誰も押さずに時間切れ → Result
            Assert.AreEqual(QuizPhase.Result, machine.Phase);

            Assert.IsTrue(machine.StartQuestion(1, Answers, T0 + 30.0, out _));
            var progress = machine.BuildProgress();

            Assert.AreEqual(1, progress.QuestionIndex);
            Assert.AreEqual(1, progress.Entries.Count, "前の問題の押下順・誤答は持ち越さない。");
            Assert.IsTrue(Get(progress, Client1).Flags.Has(ParticipantProgressFlags.SuspendedSkipNext));
            Assert.IsTrue(progress.IsExcludedFromBuzzing(Client1));
        }

        [Test]
        public void BuildProgress_Choice_ShowsSubmittedWithoutJudgementUntilResult()
        {
            var machine = new QuizStateMachine();
            Assert.IsTrue(machine.StartQuestion(0, 2, 4, T0, out _));
            machine.Tick(T0, Rng());
            Assert.IsTrue(machine.SubmitChoice(Client1, 2, T0 + 1.0, out _));
            Assert.IsTrue(machine.SubmitChoice(Client2, 0, T0 + 1.0, out _));

            var answering = machine.BuildProgress();
            Assert.AreEqual(ParticipantProgressFlags.ChoiceSubmitted, Get(answering, Client1).Flags);
            Assert.AreEqual(ParticipantProgressFlags.ChoiceSubmitted, Get(answering, Client2).Flags, "正誤は判定まで載せない。");

            Assert.AreEqual(QuizEvent.ChoiceTimedOut, machine.Tick(T0 + 100.0, Rng()));
            Assert.AreEqual(ParticipantProgressFlags.ChoiceSubmitted, Get(machine.BuildProgress(), Client2).Flags);

            Assert.AreEqual(QuizEvent.ChoiceJudged, machine.Tick(T0 + 100.0, Rng()));
            var result = machine.BuildProgress();
            Assert.IsTrue(Get(result, Client1).Flags.Has(ParticipantProgressFlags.Correct));
            Assert.IsTrue(Get(result, Client2).Flags.Has(ParticipantProgressFlags.WrongAnswered));
        }

        [Test]
        public void TransferClient_MovesBuzzRankAndAnswerOrder()
        {
            var machine = TwoBuzzersResolved();

            Assert.IsTrue(machine.TransferClient(Client1, Reconnected));
            var progress = machine.BuildProgress();

            Assert.IsFalse(progress.TryGet(Client1, out _), "古い ID の行は残らない。");
            Assert.AreEqual(1, Get(progress, Reconnected).BuzzRank);
            Assert.AreEqual(1, Get(progress, Reconnected).AnswerOrder);
        }

        [Test]
        public void ForgetClient_RemovesTheSeatFromProgress()
        {
            var machine = TwoBuzzersResolved();

            machine.ForgetClient(Client2);

            Assert.IsFalse(machine.BuildProgress().TryGet(Client2, out _));
        }
    }
}
