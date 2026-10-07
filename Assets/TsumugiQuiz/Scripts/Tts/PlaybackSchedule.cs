using System;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// 1 回分の再生予約（<see cref="PlaybackScheduler.Create"/> の結果）。生成後は不変。
    /// 時刻はすべて <c>AudioSettings.dspTime</c> と同じ絶対時間軸の秒（docs/tts.md §6.1）。
    /// </summary>
    public readonly struct PlaybackSchedule
    {
        private PlaybackSchedule(
            bool shouldPlay, bool playImmediately, double dspStartTime,
            double startOffsetSec, double dspEndTime, double leadSec)
        {
            ShouldPlay = shouldPlay;
            PlayImmediately = playImmediately;
            DspStartTime = dspStartTime;
            StartOffsetSec = startOffsetSec;
            DspEndTime = dspEndTime;
            LeadSec = leadSec;
        }

        /// <summary>
        /// 再生するか。false は「再生するものが無い」
        /// （音声が空、または読み上げ時間が丸ごと過ぎている）ことを表す。
        /// </summary>
        public bool ShouldPlay { get; }

        /// <summary>
        /// 予約せず即座に再生するか（指定時刻が既に過ぎている場合）。
        /// このとき <see cref="StartOffsetSec"/> だけ頭を飛ばして再生する。
        /// </summary>
        public bool PlayImmediately { get; }

        /// <summary>再生を始める dsp 時刻。<see cref="PlayImmediately"/> のときは「いま」。</summary>
        public double DspStartTime { get; }

        /// <summary>頭を飛ばす秒数（遅刻した分）。予約できた場合は 0。</summary>
        public double StartOffsetSec { get; }

        /// <summary>
        /// 再生が終わる dsp 時刻。<see cref="ShouldPlay"/> が false のときは
        /// <see cref="DspStartTime"/> と同値（= すでに終わっている）。
        /// </summary>
        public double DspEndTime { get; }

        /// <summary>指定時刻までの残り秒数。負なら遅刻している。</summary>
        public double LeadSec { get; }

        /// <summary>指定時刻に間に合ったので予約する。</summary>
        internal static PlaybackSchedule Reserved(double dspStartTime, double durationSec, double leadSec)
            => new PlaybackSchedule(
                shouldPlay: true, playImmediately: false, dspStartTime: dspStartTime,
                startOffsetSec: 0d, dspEndTime: dspStartTime + durationSec, leadSec: leadSec);

        /// <summary>間に合わなかったので、頭を飛ばして即座に再生する。</summary>
        internal static PlaybackSchedule Immediate(
            double dspTimeNow, double startOffsetSec, double durationSec, double leadSec)
            => new PlaybackSchedule(
                shouldPlay: true, playImmediately: true, dspStartTime: dspTimeNow,
                startOffsetSec: startOffsetSec,
                dspEndTime: dspTimeNow + Math.Max(0d, durationSec - startOffsetSec), leadSec: leadSec);

        /// <summary>再生するものが無い（空の音声、または読み上げ時間が丸ごと過ぎている）。</summary>
        internal static PlaybackSchedule Nothing(double dspTimeNow, double leadSec)
            => new PlaybackSchedule(
                shouldPlay: false, playImmediately: false, dspStartTime: dspTimeNow,
                startOffsetSec: 0d, dspEndTime: dspTimeNow, leadSec: leadSec);

        /// <summary>ログ用の説明文。</summary>
        public override string ToString()
        {
            if (!ShouldPlay)
            {
                return $"再生なし（lead={LeadSec:F3}s）";
            }

            return PlayImmediately
                ? $"即再生（lead={LeadSec:F3}s、頭 {StartOffsetSec:F3}s を飛ばす、終了 dsp={DspEndTime:F3}）"
                : $"予約（lead={LeadSec:F3}s、開始 dsp={DspStartTime:F3}、終了 dsp={DspEndTime:F3}）";
        }
    }
}
