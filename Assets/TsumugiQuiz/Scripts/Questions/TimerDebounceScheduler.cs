using System;
using System.Threading;

namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// <see cref="System.Threading.Timer"/> を使った <see cref="IDebounceScheduler"/> の実装。
    /// コールバックはタイマーのスレッドプールスレッドから呼ばれる点に注意
    /// （<see cref="QuestionLibrary"/> 側でメインスレッドへ marshalling する）。
    /// </summary>
    public sealed class TimerDebounceScheduler : IDebounceScheduler
    {
        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            // L17: Timer を「停止状態」（dueTime = Timeout.InfiniteTimeSpan）で生成してから
            // Change() で改めて delay をセットする。こうすることで、コールバックが実行される
            // 可能性があるのは必ず timer 変数への代入が完了した後になり、
            // 「コンストラクタ完了前にコールバックが発火し、クロージャ内の timer がまだ null」
            // という競合を構造的に起こり得なくしている。
            Timer timer = null;
            timer = new Timer(_ =>
            {
                try
                {
                    callback();
                }
                finally
                {
                    timer?.Dispose();
                }
            }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            timer.Change(delay, Timeout.InfiniteTimeSpan);

            return timer;
        }
    }
}
