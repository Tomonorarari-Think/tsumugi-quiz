using System;
using System.Threading;
using NUnit.Framework;
using TsumugiQuiz.Questions;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="ReloadDebouncer"/> の検証。issue #29: FileSystemWatcher の実イベントを EditMode で
    /// 決定的に検証するのは難しいため、デバウンスロジックを <see cref="ManualDebounceScheduler"/> で
    /// 純粋にテストする（実時間の待機を必要としない）。
    /// </summary>
    public class ReloadDebouncerTests
    {
        [Test]
        public void Trigger_CalledMultipleTimesRapidly_OnlyLastScheduleFires()
        {
            var scheduler = new ManualDebounceScheduler();
            var callCount = 0;
            using (var debouncer = new ReloadDebouncer(() => callCount++, TimeSpan.FromMilliseconds(500), scheduler))
            {
                debouncer.Trigger();
                debouncer.Trigger();
                debouncer.Trigger();

                Assert.AreEqual(3, scheduler.ScheduleCount, "Trigger の回数だけ新しいスケジュールが積まれること");
                Assert.AreEqual(0, callCount, "発火前はコールバックが呼ばれないこと");

                scheduler.FireLatest();

                Assert.AreEqual(1, callCount, "最後の1回分だけコールバックが呼ばれること（デバウンス）");
            }
        }

        [Test]
        public void Trigger_SingleCall_FiresCallbackOnce()
        {
            var scheduler = new ManualDebounceScheduler();
            var callCount = 0;
            using (var debouncer = new ReloadDebouncer(() => callCount++, TimeSpan.FromMilliseconds(500), scheduler))
            {
                debouncer.Trigger();
                scheduler.FireLatest();

                Assert.AreEqual(1, callCount);
            }
        }

        [Test]
        public void Dispose_CancelsPendingSchedule_CallbackNeverFires()
        {
            var scheduler = new ManualDebounceScheduler();
            var callCount = 0;
            var debouncer = new ReloadDebouncer(() => callCount++, TimeSpan.FromMilliseconds(500), scheduler);

            debouncer.Trigger();
            debouncer.Dispose();
            scheduler.FireLatest();

            Assert.AreEqual(0, callCount, "Dispose 後は保留中のスケジュールがキャンセルされ、コールバックが呼ばれないこと");
        }

        [Test]
        public void Trigger_AfterDispose_DoesNotScheduleAgain()
        {
            var scheduler = new ManualDebounceScheduler();
            var debouncer = new ReloadDebouncer(() => { }, TimeSpan.FromMilliseconds(500), scheduler);
            debouncer.Dispose();

            debouncer.Trigger();

            Assert.AreEqual(0, scheduler.ScheduleCount, "Dispose 後の Trigger は新しいスケジュールを積まないこと");
        }

        [Test]
        public void Constructor_NegativeDelay_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReloadDebouncer(() => { }, TimeSpan.FromMilliseconds(-1), new ManualDebounceScheduler()));
        }

        [Test]
        public void Constructor_NullCallback_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ReloadDebouncer(null, TimeSpan.FromMilliseconds(500), new ManualDebounceScheduler()));
        }

        [Test]
        [Category("Slow")]
        public void Trigger_WithRealTimerScheduler_FiresOnceAfterRealDelay()
        {
            // TimerDebounceScheduler（既定実装、System.Threading.Timer）を使った統合的な確認。
            // 実際の待機を伴うため遅延は短く設定する。
            var callCount = 0;
            var signal = new ManualResetEventSlim(false);
            using (var debouncer = new ReloadDebouncer(() =>
            {
                Interlocked.Increment(ref callCount);
                signal.Set();
            }, TimeSpan.FromMilliseconds(30)))
            {
                debouncer.Trigger();
                debouncer.Trigger();
                debouncer.Trigger();

                var fired = signal.Wait(TimeSpan.FromSeconds(2));

                Assert.IsTrue(fired, "デバウンス後にコールバックが発火すること");
                Assert.AreEqual(1, callCount, "連続 Trigger でもコールバックは1回だけ呼ばれること");
            }
        }
    }
}
