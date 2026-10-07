using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Network;
using Unity.Collections;
using TsumugiQuiz.UI.Views;

namespace TsumugiQuiz.Tests.EditMode.UI
{
    /// <summary>
    /// <see cref="ResultView.BuildRankingEntries"/>（H4、#20）のテスト。
    /// 名簿（<see cref="LobbyState"/>）を基準に順位計算へ渡す一覧を組み立てるロジックを、
    /// Unity API に依存しない範囲（<see cref="PlayerEntry"/> / <see cref="ScoreEntry"/> は
    /// プレーンな構造体）で直接検証する。
    /// </summary>
    public class ResultViewRankingTests
    {
        private const ulong HostClientId = 0;
        private const ulong PlayerAClientId = 1;
        private const ulong PlayerBClientId = 2;

        private static PlayerEntry Player(ulong clientId, string name, bool isHost = false, bool isModerator = false)
        {
            var entry = new PlayerEntry
            {
                ClientId = clientId,
                IsHost = isHost,
                IsModerator = isModerator,
                IsConnected = true,
            };
            entry.Name.CopyFromTruncated(name);
            return entry;
        }

        [Test]
        public void BuildRankingEntries_ExcludesModeratorHost()
        {
            var roster = new List<PlayerEntry>
            {
                Player(HostClientId, "司会", isHost: true, isModerator: true),
                Player(PlayerAClientId, "プレイヤーA"),
            };

            var entries = ResultView.BuildRankingEntries(roster, _ => 0, System.Array.Empty<ScoreEntry>());

            Assert.AreEqual(1, entries.Count, "司会専任のホストは除外されるはず。");
            Assert.AreEqual(PlayerAClientId, entries[0].ClientId);
        }

        [Test]
        public void BuildRankingEntries_IncludesHostAsPlayer_WhenNotModerator()
        {
            // 通常モード（host.role == player）のホストはプレイヤーなので除外しない。
            var roster = new List<PlayerEntry>
            {
                Player(HostClientId, "ホスト", isHost: true, isModerator: false),
                Player(PlayerAClientId, "プレイヤーA"),
            };

            var entries = ResultView.BuildRankingEntries(roster, _ => 0, System.Array.Empty<ScoreEntry>());

            Assert.AreEqual(2, entries.Count);
            CollectionAssert.AreEquivalent(
                new[] { HostClientId, PlayerAClientId }, entries.ConvertAll(e => e.ClientId));
        }

        [Test]
        public void BuildRankingEntries_PlayerWithNoScore_StillAppearsWithZero()
        {
            // 一度も得点が動いていない（＝得点表に載っていない）参加者も、名簿にさえ居れば 0 点で並ぶ（H4）。
            var roster = new List<PlayerEntry>
            {
                Player(PlayerAClientId, "プレイヤーA"),
                Player(PlayerBClientId, "プレイヤーB"),
            };

            int GetScore(ulong clientId) => clientId == PlayerAClientId ? 10 : 0;

            var entries = ResultView.BuildRankingEntries(roster, GetScore, System.Array.Empty<ScoreEntry>());

            Assert.AreEqual(2, entries.Count);
            var playerB = entries.Find(e => e.ClientId == PlayerBClientId);
            Assert.AreEqual(0, playerB.Score, "得点表に載っていなくても 0 点で並ぶはず。");
        }

        [Test]
        public void BuildRankingEntries_ScoreOnlyClientId_IsAppendedAfterRoster()
        {
            // 名簿には無いが得点表にだけ残っている ID（退出済み等）は末尾に補う。
            var roster = new List<PlayerEntry> { Player(PlayerAClientId, "プレイヤーA") };
            var scoreSnapshot = new List<ScoreEntry> { new ScoreEntry(PlayerAClientId, 10), new ScoreEntry(99, 5) };

            var entries = ResultView.BuildRankingEntries(roster, _ => 10, scoreSnapshot);

            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual(PlayerAClientId, entries[0].ClientId, "名簿に載っている分が先に並ぶはず。");
            Assert.AreEqual(99UL, entries[1].ClientId, "名簿に無い ID は末尾に補われるはず。");
            Assert.AreEqual(5, entries[1].Score);
        }

        [Test]
        public void BuildRankingEntries_NullRosterAndScoreSnapshot_ReturnsEmpty()
        {
            var entries = ResultView.BuildRankingEntries(null, null, null);

            Assert.IsEmpty(entries);
        }
    }
}
