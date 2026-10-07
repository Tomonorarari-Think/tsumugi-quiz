using System;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;
using Unity.Collections;
using Unity.Netcode;

namespace TsumugiQuiz.Tests.EditMode.Network
{
    /// <summary>
    /// ルーム設定の同期ペイロード（<see cref="RoomSettingsPayload"/>、issue #27）の
    /// 往復・サイズ・受信時の再検証を確かめる。
    /// </summary>
    public sealed class RoomSettingsPayloadTests
    {
        /// <summary>既定値以外を一通り入れたルーム設定（往復の検証用）。</summary>
        private static RoomSettings BuildCustomSettings()
        {
            var questions = new QuestionSelectionSettings(
                setIds: new List<string> { "set-a", "set-b" },
                typeFilter: QuestionTypeFilter.Choice,
                imageOnly: true,
                tagFilter: new List<string> { "歴史" },
                count: 7,
                shuffleOrder: false);

            var timeLimits = new QuizTimeLimits(
                buzzTimeLimitSec: 6.0,
                answerTimeLimitSec: 8.0,
                choiceTimeLimitSec: 12.0,
                collectWindowSec: 0.25);

            var scoring = new ScoringSettings(
                correctPoints: 20,
                incorrectPoints: -3,
                penaltyType: PenaltyKind.MinusPoints,
                penaltyMinusPoints: -7,
                reopenAfterWrongAnswer: false,
                singleAttemptOnly: false);

            var session = new SessionSettings(questions, timeLimits, scoring, resultAutoAdvanceSec: 3.0, allowLateJoin: true);

            return new RoomSettings(
                hostRole: HostRole.Moderator,
                maxPlayers: 11,
                session: session,
                shuffleChoiceDisplay: false,
                allowDuringReading: false,
                ttsEnabled: false,
                ttsSpeed: 1.75,
                ttsReadyTimeoutMs: 4200,
                ttsLeadTimeSec: 0.9,
                questionRevealMsPerChar: 140,
                showScores: false);
        }

        private static RoomSettings RoundTrip(RoomSettings settings)
        {
            var payload = RoomSettingsPayload.FromRoomSettings(settings);
            var bytes = Serialize(payload);
            var restored = Deserialize(bytes);

            Assert.IsTrue(
                restored.TryToRoomSettingsInput(out var input, out var unknownKeys),
                "往復で未知の列挙値が生じてはいけない: " + string.Join(" / ", unknownKeys));

            return RoomSettingsValidator.Validate(input).Settings;
        }

        private static byte[] Serialize(RoomSettingsPayload payload)
        {
            using var writer = new FastBufferWriter(1024, Allocator.Temp);
            writer.WriteNetworkSerializable(payload);
            return writer.ToArray();
        }

        private static RoomSettingsPayload Deserialize(byte[] bytes)
        {
            using var reader = new FastBufferReader(bytes, Allocator.Temp);
            reader.ReadNetworkSerializable(out RoomSettingsPayload payload);
            return payload;
        }

        [Test]
        public void RoundTrip_DefaultSettings_PreservesValues()
        {
            var restored = RoundTrip(RoomSettings.Default);

            Assert.AreEqual(RoomSettings.DefaultHostRole, restored.HostRole);
            Assert.AreEqual(RoomSettings.DefaultMaxPlayers, restored.MaxPlayers);
            Assert.AreEqual(QuizTimeLimits.DefaultBuzzTimeLimitSec, restored.TimeLimits.BuzzTimeLimitSec);
            Assert.AreEqual(QuizTimeLimits.DefaultAnswerTimeLimitSec, restored.TimeLimits.AnswerTimeLimitSec);
            Assert.AreEqual(QuizTimeLimits.DefaultChoiceTimeLimitSec, restored.TimeLimits.ChoiceTimeLimitSec);
            Assert.AreEqual(QuizTimeLimits.DefaultCollectWindowSec, restored.TimeLimits.CollectWindowSec, 1e-9);
            Assert.AreEqual(RoomSettings.DefaultTtsSpeed, restored.TtsSpeed);
            Assert.AreEqual(RoomSettings.DefaultTtsReadyTimeoutMs, restored.TtsReadyTimeoutMs);
            Assert.AreEqual(RoomSettings.DefaultTtsLeadTimeSec, restored.TtsLeadTimeSec);
            Assert.AreEqual(RoomSettings.DefaultQuestionRevealMsPerChar, restored.QuestionRevealMsPerChar);
        }

