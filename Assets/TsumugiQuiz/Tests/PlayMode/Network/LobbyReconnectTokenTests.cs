using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 再接続トークンで同名なりすましを防げることを、実際に接続させて検証する PlayMode テスト
    /// （issue #69 の受け入れ条件、docs/network.md §2.3 の K-N1）。
    /// </summary>
    /// <remarks>
    /// 「得点」まで含めた検証は <c>GameSession</c>（#18）が必要なのでここでは扱わない。
    /// 得点は名簿のエントリ（席）に紐づくため、席を奪われないことを確認すれば
    /// 得点の乗っ取り経路も塞げている（詳細は PR 本文の「未対応事項」）。
    /// </remarks>
    public class LobbyReconnectTokenTests : LobbyStateTestFixture
    {
        private const string VictimName = "つむぎ";

        [UnityTest]
        public IEnumerator ImposterWithoutToken_CannotTakeTheSeatOfADisconnectedPlayer()
        {
            // 定員 2（ホスト + 1 名）。落ちた 1 名の席は保持期間の間ずっと確保されている。
            StartHostAndSpawnLobby(maxPlayers: 2);

            yield return ConnectClient(0, VictimName);

            var token = SessionToken.None;
            yield return WaitForSessionToken(0, received => token = received);
            Assert.IsTrue(token.HasValue, "ホストは承認したクライアントへトークンを発行するはず。");

            yield return WaitUntil(() => CountEntries(HostLobby) == 2, () => "ホスト + 1 名で 2 件になるはず。");

            yield return DisconnectClient(0);
            yield return WaitUntil(
                () => TryFindEntry(HostLobby, VictimName, out var e) && !e.IsConnected,
                () => "切断が名簿へ反映されるはず。");

            // 同名・トークン無しの別クライアントは新規参加として評価され、満室なので拒否される。
            yield return ConnectClientExpectingRejection(1, VictimName);

            Assert.AreEqual(
                ConnectionRejectionMessages.SeatReserved,
                GetDisconnectReason(1),
                "席は本人の復帰用に確保されている、という理由で拒否されるはず。");

            Assert.AreEqual(2, CountEntries(HostLobby), "なりすましは名簿に載らないはず。");
            Assert.IsTrue(TryFindEntry(HostLobby, VictimName, out var stillDisconnected));
            Assert.IsFalse(stillDisconnected.IsConnected, "本人のエントリは切断中のまま残るはず。");

            // 本人はトークンを持っているので、同じ席へ復帰できる。
            yield return ConnectClient(0, VictimName, token);

            yield return WaitUntil(
                () => TryFindEntry(HostLobby, VictimName, out var e) && e.IsConnected,
                () => "トークンが一致する本人は復帰できるはず。");

            Assert.AreEqual(2, CountEntries(HostLobby), "復帰では新しい席を作らないはず。");
            Assert.IsTrue(TryFindEntry(HostLobby, VictimName, out var restored));
            Assert.AreEqual(0.0, restored.DisconnectedAtServerTime, 1e-9, "復帰時に切断時刻はクリアされるはず。");
        }

        [UnityTest]
        public IEnumerator ImposterWithoutToken_JoinsAsNewPlayer_WhenSeatsRemain()
        {
            // 空席がある部屋では、同名・トークン無しの接続は「新規参加」として通る（席は別に取る）。
            StartHostAndSpawnLobby(maxPlayers: 6);

            yield return ConnectClient(0, VictimName);

            var token = SessionToken.None;
            yield return WaitForSessionToken(0, received => token = received);

            yield return WaitUntil(() => CountEntries(HostLobby) == 2, () => "ホスト + 1 名で 2 件になるはず。");
            Assert.IsTrue(TryFindEntry(HostLobby, VictimName, out var before));
            var victimClientId = before.ClientId;

            yield return DisconnectClient(0);
            yield return WaitUntil(
                () => TryFindEntryByClientId(HostLobby, victimClientId, out var e) && !e.IsConnected,
                () => "切断が名簿へ反映されるはず。");

            yield return ConnectClient(1, VictimName);

            yield return WaitUntil(
                () => CountEntries(HostLobby) == 3,
                () => $"同名でも新規の席として増えるはず（実際: {CountEntries(HostLobby)}）。");

            Assert.IsTrue(
                TryFindEntryByClientId(HostLobby, victimClientId, out var keptEntry),
                "本人の席（切断中エントリ）は残っているはず。");
            Assert.IsFalse(keptEntry.IsConnected, "なりすましが本人のエントリを復帰させていないこと。");

            // 本人はトークンで自分の席へ戻れる（同名の接続中プレイヤーが居ても、
            // 再接続の判定は名前重複の判定より先に行われる）。
            yield return ConnectClient(0, VictimName, token);

            yield return WaitUntil(
                () => CountDisconnectedEntries(HostLobby) == 0,
                () => "本人が復帰すると切断中のエントリは無くなるはず"
                      + $"（実際: {CountDisconnectedEntries(HostLobby)} 件）。");

            Assert.AreEqual(3, CountEntries(HostLobby), "復帰では席を増やさないはず（ホスト + 本人 + なりすまし）。");
        }

        /// <summary>名簿のうち「切断中」のエントリ数（ホスト側の実名スナップショットから数える）。</summary>
        private static int CountDisconnectedEntries(LobbyState lobby)
        {
            if (lobby == null || !lobby.IsSpawned)
            {
                return -1;
            }

            var count = 0;
            foreach (var entry in lobby.GetPlayersSnapshot())
            {
                if (!entry.IsConnected)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
