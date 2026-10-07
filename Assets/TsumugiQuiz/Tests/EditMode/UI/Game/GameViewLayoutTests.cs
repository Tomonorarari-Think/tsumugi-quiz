using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.UI.Views.Game;

namespace TsumugiQuiz.Tests.EditMode.UI.Game
{
    /// <summary>
    /// Game 画面の縦幅対策（#187）の純ロジックの EditMode テスト。
    /// 判定後の選択肢の詰め表示（<see cref="GameViewPresenter.IsChoiceSectionCompact"/>）、
    /// 残り時間ラベルの空表示（<see cref="GameViewPresenter.FormatTimeRemaining"/>）、
    /// フェーズごとのスクロール先（<see cref="GameViewScrollPolicy"/>）。
    /// </summary>
    public class GameViewLayoutTests
    {
        [TestCase(QuizPhase.Lobby, false)]
        [TestCase(QuizPhase.Reading, false)]
        [TestCase(QuizPhase.BuzzOpen, false)]
        [TestCase(QuizPhase.Locked, false)]
        [TestCase(QuizPhase.Answering, false)]
        [TestCase(QuizPhase.ChoiceAnswering, false)]
        [TestCase(QuizPhase.Judging, false)]
        [TestCase(QuizPhase.Result, true)]
        [TestCase(QuizPhase.Finished, true)]
        public void IsChoiceSectionCompact_OnlyAfterJudgement(QuizPhase phase, bool expected)
        {
            Assert.AreEqual(expected, GameViewPresenter.IsChoiceSectionCompact(phase));
        }

        [Test]
        public void IsChoiceSectionCompact_MatchesResultSectionVisibility()
        {
            // 判定結果を出すフェーズと選択肢を詰めるフェーズは一致させる（結果の分の縦幅を空けるため）。
            foreach (QuizPhase phase in System.Enum.GetValues(typeof(QuizPhase)))
            {
                Assert.AreEqual(
                    GameViewPresenter.IsResultSectionVisible(phase),
                    GameViewPresenter.IsChoiceSectionCompact(phase),
                    phase.ToString());
            }
        }

        [Test]
        public void FormatTimeRemaining_WithDeadline_ShowsSecondsWithOneDecimal()
        {
            Assert.AreEqual("残り 3.5 秒", GameViewPresenter.FormatTimeRemaining(100.0, 3.5));
            Assert.AreEqual("残り 0.0 秒", GameViewPresenter.FormatTimeRemaining(100.0, 0.0));
        }

        [Test]
        public void FormatTimeRemaining_WithoutDeadline_KeepsLineHeightWithNonBreakingSpace()
        {
            var text = GameViewPresenter.FormatTimeRemaining(double.NaN, 0.0);

            Assert.AreEqual(GameViewPresenter.EmptyTimeRemainingText, text);
            Assert.AreEqual(((char)0x00A0).ToString(), text, "空文字にするとラベルの高さが縮み、下の要素がフェーズごとにずれるため。");
            Assert.IsTrue(string.IsNullOrWhiteSpace(text), "見た目は空欄のはず。");
        }

        private const ulong LocalClientId = 5UL;
        private const ulong OtherClientId = 9UL;

        [Test]
        public void BuzzButton_NonAnswererDuringAnswering_StaysVisibleButDisabled()
        {
            // 他の参加者が回答している間、自分の画面の早押しボタンは表示したまま無効（#187 レビュー L6）。
            Assert.IsTrue(GameViewPresenter.IsBuzzButtonVisible(QuizPhase.Answering, OtherClientId, LocalClientId));
            Assert.IsFalse(GameViewPresenter.IsBuzzButtonEnabled(QuizPhase.Answering, false, false));
            Assert.IsFalse(GameViewPresenter.IsBuzzButtonEnabled(QuizPhase.Answering, true, false));
        }

        [Test]
        public void BuzzButton_AnswererDuringAnswering_IsHidden()
        {
            Assert.IsFalse(GameViewPresenter.IsBuzzButtonVisible(QuizPhase.Answering, LocalClientId, LocalClientId));
        }

        [Test]
        public void BuzzButton_UnknownLocalClientDuringAnswering_StaysVisible()
        {
            Assert.IsTrue(GameViewPresenter.IsBuzzButtonVisible(QuizPhase.Answering, LocalClientId, null));
        }

        [TestCase(QuizPhase.Reading)]
        [TestCase(QuizPhase.BuzzOpen)]
        [TestCase(QuizPhase.Locked)]
        [TestCase(QuizPhase.Judging)]
        [TestCase(QuizPhase.Result)]
        public void BuzzButton_OutsideAnswering_StaysVisibleEvenForLockHolder(QuizPhase phase)
        {
            Assert.IsTrue(GameViewPresenter.IsBuzzButtonVisible(phase, LocalClientId, LocalClientId));
        }

        [Test]
        public void ScrollTarget_Reading_ReturnsTop()
        {
            Assert.AreEqual(
                GameScrollTarget.Top,
                GameViewScrollPolicy.TargetFor(QuizPhase.Reading, true, false, true));
        }

        [TestCase(true, GameScrollTarget.Buzz)]
        [TestCase(false, GameScrollTarget.None)]
        public void ScrollTarget_BuzzOpen_FollowsBuzzSectionVisibility(bool buzzVisible, GameScrollTarget expected)
        {
            Assert.AreEqual(expected, GameViewScrollPolicy.TargetFor(QuizPhase.BuzzOpen, buzzVisible, false, false));
        }

        [TestCase(true, GameScrollTarget.Answer)]
        [TestCase(false, GameScrollTarget.None)]
        public void ScrollTarget_Answering_OnlyForLockHolder(bool answerVisible, GameScrollTarget expected)
        {
            Assert.AreEqual(expected, GameViewScrollPolicy.TargetFor(QuizPhase.Answering, true, answerVisible, false));
        }

        [TestCase(true, GameScrollTarget.Choice)]
        [TestCase(false, GameScrollTarget.None)]
        public void ScrollTarget_ChoiceAnswering_FollowsChoiceSectionVisibility(bool choiceVisible, GameScrollTarget expected)
        {
            Assert.AreEqual(
                expected, GameViewScrollPolicy.TargetFor(QuizPhase.ChoiceAnswering, false, false, choiceVisible));
        }

        [Test]
        public void ScrollTarget_Result_ReturnsResult()
        {
            Assert.AreEqual(
                GameScrollTarget.Result,
                GameViewScrollPolicy.TargetFor(QuizPhase.Result, false, false, true));
        }

        [TestCase(QuizPhase.Lobby)]
        [TestCase(QuizPhase.Locked)]
        [TestCase(QuizPhase.Judging)]
        [TestCase(QuizPhase.Finished)]
        public void ScrollTarget_OtherPhases_DoNotScroll(QuizPhase phase)
        {
            Assert.AreEqual(GameScrollTarget.None, GameViewScrollPolicy.TargetFor(phase, true, true, true));
        }
    }
}
