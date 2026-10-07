using System;
using NUnit.Framework;
using TsumugiQuiz.Tts;

namespace TsumugiQuiz.Tests.EditMode.Tts
{
    /// <summary>
    /// ネットワーク時刻 → <c>dspTime</c> の変換と再生予約の決定（<see cref="PlaybackScheduler"/>、#23）の
    /// ユニットテスト（docs/tts.md §6.1、docs/network.md §7.3）。
    /// </summary>
    public sealed class PlaybackSchedulerTests
    {
        private const double Tolerance = 1e-9;

        [Test]
        public void ToDspTime_ConvertsRemainingSecondsOntoDspTimeline()
        {
            // playAt まで 0.5 秒 → dsp でも 0.5 秒先。
            var target = PlaybackScheduler.ToDspTime(
                playAtServerTime: 100.5d, referenceServerTimeNow: 100.0d, dspTimeNow: 5.0d);

            Assert.AreEqual(5.5d, target, Tolerance);
        }

        [Test]
        public void ToDspTime_WithDifferentClocksOnEachPeer_PointsAtTheSameRemainingTime()
        {
            // ホストとクライアントは dspTime も時刻の基準も違うが、「あと何秒か」は一致する。
            const double playAt = 42.0d;

            var hostTarget = PlaybackScheduler.ToDspTime(playAt, referenceServerTimeNow: 41.7d, dspTimeNow: 1000.0d);
            var clientTarget = PlaybackScheduler.ToDspTime(playAt, referenceServerTimeNow: 41.7d, dspTimeNow: 3.25d);

            Assert.AreEqual(0.3d, hostTarget - 1000.0d, 1e-9);
            Assert.AreEqual(0.3d, clientTarget - 3.25d, 1e-9);
        }

        [Test]
        public void ToDspTime_WhenPlayAtIsAlreadyPast_ReturnsPastDspTime()
        {
            var target = PlaybackScheduler.ToDspTime(
                playAtServerTime: 99.8d, referenceServerTimeNow: 100.0d, dspTimeNow: 5.0d);

            Assert.AreEqual(4.8d, target, Tolerance);
        }

        [TestCase(double.NaN, 1.0d, 1.0d)]
        [TestCase(double.PositiveInfinity, 1.0d, 1.0d)]
        [TestCase(1.0d, double.NaN, 1.0d)]
        [TestCase(1.0d, 1.0d, double.NegativeInfinity)]
        public void ToDspTime_WithNonFiniteValues_Throws(double playAt, double reference, double dspNow)
        {
            Assert.Throws<ArgumentException>(() => PlaybackScheduler.ToDspTime(playAt, reference, dspNow));
        }

