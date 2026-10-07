using System.Threading;
using System.Threading.Tasks;
using TsumugiQuiz.Core.Audio;

namespace TsumugiQuiz.Tests.PlayMode.Tts
{
    /// <summary>
    /// <b>合成が終わらない</b>読み上げ実装（#23 の Ready タイムアウト検証用）。
    /// <see cref="PrepareAsync"/> は取り消されるまで完了しないので、
    /// このクライアントは <c>TtsReadyRpc</c> を返さない。
    /// </summary>
    internal sealed class NeverReadyReadingPlayback : IReadingPlayback
    {
        /// <summary><see cref="PrepareAsync"/> が呼ばれた回数。</summary>
        public int PrepareCallCount { get; private set; }

        /// <summary><see cref="Schedule"/> が呼ばれた回数。</summary>
        public int ScheduleCallCount { get; private set; }

        /// <summary><see cref="CancelReading"/> が呼ばれた回数。</summary>
        public int CancelCallCount { get; private set; }

        /// <summary>直近に受け取った再生開始時刻（サーバー時刻軸の秒）。</summary>
        public double LastPlayAtServerTime { get; private set; }

        /// <summary>直近に受け取った読み上げ時間（秒）。</summary>
        public double LastDurationSec { get; private set; }

        /// <inheritdoc />
        public bool IsReadingPossible => true;

        /// <inheritdoc />
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <inheritdoc />
        public Task<double> PrepareAsync(
            int questionIndex, string readingText, float speed, CancellationToken cancellationToken)
        {
            PrepareCallCount++;

            // 取り消されるまで完了しない Task を返す（合成が終わらないクライアントの再現）。
            var completion = new TaskCompletionSource<double>();
            cancellationToken.Register(() => completion.TrySetCanceled());
            return completion.Task;
        }

        /// <inheritdoc />
        public void Schedule(
            int questionIndex, double playAtServerTime, double referenceServerTimeNow, double hostDurationSec)
        {
            ScheduleCallCount++;
            LastPlayAtServerTime = playAtServerTime;
            LastDurationSec = hostDurationSec;
        }

        /// <inheritdoc />
        public void CancelReading() => CancelCallCount++;
    }
}
