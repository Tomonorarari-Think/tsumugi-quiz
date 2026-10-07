using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Reveal;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.UI.Views.Game;

namespace TsumugiQuiz.Tests.EditMode.UI.Game
{
    /// <summary>
    /// <see cref="QuestionRevealPolicy"/>（問題文の文字送りをするか・最初の進め方、issue #144）のテスト。
    /// </summary>
    public class QuestionRevealPolicyTests
    {
        [Test]
        public void ShouldReveal_FreeTextDistribution_True()
        {
            Assert.IsTrue(QuestionRevealPolicy.ShouldReveal(
                QuestionType.FreeText, QuestionShownSource.Distribution, isModerator: false, msPerChar: 80, canRevealOnResync: false));
        }

        [Test]
        public void ShouldReveal_Choice_False()
        {
            Assert.IsFalse(QuestionRevealPolicy.ShouldReveal(
                QuestionType.Choice, QuestionShownSource.Distribution, false, 80, true), "選択式は一括表示（仕様 3）。");
        }

        [Test]
        public void ShouldReveal_ResyncNotRevealable_False()
        {
            Assert.IsFalse(QuestionRevealPolicy.ShouldReveal(
                QuestionType.FreeText, QuestionShownSource.Resync, false, 80, canRevealOnResync: false),
                "文字送りできない再同期（回答中・判定後で既知の再生開始時刻なし）は全文（仕様 4）。");
        }

        [Test]
        public void ShouldReveal_ResyncRevealable_True()
        {
            Assert.IsTrue(QuestionRevealPolicy.ShouldReveal(
                QuestionType.FreeText, QuestionShownSource.Resync, false, 80, canRevealOnResync: true),
                "文字送りできる再同期は途中から送る（仕様 4、#144 レビュー M-2）。");
        }

        [Test]
        public void ShouldReveal_Moderator_False()
        {
            Assert.IsFalse(QuestionRevealPolicy.ShouldReveal(
                QuestionType.FreeText, QuestionShownSource.Distribution, true, 80, true), "司会画面は全文（仕様 5）。");
        }

        [Test]
        public void ShouldReveal_ZeroSpeed_False()
        {
            Assert.IsFalse(QuestionRevealPolicy.ShouldReveal(
                QuestionType.FreeText, QuestionShownSource.Distribution, false, 0, true), "0 ms/文字は一括表示。");
        }

        [TestCase(true, QuizPhase.Answering, true)]
        [TestCase(true, QuizPhase.BuzzOpen, true)]
        [TestCase(false, QuizPhase.Reading, true)]
        [TestCase(false, QuizPhase.BuzzOpen, true)]
        [TestCase(false, QuizPhase.Locked, false)]
        [TestCase(false, QuizPhase.Answering, false)]
        [TestCase(false, QuizPhase.Result, false)]
        public void CanRevealOnResync_KnownReadingOrBeforeLock(bool hasKnownReading, QuizPhase phase, bool expected)
        {
            // #144 再レビュー M-2: 再接続（既知の値なし）でも受付前・受付中なら受付開始から固定速度で送る。
            Assert.AreEqual(expected, QuestionRevealPolicy.CanRevealOnResync(hasKnownReading, phase));
        }

        [TestCase(QuizPhase.Reading, true)]
        [TestCase(QuizPhase.BuzzOpen, true)]
        [TestCase(QuizPhase.Locked, true)]
        [TestCase(QuizPhase.Answering, true)]
        [TestCase(QuizPhase.Judging, true)]
        [TestCase(QuizPhase.Result, false)]
        [TestCase(QuizPhase.Finished, false)]
        [TestCase(QuizPhase.Lobby, false)]
        [TestCase(QuizPhase.ChoiceAnswering, false)]
        public void IsQuestionInProgress_OnlyBeforeJudgement(QuizPhase phase, bool expected)
        {
            Assert.AreEqual(expected, QuestionRevealPolicy.IsQuestionInProgress(phase));
        }

        [TestCase(QuizPhase.Locked, true)]
        [TestCase(QuizPhase.Answering, true)]
        [TestCase(QuizPhase.Judging, true)]
        [TestCase(QuizPhase.BuzzOpen, false)]
        [TestCase(QuizPhase.Reading, false)]
        public void IsBuzzLockedPhase_OnlyWhileSomeoneHasTheRight(QuizPhase phase, bool expected)
        {
            Assert.AreEqual(expected, QuestionRevealPolicy.IsBuzzLockedPhase(phase));
        }

