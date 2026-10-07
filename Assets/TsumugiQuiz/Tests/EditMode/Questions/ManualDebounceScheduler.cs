using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="TsumugiQuiz.Questions.IDebounceScheduler"/> のテスト用実装。
    /// 実際に待機せず、<see cref="FireLatest"/> を呼ぶまでコールバックを発火しない。
    /// <see cref="TsumugiQuiz.Questions.ReloadDebouncer"/> のデバウンス（＝連続呼び出しでは
    /// 最後の1回分だけが有効になる）ロジックを、実時間の待機なしに決定的に検証するために使う。
    /// </summary>
    internal sealed class ManualDebounceScheduler : TsumugiQuiz.Questions.IDebounceScheduler
    {
        private sealed class Handle : IDisposable
        {
            private readonly Action _callback;
            public bool IsCancelled { get; private set; }

            public Handle(Action callback)
            {
                _callback = callback;
            }

            public void Dispose()
            {
                IsCancelled = true;
            }

            public void Fire()
            {
                if (!IsCancelled)
                {
                    _callback();
                }
            }
        }

        private readonly List<Handle> _handles = new List<Handle>();

        /// <summary>Schedule が呼ばれた回数（キャンセルされたものも含む）。</summary>
        public int ScheduleCount => _handles.Count;

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var handle = new Handle(callback);
            _handles.Add(handle);
            return handle;
        }

        /// <summary>最後にスケジュールされたものだけを発火する（キャンセル済みなら何も起きない）。</summary>
        public void FireLatest()
        {
            if (_handles.Count == 0)
            {
                return;
            }

            _handles[_handles.Count - 1].Fire();
        }
    }
}
