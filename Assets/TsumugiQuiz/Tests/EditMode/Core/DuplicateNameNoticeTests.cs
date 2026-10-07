using NUnit.Framework;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 同名同時接続の注意文（<see cref="DuplicateNameNotice"/>）の検証（issue #85）。
    /// 表示するかどうかは UI 層（<c>LobbyView</c>）が決め、ここでは文言の組み立てだけを見る。
    /// </summary>
    public class DuplicateNameNoticeTests
    {
        [Test]
        public void Build_WithNoDuplicates_ReturnsEmpty()
        {
            var resolved = PlayerDisplayNames.Resolve(new[] { "つむぎ", "ずんだ" });

            Assert.IsFalse(resolved.HasDuplicates);
            Assert.AreEqual(string.Empty, DuplicateNameNotice.Build(resolved, includeHostAdvice: true));
            Assert.AreEqual(
                string.Empty,
                DuplicateNameNotice.Build(PlayerDisplayNameSet.Empty, includeHostAdvice: true));
        }

        [Test]
        public void Build_WithNull_ReturnsEmptyInsteadOfThrowing()
        {
            Assert.AreEqual(string.Empty, DuplicateNameNotice.Build(null, includeHostAdvice: false));
        }

        [Test]
        public void Build_ForAClient_MentionsTheNameAndTheAssignedLabels_WithoutHostAdvice()
        {
            var resolved = PlayerDisplayNames.Resolve(new[] { "つむぎ", "ホストさん", "つむぎ" });

            var message = DuplicateNameNotice.Build(resolved, includeHostAdvice: false);

            StringAssert.Contains("（つむぎ）", message);
            StringAssert.Contains("つむぎ #1", message);
            StringAssert.Contains("つむぎ #2", message);
            StringAssert.DoesNotContain(DuplicateNameNotice.HostAdvice, message);
        }

        [Test]
        public void Build_ForTheHost_AppendsTheAdvice()
        {
            var resolved = PlayerDisplayNames.Resolve(new[] { "つむぎ", "つむぎ" });

            var message = DuplicateNameNotice.Build(resolved, includeHostAdvice: true);

            StringAssert.Contains(DuplicateNameNotice.HostAdvice, message);
        }

        /// <summary>
        /// 連番は既存の名前と衝突する番号を飛ばすので <c>#1</c> から始まるとは限らない。
        /// 注意文は固定の「#1 / #2」ではなく、**実際に割り当てた表示名**を書く（レビュー L-3）。
        /// </summary>
        [Test]
        public void Build_QuotesTheLabelsActuallyAssigned_NotAFixedOneAndTwo()
        {
            var resolved = PlayerDisplayNames.Resolve(new[] { "つむぎ", "つむぎ", "つむぎ #1" });

            CollectionAssert.AreEqual(new[] { "つむぎ #2", "つむぎ #3" }, resolved.NumberedLabels);

            var message = DuplicateNameNotice.Build(resolved, includeHostAdvice: false);

            StringAssert.Contains(
                "つむぎ #2" + DuplicateNameNotice.NameSeparator + "つむぎ #3", message);
        }

        [Test]
        public void Build_WithSeveralDuplicatedNames_JoinsThem()
        {
            var resolved = PlayerDisplayNames.Resolve(new[] { "つむぎ", "ずんだ", "つむぎ", "ずんだ" });

            var message = DuplicateNameNotice.Build(resolved, includeHostAdvice: false);

            StringAssert.Contains("つむぎ" + DuplicateNameNotice.NameSeparator + "ずんだ", message);
            StringAssert.Contains("つむぎ #1", message);
            StringAssert.Contains("ずんだ #1", message);
        }

        [Test]
        public void Build_IgnoresNumberingTargetsThatWereExcluded()
        {
            // 切断中エントリ（伏せ字）は連番の対象外なので、注意文にも出てこない。
            var resolved = PlayerDisplayNames.Resolve(
                new[] { "（切断中）", "（切断中）", "つむぎ", "つむぎ" },
                new[] { false, false, true, true });

            var message = DuplicateNameNotice.Build(resolved, includeHostAdvice: false);

            StringAssert.Contains("（つむぎ）", message);
            StringAssert.DoesNotContain("切断中", message);
        }

        [Test]
        public void BuildLogLine_ContainsTheNameAndTheCount()
        {
            var line = DuplicateNameNotice.BuildLogLine("つむぎ", 2);

            StringAssert.Contains("つむぎ", line);
            StringAssert.Contains("2", line);
        }
    }
}
