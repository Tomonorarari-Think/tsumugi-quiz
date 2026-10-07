using System;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="QuestionListEditor"/>（issue #30「並び・削除可否」）の純ロジックを検証する。
    /// </summary>
    public class QuestionListEditorTests
    {
        private static Question MakeQuestion(string id)
            => new Question(id, QuestionType.FreeText, "問題:" + id, answers: new[] { "回答" });

        [Test]
        public void CanRemoveQuestion_MoreThanOne_ReturnsTrue()
        {
            Assert.IsTrue(QuestionListEditor.CanRemoveQuestion(2));
        }

        [Test]
        public void CanRemoveQuestion_ExactlyOne_ReturnsFalse()
        {
            // docs/question-data.md §2: questions は1件以上必要なため、最後の1件は削除不可。
            Assert.IsFalse(QuestionListEditor.CanRemoveQuestion(1));
        }

        [Test]
        public void CreateEmptyQuestion_ReturnsValidFreeTextQuestion()
        {
            var question = QuestionListEditor.CreateEmptyQuestion(Array.Empty<Question>());

            Assert.AreEqual("question-1", question.Id);
            Assert.AreEqual(QuestionType.FreeText, question.Type);
            Assert.IsFalse(string.IsNullOrEmpty(question.Text));
            Assert.AreEqual(1, question.Answers.Count);

            // 生成直後の問題がバリデーションを通過すること（保存直後に読み込み直せることの確認）。
            var set = new QuestionSet(1, "set-1", "セット", questions: new[] { question });
            var errors = new QuestionSetValidator().Validate(set, setBaseDirectory: null);
            CollectionAssert.IsEmpty(errors);
        }

        [Test]
        public void CreateEmptyQuestion_AssignsIdNotCollidingWithExisting()
        {
            var existing = new[] { MakeQuestion("question-1"), MakeQuestion("question-2") };
            var created = QuestionListEditor.CreateEmptyQuestion(existing);

            Assert.AreEqual("question-3", created.Id);
        }

        [Test]
        public void AddQuestion_AppendsToEnd_DoesNotMutateOriginalList()
        {
            var original = new[] { MakeQuestion("q1") };
            var added = MakeQuestion("q2");

            var updated = QuestionListEditor.AddQuestion(original, added);

            Assert.AreEqual(1, original.Length, "元のリストは変更されないこと（不変データ）");
            Assert.AreEqual(2, updated.Count);
            Assert.AreEqual("q2", updated[1].Id);
        }

        [Test]
        public void RemoveQuestion_RemovesMatchingId_KeepsOrderOfRemaining()
        {
            var original = new[] { MakeQuestion("q1"), MakeQuestion("q2"), MakeQuestion("q3") };

            var updated = QuestionListEditor.RemoveQuestion(original, "q2");

            CollectionAssert.AreEqual(new[] { "q1", "q3" }, updated.Select(q => q.Id).ToArray());
        }

        [Test]
        public void Move_MovesQuestionForwardInList()
        {
            var original = new[] { MakeQuestion("q1"), MakeQuestion("q2"), MakeQuestion("q3") };

            var updated = QuestionListEditor.Move(original, fromIndex: 0, toIndex: 2);

            CollectionAssert.AreEqual(new[] { "q2", "q3", "q1" }, updated.Select(q => q.Id).ToArray());
        }

        [Test]
        public void Move_MovesQuestionBackwardInList()
        {
            var original = new[] { MakeQuestion("q1"), MakeQuestion("q2"), MakeQuestion("q3") };

            var updated = QuestionListEditor.Move(original, fromIndex: 2, toIndex: 0);

            CollectionAssert.AreEqual(new[] { "q3", "q1", "q2" }, updated.Select(q => q.Id).ToArray());
        }

        [Test]
        public void Move_SameIndex_ReturnsEquivalentOrder()
        {
            var original = new[] { MakeQuestion("q1"), MakeQuestion("q2") };

            var updated = QuestionListEditor.Move(original, fromIndex: 1, toIndex: 1);

            CollectionAssert.AreEqual(new[] { "q1", "q2" }, updated.Select(q => q.Id).ToArray());
        }

        [Test]
        public void Move_OutOfRangeIndex_Throws()
        {
            var original = new[] { MakeQuestion("q1") };

            Assert.Throws<ArgumentOutOfRangeException>(() => QuestionListEditor.Move(original, fromIndex: 0, toIndex: 5));
        }
    }
}
