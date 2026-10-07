using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tests.Shared.Room;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.Game;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI.Game
{
    /// <summary>
    /// issue #101: Game View を表示したまま <see cref="NetworkService"/> が破棄された場合の挙動。
    ///
    /// <para>
    /// 修正前は、<c>GameView.Tick</c>（セッションの再探索、100ms 間隔）が破棄済みの
    /// <see cref="NetworkService"/> に対して <c>FindActiveGameSession()</c> を呼び、
    /// <see cref="ObjectDisposedException"/> を毎 Tick 投げていた。PlayMode テストでは
    /// 後片付け（<c>NetworkBootstrap</c> の破棄）の後も Main シーンが残っているテストがあり、
    /// この例外が後続テストのセットアップ中に降ってきて 26〜27 件がまとめて失敗することがあった。
    /// 本番でも、ホストを止めた・アプリを終了した後に Game View が残っていると毎フレーム例外になる。
    /// </para>
    /// </summary>
    public class GameViewDisposedServiceTests
    {
        private ConsentFileScope _consentScope;

        /// <summary>
        /// ホスト開始時に <c>RoomSettingsSync</c> が読む下書き（<c>room.lastApplied</c>）の隔離
        /// （<c>GameViewSceneTests</c> と同じ作法）。本テストはホストを立てないが、Boot → Main の経路で
        /// 実行マシンの <c>app-settings.json</c> に触らせないために揃えておく。
        /// </summary>
        private RoomSettingsDraftScope _roomSettingsDraftScope;

        /// <summary>
        /// <see cref="StartRecordingDisposedServiceLogs"/> 以降に届いた「破棄済み
        /// <see cref="NetworkService"/> に触った」ログ（#101 の回帰対象）。
        /// それ以外の想定外エラーの検出は Unity Test Framework 既定の <c>LogAssert</c> に任せるため、
        /// ここでは対象を絞って記録し、失敗時に原因が一目で分かるメッセージを組み立てるだけにする。
        /// </summary>
        private readonly List<string> _disposedServiceLogs = new();

        /// <summary>
        /// <see cref="Application.logMessageReceived"/> はメインスレッド以外からも発火しうるため、
        /// <see cref="_disposedServiceLogs"/> の読み書きを直列化する（レビュー L-5）。
        /// </summary>
        private readonly object _disposedServiceLogsLock = new();

        private bool _recordingDisposedServiceLogs;

        [SetUp]
        public void SeedConsentedState()
        {
            _consentScope = ConsentFileScope.Backup();
            _roomSettingsDraftScope = RoomSettingsDraftScope.Redirect();

            var store = ConsentGate.CreateDefaultStore();
            var requiredTerms = TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, DateTime.UtcNow);
        }

        [TearDown]
        public void RestoreConsentFile()
        {
            StopRecordingDisposedServiceLogs();
            _consentScope?.Restore();
            _consentScope = null;
            _roomSettingsDraftScope?.Restore();
            _roomSettingsDraftScope = null;
        }

        private void StartRecordingDisposedServiceLogs()
        {
            lock (_disposedServiceLogsLock)
            {
                _disposedServiceLogs.Clear();
            }

            if (!_recordingDisposedServiceLogs)
            {
                Application.logMessageReceived += HandleLogMessage;
                _recordingDisposedServiceLogs = true;
            }
        }

        private void StopRecordingDisposedServiceLogs()
        {
            if (_recordingDisposedServiceLogs)
            {
                Application.logMessageReceived -= HandleLogMessage;
                _recordingDisposedServiceLogs = false;
            }
        }

        private void HandleLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type is not (LogType.Error or LogType.Exception or LogType.Assert))
            {
                return;
            }

            // #101 の症状（ObjectDisposedException: NetworkService）だけを拾う。
            var message = condition ?? string.Empty;
            if (!message.Contains(nameof(ObjectDisposedException)) || !message.Contains(nameof(NetworkService)))
            {
                return;
            }

            lock (_disposedServiceLogsLock)
            {
                _disposedServiceLogs.Add($"[{type}] {message}{Environment.NewLine}{stackTrace}");
            }
        }

        private void AssertNoDisposedServiceLogs(string because)
        {
            StopRecordingDisposedServiceLogs();

            string[] captured;
            lock (_disposedServiceLogsLock)
            {
                captured = _disposedServiceLogs.ToArray();
            }

            Assert.IsEmpty(captured, $"{because}{Environment.NewLine}{string.Join(Environment.NewLine, captured)}");
        }

        [UnityTearDown]
        public IEnumerator TearDownScene()
        {
            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();

            TearDownMainSceneAndBootstrapSingletons();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator GameView_WhenNetworkServiceDisposedWhileShown_StopsTickWithoutException()
        {
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(r => panelRoot = r);

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Game);

            // 画面が組み上がるのを待つ（ホストは立てないので、セッションは見つからないまま再探索が続く状態）。
            Label phaseLabel = null;
            yield return WaitForElement<Label>(panelRoot, "phase-label", found => phaseLabel = found);

            var gameView = router.CurrentController as GameView;
            Assert.IsNotNull(gameView, "表示中のコントローラが GameView であるはず。");
            Assert.IsTrue(gameView.IsTickScheduled, "Game View 表示中はセッション再探索の Tick が動いているはず。");

            var service = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.Service : null;
            Assert.IsNotNull(service, "Boot シーン経由なので NetworkService が常駐しているはず。");

            // アプリ終了・シーンアンロードで NetworkBootstrap ごと破棄されたのと同じ状態を作る。
            StartRecordingDisposedServiceLogs();
            service.Dispose();
            Assert.IsTrue(service.IsDisposed, "Dispose 後は IsDisposed が true になるはず。");

            // 修正前はここで Tick が走るたびに ObjectDisposedException が出ていた
            // （FindActiveGameSession 自体の戻り値は NetworkServiceLifecycleTests で確認する）。
            yield return WaitUntil(
                () => !gameView.IsTickScheduled,
                DefaultTimeoutSeconds,
                "NetworkService の破棄後も Game View の Tick が止まりませんでした。",
                panelRoot);

            // #116: 「セッションを探しています…」のまま固まらず、ユーザー向け文言に差し替わること。
            // リテラルを二重管理しないよう GameView 側の internal const をそのまま参照する（レビュー L-5）。
            Assert.AreEqual(
                GameView.ConnectionEndedMessage,
                phaseLabel.text,
                "NetworkService の破棄検出後は「接続が終了しました」に差し替わるはず。");

            // 止まった後も例外が出ないことを、Tick 間隔（100ms）を十分に跨ぐ時間で確認する。
            var deadline = Time.realtimeSinceStartupAsDouble + 1.0;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }

            AssertNoDisposedServiceLogs("NetworkService の破棄後に ObjectDisposedException: NetworkService のログが出ました（#101）。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TearDownMainSceneAndBootstrapSingletons_StopsGameViewTickBeforeDisposingService()
        {
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(r => panelRoot = r);

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Game);
            yield return WaitForElement<Label>(panelRoot, "phase-label", _ => { });

            var gameView = router.CurrentController as GameView;
            Assert.IsNotNull(gameView, "表示中のコントローラが GameView であるはず。");
            Assert.IsTrue(gameView.IsTickScheduled, "Game View 表示中はセッション再探索の Tick が動いているはず。");

            // テストの後片付けと同じ手順。View（ViewRouter）を先に畳んでから NetworkService を破棄する。
            StartRecordingDisposedServiceLogs();
            TearDownMainSceneAndBootstrapSingletons();

            Assert.IsFalse(
                gameView.IsTickScheduled,
                "TearDownMainSceneAndBootstrapSingletons は ViewRouter の破棄（OnHide）を通して Tick を止めるはず。");
            Assert.IsNull(NetworkBootstrap.Instance, "NetworkBootstrap も破棄されているはず。");

            // 破棄後に数フレーム回しても、残ったコールバックが破棄済みサービスを触らないこと。
            for (var i = 0; i < 30; i++)
            {
                yield return null;
            }

            AssertNoDisposedServiceLogs("後片付けの後に ObjectDisposedException: NetworkService のログが出ました（#101）。");
        }
    }
}
