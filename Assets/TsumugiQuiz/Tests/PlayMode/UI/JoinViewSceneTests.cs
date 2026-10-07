using System;
using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.TextLayout;
using TsumugiQuiz.UI.Views;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// Join View（issue #6）の PlayMode テスト。
    /// 「不正なコードでエラーメッセージが表示される」「正規化が入力中にプレビューされる」
    /// 「接続拒否時にサーバーの Reason が画面に表示される」の受け入れ条件を検証する。
    /// テスト前後で実際の consent.json は退避・復元する（同意済み状態で Title を表示するため）。
    /// </summary>
    public class JoinViewSceneTests
    {
        private const string LoopbackAddress = "127.0.0.1";

        private MainSceneTestHelpers.ConsentFileScope _consentScope;
        private MainSceneTestHelpers.AppSettingsFileScope _appSettingsScope;

        private GameObject _hostObject;
        private NetworkManager _hostManager;
        private NetworkService _hostService;

        [SetUp]
        public void SeedConsentedState()
        {
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();
            // issue #28 H2: player.name の保存先が PlayerPrefs から app-settings.json へ移行したため、
            // 接続試行時の保存（SavePlayerNameIfValid）が他テストへ影響しないよう退避・復元する。
            _appSettingsScope = MainSceneTestHelpers.AppSettingsFileScope.Backup();

            var store = ConsentGate.CreateDefaultStore();
            var requiredTerms = TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, DateTime.UtcNow);
        }

        [TearDown]
        public void RestoreConsentFile()
        {
            _consentScope?.Restore();
            _appSettingsScope?.Restore();
        }

        [UnityTearDown]
        public IEnumerator TearDownSceneAndNetwork()
        {
            // M-8: テストが差し替えた場合に備え、既定値へ必ず戻す。
            JoinView.ConnectTimeoutSeconds = 10f;

            _hostService?.Stop();
            _hostService?.Dispose();
            _hostService = null;

            if (_hostObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostObject);
                _hostObject = null;
            }

            _hostManager = null;

            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();

            TearDownMainSceneAndBootstrapSingletons();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator JoinView_InvalidCode_ShowsErrorMessage_AndDisablesConnect()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            yield return NavigateFromTitleToJoin(panelRoot);

            TextField codeField = null;
            yield return WaitForElement<TextField>(panelRoot, "join-code-field", found => codeField = found);
            Label codeErrorLabel = null;
            yield return WaitForElement<Label>(panelRoot, "join-code-error-label", found => codeErrorLabel = found);
            Button connectButton = null;
            yield return WaitForElement<Button>(panelRoot, "connect-button", found => connectButton = found);

            // 12 文字だがチェックが一致しない不正なコード（docs/network-joincode.md §1.7 のベクタから 1 文字だけ変えたもの）。
            codeField.value = "6B01RGA7K1K5";

            yield return WaitUntil(
                () => !string.IsNullOrEmpty(codeErrorLabel.text),
                5f,
                "不正なコードなのにエラー表示が出ていません。");

            Assert.That(codeErrorLabel.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "エラーラベルの display が Flex になっていません。");
            StringAssert.Contains("正しくありません", PhraseWrappedText.GetSourceText(codeErrorLabel));
            Assert.IsFalse(connectButton.enabledSelf, "デコードに失敗した状態では参加ボタンが無効のはずです。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator JoinView_MixedCaseHyphenatedCode_IsNormalizedInPreview()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            yield return NavigateFromTitleToJoin(panelRoot);

            TextField codeField = null;
            yield return WaitForElement<TextField>(panelRoot, "join-code-field", found => codeField = found);

            // 小文字・ハイフン位置の乱れ・O/I/L 誤入力を含む入力（docs/network-joincode.md §1.7 のテストベクタ）。
            codeField.value = "6b0l rgA7 kIk4";

            yield return WaitUntil(
                () => codeField.value == "6B01-RGA7-K1K4",
                5f,
                "入力中の正規化プレビューが期待どおりに整形されていません。実際の値: " + codeField.value);
        }

        [UnityTest]
        [Timeout(30000)]
        public IEnumerator JoinView_ValidCodeToUnreachableHost_ShowsTimeoutMessage()
        {
            // M-8: 実際に 10 秒待つ代わりに、テストからタイムアウト秒数を短く差し替える
            // （[UnityTearDown] で既定値 10f に必ず戻す）。UTP がループバックの未使用ポートへの
            // 送信をより早く失敗として検出する可能性はあるが、0.5 秒はそれよりも十分短いため、
            // 実質的にほぼ確実に JoinView 自身のタイムアウト経路で決着する。
            JoinView.ConnectTimeoutSeconds = 0.5f;

            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(r => panelRoot = r);

            yield return NavigateFromTitleToJoin(panelRoot);

            TextField nameField = null;
            yield return WaitForElement<TextField>(panelRoot, "player-name-field", found => nameField = found);
            TextField codeField = null;
            yield return WaitForElement<TextField>(panelRoot, "join-code-field", found => codeField = found);
            Button connectButton = null;
            yield return WaitForElement<Button>(panelRoot, "connect-button", found => connectButton = found);
            Label statusLabel = null;
            yield return WaitForElement<Label>(panelRoot, "join-status-label", found => statusLabel = found);

            nameField.value = "つむぎ";
            // 未接続の宛先（issue #6 の例示どおり 127.0.0.1:1）。形式は正しいので接続は開始されるが応答が来ない。
            codeField.value = JoinCodeCodec.Encode(LoopbackAddress, 1);

            yield return WaitUntil(
                () => connectButton.enabledSelf,
                5f,
                "正しい形式のコードと名前があれば参加ボタンが有効になるはずです。");

            yield return SimulateClickRoutine(connectButton);

            // L-11: 定数（JoinStatusMessages.Timeout）で厳密に一致させる。
            yield return WaitUntil(
                () => PhraseWrappedText.GetSourceText(statusLabel) == JoinStatusMessages.Timeout,
                DefaultTimeoutSeconds,
                "タイムアウト後に既定の文言が表示されませんでした。実際の表示: " + statusLabel.text);

            Assert.IsTrue(connectButton.enabledSelf, "失敗後は再試行できるよう参加ボタンが再度有効になるはずです。");

            // H-1: タイムアウト経路でも NetworkService.Stop() が呼ばれ、クライアントとして
            // 接続中の状態が解除されているはず。Shutdown() の実処理はフレーム終端で走るため、
            // 反映されるまで数フレーム待つ。
            yield return WaitUntil(
                () => !NetworkBootstrap.Instance.Service.IsClient,
                5f,
                "タイムアウト後は NetworkService.Stop() によりクライアント状態が解除されるはずです（H-1）。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator JoinView_InvalidPlayerName_ShowsErrorAndDisablesConnect()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            yield return NavigateFromTitleToJoin(panelRoot);

            TextField nameField = null;
            yield return WaitForElement<TextField>(panelRoot, "player-name-field", found => nameField = found);
            Label nameErrorLabel = null;
            yield return WaitForElement<Label>(panelRoot, "player-name-error-label", found => nameErrorLabel = found);
            Button connectButton = null;
            yield return WaitForElement<Button>(panelRoot, "connect-button", found => connectButton = found);

            // 改行を含むため PlayerNameValidator に拒否される（M-5）。
            nameField.value = "つ\nむぎ";

            yield return WaitUntil(
                () => !string.IsNullOrEmpty(nameErrorLabel.text),
                5f,
                "不正なプレイヤー名なのにエラー表示が出ていません。");

            Assert.That(PhraseWrappedText.GetSourceText(nameErrorLabel), Is.EqualTo(ConnectionRejectionMessages.InvalidPlayerName));
            Assert.IsFalse(connectButton.enabledSelf, "プレイヤー名が不正な間は参加ボタンが無効のはずです。");
        }

        [UnityTest]
        [Timeout(45000)]
        public IEnumerator JoinView_RoomFull_ShowsServerRejectionReason()
        {
            var port = StartInProcessHostWithRoomFull();

            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(r => panelRoot = r);

            yield return NavigateFromTitleToJoin(panelRoot);

            TextField nameField = null;
            yield return WaitForElement<TextField>(panelRoot, "player-name-field", found => nameField = found);
            TextField codeField = null;
            yield return WaitForElement<TextField>(panelRoot, "join-code-field", found => codeField = found);
            Button connectButton = null;
            yield return WaitForElement<Button>(panelRoot, "connect-button", found => connectButton = found);
            Label statusLabel = null;
            yield return WaitForElement<Label>(panelRoot, "join-status-label", found => statusLabel = found);

            nameField.value = "つむぎ";
            codeField.value = JoinCodeCodec.Encode(LoopbackAddress, port);

            yield return WaitUntil(
                () => connectButton.enabledSelf,
                5f,
                "正しい形式のコードと名前があれば参加ボタンが有効になるはずです。");

            yield return SimulateClickRoutine(connectButton);

            yield return WaitUntil(
                () => !string.IsNullOrEmpty(statusLabel.text) && statusLabel.text != "接続中…",
                DefaultTimeoutSeconds,
                "満室による拒否メッセージが表示されませんでした。");

            Assert.That(PhraseWrappedText.GetSourceText(statusLabel), Is.EqualTo(ConnectionRejectionMessages.RoomFull),
                "ConnectionApprovalHandler が返した Reason がそのまま画面に表示されるはずです。実際の表示: " + statusLabel.text);

            // H-1: 拒否経路でも NetworkService.Stop() が呼ばれ、クライアント状態が解除されるはず。
            yield return WaitUntil(
                () => !NetworkBootstrap.Instance.Service.IsClient,
                5f,
                "拒否後は NetworkService.Stop() によりクライアント状態が解除されるはずです（H-1）。");
        }

        /// <summary>
        /// 改変されたホストが送った未知の拒否理由（タグ・改行・制御文字・長大な文字列）は、そのまま出さずに汎用の文言にする（#208）。
        /// #206 の時点では整えたうえでそのまま表示していた。表示欄がタグを解釈しないこと（#206）も確かめる。
        /// 改変されたホストは、承認コールバックの差し替えで模す。
        /// </summary>
        [UnityTest]
        [Timeout(45000)]
        public IEnumerator JoinView_UnknownRejectionReasonFromModifiedHost_ShowsGenericMessage()
        {
            const string visiblePart = "<size=60><color=red>拒否</color></size>";
            var craftedReason = visiblePart + "\n\n\n\u0007" + new string('あ', 200);
            var port = StartInProcessHostWithRoomFull();
            _hostManager.ConnectionApprovalCallback = (request, response) =>
            {
                response.Approved = false;
                response.Reason = craftedReason;
            };

            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(r => panelRoot = r);

            yield return NavigateFromTitleToJoin(panelRoot);

            TextField nameField = null;
            yield return WaitForElement<TextField>(panelRoot, "player-name-field", found => nameField = found);
            TextField codeField = null;
            yield return WaitForElement<TextField>(panelRoot, "join-code-field", found => codeField = found);
            Button connectButton = null;
            yield return WaitForElement<Button>(panelRoot, "connect-button", found => connectButton = found);
            Label statusLabel = null;
            yield return WaitForElement<Label>(panelRoot, "join-status-label", found => statusLabel = found);

            nameField.value = "つむぎ";
            codeField.value = JoinCodeCodec.Encode(LoopbackAddress, port);

            yield return WaitUntil(
                () => connectButton.enabledSelf,
                5f,
                "正しい形式のコードと名前があれば参加ボタンが有効になるはずです。");

            yield return SimulateClickRoutine(connectButton);

            yield return WaitUntil(
                () => !string.IsNullOrEmpty(statusLabel.text) && statusLabel.text != JoinStatusMessages.Connecting,
                DefaultTimeoutSeconds,
                "拒否理由が表示されませんでした。");

            Assert.That(PhraseWrappedText.GetSourceText(statusLabel), Is.EqualTo(JoinStatusMessages.DisconnectedWithoutReason),
                "未知の理由はそのまま出さず、汎用の文言にするはずです。実際の表示: " + statusLabel.text);
            Assert.IsFalse(statusLabel.enableRichText, "ホストから届いた理由を出す表示欄はタグを解釈しないはずです。");
        }

        /// <summary>
        /// 1 プロセス内にもう 1 つ <see cref="NetworkManager"/> を立て、人数上限 1（ホスト自身で満室）の
        /// ホストとして開始する（<c>NetworkServiceApprovalTests.ApprovalCallback_RejectsWhenRoomIsFull</c> と同じ条件）。
        /// </summary>
        private ushort StartInProcessHostWithRoomFull()
        {
            _hostObject = new GameObject(nameof(JoinViewSceneTests) + "-Host");
            _hostObject.SetActive(false);

            var transport = _hostObject.AddComponent<UnityTransport>();
            transport.MaxPayloadSize = NetworkConstants.MaxPayloadSizeBytes;

            _hostManager = _hostObject.AddComponent<NetworkManager>();
            _hostManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = null,
            };

            // Boot シーンの NetworkManager は DefaultNetworkPrefabs.asset 経由で GameSession（#13）と
            // LobbyState（#7）を登録済み。NetworkConfig.ForceSamePrefabs が既定 true で、登録済み
            // プレハブのハッシュが接続時の設定ハッシュに含まれるため、同プロセスのホスト側にも
            // 同じ登録をして構成をそろえる（そろえないと承認前に設定不一致で切断される）。
            _hostManager.AddNetworkPrefab(NetworkTestPrefabs.LoadGameSession());
            _hostManager.AddNetworkPrefab(NetworkTestPrefabs.LoadLobbyState());

            _hostObject.SetActive(true);

            _hostService = new NetworkService(_hostManager);
            var result = _hostService.StartHost(startPort: 0, maxPlayers: 1);
            Assert.IsTrue(result.Success, result.Message);

            return _hostService.ActivePort;
        }

        private static IEnumerator NavigateFromTitleToJoin(VisualElement panelRoot)
        {
            Button joinButton = null;
            yield return WaitForElement<Button>(panelRoot, "join-button", found => joinButton = found);
            Assert.IsNotNull(joinButton, "Title View の join-button が見つかりません。");

            yield return SimulateClickRoutine(joinButton);

            Button connectButton = null;
            yield return WaitForElement<Button>(panelRoot, "connect-button", found => connectButton = found);
            Assert.IsNotNull(connectButton, "join-button クリック後に Join View（connect-button）へ遷移していません。");
        }
    }
}
