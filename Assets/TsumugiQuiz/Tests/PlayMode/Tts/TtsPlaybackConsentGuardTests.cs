using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TsumugiQuiz.Tests.Shared.Tts;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.Game;
using TsumugiQuiz.UI.Views.Settings;
using UnityEngine;
using UnityEngine.TestTools;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.Tts
{
    /// <summary>
    /// <b>合成は終わったがまだ鳴っていない</b>音声の扱い（issue #127 レビュー H-2、FR-75）。
    /// </summary>
    /// <remarks>
    /// 出題ごとの同意確認（<c>IsReadingPossible</c> → <c>PrepareAsync</c>）だけでは、
    /// 合成完了から再生開始までの隙間（Ready 待ち最大 <c>tts.readyTimeoutMs</c> ＋ <c>tts.leadTimeSec</c>）に
    /// 撤回された場合に音が鳴ってしまう。<c>TtsSyncPlayer.StartPlayback</c> の入口でも同意を確認し、
    /// <b>撤回済みなら再生を始めない</b>（既に鳴っている音は止めない）ことをここで固定する。
    ///
    /// ネットワークは使わず、<c>TtsSyncPlayer</c> を直接駆動して隙間を決定的に作る
    /// （<c>SetDspClock</c> で時刻を固定し、<c>Schedule</c> を手で呼ぶ）。
    /// </remarks>
    public sealed class TtsPlaybackConsentGuardTests
    {
        private const string ReadingText = "にほんのしゅとはどこ";

        /// <summary>テスト中に固定する <c>dspTime</c>（秒）。値そのものに意味は無い。</summary>
        private const double FixedDspTime = 100d;

        private ConsentFileScope _consentScope;
        private ReadingPlaybackTestRig _rig;
        private GameObject _playerObject;

        [SetUp]
        public void SeedConsentAndCreateRig()
        {
            _consentScope = ConsentFileScope.Backup();
            ConsentGate.CreateDefaultStore()
                .RecordConsent(TermsCatalog.LoadRequiredTerms(), Application.version, DateTime.UtcNow);

            _rig = new ReadingPlaybackTestRig();
        }

        [TearDown]
        public void DisposeRigAndRestoreConsent()
        {
            if (_playerObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_playerObject);
                _playerObject = null;
            }

            _rig?.Dispose();
            _rig = null;

            _consentScope?.Restore();
            _consentScope = null;
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator 合成後再生前に撤回すると再生を始めない()
        {
            var engine = new FakeTtsSynthesisEngine();
            var service = _rig.CreateService(
                "TtsService(PlaybackGuard)", (_, __) => engine, TtsConsentCheckFactory.Build());

            var player = CreatePlayer();
            player.SetService(service);
            player.SetDspClock(() => FixedDspTime);

            // 本番と同じ配線（GameView がセッション取得時に呼ぶもの）。
            GameView.WireTtsSyncPlayer(player);

            yield return WaitUntilOrFail(
                () => service.Status.IsReady, () => $"読み上げの初期化が終わりませんでした（{service.Status}）。");

            // --- 同意済みのうちに 1 問目を合成しておく ---
            var prepare = player.PrepareAsync(0, ReadingText, 1.0f, CancellationToken.None);
            yield return WaitUntilOrFail(
                () => prepare.IsCompleted, () => "合成（PrepareAsync）が完了しませんでした。");
            AssertTaskSucceeded(prepare);

            Assert.Greater(prepare.Result, 0d, "同意済みなので合成できるはず。");
            Assert.AreEqual(1, engine.SynthesizeCount, "合成は 1 回。");
            Assert.AreEqual(0, player.PreparedQuestionIndex, "1 問目の音声を用意済み。");

            var started = new List<int>();
            var completed = new List<int>();
            player.ReadingStarted += started.Add;
            player.ReadingCompleted += completed.Add;

            // --- ここで撤回（Ready 待ち〜leadTime の隙間に相当） ---
            _consentScope.DeleteCurrentFile();

            // --- サーバーからの再生開始指示（0.3 秒後 ＝ 既定の tts.leadTimeSec 相当） ---
            player.Schedule(
                questionIndex: 0, playAtServerTime: 10.3d, referenceServerTimeNow: 10.0d, hostDurationSec: 0.01d);

            Assert.IsFalse(
                player.IsReadingScheduled,
                "撤回後は合成済みでも再生を始めない（#127 レビュー H-2、FR-75）。");
            CollectionAssert.IsEmpty(started, "再生していないので ReadingStarted は発火しない。");
            CollectionAssert.AreEqual(
                new[] { 0 }, completed,
                "購読者（立ち絵・UI）が待ち続けないよう、完了だけは通知する（CancelReading と同じ扱い）。");
            Assert.AreEqual(
                -1, player.PreparedQuestionIndex, "用意していた AudioClip は解放されているはず（docs/tts.md §6.3）。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator 同意が続いていれば合成済みの音声は予定どおり再生される()
        {
            // 上のテストの対照。ガードが「常に再生しない」になっていないことを確かめる。
            var engine = new FakeTtsSynthesisEngine();
            var service = _rig.CreateService(
                "TtsService(PlaybackGuard)", (_, __) => engine, TtsConsentCheckFactory.Build());

            var player = CreatePlayer();
            player.SetService(service);
            player.SetDspClock(() => FixedDspTime);
            GameView.WireTtsSyncPlayer(player);

            yield return WaitUntilOrFail(
                () => service.Status.IsReady, () => $"読み上げの初期化が終わりませんでした（{service.Status}）。");

            var prepare = player.PrepareAsync(0, ReadingText, 1.0f, CancellationToken.None);
            yield return WaitUntilOrFail(
                () => prepare.IsCompleted, () => "合成（PrepareAsync）が完了しませんでした。");
            AssertTaskSucceeded(prepare);

            var started = new List<int>();
            player.ReadingStarted += started.Add;

            player.Schedule(
                questionIndex: 0, playAtServerTime: 10.3d, referenceServerTimeNow: 10.0d, hostDurationSec: 0.01d);

            Assert.IsTrue(player.IsReadingScheduled, "同意が続いていれば予約されるはず。");
            CollectionAssert.AreEqual(new[] { 0 }, started, "ReadingStarted が発火するはず。");
        }

        private TtsSyncPlayer CreatePlayer()
        {
            _playerObject = new GameObject(nameof(TtsPlaybackConsentGuardTests));
            return _playerObject.AddComponent<TtsSyncPlayer>();
        }

        private static void AssertTaskSucceeded(Task task)
        {
            Assert.IsFalse(task.IsFaulted, $"合成が例外で終わりました: {task.Exception}");
            Assert.IsFalse(task.IsCanceled, "合成が取り消されました。");
        }

        /// <summary>実時間ベースの待機（<c>MainSceneTestHelpers.WaitUntil</c> と同じ作法）。</summary>
        private static IEnumerator WaitUntilOrFail(Func<bool> condition, Func<string> failureMessage)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + DefaultTimeoutSeconds;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }

            if (!condition())
            {
                Assert.Fail(failureMessage());
            }
        }
    }
}
