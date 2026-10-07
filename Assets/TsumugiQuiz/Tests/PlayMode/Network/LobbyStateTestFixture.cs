using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tests.Shared.Room;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 1 プロセス内にホスト 1 つとクライアント 2 つの <see cref="NetworkManager"/> を立て、
    /// <see cref="LobbyState"/> を動的スポーンする PlayMode テストの共通土台（docs/network.md §10.2、issue #7）。
    /// 作法は #2 の <c>NetworkApprovalIntegrationTests</c> / #12 の <c>GameSessionTestFixture</c> に倣い、
    /// ホストの開始は <see cref="NetworkService"/> 経由で行う。
    /// </summary>
    /// <remarks>
    /// スポーンには実プレハブ資産（<c>Assets/TsumugiQuiz/Prefabs/LobbyState.prefab</c>）を使い、
    /// <see cref="NetworkTestPrefabs.LoadLobbyState"/> で読み込む（#13 と同じ作法）。
    /// <c>NetworkConfig.ForceSamePrefabs</c> が既定 true で、登録済みプレハブのハッシュが接続時の
    /// 設定ハッシュに含まれるため、<see cref="NetworkManager.AddNetworkPrefab"/> による登録は
    /// ホスト・クライアントの開始前に済ませる。
    /// </remarks>
    public abstract class LobbyStateTestFixture
    {
        /// <summary>各種の待機の上限（秒）。</summary>
        protected const float TimeoutSeconds = 20f;

        /// <summary>ホストのプレイヤー名。</summary>
        protected const string HostPlayerName = "ホストさん";

        private const string LoopbackAddress = "127.0.0.1";

        private GameObject _prefabObject;
        private NetworkObject _prefabNetworkObject;
        private GameObject _gameSessionPrefabObject;
        private GameObject _hostObject;
        private GameObject _lobbyObject;
        private GameObject _gameSessionObject;
        private readonly List<GameObject> _clientObjects = new List<GameObject>();
        private readonly List<UnityTransport> _clientTransports = new List<UnityTransport>();

        /// <summary>ホストの <see cref="NetworkManager"/>。</summary>
        protected NetworkManager HostManager { get; private set; }

        /// <summary>クライアントの <see cref="NetworkManager"/>（2 つ）。</summary>
        protected IReadOnlyList<NetworkManager> ClientManagers => _clientManagers;

        private readonly List<NetworkManager> _clientManagers = new List<NetworkManager>();

        /// <summary>ホストの <see cref="NetworkService"/>。</summary>
        protected NetworkService HostService { get; private set; }

        /// <summary>ホスト側の <see cref="LobbyState"/>（名簿の権威）。</summary>
        protected LobbyState HostLobby { get; private set; }

        /// <summary>ホストが実際に待ち受けているポート。</summary>
        protected ushort HostPort { get; private set; }

        /// <summary>
        /// <see cref="GameSession"/>（#13 のプレハブ）も全ピアへ登録してスポーンさせるか（#84）。
        /// 得点・ペナルティの引き継ぎのように「名簿 + 進行」の両方が要るテストだけ true にする
        /// （既定は false。名簿だけのテストで余計な NetworkObject を増やさないため）。
        /// </summary>
        protected virtual bool UsesGameSession => false;

        /// <summary>
        /// ホスト側の <see cref="GameSession"/>。<see cref="UsesGameSession"/> が true で
        /// <see cref="StartHostAndSpawnLobby"/> を呼んだあとだけ有効。
        /// </summary>
        protected GameSession HostSession { get; private set; }


        /// <summary>
        /// ホスト開始時の <c>RoomSettingsSync</c> が読む下書き（<c>room.lastApplied</c>）を、
        /// 実行マシンの <c>app-settings.json</c> から隔離する（PR #92 再レビュー M-1）。
        /// </summary>
        private RoomSettingsDraftScope _roomSettingsDraftScope;

        [SetUp]
        public void SetUp()
        {
            _roomSettingsDraftScope = RoomSettingsDraftScope.Redirect();

            _prefabObject = NetworkTestPrefabs.LoadLobbyState();
            _prefabNetworkObject = _prefabObject.GetComponent<NetworkObject>();

            _hostObject = CreateManager("LobbyHost", out var hostManager, out _);
            HostManager = hostManager;

            for (var i = 0; i < 2; i++)
            {
                var clientObject = CreateManager($"LobbyClient{i}", out var clientManager, out var transport);
                _clientObjects.Add(clientObject);
                _clientManagers.Add(clientManager);
                _clientTransports.Add(transport);
            }

            // ForceSamePrefabs（既定 true）のため、開始前に全ピアへ同じプレハブを登録する。
            HostManager.AddNetworkPrefab(_prefabObject);
            foreach (var clientManager in _clientManagers)
            {
                clientManager.AddNetworkPrefab(_prefabObject);
            }

            if (UsesGameSession)
            {
                // GameSession は NetworkService.StartHost が自動でスポーンする（#13）。
                // 登録しておかないと「プレハブが無い」警告だけ出てスポーンされない。
                _gameSessionPrefabObject = NetworkTestPrefabs.LoadGameSession();
                HostManager.AddNetworkPrefab(_gameSessionPrefabObject);
                foreach (var clientManager in _clientManagers)
                {
                    clientManager.AddNetworkPrefab(_gameSessionPrefabObject);
                }
            }

            HostService = new NetworkService(HostManager);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _roomSettingsDraftScope?.Restore();
            _roomSettingsDraftScope = null;

            foreach (var clientManager in _clientManagers)
            {
                if (clientManager != null && (clientManager.IsClient || clientManager.IsListening))
                {
                    clientManager.Shutdown();
                }
            }

            HostService?.Stop();

            yield return WaitWhile(
                () => IsAnyPeerBusy(),
                $"{TimeoutSeconds} 秒待ってもホスト / クライアントの停止が完了しませんでした。");

            // 停止処理の後始末（キューの吐き出し）が完了するよう 1 フレーム余分に回す。
            yield return null;

            HostService?.Dispose();
            HostService = null;

            DestroyIfPresent(ref _lobbyObject);
            DestroyIfPresent(ref _gameSessionObject);

            for (var i = _clientObjects.Count - 1; i >= 0; i--)
            {
                var clientObject = _clientObjects[i];
                DestroyIfPresent(ref clientObject);
            }

            _clientObjects.Clear();
            _clientManagers.Clear();
            _clientTransports.Clear();

            DestroyIfPresent(ref _hostObject);

            // _prefabObject は資産（AssetDatabase 経由）なので破棄しない。
            _prefabObject = null;
            _gameSessionPrefabObject = null;

            HostManager = null;
            _prefabNetworkObject = null;
            HostLobby = null;
            HostSession = null;
            HostPort = 0;
        }

        /// <summary>
        /// ホストを空きポートで開始し、<see cref="LobbyState"/> をスポーンして設定を反映する。
        /// </summary>
        /// <param name="maxPlayers">参加人数の上限（<c>room.maxPlayers</c>）。</param>
        /// <param name="hostRole">ホストの役割（<c>host.role</c>）。</param>
        /// <param name="allowLateJoin">途中参加を許可するか（<c>network.allowLateJoin</c>）。</param>
        protected void StartHostAndSpawnLobby(
            int maxPlayers = LobbyRoster.DefaultMaxPlayers,
            HostRole hostRole = HostRole.Player,
            bool allowLateJoin = false)
        {
            var result = HostService.StartHost(startPort: 0, hostPlayerName: HostPlayerName);
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreNotEqual(0, HostService.ActivePort, "実際にバインドされたポートが取得できるはず。");
            HostPort = HostService.ActivePort;

            var networkObject = HostManager.SpawnManager.InstantiateAndSpawn(_prefabNetworkObject);
            Assert.IsNotNull(networkObject, "ホスト側で LobbyState をスポーンできるはず。");

            _lobbyObject = networkObject.gameObject;
            _lobbyObject.name = "LobbyState(Host)";

            HostLobby = _lobbyObject.GetComponent<LobbyState>();
            Assert.IsTrue(HostLobby.IsSpawned, "ホスト側の LobbyState がスポーンされるはず。");
            Assert.IsTrue(HostLobby.IsServer, "ホスト側の LobbyState はサーバー権限を持つはず。");

            HostLobby.AttachServer(HostService.ApprovalHandler, HostPlayerName);
            HostLobby.ConfigureRoom(hostRole, maxPlayers, allowLateJoin);

            if (!UsesGameSession)
            {
                return;
            }

            HostSession = HostService.ActiveGameSession;
            Assert.IsNotNull(HostSession, "ホスト開始時に GameSession がスポーンされるはず。");
            Assert.IsTrue(HostSession.IsServer, "ホスト側の GameSession はサーバー権限を持つはず。");
            _gameSessionObject = HostSession.gameObject;
            _gameSessionObject.name = "GameSession(Host)";

            SyncRoomSettings(hostRole, maxPlayers, allowLateJoin);
        }

        /// <summary>
        /// クライアントを接続し、承認されて接続が完了するまで待つ。
        /// </summary>
        /// <param name="index">クライアントの番号（0 または 1）。</param>
        /// <param name="playerName">プレイヤー名。</param>
        /// <param name="reconnectToken">
        /// 前回ホストから受け取った再接続トークン（#69）。既定（<see cref="SessionToken.None"/>）なら
        /// トークン無しで接続する＝新規参加として扱われる。
        /// </param>
        protected IEnumerator ConnectClient(int index, string playerName, SessionToken reconnectToken = default)
        {
            StartClient(index, playerName, reconnectToken);

            var clientManager = _clientManagers[index];
            yield return WaitUntil(
                () => clientManager.IsConnectedClient,
                () => $"クライアント {index} が接続できませんでした（切断理由: '{clientManager.DisconnectReason}'）。");
        }

        /// <summary>
        /// クライアントの接続を試み、拒否されるまで待つ。
        /// </summary>
        /// <param name="index">クライアントの番号。</param>
        /// <param name="playerName">プレイヤー名。</param>
        /// <param name="reconnectToken">提示する再接続トークン（#69）。既定はトークン無し。</param>
        protected IEnumerator ConnectClientExpectingRejection(
            int index, string playerName, SessionToken reconnectToken = default)
        {
            var clientManager = _clientManagers[index];

            // レビュー M-10: 拒否は接続開始の直後に届きうるので、購読を StartClient より先に行う
            // （後から購読すると、拒否が速いときにイベントを取りこぼしてタイムアウトする）。
            var disconnected = false;
            void OnDisconnect(ulong _) => disconnected = true;
            clientManager.OnClientDisconnectCallback += OnDisconnect;

            try
            {
                StartClient(index, playerName, reconnectToken);

                yield return WaitUntil(
                    () => disconnected || clientManager.IsConnectedClient,
                    () => $"クライアント {index} の判定結果が届きませんでした。");
            }
            finally
            {
                clientManager.OnClientDisconnectCallback -= OnDisconnect;
            }

            Assert.IsFalse(clientManager.IsConnectedClient, "拒否されるので接続は完了しないはず。");

            // 拒否された直後は NGO が停止処理中で、そのままだと次の StartClient が false を返す。
            // 同じクライアントを使い回すテストのために停止の完了まで待つ。
            yield return WaitUntil(
                () => !clientManager.IsClient && !clientManager.IsListening && !clientManager.ShutdownInProgress,
                () => $"クライアント {index} の停止が完了しませんでした。");
        }

        /// <summary>クライアントを切断し、停止が完了するまで待つ。</summary>
        protected IEnumerator DisconnectClient(int index)
        {
            var clientManager = _clientManagers[index];
            clientManager.Shutdown();

            yield return WaitUntil(
                () => !clientManager.IsClient && !clientManager.IsListening && !clientManager.ShutdownInProgress,
                () => $"クライアント {index} の停止が完了しませんでした。");

            // ホスト側が切断を検知するまで待つ。
            yield return null;
        }

        /// <summary>
        /// ロビーへ直接書いた値（<see cref="LobbyState.ConfigureRoom"/>）と同じ内容を
        /// <see cref="RoomSettingsSync"/>（#27、<c>GameSession</c> プレハブに載っている）へも積む。
        /// </summary>
        /// <remarks>
        /// #27 以降、<c>GameSession.StartQuestion</c> はゲーム開始時に
        /// <c>CommitRoomSettingsForStart</c> でルーム設定を確定させ、その変更通知
        /// （<c>RoomSettingsApplier</c>）が <see cref="LobbyState.ConfigureRoom"/> を呼び直す。
        /// 両者を揃えておかないと、出題を始めた瞬間に <c>host.role</c> / <c>room.maxPlayers</c> /
        /// <c>network.allowLateJoin</c> がルーム設定側の値（既定値や <c>PlayerPrefs</c> 由来）で
        /// 上書きされ、テストの前提が静かに崩れる（PR #98 レビュー H-1）。
        /// </remarks>
        /// <param name="hostRole">ホストの役割。</param>
        /// <param name="maxPlayers">参加人数の上限。</param>
        /// <param name="allowLateJoin">途中参加を許可するか。</param>
        private void SyncRoomSettings(HostRole hostRole, int maxPlayers, bool allowLateJoin)
        {
            var sync = HostSession.SettingsSync;
            Assert.IsNotNull(sync, "GameSession プレハブに RoomSettingsSync が載っているはず（#27）。");
            Assert.IsTrue(sync.IsSpawned, "RoomSettingsSync は GameSession と一緒にスポーンされるはず。");

            var current = sync.Current;
            Assert.IsTrue(
                sync.TrySetSettings(
                    current
                        .WithHostRole(hostRole)
                        .WithMaxPlayers(maxPlayers)
                        .WithSession(current.Session.WithAllowLateJoin(allowLateJoin))),
                "ロック前なのでルーム設定を差し替えられるはず。");

            Assert.AreEqual(hostRole, HostLobby.Role.Value, "ロビーとルーム設定の host.role が一致すること。");
            Assert.AreEqual(
                maxPlayers, HostLobby.MaxPlayers.Value, "ロビーとルーム設定の room.maxPlayers が一致すること。");
            Assert.AreEqual(
                allowLateJoin,
                HostLobby.AllowLateJoin.Value,
                "ロビーとルーム設定の network.allowLateJoin が一致すること。");
        }

        /// <summary>クライアント側の <see cref="LobbyState"/>（無ければ null）。</summary>
        protected LobbyState FindClientLobby(int index) => LobbyState.Find(_clientManagers[index]);

        /// <summary>クライアント側の <see cref="GameSession"/>（無ければ null、#84）。</summary>
        protected GameSession FindClientSession(int index)
        {
            var manager = _clientManagers[index];
            if (manager == null || manager.SpawnManager == null)
            {
                return null;
            }

            foreach (var spawned in manager.SpawnManager.SpawnedObjectsList)
            {
                if (spawned == null || spawned.NetworkManager != manager)
                {
                    continue;
                }

                var candidate = spawned.GetComponent<GameSession>();
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>クライアントの <see cref="NetworkManager.LocalClientId"/>（未接続なら意味を持たない）。</summary>
        protected ulong GetClientId(int index) => _clientManagers[index].LocalClientId;

        /// <summary>クライアントの直近の切断理由。</summary>
        protected string GetDisconnectReason(int index) => _clientManagers[index].DisconnectReason ?? string.Empty;

        /// <summary>指定した名簿から、名前でエントリを探す。</summary>
        protected static bool TryFindEntry(LobbyState lobby, string playerName, out PlayerEntry entry)
        {
            entry = default;
            if (lobby == null || !lobby.IsSpawned)
            {
                return false;
            }

            foreach (var candidate in lobby.GetPlayersSnapshot())
            {
                if (candidate.GetName() == playerName)
                {
                    entry = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>指定した名簿から、クライアント ID でエントリを探す。</summary>
        protected static bool TryFindEntryByClientId(LobbyState lobby, ulong clientId, out PlayerEntry entry)
        {
            entry = default;
            if (lobby == null || !lobby.IsSpawned)
            {
                return false;
            }

            foreach (var candidate in lobby.GetPlayersSnapshot())
            {
                if (candidate.ClientId == clientId)
                {
                    entry = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>名簿の件数（スポーンしていなければ -1）。</summary>
        protected static int CountEntries(LobbyState lobby)
            => lobby != null && lobby.IsSpawned ? lobby.GetPlayersSnapshot().Count : -1;

        /// <summary>
        /// ホストが発行した再接続トークンをクライアントが受け取るまで待つ（#69）。
        /// 保存（<c>session-token.json</c>）は <see cref="NetworkService"/> の担当で、
        /// このフィクスチャのクライアントは素の <see cref="NetworkManager"/> なのでファイルには触れない。
        /// </summary>
        /// <param name="index">クライアントの番号。</param>
        /// <param name="onReceived">受け取ったトークンを返すコールバック。</param>
        protected IEnumerator WaitForSessionToken(int index, Action<SessionToken> onReceived)
        {
            yield return WaitUntil(
                () => FindClientLobby(index) != null
                      && FindClientLobby(index).LastReceivedSessionToken.HasValue,
                () => $"クライアント {index} が再接続トークンを受け取れませんでした。");

            onReceived(FindClientLobby(index).LastReceivedSessionToken);
        }

        private void StartClient(int index, string playerName, SessionToken reconnectToken)
        {
            // ホストと同じビルドとして名乗る（ビルドの違う相手は拒否される、#204）。
            Assert.IsTrue(
                ConnectionPayloadCodec.TrySerialize(
                    new ConnectionPayload(ProtocolConstants.Version, playerName, LocalBuildIdentity.BuildHash, reconnectToken),
                    out var payloadBytes,
                    out _),
                "テスト用ペイロードの符号化に失敗しました。");

            var clientManager = _clientManagers[index];
            clientManager.NetworkConfig.ConnectionData = payloadBytes;
            _clientTransports[index].SetConnectionData(true, LoopbackAddress, HostPort);
            Assert.IsTrue(clientManager.StartClient(), $"クライアント {index} を開始できるはず。");
        }

        private bool IsAnyPeerBusy()
        {
            foreach (var clientManager in _clientManagers)
            {
                if (clientManager != null
                    && (clientManager.IsClient || clientManager.IsListening || clientManager.ShutdownInProgress))
                {
                    return true;
                }
            }

            return HostManager != null
                   && (HostManager.IsClient || HostManager.IsListening || HostManager.ShutdownInProgress);
        }

        /// <summary>ホスト用・クライアント用に同じ設定の <see cref="NetworkManager"/> を作る。</summary>
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
                ConnectionApproval = true,
            };

            gameObject.SetActive(true);
            return gameObject;
        }

        private static void DestroyIfPresent(ref GameObject gameObject)
        {
            if (gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                gameObject = null;
            }
        }

        /// <summary>条件が満たされるまで待つ。タイムアウトしたら失敗させる。</summary>
        protected static IEnumerator WaitUntil(Func<bool> condition, Func<string> failureMessage)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < TimeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsTrue(condition(), failureMessage());
        }

        /// <summary>条件が false になるまで待つ。タイムアウトしたら失敗させる。</summary>
        private static IEnumerator WaitWhile(Func<bool> condition, string failureMessage)
        {
            var elapsed = 0f;
            while (condition() && elapsed < TimeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (condition())
            {
                Assert.Fail(failureMessage);
            }
        }
    }
}
