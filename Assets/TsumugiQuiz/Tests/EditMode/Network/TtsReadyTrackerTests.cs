using System;
using NUnit.Framework;
using TsumugiQuiz.Network;

namespace TsumugiQuiz.Tests.EditMode.Network
{
    /// <summary>
    /// TTS Ready 通知の集約（<see cref="TtsReadyTracker"/>、#23）のユニットテスト
    /// （docs/tts.md §6.2、docs/network.md §7.3）。
    /// </summary>
    public sealed class TtsReadyTrackerTests
    {
        private const ulong HostClientId = 0UL;
        private const ulong ClientA = 1UL;
        private const ulong ClientB = 2UL;
        private const double StartServerTime = 100.0d;
        private const double TimeoutSec = 3.0d;

        private TtsReadyTracker _tracker;

        [SetUp]
        public void SetUp() => _tracker = new TtsReadyTracker();

        [Test]
        public void NewTracker_IsNotAwaiting()
        {
            Assert.IsFalse(_tracker.IsAwaiting);
            Assert.IsFalse(_tracker.IsComplete, "待っていないラウンドは「完了」にしない。");
            Assert.AreEqual(TtsReadyTracker.NoQuestionIndex, _tracker.QuestionIndex);
        }

        [Test]
        public void Begin_RegistersEveryConnectedClient()
        {
            BeginRound();

            Assert.IsTrue(_tracker.IsAwaiting);
            Assert.AreEqual(0, _tracker.QuestionIndex);
            Assert.AreEqual(3, _tracker.PendingCount, "ホスト自身も Ready を返す（docs/tts.md §6.2）。");
            Assert.AreEqual(StartServerTime + TimeoutSec, _tracker.DeadlineServerTime, 1e-9);
        }

        [Test]
        public void TryReportReady_FromEveryClient_CompletesTheRound()
        {
            BeginRound();

            Assert.IsTrue(_tracker.TryReportReady(HostClientId, 0, 1.25d, out _));
            Assert.IsFalse(_tracker.IsComplete, "1 人でも残っていれば完了しない。");

            Assert.IsTrue(_tracker.TryReportReady(ClientA, 0, 1.25d, out _));
            Assert.IsTrue(_tracker.TryReportReady(ClientB, 0, 1.25d, out _));

            Assert.IsTrue(_tracker.IsComplete);
            Assert.AreEqual(0, _tracker.PendingCount);
        }

        [Test]
        public void TryReportReady_WhenNotAwaiting_IsRejected()
        {
            Assert.IsFalse(_tracker.TryReportReady(ClientA, 0, 1.0d, out var reason));
            Assert.IsNotNull(reason);
        }

        [Test]
        public void TryReportReady_ForAnotherQuestion_IsRejected()
        {
            BeginRound();

            Assert.IsFalse(_tracker.TryReportReady(ClientA, questionIndex: 1, durationSec: 1.0d, out var reason));
            Assert.IsNotNull(reason);
            Assert.AreEqual(3, _tracker.PendingCount, "棄却しても待ち対象は減らさない。");
        }

        [Test]
        public void TryReportReady_Twice_IsRejected()
        {
            BeginRound();
            Assert.IsTrue(_tracker.TryReportReady(ClientA, 0, 1.0d, out _));

            Assert.IsFalse(_tracker.TryReportReady(ClientA, 0, 9.0d, out var reason));
            Assert.IsNotNull(reason);

            Assert.IsTrue(_tracker.TryGetReportedDuration(ClientA, out var duration));
            Assert.AreEqual(1.0d, duration, 1e-9, "2 回目の値で上書きしない。");
        }

        [Test]
        public void TryReportReady_FromUnknownClient_IsRejected()
        {
            BeginRound();

            Assert.IsFalse(_tracker.TryReportReady(clientId: 99UL, questionIndex: 0, durationSec: 1.0d, out var reason));
            Assert.IsNotNull(reason);
            Assert.AreEqual(3, _tracker.PendingCount);
        }

