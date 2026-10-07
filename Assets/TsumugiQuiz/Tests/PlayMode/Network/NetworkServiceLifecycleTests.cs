using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Network;
using Unity.Netcode;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// ホスト起動 → 自己接続 → 停止 → 再開始、および <see cref="NetworkService.Dispose"/> の挙動。
    /// </summary>
    public class NetworkServiceLifecycleTests : NetworkServiceTestFixture
    {
        [UnityTest]
        public IEnumerator StartHost_ThenStop_CompletesLifecycle()
        {
            var result = Service.StartHost(startPort: 0, hostPlayerName: "  つむぎ  ");

            Assert.IsTrue(result.Success, result.Message);
            Assert.IsTrue(Service.IsHost, "ホストとして動作しているはず。");
            Assert.IsTrue(Service.IsListening, "待ち受けを開始しているはず。");

            // ポート 0 を指定した場合も、実際にバインドされたポートが入る。
            Assert.AreNotEqual(0, Service.ActivePort);

            // ゲーム進行・問題配信の器（#12 / #13）はホスト開始時にスポーンされる。
            Assert.IsNotNull(Service.ActiveGameSession, "ホスト開始で GameSession がスポーンされるはず。");
            Assert.IsTrue(Service.ActiveGameSession.IsSpawned, "GameSession がスポーン済みであるはず。");
            Assert.IsNotNull(Service.ActiveGameSession.Distributor, "QuestionDistributor も同じ器に載るはず。");

            // ホスト名は正規化して保持する（#7 のロビー表示で使う）。
            Assert.AreEqual("つむぎ", Service.LocalPlayerName);

            // ホスト自身のクライアント ID は NetworkManager.ServerClientId（= 0）になる
            // （docs/network.md §1.1）。ホストは「サーバー兼クライアント」なので
            // 接続イベントも自分自身に対して発火する。
            Assert.AreEqual(NetworkManager.ServerClientId, NetworkManagerUnderTest.LocalClientId);
            CollectionAssert.Contains(ConnectedClientIds, NetworkManager.ServerClientId);

            yield return null;

            Service.Stop();

            // Shutdown は次の更新で処理される。
            yield return null;
            yield return null;

            Assert.IsFalse(NetworkManagerUnderTest.IsListening, "停止後は待ち受けていないはず。");
            Assert.IsFalse(Service.IsClient, "停止後はクライアントでもないはず。");
            Assert.AreEqual(0, Service.ActivePort);
            Assert.IsNull(Service.ActiveGameSession, "停止時に GameSession は Despawn されるはず。");
        }

        [UnityTest]
        public IEnumerator StartHost_WithInvalidHostPlayerName_Fails()
        {
            var result = Service.StartHost(startPort: 0, hostPlayerName: "   ");

            Assert.IsFalse(result.Success);
            Assert.IsFalse(Service.IsListening);

            yield return null;
        }

        [UnityTest]
        public IEnumerator StartHost_AfterStop_CanStartAgain()
        {
            var first = Service.StartHost(startPort: 0);
            Assert.IsTrue(first.Success, first.Message);
            yield return null;

            Service.Stop();

            // NGO の Shutdown() はフレーム終端で実処理が走るため、同フレームの再開始は失敗する。
            var tooEarly = Service.StartHost(startPort: 0);
            Assert.IsFalse(tooEarly.Success, "停止処理中の再開始は失敗するはず。");
            Assert.IsTrue(Service.IsShutdownInProgress);

            // 停止完了を待ってから開始するコルーチンなら成功する。
            var second = NetworkStartResult.Fail("未実行");
            yield return Service.StartHostWhenReady(startPort: 0, onCompleted: r => second = r);

            Assert.IsTrue(second.Success, second.Message);
            Assert.IsTrue(Service.IsHost);
            Assert.IsFalse(Service.IsShutdownInProgress);

            Service.Stop();
            yield return null;
        }

        /// <summary>
        /// #101: <see cref="NetworkService.FindActiveGameSession"/> は、破棄済みでも
        /// <see cref="System.ObjectDisposedException"/> を投げずに null を返す。
        /// UI（<c>GameView</c> / <c>LobbyView</c> / <c>SettingsView</c>）がセッションを見つけるまで
        /// 毎フレーム呼ぶ「探索」API であり、ホスト停止・アプリ終了の直後にも呼ばれうるため。
        /// 他の API（<see cref="NetworkService.StartHost"/> 等）はこれまでどおり例外を投げる。
        /// </summary>
        [UnityTest]
        public IEnumerator FindActiveGameSession_AfterDispose_ReturnsNullInsteadOfThrowing()
        {
            var result = Service.StartHost(startPort: 0);
            Assert.IsTrue(result.Success, result.Message);
            yield return null;

            Assert.IsNotNull(Service.FindActiveGameSession(), "破棄前はスポーン済みの GameSession が見つかるはず。");

            var service = Service;

            // フィクスチャの TearDown による二重 Dispose を避けるため、先に手放してから破棄する。
            Service = null;
            service.Dispose();

            Assert.IsTrue(service.IsDisposed, "Dispose 後は IsDisposed が true になるはず。");
            Assert.IsNull(service.FindActiveGameSession(), "破棄後の探索は例外ではなく null を返すはず。");
            Assert.Throws<System.ObjectDisposedException>(
                () => service.StartHost(startPort: 0),
                "探索以外の API は破棄後に例外を投げる契約のままであるはず。");

            // NGO の Shutdown はフレーム終端で処理されるため、TearDown の DestroyImmediate より前に待つ。
            yield return null;
            yield return null;
        }

        /// <summary>
        /// #116: <see cref="NetworkService.Dispose"/> は <see cref="NetworkService.Stop"/> を呼んでから
        /// <c>_disposed</c> を立てるため、その間に発火する <see cref="NetworkService.Stopped"/> の
        /// ハンドラから見ると、以前は <see cref="NetworkService.IsDisposed"/> がまだ false だった。
        /// <c>_disposing</c> フラグにより、<see cref="NetworkService.Stopped"/> のハンドラの中でも
        /// true になることを確認する。
        /// </summary>
        [UnityTest]
        public IEnumerator Dispose_WhileStoppedEventHandlerRuns_IsDisposedIsAlreadyTrue()
        {
            var result = Service.StartHost(startPort: 0);
            Assert.IsTrue(result.Success, result.Message);
            yield return null;

            var service = Service;

            // フィクスチャの TearDown による二重 Dispose を避けるため、先に手放してから破棄する。
            Service = null;

            var isDisposedDuringStoppedEvent = false;
            service.Stopped += () => isDisposedDuringStoppedEvent = service.IsDisposed;

            service.Dispose();

            Assert.IsTrue(
                isDisposedDuringStoppedEvent,
                "Dispose() が発火する Stopped ハンドラの中でも IsDisposed は true であるはず。");
            Assert.IsTrue(service.IsDisposed, "Dispose 完了後も IsDisposed は true のまま。");

            // NGO の Shutdown はフレーム終端で処理されるため、TearDown の DestroyImmediate より前に待つ。
            yield return null;
            yield return null;

            // レビュー L-6: IsDisposed の判定だけでなく、実際に NGO 側も停止まで完了していることを確認する。
            Assert.IsFalse(NetworkManagerUnderTest.IsListening, "Dispose 後は NetworkManager も待ち受けていないはず。");
        }

        [UnityTest]
        public IEnumerator Dispose_UnsubscribesFromNetworkManagerEvents()
        {
            var connectedCount = 0;
            Service.ClientConnected += _ => connectedCount++;

            Service.Dispose();
            Service = null;

            // Dispose 後は NetworkManager を直接動かしてもイベントが流れてこない。
            // 承認コールバックも解除済みなので ConnectionApproval は切っておく。
            NetworkManagerUnderTest.NetworkConfig.ConnectionApproval = false;
            Assert.IsNull(NetworkManagerUnderTest.ConnectionApprovalCallback, "承認コールバックも解除されるはず。");
            Assert.IsTrue(NetworkManagerUnderTest.StartHost(), "NetworkManager 単体では開始できるはず。");
            yield return null;

            Assert.AreEqual(0, connectedCount, "Dispose 後はイベントを受け取らないはず。");

            NetworkManagerUnderTest.Shutdown();
            yield return null;
        }
    }
}
