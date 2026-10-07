using System;
using NUnit.Framework;
using TsumugiQuiz.Core.Audio;
using TsumugiQuiz.Tests.Shared.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// WAV パーサの検証（docs/tts.md §6.3 / §11.1）。
    /// </summary>
    public sealed class WavParserTests
    {
        [Test]
        public void 正常な16bitPCMからサンプル数とチャンネル数とサンプルレートが取れる()
        {
            var wav = TestWavFactory.Create(frameCount: 1200);

            var parsed = WavParser.ParseWav(wav);

            Assert.That(parsed.Channels, Is.EqualTo(1));
            Assert.That(parsed.SampleRate, Is.EqualTo(TestWavFactory.DefaultSampleRate));
            Assert.That(parsed.FrameCount, Is.EqualTo(1200));
            Assert.That(parsed.Samples.Length, Is.EqualTo(1200));
            Assert.That(parsed.DurationSec, Is.EqualTo(1200d / TestWavFactory.DefaultSampleRate).Within(1e-9));
        }

        [Test]
        public void サンプルは正規化されて範囲内に収まる()
        {
            var wav = TestWavFactory.Create(frameCount: 500);

            var parsed = WavParser.ParseWav(wav);

            foreach (var sample in parsed.Samples)
            {
                Assert.That(sample, Is.InRange(-1f, 1f));
            }
            Assert.That(parsed.Samples, Has.Some.Not.EqualTo(0f), "無音ではないこと");
        }

        [Test]
        public void ステレオでもチャンネル数とフレーム数が正しい()
        {
            var wav = TestWavFactory.Create(frameCount: 300, channels: 2, sampleRate: 48000);

            var parsed = WavParser.ParseWav(wav);

            Assert.That(parsed.Channels, Is.EqualTo(2));
            Assert.That(parsed.SampleRate, Is.EqualTo(48000));
            Assert.That(parsed.FrameCount, Is.EqualTo(300));
            Assert.That(parsed.Samples.Length, Is.EqualTo(600), "インターリーブ済みサンプル数");
        }

        [Test]
        public void fmtの前に別チャンクがあっても読める()
        {
            var wav = TestWavFactory.CreateWithLeadingListChunk(frameCount: 100);

            var parsed = WavParser.ParseWav(wav);

            Assert.That(parsed.FrameCount, Is.EqualTo(100));
        }

        [Test]
        public void RIFFヘッダが壊れていれば例外になる()
        {
            var wav = TestWavFactory.CreateBrokenRiff(frameCount: 100);

            Assert.Throws<WavFormatException>(() => WavParser.ParseWav(wav));
        }

        [Test]
        public void 短すぎるバイト列は例外になる()
        {
            Assert.Throws<WavFormatException>(() => WavParser.ParseWav(new byte[] { 0x52, 0x49 }));
            Assert.Throws<WavFormatException>(() => WavParser.ParseWav(Array.Empty<byte>()));
        }

        [Test]
        public void dataチャンクが無ければ例外になる()
        {
            Assert.Throws<WavFormatException>(() => WavParser.ParseWav(TestWavFactory.CreateWithoutDataChunk()));
        }

        [Test]
        public void 非PCMは例外になる()
        {
            Assert.Throws<WavFormatException>(() => WavParser.ParseWav(TestWavFactory.CreateNonPcm(frameCount: 100)));
        }

        [Test]
        public void ビット深度が16以外なら例外になる()
        {
            Assert.Throws<WavFormatException>(() => WavParser.ParseWav(TestWavFactory.Create8Bit(frameCount: 100)));
        }

        [Test]
        public void 途中で切れたdataチャンクは例外になる()
        {
            Assert.Throws<WavFormatException>(() => WavParser.ParseWav(TestWavFactory.CreateTruncated(frameCount: 400)));
        }

        [Test]
        public void nullは例外になる()
        {
            Assert.Throws<ArgumentNullException>(() => WavParser.ParseWav((byte[])null));
        }

        [Test]
        public void ヘッダだけから再生時間を求められる()
        {
            var wav = TestWavFactory.Create(frameCount: 2400);
            var header = new byte[WavParser.HeaderProbeSize];
            Array.Copy(wav, header, Math.Min(header.Length, wav.Length));

            Assert.That(WavParser.TryGetDurationSec(header, out var durationSec), Is.True);
            Assert.That(durationSec, Is.EqualTo(2400d / TestWavFactory.DefaultSampleRate).Within(1e-9));
        }

        [Test]
        public void 壊れたヘッダなら再生時間の取得は失敗を返す()
        {
            Assert.That(WavParser.TryGetDurationSec(new byte[] { 1, 2, 3 }, out var durationSec), Is.False);
            Assert.That(durationSec, Is.EqualTo(0d));

            var broken = TestWavFactory.CreateBrokenRiff(frameCount: 10);
            Assert.That(WavParser.TryGetDurationSec(broken, out _), Is.False);
        }

        [Test]
        public void WavDataは不正な値を受け付けない()
        {
            Assert.Throws<ArgumentNullException>(() => new WavData(null, 1, 24000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WavData(new float[4], 0, 24000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WavData(new float[4], 1, 0));
        }
    }
}
