using System;

namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// <see cref="ReloadDebouncer"/> が使うタイマーの抽象化。
    /// 実運用では <see cref="TimerDebounceScheduler"/>（System.Threading.Timer）を使い、
    /// EditMode テストでは手動で発火できる実装を注入して決定的に検証する。
    /// </summary>
    public interface IDebounceScheduler
    {
        /// <summary>
        /// <paramref name="delay"/> 経過後に <paramref name="callback"/> を呼ぶ予約をする。
        /// 返り値の <see cref="IDisposable"/> を Dispose すると、まだ発火していなければキャンセルされる。
        /// </summary>
        IDisposable Schedule(TimeSpan delay, Action callback);
    }
}
