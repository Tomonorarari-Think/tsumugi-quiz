using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Network;
using TsumugiQuiz.Network.Nat;
using TsumugiQuiz.Tests.PlayMode.UI.HostSetup;
using TsumugiQuiz.Tests.Shared.Network.Nat;
using TsumugiQuiz.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// Main シーンで Title → HostSetup → ロビー → 退出 → Title の一連の流れを確認する PlayMode テスト（issue #7）。
    /// ホストとして開始したときにロビーの名簿に自分（ホスト）が現れ、「退出」でホストが停止して
    /// Title に戻ることを見る。UPnP / IP 確認サービスは <see cref="FakeNatDiscovery"/> /
    /// <see cref="FakeIpLookupClient"/>（#67）のフェイクに差し替えるため、実ネットワークには出ない。
    /// ホスト開始までの手順（<see cref="LoadMainThroughBootAndInstallFakes"/> /
    /// <see cref="StartHostingWithAutoPort"/>）は <see cref="LobbyGameStartSceneTests"/>（#95）からも使う。
    /// </summary>
    public class LobbyViewSceneTests
    {
        private const string BootSceneName = "Boot";
        private const string MainSceneName = "Main";
        private static readonly Regex JoinCodePattern = new Regex(@"^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$");

        /// <summary>ホストのプレイヤー名（<see cref="LobbyGameStartSceneTests"/> からも使う）。</summary>
        internal const string HostPlayerName = "ロビーホスト";

        private ConsentFileScope _consentScope;
        private HostSetupPreferencesScope _preferencesScope;

        [SetUp]
        public void SeedConsentedStateAndBackupPreferences()
        {
            _consentScope = ConsentFileScope.Backup();

            var store = ConsentGate.CreateDefaultStore();
            var requiredTerms = TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, DateTime.UtcNow);

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
            // ホスト停止はアンロードより先に行う（アンロード中も通信が走り続けるのを避けるため）。
            var bootstrap = NetworkBootstrap.Instance;
            bootstrap?.Service?.Stop();

            // #101 / #108 レビュー M-2: 先に Main シーンをアンロードして表示中の View を畳んでから
            // シングルトン（NetworkBootstrap = NetworkService）を破棄する。逆順にすると、
            // View のコールバックが破棄済みのサービスを触りうる（GameViewSceneTests と同じ順序）。
            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();

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
        [Timeout(90000)]
        public IEnumerator HostSetup_ToLobby_ShowsHostInRoster_ThenLeaveReturnsToTitle()
        {
            VisualElement panelRoot = null;
            yield return LoadMainThroughBootAndInstallFakes(root => panelRoot = root);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            yield return SimulateClickRoutine(hostButton);

            yield return StartHostingWithAutoPort(panelRoot);

            Assert.IsTrue(NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost, "ホストが開始されているはず。");

            Button lobbyButton = null;
            yield return WaitForElement<Button>(panelRoot, "lobby-button", found => lobbyButton = found);
            yield return WaitUntil(() => lobbyButton.enabledSelf, "到達性の解決完了後は lobby-button が有効になるはず。");
            yield return SimulateClickRoutine(lobbyButton);

            // --- ロビー画面 ---
            VisualElement playerList = null;
            yield return WaitForElement<VisualElement>(panelRoot, "lobby-player-list", found => playerList = found);

            // ホストの名簿エントリが同期されるまで待つ（LobbyState のスポーンを待つ分の遅延がある）。
            yield return WaitUntil(
                () => playerList.childCount == 1,
                $"ロビーの参加者一覧にホストが 1 件表示されるはず（実際: {playerList.childCount} 件）。");

            var hostRow = playerList[0];
            var nameLabel = hostRow.Q<Label>(className: "lobby-player-name");
            Assert.IsNotNull(nameLabel, "参加者行に名前ラベルがあるはず。");
            Assert.AreEqual(HostPlayerName, nameLabel.text, "ホストのプレイヤー名が表示されるはず。");

            // #206: 参加者が決める名前・ホストから届く切断理由を出す表示欄は、リッチテキストのタグを解釈しない。
            Assert.IsFalse(nameLabel.enableRichText, "名前ラベルはタグを解釈しないはず。");
            Assert.IsFalse(panelRoot.Q<Label>("lobby-status-label").enableRichText, "状況表示（切断理由）はタグを解釈しないはず。");
            Assert.IsFalse(panelRoot.Q<Label>("lobby-duplicate-name-label").enableRichText, "同名の注意（名前を含む）はタグを解釈しないはず。");
            Assert.IsFalse(
                hostRow.ClassListContains("lobby-player-row--disconnected"),
                "接続中のホストはグレー表示にならないはず。");

            var countLabel = panelRoot.Q<Label>("lobby-player-count-label");
            Assert.IsNotNull(countLabel, "lobby-player-count-label が見つかりません。");
            StringAssert.Contains("接続中 1 人", countLabel.text);

            var startGameButton = panelRoot.Q<Button>("start-game-button");
            Assert.IsNotNull(startGameButton, "start-game-button が見つかりません。");
            Assert.AreEqual(
                DisplayStyle.Flex,
                startGameButton.style.display.value,
                "ホストには「ゲーム開始」が表示されるはず。");

            // M9（#28 Phase 2）: ロビーから設定画面への導線。ホストでは「ルーム設定」と表示する。
            var lobbySettingsButton = panelRoot.Q<Button>("lobby-settings-button");
            Assert.IsNotNull(lobbySettingsButton, "lobby-settings-button が見つかりません。");
            Assert.AreEqual("ルーム設定", lobbySettingsButton.text, "ホストのボタン表示は「ルーム設定」。");

            var joinCodeSection = panelRoot.Q<VisualElement>("lobby-join-code-section");
            Assert.IsNotNull(joinCodeSection, "lobby-join-code-section が見つかりません。");
            var internetCodeLabel = panelRoot.Q<Label>("lobby-internet-code-label");
            Assert.IsNotNull(internetCodeLabel, "lobby-internet-code-label が見つかりません。");
            Assert.IsTrue(
                JoinCodePattern.IsMatch(internetCodeLabel.text),
                $"ロビーでも参加コードが再表示されるはず（実際の表示: '{internetCodeLabel.text}'）。");

            // --- 退出 → Title ---
            Button leaveButton = null;
            yield return WaitForElement<Button>(panelRoot, "leave-button", found => leaveButton = found);
            yield return SimulateClickRoutine(leaveButton);

            yield return WaitForElement<Button>(panelRoot, "host-button", _ => { });
            Assert.IsNull(panelRoot.Q<Button>("leave-button"), "退出後は Title View へ戻っているはず。");

            yield return WaitUntil(
                () => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening,
                "退出操作後もホストが停止していません。");
        }

        [UnityTest]
        [Timeout(90000)]
        public IEnumerator ModeratorHost_IsNotShownInThePlayerList()
        {
            // レビュー H-4: 司会専任（host.role = "moderator"）のホストはプレイヤーではないので、
            // 参加者一覧にも人数表示にも出さない（定員の数え方と揃える）。
            VisualElement panelRoot = null;
            yield return LoadMainThroughBootAndInstallFakes(root => panelRoot = root);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            yield return SimulateClickRoutine(hostButton);

            yield return StartHostingWithAutoPort(panelRoot, moderatorOnly: true);

            Button lobbyButton = null;
            yield return WaitForElement<Button>(panelRoot, "lobby-button", found => lobbyButton = found);
            yield return WaitUntil(() => lobbyButton.enabledSelf, "lobby-button が有効になるはず。");
            yield return SimulateClickRoutine(lobbyButton);

            VisualElement playerList = null;
            yield return WaitForElement<VisualElement>(panelRoot, "lobby-player-list", found => playerList = found);

            Label roleLabel = null;
            yield return WaitForElement<Label>(panelRoot, "lobby-role-label", found => roleLabel = found);
            yield return WaitUntil(
                () => roleLabel.text.Contains("司会専任"),
                $"ロビーに司会専任である旨が表示されるはず（実際の表示: '{roleLabel.text}'）。");

            // 名簿にはホストのエントリが 1 件あるが、司会専任なので一覧には出ない。
            Assert.AreEqual(0, playerList.childCount, "司会専任のホストは参加者一覧に出ないはず。");

            var countLabel = panelRoot.Q<Label>("lobby-player-count-label");
            Assert.IsNotNull(countLabel, "lobby-player-count-label が見つかりません。");
            Assert.IsEmpty(countLabel.text, "参加者が 0 人なら人数表示は空のはず。");

            var emptyLabel = panelRoot.Q<Label>("lobby-empty-label");
            Assert.IsNotNull(emptyLabel, "lobby-empty-label が見つかりません。");
            Assert.AreEqual(
                DisplayStyle.Flex,
                emptyLabel.style.display.value,
                "参加者が居ないので「参加者を待っています…」が表示されるはず。");

            // 後片付け。
            Button leaveButton = null;
            yield return WaitForElement<Button>(panelRoot, "leave-button", found => leaveButton = found);
            yield return SimulateClickRoutine(leaveButton);
            yield return WaitForElement<Button>(panelRoot, "host-button", _ => { });
            yield return WaitUntil(
                () => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening,
                "退出操作後もホストが停止していません。");
        }

        /// <summary>
        /// 同名同時接続の注意ラベル（issue #85）がロビーに存在し、
        /// 重複が無い初期状態（ホスト 1 人）では非表示になっていること（レビュー M-2）。
        /// 実際に同名が 2 人並んだ状態の検証は、接続を張れる
        /// <c>LobbyDuplicateNameTests</c>（PlayMode / Network）で行う。
        /// </summary>
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator DuplicateNameNoticeLabel_ExistsAndIsHidden_WhenNoNameIsDuplicated()
        {
            VisualElement panelRoot = null;
            yield return LoadMainThroughBootAndInstallFakes(root => panelRoot = root);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            yield return SimulateClickRoutine(hostButton);

            yield return StartHostingWithAutoPort(panelRoot);

            Button lobbyButton = null;
            yield return WaitForElement<Button>(panelRoot, "lobby-button", found => lobbyButton = found);
            yield return WaitUntil(() => lobbyButton.enabledSelf, "lobby-button が有効になるはず。");
            yield return SimulateClickRoutine(lobbyButton);

            VisualElement playerList = null;
            yield return WaitForElement<VisualElement>(panelRoot, "lobby-player-list", found => playerList = found);
            yield return WaitUntil(
                () => playerList.childCount == 1,
                $"ロビーの参加者一覧にホストが 1 件表示されるはず（実際: {playerList.childCount} 件）。");

            var duplicateNameLabel = panelRoot.Q<Label>("lobby-duplicate-name-label");
            Assert.IsNotNull(
                duplicateNameLabel,
                "lobby-duplicate-name-label が見つかりません（lobby-view.uxml を確認）。");
            Assert.IsEmpty(duplicateNameLabel.text, "同名が居なければ注意文は空のはず。");
            Assert.AreEqual(
                DisplayStyle.None,
                duplicateNameLabel.style.display.value,
                "同名が居なければ注意ラベルは非表示のはず。");

            // 後片付け。
            Button leaveButton = null;
            yield return WaitForElement<Button>(panelRoot, "leave-button", found => leaveButton = found);
            yield return SimulateClickRoutine(leaveButton);
            yield return WaitForElement<Button>(panelRoot, "host-button", _ => { });
            yield return WaitUntil(
                () => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening,
                "退出操作後もホストが停止していません。");
        }

        /// <summary>
        /// Boot → Main の遷移を待ってから、実ネットワークに出ないフェイクの
        /// <see cref="HostConnectivityService"/> を <see cref="NetworkBootstrap.HostConnectivityFactory"/> に差し込む。
        /// </summary>
        internal static IEnumerator LoadMainThroughBootAndInstallFakes(Action<VisualElement> onRootFound)
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
            Assert.IsNotNull(uiDocument.rootVisualElement, "UIDocument.rootVisualElement が null のままです。");

            var bootstrap = NetworkBootstrap.Instance;
            Assert.IsNotNull(bootstrap, "NetworkBootstrap が見つかりません。");
            bootstrap.HostConnectivityFactory = () => new HostConnectivityService(
                discovery: new FakeNatDiscovery(device: null),
                lookupClient: new FakeIpLookupClient { DefaultResponse = IpLookupResponse.Ok(200, "203.0.113.9") },
                lanIpProvider: () => "192.168.10.7");

            // #108 レビュー L-R1: ここまでの脱出条件は rootVisualElement != null までで、
            // パネルへのアタッチは待っていない。共通ヘルパーでアタッチを待ち、
            // UI Toolkit のライブリロード（= UIDocument のツリー作り直し）を止める。
            yield return MainSceneTestHelpers.DisableLiveReloadWhenPanelReady(uiDocument);

            onRootFound(uiDocument.rootVisualElement);
        }

        /// <summary>HostSetup 画面からポート 0（自動選択）でホストを開始し、参加コード表示まで待つ。</summary>
        /// <param name="panelRoot">UIDocument のルート。</param>
        /// <param name="moderatorOnly">true なら「司会専任」（<c>host.role = "moderator"</c>）で開始する。</param>
        internal static IEnumerator StartHostingWithAutoPort(VisualElement panelRoot, bool moderatorOnly = false)
        {
            TextField playerNameField = null;
            yield return WaitForElement<TextField>(panelRoot, "player-name-field", found => playerNameField = found);
            IntegerField portField = null;
            yield return WaitForElement<IntegerField>(panelRoot, "port-field", found => portField = found);
            Button startHostButton = null;
            yield return WaitForElement<Button>(panelRoot, "start-host-button", found => startHostButton = found);

            var hostOnlyToggle = panelRoot.Q<Toggle>("host-only-toggle");
            Assert.IsNotNull(hostOnlyToggle, "host-only-toggle が見つかりません。");
            hostOnlyToggle.value = moderatorOnly;

            playerNameField.value = HostPlayerName;
            portField.value = 0;

            yield return SimulateClickRoutine(startHostButton);

            Label internetCodeLabel = null;
            yield return WaitForElement<Label>(panelRoot, "internet-code-label", found => internetCodeLabel = found);
            yield return WaitUntil(
                () => JoinCodePattern.IsMatch(internetCodeLabel.text),
                $"参加コードが表示されませんでした（実際の表示: '{internetCodeLabel.text}'）。");
        }
    }
}
