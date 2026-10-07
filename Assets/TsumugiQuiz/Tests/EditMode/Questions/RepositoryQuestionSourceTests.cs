using System;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Questions;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="RepositoryQuestionSource"/> の平坦化・出題形式フィルタ・境界のテスト（#12）。
    /// </summary>
    public class RepositoryQuestionSourceTests
    {
        private static Question FreeText(string id) =>
            new Question(id, QuestionType.FreeText, $"{id} の問題文", answers: new[] { id });

        private static Question Choice(string id) =>
            new Question(id, QuestionType.Choice, $"{id} の問題文", choices: new[] { "あ", "い" }, correctIndex: 0);

        private static QuestionSet Set(string setId, params Question[] questions) =>
            new QuestionSet(1, setId, $"{setId} のタイトル", questions: questions);

        private static QuestionRepositoryResult Result(params QuestionSet[] sets) =>
            new QuestionRepositoryResult(sets, Array.Empty<QuestionSetLoadError>(), Array.Empty<string>());

        [Test]
        public void FromResult_FlattensSetsInOrder()
        {
            var source = RepositoryQuestionSource.FromResult(
                Result(Set("set-a", FreeText("a1"), FreeText("a2")), Set("set-b", FreeText("b1"))));

            Assert.AreEqual(3, source.Count);
            CollectionAssert.AreEqual(new[] { "a1", "a2", "b1" }, Ids(source));
        }

        [Test]
        public void FromResult_WithTypeFilter_KeepsOnlyMatchingType()
        {
            var source = RepositoryQuestionSource.FromResult(
                Result(Set("set-a", Choice("c1"), FreeText("f1"), Choice("c2"), FreeText("f2"))),
                QuestionType.FreeText);

            CollectionAssert.AreEqual(new[] { "f1", "f2" }, Ids(source));
        }

        [Test]
        public void FromReport_UsesLoadedSetsOnly()
        {
            var report = QuestionLoadReport.FromRepositoryResult(
                DateTimeOffset.Now,
                new QuestionRepositoryResult(
                    new[] { Set("set-a", FreeText("a1"), Choice("c1")) },
                    new[] { new QuestionSetLoadError("broken.json", new[] { "検証に失敗" }) },
                    new[] { "ファイル数が上限を超えました" }));

            var source = RepositoryQuestionSource.FromReport(report, QuestionType.FreeText);

            CollectionAssert.AreEqual(new[] { "a1" }, Ids(source));
        }

        [Test]
        public void TryGetQuestion_OutOfRange_ReturnsFalse()
        {
            var source = RepositoryQuestionSource.FromResult(Result(Set("set-a", FreeText("a1"))));

            Assert.IsFalse(source.TryGetQuestion(-1, out var before));
            Assert.IsNull(before);
            Assert.IsFalse(source.TryGetQuestion(1, out var after));
            Assert.IsNull(after);

            Assert.IsTrue(source.TryGetQuestion(0, out var first));
            Assert.AreEqual("a1", first.Id);
        }

        [Test]
        public void FromResult_WithEmptySets_HasNoQuestions()
        {
            var source = RepositoryQuestionSource.FromResult(Result());

            Assert.AreEqual(0, source.Count);
            Assert.IsFalse(source.TryGetQuestion(0, out _));
        }

        [Test]
        public void Constructor_IgnoresNullEntries()
        {
            var source = new RepositoryQuestionSource(new[] { FreeText("a1"), null, FreeText("a2") });

            CollectionAssert.AreEqual(new[] { "a1", "a2" }, Ids(source));
        }

        [Test]
        public void Factories_RejectNullArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new RepositoryQuestionSource(null));
            Assert.Throws<ArgumentNullException>(() => RepositoryQuestionSource.FromResult(null));
            Assert.Throws<ArgumentNullException>(() => RepositoryQuestionSource.FromReport(null));
            Assert.Throws<ArgumentNullException>(() => RepositoryQuestionSource.FromRepository(null));
        }

        private static List<string> Ids(IQuestionSource source)
        {
            var ids = new List<string>();
            for (var i = 0; i < source.Count; i++)
            {
                Assert.IsTrue(source.TryGetQuestion(i, out var question));
                ids.Add(question.Id);
            }

            return ids;
        }
    }
}
