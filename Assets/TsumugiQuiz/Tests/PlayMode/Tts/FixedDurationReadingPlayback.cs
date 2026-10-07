using System.Threading;
using System.Threading.Tasks;
using TsumugiQuiz.Core.Audio;

namespace TsumugiQuiz.Tests.PlayMode.Tts
{
    /// <summary>
    /// 合成せずに決まった長さを即座に返す読み上げ実装（#144 の文字送り・再生開始の記録のテスト用）。
    /// 再生はしない（<see cref="Schedule"/> は受け取った値を記録するだけ）。
    /// </summary>
    internal sealed class FixedDurationReadingPlayback : IReadingPlayback
    {
        private readonly double _durationSec;

        /// <param name="durationSec"><see cref="PrepareAsync"/> が返す長さ（秒）。</param>
        public FixedDurationReadingPlayback(double durationSec) => _durationSec = durationSec;

        /// <summary><see cref="PrepareAsync"/> が呼ばれた回数。</summary>
        public int PrepareCallCount { get; private set; }

        /// <summary><see cref="Schedule"/> が呼ばれた回数。</summary>
        public int ScheduleCallCount { get; private set; }

        /// <summary>直近に <see cref="Schedule"/> で受け取った問題インデックス。未受信なら -1。</summary>
        public int LastScheduledQuestionIndex { get; private set; } = -1;

        /// <inheritdoc />
        public bool IsReadingPossible => true;

        /// <inheritdoc />
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <inheritdoc />
        public Task<double> PrepareAsync(
            int questionIndex, string readingText, float speed, CancellationToken cancellationToken)
        {
            PrepareCallCount++;
            return Task.FromResult(_durationSec);
        }

        /// <inheritdoc />
        public void Schedule(
            int questionIndex, double playAtServerTime, double referenceServerTimeNow, double hostDurationSec)
        {
            ScheduleCallCount++;
            LastScheduledQuestionIndex = questionIndex;
        }

        /// <inheritdoc />
        public void CancelReading()
        {
        }
    }
}
