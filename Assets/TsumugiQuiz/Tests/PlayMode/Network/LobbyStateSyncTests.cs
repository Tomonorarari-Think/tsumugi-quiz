using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// ホスト + クライアント 2 を実際に接続させ、ロビー名簿の同期・切断・再接続・
    /// 拒否理由を検証する PlayMode テスト（docs/network.md §2.3 / §2.4、issue #7 の受け入れ条件）。
    /// </summary>
    public class LobbyStateSyncTests : LobbyStateTestFixture
    {
        private const string ClientAName = "つむぎ";
        private const string ClientBName = "あかね";

        [UnityTest]
        public IEnumerator TwoClients_Join_RosterSyncsToEveryPeer()
        {
            StartHostAndSpawnLobby();

            yield return ConnectClient(0, ClientAName);
            yield return ConnectClient(1, ClientBName);

            // ホスト + クライアント 2 の 3 件が全ピアで一致すること。
            yield return WaitUntil(
                () => CountEntries(HostLobby) == 3,
                () => $"ホスト側の名簿が 3 件になるはず（実際: {CountEntries(HostLobby)}）。");

            yield return WaitUntil(
                () => CountEntries(FindClientLobby(0)) == 3 && CountEntries(FindClientLobby(1)) == 3,
                () => "両クライアントへ名簿が同期されるはず"
                      + $"（client0: {CountEntries(FindClientLobby(0))}, client1: {CountEntries(FindClientLobby(1))}）。");

            foreach (var name in new[] { HostPlayerName, ClientAName, ClientBName })
            {
                Assert.IsTrue(TryFindEntry(HostLobby, name, out var hostSide), $"ホスト側に '{name}' があるはず。");
                Assert.IsTrue(hostSide.IsConnected, $"'{name}' は接続中のはず。");

                Assert.IsTrue(TryFindEntry(FindClientLobby(0), name, out var clientSide), $"クライアント側に '{name}' があるはず。");
                Assert.AreEqual(hostSide.ClientId, clientSide.ClientId, $"'{name}' のクライアント ID が一致するはず。");
                Assert.AreEqual(hostSide.IsHost, clientSide.IsHost, $"'{name}' のホストマークが一致するはず。");
                Assert.AreEqual(hostSide.IsConnected, clientSide.IsConnected, $"'{name}' の接続状態が一致するはず。");
            }

            Assert.IsTrue(TryFindEntry(HostLobby, HostPlayerName, out var hostEntry));
            Assert.IsTrue(hostEntry.IsHost, "ホストのエントリには isHost が立つはず。");
            Assert.IsFalse(hostEntry.IsModerator, "既定（host.role = player）では司会マークは付かないはず。");
        }

        [UnityTest]
        public IEnumerator ClientDisconnect_KeepsEntry_AndMarksItDisconnected()
        {
            StartHostAndSpawnLobby();
            yield return ConnectClient(0, ClientAName);
            yield return ConnectClient(1, ClientBName);

            yield return WaitUntil(() => CountEntries(HostLobby) == 3, () => "3 件になるはず。");

            yield return DisconnectClient(0);

            yield return WaitUntil(
                () => TryFindEntry(HostLobby, ClientAName, out var e) && !e.IsConnected,
                () => "切断したクライアントのエントリが「切断中」になるはず。");

            Assert.AreEqual(3, CountEntries(HostLobby), "エントリは削除されず残るはず（docs/network.md §2.4）。");

            // 残っているクライアントにも「切断中」が伝わること。
            // レビュー H-2: 切断中エントリの名前はクライアントへは伏せ字で配るので、
            // 名前ではなくクライアント ID で引く。
            Assert.IsTrue(TryFindEntry(HostLobby, ClientAName, out var hostSideEntry));
            var disconnectedClientId = hostSideEntry.ClientId;

            yield return WaitUntil(
                () => TryFindEntryByClientId(FindClientLobby(1), disconnectedClientId, out var e) && !e.IsConnected,
                () => "残っているクライアントにも切断状態が同期されるはず。");

            Assert.AreEqual(3, CountEntries(FindClientLobby(1)), "クライアント側でもエントリ数は変わらないはず。");

            Assert.IsTrue(TryFindEntryByClientId(FindClientLobby(1), disconnectedClientId, out var maskedEntry));
            Assert.AreEqual(
                PlayerEntry.DisconnectedNameMask,
                maskedEntry.GetName(),
                "切断中エントリの名前はクライアントには伏せ字で届くはず（レビュー H-2）。");
            Assert.AreEqual(
                ClientAName,
                hostSideEntry.GetName(),
                "ホストの画面にはサーバー側の名簿から実名が出るはず。");
        }

        [UnityTest]
        public IEnumerator Reconnect_WithSameNameAndToken_RestoresTheSameEntry()
        {
            StartHostAndSpawnLobby();
            yield return ConnectClient(0, ClientAName);

            // ホストが発行した再接続トークンを受け取っておく（#69）。
            var token = SessionToken.None;
            yield return WaitForSessionToken(0, received => token = received);

            yield return WaitUntil(() => CountEntries(HostLobby) == 2, () => "ホスト + 1 名で 2 件になるはず。");
            Assert.IsTrue(TryFindEntry(HostLobby, ClientAName, out var before));

            yield return DisconnectClient(0);
            yield return WaitUntil(
                () => TryFindEntry(HostLobby, ClientAName, out var e) && !e.IsConnected,
                () => "切断が反映されるはず。");

            // 同じプレイヤー名 + トークンで再接続する（docs/network.md §2.3 の 6、K-N1 改訂）。
            yield return ConnectClient(0, ClientAName, token);

            yield return WaitUntil(
                () => TryFindEntry(HostLobby, ClientAName, out var e) && e.IsConnected,
                () => "同名 + トークン一致で再接続すると復帰するはず。");

            Assert.AreEqual(2, CountEntries(HostLobby), "新しいエントリを作らず復帰するはず。");
            Assert.IsTrue(TryFindEntry(HostLobby, ClientAName, out var after));
            Assert.IsFalse(after.IsHost, "復帰したエントリはホストではないはず。");
            Assert.AreEqual(0.0, after.DisconnectedAtServerTime, 1e-9, "復帰時に切断時刻はクリアされるはず。");
            Assert.AreEqual(before.GetName(), after.GetName(), "名前は同じはず。");
        }

        [UnityTest]
        public IEnumerator ThirdPlayer_WhenRoomIsFull_IsRejectedWithRoomFullReason()
        {
            // 定員 2（= ホスト + クライアント 1）で満室にする。
            StartHostAndSpawnLobby(maxPlayers: 2);
            yield return ConnectClient(0, ClientAName);

            yield return WaitUntil(() => CountEntries(HostLobby) == 2, () => "ホスト + 1 名で満室になるはず。");

            yield return ConnectClientExpectingRejection(1, ClientBName);

            StringAssert.Contains(
                ConnectionRejectionMessages.RoomFull,
                GetDisconnectReason(1),
                "満室の拒否理由がクライアントへ届くはず。");
            Assert.AreEqual(2, CountEntries(HostLobby), "拒否されたクライアントは名簿に載らないはず。");
        }

        [UnityTest]
        public IEnumerator SeatOfDisconnectedPlayer_IsReservedAndCanBeReleasedByHost()
        {
            // 統括判断 Q3 + レビュー M-3 / H-2: 切断者の席は保持期間の間は確保され、
            // その間の新規参加は SeatReserved で拒否される。ホストは手動で解放できる。
            StartHostAndSpawnLobby(maxPlayers: 2);
            yield return ConnectClient(0, ClientAName);
            yield return WaitUntil(() => CountEntries(HostLobby) == 2, () => "ホスト + 1 名になるはず。");

            yield return DisconnectClient(0);
            yield return WaitUntil(
                () => TryFindEntry(HostLobby, ClientAName, out var e) && !e.IsConnected,
                () => "切断が反映されるはず。");

            yield return ConnectClientExpectingRejection(1, ClientBName);
            StringAssert.Contains(
                ConnectionRejectionMessages.SeatReserved,
                GetDisconnectReason(1),
                "席の確保中であることがクライアントへ伝わるはず。");

            // ホストが切断中エントリを片付けると席が空く。
            Assert.AreEqual(1, HostLobby.RemoveDisconnectedEntries(), "切断中エントリが 1 件削除されるはず。");
            Assert.AreEqual(1, CountEntries(HostLobby), "ホストだけが残るはず。");

            yield return ConnectClient(1, ClientBName);
            yield return WaitUntil(
                () => TryFindEntry(HostLobby, ClientBName, out _),
                () => "席が空いたので新しいクライアントが参加できるはず。");
        }

        [UnityTest]
        public IEnumerator ModeratorHost_IsNotCountedTowardCapacity()
        {
            // 司会専任のホストは定員に数えない（仮決め K18）。定員 2 ならクライアント 2 人まで入れる。
            StartHostAndSpawnLobby(maxPlayers: 2, hostRole: HostRole.Moderator);

            yield return ConnectClient(0, ClientAName);
            yield return ConnectClient(1, ClientBName);

            yield return WaitUntil(
                () => CountEntries(HostLobby) == 3,
                () => $"司会 + クライアント 2 で 3 件になるはず（実際: {CountEntries(HostLobby)}）。");

            Assert.IsTrue(TryFindEntry(HostLobby, HostPlayerName, out var hostEntry));
            Assert.IsTrue(hostEntry.IsModerator, "司会専任のホストには司会マークが付くはず。");
        }

        [UnityTest]
        public IEnumerator GameInProgress_WithoutLateJoin_IsRejectedWithReason()
        {
            StartHostAndSpawnLobby(allowLateJoin: false);

            // #14 / #26 で GameSession のフェーズに接続するまでは、この差し込みで進行中を表す。
            HostLobby.GameInProgressProvider = () => true;

            yield return ConnectClientExpectingRejection(0, ClientAName);

            StringAssert.Contains(
                ConnectionRejectionMessages.GameInProgress,
                GetDisconnectReason(0),
                "進行中の拒否理由がクライアントへ届くはず。");
        }

        [UnityTest]
        public IEnumerator GameInProgress_WithLateJoinAllowed_IsAccepted()
        {
            StartHostAndSpawnLobby(allowLateJoin: true);
            HostLobby.GameInProgressProvider = () => true;

            yield return ConnectClient(0, ClientAName);

            yield return WaitUntil(
                () => TryFindEntry(HostLobby, ClientAName, out _),
                () => "途中参加を許可していれば進行中でも参加できるはず。");
        }

        [UnityTest]
        public IEnumerator DuplicateName_OfConnectedPlayer_IsRejectedWithReason()
        {
            StartHostAndSpawnLobby();
            yield return ConnectClient(0, ClientAName);
            yield return WaitUntil(() => CountEntries(HostLobby) == 2, () => "2 件になるはず。");

            yield return ConnectClientExpectingRejection(1, ClientAName);

            StringAssert.Contains(
                ConnectionRejectionMessages.DuplicatePlayerName,
                GetDisconnectReason(1),
                "名前重複の拒否理由がクライアントへ届くはず。");
            Assert.AreEqual(2, CountEntries(HostLobby), "拒否されたクライアントは名簿に載らないはず。");
        }
    }
}
