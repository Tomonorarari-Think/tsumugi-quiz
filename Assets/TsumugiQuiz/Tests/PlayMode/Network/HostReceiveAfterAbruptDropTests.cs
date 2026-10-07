using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.TestTools;
using TransportEvent = Unity.Networking.Transport.NetworkEvent;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// issue #163: クライアントが切断通知なしに消えた（プロセスの強制終了など）あと、
    /// ホストが新しい接続を受け付けられなくなる問題の再現テスト。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 原因（一次情報）: Unity Transport 6.6.0 の <c>UDPNetworkInterface.ReceiveJob.Execute</c> は、
    /// 受信完了が <c>Failed</c> のとき <c>failedCount++; continue;</c> するだけで、その受信要求のバッファを
    /// <c>ReleaseBuffer</c> しない。Windows では、閉じたポートへ送った UDP に対する ICMP port unreachable が
    /// 次の受信の失敗（WSAECONNRESET）として返るため、消えた相手へハートビートを送るたびに受信バッファが 1 つずつ
    /// 失われ、受信キュー（<c>MaxPacketQueueSize</c>）を使い切るとホストは何も受信できなくなる。
    /// Unity スタッフが不具合と認めている:
    /// https://discussions.unity.com/t/unity-transport-6-6-0-udp-host-stops-receiving-after-abrupt-client-disconnect-on-windows/1736023
    /// </para>
    /// <para>
    /// 再現を短時間で決定的にするため、ホストの受信キューを小さく（<see cref="SmallQueueSize"/>）、
    /// ハートビート間隔を短く（<see cref="FastHeartbeatMs"/>）する。消えるクライアントは NGO を介さない素の
    /// <see cref="NetworkDriver"/> で接続し、<see cref="NetworkDriver.Dispose"/> で切断通知を送らずにソケットを閉じる
    /// （プロセス強制終了と同じく、ホストからは「突然応答しなくなった相手」に見える）。
    /// </para>
    /// <para>
    /// 実 UDP ソケットと OS の ICMP 挙動に依存するため <c>[Category("Network")]</c>
    /// （<c>scripts/verify.ps1 -IncludeNetwork</c> で実行）。ICMP を返さない環境（Windows 以外等）では
    /// 受信の失敗が起きないため、修正の有無に関わらず成功する。
    /// </para>
    /// </remarks>
    [Category("Network")]
    public sealed class HostReceiveAfterAbruptDropTests
    {
        private const int SmallQueueSize = 32;
        private const int FastHeartbeatMs = 20;
        private const float TimeoutSeconds = 15f;

        private GameObject _hostObject;
        private GameObject _clientObject;
        private NetworkManager _host;
        private NetworkManager _client;
        private NetworkDriver _victimDriver;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_victimDriver.IsCreated)
            {
                _victimDriver.Dispose();
                _victimDriver = default;
            }

            if (_client != null && _client.IsListening)
            {
                _client.Shutdown();
            }

            if (_host != null && _host.IsListening)
            {
                _host.Shutdown();
            }

            // Shutdown の後始末（トランスポートの破棄）が済んでから GameObject を消す。
            // 同じフレームで DestroyImmediate すると OnDestroy から二重に Shutdown が走り、
            // 破棄済みの NetworkDriver に触れて ObjectDisposedException になる。
            var elapsed = 0f;
            while (((_client != null && _client.ShutdownInProgress) || (_host != null && _host.ShutdownInProgress))
                   && elapsed < TimeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            yield return null;

            if (_clientObject != null)
            {
                Object.DestroyImmediate(_clientObject);
            }

            if (_hostObject != null)
            {
                Object.DestroyImmediate(_hostObject);
            }
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator 切断通知なしにクライアントが消えても_新しいクライアントが接続できる()
        {
            yield return RunScenario(SmallQueueSize, gracefulDisconnect: false);
        }

        /// <summary>対照: 切断通知を送って去った場合は、受信の失敗が起きないので新しいクライアントが接続できる。</summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator 対照_切断通知を送って去った場合は新しいクライアントが接続できる()
        {
            yield return RunScenario(SmallQueueSize, gracefulDisconnect: true);
        }

        private IEnumerator RunScenario(int hostQueueSize, bool gracefulDisconnect)
        {
            _hostObject = CreateManager("HostAbruptDrop", out _host, out var hostTransport);
            hostTransport.MaxPacketQueueSize = hostQueueSize;
            hostTransport.HeartbeatTimeoutMS = FastHeartbeatMs;
            hostTransport.SetConnectionData("127.0.0.1", 0, "127.0.0.1");
            Assert.IsTrue(_host.StartHost(), "ホストを開始できるはず。");
            var port = hostTransport.GetLocalEndpoint().Port;
            Assert.AreNotEqual(0, port, "ホストが実際にバインドしたポートを取得できるはず。");

            // --- 1. 素の NetworkDriver で接続し、去る ---
            _victimDriver = NetworkDriver.Create();
            var connection = _victimDriver.Connect(NetworkEndpoint.LoopbackIpv4.WithPort(port));
            var connected = false;
            var elapsed = 0f;
            while (!connected && elapsed < TimeoutSeconds)
            {
                _victimDriver.ScheduleUpdate().Complete();
                TransportEvent.Type type;
                while ((type = connection.PopEvent(_victimDriver, out _)) != TransportEvent.Type.Empty)
                {
                    if (type == TransportEvent.Type.Connect)
                    {
                        connected = true;
                    }
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsTrue(connected, "素の NetworkDriver がトランスポート層でホストへ接続できるはず。");

            if (gracefulDisconnect)
            {
                // 切断通知を送ってから閉じる（ホストはすぐに相手を忘れ、以後そのポートへは送らない）。
                _victimDriver.Disconnect(connection);
                _victimDriver.ScheduleUpdate().Complete();
                yield return null;
                _victimDriver.ScheduleUpdate().Complete();
            }

            // 通知なし: ソケットを閉じるだけ（プロセスの強制終了と同じ見え方）。
            _victimDriver.Dispose();
            _victimDriver = default;

            // ホストは消えた相手へハートビートを送り続ける（Windows では送るたびに ICMP が返り、受信が 1 回失敗する）。
            // 受信キューを使い切るのに十分な回数（SmallQueueSize の数倍）だけ待つ。
            yield return new WaitForSecondsRealtime(FastHeartbeatMs / 1000f * SmallQueueSize * 4);

            // --- 2. 新しいクライアントが接続できるか ---
            _clientObject = CreateManager("ClientAfterDrop", out _client, out var clientTransport);
            clientTransport.ConnectTimeoutMS = 500;
            clientTransport.MaxConnectAttempts = 16;
            clientTransport.SetConnectionData("127.0.0.1", port);
            Assert.IsTrue(_client.StartClient(), "クライアントを開始できるはず。");

            elapsed = 0f;
            while (!_client.IsConnectedClient && elapsed < TimeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsTrue(
                _client.IsConnectedClient,
                "去ったクライアントがいても、ホストは新しい接続を受け付けるはず（#163）。"
                + $"切断理由: '{_client.DisconnectReason}'");
        }

        private static GameObject CreateManager(string name, out NetworkManager manager, out UnityTransport transport)
        {
            var gameObject = new GameObject(name);
            gameObject.SetActive(false);

            transport = gameObject.AddComponent<UnityTransport>();
            manager = gameObject.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = null,
                ConnectionApproval = false,
            };

            gameObject.SetActive(true);
            return gameObject;
        }
    }
}
