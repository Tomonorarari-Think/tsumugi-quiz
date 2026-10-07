using System;
using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.Tests.Shared.Room;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// アプリ起動時（Boot → Main）に UI 層が <see cref="TtsService"/> へ同意ゲートを登録していることを、
    /// <b>ロビーで <c>GameSession</c> がスポーンする実経路</b>で固定する（issue #127 レビュー H-1、FR-74 / NFR-08）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 読み上げの初期化を最初に始めるのは <c>GameView</c> ではなく、ロビーで <c>GameSession</c> が
    /// スポーンした時点の <c>TtsSyncCoordinator.OnNetworkSpawn</c> → <c>TtsSyncPlayer.InitializeAsync</c> で、
    /// これは <c>GameView.WireTtsSyncPlayer</c> より前に走る。<c>Network</c> / <c>Tts</c> 層から
    /// <c>UI</c> 層は参照できないため、<c>ViewRouter.Awake</c> →
    /// <c>DefaultViewControllerRegistrations.ConfigureTtsConsentGate</c> の登録が無いと、
    /// <b>未同意・撤回済みでも voicevox_core（ONNX Runtime）のロードが走ってしまう</b>。
    /// このクラスはその 1 点だけを、実シーン・実プレハブで確かめる。
    /// </para>
    /// <para>
    /// 判定には <see cref="TtsService.Status"/> の状態を使う。<c>NotInitialized</c> のままなら
    /// 「初期化を始めていない」ことの確実な証拠になる（<c>Initialize</c> は同期的に
    /// <c>Initializing</c> へ移すため、始まっていれば必ず値が変わる）。
    /// </para>
    /// </remarks>
    public sealed class TtsConsentGateBootstrapTests
    {
        private ConsentFileScope _consentScope;
        private RoomSettingsDraftScope _roomSettingsDraftScope;

        private GameObject _hostObject;
        private NetworkService _hostService;

        [SetUp]
        public void BackupConsentAndRoomSettings()
        {
            _consentScope = ConsentFileScope.Backup();
            _roomSettingsDraftScope = RoomSettingsDraftScope.Redirect();
        }

        [UnityTearDown]
        public IEnumerator TearDownSceneAndNetwork()
        {
            // NGO の Shutdown() はフレーム終端で実処理が走るため、停止完了を待ってから破棄する
            // （GameViewSceneTests.TearDownSceneAndNetwork と同じ理由・同じ手順）。
            var bootService = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.Service : null;
            bootService?.Stop();
            _hostService?.Stop();

            var deadline = Time.realtimeSinceStartupAsDouble + DefaultTimeoutSeconds;
            while ((IsNetworkBusy(bootService) || IsNetworkBusy(_hostService))
                   && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }

            yield return null;

            _hostService?.Dispose();
            _hostService = null;

            if (_hostObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostObject);
                _hostObject = null;
            }

            // #108 レビュー R-2: 生の UnloadSceneAsync はロード済みシーンが Main 1 枚だけのとき
            // 拒否されて何もしない（Unloading the last loaded scene ... の警告）。共通ヘルパーを使う。
            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();

            TearDownMainSceneAndBootstrapSingletons();

            _consentScope?.Restore();
            _consentScope = null;
            _roomSettingsDraftScope?.Restore();
            _roomSettingsDraftScope = null;
        }

        /// <summary>
        /// 未同意のまま <c>GameSession</c> がスポーンしても、voicevox_core の初期化を始めないこと。
        /// この配線が無いと <c>TtsSyncPlayer._consentCheck</c> は null（＝制限しない）のままで、
        /// 未同意でも <c>EnsureInitializedAsync</c> が走ってしまう（#127 レビュー H-1 の症状）。
        /// </summary>
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator 未同意ならロビーのスポーンでも読み上げを初期化しない()
        {
            _consentScope.DeleteCurrentFile();

            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);
            Assert.IsNotNull(panelRoot, "Main シーンの UI ルートが取れるはず。");

            var service = TtsService.Instance;
            Assert.IsNotNull(service, "Boot シーンに TtsService が常駐しているはず。");
            Assert.IsFalse(
                service.IsConsentSatisfied,
                "アプリ起動時に UI 層が同意確認を登録しているはず（ConfigureTtsConsentGate、#127 H-1）。");
            Assert.AreEqual(
                TtsServiceState.NotInitialized, service.Status.State, "起動しただけでは初期化を始めない（#25 H-5）。");

            // ロビー相当: GameSession をスポーンさせ、TtsSyncCoordinator.OnNetworkSpawn を通す。
            StartInProcessHost();

            yield return WaitFrames(60);

            Assert.AreEqual(
                TtsServiceState.NotInitialized, service.Status.State,
                "未同意のまま voicevox_core（ONNX Runtime）の初期化を始めてはいけない（FR-74）。");
        }

        /// <summary>
        /// 同意済みなら従来どおりロビーのスポーンで初期化が始まること（同意ゲートが過剰に塞いでいないことの対照）。
        /// External 未配置の環境では最終的に <c>NotAvailable</c> になるが、本テストは
        /// 「<c>NotInitialized</c> から動いた ＝ 初期化を始めた」ことだけを見る。
        /// </summary>
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator 同意済みならロビーのスポーンで読み上げの初期化が始まる()
        {
            ConsentGate.CreateDefaultStore()
                .RecordConsent(TermsCatalog.LoadRequiredTerms(), Application.version, DateTime.UtcNow);

            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);
            Assert.IsNotNull(panelRoot, "Main シーンの UI ルートが取れるはず。");

            var service = TtsService.Instance;
            Assert.IsNotNull(service, "Boot シーンに TtsService が常駐しているはず。");
            Assert.IsTrue(service.IsConsentSatisfied, "同意済みなら同意ゲートは通る。");

            StartInProcessHost();

            var deadline = Time.realtimeSinceStartupAsDouble + DefaultTimeoutSeconds;
            while (service.Status.State == TtsServiceState.NotInitialized
                   && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }

            Assert.AreNotEqual(
                TtsServiceState.NotInitialized, service.Status.State,
                "同意済みならロビーのスポーンで読み上げの初期化が始まるはず（docs/tts.md §6.5）。");
        }

        /// <summary>
        /// 1 プロセス内にもう 1 つ <see cref="NetworkManager"/> を立ててホストを開始し、
        /// 実プレハブの <c>GameSession</c>（＝ <c>TtsSyncCoordinator</c> / <c>TtsSyncPlayer</c> を含む）を
        /// スポーンさせる（<c>GameViewSceneTests.StartInProcessHost</c> と同じ手順）。
        /// </summary>
        private void StartInProcessHost()
        {
            _hostObject = new GameObject(nameof(TtsConsentGateBootstrapTests) + "-Host");
            _hostObject.SetActive(false);

            var transport = _hostObject.AddComponent<UnityTransport>();
            transport.MaxPayloadSize = NetworkConstants.MaxPayloadSizeBytes;

            var manager = _hostObject.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = null,
            };

            manager.AddNetworkPrefab(NetworkTestPrefabs.LoadGameSession());
            manager.AddNetworkPrefab(NetworkTestPrefabs.LoadLobbyState());

            _hostObject.SetActive(true);

            _hostService = new NetworkService(manager);
            var result = _hostService.StartHost(startPort: 0);
            Assert.IsTrue(result.Success, result.Message);
            Assert.IsNotNull(_hostService.ActiveGameSession, "ホスト開始で GameSession がスポーンされるはず。");
        }

        private static bool IsNetworkBusy(NetworkService service)
            => service != null && (service.IsListening || service.IsClient || service.IsShutdownInProgress);

        private static IEnumerator WaitFrames(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                yield return null;
            }
        }
    }
}
