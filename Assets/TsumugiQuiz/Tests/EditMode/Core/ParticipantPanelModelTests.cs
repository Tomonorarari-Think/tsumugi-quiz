using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Participants;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 参加者パネル（#194）の表示内容を組み立てる <see cref="ParticipantPanelModel"/> のテスト。
    /// </summary>
    public class ParticipantPanelModelTests
    {
        private const ulong Host = 0;
        private const ulong Alice = 1;
        private const ulong Bob = 2;
        private const ulong Carol = 3;

        private static readonly ParticipantRosterEntry[] Roster =
        {
            new ParticipantRosterEntry(Host, "ホスト", true, false),
            new ParticipantRosterEntry(Alice, "あかり", true, false),
            new ParticipantRosterEntry(Bob, "ぶんた", true, false),
            new ParticipantRosterEntry(Carol, "ちなつ", false, false),
        };

        private static ParticipantPanelState Build(
            QuestionProgress progress,
            QuizPhase phase,
            bool isChoice = false,
            ulong locked = QuizStateMachine.NoClientId,
            bool showScores = true,
            IReadOnlyList<ParticipantRosterEntry> roster = null,
            IReadOnlyDictionary<ulong, int> scores = null,
            int questionIndex = 0)
        {
            return ParticipantPanelModel.Build(new ParticipantPanelInput(
                roster ?? Roster,
                scores ?? new Dictionary<ulong, int> { { Alice, 20 }, { Bob, -5 } },
                progress,
                questionIndex,
                phase,
                isChoice,
                locked,
                Alice,
                showScores));
        }

        private static QuestionProgress Progress(params ParticipantProgress[] entries) => QuestionProgress.Create(0, entries);

        private static ParticipantPanelRow Row(ParticipantPanelState state, ulong clientId)
        {
            foreach (var row in state.Rows)
            {
                if (row.ClientId == clientId)
                {
                    return row;
                }
            }

            Assert.Fail($"行 {clientId} がありません。");
            return default;
        }

        [Test]
        public void Build_KeepsRosterOrderAndMarksLocalAndScores()
        {
            var state = Build(QuestionProgress.Empty, QuizPhase.Lobby, questionIndex: -1);

            CollectionAssert.AreEqual(
                new ulong[] { Host, Alice, Bob, Carol },
                new[] { state.Rows[0].ClientId, state.Rows[1].ClientId, state.Rows[2].ClientId, state.Rows[3].ClientId },
                "並びは名簿順（参加順）で固定。");
            Assert.IsTrue(Row(state, Alice).IsLocal);
            Assert.IsFalse(Row(state, Bob).IsLocal);
            Assert.AreEqual(20, Row(state, Alice).Score);
            Assert.AreEqual(-5, Row(state, Bob).Score);
            Assert.AreEqual(0, Row(state, Host).Score, "得点表に行が無い人は 0 点。");
            Assert.IsFalse(Row(state, Carol).IsConnected);
            Assert.AreEqual(ParticipantPanelSummaryKind.None, state.SummaryKind);
        }

        [Test]
        public void Build_ExcludesModeratorHost()
        {
            var roster = new[]
            {
                new ParticipantRosterEntry(Host, "司会", true, true),
                new ParticipantRosterEntry(Alice, "あかり", true, false),
            };

            var state = Build(QuestionProgress.Empty, QuizPhase.BuzzOpen, roster: roster);

            Assert.AreEqual(1, state.Rows.Count);
            Assert.AreEqual(Alice, state.Rows[0].ClientId);
        }

        [Test]
        public void Build_ShowScoresFlagIsPassedThrough()
        {
            Assert.IsTrue(Build(QuestionProgress.Empty, QuizPhase.BuzzOpen).ShowScores);
            Assert.IsFalse(Build(QuestionProgress.Empty, QuizPhase.BuzzOpen, showScores: false).ShowScores);
        }

        [Test]
        public void Build_FreeTextAfterResolution_ShowsRanksAndAnsweringStatus()
        {
            var progress = Progress(
                new ParticipantProgress(Bob, 1, 1, ParticipantProgressFlags.TiedWithWinner),
                new ParticipantProgress(Alice, 2, 0, ParticipantProgressFlags.TiedWithWinner));

            var state = Build(progress, QuizPhase.Answering, locked: Bob);

            Assert.AreEqual(1, Row(state, Bob).BuzzRank);
            Assert.AreEqual(ParticipantStatus.Answering, Row(state, Bob).Status);
            Assert.AreEqual(2, Row(state, Alice).BuzzRank);
            Assert.IsTrue(Row(state, Alice).TiedWithWinner);
            Assert.AreEqual(ParticipantStatus.None, Row(state, Alice).Status);
            Assert.AreEqual(0, Row(state, Bob).AnswerOrder, "回答権を得た人が 1 人だけなら順番は出さない。");
            Assert.AreEqual(ParticipantPanelSummaryKind.BuzzEligible, state.SummaryKind);
            Assert.AreEqual(3, state.SummaryTotal, "分母は接続中の参加者。");
            Assert.AreEqual(3, state.SummaryCount);
        }

        [Test]
        public void Build_FreeTextAfterReopen_ShowsWrongSuspendedAndAnswerOrder()
        {
            var progress = Progress(
                new ParticipantProgress(Bob, 0, 1, ParticipantProgressFlags.WrongAnswered),
                new ParticipantProgress(Alice, 1, 2, ParticipantProgressFlags.None),
                new ParticipantProgress(Host, 0, 0, ParticipantProgressFlags.SuspendedSkipNext));

            var state = Build(progress, QuizPhase.Answering, locked: Alice);

            Assert.AreEqual(ParticipantStatus.Wrong, Row(state, Bob).Status);
            Assert.AreEqual(1, Row(state, Bob).AnswerOrder, "2 人以上が回答権を得たら回答順を出す。");
            Assert.AreEqual(2, Row(state, Alice).AnswerOrder);
            Assert.AreEqual(ParticipantStatus.Answering, Row(state, Alice).Status);
            Assert.AreEqual(ParticipantStatus.Suspended, Row(state, Host).Status);
            Assert.AreEqual(1, state.SummaryCount, "回答権あり = 誤答・休みでない接続中の人（回答中を含む）。");
            Assert.AreEqual(3, state.SummaryTotal);
        }

        [Test]
        public void Build_FreeTextResult_ShowsCorrectAndHidesSummary()
        {
            var progress = Progress(new ParticipantProgress(Alice, 1, 1, ParticipantProgressFlags.Correct));

            var state = Build(progress, QuizPhase.Result, locked: Alice);

            Assert.AreEqual(ParticipantStatus.Correct, Row(state, Alice).Status);
            Assert.AreEqual(ParticipantPanelSummaryKind.None, state.SummaryKind);
        }

        [Test]
        public void Build_ChoiceAnswering_ShowsSubmittedAndUnansweredWithoutRanks()
        {
            var progress = Progress(
                new ParticipantProgress(Alice, 3, 0, ParticipantProgressFlags.ChoiceSubmitted),
                new ParticipantProgress(Carol, 0, 0, ParticipantProgressFlags.ChoiceSubmitted),
                new ParticipantProgress(Host, 0, 0, ParticipantProgressFlags.SuspendedSkipNext));

            var state = Build(progress, QuizPhase.ChoiceAnswering, isChoice: true);

            Assert.AreEqual(ParticipantStatus.ChoiceSubmitted, Row(state, Alice).Status);
            Assert.AreEqual(0, Row(state, Alice).BuzzRank, "選択式に押下順は無い。");
            Assert.AreEqual(ParticipantStatus.ChoiceUnanswered, Row(state, Bob).Status);
            Assert.AreEqual(ParticipantStatus.Suspended, Row(state, Host).Status);
            Assert.AreEqual(ParticipantPanelSummaryKind.ChoiceAnswered, state.SummaryKind);
            Assert.AreEqual(1, state.SummaryCount, "切断中の人（ちなつ）は選択済みでも数えない。");
            Assert.AreEqual(
                2, state.SummaryTotal, "分母は接続中で休みでない参加者（休みのホスト・切断中のちなつは入れない。PR #201 再レビュー M-A）。");
        }

        [Test]
        public void Build_ChoiceBeforeAnsweringOpens_ShowsNoStatus()
        {
            var state = Build(QuestionProgress.Empty, QuizPhase.Reading, isChoice: true);

            Assert.AreEqual(ParticipantStatus.None, Row(state, Bob).Status);
            Assert.AreEqual(ParticipantPanelSummaryKind.None, state.SummaryKind);
        }

        [Test]
        public void Build_ProgressOfAnotherQuestion_IsIgnored()
        {
            var stale = QuestionProgress.Create(0, new[]
            {
                new ParticipantProgress(Bob, 0, 1, ParticipantProgressFlags.WrongAnswered),
            });

            var state = Build(stale, QuizPhase.BuzzOpen, questionIndex: 1);

            Assert.AreEqual(ParticipantStatus.None, Row(state, Bob).Status, "同期の途中で前の問題の状態を出さない。");
        }

        [Test]
        public void Input_CopiesRosterAndScores()
        {
            // PR #201 レビュー L-10: 入力は不変。渡した後に呼び出し側の辞書・リストを書き換えても影響しない。
            var scores = new Dictionary<ulong, int> { { Alice, 20 } };
            var roster = new List<ParticipantRosterEntry> { new ParticipantRosterEntry(Alice, "あかり", true, false) };
            var input = new ParticipantPanelInput(
                roster, scores, QuestionProgress.Empty, 0, QuizPhase.BuzzOpen, false, QuizStateMachine.NoClientId, Alice, true);

            scores[Alice] = 999;
            roster.Add(new ParticipantRosterEntry(Bob, "ぶんた", true, false));

            Assert.AreEqual(20, input.GetScore(Alice));
            Assert.AreEqual(1, input.Roster.Count);
        }

        [Test]
        public void Build_NullInput_ReturnsEmptyState()
        {
            var state = ParticipantPanelModel.Build(null);

            Assert.AreEqual(0, state.Rows.Count);
            Assert.AreEqual(ParticipantPanelSummaryKind.None, state.SummaryKind);
        }
    }
}
