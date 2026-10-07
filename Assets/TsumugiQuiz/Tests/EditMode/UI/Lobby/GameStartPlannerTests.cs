using System;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Room;
using TsumugiQuiz.UI.Views.Lobby;

namespace TsumugiQuiz.Tests.EditMode.UI.Lobby
{
    /// <summary>
    /// ロビーの「ゲーム開始」の下準備（<see cref="GameStartPlanner"/>、#95）のテスト。
    /// 「読み込めた問題セットとルーム設定から出題できるか」を、Unity API に依存しない範囲で検証する。
    /// 絞り込みそのものの仕様は <c>QuestionSelectorTests</c>（#19）が担当し、ここでは
    /// 「開始できる / できない」の判定と理由（<see cref="GameStartBlocker"/>）に絞る。
    /// </summary>
    public class GameStartPlannerTests
    {
        private const string FolderPath = @"C:\Users\test\Documents\TsumugiQuiz\Questions";

        [Test]
        public void Plan_NullResult_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => GameStartPlanner.Plan(null, QuestionSelectionSettings.Default, FolderPath));
        }

        [Test]
        public void Plan_NoSetsAndNoErrors_ReportsNoQuestionSets()
        {
            var plan = GameStartPlanner.Plan(Result(), QuestionSelectionSettings.Default, FolderPath);

            Assert.IsFalse(plan.CanStart, "問題セットが 1 件も無ければ開始できないはず。");
            Assert.AreEqual(GameStartBlocker.NoQuestionSets, plan.Blocker);
            StringAssert.Contains(FolderPath, plan.FailureMessage, "問題フォルダの場所を文言に添えるはず。");
            Assert.IsNull(plan.PoolSource);
            Assert.AreEqual(0, plan.Sets.Count);
        }

        [Test]
        public void Plan_FolderError_ReportsLoadFailedWithReason()
        {
            var result = Result(folderErrors: new[] { "問題フォルダが見つかりません: " + FolderPath });

            var plan = GameStartPlanner.Plan(result, QuestionSelectionSettings.Default, FolderPath);

            Assert.IsFalse(plan.CanStart);
            Assert.AreEqual(GameStartBlocker.LoadFailed, plan.Blocker);
            StringAssert.Contains("問題フォルダが見つかりません", plan.FailureMessage);
            CollectionAssert.Contains(plan.Warnings, "問題フォルダが見つかりません: " + FolderPath);
        }

        [Test]
        public void Plan_AllSetsSkipped_ReportsLoadFailed()
        {
            var result = Result(skipped: new[]
            {
                new QuestionSetLoadError(FolderPath + @"\broken.json", new[] { "questions[0].type は必須です。" }),
            });

            var plan = GameStartPlanner.Plan(result, QuestionSelectionSettings.Default, FolderPath);

            Assert.IsFalse(plan.CanStart);
            Assert.AreEqual(GameStartBlocker.LoadFailed, plan.Blocker);
            StringAssert.Contains("スキップ", plan.FailureMessage);
        }

        [Test]
        public void Plan_WithQuestions_IsReadyAndKeepsWholePoolAsSource()
        {
            var result = Result(sets: new[]
            {
                Set("set-a", FreeText("a1"), Choice("a2")),
                Set("set-b", FreeText("b1")),
            });

            var plan = GameStartPlanner.Plan(result, QuestionSelectionSettings.Default, FolderPath);

            Assert.IsTrue(plan.CanStart, "既定設定なら 3 問とも候補になるはず。");
            Assert.AreEqual(GameStartBlocker.None, plan.Blocker);
            Assert.IsNull(plan.FailureMessage);
            Assert.AreEqual(2, plan.Sets.Count, "setIds 適用のためにセットをそのまま渡すはず。");
            Assert.AreEqual(3, plan.PoolCount);
            Assert.AreEqual(3, plan.CandidateCount);

            // 供給元は「絞り込み前の候補プール全体」（実際の出題列は GameSession.StartSession が作る）。
            Assert.IsNotNull(plan.PoolSource);
            Assert.AreEqual(3, plan.PoolSource.Count);
            Assert.IsTrue(plan.PoolSource.TryGetQuestion(0, out var first));
            Assert.AreEqual("a1", first.Id);
        }

        [Test]
        public void Plan_CountSmallerThanCandidates_DoesNotAffectCandidateCount()
        {
            var result = Result(sets: new[] { Set("set-a", FreeText("a1"), FreeText("a2"), FreeText("a3")) });

            var plan = GameStartPlanner.Plan(result, new QuestionSelectionSettings(count: 1), FolderPath);

            Assert.IsTrue(plan.CanStart);
            Assert.AreEqual(3, plan.CandidateCount, "CandidateCount は questions.count で切り詰める前の値。");
            Assert.AreEqual(3, plan.PoolSource.Count, "供給元も切り詰めない（出題数の適用は StartSession）。");
        }

        [Test]
        public void Plan_CountZeroMeansAllQuestions_IsReady()
        {
            var result = Result(sets: new[] { Set("set-a", FreeText("a1"), FreeText("a2")) });

            var plan = GameStartPlanner.Plan(
                result, new QuestionSelectionSettings(count: QuestionSelectionSettings.AllQuestions), FolderPath);

            Assert.IsTrue(plan.CanStart);
            Assert.AreEqual(2, plan.CandidateCount);
        }

        [Test]
        public void Plan_TypeFilterExcludesEverything_ReportsNoMatchingQuestions()
        {
            var result = Result(sets: new[] { Set("set-a", FreeText("a1"), FreeText("a2")) });

            var plan = GameStartPlanner.Plan(
                result, new QuestionSelectionSettings(typeFilter: QuestionTypeFilter.Choice), FolderPath);

            Assert.IsFalse(plan.CanStart);
            Assert.AreEqual(GameStartBlocker.NoMatchingQuestions, plan.Blocker);
            StringAssert.Contains("2", plan.FailureMessage, "読み込み済みの問題数を文言に添えるはず。");
            Assert.AreEqual(2, plan.PoolCount);
        }

        [Test]
        public void Plan_TagFilterExcludesEverything_ReportsNoMatchingQuestions()
        {
            var result = Result(sets: new[] { Set("set-a", FreeText("a1", tags: new[] { "地理" })) });

            var plan = GameStartPlanner.Plan(
                result, new QuestionSelectionSettings(tagFilter: new[] { "音楽" }), FolderPath);

            Assert.IsFalse(plan.CanStart);
            Assert.AreEqual(GameStartBlocker.NoMatchingQuestions, plan.Blocker);
        }

        [Test]
        public void Plan_TagFilterMatchesSubset_IsReadyWithCandidateCount()
        {
            var result = Result(sets: new[]
            {
                Set("set-a", FreeText("a1", tags: new[] { "地理" }), FreeText("a2", tags: new[] { "音楽" })),
            });

            var plan = GameStartPlanner.Plan(
                result, new QuestionSelectionSettings(tagFilter: new[] { "音楽" }), FolderPath);

            Assert.IsTrue(plan.CanStart);
            Assert.AreEqual(2, plan.PoolCount);
            Assert.AreEqual(1, plan.CandidateCount);
        }

        [Test]
        public void Plan_SetIdsSelectsOneSet_IsReadyWithThatSetOnly()
        {
            var result = Result(sets: new[]
            {
                Set("set-a", FreeText("a1")),
                Set("set-b", FreeText("b1"), FreeText("b2")),
            });

            var plan = GameStartPlanner.Plan(
                result, new QuestionSelectionSettings(setIds: new[] { "set-b" }), FolderPath);

            Assert.IsTrue(plan.CanStart);
            Assert.AreEqual(2, plan.PoolCount, "setIds で絞ったあとの候補プールは set-b の 2 問。");
            Assert.AreEqual(2, plan.CandidateCount);
            Assert.AreEqual(2, plan.Sets.Count, "setIds の適用は GameSession 側で行うのでセットは全件渡す。");
        }

        [Test]
        public void Plan_UnknownSetId_IsReportedAsWarning()
        {
            var result = Result(sets: new[] { Set("set-a", FreeText("a1")) });

            var plan = GameStartPlanner.Plan(
                result, new QuestionSelectionSettings(setIds: new[] { "set-a", "set-missing" }), FolderPath);

            Assert.IsTrue(plan.CanStart, "実在する setId が 1 つでもあれば開始できる。");
            CollectionAssert.Contains(
                plan.Warnings, "ルーム設定の setId「set-missing」に一致する問題セットがありません。");
        }

        [Test]
        public void Plan_AllSetIdsUnknown_ReportsNoMatchingQuestions()
        {
            var result = Result(sets: new[] { Set("set-a", FreeText("a1")) });

            var plan = GameStartPlanner.Plan(
                result, new QuestionSelectionSettings(setIds: new[] { "set-missing" }), FolderPath);

            Assert.IsFalse(plan.CanStart);
            Assert.AreEqual(GameStartBlocker.NoMatchingQuestions, plan.Blocker);
        }

        [Test]
        public void Plan_SomeSetsSkipped_StartsAndWarns()
        {
            var result = Result(
                sets: new[] { Set("set-a", FreeText("a1")) },
                skipped: new[] { new QuestionSetLoadError(FolderPath + @"\broken.json", new[] { "不正な JSON" }) });

            var plan = GameStartPlanner.Plan(result, QuestionSelectionSettings.Default, FolderPath);

            Assert.IsTrue(plan.CanStart, "読める問題セットが残っていれば開始できる（1 件の不正で巻き込まない）。");
            CollectionAssert.Contains(plan.Warnings, "読み込めなかった問題セットが 1 件あります（ホスト設定画面で内容を確認できます）。");
        }

        [Test]
        public void Plan_NullSettings_UsesDefaults()
        {
            var result = Result(sets: new[] { Set("set-a", FreeText("a1"), Choice("a2")) });

            var plan = GameStartPlanner.Plan(result, null, FolderPath);

            Assert.IsTrue(plan.CanStart);
            Assert.AreEqual(2, plan.CandidateCount, "既定の typeFilter は both。");
        }

        [Test]
        public void Plan_WithoutFolderPath_OmitsFolderFromMessage()
        {
            var plan = GameStartPlanner.Plan(Result(), QuestionSelectionSettings.Default, null);

            Assert.IsFalse(plan.CanStart);
            StringAssert.DoesNotContain("問題フォルダ:", plan.FailureMessage);
        }

        [Test]
        public void Plan_ImageOnlyExcludesEverything_ReportsNoMatchingQuestions()
        {
            var result = Result(sets: new[] { Set("set-a", FreeText("a1")) });

            var plan = GameStartPlanner.Plan(
                result, new QuestionSelectionSettings(imageOnly: true), FolderPath);

            Assert.IsFalse(plan.CanStart);
            Assert.AreEqual(GameStartBlocker.NoMatchingQuestions, plan.Blocker);
        }

        [Test]
        public void Plan_ShuffleOrderEnabled_DoesNotChangeCandidateCountOrPoolOrder()
        {
            // PR #104 レビュー L-6: 候補数の「下見」はシャッフルを無効にして行うので、
            // shuffleOrder が true でも結果（候補数・供給元の並び）は変わらない
            // （実際の出題順は GameSession.StartSession が決める）。
            var result = Result(sets: new[] { Set("set-a", FreeText("a1"), FreeText("a2"), FreeText("a3")) });

            var shuffled = GameStartPlanner.Plan(
                result, new QuestionSelectionSettings(shuffleOrder: true), FolderPath);
            var ordered = GameStartPlanner.Plan(
                result, new QuestionSelectionSettings(shuffleOrder: false), FolderPath);

            Assert.IsTrue(shuffled.CanStart);
            Assert.AreEqual(ordered.CandidateCount, shuffled.CandidateCount);
            Assert.AreEqual(ordered.PoolCount, shuffled.PoolCount);

            Assert.IsTrue(shuffled.PoolSource.TryGetQuestion(0, out var first));
            Assert.IsTrue(shuffled.PoolSource.TryGetQuestion(2, out var last));
            Assert.AreEqual("a1", first.Id, "供給元は読み込み順のまま（並べ替えない）。");
            Assert.AreEqual("a3", last.Id);
        }

        [Test]
        public void Plan_QuestionWithoutType_IsExcludedAndReportedAsWarning()
        {
            // PR #104 レビュー L-6: type の無い問題は配信できないので候補から外れる。
            // 除外の内訳は Warnings で呼び出し側（ログ）へ返す（ここでは Unity のログは出さない）。
            var result = Result(sets: new[] { Set("set-a", FreeText("a1"), Untyped("a2")) });

            var plan = GameStartPlanner.Plan(result, QuestionSelectionSettings.Default, FolderPath);

            Assert.IsTrue(plan.CanStart);
            Assert.AreEqual(2, plan.PoolCount);
            Assert.AreEqual(1, plan.CandidateCount, "type が無い問題は候補に入らない。");
            CollectionAssert.Contains(
                plan.Warnings, "出題形式（type）が設定されていない問題 1 件を除外しました。");
        }

        [Test]
        public void Plan_FolderErrorContainingPath_DoesNotRepeatTheFolderPath()
        {
            // PR #104 レビュー L-3: 理由の文言に既にパスが入っている場合は重ねて出さない。
            var result = Result(folderErrors: new[] { "問題フォルダが見つかりません: " + FolderPath });

            var plan = GameStartPlanner.Plan(result, QuestionSelectionSettings.Default, FolderPath);

            Assert.IsFalse(plan.CanStart);
            Assert.AreEqual(
                1,
                CountOccurrences(plan.FailureMessage, FolderPath),
                $"問題フォルダのパスが 2 回出ています: {plan.FailureMessage}");
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var index = text.IndexOf(value, System.StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = text.IndexOf(value, index + value.Length, System.StringComparison.Ordinal);
            }

            return count;
        }

        private static QuestionRepositoryResult Result(
            IReadOnlyList<QuestionSet> sets = null,
            IReadOnlyList<QuestionSetLoadError> skipped = null,
            IReadOnlyList<string> folderErrors = null) =>
            new QuestionRepositoryResult(
                sets ?? Array.Empty<QuestionSet>(),
                skipped ?? Array.Empty<QuestionSetLoadError>(),
                folderErrors ?? Array.Empty<string>());

        private static QuestionSet Set(string setId, params Question[] questions) =>
            new QuestionSet(1, setId, setId + " のタイトル", string.Empty, questions);

        private static Question FreeText(string id, IReadOnlyList<string> tags = null) =>
            new Question(id, QuestionType.FreeText, id + " の問題文", answers: new[] { "答え" }, tags: tags);

        /// <summary>出題形式（type）が設定されていない問題（読み込み時の検証を通っていない想定）。</summary>
        private static Question Untyped(string id) =>
            new Question(id, null, id + " の問題文", answers: new[] { "答え" });

        private static Question Choice(string id) =>
            new Question(
                id,
                QuestionType.Choice,
                id + " の問題文",
                choices: new[] { "選択肢1", "選択肢2" },
                correctIndex: 0);
    }
}
