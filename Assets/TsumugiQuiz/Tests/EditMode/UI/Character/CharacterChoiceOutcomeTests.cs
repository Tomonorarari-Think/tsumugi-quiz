using System;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Participants;
using TsumugiQuiz.Network;
using TsumugiQuiz.UI;

namespace TsumugiQuiz.Tests.EditMode.UI.Character
{
    /// <summary>
    /// 選択式の一斉判定（<see cref="GameSession.ChoiceResolved"/>）を立ち絵の表情へ読み替える規則（issue #212）。
    /// 選んだ人は自分の正誤、選ばなかったプレイヤーは時間切れ、回答できない立場（司会・休み）は全体の結果
    /// （ユーザー決定 2026-10-03）。
    /// </summary>
    public class CharacterChoiceOutcomeTests
    {
        private const ulong Local = 7;
        private const ulong Other = 9;
        private const int CurrentQuestion = 3;

        private static ChoiceAnswerEntry Entry(ulong clientId, bool isCorrect) =>
            new ChoiceAnswerEntry(clientId, choiceIndex: 0, isCorrect: isCorrect, scoreDelta: 0, totalScore: 0);

        // ---- 表情の決め方 ---------------------------------------------------------

        [TestCase(true)]
        [TestCase(false)]
        public void LocalChoseCorrectly_IsCorrect_EvenIfOthersWrong(bool isLocalAnswerer)
        {
            var entries = new[] { Entry(Other, false), Entry(Local, true) };
            Assert.AreEqual(QuizJudgement.Correct, CharacterChoiceOutcome.ToJudgement(entries, Local, isLocalAnswerer));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LocalChoseWrongly_IsWrong_EvenIfOthersCorrect(bool isLocalAnswerer)
        {
            var entries = new[] { Entry(Other, true), Entry(Local, false) };
            Assert.AreEqual(QuizJudgement.Wrong, CharacterChoiceOutcome.ToJudgement(entries, Local, isLocalAnswerer));
        }

        [Test]
        public void AnswererWhoDidNotChoose_IsTimedOut_EvenIfSomeoneCorrect()
        {
            var entries = new[] { Entry(Other, true) };
            Assert.AreEqual(QuizJudgement.TimedOut, CharacterChoiceOutcome.ToJudgement(entries, Local, isLocalAnswerer: true));
        }

        [Test]
        public void AnswererWhoDidNotChoose_NobodyChose_IsTimedOut()
        {
            Assert.AreEqual(
                QuizJudgement.TimedOut,
                CharacterChoiceOutcome.ToJudgement(Array.Empty<ChoiceAnswerEntry>(), Local, isLocalAnswerer: true));
        }

        [Test]
        public void NonAnswerer_SomeoneCorrect_IsCorrect()
        {
            var entries = new[] { Entry(Other, false), Entry(Other + 1, true) };
            Assert.AreEqual(QuizJudgement.Correct, CharacterChoiceOutcome.ToJudgement(entries, Local, isLocalAnswerer: false));
        }

        [Test]
        public void NonAnswerer_EveryoneWrong_IsWrong()
        {
            var entries = new[] { Entry(Other, false), Entry(Other + 1, false) };
            Assert.AreEqual(QuizJudgement.Wrong, CharacterChoiceOutcome.ToJudgement(entries, Local, isLocalAnswerer: false));
        }

        [Test]
        public void NonAnswerer_NobodyChose_IsTimedOut()
        {
            Assert.AreEqual(
                QuizJudgement.TimedOut,
                CharacterChoiceOutcome.ToJudgement(Array.Empty<ChoiceAnswerEntry>(), Local, isLocalAnswerer: false));
        }

        [Test]
        public void LocalClientIdUnknown_UsesEveryoneResult()
        {
            // 自分の ID が分からない間は、選んだかどうかも分からないので全体の結果で決める。
            var entries = new[] { Entry(Local, false), Entry(Other, true) };
            Assert.AreEqual(
                QuizJudgement.Correct,
                CharacterChoiceOutcome.ToJudgement(entries, localClientId: null, isLocalAnswerer: true));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void NullEntries_AreTreatedAsNobodyChose(bool isLocalAnswerer)
        {
            Assert.AreEqual(QuizJudgement.TimedOut, CharacterChoiceOutcome.ToJudgement(null, Local, isLocalAnswerer));
        }

        // ---- 自分がその問題の回答者か -------------------------------------------------

        private static QuestionProgress Progress(int questionIndex, ParticipantProgressFlags localFlags) =>
            QuestionProgress.Create(questionIndex, new[]
            {
                new ParticipantProgress(Local, ParticipantProgress.NoRank, ParticipantProgress.NoRank, localFlags),
                new ParticipantProgress(Other, ParticipantProgress.NoRank, ParticipantProgress.NoRank, ParticipantProgressFlags.None),
            });

        [Test]
        public void IsAnswerer_Player_IsTrue()
        {
            Assert.IsTrue(CharacterChoiceOutcome.IsLocalAnswerer(
                isModeratorHost: false, Local, Progress(CurrentQuestion, ParticipantProgressFlags.None), CurrentQuestion));
        }

        [Test]
        public void IsAnswerer_PlayerWithoutProgressRow_IsTrue()
        {
            // 進行状態にまだ行が無い（何も起きていない）プレイヤーも、選べる立場。
            Assert.IsTrue(CharacterChoiceOutcome.IsLocalAnswerer(
                isModeratorHost: false, Local, QuestionProgress.Empty, CurrentQuestion));
        }

        [Test]
        public void IsAnswerer_ModeratorHost_IsFalse()
        {
            Assert.IsFalse(CharacterChoiceOutcome.IsLocalAnswerer(
                isModeratorHost: true, Local, Progress(CurrentQuestion, ParticipantProgressFlags.None), CurrentQuestion));
        }

        [Test]
        public void IsAnswerer_SuspendedThisQuestion_IsFalse()
        {
            // お手つきの「次問休み」で、この問題は選べない（QuizStateMachine.SubmitChoice が Penalized で拒否する）。
            Assert.IsFalse(CharacterChoiceOutcome.IsLocalAnswerer(
                isModeratorHost: false, Local, Progress(CurrentQuestion, ParticipantProgressFlags.SuspendedSkipNext), CurrentQuestion));
        }

        [Test]
        public void IsAnswerer_SuspendedFlagOfAnotherQuestion_IsIgnored()
        {
            // 進行状態がまだ前の問題のもの（同期待ち）なら、その休みはこの問題には当てはまらない。
            Assert.IsTrue(CharacterChoiceOutcome.IsLocalAnswerer(
                isModeratorHost: false, Local, Progress(CurrentQuestion - 1, ParticipantProgressFlags.SuspendedSkipNext), CurrentQuestion));
        }

        [Test]
        public void IsAnswerer_WrongAnsweredFlag_StillAnswerer()
        {
            // 選択式の不正解は判定後に立つ印で、選べる立場だったことは変わらない。
            Assert.IsTrue(CharacterChoiceOutcome.IsLocalAnswerer(
                isModeratorHost: false, Local, Progress(CurrentQuestion, ParticipantProgressFlags.WrongAnswered), CurrentQuestion));
        }

        [Test]
        public void IsAnswerer_LocalClientIdUnknown_IsFalse()
        {
            Assert.IsFalse(CharacterChoiceOutcome.IsLocalAnswerer(
                isModeratorHost: false, localClientId: null, Progress(CurrentQuestion, ParticipantProgressFlags.None), CurrentQuestion));
        }

        [Test]
        public void IsAnswerer_NullProgress_IsTreatedAsNoRow()
        {
            Assert.IsTrue(CharacterChoiceOutcome.IsLocalAnswerer(isModeratorHost: false, Local, progress: null, CurrentQuestion));
        }
    }
}
