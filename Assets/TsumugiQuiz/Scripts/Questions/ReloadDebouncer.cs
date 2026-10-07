using System;

namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// 短時間に連続して発生するイベント（<see cref="System.IO.FileSystemWatcher"/> の変更通知等）を
    /// まとめ、最後の <see cref="Trigger"/> 呼び出しから <c>delay</c> 経過後に一度だけコールバックを呼ぶ。
    /// UnityEngine に依存しない純 C# クラスとして実装しており、<see cref="IDebounceScheduler"/> を
    /// 差し替えることで EditMode テストから実時間の待機なしに決定的に検証できる
    /// （issue #29: 「FileSystemWatcher の通知は EditMode で検証しにくければデバウンスロジックを
    /// 純 C# に分離してテスト」）。
    /// </summary>
    public sealed class ReloadDebouncer : IDisposable
    {
        private readonly TimeSpan _delay;
        private readonly IDebounceScheduler _scheduler;
        private readonly Action _callback;
        private readonly object _gate = new object();
        private IDisposable _pending;
        // L16: Trigger() のたびに世代を進め、発火したコールバックが「自分がスケジュールされた
        // 時点で最新だった世代」と一致する場合にのみ _pending をクリア（＆コールバックを実行）する。
        // Dispose() による IDisposable.Dispose() 呼び出しはベストエフォートのキャンセルでしかなく
        // （タイマーの実装によっては、Dispose 直前に発火が既に始まっている場合キャンセルできない）、
        // 世代比較によって「古い（既にキャンセル済みのはずの）発火」が新しい _pending を誤って
        // null にしたり、二重にコールバックを呼んだりすることを防ぐ。
        private long _generation;
        private bool _disposed;

        public ReloadDebouncer(Action callback, TimeSpan delay, IDebounceScheduler scheduler = null)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            if (delay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delay), "delay は 0 以上である必要があります。");
            }

            _delay = delay;
            _scheduler = scheduler ?? new TimerDebounceScheduler();
        }

        /// <summary>
        /// 変更を通知する。既に保留中のスケジュールがあればキャンセルし、新たに delay 後の発火を予約し直す
        /// （＝連続呼び出しでは最後の1回分のみコールバックが呼ばれる）。
        /// </summary>
        public void Trigger()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _pending?.Dispose();
                var myGeneration = ++_generation;
                _pending = _scheduler.Schedule(_delay, () => RaiseCallback(myGeneration));
            }
        }

        private void RaiseCallback(long myGeneration)
        {
            bool isCurrent;
            lock (_gate)
            {
                isCurrent = !_disposed && myGeneration == _generation;
                if (isCurrent)
                {
                    _pending = null;
                }
            }

            // 世代が一致しない（＝この発火がスケジュールされた後に Trigger が再度呼ばれ、
            // 本来はキャンセルされているはずだった）場合はコールバックを呼ばない。
            if (isCurrent)
            {
                _callback();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _pending?.Dispose();
                _pending = null;
            }
        }
    }
}
