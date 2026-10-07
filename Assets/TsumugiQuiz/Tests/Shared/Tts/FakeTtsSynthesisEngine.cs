using System;
using System.Threading;
using System.Threading.Tasks;
using TsumugiQuiz.Tests.Shared.Core;
using TsumugiQuiz.Tts;

namespace TsumugiQuiz.Tests.Shared.Tts
{
    /// <summary>
    /// ネイティブ DLL を使わない <see cref="ITtsSynthesisEngine"/>。
    /// EditMode（<see cref="TsumugiQuiz.Tts.TtsService"/> のキャッシュ・同意ゲート・取り消しの分岐）と
    /// PlayMode（#23 の同期再生。<c>durationSec</c> を決め打ちできることが必要）の両方から使う（#67）。
    /// </summary>
    internal sealed class FakeTtsSynthesisEngine : ITtsSynthesisEngine
    {
        /// <summary>voicevox_core の出力サンプルレート（docs/tts.md §0）。</summary>
        public const int SampleRate = TestWavFactory.DefaultSampleRate;

        private readonly int _frameCount;
        private int _synthesizeCount;
        private string _lastRequestedText;

        public FakeTtsSynthesisEngine(int frameCount = 240, string coreVersion = "0.17.0-fake")
        {
            if (frameCount <= 0) throw new ArgumentOutOfRangeException(nameof(frameCount));

            _frameCount = frameCount;
            CoreVersion = coreVersion;
            Style = new VoicevoxStyleResolution(8u, "春日部つむぎ", "ノーマル", VoicevoxStyleMatch.Exact);
        }

        /// <summary>返す音声の長さ（秒）。PlayMode の同期再生検証で「ホストの値を正とする」ことの確認に使う。</summary>
        public double DurationSec => (double)_frameCount / SampleRate;

        /// <summary><see cref="SynthesizeAsync"/> が呼ばれた回数（キャッシュが効いているかの確認用）。</summary>
        public int SynthesizeCount => Volatile.Read(ref _synthesizeCount);

        /// <summary>
        /// 直近の <see cref="SynthesizeAsync"/> 呼び出しに渡された <c>text</c>（issue #32。
        /// 問題エディタの読み上げプレビューで、実際に渡された readingText を検証するために使う）。
        /// <see cref="SynthesizeAsync"/> はワーカースレッドから呼ばれうる一方、テスト側は
        /// メインスレッドの <c>WaitUntil</c> ポーリングで読むため、<see cref="SynthesizeCount"/> と同じく
        /// <see cref="Volatile"/> で可視性を揃える（PR #103 レビュー L5）。
        /// </summary>
        public string LastRequestedText
        {
            get => Volatile.Read(ref _lastRequestedText);
            private set => Volatile.Write(ref _lastRequestedText, value);
        }

        /// <summary>破棄されたか。</summary>
        public bool Disposed { get; private set; }

        /// <summary>合成 1 回あたりに挟む待ち時間（取り消しの検証用）。</summary>
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// true なら合成で例外を投げる（docs/tts.md §9 の「その問題だけ読み上げをスキップ」の検証用）。
        /// 型名が "Exception" 単独や ".Exception" で終わらないものを選んでいる
        /// （scripts/verify.ps1 のログ検出に引っかからないようにするため）。
        /// </summary>
        public bool ThrowOnSynthesize { get; set; }

        public VoicevoxStyleResolution Style { get; }

        public string SpeakerName => Style.SpeakerName;

        public string StyleName => Style.StyleName;

        public string CoreVersion { get; }

        public string ModelsVersion => "0.16.4-fake";

        public async Task<byte[]> SynthesizeAsync(string text, float speed, CancellationToken cancellationToken)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            LastRequestedText = text;
            Interlocked.Increment(ref _synthesizeCount);

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (ThrowOnSynthesize)
            {
                throw new InvalidOperationException("合成できません（テスト）。");
            }

            // フレーム数（＝durationSec）は固定のまま、テキストごとに波形の中身だけを変える
            // （#23 の同期再生検証で durationSec を決め打ちできる必要があるため、長さでは変えない）。
            var sampleOffset = text.GetHashCode();
            return TestWavFactory.Create(_frameCount, sampleOffset: sampleOffset);
        }

        public string Describe()
            => $"fake style={SpeakerName}/{StyleName} core={CoreVersion} models={ModelsVersion} " +
               $"frames={_frameCount} ({DurationSec:F3}s)";

        public void Dispose() => Disposed = true;
    }
}
