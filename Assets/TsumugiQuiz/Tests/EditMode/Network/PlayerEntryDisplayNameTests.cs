using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using Unity.Collections;

namespace TsumugiQuiz.Tests.EditMode.Network
{
    /// <summary>
    /// 名簿の名前の表示用の整形（<see cref="PlayerEntry.GetDisplayName"/>）と、それを使う
    /// <see cref="PlayerEntryDisplayNames"/> を検証する（issue #206）。
    /// </summary>
    public class PlayerEntryDisplayNameTests
    {
        [Test]
        public void GetDisplayName_CleansTheName_WithoutChangingTheRosterData()
        {
            var entry = Player(1, "つむ\nぎ");

            Assert.That(entry.GetDisplayName(), Is.EqualTo("つむ ぎ"));
            Assert.That(entry.GetName(), Is.EqualTo("つむ\nぎ"), "名簿のデータは変えない。");
        }

        [Test]
        public void GetDisplayName_EmptyAfterCleaning_FallsBackToThePlayerNumber()
        {
            Assert.That(Player(7, "\u0007\u200B").GetDisplayName(), Is.EqualTo(PlayerDisplayNameSanitizer.FallbackName(7)));
        }

        [Test]
        public void PlayerEntryDisplayNames_UsesCleanedNames()
        {
            var entries = new List<PlayerEntry> { Player(1, "つむぎ\n\n\n"), Player(2, "めたん"), Player(3, "\u0007") };

            var names = PlayerEntryDisplayNames.Resolve(entries);

            Assert.That(names.GetLabel(0), Is.EqualTo("つむぎ"));
            Assert.That(names.GetLabel(1), Is.EqualTo("めたん"));
            Assert.That(names.GetLabel(2), Is.EqualTo("プレイヤー3"));
        }

        [TestCase("つむぎ\u0007")]
        [TestCase("つむ\u200Bぎ")]
        public void PlayerEntryDisplayNames_NamesThatBecomeTheSame_AreNumbered(string lookalike)
        {
            // 整えると同じになる名前（制御文字・幅のない空白入り）は、画面上で区別できるよう同名として連番を付ける。
            var entries = new List<PlayerEntry> { Player(1, "つむぎ"), Player(2, lookalike) };

            var names = PlayerEntryDisplayNames.Resolve(entries);

            Assert.That(names.HasDuplicates, Is.True);
            Assert.That(names.GetLabel(0), Is.Not.EqualTo(names.GetLabel(1)));
            // StringAssert はカルチャ依存の比較で見えない文字を無視するため、序数で比べる（PR #211 レビュー L-7）。
            var prefix = "つむぎ" + PlayerDisplayNames.OrdinalSeparator;
            Assert.That(names.GetLabel(0).StartsWith(prefix, System.StringComparison.Ordinal), Is.True, names.GetLabel(0));
            Assert.That(names.GetLabel(1).StartsWith(prefix, System.StringComparison.Ordinal), Is.True, names.GetLabel(1));
        }

        private static PlayerEntry Player(ulong clientId, string name)
        {
            var entry = new PlayerEntry { ClientId = clientId, IsConnected = true };
            entry.Name.CopyFromTruncated(name);
            return entry;
        }
    }
}
