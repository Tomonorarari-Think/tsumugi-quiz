using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Room;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.Network
{
    /// <summary>
    /// 出題フィルタ・出題数・出題順シャッフル（<see cref="QuestionSelector"/>、#19）のテスト。
    /// 仕様は docs/room-settings.md §1「問題選択」・§5。
    /// </summary>
    public class QuestionSelectorTests
    {
        private const int Seed = 20260913;

        [Test]
        public void Select_WithDefaults_KeepsAllCandidatesButTrimsToCount()
        {
            var pool = Pool(15);
            var settings = new QuestionSelectionSettings(shuffleOrder: false);

            var selection = QuestionSelector.Select(pool, settings, Seed);

            Assert.AreEqual(15, selection.PoolCount, "候補プールの件数を返すはず。");
            Assert.AreEqual(15, selection.CandidateCount, "既定ではフィルタで落ちないはず。");
            Assert.AreEqual(QuestionSelectionSettings.DefaultCount, selection.Count, "既定の出題数は 10 問。");
        }

        [Test]
        public void Select_CountZero_UsesAllCandidates()
        {
            var pool = Pool(4);
            var settings = new QuestionSelectionSettings(
                count: QuestionSelectionSettings.AllQuestions, shuffleOrder: false);

            var selection = QuestionSelector.Select(pool, settings, Seed);

            Assert.AreEqual(4, selection.Count, "count = 0 は「全問」。");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, selection.SourceIndices, "シャッフル無しなら元の並び。");
        }

        [Test]
        public void Select_CountAboveCandidateCount_ClampsToCandidateCount()
        {
            var pool = Pool(3);
            var settings = new QuestionSelectionSettings(count: 10, shuffleOrder: false);

            var selection = QuestionSelector.Select(pool, settings, Seed);

            Assert.AreEqual(3, selection.Count, "候補数を超える出題数は候補数に丸める（docs/room-settings.md §5）。");
        }

        [Test]
        public void Select_TypeFilterFreeText_ExcludesChoiceQuestions()
        {
            var pool = new List<Question>
            {
                FreeText("q-free-1"),
                Choice("q-choice-1"),
                FreeText("q-free-2"),
            };
            var settings = new QuestionSelectionSettings(
                typeFilter: QuestionTypeFilter.FreeText,
                count: QuestionSelectionSettings.AllQuestions,
                shuffleOrder: false);

            var selection = QuestionSelector.Select(pool, settings, Seed);

            CollectionAssert.AreEqual(new[] { "q-free-1", "q-free-2" }, Ids(selection));
        }

        [Test]
        public void Select_TypeFilterChoice_YieldsOnlyChoiceQuestions()
        {
            // #17: 選択式の進行を実装したので、typeFilter = "choice" は選択式だけを候補にする。
            var pool = new List<Question> { FreeText("q-free-1"), Choice("q-choice-1") };
            var settings = new QuestionSelectionSettings(
                typeFilter: QuestionTypeFilter.Choice,
                count: QuestionSelectionSettings.AllQuestions,
                shuffleOrder: false);

            var selection = QuestionSelector.Select(pool, settings, Seed);

            CollectionAssert.AreEqual(new[] { "q-choice-1" }, Ids(selection));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Select_TypeFilterBoth_IncludesFreeTextAndChoice()
        {
            // #17: typeFilter が "both" なら選択式も freeText と同様に候補へ含める。
            var pool = new List<Question> { FreeText("q-free-1"), Choice("q-choice-1"), FreeText("q-free-2") };
            var settings = new QuestionSelectionSettings(
                typeFilter: QuestionTypeFilter.Both,
                count: QuestionSelectionSettings.AllQuestions,
                shuffleOrder: false);

            var selection = QuestionSelector.Select(pool, settings, Seed);

            CollectionAssert.AreEqual(new[] { "q-free-1", "q-choice-1", "q-free-2" }, Ids(selection));
            Assert.AreEqual(3, selection.CandidateCount, "選択式も候補数に数える（#17）。");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Select_ChoiceExcludedByTypeFilter_DoesNotWarn()
        {
            // typeFilter (freeText) で落ちる選択式は「型フィルタ」で外れるだけで、警告は出さない。
            var pool = new List<Question> { FreeText("q-free-1"), Choice("q-choice-1") };
            var settings = new QuestionSelectionSettings(
                typeFilter: QuestionTypeFilter.FreeText,
                count: QuestionSelectionSettings.AllQuestions,
                shuffleOrder: false);

            var selection = QuestionSelector.Select(pool, settings, Seed);

            CollectionAssert.AreEqual(new[] { "q-free-1" }, Ids(selection));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Select_TypeMissing_IsExcludedAndCounted()
        {
            // type が無い問題は配信できない（QuestionDistributor が弾く）ので候補に入れない。
            // 通常は QuestionSetValidator が読み込み時に弾くため、件数を警告に残す。
            var pool = new List<Question>
            {
                new Question("q-broken", null, "形式が無い問題", answers: new[] { "a" }),
                FreeText("q-free-1"),
            };
            var settings = new QuestionSelectionSettings(
                count: QuestionSelectionSettings.AllQuestions, shuffleOrder: false);

            LogAssert.Expect(LogType.Warning, new Regex("type.*設定されていない問題 1 件"));

            var selection = QuestionSelector.Select(pool, settings, Seed);

            CollectionAssert.AreEqual(new[] { "q-free-1" }, Ids(selection));
            Assert.AreEqual(1, selection.MissingTypeCount, "除外した件数を持つはず。");
        }

        [Test]
        public void Select_ImageOnly_KeepsOnlyQuestionsWithImage()
        {
            var pool = new List<Question>
            {
                FreeText("q-noimage"),
                FreeText("q-image", imagePath: "images/tokyo.png"),
                FreeText("q-blank-image", imagePath: "   "),
            };
            var settings = new QuestionSelectionSettings(
                imageOnly: true, count: QuestionSelectionSettings.AllQuestions, shuffleOrder: false);

            var selection = QuestionSelector.Select(pool, settings, Seed);

            CollectionAssert.AreEqual(new[] { "q-image" }, Ids(selection), "空白だけの imagePath は画像なし扱い。");
        }

        [Test]
        public void Select_TagFilter_KeepsQuestionsMatchingAnyTag()
        {
            var pool = new List<Question>
            {
                FreeText("q-geo", tags: new[] { "地理" }),
                FreeText("q-history", tags: new[] { "歴史" }),
                FreeText("q-both", tags: new[] { "歴史", "地理" }),
                FreeText("q-none"),
            };
            var settings = new QuestionSelectionSettings(
                tagFilter: new[] { "地理", "科学" },
                count: QuestionSelectionSettings.AllQuestions,
                shuffleOrder: false);

            var selection = QuestionSelector.Select(pool, settings, Seed);

            CollectionAssert.AreEqual(new[] { "q-geo", "q-both" }, Ids(selection), "いずれかのタグに一致する問題だけ。");
        }

        [Test]
        public void Select_CombinedFilters_AreAppliedTogether()
        {
            var pool = new List<Question>
            {
                FreeText("q-ok", tags: new[] { "地理" }, imagePath: "a.png"),
                FreeText("q-no-image", tags: new[] { "地理" }),
                Choice("q-choice", tags: new[] { "地理" }, imagePath: "b.png"),
                FreeText("q-other-tag", tags: new[] { "歴史" }, imagePath: "c.png"),
            };
            var settings = new QuestionSelectionSettings(
                typeFilter: QuestionTypeFilter.FreeText,
                imageOnly: true,
                tagFilter: new[] { "地理" },
                count: QuestionSelectionSettings.AllQuestions,
                shuffleOrder: false);

            var selection = QuestionSelector.Select(pool, settings, Seed);

            CollectionAssert.AreEqual(new[] { "q-ok" }, Ids(selection));
            Assert.AreEqual(1, selection.CandidateCount);
            Assert.AreEqual(4, selection.PoolCount);
        }

        [Test]
        public void Select_NoMatchingQuestion_ReturnsEmptySelection()
        {
            var pool = new List<Question> { FreeText("q-free-1") };
            var settings = new QuestionSelectionSettings(
                tagFilter: new[] { "存在しないタグ" }, count: QuestionSelectionSettings.AllQuestions);

            var selection = QuestionSelector.Select(pool, settings, Seed);

            Assert.IsTrue(selection.IsEmpty, "該当 0 件なら空の結果を返す（例外にしない）。");
            Assert.AreEqual(0, selection.Count);
            Assert.AreEqual(0, selection.CandidateCount);
            Assert.AreEqual(1, selection.PoolCount, "候補プールの件数は残す（ログ・UI 用）。");
        }

        [Test]
        public void Select_EmptyPool_ReturnsEmptySelection()
        {
            var selection = QuestionSelector.Select(
                new List<Question>(), QuestionSelectionSettings.Default, Seed);

            Assert.IsTrue(selection.IsEmpty);
            Assert.AreEqual(0, selection.PoolCount);
        }

        [Test]
        public void Select_ShuffleOrder_IsDeterministicForSameSeed()
        {
            var pool = Pool(20);
            var settings = new QuestionSelectionSettings(
                count: QuestionSelectionSettings.AllQuestions, shuffleOrder: true);

            var first = QuestionSelector.Select(pool, settings, Seed);
            var second = QuestionSelector.Select(pool, settings, Seed);

            CollectionAssert.AreEqual(first.SourceIndices, second.SourceIndices, "同じシードなら同じ並びになるはず。");
            CollectionAssert.AreEqual(Ids(first), Ids(second));
        }

        [Test]
        public void Select_ShuffleOrder_DiffersForDifferentSeed()
        {
            var pool = Pool(20);
            var settings = new QuestionSelectionSettings(
                count: QuestionSelectionSettings.AllQuestions, shuffleOrder: true);

            var first = QuestionSelector.Select(pool, settings, Seed);
            var second = QuestionSelector.Select(pool, settings, Seed + 1);

            CollectionAssert.AreNotEqual(first.SourceIndices, second.SourceIndices, "別のシードでは並びが変わるはず。");
            CollectionAssert.AreEquivalent(first.SourceIndices, second.SourceIndices, "含まれる問題は同じはず。");
        }

        [Test]
        public void Select_ShuffleOrder_IsPermutationOfCandidates()
        {
            var pool = Pool(8);
            var settings = new QuestionSelectionSettings(
                count: QuestionSelectionSettings.AllQuestions, shuffleOrder: true);

            var selection = QuestionSelector.Select(pool, settings, Seed);

            CollectionAssert.AreEquivalent(
                new[] { 0, 1, 2, 3, 4, 5, 6, 7 },
                selection.SourceIndices,
                "シャッフルは並べ替えなので、重複・欠落があってはいけない。");
        }

        [Test]
        public void Select_ShuffleOrderFalse_KeepsPoolOrder()
        {
            var pool = Pool(5);
            var settings = new QuestionSelectionSettings(
                count: QuestionSelectionSettings.AllQuestions, shuffleOrder: false);

            var selection = QuestionSelector.Select(pool, settings, Seed);

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, selection.SourceIndices);
        }

        [Test]
        public void Select_ShuffleThenCount_TakesFromShuffledOrder()
        {
            var pool = Pool(20);
            var shuffled = QuestionSelector.Select(
                pool,
                new QuestionSelectionSettings(count: QuestionSelectionSettings.AllQuestions, shuffleOrder: true),
                Seed);
            var trimmed = QuestionSelector.Select(
                pool, new QuestionSelectionSettings(count: 5, shuffleOrder: true), Seed);

            Assert.AreEqual(5, trimmed.Count);
            for (var i = 0; i < trimmed.Count; i++)
            {
                Assert.AreEqual(
                    shuffled.SourceIndices[i],
                    trimmed.SourceIndices[i],
                    "出題数の切り詰めはシャッフル後の先頭から取るはず。");
            }
        }

        [Test]
        public void SelectFromSets_FiltersBySetIds()
        {
            var sets = new List<QuestionSet>
            {
                Set("set-a", FreeText("a-1"), FreeText("a-2")),
                Set("set-b", FreeText("b-1")),
                Set("set-c", FreeText("c-1")),
            };
            var settings = new QuestionSelectionSettings(
                setIds: new[] { "set-a", "set-c" },
                count: QuestionSelectionSettings.AllQuestions,
                shuffleOrder: false);

            var selection = QuestionSelector.SelectFromSets(sets, settings, Seed);

            CollectionAssert.AreEqual(new[] { "a-1", "a-2", "c-1" }, Ids(selection));
        }

        [Test]
        public void SelectFromSets_EmptySetIds_UsesEverySet()
        {
            var sets = new List<QuestionSet>
            {
                Set("set-a", FreeText("a-1")),
                Set("set-b", FreeText("b-1")),
            };
            var settings = new QuestionSelectionSettings(
                count: QuestionSelectionSettings.AllQuestions, shuffleOrder: false);

            var selection = QuestionSelector.SelectFromSets(sets, settings, Seed);

            CollectionAssert.AreEqual(new[] { "a-1", "b-1" }, Ids(selection), "setIds が空なら全セットが対象。");
        }

        [Test]
        public void SelectFromSets_UnknownSetId_IsIgnoredAndReported()
        {
            var sets = new List<QuestionSet> { Set("set-a", FreeText("a-1")) };
            var settings = new QuestionSelectionSettings(
                setIds: new[] { "set-a", "set-missing" },
                count: QuestionSelectionSettings.AllQuestions,
                shuffleOrder: false);

            var selection = QuestionSelector.SelectFromSets(sets, settings, Seed);

            CollectionAssert.AreEqual(new[] { "a-1" }, Ids(selection), "存在しない setId は無視する（§5）。");
            CollectionAssert.AreEqual(
                new[] { "set-missing" },
                selection.UnknownSetIds,
                "無視した setId は呼び出し側が警告できるよう結果に残す。");
        }

        [Test]
        public void SelectFromSets_AllSetIdsKnown_ReportsNoUnknownSetId()
        {
            var sets = new List<QuestionSet> { Set("set-a", FreeText("a-1")), Set("set-b", FreeText("b-1")) };
            var settings = new QuestionSelectionSettings(
                setIds: new[] { "set-a", "set-b" },
                count: QuestionSelectionSettings.AllQuestions,
                shuffleOrder: false);

            var selection = QuestionSelector.SelectFromSets(sets, settings, Seed);

            CollectionAssert.IsEmpty(selection.UnknownSetIds);
        }

        [Test]
        public void Select_FlatPool_ReportsNoUnknownSetId()
        {
            // セット情報を持たない経路では setIds を判定できないので、未知として報告しない。
            var settings = new QuestionSelectionSettings(
                setIds: new[] { "set-a" }, count: QuestionSelectionSettings.AllQuestions, shuffleOrder: false);

            var selection = QuestionSelector.Select(Pool(2), settings, Seed);

            CollectionAssert.IsEmpty(selection.UnknownSetIds);
            Assert.AreEqual(2, selection.Count, "フラットな候補では setIds は効かない。");
        }

        [Test]
        public void SelectFromSets_DuplicateQuestionId_IsKeptAsSeparateQuestion()
        {
            // 同じ id の問題が複数セットに含まれていても取り除かない（docs/room-settings.md §1）。
            var sets = new List<QuestionSet>
            {
                Set("set-a", FreeText("dup-1")),
                Set("set-b", FreeText("dup-1")),
            };
            var settings = new QuestionSelectionSettings(
                count: QuestionSelectionSettings.AllQuestions, shuffleOrder: false);

            var selection = QuestionSelector.SelectFromSets(sets, settings, Seed);

            CollectionAssert.AreEqual(new[] { "dup-1", "dup-1" }, Ids(selection));
            Assert.AreEqual(2, selection.CandidateCount);
        }

        [Test]
        public void SelectFromSets_UnknownSetIdOnly_ReturnsEmptySelection()
        {
            var sets = new List<QuestionSet> { Set("set-a", FreeText("a-1")) };
            var settings = new QuestionSelectionSettings(
                setIds: new[] { "set-missing" }, count: QuestionSelectionSettings.AllQuestions);

            var selection = QuestionSelector.SelectFromSets(sets, settings, Seed);

            Assert.IsTrue(selection.IsEmpty);
        }

        [Test]
        public void SelectFromSets_NullSets_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => QuestionSelector.SelectFromSets(null, QuestionSelectionSettings.Default, Seed));
        }

        [Test]
        public void Select_NullQuestions_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => QuestionSelector.Select(null, QuestionSelectionSettings.Default, Seed));
        }

        private static List<Question> Pool(int count)
        {
            var questions = new List<Question>(count);
            for (var i = 0; i < count; i++)
            {
                questions.Add(FreeText($"q-{i:D2}"));
            }

            return questions;
        }

        private static Question FreeText(
            string id, IReadOnlyList<string> tags = null, string imagePath = null) =>
            new Question(
                id,
                QuestionType.FreeText,
                $"{id} の問題文",
                answers: new[] { "こたえ" },
                imagePath: imagePath,
                tags: tags);

        private static Question Choice(
            string id, IReadOnlyList<string> tags = null, string imagePath = null) =>
            new Question(
                id,
                QuestionType.Choice,
                $"{id} の問題文",
                choices: new[] { "A", "B" },
                correctIndex: 0,
                imagePath: imagePath,
                tags: tags);

        private static QuestionSet Set(string setId, params Question[] questions) =>
            new QuestionSet(1, setId, $"{setId} のタイトル", questions: questions);

        private static List<string> Ids(QuestionSelection selection)
        {
            var ids = new List<string>(selection.Count);
            for (var i = 0; i < selection.Questions.Count; i++)
            {
                ids.Add(selection.Questions[i].Id);
            }

            return ids;
        }
    }
}
