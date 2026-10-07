using System;
using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.TextLayout;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// クライアントとしてロビーに入っているときにホストが退出すると、ロビーの状況表示に日本語の文言
    /// （<see cref="DisconnectReasonMessages.HostShutDown"/>）が出ることを確かめる（issue #208）。
    /// #208 より前は NGO の英語の理由（「Disconnected due to host shutting down.」）がそのまま出ていた。
    /// ホストは 1 プロセス内にもう 1 つ立てた <see cref="NetworkManager"/>（<c>JoinViewSceneTests</c> と同じ作法）で、
    /// Main シーンの <see cref="NetworkService"/> が Join 画面から参加する。
    /// </summary>
    public class LobbyViewHostDisconnectSceneTests
    {
        private const string LoopbackAddress = "127.0.0.1";
        private const string HostPlayerName = "ホストさん";
        private const string ClientPlayerName = "つむぎ";

        private ConsentFileScope _consentScope;
        private AppSettingsFileScope _appSettingsScope;

        private GameObject _hostObject;
        private NetworkManager _hostManager;
        private NetworkService _hostService;

        [SetUp]
        public void SeedConsentedState()
        {
            _consentScope = ConsentFileScope.Backup();
            // 接続時に player.name を app-settings.json へ保存するので、他のテストへ影響しないよう退避・復元する（JoinViewSceneTests と同じ）。
            _appSettingsScope = AppSettingsFileScope.Backup();

            var store = ConsentGate.CreateDefaultStore();
            var requiredTerms = TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, DateTime.UtcNow);
        }

        [TearDown]
        public void RestoreFiles()
        {
            _consentScope?.Restore();
            _appSettingsScope?.Restore();
        }

        [UnityTearDown]
        public IEnumerator TearDownSceneAndNetwork()
        {
            _hostService?.Stop();
            _hostService?.Dispose();
            _hostService = null;

            if (_hostObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostObject);
                _hostObject = null;
            }

            _hostManager = null;

            yield return UnloadMainSceneRoutine();

            TearDownMainSceneAndBootstrapSingletons();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator HostLeaves_LobbyShowsJapaneseReason_ThenLeaveReturnsToTitle()
        {
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(r => panelRoot = r);

            // シーンを読み込んでから立てる（GameViewSceneTests と同じ順序）。先に立てると、クライアント側で
            // LobbyState が見つからず、ロビーが「ロビーの情報を取得できませんでした。」になった（#208 の実測。原因は調べていない）。
            var port = StartInProcessHostWithLobby();

            yield return JoinFromTitle(panelRoot, port);

            Label statusLabel = null;
            yield return WaitForElement<Label>(panelRoot, "lobby-status-label", found => statusLabel = found);
            Button leaveButton = null;
            yield return WaitForElement<Button>(panelRoot, "leave-button", found => leaveButton = found);

            // ロビーの名簿がホストから同期されるまで待つ（ロビーに入りきってからホストを止める）。
            yield return WaitUntil(
                () => NetworkBootstrap.Instance.Lobby != null && NetworkBootstrap.Instance.Lobby.GetPlayersSnapshot().Count == 2,
                DefaultTimeoutSeconds,
                "クライアント側のロビーにホストと自分の 2 人が同期されるはずです。",
                panelRoot);

            // ホストの「退出」と同じ操作（NetworkService.Stop()）。NGO は各クライアントへ英語の理由を送る。
            _hostService.Stop();

            yield return WaitUntil(
                () => statusLabel.resolvedStyle.display == DisplayStyle.Flex && !string.IsNullOrEmpty(statusLabel.text),
                DefaultTimeoutSeconds,
                "ホストの退出で、ロビーに切断の理由が表示されるはずです。",
                panelRoot);

            Assert.That(statusLabel.text, Is.EqualTo(DisconnectReasonMessages.HostShutDown),
                "NGO の英語の理由ではなく、日本語の文言が表示されるはずです。実際の表示: " + statusLabel.text);
            Assert.IsFalse(statusLabel.enableRichText, "ロビーの状況表示はタグを解釈しないはずです（#206）。");
            Assert.That(leaveButton.text, Is.EqualTo("タイトルへ戻る"), "切断後の操作は「タイトルへ戻る」だけに絞るはずです。");

            // 既存の画面遷移（「タイトルへ戻る」で Title へ）が変わっていないこと。
            yield return SimulateClickRoutine(leaveButton, panelRoot);

            Button joinButton = null;
            yield return WaitForElement<Button>(panelRoot, "join-button", found => joinButton = found);
            Assert.IsNotNull(joinButton, "「タイトルへ戻る」で Title へ戻るはずです。");
            Assert.IsFalse(NetworkBootstrap.Instance.Service.IsClient, "Title へ戻ったあとはクライアントとして動いていないはずです。");
        }

        /// <summary>
        /// 1 プロセス内にもう 1 つ <see cref="NetworkManager"/> を立ててホストを開始し、<see cref="LobbyState"/> をスポーンする
        /// （<c>LobbyStateTestFixture.StartHostAndSpawnLobby</c> と同じ手順）。
        /// </summary>
        private ushort StartInProcessHostWithLobby()
        {
            _hostObject = new GameObject(nameof(LobbyViewHostDisconnectSceneTests) + "-Host");
            _hostObject.SetActive(false);

            var transport = _hostObject.AddComponent<UnityTransport>();
            transport.MaxPayloadSize = NetworkConstants.MaxPayloadSizeBytes;

            _hostManager = _hostObject.AddComponent<NetworkManager>();
            _hostManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = null,
            };

            // Boot シーンの NetworkManager と同じプレハブを登録して、接続時の設定ハッシュをそろえる（JoinViewSceneTests と同じ）。
            var lobbyPrefab = NetworkTestPrefabs.LoadLobbyState();
            _hostManager.AddNetworkPrefab(NetworkTestPrefabs.LoadGameSession());
            _hostManager.AddNetworkPrefab(lobbyPrefab);

            _hostObject.SetActive(true);

            _hostService = new NetworkService(_hostManager);
            var result = _hostService.StartHost(startPort: 0, hostPlayerName: HostPlayerName);
            Assert.IsTrue(result.Success, result.Message);

            var lobbyObject = _hostManager.SpawnManager.InstantiateAndSpawn(lobbyPrefab.GetComponent<NetworkObject>());
            Assert.IsNotNull(lobbyObject, "ホスト側で LobbyState をスポーンできるはず。");
            var lobby = lobbyObject.GetComponent<LobbyState>();
            lobby.AttachServer(_hostService.ApprovalHandler, HostPlayerName);
            lobby.ConfigureRoom(HostRole.Player, LobbyRoster.DefaultMaxPlayers, allowLateJoin: false);

            return _hostService.ActivePort;
        }

        private static IEnumerator JoinFromTitle(VisualElement panelRoot, ushort port)
        {
            Button titleJoinButton = null;
            yield return WaitForElement<Button>(panelRoot, "join-button", found => titleJoinButton = found);
            yield return SimulateClickRoutine(titleJoinButton, panelRoot);

            TextField nameField = null;
            yield return WaitForElement<TextField>(panelRoot, "player-name-field", found => nameField = found);
            TextField codeField = null;
            yield return WaitForElement<TextField>(panelRoot, "join-code-field", found => codeField = found);
            Button connectButton = null;
            yield return WaitForElement<Button>(panelRoot, "connect-button", found => connectButton = found);
            Label joinStatusLabel = null;
            yield return WaitForElement<Label>(panelRoot, "join-status-label", found => joinStatusLabel = found);

            nameField.value = ClientPlayerName;
            codeField.value = JoinCodeCodec.Encode(LoopbackAddress, port);

            yield return WaitUntil(
                () => connectButton.enabledSelf,
                5f,
                "正しい形式のコードと名前があれば参加ボタンが有効になるはずです。");

            yield return SimulateClickRoutine(connectButton, panelRoot);

            yield return WaitUntil(
                () => panelRoot.Q<Label>("lobby-status-label") != null,
                DefaultTimeoutSeconds,
                () => "参加に成功してロビーへ移るはずです。Join 画面の表示: "
                    + PhraseWrappedText.GetSourceText(joinStatusLabel),
                panelRoot);
        }
    }
}
