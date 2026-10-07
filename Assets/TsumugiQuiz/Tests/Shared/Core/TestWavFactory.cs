using System;
using System.IO;
using System.Text;

namespace TsumugiQuiz.Tests.Shared.Core
{
    /// <summary>
    /// テスト用の 16bit PCM WAV バイト列を組み立てる（docs/tts.md §11.1）。
    /// voicevox_core の出力に合わせて既定は mono / 24000Hz。
    /// </summary>
    internal static class TestWavFactory
    {
        /// <summary>voicevox_core の出力サンプルレート。</summary>
        public const int DefaultSampleRate = 24000;

        /// <summary>
        /// 16bit PCM の WAV を作る。サンプル値は連番の三角波風に埋める。
        /// </summary>
        /// <param name="frameCount">フレーム数。</param>
        /// <param name="channels">チャンネル数。</param>
        /// <param name="sampleRate">サンプルレート。</param>
        /// <param name="sampleOffset">
        /// サンプル値の位相オフセット。フレーム数（＝再生時間）を変えずに波形の中身だけを
        /// 変えたいとき（例: 読みが違うテキストごとに異なる wav を返すフェイク）に使う。
        /// </param>
        public static byte[] Create(
            int frameCount, int channels = 1, int sampleRate = DefaultSampleRate, int sampleOffset = 0)
        {
            if (frameCount < 0) throw new ArgumentOutOfRangeException(nameof(frameCount));

            var samples = new short[frameCount * channels];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = (short)((((i + sampleOffset) % 200) - 100) * 300);
            }

            return Create(samples, channels, sampleRate, bitsPerSample: 16, audioFormat: 1, extraChunk: false);
        }

        /// <summary>fmt の前に無関係なチャンク（LIST）を挟んだ WAV を作る（チャンク走査の検証用）。</summary>
        public static byte[] CreateWithLeadingListChunk(int frameCount, int sampleRate = DefaultSampleRate)
        {
            var samples = new short[frameCount];
            for (var i = 0; i < samples.Length; i++) samples[i] = (short)(i * 7);

            return Create(samples, channels: 1, sampleRate: sampleRate, bitsPerSample: 16, audioFormat: 1, extraChunk: true);
        }

        /// <summary>PCM 以外（audioFormat != 1）の WAV を作る。</summary>
        public static byte[] CreateNonPcm(int frameCount)
            => Create(new short[frameCount], channels: 1, sampleRate: DefaultSampleRate,
                      bitsPerSample: 16, audioFormat: 3, extraChunk: false);

        /// <summary>8bit PCM の WAV を作る（未対応であることの検証用）。</summary>
        public static byte[] Create8Bit(int frameCount)
            => Create(new short[frameCount], channels: 1, sampleRate: DefaultSampleRate,
                      bitsPerSample: 8, audioFormat: 1, extraChunk: false);

        /// <summary>data チャンクの宣言サイズが実データより大きい（途中で切れた）WAV を作る。</summary>
        public static byte[] CreateTruncated(int frameCount)
        {
            var wav = Create(frameCount);
            var truncated = new byte[wav.Length - 100];
            Array.Copy(wav, truncated, truncated.Length);
            return truncated;
        }

        /// <summary>RIFF ヘッダを壊した WAV を作る。</summary>
        public static byte[] CreateBrokenRiff(int frameCount)
        {
            var wav = Create(frameCount);
            wav[0] = (byte)'X';
            return wav;
        }

        /// <summary>data チャンクを持たない WAV を作る（fmt のみ）。</summary>
        public static byte[] CreateWithoutDataChunk()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(4 + 24);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                WriteFormatChunk(writer, channels: 1, sampleRate: DefaultSampleRate, bitsPerSample: 16, audioFormat: 1);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static byte[] Create(
            short[] samples, int channels, int sampleRate, int bitsPerSample, int audioFormat, bool extraChunk)
        {
            var bytesPerSample = bitsPerSample / 8;
            var dataBytes = samples.Length * bytesPerSample;

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(0); // 後で埋める
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));

                if (extraChunk)
                {
                    writer.Write(Encoding.ASCII.GetBytes("LIST"));
                    writer.Write(4);
                    writer.Write(Encoding.ASCII.GetBytes("INFO"));
                }

                WriteFormatChunk(writer, channels, sampleRate, bitsPerSample, audioFormat);

                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataBytes);
                foreach (var sample in samples)
                {
                    if (bytesPerSample == 2) writer.Write(sample);
                    else writer.Write((byte)(sample & 0xFF));
                }

                writer.Flush();
                var bytes = stream.ToArray();

                // RIFF のサイズフィールド（先頭 8 バイトを除いた長さ）を埋める。
                var riffSize = bytes.Length - 8;
                bytes[4] = (byte)(riffSize & 0xFF);
                bytes[5] = (byte)((riffSize >> 8) & 0xFF);
                bytes[6] = (byte)((riffSize >> 16) & 0xFF);
                bytes[7] = (byte)((riffSize >> 24) & 0xFF);
                return bytes;
            }
        }

        private static void WriteFormatChunk(
            BinaryWriter writer, int channels, int sampleRate, int bitsPerSample, int audioFormat)
        {
            var blockAlign = channels * (bitsPerSample / 8);

            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((ushort)audioFormat);
            writer.Write((ushort)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * blockAlign);
            writer.Write((ushort)blockAlign);
            writer.Write((ushort)bitsPerSample);
        }
    }
}
