using System;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 同名プレイヤーの表示名（<see cref="PlayerDisplayNames"/>）の検証（issue #85、docs/network.md §2.3）。
    /// 再接続経路でだけ起こる「同名の同時接続」を画面上で区別できるようにする純 C# ロジック。
    /// </summary>
    public class PlayerDisplayNamesTests
    {
        private static readonly string[] NoNames = Array.Empty<string>();

        [Test]
        public void Resolve_WithNoDuplicates_KeepsEveryNameAsIs()
        {
            var resolved = PlayerDisplayNames.Resolve(new[] { "つむぎ", "ホストさん", "だれか" });

            CollectionAssert.AreEqual(new[] { "つむぎ", "ホストさん", "だれか" }, resolved.Labels);
            Assert.IsFalse(resolved.HasDuplicates, "重複が無ければ連番は付けないはず。");
            CollectionAssert.IsEmpty(resolved.DuplicatedNames);
        }

        [Test]
        public void Resolve_WithDuplicates_NumbersThemInTheGivenOrder()
        {
            var resolved = PlayerDisplayNames.Resolve(new[] { "つむぎ", "ホストさん", "つむぎ" });

            CollectionAssert.AreEqual(new[] { "つむぎ #1", "ホストさん", "つむぎ #2" }, resolved.Labels);
            Assert.IsTrue(resolved.HasDuplicates);
            CollectionAssert.AreEqual(new[] { "つむぎ" }, resolved.DuplicatedNames);
        }

        [Test]
        public void Resolve_WithThreeOfTheSameName_NumbersAllOfThem()
        {
            var resolved = PlayerDisplayNames.Resolve(new[] { "A", "A", "A" });

            CollectionAssert.AreEqual(new[] { "A #1", "A #2", "A #3" }, resolved.Labels);
            CollectionAssert.AreEqual(new[] { "A" }, resolved.DuplicatedNames, "同じ名前は 1 回だけ報告するはず。");
        }

        [Test]
        public void Resolve_WithTwoDuplicatedNames_ReportsBothInFirstAppearanceOrder()
        {
            var resolved = PlayerDisplayNames.Resolve(new[] { "B", "A", "A", "B" });

            CollectionAssert.AreEqual(new[] { "B #1", "A #1", "A #2", "B #2" }, resolved.Labels);
            CollectionAssert.AreEqual(new[] { "B", "A" }, resolved.DuplicatedNames, "初出順で並ぶはず。");
        }

        [Test]
        public void Resolve_SkipsOrdinalsThatWouldCollideWithAnExistingName()
        {
            // プレイヤー名には「つむぎ #1」も付けられる（PlayerNameValidator は 1〜16 文字・
            // 制御文字以外を通す）。そのまま #1 を振ると 3 人目の名前と衝突するので番号を進める。
            var resolved = PlayerDisplayNames.Resolve(new[] { "つむぎ", "つむぎ", "つむぎ #1" });

            CollectionAssert.AreEqual(new[] { "つむぎ #2", "つむぎ #3", "つむぎ #1" }, resolved.Labels);
            CollectionAssert.AreEqual(new[] { "つむぎ #2", "つむぎ #3" }, resolved.NumberedLabels);

            var unique = new HashSet<string>(resolved.Labels, StringComparer.Ordinal);
            Assert.AreEqual(3, unique.Count, $"表示名が一意にならない: {string.Join(" / ", resolved.Labels)}");
        }

        [Test]
        public void Resolve_ProducesUniqueLabels_WhenEveryRowIsANumberingTarget()
        {
            var names = new[] { "つむぎ", "つむぎ #2", "つむぎ", "つむぎ", "ホストさん" };

            var resolved = PlayerDisplayNames.Resolve(names);

            var unique = new HashSet<string>(resolved.Labels, StringComparer.Ordinal);
            Assert.AreEqual(
                names.Length, unique.Count, $"表示名が一意にならない: {string.Join(" / ", resolved.Labels)}");
        }

        /// <summary>
        /// 保証しているのは「<b>連番を付けた</b>表示名が他のどの行の表示名とも衝突しないこと」であって、
        /// 全行の表示名が互いに異なることではない（レビュー M-1）。連番の対象外にした行どうし
        /// —— 実際の使い方では切断中エントリの伏せ字 —— は元々同じ文字列のまま残る。
        /// </summary>
        [Test]
        public void Resolve_LeavesExcludedRowsIdentical_ButNumberedLabelsNeverCollide()
        {
            var names = new[] { "（切断中）", "（切断中）", "つむぎ", "つむぎ" };
            var targets = new[] { false, false, true, true };

            var resolved = PlayerDisplayNames.Resolve(names, targets);

            // 対象外の 2 行は同じ文字列のまま（連番では区別しない）。
            Assert.AreEqual("（切断中）", resolved.GetLabel(0));
            Assert.AreEqual("（切断中）", resolved.GetLabel(1));

            // 連番を付けた表示名は、他のどの行の表示名とも衝突しない。
            var others = new HashSet<string>(resolved.Labels, StringComparer.Ordinal);
            foreach (var numbered in resolved.NumberedLabels)
            {
                var occurrences = 0;
                foreach (var label in resolved.Labels)
                {
                    if (string.Equals(label, numbered, StringComparison.Ordinal))
                    {
                        occurrences++;
                    }
                }

                Assert.AreEqual(1, occurrences, $"連番付きの表示名 '{numbered}' が他の行と衝突している。");
            }

            Assert.IsTrue(others.Contains("つむぎ #1") && others.Contains("つむぎ #2"));
        }

        [Test]
        public void NumberedLabels_ContainsOnlyTheLabelsThatReceivedAnOrdinal()
        {
            var resolved = PlayerDisplayNames.Resolve(new[] { "つむぎ", "ホストさん", "つむぎ" });

            CollectionAssert.AreEqual(new[] { "つむぎ #1", "つむぎ #2" }, resolved.NumberedLabels);
        }

        [Test]
        public void NumberedLabels_IsEmpty_WhenThereIsNoDuplicate()
        {
            var resolved = PlayerDisplayNames.Resolve(new[] { "つむぎ", "ずんだ" });

            CollectionAssert.IsEmpty(resolved.NumberedLabels);
            CollectionAssert.IsEmpty(PlayerDisplayNameSet.Empty.NumberedLabels);
        }

        [Test]
        public void Resolve_WithNullOrEmptyInput_ReturnsEmpty()
        {
            Assert.AreSame(PlayerDisplayNameSet.Empty, PlayerDisplayNames.Resolve(null));
            Assert.AreSame(PlayerDisplayNameSet.Empty, PlayerDisplayNames.Resolve(NoNames));
        }

        [Test]
        public void Resolve_WithNullElements_TreatsThemAsEmptyStrings()
        {
            var resolved = PlayerDisplayNames.Resolve(new string[] { null, "つむぎ", null });

            CollectionAssert.AreEqual(new[] { "#1", "つむぎ", "#2" }, resolved.Labels);
            CollectionAssert.AreEqual(new[] { string.Empty }, resolved.DuplicatedNames);
        }

        [Test]
        public void Resolve_ComparesNamesExactly()
        {
            // 全角・半角や大文字小文字は別名として扱う（名簿の名前は Ordinal 比較。LobbyRoster と同じ規則）。
            var resolved = PlayerDisplayNames.Resolve(new[] { "abc", "ABC", "ａｂｃ" });

            CollectionAssert.AreEqual(new[] { "abc", "ABC", "ａｂｃ" }, resolved.Labels);
            Assert.IsFalse(resolved.HasDuplicates);
        }

        [Test]
        public void Resolve_WithNumberingTargets_SkipsExcludedEntries()
        {
            // 切断中のエントリ（クライアントには伏せ字で届く）を対象外にする使い方。
            var names = new[] { "（切断中）", "つむぎ", "（切断中）", "つむぎ" };
            var targets = new[] { false, true, false, true };

            var resolved = PlayerDisplayNames.Resolve(names, targets);

            CollectionAssert.AreEqual(new[] { "（切断中）", "つむぎ #1", "（切断中）", "つむぎ #2" }, resolved.Labels);
            CollectionAssert.AreEqual(new[] { "つむぎ" }, resolved.DuplicatedNames, "対象外の伏せ字は重複に数えないはず。");
        }

        [Test]
        public void Resolve_WithNumberingTargets_DoesNotCountExcludedEntriesAsDuplicates()
        {
            // 「A が切断中（ホストには実名で見える） + 別人 B が同じ名前で接続中」だけなら、
            // 同時接続はしていないので連番は付けない（切断中は行のグレー表示で区別できる）。
            var resolved = PlayerDisplayNames.Resolve(
                new[] { "つむぎ", "つむぎ" }, new[] { false, true });

            CollectionAssert.AreEqual(new[] { "つむぎ", "つむぎ" }, resolved.Labels);
            Assert.IsFalse(resolved.HasDuplicates);
        }

        [Test]
        public void Resolve_WithAllTargetsEnabled_MatchesTheSingleArgumentOverload()
        {
            var names = new[] { "つむぎ", "つむぎ", "ホストさん" };

            var withTargets = PlayerDisplayNames.Resolve(names, new[] { true, true, true });
            var withoutTargets = PlayerDisplayNames.Resolve(names);

            CollectionAssert.AreEqual(withoutTargets.Labels, withTargets.Labels);
        }

        [Test]
        public void Resolve_WithMismatchedNumberingTargets_Throws()
        {
            var names = new[] { "つむぎ", "ホストさん" };

            Assert.Throws<ArgumentException>(() => PlayerDisplayNames.Resolve(names, new[] { true }));
        }

        [Test]
        public void WithOrdinal_AppendsTheNumber()
        {
            Assert.AreEqual("つむぎ #2", PlayerDisplayNames.WithOrdinal("つむぎ", 2));
            Assert.AreEqual("つむぎ #10", PlayerDisplayNames.WithOrdinal("つむぎ", 10));
        }

        [Test]
        public void WithOrdinal_ClampsNonPositiveOrdinalsToOne()
        {
            Assert.AreEqual("つむぎ #1", PlayerDisplayNames.WithOrdinal("つむぎ", 0));
            Assert.AreEqual("つむぎ #1", PlayerDisplayNames.WithOrdinal("つむぎ", -5));
        }

        [Test]
        public void WithOrdinal_WithEmptyName_ReturnsOnlyTheNumber()
        {
            Assert.AreEqual("#3", PlayerDisplayNames.WithOrdinal(string.Empty, 3));
            Assert.AreEqual("#3", PlayerDisplayNames.WithOrdinal(null, 3));
        }

        [Test]
        public void GetLabel_OutOfRange_ReturnsEmptyInsteadOfThrowing()
        {
            var resolved = PlayerDisplayNames.Resolve(new[] { "つむぎ" });

            Assert.AreEqual("つむぎ", resolved.GetLabel(0));
            Assert.AreEqual(string.Empty, resolved.GetLabel(-1));
            Assert.AreEqual(string.Empty, resolved.GetLabel(1));
        }

        [Test]
        public void Empty_HasNoLabelsAndNoDuplicates()
        {
            IReadOnlyList<string> labels = PlayerDisplayNameSet.Empty.Labels;

            CollectionAssert.IsEmpty(labels);
            CollectionAssert.IsEmpty(PlayerDisplayNameSet.Empty.DuplicatedNames);
            Assert.IsFalse(PlayerDisplayNameSet.Empty.HasDuplicates);
        }
    }
}
