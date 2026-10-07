using System;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.Tests.EditMode.Room
{
    /// <summary>
    /// <see cref="RoomSettings"/>（#26）のテスト。既定値は docs/room-settings.md §1/§2 の「ルーム設定」の行。
    /// </summary>
    public class RoomSettingsTests
    {
        [Test]
        public void Default_MatchesDocumentedValues()
        {
            var settings = RoomSettings.Default;

            Assert.AreEqual(HostRole.Player, settings.HostRole, "host.role の既定は player。");
            Assert.AreEqual(6, settings.MaxPlayers, "room.maxPlayers の既定は 6。");
            Assert.IsTrue(settings.ShuffleChoiceDisplay, "choices.shuffleDisplay の既定は true。");
            Assert.IsTrue(settings.AllowDuringReading, "buzz.allowDuringReading の既定は true（仮決め K12）。");
            Assert.AreEqual(20.0, settings.ChoiceTimeLimitSec, 1e-9, "answer.choiceTimeLimitSec の既定は 20 秒。");
            Assert.IsTrue(settings.TtsEnabled, "tts.enabled の既定は true。");
            Assert.AreEqual(1.0, settings.TtsSpeed, 1e-9, "tts.speed の既定は 1.0。");
            Assert.AreEqual(3000, settings.TtsReadyTimeoutMs, "tts.readyTimeoutMs の既定は 3000ms。");
            Assert.AreEqual(0.3, settings.TtsLeadTimeSec, 1e-9, "tts.leadTimeSec の既定は 0.3 秒。");
            Assert.AreEqual(80, settings.QuestionRevealMsPerChar, "question.revealMsPerChar の既定は 80ms/文字（#144）。");
            Assert.IsTrue(settings.ShowScores, "display.showScores の既定は「表示する」（#194、ユーザー承認済み）。");

            // Session 経由の項目（#19 の既定値との整合）。
            Assert.AreSame(SessionSettings.Default, settings.Session);
            Assert.AreEqual(10, settings.Scoring.CorrectPoints, "score.correctPoints の既定は 10。");
            Assert.AreEqual(0, settings.Scoring.IncorrectPoints, "score.incorrectPoints の既定は 0。");
            Assert.AreEqual(PenaltyKind.SkipNext, settings.Scoring.PenaltyType, "score.penaltyType の既定は skipNext。");
            Assert.AreEqual(-5, settings.Scoring.PenaltyMinusPoints, "score.penaltyMinusPoints の既定は -5。");
            Assert.AreEqual(10.0, settings.TimeLimits.BuzzTimeLimitSec, 1e-9, "buzz.timeLimitSec の既定は 10 秒。");
            Assert.AreEqual(15.0, settings.TimeLimits.AnswerTimeLimitSec, 1e-9, "answer.freeTextTimeLimitSec の既定は 15 秒。");
            Assert.AreEqual(0.15, settings.TimeLimits.CollectWindowSec, 1e-9, "buzz.collectWindowMs の既定は 150ms。");
            Assert.IsFalse(settings.AllowLateJoin, "network.allowLateJoin の既定は false。");
            Assert.AreEqual(5.0, settings.ResultAutoAdvanceSec, 1e-9, "result.autoAdvanceSec の既定は 5 秒。");
        }

        [TestCase(1)]
        [TestCase(13)]
        public void MaxPlayers_OutOfRange_Throws(int maxPlayers)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RoomSettings(maxPlayers: maxPlayers));
        }

        [TestCase(0.4)]
        [TestCase(2.1)]
        public void TtsSpeed_OutOfRange_Throws(double speed)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RoomSettings(ttsSpeed: speed));
        }

        [Test]
        public void ChoiceTimeLimitSec_NonPositive_Throws()
        {
            // answer.choiceTimeLimitSec は #79 で QuizTimeLimits.ChoiceTimeLimitSec に統合されたため、
            // RoomSettings 自身は QuizTimeLimits の検証（0 より大きい有限値）のみを引き継ぐ。
            // docs の 1〜60 秒という UI 向けの範囲は RoomSettingsValidator がクランプする（#26/#79 整合）。
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomSettings.Default.WithChoiceTimeLimitSec(0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomSettings.Default.WithChoiceTimeLimitSec(-1.0));
        }

        [Test]
        public void WithChoiceTimeLimitSec_UpdatesTimeLimitsOnly()
        {
            var original = RoomSettings.Default;

            var changed = original.WithChoiceTimeLimitSec(45.0);

            Assert.AreEqual(20.0, original.ChoiceTimeLimitSec, 1e-9, "元のインスタンスは変わらない。");
            Assert.AreEqual(45.0, changed.ChoiceTimeLimitSec, 1e-9);
            Assert.AreEqual(45.0, changed.TimeLimits.ChoiceTimeLimitSec, 1e-9, "TimeLimits にも反映される。");
            Assert.AreEqual(original.TimeLimits.BuzzTimeLimitSec, changed.TimeLimits.BuzzTimeLimitSec, 1e-9, "他の制限時間は変わらない。");
            Assert.AreEqual(original.TimeLimits.AnswerTimeLimitSec, changed.TimeLimits.AnswerTimeLimitSec, 1e-9);
            Assert.AreEqual(original.TimeLimits.CollectWindowSec, changed.TimeLimits.CollectWindowSec, 1e-9);
        }

        [Test]
        public void TtsReadyTimeoutMs_OutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RoomSettings(ttsReadyTimeoutMs: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RoomSettings(ttsReadyTimeoutMs: 15001));
        }

        [Test]
        public void QuestionRevealMsPerChar_OutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RoomSettings(questionRevealMsPerChar: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RoomSettings(questionRevealMsPerChar: 501));
            Assert.DoesNotThrow(() => new RoomSettings(questionRevealMsPerChar: 0), "0 は一括表示として有効。");
        }

        [Test]
        public void WithQuestionRevealMsPerChar_ReturnsNewInstanceAndKeepsOthers()
        {
            var original = RoomSettings.Default.WithMaxPlayers(9);

            var changed = original.WithQuestionRevealMsPerChar(200);

            Assert.AreNotSame(original, changed);
            Assert.AreEqual(RoomSettings.DefaultQuestionRevealMsPerChar, original.QuestionRevealMsPerChar);
            Assert.AreEqual(200, changed.QuestionRevealMsPerChar);
            Assert.AreEqual(9, changed.MaxPlayers);
            Assert.AreEqual(200, changed.WithMaxPlayers(4).QuestionRevealMsPerChar, "他の With でも引き継がれる。");
            Assert.AreEqual(200, changed.WithTts(false, 1.5, 1000, 0.5).QuestionRevealMsPerChar);
        }

        [Test]
        public void TtsLeadTimeSec_OutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RoomSettings(ttsLeadTimeSec: 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RoomSettings(ttsLeadTimeSec: 2.1));
        }

        [Test]
        public void With_ReturnsNewInstanceAndKeepsOthers()
        {
            var original = RoomSettings.Default;

            var changed = original.WithMaxPlayers(10);

            Assert.AreNotSame(original, changed);
            Assert.AreEqual(6, original.MaxPlayers, "元のインスタンスは変わらない。");
            Assert.AreEqual(10, changed.MaxPlayers);
            Assert.AreEqual(original.HostRole, changed.HostRole);
            Assert.AreSame(original.Session, changed.Session);
        }

        [Test]
        public void ToConverters_ReturnUnderlyingObjects()
        {
            var settings = RoomSettings.Default;

            Assert.AreSame(settings.Session, settings.ToSessionSettings());
            Assert.AreSame(settings.Session.Scoring, settings.ToScoringSettings());
            Assert.AreSame(settings.Session.TimeLimits, settings.ToQuizTimeLimits());
            Assert.AreSame(settings.Session.Questions, settings.ToQuestionSelectionSettings());
        }

        [Test]
        public void WithShowScores_ReturnsNewInstanceAndIsKeptByOtherWithMethods()
        {
            // issue #194: display.showScores は他の With* で作り直しても失われない。
            var original = RoomSettings.Default.WithQuestionRevealMsPerChar(120);
            var changed = original.WithShowScores(false);

            Assert.AreNotSame(original, changed);
            Assert.IsTrue(original.ShowScores);
            Assert.IsFalse(changed.ShowScores);
            Assert.AreEqual(120, changed.QuestionRevealMsPerChar);

            Assert.IsFalse(changed.WithHostRole(HostRole.Moderator).ShowScores);
            Assert.IsFalse(changed.WithMaxPlayers(4).ShowScores);
            Assert.IsFalse(changed.WithSession(SessionSettings.Default).ShowScores);
            Assert.IsFalse(changed.WithShuffleChoiceDisplay(false).ShowScores);
            Assert.IsFalse(changed.WithAllowDuringReading(false).ShowScores);
            Assert.IsFalse(changed.WithChoiceTimeLimitSec(30.0).ShowScores);
            Assert.IsFalse(changed.WithTts(false, 1.5, 1000, 0.5).ShowScores);
            Assert.IsFalse(changed.WithQuestionRevealMsPerChar(0).ShowScores);
        }
    }
}
