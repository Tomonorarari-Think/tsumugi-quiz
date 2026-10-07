using System.Collections.Generic;
using System.Security.Cryptography;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// ロビー名簿（<see cref="LobbyRoster"/>）の純 C# ロジックの検証（docs/network.md §2.3 / §2.4、issue #7）。
    /// 追加・切断・再接続（トークン一致 / 不一致。issue #69）・定員・ホストの役割・
    /// 保持期間切れの削除を網羅する。
    /// </summary>
    public class LobbyRosterTests
    {
        private const string HostName = "ホストさん";
        private const bool InLobby = false;
        private const bool InGame = true;
        private const bool LateJoinDenied = false;
        private const bool LateJoinAllowed = true;

        /// <summary>承認済み・接続完了待ちのクライアントが居ない状態。</summary>
        private static readonly IReadOnlyList<LobbyReservation> NoReservations = System.Array.Empty<LobbyReservation>();

        private static LobbyRoster CreateRoster(
            int maxPlayers = LobbyRoster.DefaultMaxPlayers,
            HostRole hostRole = HostRole.Player,
            string hostName = HostName)
        {
            var roster = new LobbyRoster(maxPlayers, hostRole);
            roster.SetHost(0UL, hostName);
            return roster;
        }

        /// <summary>正常系の入室を 1 件行い、追加されたエントリを返す。</summary>
        private static LobbyPlayer Join(LobbyRoster roster, ulong clientId, string name)
            => Join(roster, clientId, name, out _);

        /// <summary>
        /// 正常系の入室を 1 件行う。サーバーが発行する再接続トークン（#69）も同時に作り、
        /// 名簿にはそのハッシュだけを持たせる（実装と同じ流れ）。
        /// </summary>
        private static LobbyPlayer Join(LobbyRoster roster, ulong clientId, string name, out SessionToken issuedToken)
        {
            var admission = roster.Evaluate(name, SessionToken.None, NoReservations, InLobby, LateJoinDenied);
            Assert.AreEqual(LobbyAdmissionKind.NewPlayer, admission.Kind, $"'{name}' は新規として受け入れられるはず。");

            issuedToken = CreateToken();
            Assert.IsTrue(
                roster.TryApply(clientId, name, admission, SessionTokenHash.Of(issuedToken), out var player),
                "名簿に反映されるはず。");
            return player;
        }

        /// <summary>ホストが発行するのと同じ 128bit トークンを作る。</summary>
        private static SessionToken CreateToken()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                return SessionToken.CreateRandom(rng);
            }
        }

        [Test]
        public void DefaultMaxPlayers_MatchesRoomSettingsDefault()
        {
            // docs/room-settings.md §1 の room.maxPlayers 既定値 6。
            Assert.AreEqual(6, LobbyRoster.DefaultMaxPlayers);
            Assert.AreEqual(2, LobbyRoster.MinMaxPlayers);
            Assert.AreEqual(12, LobbyRoster.MaxMaxPlayers);
        }

        [TestCase(0, 2)]
        [TestCase(1, 2)]
        [TestCase(2, 2)]
        [TestCase(6, 6)]
        [TestCase(12, 12)]
        [TestCase(99, 12)]
        [TestCase(-5, 2)]
        public void ClampMaxPlayers_ClampsToDocumentedRange(int input, int expected)
        {
            Assert.AreEqual(expected, LobbyRoster.ClampMaxPlayers(input));
            Assert.AreEqual(expected, new LobbyRoster(input).MaxPlayers);
        }

        [Test]
        public void SetHost_AddsSingleHostEntry_AndIsIdempotent()
        {
            var roster = CreateRoster();

            Assert.AreEqual(1, roster.Players.Count);
            Assert.IsTrue(roster.Players[0].IsHost);
            Assert.IsTrue(roster.Players[0].IsConnected);
            Assert.IsFalse(roster.Players[0].IsModerator);
            Assert.AreEqual(HostName, roster.Players[0].Name);

            roster.SetHost(0UL, "別の名前");
            Assert.AreEqual(1, roster.Players.Count, "ホストのエントリは 1 件のままのはず。");
            Assert.AreEqual("別の名前", roster.Players[0].Name);
        }

        [Test]
        public void SetHost_WithEmptyName_Throws()
        {
            var roster = new LobbyRoster();
            Assert.Throws<System.ArgumentException>(() => roster.SetHost(0UL, string.Empty));
        }

        [Test]
        public void Join_AddsConnectedEntry()
        {
            var roster = CreateRoster();

            var player = Join(roster, 1UL, "つむぎ");

            Assert.AreEqual(2, roster.Players.Count);
            Assert.AreEqual(1UL, player.ClientId);
            Assert.AreEqual("つむぎ", player.Name);
            Assert.IsTrue(player.IsConnected);
            Assert.IsFalse(player.IsHost);
            Assert.AreEqual(2, roster.ConnectedCount);
            Assert.AreEqual(2, roster.OccupiedSlotCount, "ホストが player 役なので 2 席使う。");
        }

        [Test]
        public void Disconnect_KeepsEntryAndMarksDisconnected()
        {
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ");

            Assert.IsTrue(roster.TryMarkDisconnected(1UL, nowSec: 100.0, out var disconnected));

            Assert.AreEqual(2, roster.Players.Count, "エントリは残るはず（docs/network.md §2.4）。");
            Assert.IsFalse(disconnected.IsConnected);
            Assert.AreEqual(100.0, disconnected.DisconnectedAtSec, 1e-9);
            Assert.AreEqual(1, roster.ConnectedCount, "接続中の人数は 1 人に減る。");
            Assert.AreEqual(
                2,
                roster.OccupiedSlotCount,
                "切断中でも保持期間の間は席を押さえる（統括判断 #7 Q3）。");
        }

        [Test]
        public void Disconnect_ForUnknownOrAlreadyDisconnectedClient_ReturnsFalse()
        {
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ");

            Assert.IsFalse(roster.TryMarkDisconnected(42UL, 1.0, out _), "未知のクライアントは false。");
            Assert.IsTrue(roster.TryMarkDisconnected(1UL, 1.0, out _));
            Assert.IsFalse(roster.TryMarkDisconnected(1UL, 2.0, out _), "二重の切断通知は false。");
        }

        [Test]
        public void Reconnect_WithMatchingToken_RestoresSameEntryWithNewClientId()
        {
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ", out var token);
            roster.TryMarkDisconnected(1UL, nowSec: 10.0, out _);

            var admission = roster.Evaluate("つむぎ", token, NoReservations, InLobby, LateJoinDenied);
            Assert.AreEqual(LobbyAdmissionKind.Reconnect, admission.Kind);
            Assert.AreEqual(1UL, admission.PreviousClientId);

            // 再接続では「発行したトークン」は採用されない（既存のハッシュを保つ）ことまで確かめるため、
            // わざと別のトークンのハッシュを渡す。
            Assert.IsTrue(
                roster.TryApply(7UL, "つむぎ", admission, SessionTokenHash.Of(CreateToken()), out var restored));

            Assert.AreEqual(2, roster.Players.Count, "新しいエントリを作らず復帰させるはず。");
            Assert.AreEqual(7UL, restored.ClientId);
            Assert.IsTrue(restored.IsConnected);
            Assert.AreEqual(LobbyPlayer.NotDisconnected, restored.DisconnectedAtSec, 1e-9);
            Assert.AreEqual(2, roster.OccupiedSlotCount);
            Assert.IsTrue(
                restored.TokenHash.Matches(token),
                "復帰後も同じトークンで再接続できるはず（トークンは入れ替えない）。");
        }

        // --- #163: 切断を検知する前（名簿上まだ接続中）の再接続 ---

        [Test]
        public void Takeover_ConnectedEntryWithMatchingToken_IsApprovedAsReconnectReplacingOldClient()
        {
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ", out var token);

            // 強制終了した直後で、ホストはまだ切断を検知していない（TryMarkDisconnected を呼ばない）。
            var admission = roster.Evaluate("つむぎ", token, NoReservations, InGame, LateJoinDenied);

            Assert.AreEqual(LobbyAdmissionKind.Reconnect, admission.Kind, "トークンが一致すれば接続中でも引き継げるはず。");
            Assert.IsTrue(admission.IsApproved);
            Assert.IsTrue(admission.ReplacesConnectedClient, "旧接続を切ってから引き継ぐ印が付くはず。");
            Assert.AreEqual(1UL, admission.PreviousClientId);
        }

        [Test]
        public void Takeover_ApplyMovesSeatToNewClientIdWithoutCreatingEntry()
        {
            var roster = CreateRoster();
            var original = Join(roster, 1UL, "つむぎ", out var token);
            var admission = roster.Evaluate("つむぎ", token, NoReservations, InGame, LateJoinDenied);

            Assert.IsTrue(
                roster.TryApply(9UL, "つむぎ", admission, SessionTokenHash.Of(CreateToken()), out var restored, out var transfer));

            Assert.AreEqual(2, roster.Players.Count, "新しいエントリを作らず席を引き継ぐはず。");
            Assert.AreEqual(9UL, restored.ClientId);
            Assert.IsTrue(restored.IsConnected);
            Assert.AreEqual(original.SeatId, restored.SeatId, "同じ席（得点行）を引き継ぐはず。");
            Assert.IsTrue(restored.TokenHash.Matches(token), "トークンは入れ替えない。");
            Assert.IsTrue(transfer.HasValue);
            Assert.AreEqual(1UL, transfer.PreviousClientId);
            Assert.AreEqual(9UL, transfer.ClientId);
            Assert.AreEqual(1, roster.CountConnectedWithName("つむぎ"), "同名の接続中エントリが 2 つ並ばないこと。");

            // 旧接続の切断通知が後から届いても、席は新しいクライアント ID に移っているので影響しない。
            Assert.IsFalse(roster.TryMarkDisconnected(1UL, 5.0, out _));
            Assert.IsTrue(roster.Players[1].IsConnected);
        }

        [Test]
        public void Takeover_ConnectedEntryWithWrongToken_IsStillRejectedAsDuplicateName()
        {
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ", out _);

            var admission = roster.Evaluate("つむぎ", CreateToken(), NoReservations, InLobby, LateJoinDenied);

            Assert.AreEqual(LobbyAdmissionKind.RejectDuplicateName, admission.Kind, "トークンが違えば接続中の席は奪えない（K-N1）。");
            Assert.IsFalse(admission.ReplacesConnectedClient);
        }

        [Test]
        public void Takeover_ConnectedEntryWithoutToken_IsStillRejectedAsDuplicateName()
        {
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ", out _);

            var admission = roster.Evaluate("つむぎ", SessionToken.None, NoReservations, InLobby, LateJoinDenied);

            Assert.AreEqual(LobbyAdmissionKind.RejectDuplicateName, admission.Kind);
        }

        [Test]
        public void Takeover_TokenOfAnotherConnectedPlayer_DoesNotStealThatSeat()
        {
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ", out var tsumugiToken);

            // 名前が違えば、トークンが一致していても引き継がない（名前とトークンの両方が必要）。
            var admission = roster.Evaluate("べつのひと", tsumugiToken, NoReservations, InLobby, LateJoinDenied);

            Assert.AreEqual(LobbyAdmissionKind.NewPlayer, admission.Kind);
            Assert.IsFalse(admission.ReplacesConnectedClient);
        }

        [Test]
        public void Takeover_HostEntryIsNeverReplaced()
        {
            var roster = CreateRoster();

            // ホストのエントリはトークンを持たないので、ホスト名で来ても引き継ぎにはならない。
            var admission = roster.Evaluate(HostName, CreateToken(), NoReservations, InLobby, LateJoinDenied);

            Assert.AreEqual(LobbyAdmissionKind.RejectDuplicateName, admission.Kind);
        }

        [Test]
        public void Takeover_SecondRequestForSameSeatWhilePending_IsRejected()
        {
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ", out var token);
            var first = roster.Evaluate("つむぎ", token, NoReservations, InGame, LateJoinDenied);
            Assert.IsTrue(first.ReplacesConnectedClient);

            var reservations = new[] { LobbyReservation.FromAdmission("つむぎ", first) };
            var second = roster.Evaluate("つむぎ", token, reservations, InGame, LateJoinDenied);

            Assert.AreEqual(LobbyAdmissionKind.RejectDuplicateName, second.Kind, "同じ席への二重の引き継ぎは拒否するはず。");
        }

        [Test]
        public void Reconnect_NormalReconnectDoesNotReplaceConnectedEntry()
        {
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ", out var token);
            roster.TryMarkDisconnected(1UL, nowSec: 10.0, out _);

            var admission = roster.Evaluate("つむぎ", token, NoReservations, InLobby, LateJoinDenied);

            Assert.AreEqual(LobbyAdmissionKind.Reconnect, admission.Kind);
            Assert.IsFalse(admission.ReplacesConnectedClient, "切断済みへの通常の再接続は引き継ぎの印を付けない。");
        }

        [Test]
        public void Reconnect_WithoutToken_IsTreatedAsNewPlayer()
        {
            // issue #69: 名前を騙っただけでは切断中のエントリ（席・得点）へ復帰できない。
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ");
            roster.TryMarkDisconnected(1UL, nowSec: 10.0, out _);

            var admission = roster.Evaluate("つむぎ", SessionToken.None, NoReservations, InLobby, LateJoinDenied);

            Assert.AreEqual(
                LobbyAdmissionKind.NewPlayer,
                admission.Kind,
                "トークンが無ければ同名でも新規参加として扱う。");
            Assert.AreEqual(0UL, admission.PreviousClientId);
        }

        [Test]
        public void Reconnect_WithWrongToken_IsTreatedAsNewPlayer()
        {
            // issue #69: 別のトークン（他の部屋でもらったものなど）では復帰できない。
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ");
            roster.TryMarkDisconnected(1UL, nowSec: 10.0, out _);

            var admission = roster.Evaluate("つむぎ", CreateToken(), NoReservations, InLobby, LateJoinDenied);

            Assert.AreEqual(LobbyAdmissionKind.NewPlayer, admission.Kind);
        }

        [Test]
        public void Reconnect_WithWrongTokenInFullRoom_IsRejectedInsteadOfStealingTheSeat()
        {
            // issue #69 の本丸: 満室の部屋で落ちた人の名前を騙っても席を奪えない。
            var roster = CreateRoster(maxPlayers: 2);
            Join(roster, 1UL, "つむぎ", out var token);
            roster.TryMarkDisconnected(1UL, nowSec: 1.0, out _);

            var imposter = roster.Evaluate("つむぎ", SessionToken.None, NoReservations, InLobby, LateJoinDenied);
            Assert.AreEqual(
                LobbyAdmissionKind.RejectSeatReserved,
                imposter.Kind,
                "席は本人の復帰用に確保されたままで、同名の別人は入れない。");

            // 本人はトークンを持っているので復帰できる。
            Assert.AreEqual(
                LobbyAdmissionKind.Reconnect,
                roster.Evaluate("つむぎ", token, NoReservations, InLobby, LateJoinDenied).Kind);
        }

        [Test]
        public void Reconnect_WithTokenOfAnotherName_IsTreatedAsNewPlayer()
        {
            // トークンは本物でも名前が違えば復帰させない（名前 + トークンの両方が条件）。
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ", out var tsumugiToken);
            roster.TryMarkDisconnected(1UL, nowSec: 1.0, out _);

            var admission = roster.Evaluate("べつのひと", tsumugiToken, NoReservations, InLobby, LateJoinDenied);

            Assert.AreEqual(LobbyAdmissionKind.NewPlayer, admission.Kind);
        }

        [Test]
        public void DisconnectedEntry_ReservesItsSeatUntilRetentionExpires()
        {
            // 統括判断 #7 Q3: 満室の部屋で誰かが落ちても、保持期間の間は席を押さえたままにして
            // 本人が必ず復帰できるようにする。
            var roster = CreateRoster(maxPlayers: 2);
            Join(roster, 1UL, "つむぎ", out var token);
            Assert.AreEqual(2, roster.OccupiedSlotCount, "ホスト + 1 名で満室。");

            roster.TryMarkDisconnected(1UL, nowSec: 0.0, out _);

            // 保持期間内: 席は空かないので新規は入れない。
            Assert.AreEqual(2, roster.OccupiedSlotCount);
            Assert.AreEqual(
                LobbyAdmissionKind.RejectSeatReserved,
                roster.Evaluate("よこどり", SessionToken.None, NoReservations, InLobby, LateJoinDenied).Kind,
                "保持期間内は切断者の席が空かないので新規参加は拒否されるはず（レビュー M-3）。");

            // 保持期間内: 本人（トークン一致）は復帰できる。
            Assert.AreEqual(
                LobbyAdmissionKind.Reconnect,
                roster.Evaluate("つむぎ", token, NoReservations, InLobby, LateJoinDenied).Kind,
                "保持期間内ならトークンを持つ本人は復帰できるはず。");

            // 保持期間切れ: エントリが消えて席が空き、新規が入れる。
            var removed = roster.RemoveExpired(
                nowSec: LobbyRoster.DefaultDisconnectedRetentionSec,
                retentionSec: LobbyRoster.DefaultDisconnectedRetentionSec);
            Assert.AreEqual(1, removed.Count);
            Assert.AreEqual(1, roster.OccupiedSlotCount, "保持期間を過ぎれば席が空く。");
            Assert.AreEqual(
                LobbyAdmissionKind.NewPlayer,
                roster.Evaluate("よこどり", SessionToken.None, NoReservations, InLobby, LateJoinDenied).Kind,
                "保持期間を過ぎたら新規参加できるはず。");
        }

        [Test]
        public void Reconnect_IsAllowedEvenWhenRoomIsFullOrGameInProgress()
        {
            var roster = CreateRoster(maxPlayers: 2);
            Join(roster, 1UL, "つむぎ", out var token);
            Assert.AreEqual(2, roster.OccupiedSlotCount, "ホスト + 1 名で満室。");

            roster.TryMarkDisconnected(1UL, nowSec: 5.0, out _);

            var admission = roster.Evaluate("つむぎ", token, NoReservations, InGame, LateJoinDenied);
            Assert.AreEqual(
                LobbyAdmissionKind.Reconnect,
                admission.Kind,
                "再接続は本人の席へ戻る操作なので、満室・進行中でも許可する。");
        }

        [Test]
        public void Reconnect_AfterEntryExpired_IsTreatedAsNewPlayer()
        {
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ", out var token);
            roster.TryMarkDisconnected(1UL, nowSec: 0.0, out _);

            var removed = roster.RemoveExpired(
                nowSec: LobbyRoster.DefaultDisconnectedRetentionSec + 0.1,
                retentionSec: LobbyRoster.DefaultDisconnectedRetentionSec);
            Assert.AreEqual(1, removed.Count);
            Assert.AreEqual(1, roster.Players.Count, "ホストだけが残るはず。");

            var admission = roster.Evaluate("つむぎ", token, NoReservations, InLobby, LateJoinDenied);
            Assert.AreEqual(
                LobbyAdmissionKind.NewPlayer,
                admission.Kind,
                "エントリが消えていればトークンを持っていても新規参加になる。");
        }

        [Test]
        public void RemoveExpired_KeepsEntriesWithinRetentionWindow_AndNeverRemovesHost()
        {
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ");
            roster.TryMarkDisconnected(1UL, nowSec: 0.0, out _);

            var stillKept = roster.RemoveExpired(
                nowSec: LobbyRoster.DefaultDisconnectedRetentionSec - 0.1,
                retentionSec: LobbyRoster.DefaultDisconnectedRetentionSec);
            Assert.AreEqual(0, stillKept.Count, "保持期間内は消さない。");
            Assert.AreEqual(2, roster.Players.Count);
            Assert.AreEqual(2, roster.OccupiedSlotCount, "保持期間内は席も押さえたまま。");

            // ホストが切断扱いになっても名簿からは消さない。
            roster.TryMarkDisconnected(0UL, nowSec: 0.0, out _);
            roster.RemoveExpired(nowSec: 10_000.0, retentionSec: LobbyRoster.DefaultDisconnectedRetentionSec);
            Assert.AreEqual(1, roster.Players.Count);
            Assert.IsTrue(roster.Players[0].IsHost);
        }

        [Test]
        public void Evaluate_WhenRoomIsFull_RejectsWithRoomFullReason()
        {
            var roster = CreateRoster(maxPlayers: 2);
            Join(roster, 1UL, "つむぎ");

            var admission = roster.Evaluate("あとから", SessionToken.None, NoReservations, InLobby, LateJoinDenied);

            Assert.AreEqual(LobbyAdmissionKind.RejectRoomFull, admission.Kind);
            Assert.IsFalse(admission.IsApproved);
            Assert.AreEqual(ConnectionRejectionReason.RoomFull, admission.RejectionReason);
            Assert.AreEqual(ConnectionRejectionMessages.RoomFull, admission.Message);
        }

        [Test]
        public void Evaluate_CountsReservedSlots_SoConcurrentApprovalsCannotOverfill()
        {
            var roster = CreateRoster(maxPlayers: 3);
            Join(roster, 1UL, "つむぎ");
            Assert.AreEqual(LobbyAdmissionKind.NewPlayer, roster.Evaluate("3人目", SessionToken.None, NoReservations, InLobby, LateJoinDenied).Kind);

            // 承認済みでまだ接続完了していないクライアントが 1 人いる場合は満室扱い。
            var reservations = new[] { LobbyReservation.NewPlayer("さきに承認された人") };
            Assert.AreEqual(
                LobbyAdmissionKind.RejectRoomFull,
                roster.Evaluate("3人目", SessionToken.None, reservations, InLobby, LateJoinDenied).Kind);
        }

        [Test]
        public void Evaluate_WithModeratorHost_DoesNotCountHostTowardCapacity()
        {
            var roster = CreateRoster(maxPlayers: 2, hostRole: HostRole.Moderator);
            Assert.AreEqual(0, roster.OccupiedSlotCount, "司会専任のホストは定員に数えない（仮決め K18）。");
            Assert.IsTrue(roster.Players[0].IsModerator);

            Join(roster, 1UL, "A");
            Join(roster, 2UL, "B");
            Assert.AreEqual(2, roster.OccupiedSlotCount);

            Assert.AreEqual(LobbyAdmissionKind.RejectRoomFull, roster.Evaluate("C", SessionToken.None, NoReservations, InLobby, LateJoinDenied).Kind);
        }

        [Test]
        public void SetHostRole_UpdatesExistingHostEntryAndCapacity()
        {
            var roster = CreateRoster(maxPlayers: 2);
            Join(roster, 1UL, "つむぎ");
            Assert.AreEqual(LobbyAdmissionKind.RejectRoomFull, roster.Evaluate("もう1人", SessionToken.None, NoReservations, InLobby, LateJoinDenied).Kind);

            roster.SetHostRole(HostRole.Moderator);

            Assert.IsTrue(roster.Players[0].IsModerator);
            Assert.AreEqual(1, roster.OccupiedSlotCount, "司会専任へ切り替えるとホストの席が空く。");
            Assert.AreEqual(LobbyAdmissionKind.NewPlayer, roster.Evaluate("もう1人", SessionToken.None, NoReservations, InLobby, LateJoinDenied).Kind);
        }

        [Test]
        public void SetMaxPlayers_AppliesClampedValue()
        {
            var roster = CreateRoster();
            roster.SetMaxPlayers(99);
            Assert.AreEqual(LobbyRoster.MaxMaxPlayers, roster.MaxPlayers);
            roster.SetMaxPlayers(0);
            Assert.AreEqual(LobbyRoster.MinMaxPlayers, roster.MaxPlayers);
        }

        [Test]
        public void Evaluate_WithNameAlreadyReserved_RejectsAsDuplicate()
        {
            // レビュー H-1: 承認済みでまだ接続完了していない同名クライアントが居る間は、
            // 2 件目を承認しない（名簿にはまだ現れないので、予約で塞ぐ）。
            var roster = CreateRoster();

            var reservations = new[] { LobbyReservation.NewPlayer("つむぎ") };

            var admission = roster.Evaluate("つむぎ", SessionToken.None, reservations, InLobby, LateJoinDenied);

            Assert.AreEqual(LobbyAdmissionKind.RejectDuplicateName, admission.Kind);
            Assert.AreEqual(ConnectionRejectionReason.DuplicatePlayerName, admission.RejectionReason);

            // 別名なら通る（予約が無関係な参加まで塞がないこと）。
            Assert.AreEqual(
                LobbyAdmissionKind.NewPlayer,
                roster.Evaluate("あかね", SessionToken.None, reservations, InLobby, LateJoinDenied).Kind);
        }

        [Test]
        public void Evaluate_WithSeatAlreadyReservedByReconnect_RejectsSecondReconnect()
        {
            // レビュー H-1: 同じ席（PreviousClientId）を狙う再接続が二重に承認されないこと。
            // 名簿の名前と承認ペイロードの名前がずれた場合の保険として、席そのものでも弾く。
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ", out var token);
            roster.TryMarkDisconnected(1UL, nowSec: 5.0, out _);

            var reservations = new[] { LobbyReservation.Reconnect("別名だが同じ席", previousClientId: 1UL) };

            var admission = roster.Evaluate("つむぎ", token, reservations, InLobby, LateJoinDenied);

            Assert.AreEqual(LobbyAdmissionKind.RejectDuplicateName, admission.Kind);
            Assert.AreEqual(ConnectionRejectionReason.DuplicatePlayerName, admission.RejectionReason);
        }

        [Test]
        public void Evaluate_ReconnectReservation_DoesNotConsumeAnExtraSeat()
        {
            // レビュー M-1 / M-2: 再接続の予約は既存の席へ戻るだけなので定員に二重計上しない。
            var roster = CreateRoster(maxPlayers: 3);
            Join(roster, 1UL, "つむぎ");
            roster.TryMarkDisconnected(1UL, nowSec: 1.0, out _);

            // 席は「ホスト + つむぎ（切断中・予約席）」の 2 つが埋まっている状態。
            var reconnectReservation = new[] { LobbyReservation.Reconnect("つむぎ", previousClientId: 1UL) };

            Assert.AreEqual(
                LobbyAdmissionKind.NewPlayer,
                roster.Evaluate("あかね", SessionToken.None, reconnectReservation, InLobby, LateJoinDenied).Kind,
                "再接続の予約は新しい席を要求しないので、3 席目は空いているはず。");

            var newPlayerReservation = new[] { LobbyReservation.NewPlayer("さきに承認された人") };
            Assert.AreEqual(
                LobbyAdmissionKind.RejectSeatReserved,
                roster.Evaluate("あかね", SessionToken.None, newPlayerReservation, InLobby, LateJoinDenied).Kind,
                "新規参加の予約は席を 1 つ使うので、3 席目が埋まって満席になるはず。");
        }

        [Test]
        public void Evaluate_WhenOnlyReservedSeatsRemain_RejectsWithSeatReservedReason()
        {
            // レビュー M-3: 「単なる満室」と「切断者の席を確保中」を区別して伝える。
            var roster = CreateRoster(maxPlayers: 2);
            Join(roster, 1UL, "つむぎ");

            var plainFull = roster.Evaluate("あとから", SessionToken.None, NoReservations, InLobby, LateJoinDenied);
            Assert.AreEqual(LobbyAdmissionKind.RejectRoomFull, plainFull.Kind, "全員接続中なら単なる満室。");

            roster.TryMarkDisconnected(1UL, nowSec: 0.0, out _);

            var reserved = roster.Evaluate("あとから", SessionToken.None, NoReservations, InLobby, LateJoinDenied);
            Assert.AreEqual(LobbyAdmissionKind.RejectSeatReserved, reserved.Kind);
            Assert.AreEqual(ConnectionRejectionReason.SeatReserved, reserved.RejectionReason);
            Assert.AreEqual(ConnectionRejectionMessages.SeatReserved, reserved.Message);
        }

        [Test]
        public void Evaluate_DuringGame_RejectsUnlessLateJoinAllowed()
        {
            var roster = CreateRoster();

            var denied = roster.Evaluate("あとから", SessionToken.None, NoReservations, InGame, LateJoinDenied);
            Assert.AreEqual(LobbyAdmissionKind.RejectGameInProgress, denied.Kind);
            Assert.AreEqual(ConnectionRejectionReason.GameInProgress, denied.RejectionReason);
            Assert.AreEqual(ConnectionRejectionMessages.GameInProgress, denied.Message);

            var allowed = roster.Evaluate("あとから", SessionToken.None, NoReservations, InGame, LateJoinAllowed);
            Assert.AreEqual(LobbyAdmissionKind.NewPlayer, allowed.Kind);
        }

        [Test]
        public void Evaluate_WithNameOfConnectedPlayer_RejectsAsDuplicate()
        {
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ");

            var duplicate = roster.Evaluate("つむぎ", SessionToken.None, NoReservations, InLobby, LateJoinDenied);
            Assert.AreEqual(LobbyAdmissionKind.RejectDuplicateName, duplicate.Kind);
            Assert.AreEqual(ConnectionRejectionReason.DuplicatePlayerName, duplicate.RejectionReason);

            var sameAsHost = roster.Evaluate(HostName, SessionToken.None, NoReservations, InLobby, LateJoinDenied);
            Assert.AreEqual(LobbyAdmissionKind.RejectDuplicateName, sameAsHost.Kind, "ホストと同名も拒否する。");
        }

        [Test]
        public void Evaluate_WithEmptyName_RejectsAsInvalidPlayerName()
        {
            // レビュー M-9: 空名は「名前重複」ではなく「名前が不正」として返す。
            var roster = CreateRoster();

            foreach (var name in new[] { string.Empty, null })
            {
                var admission = roster.Evaluate(name, SessionToken.None, NoReservations, InLobby, LateJoinDenied);
                Assert.IsFalse(admission.IsApproved);
                Assert.AreEqual(LobbyAdmissionKind.RejectInvalidPlayerName, admission.Kind);
                Assert.AreEqual(ConnectionRejectionReason.InvalidPlayerName, admission.RejectionReason);
            }
        }

        [Test]
        public void TryApply_WithRejectedAdmission_DoesNotChangeRoster()
        {
            var roster = CreateRoster(maxPlayers: 2);
            Join(roster, 1UL, "つむぎ");

            var rejected = roster.Evaluate("あとから", SessionToken.None, NoReservations, InLobby, LateJoinDenied);
            Assert.IsFalse(
                roster.TryApply(2UL, "あとから", rejected, SessionTokenHash.Of(CreateToken()), out _));
            Assert.AreEqual(2, roster.Players.Count);
        }

        [Test]
        public void TryApply_ReusingClientIdOfDisconnectedEntry_KeepsItForReconnect()
        {
            // レビュー M-4: NGO がクライアント ID を再利用しても、切断中のエントリは
            // 復帰用に保持しているので別人の新規参加で潰さない。
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ", out var token);
            roster.TryMarkDisconnected(1UL, 1.0, out _);

            var admission = roster.Evaluate("べつのひと", SessionToken.None, NoReservations, InLobby, LateJoinDenied);
            Assert.AreEqual(LobbyAdmissionKind.NewPlayer, admission.Kind);
            Assert.IsTrue(
                roster.TryApply(1UL, "べつのひと", admission, SessionTokenHash.Of(CreateToken()), out var added));

            Assert.AreEqual(3, roster.Players.Count, "切断中エントリは残したまま新しいエントリを足す。");
            Assert.AreEqual("べつのひと", added.Name);
            Assert.IsTrue(roster.TryGet(1UL, out var kept));
            Assert.IsTrue(
                kept.Name == "つむぎ" || added.ClientId == 1UL,
                "切断中の『つむぎ』のエントリが消えていないこと。");

            // 『つむぎ』は依然としてトークンで復帰できる。
            Assert.AreEqual(
                LobbyAdmissionKind.Reconnect,
                roster.Evaluate("つむぎ", token, NoReservations, InLobby, LateJoinDenied).Kind);
        }

        [Test]
        public void TryApply_ReusingClientIdOfConnectedEntry_ReplacesStaleEntry()
        {
            // 切断通知を取りこぼして「接続中」のまま残っていたエントリは、同じ ID の
            // 新しい接続で置き換える（名簿に幽霊を残さない）。
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ");

            var admission = LobbyAdmission.NewPlayer();
            Assert.IsTrue(
                roster.TryApply(1UL, "べつのひと", admission, SessionTokenHash.Of(CreateToken()), out var replaced));

            Assert.AreEqual(2, roster.Players.Count, "同じ ID の接続中エントリは増やさず置き換える。");
            Assert.AreEqual("べつのひと", replaced.Name);
        }

        [Test]
        public void TryGetAndRemove_WorkByClientId()
        {
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ");

            Assert.IsTrue(roster.TryGet(1UL, out var found));
            Assert.AreEqual("つむぎ", found.Name);
            Assert.IsFalse(roster.TryGet(99UL, out _));

            Assert.IsTrue(roster.Remove(1UL));
            Assert.IsFalse(roster.Remove(1UL));
            Assert.AreEqual(1, roster.Players.Count);

            roster.Clear();
            Assert.AreEqual(0, roster.Players.Count);
        }

        [Test]
        public void CountConnectedWithName_CountsOnlyConnectedEntries()
        {
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ");

            Assert.AreEqual(1, roster.CountConnectedWithName("つむぎ"));
            Assert.AreEqual(1, roster.CountConnectedWithName(HostName));
            Assert.AreEqual(0, roster.CountConnectedWithName("いないひと"));

            roster.TryMarkDisconnected(1UL, nowSec: 10.0, out _);
            Assert.AreEqual(0, roster.CountConnectedWithName("つむぎ"), "切断中は数えないはず。");
        }

        [Test]
        public void CountConnectedWithName_WithNullOrEmpty_ReturnsZero()
        {
            var roster = CreateRoster();

            Assert.AreEqual(0, roster.CountConnectedWithName(null));
            Assert.AreEqual(0, roster.CountConnectedWithName(string.Empty));
        }

        /// <summary>
        /// #85 / #69: 名前の先取りが起きたときだけ同名の接続中エントリが 2 件並ぶ。
        /// （A が参加 → A が切断 → 別人 B が同じ名前で新規参加 → A がトークン付きで復帰）
        /// </summary>
        [Test]
        public void CountConnectedWithName_AfterNameStealingReconnect_ReturnsTwo()
        {
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ", out var token);
            roster.TryMarkDisconnected(1UL, nowSec: 10.0, out _);

            // 別人 B が同じ名前で新規参加する（空席があるので通る）。
            Join(roster, 2UL, "つむぎ");
            Assert.AreEqual(1, roster.CountConnectedWithName("つむぎ"));

            // 本人 A がトークンで復帰する（再接続の判定は名前重複より先）。
            var admission = roster.Evaluate("つむぎ", token, NoReservations, InLobby, LateJoinDenied);
            Assert.AreEqual(LobbyAdmissionKind.Reconnect, admission.Kind);
            Assert.IsTrue(roster.TryApply(3UL, "つむぎ", admission, SessionTokenHash.Of(CreateToken()), out _));

            Assert.AreEqual(
                2,
                roster.CountConnectedWithName("つむぎ"),
                "再接続経路では同名の接続中エントリが 2 件並ぶ（docs/network.md §2.3 の例外）。");
        }

        // --- 席の安定 ID と付け替えの報告（#84） ---

        [Test]
        public void SeatId_IsAssignedPerSeatAndNeverReused()
        {
            var roster = CreateRoster();
            var host = roster.SetHost(0UL, HostName);

            var first = Join(roster, 1UL, "つむぎ");
            var second = Join(roster, 2UL, "ひかり");

            Assert.AreEqual(LobbyRoster.FirstSeatId, host.SeatId, "ホストの席は 1 番。");
            Assert.AreNotEqual(LobbyPlayer.NoSeatId, first.SeatId);
            Assert.AreNotEqual(first.SeatId, second.SeatId, "席ごとに別の ID を振る。");
            Assert.IsTrue(roster.TryGetSeatId(1UL, out var lookedUp));
            Assert.AreEqual(first.SeatId, lookedUp);
        }

        [Test]
        public void SetHost_CalledAgain_KeepsTheSameSeatId()
        {
            var roster = CreateRoster();
            var before = roster.SetHost(0UL, HostName);

            // ルーム設定の反映（ConfigureRoom）で毎回呼ばれる経路。
            roster.SetHostRole(HostRole.Moderator);
            var after = roster.SetHost(0UL, HostName);

            Assert.AreEqual(before.SeatId, after.SeatId, "設定変更で席の ID が変わらないこと。");
            Assert.AreEqual(1, roster.Players.Count);
        }

        [Test]
        public void TryApply_Reconnect_ReportsSeatTransferWithBothClientIds()
        {
            var roster = CreateRoster();
            var joined = Join(roster, 1UL, "つむぎ", out var token);
            roster.TryMarkDisconnected(1UL, nowSec: 10.0, out _);

            var admission = roster.Evaluate("つむぎ", token, NoReservations, InLobby, LateJoinDenied);
            Assert.IsTrue(
                roster.TryApply(
                    7UL, "つむぎ", admission, SessionTokenHash.Of(CreateToken()), out var restored, out var transfer));

            Assert.IsTrue(transfer.HasValue, "再接続では付け替えを報告するはず。");
            Assert.AreEqual(joined.SeatId, transfer.SeatId, "席の ID は再接続でも変わらない。");
            Assert.AreEqual(joined.SeatId, restored.SeatId);
            Assert.AreEqual(1UL, transfer.PreviousClientId);
            Assert.AreEqual(7UL, transfer.ClientId);
        }

        [Test]
        public void TryApply_NewPlayer_ReportsNoSeatTransfer()
        {
            var roster = CreateRoster();
            var admission = roster.Evaluate("つむぎ", SessionToken.None, NoReservations, InLobby, LateJoinDenied);

            Assert.IsTrue(
                roster.TryApply(
                    1UL, "つむぎ", admission, SessionTokenHash.Of(CreateToken()), out _, out var transfer));

            Assert.IsFalse(transfer.HasValue, "新規参加は別人なので何も引き継がない。");
        }

        [Test]
        public void TryApply_AfterEntryExpired_ReportsNoSeatTransfer()
        {
            var roster = CreateRoster();
            var joined = Join(roster, 1UL, "つむぎ", out var token);
            roster.TryMarkDisconnected(1UL, nowSec: 0.0, out _);

            // 承認の判定だけ先に済ませ、そのあとで保持期間切れの掃除が走った状況を作る。
            var admission = roster.Evaluate("つむぎ", token, NoReservations, InLobby, LateJoinDenied);
            Assert.AreEqual(LobbyAdmissionKind.Reconnect, admission.Kind);
            roster.RemoveExpired(
                nowSec: LobbyRoster.DefaultDisconnectedRetentionSec + 0.1,
                retentionSec: LobbyRoster.DefaultDisconnectedRetentionSec);

            Assert.IsTrue(
                roster.TryApply(
                    7UL, "つむぎ", admission, SessionTokenHash.Of(CreateToken()), out var applied, out var transfer));

            Assert.IsFalse(transfer.HasValue, "席が消えていたら得点も引き継がない。");
            Assert.AreNotEqual(joined.SeatId, applied.SeatId, "作り直した席には新しい ID を振る。");
        }

        [Test]
        public void Clear_RestartsSeatIdsFromTheFirstValue()
        {
            var roster = CreateRoster();
            Join(roster, 1UL, "つむぎ");

            roster.Clear();
            var host = roster.SetHost(0UL, HostName);

            Assert.AreEqual(LobbyRoster.FirstSeatId, host.SeatId, "ホストを止めたら席の ID も振り直す。");
        }
    }
}
