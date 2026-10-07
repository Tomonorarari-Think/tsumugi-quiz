using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="QuestionFormData"/>（issue #31。編集フォームの不変スナップショット）を検証する。
    /// <c>With*</c> は元のインスタンスを変更せず、変更後の新しいインスタンスを返すこと（CLAUDE.md）を確認する。
    /// </summary>
    public class QuestionFormDataTests
    {
        private static QuestionFormData MakeChoiceForm()
            => new QuestionFormData(
                QuestionType.Choice, "問題文", "読み", new[] { "回答" }, new[] { "A", "B" }, 0, "images/a.png",
                new[] { "タグ1" }, 3);

        [Test]
        public void WithText_ReturnsNewInstance_DoesNotMutateOriginal()
        {
            var original = MakeChoiceForm();

            var updated = original.WithText("新しい問題文");

            Assert.AreEqual("問題文", original.Text, "元のインスタンスは変更されないこと。");
            Assert.AreEqual("新しい問題文", updated.Text);
            Assert.AreNotSame(original, updated);
        }

        [Test]
        public void WithChoiceAt_ReplacesOnlyTargetIndex()
        {
            var original = MakeChoiceForm();

            var updated = original.WithChoiceAt(1, "B改");

            CollectionAssert.AreEqual(new[] { "A", "B" }, original.Choices, "元のインスタンスは変更されないこと。");
            CollectionAssert.AreEqual(new[] { "A", "B改" }, updated.Choices);
        }

        [Test]
        public void WithAnswerAt_ReplacesOnlyTargetIndex()
        {
            var original = new QuestionFormData(
                QuestionType.FreeText, "問題文", null, new[] { "回答1", "回答2" }, System.Array.Empty<string>(), 0,
                null, System.Array.Empty<string>(), 3);

            var updated = original.WithAnswerAt(0, "回答1改");

            CollectionAssert.AreEqual(new[] { "回答1", "回答2" }, original.Answers);
            CollectionAssert.AreEqual(new[] { "回答1改", "回答2" }, updated.Answers);
        }

        [Test]
        public void NullListArguments_FallBackToEmptyArrays()
        {
            var form = new QuestionFormData(QuestionType.FreeText, null, null, null, null, 0, null, null, 3);

            Assert.AreEqual(string.Empty, form.Text);
            Assert.AreEqual(string.Empty, form.ReadingText);
            Assert.AreEqual(string.Empty, form.ImagePath);
            CollectionAssert.IsEmpty(form.Answers);
            CollectionAssert.IsEmpty(form.Choices);
            CollectionAssert.IsEmpty(form.Tags);
        }

        [Test]
        public void WithAnswerAt_IndexOutOfRange_Throws()
        {
            // L4（PR #93 レビュー持ち越し、issue #32）: 境界での入力検証。
            var form = new QuestionFormData(
                QuestionType.FreeText, "問題文", null, new[] { "回答1" }, System.Array.Empty<string>(), 0,
                null, System.Array.Empty<string>(), 3);

            Assert.Throws<System.ArgumentOutOfRangeException>(() => form.WithAnswerAt(1, "はみ出し"));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => form.WithAnswerAt(-1, "はみ出し"));
        }

        [Test]
        public void WithChoiceAt_IndexOutOfRange_Throws()
        {
            var form = MakeChoiceForm();

            Assert.Throws<System.ArgumentOutOfRangeException>(() => form.WithChoiceAt(2, "はみ出し"));
        }

        [Test]
        public void HasSameValuesAs_SameValues_ReturnsTrue()
        {
            var a = MakeChoiceForm();
            var b = MakeChoiceForm();

            Assert.IsTrue(a.HasSameValuesAs(b));
            Assert.IsTrue(b.HasSameValuesAs(a));
        }

        [Test]
        public void HasSameValuesAs_DifferentText_ReturnsFalse()
        {
            var original = MakeChoiceForm();
            var changed = original.WithText("変更後の問題文");

            Assert.IsFalse(original.HasSameValuesAs(changed));
        }

        [Test]
        public void HasSameValuesAs_DifferentAnswersOrder_ReturnsFalse()
        {
            var original = new QuestionFormData(
                QuestionType.FreeText, "問題文", null, new[] { "回答1", "回答2" }, System.Array.Empty<string>(), 0,
                null, System.Array.Empty<string>(), 3);
            var reordered = original.WithAnswers(new[] { "回答2", "回答1" });

            Assert.IsFalse(original.HasSameValuesAs(reordered));
        }

        [Test]
        public void HasSameValuesAs_Null_ReturnsFalse()
        {
            Assert.IsFalse(MakeChoiceForm().HasSameValuesAs(null));
        }
    }
}
