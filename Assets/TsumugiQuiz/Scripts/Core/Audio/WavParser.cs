using System;
using System.Globalization;
using System.Text;

namespace TsumugiQuiz.Core.Audio
{
    /// <summary>
    /// メモリ上の WAV バイト列を解析する純 C# パーサ（docs/tts.md §6.3）。
    ///
    /// <c>UnityWebRequestMultimedia.GetAudioClip</c> はファイル URL を要求するため、
    /// voicevox_core が返す wav（RIFF ヘッダ付き 16bit PCM）を自前で解析して
    /// <c>AudioClip</c> 化する必要がある。Unity API に依存しないので EditMode テストで検証できる。
    ///
    /// 対応範囲は voicevox_core の出力（RIFF/WAVE、fmt = PCM 16bit、data）に限る。
    /// それ以外は <see cref="WavFormatException"/> を投げ、呼び出し側は読み上げなしで続行する。
    /// </summary>
    public static class WavParser
    {
        /// <summary>RIFF ヘッダ（"RIFF" + サイズ + "WAVE"）の最小長。</summary>
        public const int MinimumHeaderSize = 12;

        /// <summary>チャンクヘッダを読むのに十分な先頭バイト数（<see cref="TryGetDurationSec"/> 用の目安）。</summary>
        public const int HeaderProbeSize = 1024;

        private const ushort WaveFormatPcm = 1;
        private const int SupportedBitsPerSample = 16;

        /// <summary>16bit PCM の WAV を解析する。</summary>
        /// <exception cref="ArgumentNullException"><paramref name="bytes"/> が null</exception>
        /// <exception cref="WavFormatException">RIFF/WAVE でない、PCM 16bit でない、チャンクが壊れている</exception>
        public static WavData ParseWav(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            return ParseWav(new ReadOnlySpan<byte>(bytes));
        }

        /// <inheritdoc cref="ParseWav(byte[])"/>
        public static WavData ParseWav(ReadOnlySpan<byte> bytes)
        {
            var layout = ReadLayout(bytes, totalFileBytes: -1L, requireCompleteData: true);

            var bytesPerSample = layout.BitsPerSample / 8;
            var sampleCount = layout.DataLength / bytesPerSample;

            // 途中で切れているとチャンネル数の倍数にならない。端数は落とす（再生は成立する）。
            sampleCount -= sampleCount % layout.Channels;

            var samples = new float[sampleCount];
            var data = bytes.Slice(layout.DataOffset, sampleCount * bytesPerSample);
            for (var i = 0; i < sampleCount; i++)
            {
                var raw = (short)(data[i * 2] | (data[(i * 2) + 1] << 8));

                // 32768 で割ると short.MinValue が -1.0 に対応し、値域が -1.0〜0.99997 に収まる。
                samples[i] = raw / 32768f;
            }

            return new WavData(samples, layout.Channels, layout.SampleRate);
        }

        /// <summary>
        /// ヘッダだけから再生時間を求める（サンプル本体を読まない）。
        /// キャッシュの index.json をディレクトリ走査で再構築するときに使う（docs/tts.md §7.3）。
        /// 壊れていれば false を返す（例外にしない）。
        ///
        /// <paramref name="bytes"/> には先頭だけ（<see cref="HeaderProbeSize"/> バイト程度）を渡せばよい。
        /// data チャンクの<b>宣言サイズ</b>から算出するので、サンプル本体が手元に無くても求まる。
        /// </summary>
        public static bool TryGetDurationSec(ReadOnlySpan<byte> bytes, out double durationSec)
            => TryGetDurationSec(bytes, totalFileBytes: -1L, out durationSec, out _);

        /// <summary>
        /// <paramref name="totalFileBytes"/> にファイル全体のバイト数を渡すと、
        /// data チャンクの宣言サイズが実ファイルを超えている場合に実サイズで丸める
        /// （壊れたヘッダで極端な再生時間が出るのを防ぐ）。0 未満なら丸めない。
        /// </summary>
        public static bool TryGetDurationSec(ReadOnlySpan<byte> bytes, long totalFileBytes, out double durationSec)
            => TryGetDurationSec(bytes, totalFileBytes, out durationSec, out _);

