using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TsumugiQuiz.Tts;
using TsumugiQuiz.Tts.Native;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace TsumugiQuiz.Tests.PlayMode.Tts
{
    /// <summary>
    /// 実 DLL を使った初期化 → モデル読み込み → 合成 → 解放の通し検証（docs/tts.md §11.2）。
    /// External / 配置物が無い環境では Assert.Ignore でスキップする。
    /// </summary>
    public sealed class VoicevoxNativeTests
    {
        private const int WavHeaderSize = 44;

        [Test]
        public void 初期化からこんにちはの合成までが通る()
        {
            SkipIfUnavailable();

            var location = VoicevoxTestFixture.Location;
            Debug.Log($"[voicevox] {location.Describe()}");

            var stopwatch = Stopwatch.StartNew();
            using (var synthesizer = VoicevoxSynthesizer.Create(location))
            {
                var initializedMs = stopwatch.ElapsedMilliseconds;
                Debug.Log($"[voicevox] core={synthesizer.CoreVersion} gpu={synthesizer.IsGpuMode} " +
                          $"onnxruntimeLoad={VoicevoxSynthesizer.OnnxruntimeLoadStrategy} " +
                          $"preload={NativeLibraryLoader.LastStrategy} init={initializedMs}ms");

                Assert.That(synthesizer.CoreVersion, Is.Not.Empty, "voicevox_get_version が取得できること");
                Assert.That(synthesizer.LoadedModelFiles, Is.Not.Empty, ".vvm が 1 つ以上読み込まれていること");

                var metasJson = synthesizer.CreateMetasJson();
                Assert.That(metasJson, Does.Contain(VoicevoxStyleResolver.DefaultSpeakerName),
                    "メタ情報に春日部つむぎが含まれること");

                var style = synthesizer.ResolveStyle();
                Debug.Log($"[voicevox] style={style.SpeakerName}/{style.StyleName} id={style.StyleId} match={style.Match}");
                Assert.That(style.Match, Is.EqualTo(VoicevoxStyleMatch.Exact),
                    "春日部つむぎ・ノーマルがそのまま解決できること");

                var synthesisStart = stopwatch.ElapsedMilliseconds;
                var wav = synthesizer.Tts("こんにちは", style.StyleId);
                var synthesisMs = stopwatch.ElapsedMilliseconds - synthesisStart;

                var sampleRate = ReadSampleRate(wav);
                Debug.Log($"[voicevox] wav={wav.Length}B sampleRate={sampleRate}Hz 合成={synthesisMs}ms 合計={stopwatch.ElapsedMilliseconds}ms");

                AssertIsWav(wav);
                Assert.That(sampleRate, Is.GreaterThan(0), "サンプルレートが取得できること");
            }

            Assert.That(stopwatch.ElapsedMilliseconds, Is.GreaterThan(0));
        }

        /// <summary>
        /// 2 段系統（create_audio_query → synthesis）。読み上げ速度を変えるにはこちらが必須
        /// （VoicevoxTtsOptions に速度パラメータが無いため。docs/tts-native-api.md §1.2、docs/tts.md §6.4）。
        /// </summary>
        [Test]
        public void AudioQueryを経由した2段合成で速度を変えられる()
        {
            SkipIfUnavailable();

            using (var synthesizer = VoicevoxSynthesizer.Create(VoicevoxTestFixture.Location))
            {
                var styleId = synthesizer.ResolveStyle().StyleId;

                var query = synthesizer.CreateAudioQuery("こんにちは", styleId);
                Assert.That(query, Does.Contain("speedScale"), "AudioQuery に速度パラメータが含まれること");

                var normal = synthesizer.Synthesis(query, styleId);
                AssertIsWav(normal);

                var fastQuery = Regex.Replace(query, "\"speedScale\"\\s*:\\s*[0-9.]+", "\"speedScale\":1.5");
                Assert.That(fastQuery, Is.Not.EqualTo(query), "speedScale を書き換えられること");

                var fast = synthesizer.Synthesis(fastQuery, styleId);
                AssertIsWav(fast);

                Debug.Log($"[voicevox] 2 段合成: speed=1.0 -> {normal.Length}B / speed=1.5 -> {fast.Length}B");
                Assert.That(fast.Length, Is.LessThan(normal.Length), "速度を上げた wav は短くなること");
            }
        }

        /// <summary>
        /// ヘッダの voicevox_synthesizer_delete は「破棄対象への他スレッドでのアクセスが存在する場合、
        /// それらがすべて終わるのを待ってから破棄する」仕様。ラッパ側もロックで直列化しているので、
        /// 合成中に Dispose を呼んでも合成は完走し、Dispose はその完了を待つ。
        /// </summary>
        [Test]
        public void 合成中のDisposeは合成の完了を待つ()
        {
            SkipIfUnavailable();

            var synthesizer = VoicevoxSynthesizer.Create(VoicevoxTestFixture.Location);
            try
            {
                var styleId = synthesizer.ResolveStyle().StyleId;
                synthesizer.Tts("こんにちは", styleId); // ウォームアップ（初回は推論の初期化で時間が読めない）

                var longText = string.Concat(Enumerable.Repeat("これは読み上げの途中で破棄しても壊れないことを確かめる長い文章です。", 4));
                var entered = new ManualResetEventSlim(false);
                byte[] wav = null;
                Exception error = null;

                var task = Task.Run(() =>
                {
                    entered.Set();
                    try
                    {
                        wav = synthesizer.Tts(longText, styleId);
                    }
                    catch (Exception e)
                    {
                        error = e;
                    }
                });

                Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True, "合成スレッドが起動すること");
                Thread.Sleep(300); // 合成がロックを取るのを待つ

                var stopwatch = Stopwatch.StartNew();
                synthesizer.Dispose();
                var disposeMs = stopwatch.ElapsedMilliseconds;

                Assert.That(task.Wait(TimeSpan.FromSeconds(60)), Is.True, "合成が完了すること");
                // ログに "error" の語を入れると scripts/common.ps1 の Test-LogHasErrors が誤検知するので避ける。
                Debug.Log($"[voicevox] Dispose 待ち={disposeMs}ms wav={(wav?.Length ?? 0)}B 例外={error?.GetType().Name ?? "(なし)"}");

                Assert.That(error, Is.Null, "Dispose が合成を中断しないこと");
                Assert.That(wav, Is.Not.Null);
                Assert.That(wav.Length, Is.GreaterThan(WavHeaderSize));
                Assert.That(disposeMs, Is.GreaterThan(0), "Dispose が合成の完了を待つこと");

                Assert.Throws<ObjectDisposedException>(() => synthesizer.Tts("こんにちは", styleId),
                    "破棄後の呼び出しは例外になること");
                Assert.DoesNotThrow(() => synthesizer.Dispose(), "2 回目の Dispose は何もしないこと");
            }
            finally
            {
                synthesizer.Dispose();
            }
        }

        private static void SkipIfUnavailable()
        {
            if (!VoicevoxTestFixture.IsAvailable)
            {
                Assert.Ignore(VoicevoxTestFixture.SkipReason);
            }
        }

        private static void AssertIsWav(byte[] wav)
        {
            Assert.That(wav.Length, Is.GreaterThan(WavHeaderSize), "wav が RIFF ヘッダより長いこと");
            Assert.That(Ascii(wav, 0, 4), Is.EqualTo("RIFF"), "RIFF ヘッダであること");
            Assert.That(Ascii(wav, 8, 4), Is.EqualTo("WAVE"), "WAVE フォーマットであること");
        }

        /// <summary>wav ヘッダの fmt チャンクからサンプルレート（オフセット 24、リトルエンディアン）を読む。</summary>
        private static int ReadSampleRate(byte[] wav)
        {
            if (wav == null || wav.Length < WavHeaderSize) return 0;
            return BitConverter.ToInt32(wav, 24);
        }

        private static string Ascii(byte[] bytes, int offset, int count)
            => bytes.Length < offset + count ? string.Empty : Encoding.ASCII.GetString(bytes, offset, count);
    }
}
