using System;
using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.Tts;
using TsumugiQuiz.Tts.Native;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Tts
{
    /// <summary>
    /// 実 DLL で合成した音声を使った同期再生の通し検証（docs/tts.md §6 / §11.2、#23）。
    /// External / 配置物が無い環境では <c>Assert.Ignore</c> でスキップする。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>クラス名を Voicevox で始め、さらに <c>VoicevoxTtsService…</c> より後に来る名前にしているのは意図的</b>。
    /// フィクスチャの実行順はクラス名の辞書順で、<c>VoicevoxDllSearchPathTests</c>
    /// （ONNX Runtime のロード手段を測るためプロセス内で最初に走る必要がある、docs/tts.md §6.5）
    /// より後に回す必要があるため。
    /// </para>
    /// <para>
    /// 名前順に頼るだけでは不十分なので、<b>ONNX Runtime がまだロードされていなければスキップする</b>
    /// （本テストが最初のロードを奪うと <c>VoicevoxDllSearchPathTests</c> の実測ができなくなる）。
    /// 単体で走らせたい場合は <c>VoicevoxDllSearchPathTests</c> / <c>VoicevoxNativeTests</c> と
    /// 一緒にフィルタへ含めること。
    /// </para>
    /// <para>
    /// フェイクエンジンを使う網羅的な検証は <c>TtsSyncPlaybackTests</c>（ネイティブを読み込まない）にある。
    /// </para>
    /// </remarks>
    public sealed class VoicevoxTtsSyncPlaybackTests : GameSessionTestFixture
    {
        private const string ReadingText = "にほんのしゅとはどこ";

        /// <summary>実合成（辞書とモデルの読み込みを含む）を待つ上限（秒）。</summary>
        private const float SynthesisTimeoutSec = 180f;

        private ReadingPlaybackTestRig _rig;

        [SetUp]
        public void CreateRig() => _rig = new ReadingPlaybackTestRig();

        [TearDown]
        public void DisposeRig()
        {
            _rig?.Dispose();
            _rig = null;
        }

        [UnityTest]
        [Order(100)]
        public IEnumerator 実合成でもホストとクライアントの再生予約がそろう()
        {
            if (!VoicevoxTestFixture.IsAvailable)
            {
                Assert.Ignore(VoicevoxTestFixture.SkipReason);
            }

            if (VoicevoxSynthesizer.OnnxruntimeLoadStrategy == NativeLoadStrategy.None)
            {
                // ONNX Runtime の最初のロードは VoicevoxDllSearchPathTests が実測する（docs/tts.md §6.5）。
                // 本テストがそれを奪わないよう、まだロードされていなければ実行しない。
                Assert.Ignore(
                    "ONNX Runtime がまだロードされていないためスキップします"
                    + "（VoicevoxDllSearchPathTests が最初のロードを実測するため。単体実行時は同テストも含めてください）。");
            }

            yield return ConnectHostAndClient(CreateQuestionSource());

            // 実 DLL・実モデルを使う（engineFactory を渡さない）。ホストとクライアントで 1 つを共有する
            // （同じプロセスで voicevox_core を 2 重に初期化しないため）。
            var service = _rig.CreateService("TtsService(Voicevox)");
            var hostPlayer = ReadingPlaybackTestRig.AttachPlayer(HostSession, service);
            var clientPlayer = ReadingPlaybackTestRig.AttachPlayer(ClientSession, service);
            var hostCoordinator = ReadingPlaybackTestRig.GetCoordinator(HostSession);
            var clientCoordinator = ReadingPlaybackTestRig.GetCoordinator(ClientSession);

            // 初回は辞書とモデルの読み込みで数秒〜数十秒かかるので、Ready 待ちの上限を広げておく。
            hostCoordinator.ReadyTimeoutSec = TtsSyncCoordinator.MaxReadyTimeoutSec;

            yield return WaitUntilWithTimeout(
                () => service.Status.State != TtsServiceState.Initializing
                      && service.Status.State != TtsServiceState.NotInitialized,
                SynthesisTimeoutSec,
                () => $"読み上げの初期化が終わりませんでした（{service.Status}）。");

            if (!service.Status.IsReady)
            {
                Assert.Ignore($"読み上げを利用できない環境のためスキップします（{service.Status}）。");
            }

            // 出題前に 1 度合成してキャッシュに入れておく（Ready 待ちの時間を実運用と同じ「キャッシュ命中」に近づける）。
            var prefetch = service.PrefetchAsync(new[] { ReadingText });
            yield return WaitUntilWithTimeout(
                () => prefetch.IsCompleted,
                SynthesisTimeoutSec,
                () => "事前合成が終わりませんでした。");
            Assert.IsFalse(prefetch.IsFaulted, "事前合成で例外が出てはいけない。");

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntilWithTimeout(
                () => hostPlayer.IsReadingScheduled && clientPlayer.IsReadingScheduled,
                SynthesisTimeoutSec,
                () => "ホスト・クライアントの両方で読み上げが予約されませんでした"
                      + $"（host: {hostPlayer.IsReadingScheduled}、client: {clientPlayer.IsReadingScheduled}）。");

            Assert.Greater(hostCoordinator.LastDurationSec, 0d, "実合成の読み上げ時間は 0 より大きいはず。");
            Assert.AreEqual(
                hostCoordinator.LastDurationSec, clientCoordinator.LastDurationSec, 1e-9,
                "読み上げ時間はホストの値を全員に配る（docs/tts.md §6.2）。");
            Assert.AreEqual(
                hostCoordinator.LastPlayAtServerTime, clientCoordinator.LastPlayAtServerTime, 1e-9,
                "再生開始時刻はサーバーが決めた 1 つの値。");

            Debug.Log(
                $"[VoicevoxTtsSyncPlaybackTests] 実合成 {hostCoordinator.LastDurationSec:F3}s "
                + $"予約 dsp host={hostPlayer.LastSchedule.DspStartTime:F4} "
                + $"client={clientPlayer.LastSchedule.DspStartTime:F4}");

            var playAtServerTime = hostCoordinator.LastPlayAtServerTime;
            yield return WaitUntilWithTimeout(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                SynthesisTimeoutSec,
                () => $"早押し受付が開きませんでした（現在: {ClientSession.Phase.Value}）。");

            Assert.AreEqual(
                playAtServerTime, HostSession.BuzzOpenServerTime.Value, 1e-6,
                "T0 = playAtServerTime（buzz.allowDuringReading = true）。");
        }

        private static IQuestionSource CreateQuestionSource()
        {
            return new TestQuestionSource(
                TestQuestionSource.FreeText(
                    "q-voicevox-1", "日本の首都はどこ？", ReadingText, new[] { "地理" }, 2, "とうきょう"));
        }

        private static IEnumerator WaitUntilWithTimeout(Func<bool> condition, float timeoutSec, Func<string> message)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < timeoutSec)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsTrue(condition(), message());
        }
    }
}
