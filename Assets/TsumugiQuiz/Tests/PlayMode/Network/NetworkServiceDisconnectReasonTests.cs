using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tests.Shared.Core.Network;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// ホストから届く切断理由を、<see cref="NetworkService.DisconnectedFromHost"/> が日本語の文言に対応づけてから渡すことを
    /// 検証する（issue #206 / #208、docs/network.md §2.4・§9）。
    /// NGO が実際に入れる文字列（<see cref="NgoDisconnectReasons"/>）もここで確かめるので、NGO を更新して文言が変わったら落ちる。
    /// 改変されたホストは、承認コールバックの差し替えで模す。
    /// </summary>
    public class NetworkServiceDisconnectReasonTests : LobbyStateTestFixture
    {
        private const string PlayerName = "つむぎ";
        private const string LoopbackAddress = "127.0.0.1";

        /// <summary>停止が済んだあと、遅れて通知が来ないかを見るフレーム数。</summary>
        private const int FramesToWatchAfterStop = 10;

        private NetworkService _clientService;
        private int _disconnectedCount;
        private string _received;

        /// <summary>フィクスチャのインスタンスはテスト間で使い回されるので、受け取った結果を毎回消す。</summary>
        [SetUp]
        public void ResetReceived()
        {
            _disconnectedCount = 0;
            _received = null;
        }

        [UnityTearDown]
        public IEnumerator DisposeClientService()
        {
            if (_clientService != null)
            {
                _clientService.Dispose();
                _clientService = null;
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator DisconnectedFromHost_UnknownReasonFromModifiedHost_IsReplacedWithGenericMessage()
        {
            const string visiblePart = "<size=60><color=red>拒否</color></size>";
            var craftedReason = visiblePart + "\r\n\n\n\t理由\u0007" + new string('あ', 200);

            StartHostAndSpawnLobby();
            // 改変されたホスト: 承認コールバックを差し替え、任意の理由で拒否する。
            HostManager.ConnectionApprovalCallback = (request, response) =>
            {
                response.Approved = false;
                response.Reason = craftedReason;
            };

            // 元の理由は詳細ログ（警告）に残す。「<」は Editor のコンソールに解釈させないよう置き換える（#208 レビュー L-3）。
            LogAssert.Expect(LogType.Warning, new Regex(
                @"^\[NetworkService\] 切断理由を「" + Regex.Escape(JoinStatusMessages.DisconnectedWithoutReason)
                + @"」として表示します（分類 Unknown、元の長さ 245、元の理由: ‹size=60>‹color=red>拒否‹/color>‹/size> 理由あ{200}）。$"));

            CreateClientService();
            var result = _clientService.StartClient(LoopbackAddress, HostPort, PlayerName);
            Assert.IsTrue(result.Success, result.Message);

            yield return WaitUntil(() => _received != null, () => "ホストから拒否されるはず。");

            // NGO は受け取ったとおりの文字列を持っている（対応づけているのは NetworkService）。
            Assert.That(ClientManagers[0].DisconnectReason, Is.EqualTo(craftedReason));
            Assert.That(_received, Is.EqualTo(JoinStatusMessages.DisconnectedWithoutReason),
                "未知の理由はそのまま出さず、汎用の文言にするはず。");
        }

        /// <summary>
        /// このアプリの拒否理由はそのまま渡す。バージョンの違うホストから届く、数字入りのバージョン不一致の理由で確かめる
        /// （バージョンは実際には一致しているので、承認コールバックの差し替えで違うバージョンのホストを模す）。
        /// </summary>
        [UnityTest]
        public IEnumerator DisconnectedFromHost_RejectionReasonOfThisApp_IsShownAsIs()
        {
            var reason = ConnectionRejectionMessages.Create(ConnectionRejectionReason.ProtocolVersionMismatch, 99, 1);

            StartHostAndSpawnLobby();
            HostManager.ConnectionApprovalCallback = (request, response) =>
            {
                response.Approved = false;
                response.Reason = reason;
            };

            CreateClientService();
            var result = _clientService.StartClient(LoopbackAddress, HostPort, PlayerName);
            Assert.IsTrue(result.Success, result.Message);

            yield return WaitUntil(() => _received != null, () => "ホストから拒否されるはず。");

            Assert.That(ClientManagers[0].DisconnectReason, Is.EqualTo(reason));
            Assert.That(_received, Is.EqualTo(reason));
        }

        /// <summary>
        /// #204: ビルドの違うクライアントは、本物の承認（<see cref="NetworkService"/> のホスト + 名簿）でバージョン不一致の書式の理由で
        /// 拒否され、クライアントの <see cref="NetworkService.DisconnectedFromHost"/> にはその文言がそのまま届く。
        /// 別のビルドは <see cref="NetworkService.StartClient"/> の <c>clientBuildHash</c> で模す。
        /// </summary>
        [UnityTest]
        public IEnumerator DisconnectedFromHost_ClientOfDifferentBuild_ReceivesVersionMismatchReason()
        {
            const string otherBuildHash = "0123456789abcdef0123456789abcdef";

            StartHostAndSpawnLobby();

            CreateClientService();
            var result = _clientService.StartClient(LoopbackAddress, HostPort, PlayerName, otherBuildHash);
            Assert.IsTrue(result.Success, result.Message);

            yield return WaitUntil(() => _received != null, () => "ビルドが違うので拒否されるはず。");

            var expected = ConnectionRejectionMessages.CreateBuildMismatch(LocalBuildIdentity.BuildHash, otherBuildHash);
            Assert.That(ClientManagers[0].DisconnectReason, Is.EqualTo(expected));
            Assert.That(_received, Is.EqualTo(expected), "自前の文言なので、そのまま Join 画面に渡るはず。");
            StringAssert.StartsWith("バージョンが異なります（ホスト: ", _received);
        }

        /// <summary>
        /// ホストが停止すると、NGO は各クライアントへ英語の理由（<see cref="NgoDisconnectReasons.HostShuttingDown"/>）を送る。
        /// これを「ホストがゲームを終了しました。」にして渡す（#208 の主目的）。
        /// </summary>
        [UnityTest]
        public IEnumerator DisconnectedFromHost_HostStops_ReceivesJapaneseHostShutDown()
        {
            StartHostAndSpawnLobby();
            yield return ConnectClientService();

            HostService.Stop();

            yield return WaitUntil(() => _received != null, () => "ホストの停止でクライアントへ切断が届くはず。");

            Assert.That(ClientManagers[0].DisconnectReason, Is.EqualTo(NgoDisconnectReasons.HostShuttingDown),
                "NGO が送る英語の理由が変わった（NGO を更新した場合は NgoDisconnectReasons を合わせる。docs/network.md §2.4）。");
            Assert.That(_received, Is.EqualTo(DisconnectReasonMessages.HostShutDown));
            Assert.That(_disconnectedCount, Is.EqualTo(1));
        }

        /// <summary>
        /// 自分で <see cref="NetworkService.Stop"/> した切断は、ホストからの切断として通知しない（#208）。
        /// NGO はこの場合もクライアント側の切断コールバックを呼び、診断文字列を DisconnectReason に入れる。
        /// </summary>
        [UnityTest]
        public IEnumerator DisconnectedFromHost_ClientStopsItself_IsNotRaised()
        {
            StartHostAndSpawnLobby();
            yield return ConnectClientService();

            var ngoCallbackCount = 0;
            ClientManagers[0].OnClientDisconnectCallback += _ => ngoCallbackCount++;

            _clientService.Stop();

            yield return WaitUntil(
                () => !ClientManagers[0].IsClient && !ClientManagers[0].ShutdownInProgress,
                () => "クライアントの停止が完了するはず。");
            for (var i = 0; i < FramesToWatchAfterStop; i++)
            {
                yield return null;
            }

            Assert.That(ngoCallbackCount, Is.EqualTo(1), "NGO は自分で停止したときも切断コールバックを呼ぶ（#208 の実測）。");
            Assert.That(_received, Is.Null, "自分で停止した切断は DisconnectedFromHost で通知しないはず。");
            AssertIsNgoDiagnostic(ClientManagers[0].DisconnectReason, NgoDisconnectReasons.TransportShutdownEvent);
        }

        /// <summary>
        /// <see cref="NetworkService.Stop"/> を通らずに NGO が停止した場合（NGO 自身の承認待ちのタイムアウトなど）は、
        /// 診断文字列を汎用の文言にして通知する。NGO の診断文字列の形式もここで確かめる。
        /// </summary>
        [UnityTest]
        public IEnumerator DisconnectedFromHost_NgoShutsDownWithoutStop_ReceivesGenericMessage()
        {
            StartHostAndSpawnLobby();
            yield return ConnectClientService();

            ClientManagers[0].Shutdown();

            yield return WaitUntil(() => _received != null, () => "NGO の停止で切断が通知されるはず。");

            AssertIsNgoDiagnostic(ClientManagers[0].DisconnectReason, NgoDisconnectReasons.TransportShutdownEvent);
            Assert.That(_received, Is.EqualTo(JoinStatusMessages.DisconnectedWithoutReason));
        }

        /// <summary>
        /// 自分で停止した次の接続で、ホストからの切断をまた通知する（停止の印を次の接続へ持ち越さない）。
        /// </summary>
        [UnityTest]
        public IEnumerator DisconnectedFromHost_AfterStopAndReconnect_IsRaisedAgain()
        {
            StartHostAndSpawnLobby();
            yield return ConnectClientService();

            _clientService.Stop();
            yield return WaitUntil(
                () => !ClientManagers[0].IsClient && !ClientManagers[0].ShutdownInProgress,
                () => "クライアントの停止が完了するはず。");
            Assert.That(_received, Is.Null);

            // 最初の席は保持期間の間「切断中」で残るので、別の名前で入り直す。
            yield return ConnectClientService("つむぎ二");

            HostService.Stop();

            yield return WaitUntil(() => _received != null, () => "ホストの停止でクライアントへ切断が届くはず。");
            Assert.That(_received, Is.EqualTo(DisconnectReasonMessages.HostShutDown));
        }

        private void CreateClientService()
        {
            _clientService = new NetworkService(ClientManagers[0])
            {
                SessionTokenStore = new SessionTokenStore(new FakeSessionTokenStorage()),
            };
            _clientService.DisconnectedFromHost += reason =>
            {
                _disconnectedCount++;
                _received = reason;
            };
        }

        private IEnumerator ConnectClientService(string playerName = PlayerName)
        {
            if (_clientService == null)
            {
                CreateClientService();
            }

            var result = _clientService.StartClient(LoopbackAddress, HostPort, playerName);
            Assert.IsTrue(result.Success, result.Message);

            var clientManager = ClientManagers[0];
            yield return WaitUntil(
                () => clientManager.IsConnectedClient,
                () => $"クライアントが接続できませんでした（切断理由: '{clientManager.DisconnectReason}'）。");
        }

        private static void AssertIsNgoDiagnostic(string reason, string expectedEvent)
        {
            StringAssert.StartsWith(NgoDisconnectReasons.DisconnectEventHeader, reason,
                "NGO の診断文字列の形式が変わった（NGO を更新した場合は DisconnectReasonLocalizer を合わせる）。");
            StringAssert.Contains($"[{expectedEvent}]", reason);
            Assert.That(DisconnectReasonLocalizer.Localize(reason).Category, Is.EqualTo(DisconnectReasonCategory.TransportEvent),
                $"実際の診断文字列を NGO の形式として読めるはず: {reason}");
        }
    }
}