        [Test]
        public void CreateInitial_NotRevealing_IsFull()
        {
            var schedule = QuestionRevealPolicy.CreateInitial(10, 1.0, shouldReveal: false, expectsReading: true, 80);

            Assert.AreEqual(QuestionRevealMode.Full, schedule.Mode);
            Assert.AreEqual(10, schedule.VisibleCount(1.0));
        }

        [Test]
        public void CreateInitial_NoRoomReading_FixedSpeedFromNow()
        {
            // 部屋として読み上げが無い（tts.enabled = false、統括判断 B 案）。
            var schedule = QuestionRevealPolicy.CreateInitial(10, 2.0, true, expectsReading: false, 120);

            Assert.AreEqual(QuestionRevealMode.FixedSpeed, schedule.Mode);
            Assert.AreEqual(2.0, schedule.StartTime, 1e-9);
            Assert.AreEqual(0.12, schedule.SecondsPerChar, 1e-9);
        }

        [Test]
        public void CreateInitial_RoomReading_AwaitsReadingWithoutTimer()
        {
            var schedule = QuestionRevealPolicy.CreateInitial(10, 2.0, true, expectsReading: true, 80);

            Assert.AreEqual(QuestionRevealMode.AwaitingReading, schedule.Mode);
            Assert.AreEqual(0, schedule.VisibleCount(2.0), "読み上げが始まるまでは 1 文字も出さない。");
            Assert.AreEqual(0, schedule.VisibleCount(100.0), "待ちの上限タイマーは無い（#144 レビュー M-1）。");
            Assert.AreEqual(80, schedule.FallbackMsPerChar);
        }

        [Test]
        public void CreateInitial_OutOfRangeSpeed_IsClamped()
        {
            var tooFast = QuestionRevealPolicy.CreateInitial(10, 0.0, true, false, -5);
            var tooSlow = QuestionRevealPolicy.CreateInitial(10, 0.0, true, false, 99999);

            Assert.AreEqual(QuestionRevealMode.Full, tooFast.Mode, "負の値は 0（一括表示）へ丸める。");
            Assert.AreEqual(QuestionRevealSchedule.MaxMsPerChar / 1000.0, tooSlow.SecondsPerChar, 1e-9);
        }

        [TestCase(80, true, 250)]
        [TestCase(300, true, 300)]
        [TestCase(0, true, 0)]
        [TestCase(80, false, 80)]
        public void ResolveFixedSpeedMsPerChar_ReadingRoomHasFloor(int msPerChar, bool roomHasReading, int expected)
        {
            // 再レビュー 2 回目: 読み上げのある部屋で時間軸が取れないときの固定速度は 250ms/文字以上（2026-09-30 ユーザー承認済み）。
            Assert.AreEqual(expected, QuestionRevealPolicy.ResolveFixedSpeedMsPerChar(msPerChar, roomHasReading));
        }

        [Test]
        public void CreateInitial_ReadingRoom_FallbackAndFixedSpeedUseFloor()
        {
            var awaiting = QuestionRevealPolicy.CreateInitial(10, 0.0, true, expectsReading: true, 80, roomHasReading: true);
            var fixedInReadingRoom = QuestionRevealPolicy.CreateInitial(10, 0.0, true, expectsReading: false, 80, roomHasReading: true);
            var fixedInSilentRoom = QuestionRevealPolicy.CreateInitial(10, 0.0, true, expectsReading: false, 80, roomHasReading: false);

            Assert.AreEqual(QuestionRevealPolicy.ReadingRoomFallbackMinMsPerChar, awaiting.FallbackMsPerChar);
            Assert.AreEqual(0.25, fixedInReadingRoom.SecondsPerChar, 1e-9, "読み上げのある部屋の固定速度は下限あり。");
            Assert.AreEqual(0.08, fixedInSilentRoom.SecondsPerChar, 1e-9, "読み上げの無い部屋はルーム設定どおり。");
        }

        [Test]
        public void ToLocalTime_ShiftsByServerOffset()
        {
            // サーバー時刻 100.5 に再生、いまのネットワーク時刻 100.2、表示側の時計 7.0 → 表示側で 7.3。
            Assert.AreEqual(7.3, QuestionRevealPolicy.ToLocalTime(100.5, 100.2, 7.0), 1e-9);
        }
    }
}
