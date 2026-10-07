using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core.Participants;
using TsumugiQuiz.Questions;
using TsumugiQuiz.UI.Views.Game;
using UnityEngine.UIElements;

namespace TsumugiQuiz.Tests.EditMode.UI.Game
{
    /// <summary>
    /// 参加者パネル（#194）の文言（<see cref="ParticipantPanelPresenter"/>）と
    /// 要素の組み立て（<see cref="ParticipantPanel"/>）のテスト。
    /// </summary>
    public class ParticipantPanelTests
    {
        private static ParticipantPanelRow Row(
            ulong clientId = 1,
            string name = "あかり",
            bool isLocal = false,
            bool isConnected = true,
            ParticipantStatus status = ParticipantStatus.None,
            int buzzRank = 0,
            bool tied = false,
            int answerOrder = 0,
            int score = 0) =>
            new ParticipantPanelRow(clientId, name, isLocal, isConnected, status, buzzRank, tied, answerOrder, score);

        private static (VisualElement Root, ParticipantPanel Panel) CreatePanel()
        {
            var root = new VisualElement();
            root.Add(new Label { name = ParticipantPanel.SummaryLabelName });
            root.Add(new ScrollView { name = ParticipantPanel.ListName });
            var panel = ParticipantPanel.Create(root);
            Assert.IsNotNull(panel);
            return (root, panel);
        }

        [TestCase(ParticipantStatus.None, "")]
        [TestCase(ParticipantStatus.Answering, "回答中")]
        [TestCase(ParticipantStatus.Correct, "○ 正解")]
        [TestCase(ParticipantStatus.Wrong, "× 不正解")]
        [TestCase(ParticipantStatus.Suspended, "休み（お手つき）")]
        [TestCase(ParticipantStatus.ChoiceSubmitted, "回答済み")]
        [TestCase(ParticipantStatus.ChoiceUnanswered, "未回答")]
        public void StatusLabel_MatchesStatus(ParticipantStatus status, string expected)
        {
            Assert.AreEqual(expected, ParticipantPanelPresenter.StatusLabel(status));
        }

        [Test]
        public void StatusLabel_DoesNotUseGlyphMissingFromTheFont()
        {
            // UI フォント（Noto Sans JP）は「✕」（U+2715）を持たないので使わない（豆腐になる）。
            foreach (ParticipantStatus status in System.Enum.GetValues(typeof(ParticipantStatus)))
            {
                StringAssert.DoesNotContain("✕", ParticipantPanelPresenter.StatusLabel(status));
            }
        }

        [Test]
        public void StatusText_JoinsAnswerOrderStatusAndTie()
        {
            // PR #201 レビュー M-1: 回答権を持っている行は「回答中」だけ（何人目かは出さない）。
            Assert.AreEqual(
                "回答中・同着",
                ParticipantPanelPresenter.StatusText(Row(status: ParticipantStatus.Answering, answerOrder: 2, tied: true, buzzRank: 1)));

            // 誤答・正解が確定した行は「回答n人目」を付ける。
            Assert.AreEqual("回答1人目・× 不正解", ParticipantPanelPresenter.StatusText(Row(status: ParticipantStatus.Wrong, answerOrder: 1)));
            Assert.AreEqual("回答2人目・○ 正解", ParticipantPanelPresenter.StatusText(Row(status: ParticipantStatus.Correct, answerOrder: 2)));
            Assert.AreEqual("× 不正解", ParticipantPanelPresenter.StatusText(Row(status: ParticipantStatus.Wrong)), "回答者が 1 人だけなら省く。");
            Assert.AreEqual(string.Empty, ParticipantPanelPresenter.StatusText(Row()));
        }

        [Test]
        public void StatusText_DisconnectedRow_ShowsDisconnected()
        {
            // PR #201 レビュー L-2: 切断中の行は状態欄に「切断中」を出す。
            Assert.AreEqual("切断中", ParticipantPanelPresenter.StatusText(Row(isConnected: false)));
            Assert.AreEqual(
                "回答1人目・× 不正解・同着・切断中",
                ParticipantPanelPresenter.StatusText(
                    Row(isConnected: false, status: ParticipantStatus.Wrong, answerOrder: 1, tied: true)));
        }