        [Test]
        public void RoundTrip_CustomSettings_PreservesValues()
        {
            var original = BuildCustomSettings();
            var restored = RoundTrip(original);

            Assert.AreEqual(original.HostRole, restored.HostRole);
            Assert.AreEqual(original.MaxPlayers, restored.MaxPlayers);
            Assert.AreEqual(original.ShuffleChoiceDisplay, restored.ShuffleChoiceDisplay);
            Assert.AreEqual(original.AllowDuringReading, restored.AllowDuringReading);
            Assert.AreEqual(original.AllowLateJoin, restored.AllowLateJoin);
            Assert.AreEqual(original.ResultAutoAdvanceSec, restored.ResultAutoAdvanceSec);

            Assert.AreEqual(original.TimeLimits.BuzzTimeLimitSec, restored.TimeLimits.BuzzTimeLimitSec);
            Assert.AreEqual(original.TimeLimits.AnswerTimeLimitSec, restored.TimeLimits.AnswerTimeLimitSec);
            Assert.AreEqual(original.TimeLimits.ChoiceTimeLimitSec, restored.TimeLimits.ChoiceTimeLimitSec);
            Assert.AreEqual(original.TimeLimits.CollectWindowSec, restored.TimeLimits.CollectWindowSec, 1e-9);

            Assert.AreEqual(original.Scoring.CorrectPoints, restored.Scoring.CorrectPoints);
            Assert.AreEqual(original.Scoring.IncorrectPoints, restored.Scoring.IncorrectPoints);
            Assert.AreEqual(original.Scoring.PenaltyType, restored.Scoring.PenaltyType);
            Assert.AreEqual(original.Scoring.PenaltyMinusPoints, restored.Scoring.PenaltyMinusPoints);
            Assert.AreEqual(original.Scoring.ReopenAfterWrongAnswer, restored.Scoring.ReopenAfterWrongAnswer);
            Assert.AreEqual(original.Scoring.SingleAttemptOnly, restored.Scoring.SingleAttemptOnly);

            Assert.AreEqual(original.Questions.TypeFilter, restored.Questions.TypeFilter);
            Assert.AreEqual(original.Questions.ImageOnly, restored.Questions.ImageOnly);
            Assert.AreEqual(original.Questions.Count, restored.Questions.Count);
            Assert.AreEqual(original.Questions.ShuffleOrder, restored.Questions.ShuffleOrder);

            Assert.AreEqual(original.TtsEnabled, restored.TtsEnabled);
            Assert.AreEqual(original.TtsSpeed, restored.TtsSpeed);
            Assert.AreEqual(original.TtsReadyTimeoutMs, restored.TtsReadyTimeoutMs);
            Assert.AreEqual(original.TtsLeadTimeSec, restored.TtsLeadTimeSec);
            Assert.AreEqual(original.QuestionRevealMsPerChar, restored.QuestionRevealMsPerChar, "question.revealMsPerChar（#144）も同期される。");
            Assert.AreEqual(original.ShowScores, restored.ShowScores, "display.showScores（#194）も同期される。");
        }

        [Test]
        public void RoundTrip_SetIdsAndTagFilter_NotSynced()
        {
            var original = BuildCustomSettings();
            var restored = RoundTrip(original);

            // 可変長の文字列配列は unmanaged な構造体に入らないため、意図的に同期しない
            // （docs/network.md §12、RoomSettingsPayload の remarks）。
            Assert.AreEqual(2, original.Questions.SetIds.Count);
            Assert.AreEqual(1, original.Questions.TagFilter.Count);
            Assert.AreEqual(0, restored.Questions.SetIds.Count);
            Assert.AreEqual(0, restored.Questions.TagFilter.Count);
        }

        [Test]
        public void Serialize_Size_FitsInMaxPayloadSize()
        {
            var bytes = Serialize(RoomSettingsPayload.FromRoomSettings(BuildCustomSettings()));

            Assert.Less(
                bytes.Length,
                NetworkConstants.MaxPayloadSizeBytes,
                $"RoomSettingsPayload が MaxPayloadSize を超えています（{bytes.Length} バイト）。");

            // 目安として 256 バイト未満に収まっていること（項目追加で急に膨らんだら気づけるように）。
            TestContext.WriteLine($"RoomSettingsPayload の実測サイズ: {bytes.Length} バイト。");
            Assert.Less(bytes.Length, 256, $"RoomSettingsPayload の実測サイズ: {bytes.Length} バイト。");
        }