        /// <summary>
        /// <paramref name="probe"/> で失敗の種類を区別する版。
        ///
        /// <see cref="WavProbeResult.Incomplete"/> は「渡したバッファが短くて fmt / data チャンクに
        /// 届かなかった」だけで、<b>ファイルが壊れているとは限らない</b>。
        /// キャッシュの再構築（docs/tts.md §7.3）では、この場合にファイルを削除してはいけない。
        /// </summary>
        public static bool TryGetDurationSec(
            ReadOnlySpan<byte> bytes, long totalFileBytes, out double durationSec, out WavProbeResult probe)
        {
            durationSec = 0d;
            probe = WavProbeResult.Invalid;

            try
            {
                var layout = ReadLayout(bytes, totalFileBytes, requireCompleteData: false);
                var frameBytes = layout.Channels * (layout.BitsPerSample / 8);
                if (frameBytes <= 0) return false;

                durationSec = (double)(layout.DataLength / frameBytes) / layout.SampleRate;
                probe = WavProbeResult.Ok;
                return true;
            }
            catch (WavTruncatedException)
            {
                probe = WavProbeResult.Incomplete;
                return false;
            }
            catch (WavFormatException)
            {
                probe = WavProbeResult.Invalid;
                return false;
            }
        }

        /// <summary>解析結果のうちサンプル本体を除いた配置情報。</summary>
        private readonly struct WavLayout
        {
            public WavLayout(int channels, int sampleRate, int bitsPerSample, int dataOffset, int dataLength)
            {
                Channels = channels;
                SampleRate = sampleRate;
                BitsPerSample = bitsPerSample;
                DataOffset = dataOffset;
                DataLength = dataLength;
            }

            public int Channels { get; }
            public int SampleRate { get; }
            public int BitsPerSample { get; }
            public int DataOffset { get; }
            public int DataLength { get; }
        }

        /// <param name="totalFileBytes">
        /// ファイル全体のバイト数。0 未満なら <paramref name="bytes"/> の長さを全体とみなす。
        /// </param>
        /// <param name="requireCompleteData">
        /// true なら data チャンクがバッファ内に収まっていることを要求する（壊れたファイルの検出）。
        /// false なら先頭だけ読み込んだバッファでもよく、宣言サイズをそのまま長さとして扱う。
        /// </param>
        private static WavLayout ReadLayout(ReadOnlySpan<byte> bytes, long totalFileBytes, bool requireCompleteData)
        {
            if (bytes.Length < MinimumHeaderSize)
            {
                throw new WavFormatException(
                    $"WAV が短すぎます（{bytes.Length} バイト、最低 {MinimumHeaderSize} バイト必要）。");
            }
            if (!Matches(bytes, 0, "RIFF"))
            {
                throw new WavFormatException($"RIFF ヘッダではありません（先頭 4 バイト: {Describe(bytes, 0)}）。");
            }
            if (!Matches(bytes, 8, "WAVE"))
            {
                throw new WavFormatException($"WAVE 形式ではありません（形式識別子: {Describe(bytes, 8)}）。");
            }

            var channels = 0;
            var sampleRate = 0;
            var bitsPerSample = 0;
            var formatFound = false;
            var dataOffset = -1;
            var dataLength = 0;

            var offset = MinimumHeaderSize;
            while (offset + 8 <= bytes.Length)
            {
                var chunkSizeRaw = ReadUInt32(bytes, offset + 4);
                if (chunkSizeRaw > int.MaxValue)
                {
                    throw new WavFormatException($"チャンクサイズが不正です（{chunkSizeRaw} バイト、offset={offset}）。");
                }

                var chunkSize = (int)chunkSizeRaw;
                var chunkStart = offset + 8;

                if (Matches(bytes, offset, "fmt "))
                {
                    ReadFormatChunk(bytes, chunkStart, chunkSize, out channels, out sampleRate, out bitsPerSample);
                    formatFound = true;
                }
                else if (Matches(bytes, offset, "data"))
                {
                    if (requireCompleteData)
                    {
                        // サンプル本体まで手元にある前提。宣言サイズが収まらなければ壊れている。
                        var present = Math.Max(0, bytes.Length - chunkStart);
                        if (chunkSize > present)
                        {
                            throw new WavFormatException(
                                $"data チャンクが途中で切れています（宣言 {chunkSize} バイト、実際 {present} バイト）。");
                        }

                        dataOffset = chunkStart;
                        dataLength = chunkSize;
                    }
                    else
                    {
                        // 先頭だけ読んでいる前提。宣言サイズを採り、ファイル全体の大きさが分かる場合だけ丸める。
                        var available = totalFileBytes >= 0L
                            ? Math.Max(0L, totalFileBytes - chunkStart)
                            : long.MaxValue;

                        dataOffset = chunkStart;
                        dataLength = (int)Math.Min(chunkSize, available);
                    }

                    // voicevox_core の出力では data の後ろにチャンクは続かないので、ここで打ち切る。
                    break;
                }

                // チャンクは偶数バイト境界に揃えられる（RIFF の仕様）。
                var next = chunkStart + chunkSize + (chunkSize & 1);
                if (next <= offset)
                {
                    throw new WavFormatException($"チャンクの連結が不正です（offset={offset}, size={chunkSize}）。");
                }
                offset = next;
            }

            // 先頭だけを読んでいる場合、チャンクに届く前にバッファが尽きただけかもしれない。
            // その場合は「壊れている」ではなく「情報不足」として区別する（呼び出し側が削除しないため）。
            var truncatedProbe = !requireCompleteData && totalFileBytes > bytes.Length;

            if (!formatFound)
            {
                if (truncatedProbe) throw new WavTruncatedException("fmt チャンクに届く前にバッファが尽きました。");
                throw new WavFormatException("fmt チャンクが見つかりません。");
            }
            if (dataOffset < 0)
            {
                if (truncatedProbe) throw new WavTruncatedException("data チャンクに届く前にバッファが尽きました。");
                throw new WavFormatException("data チャンクが見つかりません。");
            }

            return new WavLayout(channels, sampleRate, bitsPerSample, dataOffset, dataLength);
        }

