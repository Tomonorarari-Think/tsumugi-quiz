using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Network;
using TsumugiQuiz.Network.Nat;
using TsumugiQuiz.Tests.Shared.Network.Nat;
using TsumugiQuiz.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI.HostSetup
{
    /// <summary>
    /// Boot → Main の遷移を経て、Title から HostSetup 画面へ進み、ホストを開始して
    /// 参加コードが表示されること、「戻る」でホストを停止して Title に戻れること、
    /// Lobby（#7 の <c>LobbyView</c>）へ進んでも到達性の解決結果
    /// （<see cref="HostConnectivityService"/>）が生き続けること（issue #5 レビュー C-1）、
    /// ロビーの「退出」でホストが停止して Title に戻ること（issue #7）を確認する PlayMode テスト。
    ///
    /// UPnP 探索・IP 確認サービスは実ネットワークに依存させず、<c>Tests/Shared/Network/Nat</c> の
    /// <see cref="FakeNatDiscovery"/> / <see cref="FakeIpLookupClient"/> へ
    /// <see cref="NetworkBootstrap.HostConnectivityFactory"/> を差し替える（#67）。
    /// 実 NAT / 実インターネットに依存する検証は EditMode の <c>[Category("Network")]</c> テストが別途担う。
    /// </summary>
    public class HostSetupFlowTests
    {
        private const string BootSceneName = "Boot";
        private const string MainSceneName = "Main";
        private static readonly Regex JoinCodePattern = new Regex(@"^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$");

        private MainSceneTestHelpers.ConsentFileScope _consentScope;
        private HostSetupPreferencesScope _preferencesScope;

        [SetUp]
        public void SeedConsentedStateAndBackupPreferences()
        {
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();

            var store = ConsentGate.CreateDefaultStore();
            var requiredTerms = TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, DateTime.UtcNow);

            // M-9: HostSetupPreferences（PlayerPrefs）を退避し、テスト後に元へ戻す。
            _preferencesScope = HostSetupPreferencesScope.Backup();
        }

        [TearDown]
        public void RestoreConsentFileAndPreferences()
        {
            _consentScope?.Restore();
            _preferencesScope?.Restore();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // #101 / #108 レビュー M-2: 先に Main シーンをアンロードして表示中の View を畳んでから
            // シングルトン（NetworkBootstrap = NetworkService）を破棄する。逆順にすると、
            // View のコールバックが破棄済みのサービスを触りうる（GameViewSceneTests と同じ順序）。
            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();

            var bootstrap = NetworkBootstrap.Instance;
            if (bootstrap != null)
            {
                UnityEngine.Object.DestroyImmediate(bootstrap.gameObject);
            }

            var sePlayer = SePlayer.Instance;
            if (sePlayer != null)
            {
                UnityEngine.Object.DestroyImmediate(sePlayer.gameObject);
            }
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator HostSetup_StartHostWithAutoPort_ShowsJoinCode_ThenBackStopsHost()
        {
            VisualElement panelRoot = null;
            yield return LoadMainThroughBootAndInstallFakes(root => panelRoot = root);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            Assert.IsNotNull(hostButton, "Title View の host-button が見つかりません。");
            yield return SimulateClickRoutine(hostButton);

            Label internetCodeLabel = null;
            yield return StartHostingWithAutoPort(panelRoot, found => internetCodeLabel = found);

            Assert.IsTrue(NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost, "ホストが開始されているはず。");

            Button backButton = null;
            yield return WaitForElement<Button>(panelRoot, "back-button", found => backButton = found);
            Button confirmBackButton = null;
            yield return WaitForElement<Button>(panelRoot, "confirm-back-button", found => confirmBackButton = found);

            yield return SimulateClickRoutine(backButton);
            yield return SimulateClickRoutine(confirmBackButton);

            Button hostButtonAfterBack = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButtonAfterBack = found);
            Assert.IsNotNull(hostButtonAfterBack, "戻る操作後に Title View（host-button）へ戻っていません。");

            yield return WaitUntil(
                () => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening,
                "戻る操作後もホストが停止していません。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator HostSetup_NavigateToLobby_KeepsHostConnectivityAlive()
        {
            VisualElement panelRoot = null;
            yield return LoadMainThroughBootAndInstallFakes(root => panelRoot = root);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            yield return SimulateClickRoutine(hostButton);

            Label internetCodeLabel = null;
            yield return StartHostingWithAutoPort(panelRoot, found => internetCodeLabel = found);

            // C-1: HostSetupView は所有していない。NetworkBootstrap が保持する
            // HostConnectivityService の参照とその到達性の解決結果を控えておく。
            var bootstrap = NetworkBootstrap.Instance;
            Assert.IsNotNull(bootstrap, "NetworkBootstrap が見つかりません。");
            var connectivityBeforeNavigation = bootstrap.HostConnectivity;
            var boundPortBeforeNavigation = connectivityBeforeNavigation.Current.InternalPort;
            Assert.AreNotEqual(0, boundPortBeforeNavigation, "到達性が解決済み（InternalPort != 0）のはず。");

            Button lobbyButton = null;
            yield return WaitForElement<Button>(panelRoot, "lobby-button", found => lobbyButton = found);
            Assert.IsNotNull(lobbyButton, "lobby-button が見つかりません。");
            yield return WaitUntil(() => lobbyButton.enabledSelf, "到達性の解決完了後は lobby-button が有効になるはず。");

            yield return SimulateClickRoutine(lobbyButton);

            // Lobby View（#7）が表示される（参加者一覧と「退出」ボタンを持つ）。
            Button leaveButton = null;
            yield return WaitForElement<Button>(panelRoot, "leave-button", found => leaveButton = found);
            Assert.IsNull(panelRoot.Q<Button>("lobby-button"), "Lobby View へ遷移しているはず。");
            Assert.IsNotNull(panelRoot.Q<VisualElement>("lobby-player-list"), "参加者一覧のコンテナがあるはず。");

            // C-1 の本題: View が破棄された（OnHide 済み）後も、NetworkBootstrap が保持する
            // HostConnectivityService は同一インスタンスのまま、解決済みの状態を保ち続けている。
            Assert.AreSame(
                connectivityBeforeNavigation,
                bootstrap.HostConnectivity,
                "Lobby へ遷移しても HostConnectivityService は破棄されず同一インスタンスのままのはず。");
            Assert.AreEqual(
                boundPortBeforeNavigation,
                bootstrap.HostConnectivity.Current.InternalPort,
                "Lobby へ遷移しても到達性の解決結果（ポートマッピング等）は保持され続けるはず。");
            Assert.IsTrue(
                NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening,
                "Lobby へ遷移してもホストは動作し続けているはず。");

            // 後片付け: ロビーの「退出」でホストを停止して Title へ戻る。
            yield return SimulateClickRoutine(leaveButton);

            yield return WaitForElement<Button>(panelRoot, "host-button", _ => { });

            yield return WaitUntil(
                () => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening,
                "後片付けのホスト停止が完了しませんでした。");
        }

        /// <summary>
        /// Boot シーンから Main シーンへ遷移し、UIDocument の準備を待ってから、
        /// 実ネットワークに出ないフェイクの <see cref="HostConnectivityService"/> を
        /// <see cref="NetworkBootstrap.HostConnectivityFactory"/> に差し込む。
        /// HostSetupView は Show された瞬間に <c>NetworkBootstrap.HostConnectivity</c> へアクセスするため、
        /// HostSetup へ遷移するより前（Title 表示中）にこの差し替えを終えておく必要がある。
        /// </summary>
        private static IEnumerator LoadMainThroughBootAndInstallFakes(Action<VisualElement> onRootFound)
        {
            yield return SceneManager.LoadSceneAsync(BootSceneName, LoadSceneMode.Single);

            var sceneDeadline = Time.realtimeSinceStartupAsDouble + DefaultTimeoutSeconds;
            var scenePolls = 0;
            while (SceneManager.GetActiveScene().name != MainSceneName
                   && (Time.realtimeSinceStartupAsDouble < sceneDeadline || scenePolls < MinPollCount))
            {
                scenePolls++;
                yield return null;
            }

            Assert.AreEqual(MainSceneName, SceneManager.GetActiveScene().name, "Main シーンへ遷移しているはず。");

            // issue #73 レビュー H3: ViewRouter がアタッチされている UIDocument を名指しで取得する。
            var uiDocument = FindViewRouterUIDocument();
            var documentDeadline = Time.realtimeSinceStartupAsDouble + DefaultTimeoutSeconds;
            var documentPolls = 0;
            while ((uiDocument == null || uiDocument.rootVisualElement == null)
                   && (Time.realtimeSinceStartupAsDouble < documentDeadline || documentPolls < MinPollCount))
            {
                documentPolls++;
                yield return null;
                uiDocument = FindViewRouterUIDocument();
            }

            Assert.IsNotNull(uiDocument, "ViewRouter の UIDocument が Main シーンに見つかりません。");
            var panelRoot = uiDocument.rootVisualElement;
            Assert.IsNotNull(panelRoot, "UIDocument.rootVisualElement が null のままです。");

            var bootstrap = NetworkBootstrap.Instance;
            Assert.IsNotNull(bootstrap, "NetworkBootstrap が見つかりません。");

            // C-1: HostConnectivityService の生成は NetworkBootstrap の責務になったため、
            // ここでフェイクの生成方法を差し込む。
            bootstrap.HostConnectivityFactory = () => new HostConnectivityService(
                discovery: new FakeNatDiscovery(device: null),
                lookupClient: new FakeIpLookupClient { DefaultResponse = IpLookupResponse.Ok(200, "203.0.113.5") },
                lanIpProvider: () => "192.168.1.23");

            // #108 レビュー L-R1: ここまでの脱出条件は rootVisualElement != null までで、
            // パネルへのアタッチは待っていない。共通ヘルパーでアタッチを待ち、
            // UI Toolkit のライブリロード（= UIDocument のツリー作り直し）を止める。
            yield return MainSceneTestHelpers.DisableLiveReloadWhenPanelReady(uiDocument);

            onRootFound(panelRoot);
        }

        /// <summary>
        /// HostSetup 画面が表示された状態から、プレイヤー名を入力しポート 0（自動選択）で
        /// ホストを開始し、参加コードが 12 文字形式で表示されるまで待つ。
        /// </summary>
        private static IEnumerator StartHostingWithAutoPort(VisualElement panelRoot, Action<Label> onInternetCodeLabelFound)
        {
            TextField playerNameField = null;
            yield return WaitForElement<TextField>(panelRoot, "player-name-field", found => playerNameField = found);
            IntegerField portField = null;
            yield return WaitForElement<IntegerField>(panelRoot, "port-field", found => portField = found);
            Button startHostButton = null;
            yield return WaitForElement<Button>(panelRoot, "start-host-button", found => startHostButton = found);

            Assert.IsNotNull(playerNameField, "player-name-field が見つかりません。");
            Assert.IsNotNull(portField, "port-field が見つかりません。");
            Assert.IsNotNull(startHostButton, "start-host-button が見つかりません。");

            playerNameField.value = "テストホスト";
            portField.value = 0; // OS に空きポートを選ばせる。

            yield return SimulateClickRoutine(startHostButton);

            Label internetCodeLabel = null;
            yield return WaitForElement<Label>(panelRoot, "internet-code-label", found => internetCodeLabel = found);
            Assert.IsNotNull(internetCodeLabel, "internet-code-label が見つかりません。");

            yield return WaitUntil(
                () => JoinCodePattern.IsMatch(internetCodeLabel.text),
                $"参加コードが 12 文字形式で表示されませんでした（実際の表示: '{internetCodeLabel.text}'）。");

            onInternetCodeLabelFound(internetCodeLabel);
        }
    }
}