        [Test]
        public void Validate_OutOfRangePayload_ClampsAndWarns()
        {
            var payload = RoomSettingsPayload.FromRoomSettings(RoomSettings.Default);
            payload.MaxPlayers = 999;
            payload.BuzzTimeLimitSec = 0.0;
            payload.TtsSpeed = 9.5;
            payload.TtsReadyTimeoutMs = -1;
            payload.CollectWindowMs = 10000;

            Assert.IsTrue(payload.TryToRoomSettingsInput(out var input, out _));
            var result = RoomSettingsValidator.Validate(input);

            Assert.AreEqual(RoomSettings.MaxMaxPlayers, result.Settings.MaxPlayers);
            Assert.AreEqual(QuizTimeLimits.MinBuzzOrAnswerTimeLimitSec, result.Settings.TimeLimits.BuzzTimeLimitSec);
            Assert.AreEqual(RoomSettings.MaxTtsSpeed, result.Settings.TtsSpeed);
            Assert.AreEqual(RoomSettings.MinTtsReadyTimeoutMs, result.Settings.TtsReadyTimeoutMs);
            Assert.AreEqual(QuizTimeLimits.MaxCollectWindowSec, result.Settings.TimeLimits.CollectWindowSec, 1e-9);
            Assert.AreEqual(5, result.Warnings.Count, string.Join(" / ", result.Warnings));
        }

        [Test]
        public void TryToRoomSettingsInput_UnknownEnumBytes_ReportsKeysAndFallsBackToDefaults()
        {
            var payload = RoomSettingsPayload.FromRoomSettings(BuildCustomSettings());
            payload.HostRole = 200;
            payload.QuestionTypeFilter = 200;
            payload.PenaltyType = 200;

            Assert.IsFalse(
                payload.TryToRoomSettingsInput(out var input, out var unknownKeys),
                "未知の列挙値は呼び出し元へ返すこと（無言で既定値にしない）。");

            CollectionAssert.AreEquivalent(
                new[] { HostRoles.SettingsKey, QuestionTypeFilters.SettingsKey, PenaltyKinds.SettingsKey },
                unknownKeys);

            var settings = RoomSettingsValidator.Validate(input).Settings;
            Assert.AreEqual(RoomSettings.DefaultHostRole, settings.HostRole);
            Assert.AreEqual(QuestionSelectionSettings.DefaultTypeFilter, settings.Questions.TypeFilter);
            Assert.AreEqual(ScoreRules.DefaultPenaltyKind, settings.Scoring.PenaltyType);
        }

        [Test]
        public void TryToRoomSettingsInput_KnownEnumBytes_ReportsNoUnknownKeys()
        {
            var payload = RoomSettingsPayload.FromRoomSettings(BuildCustomSettings());

            Assert.IsTrue(payload.TryToRoomSettingsInput(out _, out var unknownKeys));
            Assert.IsEmpty(unknownKeys);
        }

        [Test]
        public void TryToRoomSettingsInput_WithSetIdsAndTagFilter_KeepsThem()
        {
            // サーバーが自分のルーム設定を検証し直す経路（RoomSettingsSync.PublishServerSettings）。
            var original = BuildCustomSettings();
            var payload = RoomSettingsPayload.FromRoomSettings(original);

            Assert.IsTrue(
                payload.TryToRoomSettingsInput(
                    original.Questions.SetIds, original.Questions.TagFilter, out var input, out _));

            var settings = RoomSettingsValidator.Validate(input).Settings;
            CollectionAssert.AreEqual(original.Questions.SetIds, settings.Questions.SetIds);
            CollectionAssert.AreEqual(original.Questions.TagFilter, settings.Questions.TagFilter);
        }

        [Test]
        public void Equals_SameContent_ReturnsTrue()
        {
            var settings = BuildCustomSettings();
            var a = RoomSettingsPayload.FromRoomSettings(settings);
            var b = RoomSettingsPayload.FromRoomSettings(settings);

            Assert.IsTrue(a.Equals(b));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());

            b.MaxPlayers += 1;
            Assert.IsFalse(a.Equals(b));
        }

        [Test]
        public void FromRoomSettings_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => RoomSettingsPayload.FromRoomSettings(null));
        }
    }
}
