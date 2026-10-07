using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.Tests.EditMode.Room
{
    /// <summary>
    /// <see cref="RoomSettingsValidator"/>（#26）のテスト。docs/room-settings.md §5:
    /// 範囲外の値は既定値へクランプし、警告を返す。
    /// </summary>
    public class RoomSettingsValidatorTests
    {
        [Test]
        public void Validate_NullInput_ReturnsDefaultsWithoutWarnings()
        {
            var result = RoomSettingsValidator.Validate(null);

            Assert.IsFalse(result.HasWarnings);
            Assert.AreEqual(RoomSettings.DefaultMaxPlayers, result.Settings.MaxPlayers);
            Assert.AreEqual(HostRole.Player, result.Settings.HostRole);
        }

        [Test]
        public void Validate_MaxPlayersOutOfRange_ClampsAndWarns()
        {
            var result = RoomSettingsValidator.Validate(new RoomSettingsInput { MaxPlayers = 99 });

            Assert.AreEqual(RoomSettings.MaxMaxPlayers, result.Settings.MaxPlayers);
            Assert.IsTrue(result.HasWarnings);
            StringAssert.Contains("room.maxPlayers", result.Warnings[0]);
        }

        [Test]
        public void Validate_MaxPlayersBelowMin_ClampsToMin()
        {
            var result = RoomSettingsValidator.Validate(new RoomSettingsInput { MaxPlayers = 0 });

            Assert.AreEqual(RoomSettings.MinMaxPlayers, result.Settings.MaxPlayers);
            Assert.IsTrue(result.HasWarnings);
        }

        [Test]
        public void Validate_UnknownHostRole_FallsBackToDefaultWithWarning()
        {
            var result = RoomSettingsValidator.Validate(new RoomSettingsInput { HostRole = "admin" });

            Assert.AreEqual(HostRole.Player, result.Settings.HostRole);
            Assert.IsTrue(result.HasWarnings);
            StringAssert.Contains("host.role", result.Warnings[0]);
        }

        [Test]
        public void Validate_ModeratorHostRole_Parsed()
        {
            var result = RoomSettingsValidator.Validate(new RoomSettingsInput { HostRole = "moderator" });

            Assert.AreEqual(HostRole.Moderator, result.Settings.HostRole);
            Assert.IsFalse(result.HasWarnings);
        }

        [Test]
        public void Validate_UnknownTypeFilter_FallsBackToBoth()
        {
            var result = RoomSettingsValidator.Validate(new RoomSettingsInput { QuestionsTypeFilter = "invalid" });

            Assert.AreEqual(QuestionTypeFilter.Both, result.Settings.Questions.TypeFilter);
            Assert.IsTrue(result.HasWarnings);
        }

        [Test]
        public void Validate_NegativeQuestionsCount_ClampsToDefault()
        {
            var result = RoomSettingsValidator.Validate(new RoomSettingsInput { QuestionsCount = -5 });

            Assert.AreEqual(QuestionSelectionSettings.DefaultCount, result.Settings.Questions.Count);
            Assert.IsTrue(result.HasWarnings);
        }

        [Test]
        public void Validate_BuzzCollectWindowMsAboveMax_ClampsAndWarns()
        {
            var result = RoomSettingsValidator.Validate(new RoomSettingsInput { BuzzCollectWindowMs = 5000 });

            Assert.AreEqual(500, RoomSettingsValidator.MaxCollectWindowMs, "上限は 500ms（docs/network.md §6.3）。");
            Assert.AreEqual(0.5, result.Settings.TimeLimits.CollectWindowSec, 1e-9);
            Assert.IsTrue(result.HasWarnings);
        }

        [Test]
        public void Validate_BuzzCollectWindowMsBelowMin_ClampsAndWarns()
        {
            var result = RoomSettingsValidator.Validate(new RoomSettingsInput { BuzzCollectWindowMs = 10 });

            Assert.AreEqual(50, RoomSettingsValidator.MinCollectWindowMs, "下限は 50ms（docs/network.md §6.3）。");
            Assert.AreEqual(0.05, result.Settings.TimeLimits.CollectWindowSec, 1e-9);
            Assert.IsTrue(result.HasWarnings);
        }

        [Test]
        public void Validate_UnknownPenaltyType_FallsBackToSkipNextWithWarning()
        {
            var result = RoomSettingsValidator.Validate(new RoomSettingsInput { ScorePenaltyType = "unknown" });

            Assert.AreEqual(PenaltyKind.SkipNext, result.Settings.Scoring.PenaltyType);
            Assert.IsTrue(result.HasWarnings);
        }

        [Test]
        public void Validate_TtsSpeedOutOfRange_ClampsAndWarns()
        {
            var tooHigh = RoomSettingsValidator.Validate(new RoomSettingsInput { TtsSpeed = 3.0 });
            var tooLow = RoomSettingsValidator.Validate(new RoomSettingsInput { TtsSpeed = 0.1 });

            Assert.AreEqual(RoomSettings.MaxTtsSpeed, tooHigh.Settings.TtsSpeed);
            Assert.AreEqual(RoomSettings.MinTtsSpeed, tooLow.Settings.TtsSpeed);
            Assert.IsTrue(tooHigh.HasWarnings);
            Assert.IsTrue(tooLow.HasWarnings);
        }

        [Test]
        public void Validate_ResultAutoAdvanceSecOutOfRange_ClampsAndWarns()
        {
            var result = RoomSettingsValidator.Validate(new RoomSettingsInput { ResultAutoAdvanceSec = -1.0 });

            Assert.AreEqual(SessionSettings.ManualAdvance, result.Settings.ResultAutoAdvanceSec);
            Assert.IsTrue(result.HasWarnings);
        }

        [Test]
        public void Validate_NonFiniteDouble_FallsBackToDefaultWithWarning()
        {
            var result = RoomSettingsValidator.Validate(new RoomSettingsInput { TtsLeadTimeSec = double.NaN });

            Assert.AreEqual(RoomSettings.DefaultTtsLeadTimeSec, result.Settings.TtsLeadTimeSec);
            Assert.IsTrue(result.HasWarnings);
        }

        [Test]
        public void Validate_ScoreCorrectPointsOutOfRange_ClampsAndWarns()
        {
            var tooHigh = RoomSettingsValidator.Validate(new RoomSettingsInput { ScoreCorrectPoints = 999 });
            var tooLow = RoomSettingsValidator.Validate(new RoomSettingsInput { ScoreIncorrectPoints = -999 });

            Assert.AreEqual(ScoreRules.MaxCorrectPoints, tooHigh.Settings.Scoring.CorrectPoints);
            Assert.AreEqual(ScoreRules.MinWrongPoints, tooLow.Settings.Scoring.IncorrectPoints);
            Assert.IsTrue(tooHigh.HasWarnings);
            Assert.IsTrue(tooLow.HasWarnings);
        }

        [Test]
        public void Validate_ScorePenaltyMinusPointsOutOfRange_ClampsAndWarns()
        {
            var result = RoomSettingsValidator.Validate(new RoomSettingsInput { ScorePenaltyMinusPoints = 10 });

            Assert.AreEqual(ScoreRules.MaxPenaltyPoints, result.Settings.Scoring.PenaltyMinusPoints);
            Assert.IsTrue(result.HasWarnings);
        }

        [Test]
        public void Validate_ChoiceTimeLimitSecOutOfRange_ClampsAndWarns()
        {
            var tooHigh = RoomSettingsValidator.Validate(new RoomSettingsInput { AnswerChoiceTimeLimitSec = 90.0 });
            var tooLow = RoomSettingsValidator.Validate(new RoomSettingsInput { AnswerChoiceTimeLimitSec = 0.5 });

            Assert.AreEqual(RoomSettings.MaxChoiceTimeLimitSec, tooHigh.Settings.ChoiceTimeLimitSec);
            Assert.AreEqual(RoomSettings.MinChoiceTimeLimitSec, tooLow.Settings.ChoiceTimeLimitSec);
            Assert.AreEqual(
                RoomSettings.MaxChoiceTimeLimitSec, tooHigh.Settings.TimeLimits.ChoiceTimeLimitSec,
                "answer.choiceTimeLimitSec は QuizTimeLimits.ChoiceTimeLimitSec に反映される（#26/#79 整合）。");
            Assert.AreEqual(RoomSettings.MinChoiceTimeLimitSec, tooLow.Settings.TimeLimits.ChoiceTimeLimitSec);
            Assert.IsTrue(tooHigh.HasWarnings);
            Assert.IsTrue(tooLow.HasWarnings);
        }

        [Test]
        public void Validate_TtsReadyTimeoutMsOutOfRange_ClampsAndWarns()
        {
            var tooHigh = RoomSettingsValidator.Validate(new RoomSettingsInput { TtsReadyTimeoutMs = 99999 });
            var tooLow = RoomSettingsValidator.Validate(new RoomSettingsInput { TtsReadyTimeoutMs = -1 });

            Assert.AreEqual(RoomSettings.MaxTtsReadyTimeoutMs, tooHigh.Settings.TtsReadyTimeoutMs);
            Assert.AreEqual(RoomSettings.MinTtsReadyTimeoutMs, tooLow.Settings.TtsReadyTimeoutMs);
            Assert.IsTrue(tooHigh.HasWarnings);
            Assert.IsTrue(tooLow.HasWarnings);
        }

        [TestCase(-10, 0)]
        [TestCase(900, 500)]
        public void Validate_QuestionRevealMsPerCharOutOfRange_ClampsAndWarns(int raw, int expected)
        {
            var result = RoomSettingsValidator.Validate(new RoomSettingsInput { QuestionRevealMsPerChar = raw });

            Assert.AreEqual(expected, result.Settings.QuestionRevealMsPerChar);
            Assert.IsTrue(result.HasWarnings);
        }

        [Test]
        public void Validate_QuestionRevealMsPerCharMissing_UsesDefault()
        {
            var result = RoomSettingsValidator.Validate(new RoomSettingsInput());

            Assert.AreEqual(RoomSettings.DefaultQuestionRevealMsPerChar, result.Settings.QuestionRevealMsPerChar);
        }

        [Test]
        public void Validate_ShowScores_MissingUsesDefaultAndFalseIsKept()
        {
            // issue #194: display.showScores は bool。未指定なら既定（表示する）。
            Assert.IsTrue(RoomSettingsValidator.Validate(new RoomSettingsInput()).Settings.ShowScores);

            var result = RoomSettingsValidator.Validate(new RoomSettingsInput { DisplayShowScores = false });

            Assert.IsFalse(result.Settings.ShowScores);
            Assert.IsFalse(result.HasWarnings);
        }

        [Test]
        public void Validate_TtsLeadTimeSecOutOfRange_ClampsAndWarns()
        {
            var result = RoomSettingsValidator.Validate(new RoomSettingsInput { TtsLeadTimeSec = 5.0 });

            Assert.AreEqual(RoomSettings.MaxTtsLeadTimeSec, result.Settings.TtsLeadTimeSec);
            Assert.IsTrue(result.HasWarnings);
        }

        [Test]
        public void Validate_SetIdsAndTagFilter_RoundTripThroughQuestionsSettings()
        {
            var result = RoomSettingsValidator.Validate(new RoomSettingsInput
            {
                QuestionsSetIds = new System.Collections.Generic.List<string> { "set-a", "set-b" },
                QuestionsTagFilter = new System.Collections.Generic.List<string> { "地理", "歴史" },
            });

            CollectionAssert.AreEqual(new[] { "set-a", "set-b" }, result.Settings.Questions.SetIds);
            CollectionAssert.AreEqual(new[] { "地理", "歴史" }, result.Settings.Questions.TagFilter);
            Assert.IsFalse(result.HasWarnings);
        }

        [Test]
        public void Validate_ValidValues_NoWarnings()
        {
            var input = new RoomSettingsInput
            {
                HostRole = "moderator",
                MaxPlayers = 8,
                QuestionsTypeFilter = "freeText",
                BuzzTimeLimitSec = 12.0,
                BuzzCollectWindowMs = 200,
                ScorePenaltyType = "minusPoints",
                ScorePenaltyMinusPoints = -10,
                TtsSpeed = 1.5,
                ResultAutoAdvanceSec = 4.0,
            };

            var result = RoomSettingsValidator.Validate(input);

            Assert.IsFalse(result.HasWarnings);
            Assert.AreEqual(8, result.Settings.MaxPlayers);
            Assert.AreEqual(12.0, result.Settings.TimeLimits.BuzzTimeLimitSec, 1e-9);
            Assert.AreEqual(0.2, result.Settings.TimeLimits.CollectWindowSec, 1e-9);
            Assert.AreEqual(PenaltyKind.MinusPoints, result.Settings.Scoring.PenaltyType);
            Assert.AreEqual(-10, result.Settings.Scoring.PenaltyMinusPoints);
            Assert.AreEqual(1.5, result.Settings.TtsSpeed, 1e-9);
        }
    }
}
