using System;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tests.Shared.Room;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// <see cref="NetworkService"/> の PlayMode テスト用の共通土台。
    /// 1 プロセス内に <see cref="NetworkManager"/> + <see cref="UnityTransport"/> を組み立て、
    /// テストごとに破棄する（docs/network.md §10.2）。
    ///
    /// ホスト起動時のポートは 0 を指定して OS に空きポートを選ばせる。7777 を直接使うと、
    /// 実行環境で既にそのポートが使われていた場合にテストが不安定になるため。
    /// </summary>
    public abstract class NetworkServiceTestFixture
    {
        /// <summary>テスト対象の NetworkManager。</summary>
        protected NetworkManager NetworkManagerUnderTest { get; private set; }

        /// <summary>テスト対象のサービス。<see cref="Dispose"/> 済みのテストでは null になる。</summary>
        protected NetworkService Service { get; set; }

        /// <summary><see cref="NetworkService.ClientConnected"/> で通知されたクライアント ID。</summary>
        protected List<ulong> ConnectedClientIds { get; private set; }

        /// <summary>
        /// ホスト開始時の <c>RoomSettingsSync</c> が読む下書き（<c>room.lastApplied</c>）を、
        /// 実行マシンの <c>app-settings.json</c> から隔離する（PR #92 再レビュー M-1）。
        /// </summary>
        private RoomSettingsDraftScope _roomSettingsDraftScope;

        [SetUp]
        public void SetUpFixture()
        {
            _roomSettingsDraftScope = RoomSettingsDraftScope.Redirect();

            // 非アクティブな状態でコンポーネントを組み立ててから有効化する。
            // NetworkManager は OnEnable で Singleton 登録と DontDestroyOnLoad を行うため、
            // NetworkConfig の差し替えはそれより前に済ませる必要がある。
            HostObject = new GameObject(GetType().Name);
            HostObject.SetActive(false);

            var transport = HostObject.AddComponent<UnityTransport>();
            NetworkManagerUnderTest = HostObject.AddComponent<NetworkManager>();
            NetworkManagerUnderTest.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = null,
            };

            HostObject.SetActive(true);

            // ホスト開始時に NetworkService が GameSession をスポーンする（#13）。
            // ForceSamePrefabs（既定 true）のため、開始前に登録しておく必要がある。
            NetworkManagerUnderTest.AddNetworkPrefab(NetworkTestPrefabs.LoadGameSession());

            Service = new NetworkService(NetworkManagerUnderTest);
            ConnectedClientIds = new List<ulong>();
            Service.ClientConnected += id => ConnectedClientIds.Add(id);
        }

        [TearDown]
        public void TearDownFixture()
        {
            _roomSettingsDraftScope?.Restore();
            _roomSettingsDraftScope = null;

            Service?.Dispose();
            Service = null;

            if (HostObject != null)
            {
                UnityEngine.Object.DestroyImmediate(HostObject);
                HostObject = null;
            }

            NetworkManagerUnderTest = null;
        }

        private GameObject HostObject { get; set; }

        /// <summary>
        /// NGO に登録済みの承認コールバックを直接呼び、2 プロセスを立てずに承認パスを検証する。
        /// クライアント ID には <see cref="NetworkManager.ServerClientId"/> 以外の値を渡す
        /// （ホスト自身は拒否できない扱いのため）。
        /// </summary>
        /// <param name="protocolVersion">クライアントが名乗るプロトコルバージョン。</param>
        /// <param name="playerName">クライアントが名乗るプレイヤー名。</param>
        /// <returns>NGO へ返される承認応答。</returns>
        protected NetworkManager.ConnectionApprovalResponse InvokeApproval(ushort protocolVersion, string playerName)
        {
            // ホストと同じビルドとして名乗る（ビルドの違う相手は拒否される、#204）。
            Assert.IsTrue(
                ConnectionPayloadCodec.TrySerialize(
                    new ConnectionPayload(protocolVersion, playerName, LocalBuildIdentity.BuildHash),
                    out var payloadBytes,
                    out _),
                "テスト用ペイロードの符号化に失敗しました。");

            return InvokeApproval(payloadBytes, clientNetworkId: 1);
        }

        /// <summary>任意のペイロード・クライアント ID で承認コールバックを呼ぶ。</summary>
        /// <param name="payload">承認ペイロード（信用されない入力を模す）。</param>
        /// <param name="clientNetworkId">クライアント ID。</param>
        /// <returns>NGO へ返される承認応答。</returns>
        protected NetworkManager.ConnectionApprovalResponse InvokeApproval(byte[] payload, ulong clientNetworkId)
        {
            var callback = NetworkManagerUnderTest.ConnectionApprovalCallback;
            Assert.IsNotNull(callback, "ConnectionApprovalCallback が登録されているはず。");

            var request = new NetworkManager.ConnectionApprovalRequest
            {
                Payload = payload ?? Array.Empty<byte>(),
                ClientNetworkId = clientNetworkId,
            };
            var response = new NetworkManager.ConnectionApprovalResponse();
            callback(request, response);
            return response;
        }
    }
}