        [TestCase(true, false, true)]
        [TestCase(false, false, false)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public void ShouldShowScores_ModeratorAlwaysSeesScores(bool setting, bool isModerator, bool expected)
        {
            // 統括判断 #194: display.showScores が OFF でも司会には得点を出す（PR #201 レビュー L-7）。
            Assert.AreEqual(expected, ParticipantPanelPresenter.ShouldShowScores(setting, isModerator));
        }

        [Test]
        public void RankNameAndScoreText()
        {
            Assert.AreEqual(string.Empty, ParticipantPanelPresenter.RankText(Row()));
            Assert.AreEqual("2着", ParticipantPanelPresenter.RankText(Row(buzzRank: 2)));
            Assert.AreEqual("12着", ParticipantPanelPresenter.RankText(Row(buzzRank: 12)));
            Assert.AreEqual("あかり（あなた）", ParticipantPanelPresenter.NameText(Row(isLocal: true)));
            Assert.AreEqual("あかり", ParticipantPanelPresenter.NameText(Row()));
            Assert.AreEqual("-5点", ParticipantPanelPresenter.ScoreText(-5));
        }

        [Test]
        public void SummaryText_MatchesKind()
        {
            var rows = new List<ParticipantPanelRow>();
            Assert.AreEqual(
                "回答権あり 2 / 4 人",
                ParticipantPanelPresenter.SummaryText(new ParticipantPanelState(rows, true, ParticipantPanelSummaryKind.BuzzEligible, 2, 4)));
            Assert.AreEqual(
                "回答済み 1 / 3 人",
                ParticipantPanelPresenter.SummaryText(new ParticipantPanelState(rows, true, ParticipantPanelSummaryKind.ChoiceAnswered, 1, 3)));
            Assert.AreEqual(
                string.Empty,
                ParticipantPanelPresenter.SummaryText(new ParticipantPanelState(rows, true, ParticipantPanelSummaryKind.None, 0, 0)));
        }

        [TestCase(QuestionType.Choice, 2, 2, true)]
        [TestCase(QuestionType.Choice, 1, 2, false)]
        [TestCase(QuestionType.FreeText, 2, 2, false)]
        [TestCase(QuestionType.Choice, -1, -1, false)]
        public void IsCurrentQuestionChoice_TrustsTypeOnlyForTheCurrentQuestion(
            QuestionType type, int displayedIndex, int currentIndex, bool expected)
        {
            // PR #201 再レビュー L-1: 次の問題の提示が届く前は、前の問題の形式で判断しない。
            Assert.AreEqual(expected, ParticipantPanelPresenter.IsCurrentQuestionChoice(type, displayedIndex, currentIndex));
        }

        [Test]
        public void IsCurrentQuestionChoice_NoQuestion_IsFalse()
        {
            Assert.IsFalse(ParticipantPanelPresenter.IsCurrentQuestionChoice(null, 0, 0));
        }

        [Test]
        public void ShowsRankColumn_OnlyWhenSomeRowHasRank()
        {
            Assert.IsFalse(ParticipantPanelPresenter.ShowsRankColumn(null));
            Assert.IsFalse(ParticipantPanelPresenter.ShowsRankColumn(
                new ParticipantPanelState(new[] { Row(1), Row(2) }, true, ParticipantPanelSummaryKind.None, 0, 0)));
            Assert.IsTrue(ParticipantPanelPresenter.ShowsRankColumn(
                new ParticipantPanelState(new[] { Row(1), Row(2, buzzRank: 2) }, true, ParticipantPanelSummaryKind.None, 0, 0)));
        }

        [Test]
        public void Render_WithoutAnyRank_OmitsRankColumnAndIndent()
        {
            // PR #201 再レビュー M-B (b): 押下順位を持つ行が無ければバッジの列ごと出さない（選択式は常にこの状態）。
            var (root, panel) = CreatePanel();

            panel.Render(new ParticipantPanelState(
                new[] { Row(1, status: ParticipantStatus.ChoiceSubmitted) }, true, ParticipantPanelSummaryKind.None, 0, 0));

            Assert.IsNull(root.Q<Label>("participant-rank"));
            Assert.IsFalse(root.Q<Label>("participant-status").ClassListContains(ParticipantPanelPresenter.StatusIndentedClass));
        }

        [Test]
        public void Render_WithRank_PutsStatusBelowHeaderAndIndentsIt()
        {
            // PR #201 再レビュー M-B (a): 状態は上段（押下順位・名前・得点）の外の下段に出す。
            var (root, panel) = CreatePanel();

            panel.Render(new ParticipantPanelState(
                new[] { Row(1, status: ParticipantStatus.Wrong, answerOrder: 1), Row(2, buzzRank: 1, status: ParticipantStatus.Answering) },
                true,
                ParticipantPanelSummaryKind.None,
                0,
                0));

            var row = root.Q<VisualElement>("participant-row-1");
            var header = row.Q<VisualElement>("participant-row-header");
            var status = row.Q<Label>("participant-status");
            Assert.AreSame(row, status.parent, "状態は行の直下（上段の外）。");
            Assert.AreSame(header, row.Q<Label>("participant-score").parent, "得点は上段。");
            Assert.AreEqual(string.Empty, row.Q<Label>("participant-rank").text, "押下順位の無い行もバッジの列は空で持つ（字下げをそろえる）。");
            Assert.IsTrue(status.ClassListContains(ParticipantPanelPresenter.StatusIndentedClass));
        }

        [Test]
        public void Create_MissingElements_ReturnsNull()
        {
            Assert.IsNull(ParticipantPanel.Create(new VisualElement()));
            Assert.IsNull(ParticipantPanel.Create(null));
        }

        [Test]
        public void Create_ListIsNotFocusable()
        {
            var (root, _) = CreatePanel();
            var list = root.Q<ScrollView>(ParticipantPanel.ListName);

            Assert.IsFalse(list.focusable, "Space（早押し）を奪わないよう、行の一覧はフォーカスを持たない。");
            list.Query<VisualElement>().ForEach(
                element => Assert.IsFalse(element.focusable, $"{element.name}（{element.GetType().Name}）はフォーカスを持たない。"));
        }

        [Test]
        public void Render_BuildsRowsWithScoresAndStatus()
        {
            var (root, panel) = CreatePanel();
            var state = new ParticipantPanelState(
                new[]
                {
                    Row(1, "あかり", isLocal: true, status: ParticipantStatus.Answering, buzzRank: 1, score: 20),
                    Row(2, "ぶんた", isConnected: false, score: -5),
                },
                true,
                ParticipantPanelSummaryKind.BuzzEligible,
                1,
                1);

            panel.Render(state);

            var rows = root.Query<VisualElement>(className: ParticipantPanelPresenter.RowClass).ToList();
            Assert.AreEqual(2, rows.Count);
            Assert.IsTrue(rows[0].ClassListContains(ParticipantPanelPresenter.RowLocalClass));
            Assert.IsTrue(rows[1].ClassListContains(ParticipantPanelPresenter.RowDisconnectedClass));
            Assert.AreEqual("1着", rows[0].Q<Label>("participant-rank").text);
            Assert.AreEqual("あかり（あなた）", rows[0].Q<Label>("participant-name").text);
            Assert.AreEqual("回答中", rows[0].Q<Label>("participant-status").text);
            Assert.IsTrue(rows[0].Q<Label>("participant-status").ClassListContains("participant-status--answering"));
            Assert.AreEqual("20点", rows[0].Q<Label>("participant-score").text);
            Assert.AreEqual("切断中", rows[1].Q<Label>("participant-status").text, "切断中の行は状態欄に「切断中」を出す。");

            var summary = root.Q<Label>(ParticipantPanel.SummaryLabelName);
            Assert.AreEqual("回答権あり 1 / 1 人", summary.text);
            Assert.AreEqual(DisplayStyle.Flex, summary.style.display.value);
        }

        [Test]
        public void Render_ShowScoresOff_HasNoScoreLabels()
        {
            var (root, panel) = CreatePanel();

            panel.Render(new ParticipantPanelState(
                new[] { Row(1, score: 30) }, false, ParticipantPanelSummaryKind.None, 0, 0));

            Assert.IsNull(root.Q<Label>("participant-score"), "display.showScores が OFF なら得点を出さない。");
            Assert.IsNull(root.Q<Label>("participant-status"), "状態が何も無い行には状態の行を作らない。");
            Assert.AreEqual(DisplayStyle.None, root.Q<Label>(ParticipantPanel.SummaryLabelName).style.display.value);
        }

        [Test]
        public void Render_Again_ReplacesRows()
        {
            var (root, panel) = CreatePanel();
            panel.Render(new ParticipantPanelState(new[] { Row(1), Row(2) }, true, ParticipantPanelSummaryKind.None, 0, 0));

            panel.Render(new ParticipantPanelState(new[] { Row(3) }, true, ParticipantPanelSummaryKind.None, 0, 0));

            Assert.AreEqual(1, root.Query<VisualElement>(className: ParticipantPanelPresenter.RowClass).ToList().Count);
            panel.Render(null);
            Assert.AreEqual(0, root.Query<VisualElement>(className: ParticipantPanelPresenter.RowClass).ToList().Count);
        }
    }
}