        [Test]
        public void ToDspTime_WithNegativeDspTime_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlaybackScheduler.ToDspTime(1.0d, 1.0d, dspTimeNow: -0.001d));
        }

        [Test]
        public void Create_WithFutureTarget_ReservesPlayback()
        {
            var schedule = PlaybackScheduler.Create(targetDspTime: 5.5d, dspTimeNow: 5.0d, durationSec: 2.0d);

            Assert.IsTrue(schedule.ShouldPlay);
            Assert.IsFalse(schedule.PlayImmediately, "間に合っているので予約する。");
            Assert.AreEqual(5.5d, schedule.DspStartTime, Tolerance);
            Assert.AreEqual(0.0d, schedule.StartOffsetSec, Tolerance);
            Assert.AreEqual(7.5d, schedule.DspEndTime, Tolerance);
            Assert.AreEqual(0.5d, schedule.LeadSec, Tolerance);
        }

        [Test]
        public void Create_WithPastTarget_PlaysImmediatelyFromOffset()
        {
            // 0.5 秒遅刻 → 頭を 0.5 秒飛ばして即再生し、完了時刻を全員と揃える。
            var schedule = PlaybackScheduler.Create(targetDspTime: 4.5d, dspTimeNow: 5.0d, durationSec: 2.0d);

            Assert.IsTrue(schedule.ShouldPlay);
            Assert.IsTrue(schedule.PlayImmediately);
            Assert.AreEqual(5.0d, schedule.DspStartTime, Tolerance);
            Assert.AreEqual(0.5d, schedule.StartOffsetSec, Tolerance);
            Assert.AreEqual(6.5d, schedule.DspEndTime, Tolerance, "完了時刻は遅刻しても同じ（4.5 + 2.0 相当）。");
            Assert.AreEqual(-0.5d, schedule.LeadSec, Tolerance);
        }

        [Test]
        public void Create_AtExactlyNow_PlaysImmediatelyWithoutOffset()
        {
            var schedule = PlaybackScheduler.Create(targetDspTime: 5.0d, dspTimeNow: 5.0d, durationSec: 2.0d);

            Assert.IsTrue(schedule.ShouldPlay);
            Assert.IsTrue(schedule.PlayImmediately, "境界（lead = 0）は即再生。");
            Assert.AreEqual(0.0d, schedule.StartOffsetSec, Tolerance);
            Assert.AreEqual(7.0d, schedule.DspEndTime, Tolerance);
        }

        [Test]
        public void Create_WhenWholeReadingHasPassed_DoesNotPlay()
        {
            var schedule = PlaybackScheduler.Create(targetDspTime: 2.0d, dspTimeNow: 5.0d, durationSec: 2.0d);

            Assert.IsFalse(schedule.ShouldPlay, "読み上げ時間が丸ごと過ぎていれば再生しない。");
            Assert.AreEqual(5.0d, schedule.DspEndTime, Tolerance, "すでに終わっているので完了時刻は「いま」。");
        }

        [Test]
        public void Create_WhenOffsetEqualsDuration_DoesNotPlay()
        {
            // 境界: 遅刻分がちょうど音声の長さと同じ。
            var schedule = PlaybackScheduler.Create(targetDspTime: 3.0d, dspTimeNow: 5.0d, durationSec: 2.0d);

            Assert.IsFalse(schedule.ShouldPlay);
        }

        [Test]
        public void Create_JustBeforeTheEnd_PlaysTheRemainder()
        {
            var schedule = PlaybackScheduler.Create(targetDspTime: 3.1d, dspTimeNow: 5.0d, durationSec: 2.0d);

            Assert.IsTrue(schedule.ShouldPlay);
            Assert.IsTrue(schedule.PlayImmediately);
            Assert.AreEqual(1.9d, schedule.StartOffsetSec, 1e-9);
            Assert.AreEqual(5.1d, schedule.DspEndTime, 1e-9, "残り 0.1 秒だけ鳴らす。");
        }

        [Test]
        public void Create_WithZeroDuration_DoesNotPlay()
        {
            var schedule = PlaybackScheduler.Create(targetDspTime: 6.0d, dspTimeNow: 5.0d, durationSec: 0d);

            Assert.IsFalse(schedule.ShouldPlay, "読み上げなし（長さ 0）のときは再生しない。");
            Assert.AreEqual(5.0d, schedule.DspEndTime, Tolerance);
        }

        [Test]
        public void Create_WithNegativeDuration_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlaybackScheduler.Create(targetDspTime: 6.0d, dspTimeNow: 5.0d, durationSec: -0.1d));
        }

        [TestCase(double.NaN, 1.0d, 1.0d)]
        [TestCase(1.0d, double.PositiveInfinity, 1.0d)]
        [TestCase(1.0d, 1.0d, double.NaN)]
        public void Create_WithNonFiniteValues_Throws(double target, double dspNow, double duration)
        {
            Assert.Throws<ArgumentException>(() => PlaybackScheduler.Create(target, dspNow, duration));
        }

        [Test]
        public void Create_WithNegativeDspTime_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlaybackScheduler.Create(targetDspTime: 1.0d, dspTimeNow: -1.0d, durationSec: 1.0d));
        }

        [Test]
        public void ToDspTimeThenCreate_OnTwoPeers_SchedulesTheSameRemainingTime()
        {
            // docs/tts.md §6 の受け入れ条件: 各ピアの dsp 予約が同じサーバー時刻を指すこと。
            const double playAt = 200.3d;
            const double durationSec = 1.5d;

            var hostTarget = PlaybackScheduler.ToDspTime(playAt, referenceServerTimeNow: 200.0d, dspTimeNow: 12.0d);
            var clientTarget = PlaybackScheduler.ToDspTime(playAt, referenceServerTimeNow: 200.0d, dspTimeNow: 987.5d);

            var hostSchedule = PlaybackScheduler.Create(hostTarget, dspTimeNow: 12.0d, durationSec: durationSec);
            var clientSchedule = PlaybackScheduler.Create(clientTarget, dspTimeNow: 987.5d, durationSec: durationSec);

            Assert.AreEqual(hostSchedule.LeadSec, clientSchedule.LeadSec, Tolerance);
            Assert.AreEqual(
                hostSchedule.DspStartTime - 12.0d,
                clientSchedule.DspStartTime - 987.5d,
                Tolerance,
                "dsp の絶対値は違っても、いまから再生までの秒数は一致する。");
        }
    }
}
