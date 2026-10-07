using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 再接続経路でだけ起こる「同名の同時接続」を、実際に接続させて再現し、
    /// 表示側の対処（連番の付与）とホストへの警告を検証する PlayMode テスト
    /// （issue #85 の M3、docs/network.md §2.3 の補足）。
    /// </summary>
    /// <remarks>
    /// サーバー側の名簿ロジック（本人の席への復帰を名前の一意性より優先する）は #69 のまま変えない。
    /// ここで確かめるのは「起きたときに画面で区別できること」と「ホストが気づけること」。
    /// </remarks>
    public class LobbyDuplicateNameTests : LobbyStateTestFixture
    {
        private const string DuplicatedName = "つむぎ";

        [UnityTest]
        public IEnumerator NameStealingReconnect_NumbersTheDuplicates_AndWarnsTheHost()
        {
            // 空席がある部屋。(1) A が参加 → (2) A が切断 → (3) 別人 B が同じ名前で新規参加
            // → (4) A がトークンで復帰、の順で同名の接続中エントリが 2 つ並ぶ。
            StartHostAndSpawnLobby(maxPlayers: 6);

            yield return ConnectClient(0, DuplicatedName);

            var token = SessionToken.None;
            yield return WaitForSessionToken(0, received => token = received);
            Assert.IsTrue(token.HasValue, "ホストは承認したクライアントへトークンを発行するはず。");

            yield return WaitUntil(() => CountEntries(HostLobby) == 2, () => "ホスト + A で 2 件になるはず。");
            Assert.IsTrue(TryFindEntry(HostLobby, DuplicatedName, out var beforeDisconnect));
            var victimClientId = beforeDisconnect.ClientId;

            yield return DisconnectClient(0);
            yield return WaitUntil(
                () => TryFindEntryByClientId(HostLobby, victimClientId, out var e) && !e.IsConnected,
                () => "切断が名簿へ反映されるはず。");

            // (3) 別人 B が名前を先取りする（空席があるので新規参加として通る）。
            yield return ConnectClient(1, DuplicatedName);
            yield return WaitUntil(
                () => CountEntries(HostLobby) == 3,
                () => $"同名でも新規の席として増えるはず（実際: {CountEntries(HostLobby)} 件）。");

            // この時点では同時接続していない（A は切断中）ので、連番は付けない。
            var beforeReconnect = PlayerEntryDisplayNames.Resolve(HostLobby.GetPlayersSnapshot());
            Assert.IsFalse(
                beforeReconnect.HasDuplicates,
                "切断中のエントリは連番の対象外（グレー表示と『切断中』で区別できる）。");

            // (4) 本人 A がトークンで復帰する。ここで初めて同名が 2 人接続する。
            LogAssert.Expect(
                LogType.Warning, "[LobbyState] " + DuplicateNameNotice.BuildLogLine(DuplicatedName, 2));

            yield return ConnectClient(0, DuplicatedName, token);

            yield return WaitUntil(
                () => HostLobby.ServerRoster != null
                      && HostLobby.ServerRoster.CountConnectedWithName(DuplicatedName) == 2,
                () => "同名が 2 人接続している状態になるはず。");

            Assert.AreEqual(3, CountEntries(HostLobby), "復帰では席を増やさないはず（ホスト + A + B）。");

            // --- 表示側: 同名の 2 人に一意な表示名が付く ---
            var snapshot = HostLobby.GetPlayersSnapshot();
            var resolved = PlayerEntryDisplayNames.Resolve(snapshot);

            Assert.IsTrue(resolved.HasDuplicates, "同名の同時接続を検出するはず。");
            CollectionAssert.AreEqual(new[] { DuplicatedName }, resolved.DuplicatedNames);

            var labels = new List<string>(resolved.Labels);
            CollectionAssert.AllItemsAreUnique(labels, $"表示名が一意にならない: {string.Join(" / ", labels)}");
            CollectionAssert.Contains(labels, DuplicatedName + " #1");
            CollectionAssert.Contains(labels, DuplicatedName + " #2");
            CollectionAssert.Contains(labels, HostPlayerName, "重複していないホストの名前はそのまま出すはず。");

            // 表示名は名簿の並び順（= 参加順）と 1 対 1 で対応し、名簿の name 自体は書き換えない。
            Assert.AreEqual(snapshot.Count, resolved.Labels.Count);
            for (var i = 0; i < snapshot.Count; i++)
            {
                StringAssert.StartsWith(snapshot[i].GetName(), resolved.GetLabel(i));
            }

            // --- ホストへの注意文（LobbyView が出すもの）に対処の案内が入る ---
            var hostNotice = DuplicateNameNotice.Build(resolved, includeHostAdvice: true);
            StringAssert.Contains(DuplicatedName, hostNotice);
            StringAssert.Contains(DuplicateNameNotice.HostAdvice, hostNotice);

            var clientNotice = DuplicateNameNotice.Build(resolved, includeHostAdvice: false);
            StringAssert.DoesNotContain(DuplicateNameNotice.HostAdvice, clientNotice);
        }

        [UnityTest]
        public IEnumerator DistinctNames_GetNoOrdinalAndNoNotice()
        {
            StartHostAndSpawnLobby(maxPlayers: 6);

            yield return ConnectClient(0, "つむぎ");
            yield return ConnectClient(1, "ずんだ");

            yield return WaitUntil(
                () => CountEntries(HostLobby) == 3,
                () => $"ホスト + 2 名で 3 件になるはず（実際: {CountEntries(HostLobby)} 件）。");

            var resolved = PlayerEntryDisplayNames.Resolve(HostLobby.GetPlayersSnapshot());

            Assert.IsFalse(resolved.HasDuplicates, "同名が居なければ注意は出さない。");
            CollectionAssert.Contains(new List<string>(resolved.Labels), "つむぎ");
            CollectionAssert.Contains(new List<string>(resolved.Labels), "ずんだ");
            Assert.AreEqual(
                string.Empty,
                DuplicateNameNotice.Build(resolved, includeHostAdvice: true));

            foreach (var label in resolved.Labels)
            {
                StringAssert.DoesNotContain(
                    PlayerDisplayNames.OrdinalSeparator, label, "重複が無ければ連番は付けない。");
            }
        }
    }
}
