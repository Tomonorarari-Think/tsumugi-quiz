using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using TsumugiQuiz.Network;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// ポートが使用中のときの +1 リトライ（受け入れ条件 2、docs/network.md §2.1）と、
    /// Transport 失敗通知のタイミング（リトライ中は通知しない）を確認する。
    ///
    /// <see cref="NetworkService"/> は UTP に渡す前に自前でポートの空きを確認するため、
    /// これらのケースでも <c>Debug.LogError</c>（"Server failed to bind"）は出ない。
    /// ログが汚れないので通常の <c>scripts/verify.ps1</c> にそのまま含められる。
    /// </summary>
    public class NetworkServicePortTests : NetworkServiceTestFixture
    {
        [UnityTest]
        public IEnumerator StartHost_UsesNextPort_WhenRequestedPortIsTaken()
        {
            // OS に空きポートを 1 つ選ばせ、その UDP ポートを塞いだ状態でホストを開始する。
            using (var blocker = new UdpClient(new IPEndPoint(IPAddress.Any, 0)))
            {
                var blockedPort = (ushort)((IPEndPoint)blocker.Client.LocalEndPoint).Port;

                var transportFailures = 0;
                Service.TransportFailed += () => transportFailures++;

                var result = Service.StartHost(startPort: blockedPort);

                Assert.IsTrue(result.Success, result.Message);
                Assert.Greater(result.Port, blockedPort, "使用中のポートを避けて +1 以降のポートを使うはず。");
                Assert.AreEqual(result.Port, Service.ActivePort);

                // 再試行して成功した場合は Transport 失敗として通知しない（H1）。
                Assert.AreEqual(0, transportFailures);
            }

            yield return null;

            Service.Stop();
            yield return null;
        }

        [UnityTest]
        public IEnumerator StartHost_WhenAllPortsAreTaken_FailsAndNotifiesOnce()
        {
            var blockers = new List<UdpClient>();
            try
            {
                var basePort = ReserveConsecutivePorts(blockers, NetworkConstants.PortRetryCount);
                if (basePort == 0)
                {
                    Assert.Ignore("連続した空きポートを確保できなかったため検証をスキップしました。");
                }

                var transportFailures = 0;
                Service.TransportFailed += () => transportFailures++;

                var result = Service.StartHost(startPort: basePort);

                Assert.IsFalse(result.Success, "すべてのポートが使用中なら失敗するはず。");
                StringAssert.Contains("使用中", result.Message);
                Assert.AreEqual(0, result.Port);
                Assert.IsFalse(Service.IsListening);

                // 全滅したときに 1 回だけ通知する。
                Assert.AreEqual(1, transportFailures);
            }
            finally
            {
                DisposeAll(blockers);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator StartHost_WhenPortRangeIsExceeded_ReportsRangeMessage()
        {
            // 65535 を塞いだ状態で 65535 から開始すると、次の候補が範囲外になる。
            // すでに他プロセスが使っていて塞げなかった場合も、結果は同じ（範囲超過）。
            UdpClient blocker = null;
            try
            {
                try
                {
                    blocker = new UdpClient(new IPEndPoint(IPAddress.Any, ushort.MaxValue));
                }
                catch (SocketException)
                {
                    blocker = null;
                }

                var result = Service.StartHost(startPort: ushort.MaxValue);

                Assert.IsFalse(result.Success, "候補ポートが尽きるので失敗するはず。");
                StringAssert.Contains("上限", result.Message);
                Assert.IsFalse(Service.IsListening);
            }
            finally
            {
                blocker?.Dispose();
            }

            yield return null;
        }

        /// <summary>
        /// 連続した空きポートを確保する。確保できたら先頭のポート番号、できなければ 0 を返す。
        /// </summary>
        private static ushort ReserveConsecutivePorts(List<UdpClient> blockers, int count)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                DisposeAll(blockers);

                ushort candidateBase;
                using (var seed = new UdpClient(new IPEndPoint(IPAddress.Any, 0)))
                {
                    candidateBase = (ushort)((IPEndPoint)seed.Client.LocalEndPoint).Port;
                }

                if (candidateBase == 0 || candidateBase + count > ushort.MaxValue)
                {
                    continue;
                }

                var reserved = true;
                for (var offset = 0; offset < count; offset++)
                {
                    try
                    {
                        blockers.Add(new UdpClient(new IPEndPoint(IPAddress.Any, candidateBase + offset)));
                    }
                    catch (SocketException)
                    {
                        reserved = false;
                        break;
                    }
                }

                if (reserved)
                {
                    return candidateBase;
                }
            }

            return 0;
        }

        private static void DisposeAll(List<UdpClient> blockers)
        {
            foreach (var blocker in blockers)
            {
                blocker.Dispose();
            }

            blockers.Clear();
        }
    }
}
