using System;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// ネットワーク時刻（サーバー時刻軸）→ <c>AudioSettings.dspTime</c> の変換と、
    /// 再生予約の決定を行う純関数（docs/tts.md §6.1、docs/network.md §7.3、#23）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity API に依存しない純 C# にしているのは EditMode でそのままテストできるようにするため。
    /// 実際の <c>AudioSource.PlayScheduled</c> 呼び出しは <see cref="TtsSyncPlayer"/> が行う。
    /// </para>
    /// <para>
    /// <c>AudioSource.PlayScheduled(double time)</c> の <c>time</c> は
    /// 「<c>AudioSettings.dspTime</c> と同じ絶対時間軸の秒数」なので、
    /// 「サーバー時刻での残り秒数」をそのまま <c>dspTime</c> に足せばよい。
    /// </para>
    /// </remarks>
    public static class PlaybackScheduler
    {
        /// <summary>
        /// サーバー時刻軸の再生開始時刻を <c>dspTime</c> 軸へ変換する。
        /// </summary>
        /// <param name="playAtServerTime">サーバーが指定した再生開始時刻（サーバー時刻軸の秒）。</param>
        /// <param name="referenceServerTimeNow">
        /// 同じ時刻軸での「いま」。NGO では <c>NetworkManager.LocalTime.Time</c>
        /// （docs/tts.md §6.1 / docs/network.md §7.3 の leadSec の式）。
        /// </param>
        /// <param name="dspTimeNow">いまの <c>AudioSettings.dspTime</c>。</param>
        /// <returns><c>dspTime</c> 軸での再生開始時刻（過去になりうる）。</returns>
        /// <exception cref="ArgumentException">有限でない値を渡したとき。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="dspTimeNow"/> が負のとき。</exception>
        public static double ToDspTime(double playAtServerTime, double referenceServerTimeNow, double dspTimeNow)
        {
            RequireFinite(playAtServerTime, nameof(playAtServerTime));
            RequireFinite(referenceServerTimeNow, nameof(referenceServerTimeNow));
            RequireFinite(dspTimeNow, nameof(dspTimeNow));
            RequireNotNegative(dspTimeNow, nameof(dspTimeNow), "dspTime は 0 以上でなければなりません。");

            return dspTimeNow + (playAtServerTime - referenceServerTimeNow);
        }

        /// <summary>
        /// <c>dspTime</c> 軸の開始時刻と音声の長さから再生予約を決める。
        /// </summary>
        /// <remarks>
        /// 遅延が負（指定時刻が既に過ぎている）場合は、公式リファレンスの挙動
        /// （過去を指定すると即座に再生が始まる）に合わせて<b>即再生</b>にし、
        /// 過ぎた分だけ頭を飛ばして「全員の読み上げ完了時刻」に追従させる（docs/tts.md §6.1）。
        /// 過ぎた分が音声の長さ以上なら、再生するものが無い
        /// （<see cref="PlaybackSchedule.ShouldPlay"/> が false）。
        /// </remarks>
        /// <param name="targetDspTime"><see cref="ToDspTime"/> の結果。</param>
        /// <param name="dspTimeNow">いまの <c>AudioSettings.dspTime</c>。</param>
        /// <param name="durationSec">再生する音声の長さ（秒）。0 なら再生するものが無い。</param>
        /// <returns>再生予約。</returns>
        /// <exception cref="ArgumentException">有限でない値を渡したとき。</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="dspTimeNow"/> または <paramref name="durationSec"/> が負のとき。
        /// </exception>
        public static PlaybackSchedule Create(double targetDspTime, double dspTimeNow, double durationSec)
        {
            RequireFinite(targetDspTime, nameof(targetDspTime));
            RequireFinite(dspTimeNow, nameof(dspTimeNow));
            RequireFinite(durationSec, nameof(durationSec));
            RequireNotNegative(dspTimeNow, nameof(dspTimeNow), "dspTime は 0 以上でなければなりません。");
            RequireNotNegative(durationSec, nameof(durationSec), "音声の長さは 0 以上でなければなりません。");

            var leadSec = targetDspTime - dspTimeNow;

            if (durationSec <= 0d)
            {
                return PlaybackSchedule.Nothing(dspTimeNow, leadSec);
            }

            if (leadSec > 0d)
            {
                return PlaybackSchedule.Reserved(targetDspTime, durationSec, leadSec);
            }

            var startOffsetSec = -leadSec;
            if (startOffsetSec >= durationSec)
            {
                // 読み上げ時間が丸ごと過ぎている（極端に遅れて受け取った場合）。無音で追いつく。
                return PlaybackSchedule.Nothing(dspTimeNow, leadSec);
            }

            return PlaybackSchedule.Immediate(dspTimeNow, startOffsetSec, durationSec, leadSec);
        }

        private static void RequireFinite(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException($"有限の値でなければなりません（{value}）。", parameterName);
            }
        }

        private static void RequireNotNegative(double value, string parameterName, string message)
        {
            if (value < 0d)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, message);
            }
        }
    }
}
