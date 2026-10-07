using System;

namespace TsumugiQuiz.Core.Audio
{
    /// <summary>
    /// 解析済みの WAV データ（docs/tts.md §6.3）。生成後は不変。
    /// <see cref="Samples"/> はインターリーブ済みの -1.0〜1.0 の float 配列で、
    /// Unity 側の <c>AudioClip.SetData</c> にそのまま渡せる形になっている。
    /// </summary>
    public readonly struct WavData
    {
        private readonly float[] _samples;

        public WavData(float[] samples, int channels, int sampleRate)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels), channels, "チャンネル数は 1 以上でなければなりません。");
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "サンプルレートは 1 以上でなければなりません。");

            _samples = samples;
            Channels = channels;
            SampleRate = sampleRate;
        }

        /// <summary>インターリーブ済みサンプル（-1.0〜1.0）。</summary>
        public float[] Samples => _samples ?? Array.Empty<float>();

        /// <summary>チャンネル数。</summary>
        public int Channels { get; }

        /// <summary>サンプルレート（Hz）。</summary>
        public int SampleRate { get; }

        /// <summary>1 チャンネルあたりのサンプル数（<c>AudioClip.Create</c> の lengthSamples）。</summary>
        public int FrameCount => Channels <= 0 ? 0 : Samples.Length / Channels;

        /// <summary>再生時間（秒）。再生同期の <c>durationSec</c> の元になる（docs/tts.md §6.2）。</summary>
        public double DurationSec => SampleRate <= 0 ? 0d : (double)FrameCount / SampleRate;
    }
}
