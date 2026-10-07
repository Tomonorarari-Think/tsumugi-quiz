using System;
using NUnit.Framework;
using TsumugiQuiz.Core.Reveal;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="QuestionRevealSchedule"/>（問題文の文字送り表示、issue #144）のテスト。
    /// 按分・固定速度・停止・再開・全文表示・読み上げ待ちからの切り替えを確認する。
    /// </summary>
    public class QuestionRevealScheduleTests
    {
        private const double Tolerance = 1e-9;

        // --- 固定速度 ---

        [Test]
        public void FixedSpeed_FirstCharAppearsAtStart_ThenOnePerInterval()
        {
            var schedule = QuestionRevealSchedule.FixedSpeed(totalCount: 10, startTime: 5.0, msPerChar: 80);

            Assert.AreEqual(QuestionRevealMode.FixedSpeed, schedule.Mode);
            Assert.AreEqual(0, schedule.VisibleCount(4.999), "開始前は 1 文字も出さない。");
            Assert.AreEqual(1, schedule.VisibleCount(5.0), "開始時刻ちょうどに 1 文字目を出す。");
            Assert.AreEqual(1, schedule.VisibleCount(5.079));
            Assert.AreEqual(2, schedule.VisibleCount(5.08), "80ms 後に 2 文字目。丸め誤差で 1 文字少なくならない。");
            Assert.AreEqual(4, schedule.VisibleCount(5.24));
            Assert.AreEqual(10, schedule.VisibleCount(5.72), "10 文字目は 9 × 80ms 後。");
            Assert.AreEqual(10, schedule.VisibleCount(100.0), "文字数を超えない。");
        }

        [Test]
        public void FixedSpeed_ZeroMsPerChar_IsFull()
        {
            var schedule = QuestionRevealSchedule.FixedSpeed(10, 0.0, 0);

            Assert.AreEqual(QuestionRevealMode.Full, schedule.Mode, "0 ms/文字は一括表示。");
            Assert.AreEqual(10, schedule.VisibleCount(0.0));
        }

        [Test]
        public void FixedSpeed_EmptyText_IsFull()
        {
            var schedule = QuestionRevealSchedule.FixedSpeed(0, 0.0, 80);

            Assert.AreEqual(QuestionRevealMode.Full, schedule.Mode);
            Assert.AreEqual(0, schedule.VisibleCount(1.0));
            Assert.IsTrue(schedule.IsSettled(0.0));
        }

        [TestCase(-1)]
        [TestCase(QuestionRevealSchedule.MaxMsPerChar + 1)]
        public void FixedSpeed_OutOfRangeMsPerChar_Throws(int msPerChar)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => QuestionRevealSchedule.FixedSpeed(10, 0.0, msPerChar));
        }

        [Test]
        public void FixedSpeed_NonFiniteStart_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => QuestionRevealSchedule.FixedSpeed(10, double.NaN, 80));
        }

        [Test]
        public void Factories_NegativeTotal_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => QuestionRevealSchedule.Full(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => QuestionRevealSchedule.FixedSpeed(-1, 0.0, 80));
            Assert.Throws<ArgumentOutOfRangeException>(() => QuestionRevealSchedule.AwaitingReading(-1, 80));
        }

        // --- 読み上げとの同期（按分） ---

        [Test]
        public void WithReading_DistributesDurationOverCharacters()
        {
            // 10 文字を 2 秒で読む → 1 文字 0.2 秒。
            var schedule = QuestionRevealSchedule.AwaitingReading(10, fallbackMsPerChar: 80)
                .WithReading(readingStartTime: 1.0, durationSec: 2.0, now: 0.7);

            Assert.AreEqual(QuestionRevealMode.Synced, schedule.Mode);
            Assert.AreEqual(0.2, schedule.SecondsPerChar, Tolerance);
            Assert.AreEqual(0, schedule.VisibleCount(0.9), "再生開始前は出さない。");
            Assert.AreEqual(1, schedule.VisibleCount(1.0));
            Assert.AreEqual(5, schedule.VisibleCount(1.8), "0.8 秒経過 → 5 文字目。");
            Assert.AreEqual(10, schedule.VisibleCount(2.8), "最後の文字は 9 × 0.2 秒後。");
            Assert.AreEqual(10, schedule.VisibleCount(3.0), "読み上げ完了時点で全文。");
            Assert.IsTrue(schedule.IsSettled(3.0));
            Assert.IsFalse(schedule.IsSettled(2.0));
        }

        [Test]
        public void WithReading_StartInPast_CatchesUpImmediately()
        {
            // 合成が間に合わず頭を飛ばして再生した場合・View の復元（#144 レビュー H-1）と同じく、表示も時間軸に追いつく。
            var schedule = QuestionRevealSchedule.AwaitingReading(10, 80)
                .WithReading(readingStartTime: 1.0, durationSec: 1.0, now: 1.55);

            Assert.AreEqual(6, schedule.VisibleCount(1.55));
        }

        [Test]
        public void WithReading_NeverDecreasesVisibleCount()
        {
            // 固定速度で 5 文字出た後に（遅れて）読み上げの時刻が届いても、表示は減らない。
            var fixedSpeed = QuestionRevealSchedule.FixedSpeed(10, 0.0, 100);
            Assert.AreEqual(5, fixedSpeed.VisibleCount(0.45));

            var synced = fixedSpeed.WithReading(readingStartTime: 0.45, durationSec: 10.0, now: 0.45);

            Assert.AreEqual(QuestionRevealMode.Synced, synced.Mode);
            Assert.AreEqual(5, synced.FloorCount);
            Assert.AreEqual(5, synced.VisibleCount(0.45), "読み上げ側は 1 文字目だが、見えていた 5 文字を下限にする。");
            Assert.AreEqual(6, synced.VisibleCount(0.45 + 5.0), "読み上げが 6 文字目に達したら増える。");
        }

        [TestCase(0.0)]
        [TestCase(-1.0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void WithReading_UnusableDuration_FallsBackToFixedSpeedFromReadingStart(double durationSec)
        {
            // #144 レビュー L-1: 開始は「受け取った時刻」ではなく再生開始時刻（= 受付開始）にそろえる。
            var schedule = QuestionRevealSchedule.AwaitingReading(10, 80)
                .WithReading(readingStartTime: 2.3, durationSec: durationSec, now: 2.0);

            Assert.AreEqual(QuestionRevealMode.FixedSpeed, schedule.Mode);
            Assert.AreEqual(2.3, schedule.StartTime, Tolerance);
            Assert.AreEqual(0, schedule.VisibleCount(2.2), "再生開始時刻より前は出さない。");
            Assert.AreEqual(1, schedule.VisibleCount(2.3));
        }

        [Test]
        public void WithReading_OnFull_KeepsFull()
        {
            var full = QuestionRevealSchedule.Full(10);

            Assert.AreSame(full, full.WithReading(0.0, 2.0, 0.0));
        }

        [Test]
        public void IsUsableReadingDuration_OnlyPositiveFinite()
        {
            Assert.IsTrue(QuestionRevealSchedule.IsUsableReadingDuration(0.5));
            Assert.IsFalse(QuestionRevealSchedule.IsUsableReadingDuration(0.0));
            Assert.IsFalse(QuestionRevealSchedule.IsUsableReadingDuration(-0.1));
            Assert.IsFalse(QuestionRevealSchedule.IsUsableReadingDuration(double.NaN));
            Assert.IsFalse(QuestionRevealSchedule.IsUsableReadingDuration(double.PositiveInfinity));
        }

        // --- 読み上げ待ち ---

        [Test]
        public void AwaitingReading_ShowsNothingAndHasNoTimer()
        {
            // #144 レビュー M-1: 待ちの上限タイマーは持たない（時間が経っても自分からは固定速度へ切り替えない）。
            var awaiting = QuestionRevealSchedule.AwaitingReading(10, fallbackMsPerChar: 50);

            Assert.AreEqual(QuestionRevealMode.AwaitingReading, awaiting.Mode);
            Assert.AreEqual(0, awaiting.VisibleCount(0.0));
            Assert.AreEqual(0, awaiting.VisibleCount(1000.0));
            Assert.IsTrue(awaiting.IsSettled(1000.0), "時間では変わらない（次の変化はイベントで起きる）。");
        }

        [Test]
        public void FallBackToFixedSpeed_FromAwaiting_StartsAtGivenTime()
        {
            // 再生開始時刻が届かないまま受付が開いた（BuzzOpen）。
            var fallback = QuestionRevealSchedule.AwaitingReading(10, 50).FallBackToFixedSpeed(startTime: 4.0, now: 4.0);

            Assert.AreEqual(QuestionRevealMode.FixedSpeed, fallback.Mode);
            Assert.AreEqual(4.0, fallback.StartTime, Tolerance);
            Assert.AreEqual(0.05, fallback.SecondsPerChar, Tolerance);
            Assert.AreEqual(3, fallback.VisibleCount(4.1));
        }

        [Test]
        public void AwaitingReading_ZeroFallbackSpeed_IsFull()
        {
            Assert.AreEqual(QuestionRevealMode.Full, QuestionRevealSchedule.AwaitingReading(10, 0).Mode);
        }

        [Test]
        public void FallBackToFixedSpeed_OnlyAffectsAwaiting()
        {
            var fixedSpeed = QuestionRevealSchedule.FixedSpeed(10, 0.0, 80);
            Assert.AreSame(fixedSpeed, fixedSpeed.FallBackToFixedSpeed(1.0, 1.0));

            var synced = QuestionRevealSchedule.AwaitingReading(10, 80).WithReading(0.0, 2.0, 0.0);
            Assert.AreSame(synced, synced.FallBackToFixedSpeed(1.0, 1.0), "読み上げに同期済みなら固定速度へ戻さない。");
        }

        // --- 早押しで止める・再開・全文 ---

        [Test]
        public void Freeze_StopsAtCurrentCount()
        {
            var schedule = QuestionRevealSchedule.FixedSpeed(10, 0.0, 100);

            var frozen = schedule.Freeze(0.35);

            Assert.IsTrue(frozen.IsFrozen);
            Assert.AreEqual(4, frozen.VisibleCount(0.35));
            Assert.AreEqual(4, frozen.VisibleCount(10.0), "止めている間は増えない。");
            Assert.AreEqual(0.35, frozen.FrozenAt, Tolerance);
            Assert.IsTrue(frozen.IsSettled(0.35));
            Assert.IsFalse(schedule.IsFrozen, "元のインスタンスは変わらない（不変）。");
        }

        [Test]
        public void Freeze_Twice_KeepsFirstPosition()
        {
            var frozen = QuestionRevealSchedule.FixedSpeed(10, 0.0, 100).Freeze(0.15);

            Assert.AreSame(frozen, frozen.Freeze(0.85));
            Assert.AreEqual(2, frozen.VisibleCount(0.85));
        }

        [Test]
        public void Resume_FixedSpeed_ShiftsStartByPausedTime()
        {
            // #144 レビュー M-3: 読み上げの無い部屋では、止めていた時間ぶん後ろへずらして止めた位置から続ける。
            var frozen = QuestionRevealSchedule.FixedSpeed(10, 0.0, 100).Freeze(0.15);

            var resumed = frozen.Resume(5.15);

            Assert.IsFalse(resumed.IsFrozen);
            Assert.AreEqual(5.0, resumed.StartTime, Tolerance, "5 秒止めていたので開始時刻も 5 秒後ろへ。");
            Assert.AreEqual(2, resumed.VisibleCount(5.15), "再開した瞬間は止めた位置のまま（全文にならない）。");
            Assert.AreEqual(3, resumed.VisibleCount(5.25), "そこから同じ速度で続ける。");
            Assert.AreSame(resumed, resumed.Resume(6.0), "止めていなければ何もしない。");
        }

        [Test]
        public void Resume_Synced_CatchesUpWithReading()
        {
            // 読み上げ音声は早押しで止まらないので、読み上げの位置へ追いつく。
            var frozen = QuestionRevealSchedule.AwaitingReading(10, 80)
                .WithReading(readingStartTime: 0.0, durationSec: 1.0, now: 0.0)
                .Freeze(0.15);

            var resumed = frozen.Resume(0.55);

            Assert.AreEqual(0.0, resumed.StartTime, Tolerance, "時間軸はずらさない。");
            Assert.AreEqual(6, resumed.VisibleCount(0.55));
        }

        [Test]
        public void Resume_FrozenWhileAwaiting_ShiftsOnlyFromFallbackStart()
        {
            // 読み上げ待ちの間に止め、その後（止めたまま）固定速度へ切り替わった場合は、切り替え以降のぶんだけずらす。
            var schedule = QuestionRevealSchedule.AwaitingReading(10, 100)
                .Freeze(1.0)
                .FallBackToFixedSpeed(startTime: 2.0, now: 2.0);

            var resumed = schedule.Resume(3.0);

            Assert.AreEqual(3.0, resumed.StartTime, Tolerance);
            Assert.AreEqual(1, resumed.VisibleCount(3.0));
        }

        [Test]
        public void Resume_NeverDecreasesBelowFrozenCount()
        {
            // 同期の時間軸より先へ表示していた状態で止めた場合も、再開で減らない。
            var synced = QuestionRevealSchedule.FixedSpeed(10, 0.0, 100)
                .WithReading(readingStartTime: 0.45, durationSec: 10.0, now: 0.45);
            var resumed = synced.Freeze(0.5).Resume(0.6);

            Assert.AreEqual(5, resumed.VisibleCount(0.6));
        }

        [Test]
        public void WithReading_WhileFrozen_StaysFrozenUntilResume()
        {
            var frozen = QuestionRevealSchedule.AwaitingReading(10, 80).Freeze(0.1);

            var synced = frozen.WithReading(readingStartTime: 0.2, durationSec: 1.0, now: 0.3);

            Assert.AreEqual(QuestionRevealMode.Synced, synced.Mode);
            Assert.IsTrue(synced.IsFrozen);
            Assert.AreEqual(0, synced.VisibleCount(0.9));
            Assert.AreEqual(8, synced.Resume(0.9).VisibleCount(0.9));
        }

        [Test]
        public void ShowAll_ReturnsFullFromAnyState()
        {
            var frozen = QuestionRevealSchedule.FixedSpeed(10, 0.0, 100).Freeze(0.15);

            var full = frozen.ShowAll();

            Assert.AreEqual(QuestionRevealMode.Full, full.Mode);
            Assert.IsFalse(full.IsFrozen);
            Assert.AreEqual(10, full.VisibleCount(0.0));
            Assert.AreSame(full, full.ShowAll());
            Assert.AreSame(full, full.Freeze(1.0), "全文表示は止めない。");
        }
    }
}
