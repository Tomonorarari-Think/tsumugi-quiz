using NUnit.Framework;
using TsumugiQuiz.Tts;
using TsumugiQuiz.Tts.Native;
using UnityEngine;

namespace TsumugiQuiz.Tests.PlayMode.Tts
{
    /// <summary>
    /// DLL 検索パスの実測（docs/tts-native-api.md §3.3）。
    ///
    /// voicevox_onnxruntime_load_once に絶対パスを渡す対策（§3.1）だけで解決できるかを確かめる。
    ///
    /// <b>`voicevox_onnxruntime_load_once` は冪等で、一度成功すると以後は引数を無視して同じ参照を返す</b>
    /// （ヘッダのコメント）。つまり「どの手段でロードされたか」を測れるのはプロセス内で最初の 1 回だけ。
    /// そのため <see cref="OrderAttribute"/> を付けて先に走らせる
    /// （フィクスチャ間の実行順はクラス名のアルファベット順で、本クラスは VoicevoxNativeTests より前に来る）。
    /// それでも既にロード済みだった場合は測定不能としてスキップする。
    /// </summary>
    public sealed class VoicevoxDllSearchPathTests
    {
        [Test]
        [Order(1)]
        public void 絶対パス指定だけでONNXRuntimeがロードできる()
        {
            if (!VoicevoxTestFixture.IsAvailable)
            {
                Assert.Ignore(VoicevoxTestFixture.SkipReason);
            }
            if (VoicevoxSynthesizer.OnnxruntimeLoadStrategy != NativeLoadStrategy.None)
            {
                Assert.Ignore("ONNX Runtime が既にロード済みのため、検索パスの測定はできません。");
            }

            // SetDllDirectory も事前ロードも行わず、絶対パス指定だけで初期化する。
            using (var synthesizer = VoicevoxSynthesizer.Create(VoicevoxTestFixture.Location, prepareNativeSearchPath: false))
            {
                Debug.Log($"[voicevox] 絶対パス指定のみ: onnxruntimeLoad={VoicevoxSynthesizer.OnnxruntimeLoadStrategy} " +
                          $"preload={NativeLibraryLoader.LastStrategy} core={synthesizer.CoreVersion}");

                Assert.That(VoicevoxSynthesizer.OnnxruntimeLoadStrategy, Is.EqualTo(NativeLoadStrategy.PreloadAbsolutePath),
                    "絶対パス指定（§3.1）だけでロードできること");
                Assert.That(NativeLibraryLoader.LastStrategy, Is.EqualTo(NativeLoadStrategy.None),
                    "SetDllDirectory / 事前ロード（§3.3）を使っていないこと");
                Assert.That(synthesizer.CoreVersion, Is.Not.Empty);
            }
        }
    }
}
