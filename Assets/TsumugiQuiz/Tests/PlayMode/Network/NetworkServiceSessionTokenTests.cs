using System;
using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tests.Shared.Core.Network;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// <see cref="NetworkService"/> を通した再接続トークンの一連の経路を検証する PlayMode テスト
    /// （issue #85 の M2、#69 のレビュー指摘。docs/network.md §2.3 / §10.2）。
    ///
    /// <see cref="LobbyReconnectTokenTests"/>（#69）はトークンの受け渡しを素の
    /// <c>NetworkManager</c> で検証しており、<see cref="NetworkService"/> 側の
    /// 「保存 → 次の接続で送出」（<c>AttachSessionTokenListener</c> →
    /// <c>HandleSessionTokenReceived</c> → <c>StartClient</c> の <c>LoadReconnectToken</c>）を通っていなかった。
    /// </summary>
    /// <remarks>
    /// 保管庫は <see cref="FakeSessionTokenStorage"/>（メモリのみ。<c>Tests/Shared</c>）に差し替えるので、
    /// 実ファイル（<c>AppPaths.DataRoot/session-token.json</c>）には一切触れない。
    /// </remarks>
    public class NetworkServiceSessionTokenTests : LobbyStateTestFixture
    {
        private const string PlayerName = "つむぎ";
        private const string LoopbackAddress = "127.0.0.1";

        /// <summary>クライアント 0 番の <see cref="NetworkManager"/> を包むサービス（本テストの主役）。</summary>
        private NetworkService _clientService;

        private FakeSessionTokenStorage _storage;

        /// <summary>
        /// クライアント側のサービスを片付ける。派生クラスの <c>[UnityTearDown]</c> は
        /// 基底（<see cref="LobbyStateTestFixture.TearDown"/>）より先に走るため、
        /// ここで停止・破棄してから基底の停止待ちに入る。
        /// </summary>
        [UnityTearDown]
        public IEnumerator DisposeClientService()
        {
            if (_clientService != null)
            {
                _clientService.Dispose();
                _clientService = null;
            }

            _storage = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator StartClient_StoresTheIssuedToken_AndReturnsToTheSameSeatOnReconnect()
        {
            // 定員 2（ホスト + 1 名）。復帰できなければ席が埋まっていて弾かれるので、
            // 「トークンを載せて再接続できた」ことが結果からはっきり分かる。
            StartHostAndSpawnLobby(maxPlayers: 2);
            CreateClientService();

            // --- 1 回目の接続: トークンが配られ、ストアに保存される ---
            yield return StartClientAndWaitForConnection();

            Assert.AreEqual(PlayerName, _clientService.LocalPlayerName, "正規化した名前が保持されるはず。");

            yield return WaitUntil(
                () => _storage.SaveCount >= 1,
                () => "NetworkService が受け取ったトークンを保存するはず（保存回数 0）。");

            Assert.AreEqual(1, _storage.SaveCount, "保存は 1 回だけのはず。");
            Assert.AreEqual(1, _storage.Count, "ホスト 1 件分のレコードだけが残るはず。");

            Assert.IsTrue(
                SessionTokenHostKey.TryCreate(LoopbackAddress, HostPort, out var hostKey),
                "接続先からホストキーを作れるはず。");
            Assert.IsTrue(
                _clientService.SessionTokenStore.TryGet(hostKey, DateTime.UtcNow, out var savedToken),
                $"'{hostKey}' のトークンが保存されているはず。");
            Assert.IsTrue(savedToken.HasValue);

            yield return WaitUntil(
                () => CountEntries(HostLobby) == 2,
                () => $"ホスト + 1 名で 2 件になるはず（実際: {CountEntries(HostLobby)} 件）。");

            Assert.IsTrue(TryFindEntry(HostLobby, PlayerName, out var firstEntry));
            var firstClientId = firstEntry.ClientId;

            // --- 切断 ---
            _clientService.Stop();
            yield return WaitUntil(
                () => !ClientManagers[0].IsClient && !ClientManagers[0].ShutdownInProgress,
                () => "クライアントの停止が完了しませんでした。");

            yield return WaitUntil(
                () => TryFindEntry(HostLobby, PlayerName, out var e) && !e.IsConnected,
                () => "切断が名簿へ反映されるはず。");

            // --- 2 回目の接続: 保存済みトークンが自動で載り、同じ席へ戻る ---
            yield return StartClientAndWaitForConnection();

            yield return WaitUntil(
                () => TryFindEntry(HostLobby, PlayerName, out var e) && e.IsConnected,
                () => $"保存済みトークンで復帰できるはず（切断理由: '{ClientManagers[0].DisconnectReason}'）。");

            Assert.AreEqual(2, CountEntries(HostLobby), "復帰では新しい席を作らないはず。");

            Assert.IsTrue(TryFindEntry(HostLobby, PlayerName, out var restored));
            Assert.AreNotEqual(firstClientId, restored.ClientId, "新しいクライアント ID で同じ席へ戻るはず。");
            Assert.AreEqual(0.0, restored.DisconnectedAtServerTime, 1e-9, "復帰時に切断時刻はクリアされるはず。");

            // 再接続では新しいトークンを配らない（クライアントの有効なトークンを上書きしない。#69）。
            Assert.AreEqual(1, _storage.SaveCount, "再接続ではトークンを保存し直さないはず。");
            Assert.IsTrue(
                _clientService.SessionTokenStore.TryGet(hostKey, DateTime.UtcNow, out var afterReconnect));
            Assert.AreEqual(
                savedToken.ToHex(), afterReconnect.ToHex(), "保存済みトークンは 1 回目のものと同じはず。");
        }

        /// <summary>
        /// ロビーを立てていないホスト（テストの土台や将来の検証ツール）に繋いだ場合は、
        /// 記録だけ残して接続そのものは続ける（issue #85 の M2 後半）。
        /// </summary>
        [UnityTest]
        public IEnumerator StartClient_WhenTheHostHasNoLobbyState_LogsAndKeepsTheConnection()
        {
            var hostResult = HostService.StartHost(startPort: 0, hostPlayerName: HostPlayerName);
            Assert.IsTrue(hostResult.Success, hostResult.Message);
            Assert.AreNotEqual(0, HostService.ActivePort);

            CreateClientService();

            LogAssert.Expect(
                LogType.Log, "[NetworkService] ホストに LobbyState が無いため、再接続トークンは保存されません。");

            var result = _clientService.StartClient(LoopbackAddress, HostService.ActivePort, PlayerName);
            Assert.IsTrue(result.Success, result.Message);

            yield return WaitUntil(
                () => ClientManagers[0].IsConnectedClient,
                () => $"接続は続けられるはず（切断理由: '{ClientManagers[0].DisconnectReason}'）。");

            // 記録が出るのは接続完了のコールバック内なので、この時点で既に出ている。
            Assert.AreEqual(0, _storage.SaveCount, "保存対象のトークンは届かないはず。");
        }

        /// <summary>
        /// クライアント 0 番の <see cref="NetworkManager"/> を <see cref="NetworkService"/> で包み、
        /// 保管庫をメモリ上のフェイクに差し替える（実ファイルに触れさせない）。
        /// </summary>
        private void CreateClientService()
        {
            _storage = new FakeSessionTokenStorage();
            _clientService = new NetworkService(ClientManagers[0])
            {
                SessionTokenStore = new SessionTokenStore(_storage),
            };
        }

        /// <summary>
        /// <see cref="NetworkService.StartClient"/> で接続し、接続完了まで待つ。
        /// 承認ペイロード（保存済みトークンを含む）の組み立てもサービス側が行う。
        /// </summary>
        private IEnumerator StartClientAndWaitForConnection()
        {
            var result = _clientService.StartClient(LoopbackAddress, HostPort, PlayerName);
            Assert.IsTrue(result.Success, result.Message);

            yield return WaitUntil(
                () => ClientManagers[0].IsConnectedClient,
                () => $"クライアントが接続できませんでした（切断理由: '{ClientManagers[0].DisconnectReason}'）。");
        }
    }
}
