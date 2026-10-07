using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Questions;
using TsumugiQuiz.UI.Views.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.UI.Game
{
    /// <summary>
    /// <see cref="GameViewPresenter"/> の EditMode テスト（issue #14）。
    /// Unity API に依存しない純ロジックのため、UI 要素なしにテストできる
    /// （<c>HostSetupPresenterTests</c> と同じ方針）。
    /// </summary>
    public class GameViewPresenterTests
    {
        private const ulong LocalClientId = 5UL;
        private const ulong OtherClientId = 9UL;

        [TestCase(QuizPhase.Lobby, "待機中")]
        [TestCase(QuizPhase.Reading, "問題文表示中")]
        [TestCase(QuizPhase.BuzzOpen, "早押し受付中")]
        [TestCase(QuizPhase.Locked, "回答権確定")]
        [TestCase(QuizPhase.Answering, "回答入力中")]
        [TestCase(QuizPhase.ChoiceAnswering, "選択中")]
        [TestCase(QuizPhase.Judging, "判定中")]
        [TestCase(QuizPhase.Result, "結果発表")]
        [TestCase(QuizPhase.Finished, "終了")]
        public void PhaseLabel_ReturnsExpectedJapaneseText(QuizPhase phase, string expected)
        {
            Assert.AreEqual(expected, GameViewPresenter.PhaseLabel(phase));
        }

        // issue #178: 残り秒数の計算そのものは Core の QuizDeadlines.RemainingSeconds に一本化した。
        // ここでは「Core の計算結果をそのまま使う」こと、および Core が「不明」として返す NaN
        // （締め切り無し・現在時刻が壊れている）を UI 向けに 0.0 へ変換することだけを検証する。
        // SelectIntervalStart（旧 GameViewPresenter）の起点選択ロジックは
        // QuizDeadlines.IntervalStartServerTime（Core）へ移した（Tests/EditMode/Core/QuizDeadlinesTests.cs 参照）。

        [Test]
        public void RemainingSeconds_MatchesCoreCalculation_WhenDeadlineIsFinite()
        {
            Assert.AreEqual(
                QuizDeadlines.RemainingSeconds(10.0, 5.5), GameViewPresenter.RemainingSeconds(10.0, 5.5), 1e-9);
            Assert.AreEqual(4.5, GameViewPresenter.RemainingSeconds(10.0, 5.5), 1e-9);
        }

        [Test]
        public void RemainingSeconds_ClampsToZero_WhenDeadlinePassed()
        {
            Assert.AreEqual(0.0, GameViewPresenter.RemainingSeconds(10.0, 12.0));
        }

        [Test]
        public void RemainingSeconds_ConvertsCoreNaN_ToZero_WhenNoDeadline()
        {
            Assert.IsTrue(double.IsNaN(QuizDeadlines.RemainingSeconds(double.NaN, 5.0)), "前提: Core は NaN を返す。");
            Assert.AreEqual(0.0, GameViewPresenter.RemainingSeconds(double.NaN, 5.0));
        }

        [Test]
        public void RemainingSeconds_ConvertsCoreNaN_ToZero_WhenCurrentTimeIsNonFinite()
        {
            Assert.IsTrue(double.IsNaN(QuizDeadlines.RemainingSeconds(10.0, double.NaN)), "前提: Core は NaN を返す。");
            Assert.AreEqual(0.0, GameViewPresenter.RemainingSeconds(10.0, double.NaN));
        }

        [Test]
        public void ProgressFraction_ReturnsOne_AtIntervalStart()
        {
            // T0=100, 締め切り=110（10秒枠）、現在=100（開始直後）→ 満タン。
            Assert.AreEqual(1.0, GameViewPresenter.ProgressFraction(100.0, 110.0, 100.0), 1e-9);
        }

        [Test]
        public void ProgressFraction_ReturnsHalf_AtMidpoint()
        {
            Assert.AreEqual(0.5, GameViewPresenter.ProgressFraction(100.0, 110.0, 105.0), 1e-9);
        }

        [Test]
        public void ProgressFraction_ReturnsZero_AfterDeadline()
        {
            Assert.AreEqual(0.0, GameViewPresenter.ProgressFraction(100.0, 110.0, 111.0));
        }

        [Test]
        public void ProgressFraction_ReturnsZero_WhenNoDeadline()
        {
            Assert.AreEqual(0.0, GameViewPresenter.ProgressFraction(100.0, double.NaN, 105.0));
        }

        [Test]
        public void ProgressFraction_ReturnsZero_WhenPhaseStartIsNaN()
        {
            // レビュー L-13: phaseStartServerTime が NaN だと total も NaN になり、
            // ガードが無いと fraction が NaN のまま返ってしまう（0.0〜1.0 の範囲チェックは NaN では false になるため）。
            Assert.AreEqual(0.0, GameViewPresenter.ProgressFraction(double.NaN, 110.0, 105.0));
        }

        [Test]
        public void ProgressFraction_ReturnsZero_WhenCurrentTimeIsNaN()
        {
            Assert.AreEqual(0.0, GameViewPresenter.ProgressFraction(100.0, 110.0, double.NaN));
        }

        [Test]
        public void IsBuzzButtonEnabled_True_WhenBuzzOpenAndNotYetBuzzedAndNotExcluded()
        {
            Assert.IsTrue(GameViewPresenter.IsBuzzButtonEnabled(
                QuizPhase.BuzzOpen, hasBuzzedLocally: false, isExcludedFromBuzzing: false));
        }

        [Test]
        public void IsBuzzButtonEnabled_False_WhenAlreadyBuzzed()
        {
            Assert.IsFalse(GameViewPresenter.IsBuzzButtonEnabled(
                QuizPhase.BuzzOpen, hasBuzzedLocally: true, isExcludedFromBuzzing: false));
        }

        [Test]
        public void IsBuzzButtonEnabled_False_WhenExcludedFromBuzzing()
        {
            // レビュー H-5: 誤答後の再開放で対象外にされている間は、まだ押していなくても押せない。
            Assert.IsFalse(GameViewPresenter.IsBuzzButtonEnabled(
                QuizPhase.BuzzOpen, hasBuzzedLocally: false, isExcludedFromBuzzing: true));
        }

        [TestCase(QuizPhase.Reading)]
        [TestCase(QuizPhase.Locked)]
        [TestCase(QuizPhase.Answering)]
        [TestCase(QuizPhase.Judging)]
        [TestCase(QuizPhase.Result)]
        public void IsBuzzButtonEnabled_False_WhenNotBuzzOpen(QuizPhase phase)
        {
            Assert.IsFalse(GameViewPresenter.IsBuzzButtonEnabled(phase, hasBuzzedLocally: false, isExcludedFromBuzzing: false));
        }

        [Test]
        public void IsAnswerFieldVisible_True_ForLockHolderDuringAnswering()
        {
            Assert.IsTrue(GameViewPresenter.IsAnswerFieldVisible(QuizPhase.Answering, LocalClientId, LocalClientId));
        }

        [Test]
        public void IsAnswerFieldVisible_False_ForOtherPlayer()
        {
            Assert.IsFalse(GameViewPresenter.IsAnswerFieldVisible(QuizPhase.Answering, OtherClientId, LocalClientId));
        }

        [Test]
        public void IsAnswerFieldVisible_False_OutsideAnsweringPhase()
        {
            Assert.IsFalse(GameViewPresenter.IsAnswerFieldVisible(QuizPhase.Locked, LocalClientId, LocalClientId));
        }

        [Test]
        public void IsAnswerFieldVisible_False_WhenLocalClientIdUnknown()
        {
            // レビュー L-14: セッションがまだ無く自分の ID が分からない（null）場合は表示しない。
            Assert.IsFalse(GameViewPresenter.IsAnswerFieldVisible(QuizPhase.Answering, LocalClientId, null));
        }

        [Test]
        public void IsHostControlsVisible_True_ForHostDuringResult()
        {
            Assert.IsTrue(GameViewPresenter.IsHostControlsVisible(QuizPhase.Result, isHost: true));
        }

        [Test]
        public void IsHostControlsVisible_False_ForNonHost()
        {
            Assert.IsFalse(GameViewPresenter.IsHostControlsVisible(QuizPhase.Result, isHost: false));
        }

        [Test]
        public void IsHostControlsVisible_False_OutsideResultPhase()
        {
            Assert.IsFalse(GameViewPresenter.IsHostControlsVisible(QuizPhase.Answering, isHost: true));
        }

        [Test]
        public void AnswerInputError_ReturnsMessage_ForEmptyText()
        {
            var error = GameViewPresenter.AnswerInputError(string.Empty);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void AnswerInputError_ReturnsMessage_ForWhitespaceOnlyText()
        {
            var error = GameViewPresenter.AnswerInputError("   ");
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void AnswerInputError_ReturnsMessage_WhenTooLong()
        {
            var tooLong = new string('あ', GameViewPresenter.MaxAnswerLength + 1);
            var error = GameViewPresenter.AnswerInputError(tooLong);
            Assert.IsNotEmpty(error);
            StringAssert.Contains(GameViewPresenter.MaxAnswerLength.ToString(), error);
        }

        [Test]
        public void AnswerInputError_ReturnsEmpty_ForValidText()
        {
            Assert.AreEqual(string.Empty, GameViewPresenter.AnswerInputError("とうきょう"));
        }

        [Test]
        public void AnswerInputError_ReturnsEmpty_AtExactMaxLength()
        {
            var exact = new string('あ', GameViewPresenter.MaxAnswerLength);
            Assert.AreEqual(string.Empty, GameViewPresenter.AnswerInputError(exact));
        }

        [Test]
        public void FormatBuzzWinner_ShowsYou_WhenLocalPlayerWon()
        {
            var text = GameViewPresenter.FormatBuzzWinner(LocalClientId, LocalClientId, wasTie: false);
            StringAssert.Contains("あなた", text);
        }

        [Test]
        public void FormatBuzzWinner_MentionsTie_WhenWasTie()
        {
            var text = GameViewPresenter.FormatBuzzWinner(LocalClientId, LocalClientId, wasTie: true);
            StringAssert.Contains("同着", text);
        }

        [Test]
        public void FormatBuzzWinner_UsesFallbackName_WhenNoResolverProvided()
        {
            var text = GameViewPresenter.FormatBuzzWinner(OtherClientId, LocalClientId, wasTie: false);
            StringAssert.Contains(GameViewPresenter.FormatPlayerFallbackName(OtherClientId), text);
            StringAssert.DoesNotContain("あなた", text);
        }

        [Test]
        public void FormatBuzzWinner_UsesResolvedName_WhenResolverProvided()
        {
            var text = GameViewPresenter.FormatBuzzWinner(
                OtherClientId, LocalClientId, wasTie: false, resolveName: _ => "つむぎ");
            StringAssert.Contains("つむぎ", text);
            StringAssert.DoesNotContain(GameViewPresenter.FormatPlayerFallbackName(OtherClientId), text);
        }

        [Test]
        public void FormatBuzzWinner_FallsBackToDefaultName_WhenResolverReturnsEmpty()
        {
            var text = GameViewPresenter.FormatBuzzWinner(
                OtherClientId, LocalClientId, wasTie: false, resolveName: _ => string.Empty);
            StringAssert.Contains(GameViewPresenter.FormatPlayerFallbackName(OtherClientId), text);
        }

        [Test]
        public void FormatBuzzWinner_AlwaysShowsYou_ForLocalPlayer_EvenWithResolver()
        {
            var text = GameViewPresenter.FormatBuzzWinner(
                LocalClientId, LocalClientId, wasTie: false, resolveName: _ => "別名");
            StringAssert.Contains("あなた", text);
            StringAssert.DoesNotContain("別名", text);
        }

        [Test]
        public void FormatBuzzWinner_UsesFallbackName_WhenLocalClientIdUnknown()
        {
            // レビュー L-14: 自分の ID がまだ分からない（null）場合、誰も「あなた」にはならない。
            var text = GameViewPresenter.FormatBuzzWinner(OtherClientId, null, wasTie: false);
            StringAssert.DoesNotContain("あなた", text);
        }

        [Test]
        public void FormatBuzzReopened_ShowsYou_WhenLocalPlayerWasPenalized()
        {
            var text = GameViewPresenter.FormatBuzzReopened(LocalClientId, LocalClientId);
            StringAssert.Contains("あなた", text);
        }

        [Test]
        public void FormatBuzzReopened_UsesResolvedName_WhenResolverProvided()
        {
            var text = GameViewPresenter.FormatBuzzReopened(OtherClientId, LocalClientId, resolveName: _ => "つむぎ");
            StringAssert.Contains("つむぎ", text);
        }

        [Test]
        public void ExitConfirmMessage_MentionsEveryoneDisconnecting_ForHost()
        {
            var text = GameViewPresenter.ExitConfirmMessage(isHost: true);
            StringAssert.Contains("全員", text);
        }

        [Test]
        public void ExitConfirmMessage_IsShortConfirmation_ForNonHost()
        {
            var text = GameViewPresenter.ExitConfirmMessage(isHost: false);
            StringAssert.DoesNotContain("全員", text);
        }

        [Test]
        public void FormatJudgement_Correct_ForLocalAnswerer_IncludesYouAndDeltaAndTotal()
        {
            var text = GameViewPresenter.FormatJudgement(
                QuizJudgement.Correct, LocalClientId, LocalClientId, "とうきょう", scoreDelta: 10, totalScore: 10);
            StringAssert.Contains("あなた", text);
            StringAssert.Contains("正解", text);
            StringAssert.Contains("とうきょう", text);
            StringAssert.Contains("+10", text);
            StringAssert.Contains("合計10点", text);
        }

        [Test]
        public void FormatJudgement_Correct_ForOtherAnswerer_OmitsScore()
        {
            // レビュー M-7: 他人の回答結果には、あたかも自分の得点であるかのように見える
            // 得点の増減・累計を表示しない。
            var text = GameViewPresenter.FormatJudgement(
                QuizJudgement.Correct, OtherClientId, LocalClientId, "とうきょう", scoreDelta: 10, totalScore: 10);
            StringAssert.Contains(GameViewPresenter.FormatPlayerFallbackName(OtherClientId), text);
            StringAssert.Contains("正解", text);
            StringAssert.DoesNotContain("合計", text);
            Assert.IsFalse(text.Contains("+10"), "他人の得点増減は表示しないはず。実際: " + text);
        }

        [Test]
        public void FormatJudgement_Wrong_ForLocalAnswerer_ShowsNonPositiveDeltaWithoutPlusSign()
        {
            var text = GameViewPresenter.FormatJudgement(
                QuizJudgement.Wrong, LocalClientId, LocalClientId, "とうきょう", scoreDelta: 0, totalScore: 0);
            StringAssert.Contains("あなた", text);
            StringAssert.Contains("不正解", text);
            Assert.IsFalse(text.Contains("+0"), "0 に + 記号は付かないはず。実際: " + text);
        }

        [Test]
        public void FormatJudgement_TimedOut_ShowsCorrectAnswerWithoutScore()
        {
            var text = GameViewPresenter.FormatJudgement(
                QuizJudgement.TimedOut, LocalClientId, LocalClientId, "とうきょう", scoreDelta: 0, totalScore: 0);
            StringAssert.Contains("時間切れ", text);
            StringAssert.Contains("とうきょう", text);
        }

        /// <summary>
        /// #200: 押せる人が居なくなって締めた場合は、時間切れとは別の文言にする（得点は出さない）。
        /// </summary>
        [Test]
        public void FormatJudgement_NoEligibleBuzzers_ShowsDedicatedTextDistinctFromTimeUp()
        {
            var text = GameViewPresenter.FormatJudgement(
                QuizJudgement.NoEligibleBuzzers, QuizStateMachine.NoClientId, LocalClientId, "とうきょう", scoreDelta: 0, totalScore: 0);

            Assert.AreEqual("回答できる人がいないため締め切りました。正解: とうきょう", text);
            StringAssert.DoesNotContain("時間切れ", text, "時間切れとは区別する。");
        }

        /// <summary>
        /// #209: ホストから届く正解（問題データ）の見えない書式文字は、結果の文言に入れる前に除く。
        /// 閉じていない双方向の制御（U+202E）が残ると、後ろに続く得点の部分まで並びが入れ替わって見える。
        /// </summary>
        [Test]
        public void FormatJudgement_RemovesHiddenCharactersFromTheCorrectAnswer()
        {
            var text = GameViewPresenter.FormatJudgement(
                QuizJudgement.Correct, LocalClientId, LocalClientId, "とう\u202Eきょう\u200B", scoreDelta: 10, totalScore: 30);

            // StringAssert はカルチャ依存の比較で見えない文字を無視するため、序数（string.Contains）で比べる。
            Assert.IsTrue(text.Contains("正解: とうきょう"), text);
            Assert.IsFalse(text.Contains("\u202E"), "U+202E が残っている。");
            Assert.IsFalse(text.Contains("\u200B"), "U+200B が残っている。");
        }

        [Test]
        public void FormatChoiceResult_RemovesHiddenCharactersAndExcessMarksFromTheChoice()
        {
            var text = GameViewPresenter.FormatChoiceResult(
                "東\u2066京\u0301\u0301\u0301\u0301\u0301\u0301", localIsCorrect: true, scoreDelta: 10, totalScore: 10);

            Assert.IsTrue(text.StartsWith("正解: 東京\u0301\u0301\u0301\u0301 / ", System.StringComparison.Ordinal), text);
        }

        [Test]
        public void ToQuestionDisplayText_RemovesHiddenCharactersButKeepsLineBreaks()
        {
            // 問題文・選択肢は改行や連続した空白を書いたとおりに出す（切り詰めもしない）。見えない文字と 5 個目以降の結合記号だけ除く。
            Assert.AreEqual(
                "一行目\n二行目  〇\u0301\u0301\u0301\u0301",
                GameViewPresenter.ToQuestionDisplayText("一行目\u202E\n二行目  \u200B〇\u0301\u0301\u0301\u0301\u0301"));
            Assert.AreEqual(string.Empty, GameViewPresenter.ToQuestionDisplayText(null));
        }

        /// <summary>
        /// 判定が付いていない（<see cref="QuizJudgement.None"/>）ときは文言を作らない。
        /// #132 レビュー L4-3: このとき result-section は表示されない仕様（M3-3）なので、
        /// 想定外の呼び出しに気づけるよう警告ログを出す。テストでも出ることを明示しておく。
        /// </summary>
        [Test]
        public void FormatJudgement_None_ReturnsEmpty()
        {
            LogAssert.Expect(LogType.Warning, new Regex("GameViewPresenter.*対応する文言がありません"));

            Assert.AreEqual(
                string.Empty, GameViewPresenter.FormatJudgement(QuizJudgement.None, LocalClientId, LocalClientId, "x", 0, 0));
        }

        [Test]
        public void FormatJudgement_UsesResolvedName_ForOtherAnswerer()
        {
            var text = GameViewPresenter.FormatJudgement(
                QuizJudgement.Correct, OtherClientId, LocalClientId, "とうきょう", scoreDelta: 10, totalScore: 10,
                resolveName: _ => "つむぎ");
            StringAssert.Contains("つむぎ", text);
        }

        // ------------------------------------------------------------------
        // #132 レビュー H-1: 縦幅を節約するためのセクション出し分け
        // ------------------------------------------------------------------

        [Test]
        public void IsBuzzSectionVisible_False_ForChoiceQuestion()
        {
            Assert.IsFalse(
                GameViewPresenter.IsBuzzSectionVisible(QuestionType.Choice),
                "選択式は早押しなし（確定仕様 K18）なので早押しセクションは出さない。");
        }

        [Test]
        public void IsBuzzSectionVisible_True_ForFreeTextQuestion()
        {
            Assert.IsTrue(GameViewPresenter.IsBuzzSectionVisible(QuestionType.FreeText));
        }

        [Test]
        public void IsBuzzSectionVisible_True_WhenQuestionNotShownYet()
        {
            Assert.IsTrue(
                GameViewPresenter.IsBuzzSectionVisible(null),
                "まだ出題されていない間は従来どおり早押しセクションを出す。");
        }

        [TestCase(QuizPhase.Result, true)]
        [TestCase(QuizPhase.Finished, true)]
        [TestCase(QuizPhase.Lobby, false)]
        [TestCase(QuizPhase.Reading, false)]
        [TestCase(QuizPhase.BuzzOpen, false)]
        [TestCase(QuizPhase.Locked, false)]
        [TestCase(QuizPhase.Answering, false)]
        [TestCase(QuizPhase.ChoiceAnswering, false)]
        [TestCase(QuizPhase.Judging, false)]
        public void IsResultSectionVisible_OnlyAfterJudgement(QuizPhase phase, bool expected)
        {
            Assert.AreEqual(expected, GameViewPresenter.IsResultSectionVisible(phase));
        }

        [Test]
        public void ToQuestionDisplayText_KeepsLoneSurrogatesWithoutThrowing()
        {
            // PR #211 レビュー L-8: 単独のサロゲートを含む問題文は、見えない文字の規則の対象外なのでそのまま返す（例外にしない）。
            Assert.AreEqual("あ\uD800い", GameViewPresenter.ToQuestionDisplayText("あ\uD800い"));
            Assert.AreEqual("あ\uDC00", GameViewPresenter.ToQuestionDisplayText("あ\uDC00"));
        }

        [Test]
        public void ToQuestionDisplayText_ChoiceOfOnlyHiddenCharacters_BecomesEmpty()
        {
            // PR #211 レビュー L-8: 除く文字だけの選択肢は空文字になる（読み込み時は空ではないので通る）。
            // 除く前も何も見えなかったので、ボタンの見た目は変わらない。
            Assert.AreEqual(string.Empty, GameViewPresenter.ToQuestionDisplayText("\u200B\u202E\u3164"));
            Assert.AreEqual(
                "正解: （あなたは選択しませんでした）",
                GameViewPresenter.FormatChoiceResult("\u200B\u202E", localIsCorrect: null, scoreDelta: 0, totalScore: 0));
        }
    }
}