        private static void ReadFormatChunk(
            ReadOnlySpan<byte> bytes, int chunkStart, int chunkSize,
            out int channels, out int sampleRate, out int bitsPerSample)
        {
            if (chunkSize < 16 || chunkStart + 16 > bytes.Length)
            {
                throw new WavFormatException($"fmt チャンクが不正です（size={chunkSize}）。");
            }

            var audioFormat = ReadUInt16(bytes, chunkStart);
            channels = ReadUInt16(bytes, chunkStart + 2);
            sampleRate = (int)ReadUInt32(bytes, chunkStart + 4);
            bitsPerSample = ReadUInt16(bytes, chunkStart + 14);

            if (audioFormat != WaveFormatPcm)
            {
                throw new WavFormatException($"非圧縮 PCM 以外には対応していません（audioFormat={audioFormat}）。");
            }
            if (bitsPerSample != SupportedBitsPerSample)
            {
                throw new WavFormatException(
                    $"{SupportedBitsPerSample}bit PCM 以外には対応していません（bitsPerSample={bitsPerSample}）。");
            }
            if (channels <= 0)
            {
                throw new WavFormatException($"チャンネル数が不正です（channels={channels}）。");
            }
            if (sampleRate <= 0)
            {
                throw new WavFormatException($"サンプルレートが不正です（sampleRate={sampleRate}）。");
            }
        }

        private static bool Matches(ReadOnlySpan<byte> bytes, int offset, string ascii)
        {
            if (offset + ascii.Length > bytes.Length) return false;
            for (var i = 0; i < ascii.Length; i++)
            {
                if (bytes[offset + i] != (byte)ascii[i]) return false;
            }
            return true;
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
            => (uint)(bytes[offset]
                      | (bytes[offset + 1] << 8)
                      | (bytes[offset + 2] << 16)
                      | (bytes[offset + 3] << 24));

        private static int ReadUInt16(ReadOnlySpan<byte> bytes, int offset)
            => bytes[offset] | (bytes[offset + 1] << 8);

        /// <summary>エラーメッセージ用に 4 バイトを読める形で表す。</summary>
        private static string Describe(ReadOnlySpan<byte> bytes, int offset)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < 4 && offset + i < bytes.Length; i++)
            {
                var b = bytes[offset + i];
                builder.Append(b >= 0x20 && b < 0x7F
                    ? ((char)b).ToString()
                    : "\\x" + b.ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }
    }
}
