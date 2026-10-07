using System;
using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tests.Shared.Room;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 1 プロセス内にホストとクライアントの <see cref="NetworkManager"/> を 2 つ立て、
    /// <see cref="GameSession"/> を動的スポーンする PlayMode テストの共通土台（docs/network.md §10.2）。
    /// 作法は #2 の <see cref="NetworkApprovalIntegrationTests"/> に倣う。
    /// 2 人目のクライアントが必要なテスト（誤答 → 再開放、#18）は
    /// <see cref="ConnectSecondClient"/> を追加で呼ぶ。
    /// </summary>
    /// <remarks>
    /// <see cref="GameSession"/> は動的スポーンするため、両方の <see cref="NetworkManager"/> に
    /// 同じネットワークプレハブとして登録する必要がある。#13 で実プレハブ
    /// （<c>Assets/TsumugiQuiz/Prefabs/GameSession.prefab</c>）を用意したので、
    /// 実行時生成 + <c>GlobalObjectIdHash</c> の固定（リフレクション）ではなく、そのプレハブ資産を
    /// <see cref="NetworkManager.AddNetworkPrefab"/> で登録して使う。
    /// <c>NetworkConfig.ForceSamePrefabs</c> が既定 true で、登録済みプレハブのハッシュが
    /// 接続時の設定ハッシュに含まれるため、登録はホスト・クライアントの開始前に済ませる。
    /// スポーン自体は本番と同じ経路（<see cref="NetworkService.StartHost"/> → <c>SpawnGameSession</c>）で行う。
    /// </remarks>
    public abstract class GameSessionTestFixture
    {
        private const float TimeoutSeconds = 20f;
        private const string LoopbackAddress = "127.0.0.1";

        protected const string QuestionText = "日本の首都はどこ？";
        protected const string CorrectAnswer = "とうきょう";

        private GameObject _prefabObject;
        private GameObject _hostObject;
        private GameObject _clientObject;
        private GameObject _secondClientObject;
        protected NetworkManager HostManager { get; private set; }
        protected NetworkManager ClientManager { get; private set; }

        /// <summary>2 人目のクライアント。<see cref="ConnectSecondClient"/> を呼ぶまで未接続。</summary>
        protected NetworkManager SecondClientManager { get; private set; }

        private UnityTransport _clientTransport;
        private UnityTransport _secondClientTransport;
        private NetworkService _hostService;
        private GameObject HostSessionObject;
        private ushort _hostPort;
        protected GameSession HostSession { get; private set; }
        protected GameSession ClientSession { get; private set; }

        /// <summary>2 人目のクライアント側の <see cref="GameSession"/>。</summary>
        protected GameSession SecondClientSession { get; private set; }

        /// <summary>クライアント側の Transport（受信バイト列を覗くテストで使う）。</summary>
        protected UnityTransport ClientTransport => _clientTransport;

        /// <summary>ホスト側の問題配信器。</summary>
        protected QuestionDistributor HostDistributor => HostSession?.Distributor;

        /// <summary>クライアント側の問題配信器。</summary>
        protected QuestionDistributor ClientDistributor => ClientSession?.Distributor;

        /// <summary>
        /// ホスト・クライアント双方へ登録する <c>GameSession</c> プレハブ。
        /// 既定は実プレハブ資産。コンポーネント順の影響を確かめる派生クラスが差し替える。
        /// </summary>
        /// <returns>登録するプレハブ。</returns>
        protected virtual GameObject CreateGameSessionPrefab() => NetworkTestPrefabs.LoadGameSession();

        /// <summary>
        /// <see cref="CreateGameSessionPrefab"/> で用意したプレハブの後片付け。
        /// 資産をそのまま使う既定では何もしない（破棄すると資産が消える）。
        /// </summary>
        /// <param name="prefab">後片付けするプレハブ。</param>
        protected virtual void ReleaseGameSessionPrefab(GameObject prefab)
        {
        }


        /// <summary>
        /// ホスト開始時の <c>RoomSettingsSync</c> が読む下書き（<c>room.lastApplied</c>）を、
        /// 実行マシンの <c>app-settings.json</c> から隔離する（PR #92 再レビュー M-1）。
        /// </summary>
        private RoomSettingsDraftScope _roomSettingsDraftScope;

        [SetUp]
        public void SetUp()
        {
            _roomSettingsDraftScope = RoomSettingsDraftScope.Redirect();

            _prefabObject = CreateGameSessionPrefab();

            _hostObject = CreateManager("GameSessionHost", out var hostManager, out _);
            HostManager = hostManager;
            _clientObject = CreateManager("GameSessionClient", out var clientManager, out _clientTransport);
            ClientManager = clientManager;
            _secondClientObject = CreateManager(
                "GameSessionClient2", out var secondClientManager, out _secondClientTransport);
            SecondClientManager = secondClientManager;

            // ForceSamePrefabs（既定 true）のため、開始前に全員へ同じプレハブを登録する。
            HostManager.AddNetworkPrefab(_prefabObject);
            ClientManager.AddNetworkPrefab(_prefabObject);
            SecondClientManager.AddNetworkPrefab(_prefabObject);

            _hostService = new NetworkService(HostManager);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _roomSettingsDraftScope?.Restore();
            _roomSettingsDraftScope = null;

            ShutdownIfRunning(ClientManager);
            ShutdownIfRunning(SecondClientManager);

            _hostService?.Stop();

            yield return WaitWhile(
                () => IsBusy(ClientManager) || IsBusy(SecondClientManager) || IsBusy(HostManager),
                $"{TimeoutSeconds} 秒待ってもホスト / クライアントの停止が完了しませんでした。");

            // 停止処理の後始末（キューの吐き出し）が完了するよう 1 フレーム余分に回す。
            yield return null;

            _hostService?.Dispose();
            _hostService = null;

            DestroyIfPresent(ref HostSessionObject);
            DestroyIfPresent(ref _secondClientObject);
            DestroyIfPresent(ref _clientObject);
            DestroyIfPresent(ref _hostObject);

            // 既定の _prefabObject は資産（プレハブ）なので破棄しない。
            // 実行時に作った変種を使う派生クラスは ReleaseGameSessionPrefab で破棄する。
            ReleaseGameSessionPrefab(_prefabObject);
            _prefabObject = null;

            HostManager = null;
            ClientManager = null;
            SecondClientManager = null;
            _clientTransport = null;
            _secondClientTransport = null;
            _hostPort = 0;
            HostSession = null;
            ClientSession = null;
            SecondClientSession = null;
        }

        /// <summary>接続中・待ち受け中なら停止する。</summary>
        private static void ShutdownIfRunning(NetworkManager manager)
        {
            if (manager != null && (manager.IsClient || manager.IsListening))
            {
                manager.Shutdown();
            }
        }

        /// <summary>まだ接続・停止処理が残っているか。</summary>
        private static bool IsBusy(NetworkManager manager) =>
            manager != null && (manager.IsClient || manager.IsListening || manager.ShutdownInProgress);

        /// <summary>
        /// ホストとクライアントを接続し、<see cref="GameSession"/> をスポーンして
        /// <see cref="HostSession"/> / <see cref="ClientSession"/> に格納する。
        /// </summary>
        /// <param name="scoring">
        /// 得点・ペナルティ・誤答後の再開放の設定（#18）。null なら docs/room-settings.md の既定値。
        /// </param>
        /// <param name="limits">
        /// 制限時間。null なら docs/room-settings.md の既定値。
        /// 何段階も進むテスト（誤答 → 再開放 → 正解）では早押しの持ち時間を長めにして、
        /// 実行環境の速度で時間切れにならないようにする。
        /// </param>
        protected IEnumerator ConnectHostAndClient(ScoringSettings scoring = null, QuizTimeLimits limits = null)
        {
            yield return ConnectHostAndClient(DefaultQuestionSource(), scoring, limits);
        }

        /// <summary>
        /// 問題の供給元を指定してホストとクライアントを接続する（先読みの検証などで複数問を渡す）。
        /// </summary>
        /// <param name="questionSource">ホストに設定する問題の供給元。</param>
        /// <param name="scoring">
        /// 得点・ペナルティ・誤答後の再開放の設定（#18）。null なら docs/room-settings.md の既定値。
        /// </param>
        /// <param name="limits">制限時間（#18）。null なら docs/room-settings.md の既定値。</param>
        protected IEnumerator ConnectHostAndClient(
            IQuestionSource questionSource, ScoringSettings scoring = null, QuizTimeLimits limits = null)
        {
            // ホスト開始で NetworkService が GameSession をスポーンする（本番と同じ経路、#13）。
            _hostPort = StartHostOnFreePort();
            HostSession = _hostService.ActiveGameSession;
            Assert.IsNotNull(HostSession, "ホスト開始時に GameSession がスポーンされるはず。");
            Assert.IsTrue(HostSession.IsSpawned, "ホスト側の GameSession がスポーンされるはず。");
            Assert.IsTrue(HostSession.IsServer, "ホスト側の GameSession はサーバー権限を持つはず。");
            Assert.IsNotNull(HostSession.Distributor, "GameSession プレハブに QuestionDistributor が載っているはず。");

            HostSessionObject = HostSession.gameObject;
            HostSessionObject.name = "GameSession(Host)";
            HostSession.Configure(questionSource, limits: limits, scoring: scoring);

            SetPayload(ClientManager, "つむぎ");
            _clientTransport.SetConnectionData(true, LoopbackAddress, _hostPort);
            Assert.IsTrue(ClientManager.StartClient(), "クライアントを開始できるはず。");

            yield return WaitUntil(
                () => ClientManager.IsConnectedClient,
                () => $"クライアントが接続できませんでした（切断理由: '{ClientManager.DisconnectReason}'）。");

            GameSession clientSession = null;
            yield return WaitUntil(
                () => TryFindSession(ClientManager, out clientSession),
                () => "クライアント側に GameSession が生成されませんでした。");
            ClientSession = clientSession;
            Assert.IsNotNull(ClientSession.Distributor, "クライアント側にも QuestionDistributor が載っているはず。");
        }

        /// <summary>
        /// 2 人目のクライアントを接続し、<see cref="SecondClientSession"/> に格納する
        /// （<see cref="ConnectHostAndClient"/> のあとに呼ぶ、#18）。
        /// </summary>
        protected IEnumerator ConnectSecondClient()
        {
            Assert.AreNotEqual(0, _hostPort, "先に ConnectHostAndClient を呼ぶこと。");

            SetPayload(SecondClientManager, "つむぎ2");
            _secondClientTransport.SetConnectionData(true, LoopbackAddress, _hostPort);
            Assert.IsTrue(SecondClientManager.StartClient(), "2 人目のクライアントを開始できるはず。");

            yield return WaitUntil(
                () => SecondClientManager.IsConnectedClient,
                () => "2 人目のクライアントが接続できませんでした（切断理由: "
                    + SecondClientManager.DisconnectReason + "）。");

            GameSession session = null;
            yield return WaitUntil(
                () => TryFindSession(SecondClientManager, out session),
                () => "2 人目のクライアント側に GameSession が生成されませんでした。");
            SecondClientSession = session;
        }

        /// <summary>既定の問題の供給元（freeText 1 問）。</summary>
        private static IQuestionSource DefaultQuestionSource() =>
            new TestQuestionSource(TestQuestionSource.FreeText("q-test-1", QuestionText, CorrectAnswer));

        private static bool TryFindSession(NetworkManager manager, out GameSession session)
        {
            session = null;
            foreach (var spawned in manager.SpawnManager.SpawnedObjectsList)
            {
                if (spawned == null || spawned.NetworkManager != manager)
                {
                    continue;
                }

                var candidate = spawned.GetComponent<GameSession>();
                if (candidate != null)
                {
                    session = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>ホストを空きポートで開始し、実際にバインドされたポートを返す。</summary>
        private ushort StartHostOnFreePort()
        {
            var result = _hostService.StartHost(startPort: 0);
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreNotEqual(0, _hostService.ActivePort, "実際にバインドされたポートが取得できるはず。");
            return _hostService.ActivePort;
        }

        private static void SetPayload(NetworkManager manager, string playerName)
        {
            // ホストと同じビルドとして名乗る（ビルドの違う相手は拒否される、#204）。
            Assert.IsTrue(
                ConnectionPayloadCodec.TrySerialize(
                    new ConnectionPayload(ProtocolConstants.Version, playerName, LocalBuildIdentity.BuildHash),
                    out var payloadBytes,
                    out _),
                "テスト用ペイロードの符号化に失敗しました。");

            manager.NetworkConfig.ConnectionData = payloadBytes;
        }

        /// <summary>
        /// ホスト用・クライアント用に同じ設定の <see cref="NetworkManager"/> を作る
        /// （<see cref="NetworkApprovalIntegrationTests"/> と同じ手順）。
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

        /// <summary>指定フレーム数だけ回す（棄却された RPC が「何も起こさない」ことを確かめるため）。</summary>
        protected static IEnumerator WaitFrames(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                yield return null;
            }
        }

        /// <summary>
        /// 条件が false になるまで待つ。タイムアウトしたら失敗させる
        /// （停止しきらないまま次のテストに進むと、原因の分かりにくい失敗が後続で起きるため）。
        /// </summary>
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