        [TestCase(-0.001d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(TtsReadyTracker.MaxReportedDurationSec + 0.001d)]
        public void TryReportReady_WithOutOfRangeDuration_IsRejected(double durationSec)
        {
            BeginRound();

            Assert.IsFalse(_tracker.TryReportReady(ClientA, 0, durationSec, out var reason));
            Assert.IsNotNull(reason);
            Assert.AreEqual(3, _tracker.PendingCount);
        }

        [Test]
        public void TryReportReady_WithZeroDuration_IsAccepted()
        {
            BeginRound();

            Assert.IsTrue(
                _tracker.TryReportReady(ClientA, 0, 0d, out _),
                "読み上げを行わないクライアントは長さ 0 で即 Ready を返す。");
        }

        [Test]
        public void AuthoritativeDuration_UsesHostValue()
        {
            BeginRound();
            _tracker.TryReportReady(ClientA, 0, 1.75d, out _);
            _tracker.TryReportReady(HostClientId, 0, 1.50d, out _);
            _tracker.TryReportReady(ClientB, 0, 1.60d, out _);

            Assert.AreEqual(
                1.50d, _tracker.AuthoritativeDurationSec, 1e-9,
                "読み上げ時間はホストの合成結果を正とする（docs/tts.md §6.2）。");
        }

        [Test]
        public void AuthoritativeDuration_IsZero_WhenHostCannotSynthesize()
        {
            BeginRound();
            _tracker.TryReportReady(HostClientId, 0, 0d, out _);
            _tracker.TryReportReady(ClientA, 0, 1.25d, out _);
            _tracker.TryReportReady(ClientB, 0, 1.10d, out _);

            Assert.AreEqual(
                0d, _tracker.AuthoritativeDurationSec, 1e-9,
                "クライアントの報告値は信用しない（読み上げ完了時刻 = playAtServerTime になる）。");
        }

        [Test]
        public void AuthoritativeDuration_IgnoresClientValues_WhenHostHasNotReported()
        {
            BeginRound();
            _tracker.TryReportReady(ClientA, 0, 90d, out _);

            Assert.AreEqual(
                0d, _tracker.AuthoritativeDurationSec, 1e-9,
                "ホストが報告するまでは 0（クライアントに受付開始を遅らせられない）。");
        }

        [Test]
        public void AuthoritativeDuration_WithNoReports_IsZero()
        {
            BeginRound();

            Assert.AreEqual(0d, _tracker.AuthoritativeDurationSec, 1e-9);
        }

        [Test]
        public void HasTimedOut_AtTheDeadline_IsTrue()
        {
            BeginRound();

            Assert.IsFalse(_tracker.HasTimedOut(StartServerTime + TimeoutSec - 0.001d));
            Assert.IsTrue(_tracker.HasTimedOut(StartServerTime + TimeoutSec), "境界（期限ちょうど）で打ち切る。");
        }

        [Test]
        public void HasTimedOut_WhenNotAwaiting_IsFalse()
        {
            Assert.IsFalse(_tracker.HasTimedOut(double.MaxValue));
        }

        [Test]
        public void RemoveClient_OnDisconnect_LetsTheRoundComplete()
        {
            BeginRound();
            _tracker.TryReportReady(HostClientId, 0, 1.0d, out _);
            _tracker.TryReportReady(ClientA, 0, 1.0d, out _);

            Assert.IsTrue(_tracker.RemoveClient(ClientB), "切断したクライアントの Ready は待たない。");
            Assert.IsTrue(_tracker.IsComplete);
        }

        [Test]
        public void RemoveClient_ForUnknownClient_ReturnsFalse()
        {
            BeginRound();

            Assert.IsFalse(_tracker.RemoveClient(99UL));
        }

        [Test]
        public void Begin_Twice_DiscardsThePreviousRound()
        {
            BeginRound();
            _tracker.TryReportReady(ClientA, 0, 1.0d, out _);

            _tracker.Begin(1, new[] { HostClientId, ClientA }, HostClientId, StartServerTime + 10d, TimeoutSec);

            Assert.AreEqual(1, _tracker.QuestionIndex);
            Assert.AreEqual(2, _tracker.PendingCount);
            Assert.IsFalse(_tracker.TryGetReportedDuration(ClientA, out _), "前の問題の報告は持ち越さない。");
        }

        [Test]
        public void Reset_ClearsTheRound()
        {
            BeginRound();
            _tracker.Reset();

            Assert.IsFalse(_tracker.IsAwaiting);
            Assert.AreEqual(0, _tracker.PendingCount);
            Assert.AreEqual(TtsReadyTracker.NoQuestionIndex, _tracker.QuestionIndex);
        }

        [Test]
        public void Begin_WithInvalidArguments_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _tracker.Begin(-1, new[] { HostClientId }, HostClientId, StartServerTime, TimeoutSec));
            Assert.Throws<ArgumentNullException>(
                () => _tracker.Begin(0, null, HostClientId, StartServerTime, TimeoutSec));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _tracker.Begin(0, new[] { HostClientId }, HostClientId, StartServerTime, timeoutSec: -1d));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _tracker.Begin(0, new[] { HostClientId }, HostClientId, double.NaN, TimeoutSec));
        }

        private void BeginRound()
        {
            _tracker.Begin(
                questionIndex: 0,
                clientIds: new[] { HostClientId, ClientA, ClientB },
                hostClientId: HostClientId,
                startServerTime: StartServerTime,
                timeoutSec: TimeoutSec);
        }
    }
}
