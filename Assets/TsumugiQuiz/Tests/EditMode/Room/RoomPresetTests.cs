using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.Tests.EditMode.Room
{
    /// <summary>
    /// <see cref="RoomPreset"/> / <see cref="RoomPresetJson"/>（#26）のテスト。
    /// 組み込み3プリセットの値は docs/room-settings.md §3 の表に基づく。
    /// </summary>
    public class RoomPresetTests
    {
        [Test]
        public void Standard_MatchesDefaults()
        {
            var preset = RoomPreset.Standard;

            Assert.AreEqual("標準", preset.Name);
            Assert.AreSame(RoomSettings.Default.Session, preset.Settings.Session);
            Assert.AreEqual(RoomSettings.Default.MaxPlayers, preset.Settings.MaxPlayers);
        }

        [Test]
        public void BuzzFocused_MatchesDocumentedValues()
        {
            var settings = RoomPreset.BuzzFocused.Settings;

            Assert.IsFalse(settings.AllowDuringReading);
            Assert.AreEqual(6.0, settings.TimeLimits.BuzzTimeLimitSec, 1e-9);
            Assert.AreEqual(8.0, settings.TimeLimits.AnswerTimeLimitSec, 1e-9);
            Assert.AreEqual(10.0, settings.ChoiceTimeLimitSec, 1e-9);
            Assert.AreEqual(
                10.0, settings.TimeLimits.ChoiceTimeLimitSec, 1e-9,
                "RoomSettings.ChoiceTimeLimitSec は TimeLimits.ChoiceTimeLimitSec に委譲している（#26/#79 整合）。");
            Assert.AreEqual(PenaltyKind.MinusPoints, settings.Scoring.PenaltyType);
            Assert.AreEqual(-5, settings.Scoring.PenaltyMinusPoints);
            Assert.AreEqual(3.0, settings.ResultAutoAdvanceSec, 1e-9);
        }

        [Test]
        public void Relaxed_MatchesDocumentedValues()
        {
            var settings = RoomPreset.Relaxed.Settings;

            Assert.AreEqual(20.0, settings.TimeLimits.BuzzTimeLimitSec, 1e-9);
            Assert.AreEqual(30.0, settings.TimeLimits.AnswerTimeLimitSec, 1e-9);
            Assert.AreEqual(40.0, settings.ChoiceTimeLimitSec, 1e-9);
            Assert.AreEqual(
                40.0, settings.TimeLimits.ChoiceTimeLimitSec, 1e-9,
                "RoomSettings.ChoiceTimeLimitSec は TimeLimits.ChoiceTimeLimitSec に委譲している（#26/#79 整合）。");
            Assert.AreEqual(0, settings.Scoring.IncorrectPoints);
            Assert.AreEqual(PenaltyKind.None, settings.Scoring.PenaltyType);
            Assert.IsTrue(settings.Scoring.ReopenAfterWrongAnswer);
            Assert.AreEqual(8.0, settings.ResultAutoAdvanceSec, 1e-9);
        }

        [Test]
        public void BuiltIns_ContainsExactlyThreePresetsInOrder()
        {
            var builtIns = RoomPreset.BuiltIns;

            Assert.AreEqual(3, builtIns.Count);
            Assert.AreEqual("標準", builtIns[0].Name);
            Assert.AreEqual("早押し重視", builtIns[1].Name);
            Assert.AreEqual("のんびり", builtIns[2].Name);
        }

        [Test]
        public void Json_RoundTrip_PreservesValues()
        {
            var original = RoomPreset.BuzzFocused;

            var json = RoomPresetJson.Serialize(original);
            var result = RoomPresetJson.Parse(json, "fallback");

            Assert.IsTrue(result.Found);
            Assert.IsFalse(result.HasWarnings);
            Assert.AreEqual(original.Name, result.Preset.Name);
            Assert.AreEqual(original.SchemaVersion, result.Preset.SchemaVersion);
            Assert.AreEqual(original.Settings.AllowDuringReading, result.Preset.Settings.AllowDuringReading);
            Assert.AreEqual(original.Settings.TimeLimits.BuzzTimeLimitSec, result.Preset.Settings.TimeLimits.BuzzTimeLimitSec, 1e-9);
            Assert.AreEqual(original.Settings.Scoring.PenaltyType, result.Preset.Settings.Scoring.PenaltyType);
            Assert.AreEqual(original.Settings.Scoring.PenaltyMinusPoints, result.Preset.Settings.Scoring.PenaltyMinusPoints);
            Assert.AreEqual(original.Settings.ResultAutoAdvanceSec, result.Preset.Settings.ResultAutoAdvanceSec, 1e-9);
        }

        [Test]
        public void Json_RoundTrip_PreservesQuestionRevealMsPerChar()
        {
            // issue #144: question.revealMsPerChar もプリセット・room.lastApplied に保存される。
            var original = new RoomPreset("文字送り", RoomSettings.Default.WithQuestionRevealMsPerChar(150));

            var json = RoomPresetJson.Serialize(original);
            var result = RoomPresetJson.Parse(json, "fallback");

            StringAssert.Contains("\"question.revealMsPerChar\"", json);
            Assert.IsTrue(result.Found);
            Assert.IsFalse(result.HasWarnings, "既知のキーなので未知キーの警告は出ない。");
            Assert.AreEqual(150, result.Preset.Settings.QuestionRevealMsPerChar);
        }

        [Test]
        public void Json_RoundTrip_PreservesShowScores()
        {
            // issue #194: display.showScores もプリセット・room.lastApplied に保存される。
            var original = new RoomPreset("得点を隠す", RoomSettings.Default.WithShowScores(false));

            var json = RoomPresetJson.Serialize(original);
            var result = RoomPresetJson.Parse(json, "fallback");

            StringAssert.Contains("\"display.showScores\"", json);
            Assert.IsTrue(result.Found);
            Assert.IsFalse(result.HasWarnings, "既知のキーなので未知キーの警告は出ない。");
            Assert.IsFalse(result.Preset.Settings.ShowScores);
        }

        [Test]
        public void BuiltIns_ShowScoresByDefault()
        {
            foreach (var preset in RoomPreset.BuiltIns)
            {
                Assert.IsTrue(preset.Settings.ShowScores, $"組み込みプリセット「{preset.Name}」は既定値（表示する）のまま。");
            }
        }

        /// <summary>#221: 設定画面から外した answer.singleAttemptOnly も、JSON の読み込みは従来どおり受け付ける。</summary>
        [Test]
        public void Json_SingleAttemptOnlyKey_IsStillAcceptedWithoutWarning()
        {
            const string json = "{ \"schemaVersion\": 1, \"name\": \"互換\", \"settings\": { \"answer.singleAttemptOnly\": false } }";

            var result = RoomPresetJson.Parse(json, "fallback");

            Assert.IsTrue(result.Found);
            Assert.IsFalse(result.HasWarnings, "既知のキーなので警告は出ない。");
            Assert.IsFalse(result.Preset.Settings.Scoring.SingleAttemptOnly);
        }

        [Test]
        public void Json_PartialSettings_FillsRemainingWithDefaults()
        {
            const string json = "{ \"schemaVersion\": 1, \"name\": \"カスタム\", \"settings\": { \"room.maxPlayers\": 4 } }";

            var result = RoomPresetJson.Parse(json, "fallback");

            Assert.IsTrue(result.Found);
            Assert.IsFalse(result.HasWarnings);
            Assert.AreEqual("カスタム", result.Preset.Name);
            Assert.AreEqual(4, result.Preset.Settings.MaxPlayers);
            Assert.AreEqual(RoomSettings.DefaultTtsSpeed, result.Preset.Settings.TtsSpeed, 1e-9, "未指定のキーは既定値。");
        }

        [Test]
        public void Json_CorruptContent_FallsBackToDefaultsWithWarning()
        {
            const string corruptJson = "{ this is not valid json ";

            var result = RoomPresetJson.Parse(corruptJson, "壊れたプリセット");

            Assert.IsTrue(result.Found, "ファイル自体は読めているので Found は true。");
            Assert.IsTrue(result.HasWarnings);
            Assert.AreEqual("壊れたプリセット", result.Preset.Name);
            Assert.AreEqual(RoomSettings.Default.MaxPlayers, result.Preset.Settings.MaxPlayers);
        }

        [Test]
        public void Json_EmptyContent_FallsBackToDefaultsWithWarning()
        {
            var result = RoomPresetJson.Parse(string.Empty, "空プリセット");

            Assert.IsTrue(result.HasWarnings);
            Assert.AreEqual("空プリセット", result.Preset.Name);
        }

        [Test]
        public void Json_SetIdsAndTagFilter_RoundTrip()
        {
            var original = new RoomPreset(
                "セット指定",
                RoomSettings.Default.WithSession(RoomSettings.Default.Session.WithQuestions(
                    QuestionSelectionSettings.Default
                        .WithSetIds(new[] { "set-a", "set-b" })
                        .WithTagFilter(new[] { "地理", "歴史" }))));

            var json = RoomPresetJson.Serialize(original);
            var result = RoomPresetJson.Parse(json, "fallback");

            Assert.IsFalse(result.HasWarnings);
            CollectionAssert.AreEqual(new[] { "set-a", "set-b" }, result.Preset.Settings.Questions.SetIds);
            CollectionAssert.AreEqual(new[] { "地理", "歴史" }, result.Preset.Settings.Questions.TagFilter);
        }

        [Test]
        public void Json_UnknownKey_IsIgnoredWithWarning()
        {
            const string json =
                "{ \"schemaVersion\": 1, \"name\": \"test\", \"settings\": { \"room.maxPlayers\": 4, \"unknown.key\": 1 } }";

            var result = RoomPresetJson.Parse(json, "fallback");

            Assert.IsTrue(result.HasWarnings);
            Assert.AreEqual(4, result.Preset.Settings.MaxPlayers);
            StringAssert.Contains("unknown.key", string.Join(" / ", result.Warnings));
        }

        [Test]
        public void Json_SchemaVersionMismatch_FallsBackToDefaultsWithWarning()
        {
            const string json =
                "{ \"schemaVersion\": 2, \"name\": \"未来の設定\", \"settings\": { \"room.maxPlayers\": 12 } }";

            var result = RoomPresetJson.Parse(json, "fallback");

            Assert.IsTrue(result.Found);
            Assert.IsTrue(result.HasWarnings);
            Assert.AreEqual("未来の設定", result.Preset.Name);
            Assert.AreEqual(RoomSettings.Default.MaxPlayers, result.Preset.Settings.MaxPlayers, "settings の中身は信用せず既定値。");
        }

        [Test]
        public void Json_MissingSchemaVersion_FallsBackToDefaultsWithWarning()
        {
            const string json = "{ \"name\": \"バージョン無し\", \"settings\": { \"room.maxPlayers\": 12 } }";

            var result = RoomPresetJson.Parse(json, "fallback");

            Assert.IsTrue(result.HasWarnings);
            Assert.AreEqual(RoomSettings.Default.MaxPlayers, result.Preset.Settings.MaxPlayers);
        }

        [Test]
        public void Serialize_AlwaysWritesCurrentSchemaVersion()
        {
            var preset = new RoomPreset("バージョン強制", RoomSettings.Default, schemaVersion: 999);

            var json = RoomPresetJson.Serialize(preset);

            StringAssert.Contains($"\"schemaVersion\": {RoomPreset.CurrentSchemaVersion}", json);
        }

        [Test]
        public void Json_TypeMismatch_RecoversOnlyThatKey()
        {
            const string json =
                "{ \"schemaVersion\": 1, \"name\": \"型不一致\", \"settings\": { " +
                "\"room.maxPlayers\": \"not-a-number\", \"tts.speed\": 1.5 } }";

            var result = RoomPresetJson.Parse(json, "fallback");

            Assert.IsTrue(result.HasWarnings);
            Assert.AreEqual(RoomSettings.DefaultMaxPlayers, result.Preset.Settings.MaxPlayers, "型不一致のキーだけ既定値。");
            Assert.AreEqual(1.5, result.Preset.Settings.TtsSpeed, 1e-9, "他の正しいキーは反映される。");
        }

        [Test]
        public void Json_MaxPlayersExceedsIntRange_RecoversOnlyThatKey()
        {
            // int の範囲を超える整数（long には収まる）。JsonInputReader.GetInt が例外を投げず
            // そのキーだけ既定値+警告にすること（#26 統括判断 H1）。他の正しいキーは反映されたままであること。
            const string json =
                "{ \"schemaVersion\": 1, \"name\": \"巨大な人数\", \"settings\": { " +
                "\"room.maxPlayers\": 99999999999, \"tts.speed\": 1.5 } }";

            RoomPresetParseResult result = null;
            Assert.DoesNotThrow(() => result = RoomPresetJson.Parse(json, "fallback"));

            Assert.IsTrue(result.HasWarnings);
            Assert.AreEqual(RoomSettings.DefaultMaxPlayers, result.Preset.Settings.MaxPlayers);
            Assert.AreEqual(1.5, result.Preset.Settings.TtsSpeed, 1e-9, "他の正しいキーは反映される。");
        }
    }
}
