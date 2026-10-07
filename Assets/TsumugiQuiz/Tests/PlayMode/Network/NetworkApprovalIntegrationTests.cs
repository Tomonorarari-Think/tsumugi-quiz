using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 1 プロセス内にホストとクライアントの <see cref="NetworkManager"/> を 2 つ立て、
    /// 実際に接続させて ConnectionApproval の経路を確認する（docs/network.md §10.2）。
    ///
    /// ホストのポートは 0 を指定して OS に空きポートを選ばせ、
    /// <see cref="NetworkService.ActivePort"/>（実際にバインドされたポート）へ接続する。
    /// ホストとクライアントの <c>NetworkConfig</c> は
    /// <c>NetworkConfig.GetConfig()</c> のハッシュが一致する必要があるため、同じ設定で作る。
    /// </summary>
    public class NetworkApprovalIntegrationTests
    {
        private const float ConnectTimeoutSeconds = 15f;
        private const string LoopbackAddress = "127.0.0.1";

        /// <summary>ホストと違うビルドの識別子（<see cref="UnityEngine.Application.buildGUID"/> と同じ 16 進 32 文字）。</summary>
        private const string OtherBuildHash = "0123456789abcdef0123456789abcdef";

        private GameObject _hostObject;
        private GameObject _clientObject;
        private NetworkManager _hostManager;
        private NetworkManager _clientManager;
        private UnityTransport _clientTransport;
        private NetworkService _hostService;

        [SetUp]
        public void SetUp()
        {
            _hostObject = CreateManager("IntegrationHost", out _hostManager, out _);
            _clientObject = CreateManager("IntegrationClient", out _clientManager, out _clientTransport);

            // ホスト開始時に NetworkService が GameSession をスポーンする（#13）。
            // ForceSamePrefabs（既定 true）のため、開始前に双方へ同じ登録をしておく。
            var gameSessionPrefab = NetworkTestPrefabs.LoadGameSession();
            _hostManager.AddNetworkPrefab(gameSessionPrefab);
            _clientManager.AddNetworkPrefab(gameSessionPrefab);

            _hostService = new NetworkService(_hostManager);
        }

        /// <summary>
        /// 後片付け。NGO の <c>Shutdown()</c> はフレーム終端で実処理が走るため、
        /// 完了を待ってから GameObject を破棄する。待たずに破棄すると
        /// <c>NetworkManager.OnDestroy</c> が破棄済みの Transport ドライバに触れて
        /// <c>ObjectDisposedException</c> をログに出す（NGO 側は例外を捕まえてログするだけ）。
        /// </summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_clientManager != null && (_clientManager.IsClient || _clientManager.IsListening))
            {
                _clientManager.Shutdown();
            }

            _hostService?.Stop();

            yield return WaitUntil(() =>
                (_clientManager == null || (!_clientManager.IsClient && !_clientManager.IsListening && !_clientManager.ShutdownInProgress))
                && (_hostManager == null || (!_hostManager.IsClient && !_hostManager.IsListening && !_hostManager.ShutdownInProgress)));

            // 停止処理の後始末（キューの吐き出し）が完了するよう 1 フレーム余分に回す。
            yield return null;

            _hostService?.Dispose();
            _hostService = null;

            if (_clientObject != null)
            {
                Object.DestroyImmediate(_clientObject);
                _clientObject = null;
            }

            if (_hostObject != null)
            {
                Object.DestroyImmediate(_hostObject);
                _hostObject = null;
            }

            _hostManager = null;
            _clientManager = null;
            _clientTransport = null;
        }

        [UnityTest]
        public IEnumerator Client_WithMatchingProtocolVersion_IsApprovedAndConnects()
        {
            var port = StartHostOnFreePort();

            var connectedClientIds = new System.Collections.Generic.List<ulong>();
            _hostService.ClientConnected += id => connectedClientIds.Add(id);

            SetClientPayload(ProtocolConstants.Version, "つむぎ");
            _clientTransport.SetConnectionData(true, LoopbackAddress, port);
            Assert.IsTrue(_clientManager.StartClient(), "クライアントを開始できるはず。");

            yield return WaitUntil(() => _clientManager.IsConnectedClient || !_clientManager.IsClient);

            Assert.IsTrue(_clientManager.IsConnectedClient, "承認されて接続が完了するはず。");
            Assert.IsTrue(string.IsNullOrEmpty(_clientManager.DisconnectReason), "拒否理由は付かないはず。");

            // ホスト側の OnClientConnected は、クライアントのシーン同期が終わってから発火するため
            // クライアントの IsConnectedClient より遅れることがある。
            yield return WaitUntil(() => connectedClientIds.Contains(_clientManager.LocalClientId));
            CollectionAssert.Contains(connectedClientIds, _clientManager.LocalClientId);
        }

        [UnityTest]
        public IEnumerator Client_WithMismatchedProtocolVersion_IsRejectedWithReason()
        {
            var port = StartHostOnFreePort();

            var clientVersion = (ushort)(ProtocolConstants.Version + 1);
            SetClientPayload(clientVersion, "つむぎ");
            _clientTransport.SetConnectionData(true, LoopbackAddress, port);
            Assert.IsTrue(_clientManager.StartClient(), "クライアントを開始できるはず。");

            var disconnected = false;
            _clientManager.OnClientDisconnectCallback += _ => disconnected = true;

            yield return WaitUntil(() => disconnected || _clientManager.IsConnectedClient);

            Assert.IsFalse(_clientManager.IsConnectedClient, "バージョン不一致なので接続は完了しないはず。");
            Assert.IsTrue(disconnected, "ホストから切断されるはず。");

            // NGO は ConnectionApprovalResponse.Reason をクライアントの DisconnectReason に入れる。
            StringAssert.Contains("バージョンが異なります", _clientManager.DisconnectReason);
            StringAssert.Contains(clientVersion.ToString(), _clientManager.DisconnectReason);
        }

        /// <summary>
        /// #209: 見えない文字を含む名前は、ホストが承認時に拒否する。クライアント側の入力チェックを通さずに
        /// ペイロードを組み立て、以前のビルドのクライアント（見えない文字を拒否しない）が接続した場合を再現する。
        /// クライアントには <see cref="ConnectionRejectionMessages.InvalidPlayerName"/> が届き、#208 の対応づけ
        /// （<see cref="DisconnectReasonLocalizer"/>）で自前の文言（AppMessage）としてそのまま Join 画面に出る。
        /// </summary>
        [UnityTest]
        public IEnumerator Client_WithHiddenCharacterInName_IsRejectedWithInvalidPlayerNameReason()
        {
            var port = StartHostOnFreePort();

            SetClientPayload(ProtocolConstants.Version, "つむ\u200Bぎ");
            _clientTransport.SetConnectionData(true, LoopbackAddress, port);
            Assert.IsTrue(_clientManager.StartClient(), "クライアントを開始できるはず。");

            var disconnected = false;
            _clientManager.OnClientDisconnectCallback += _ => disconnected = true;

            yield return WaitUntil(() => disconnected || _clientManager.IsConnectedClient);

            Assert.IsFalse(_clientManager.IsConnectedClient, "見えない文字を含む名前なので接続は完了しないはず。");
            Assert.IsTrue(disconnected, "ホストから切断されるはず。");
            Assert.AreEqual(ConnectionRejectionMessages.InvalidPlayerName, _clientManager.DisconnectReason);
            var localized = DisconnectReasonLocalizer.Localize(_clientManager.DisconnectReason);
            Assert.AreEqual(ConnectionRejectionMessages.InvalidPlayerName, localized.Message, "Join 画面には拒否理由がそのまま出るはず。");
            Assert.AreEqual(DisconnectReasonCategory.AppMessage, localized.Category);
        }

        /// <summary>
        /// #204: ビルドの違うクライアント（プロトコルバージョンは同じ）は、承認でバージョン不一致の書式の理由を付けて拒否される。
        /// 数字はビルドの番号（<see cref="BuildDisplayNumber"/>）で、クライアントの <see cref="DisconnectReasonLocalizer"/> は
        /// 自前の文言としてそのまま出す。
        /// </summary>
        [UnityTest]
        public IEnumerator Client_WithDifferentBuild_IsRejectedWithVersionMismatchReason()
        {
            var port = StartHostOnFreePort();
            var hostReasons = new System.Collections.Generic.List<ConnectionRejectionReason>();
            _hostService.ApprovalHandler.Evaluated += (_, decision) => hostReasons.Add(decision.Reason);

            // ホストのログには、拒否の文言と同じビルドの番号を残す（識別子そのものは残さない。PR #216 レビュー L-3）。
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                @"^\[ConnectionApprovalHandler\] 接続を拒否しました clientId=\d+ reason=ClientBuildMismatch "
                + $"hostBuildNumber={LocalBuildIdentity.DisplayNumber} clientBuildNumber={BuildDisplayNumber.From(OtherBuildHash)}$"));

            SetClientPayload(ProtocolConstants.Version, "つむぎ", OtherBuildHash);
            yield return ConnectExpectingRejection(port);

            CollectionAssert.Contains(hostReasons, ConnectionRejectionReason.ClientBuildMismatch);
            var expected = ConnectionRejectionMessages.CreateBuildMismatch(LocalBuildIdentity.BuildHash, OtherBuildHash);
            Assert.AreEqual(expected, _clientManager.DisconnectReason);
            Assert.AreEqual(
                $"バージョンが異なります（ホスト: {LocalBuildIdentity.DisplayNumber} / あなた: {BuildDisplayNumber.From(OtherBuildHash)}）。",
                _clientManager.DisconnectReason);
            AssertShownAsIs(_clientManager.DisconnectReason);
        }

        /// <summary>
        /// #204: ビルドした exe のホスト（識別子あり）に、識別子を載せないクライアントが同じプロトコルバージョンで来た場合。
        /// Editor の識別子は空なので、ホストの識別子は承認の条件を差し替えて再現する。クライアントの番号は 0 になる。
        /// </summary>
        [UnityTest]
        public IEnumerator Client_WithoutBuildHash_IsRejectedByHostWithBuildHash()
        {
            var port = StartHostOnFreePort();
            _hostService.ApprovalHandler.SetPolicy(
                _hostService.ApprovalHandler.Policy.WithExpectedClientBuildHash(OtherBuildHash));

            SetClientPayload(ProtocolConstants.Version, "つむぎ", string.Empty);
            yield return ConnectExpectingRejection(port);

            Assert.AreEqual(
                $"バージョンが異なります（ホスト: {BuildDisplayNumber.From(OtherBuildHash)} / あなた: 0）。",
                _clientManager.DisconnectReason);
            AssertShownAsIs(_clientManager.DisconnectReason);
        }

        /// <summary>
        /// #204: #208〜#209 の版のクライアント（プロトコルバージョン 2、識別子は空）は、#204 で上げたバージョン（3）の不一致で拒否される。
        /// 届く理由は既存の書式どおりなので、その版のクライアントでも日本語でそのまま出る（書式の一致は EditMode の
        /// <c>ConnectionApprovalBuildMatchTests</c> で凍結した写しと照合している）。
        /// </summary>
        [UnityTest]
        public IEnumerator Client_OfPreviousRelease_IsRejectedWithVersionMismatchReason()
        {
            var port = StartHostOnFreePort();

            SetClientPayload(2, "つむぎ", string.Empty);
            yield return ConnectExpectingRejection(port);

            Assert.AreEqual("バージョンが異なります（ホスト: 3 / あなた: 2）。", _clientManager.DisconnectReason);
            AssertShownAsIs(_clientManager.DisconnectReason);
        }

        /// <summary>
        /// 設定済みのペイロードでクライアントを開始し、ホストに拒否されて切断されるまで待つ。
        /// </summary>
        private IEnumerator ConnectExpectingRejection(ushort port)
        {
            _clientTransport.SetConnectionData(true, LoopbackAddress, port);
            Assert.IsTrue(_clientManager.StartClient(), "クライアントを開始できるはず。");

            var disconnected = false;
            _clientManager.OnClientDisconnectCallback += _ => disconnected = true;

            yield return WaitUntil(() => disconnected || _clientManager.IsConnectedClient);

            Assert.IsFalse(_clientManager.IsConnectedClient, "拒否されるので接続は完了しないはず。");
            Assert.IsTrue(disconnected, "ホストから切断されるはず。");
        }

        /// <summary>クライアントの Join 画面に、届いた理由が自前の文言としてそのまま出ること。</summary>
        private static void AssertShownAsIs(string disconnectReason)
        {
            var localized = DisconnectReasonLocalizer.Localize(disconnectReason);
            Assert.AreEqual(DisconnectReasonCategory.AppMessage, localized.Category, disconnectReason);
            Assert.AreEqual(disconnectReason, localized.Message);
        }

        /// <summary>ホストを空きポートで開始し、実際にバインドされたポートを返す。</summary>
        private ushort StartHostOnFreePort()
        {
            var result = _hostService.StartHost(startPort: 0);
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreNotEqual(0, _hostService.ActivePort, "実際にバインドされたポートが取得できるはず。");
            return _hostService.ActivePort;
        }

        /// <summary>
        /// クライアントの承認ペイロードを直接組み立てて設定する。
        /// バージョン不一致を再現するため、<see cref="NetworkService.StartClient"/> は使わない
        /// （そちらは常に自分のプロトコルバージョンを載せる）。
        /// </summary>
        /// <param name="protocolVersion">名乗るプロトコルバージョン。</param>
        /// <param name="playerName">名乗るプレイヤー名。</param>
        /// <param name="clientBuildHash">名乗るビルドの識別子。null ならホストと同じビルド（<see cref="LocalBuildIdentity.BuildHash"/>、#204）。</param>
        private void SetClientPayload(ushort protocolVersion, string playerName, string clientBuildHash = null)
        {
            Assert.IsTrue(
                ConnectionPayloadCodec.TrySerialize(
                    new ConnectionPayload(protocolVersion, playerName, clientBuildHash ?? LocalBuildIdentity.BuildHash),
                    out var payloadBytes,
                    out _),
                "テスト用ペイロードの符号化に失敗しました。");

            _clientManager.NetworkConfig.ConnectionData = payloadBytes;
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < ConnectTimeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>
        /// ホスト用・クライアント用に同じ設定の <see cref="NetworkManager"/> を作る。
        /// 非アクティブな状態で組み立ててから有効化する（OnEnable で Singleton 登録が走るため）。
        /// </summary>
        private static GameObject CreateManager(string name, out NetworkManager manager, out UnityTransport transport)
        {
            var gameObject = new GameObject(name);
            gameObject.SetActive(false);

            transport = gameObject.AddComponent<UnityTransport>();
            transport.MaxPayloadSize = NetworkConstants.MaxPayloadSizeBytes;

            manager = gameObject.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = null,

                // クライアント側でも true にしないと承認ペイロードが送信されず、
                // NetworkConfig のハッシュもホストと一致しない（docs/network.md §2.2）。
                ConnectionApproval = true,
            };

            gameObject.SetActive(true);
            return gameObject;
        }
    }
}
