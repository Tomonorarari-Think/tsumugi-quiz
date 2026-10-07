using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="QuestionFormConverter"/>（issue #31。フォーム値 ⇔ <see cref="Question"/> の変換）を検証する。
    /// </summary>
    public class QuestionFormConverterTests
    {
        [Test]
        public void FromQuestion_FreeText_CopiesAllFields()
        {
            var question = new Question(
                "q1", QuestionType.FreeText, "問題文", "読み上げ", answers: new[] { "回答1", "回答2" },
                imagePath: "images/a.png", tags: new[] { "タグ" }, difficulty: 4);

            var form = QuestionFormConverter.FromQuestion(question);

            Assert.AreEqual(QuestionType.FreeText, form.Type);
            Assert.AreEqual("問題文", form.Text);
            Assert.AreEqual("読み上げ", form.ReadingText);
            CollectionAssert.AreEqual(new[] { "回答1", "回答2" }, form.Answers);
            Assert.AreEqual("images/a.png", form.ImagePath);
            CollectionAssert.AreEqual(new[] { "タグ" }, form.Tags);
            Assert.AreEqual(4, form.Difficulty);
        }

        [Test]
        public void FromQuestion_TypeMissing_FallsBackToFreeText()
        {
            var question = new Question("q1", type: null, text: "問題文", answers: new[] { "回答" });

            var form = QuestionFormConverter.FromQuestion(question);

            Assert.AreEqual(QuestionType.FreeText, form.Type);
        }

        [Test]
        public void ToQuestion_FreeText_OmitsChoicesAndCorrectIndex()
        {
            var form = new QuestionFormData(
                QuestionType.FreeText, "問題文", "読み", new[] { "回答" }, new[] { "使わない選択肢" }, 3, "images/a.png",
                new[] { "タグ" }, 2);

            var question = QuestionFormConverter.ToQuestion("q1", form);

            Assert.AreEqual("q1", question.Id);
            Assert.AreEqual(QuestionType.FreeText, question.Type);
            CollectionAssert.AreEqual(new[] { "回答" }, question.Answers);
            CollectionAssert.IsEmpty(question.Choices);
            Assert.IsNull(question.CorrectIndex);
        }

        [Test]
        public void ToQuestion_Choice_OmitsAnswers()
        {
            var form = new QuestionFormData(
                QuestionType.Choice, "問題文", null, new[] { "使わない回答" }, new[] { "A", "B" }, 1, null,
                System.Array.Empty<string>(), 3);

            var question = QuestionFormConverter.ToQuestion("q1", form);

            Assert.AreEqual(QuestionType.Choice, question.Type);
            CollectionAssert.AreEqual(new[] { "A", "B" }, question.Choices);
            Assert.AreEqual(1, question.CorrectIndex);
            CollectionAssert.IsEmpty(question.Answers);
        }

        [Test]
        public void ToQuestion_TrimsTextAndReadingText()
        {
            var form = new QuestionFormData(
                QuestionType.FreeText, "  問題文  ", "  読み  ", new[] { "回答" }, System.Array.Empty<string>(), 0,
                null, System.Array.Empty<string>(), 3);

            var question = QuestionFormConverter.ToQuestion("q1", form);

            Assert.AreEqual("問題文", question.Text);
            Assert.AreEqual("読み", question.ReadingText);
        }

        [Test]
        public void ToQuestion_TrimsAnswersAndChoices()
        {
            var freeTextForm = new QuestionFormData(
                QuestionType.FreeText, "問題文", null, new[] { "  回答1  ", "\t回答2 ", "   " },
                System.Array.Empty<string>(), 0, null, System.Array.Empty<string>(), 3);

            var freeTextQuestion = QuestionFormConverter.ToQuestion("q1", freeTextForm);

            // 空白のみの行は空文字列になり、保存前バリデーションの
            // 「answers に空文字列を含めることはできません。」で弾かれる（PR #93 レビュー M1）。
            CollectionAssert.AreEqual(new[] { "回答1", "回答2", string.Empty }, freeTextQuestion.Answers);

            var choiceForm = new QuestionFormData(
                QuestionType.Choice, "問題文", null, System.Array.Empty<string>(),
                new[] { " A ", "B\t", "  " }, 0, null, System.Array.Empty<string>(), 3);

            var choiceQuestion = QuestionFormConverter.ToQuestion("q1", choiceForm);

            CollectionAssert.AreEqual(new[] { "A", "B", string.Empty }, choiceQuestion.Choices);
        }

        [Test]
        public void ToQuestion_WhitespaceOnlyReadingText_BecomesNull()
        {
            var form = new QuestionFormData(
                QuestionType.FreeText, "問題文", "   ", new[] { "回答" }, System.Array.Empty<string>(), 0,
                null, System.Array.Empty<string>(), 3);

            var question = QuestionFormConverter.ToQuestion("q1", form);

            // null なら JSON へ readingText を書き出さない（PR #93 レビュー L8）。
            Assert.IsNull(question.ReadingText);
        }

        [Test]
        public void ToQuestion_EmptyImagePath_BecomesNull()
        {
            var form = new QuestionFormData(
                QuestionType.FreeText, "問題文", null, new[] { "回答" }, System.Array.Empty<string>(), 0,
                string.Empty, System.Array.Empty<string>(), 3);

            var question = QuestionFormConverter.ToQuestion("q1", form);

            Assert.IsNull(question.ImagePath);
        }

        [TestCase(null, ExpectedResult = new string[0])]
        [TestCase("", ExpectedResult = new string[0])]
        [TestCase("タグ1", ExpectedResult = new[] { "タグ1" })]
        [TestCase("タグ1,タグ2", ExpectedResult = new[] { "タグ1", "タグ2" })]
        [TestCase(" タグ1 , , タグ2 ", ExpectedResult = new[] { "タグ1", "タグ2" })]
        public string[] ParseTags_VariousInputs(string rawText)
            => (string[])QuestionFormConverter.ParseTags(rawText);

        [Test]
        public void FormatTags_JoinsWithCommaAndSpace()
        {
            Assert.AreEqual("タグ1, タグ2", QuestionFormConverter.FormatTags(new[] { "タグ1", "タグ2" }));
        }

        [Test]
        public void FormatTags_Empty_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, QuestionFormConverter.FormatTags(System.Array.Empty<string>()));
        }

        /// <summary>
        /// PR #103 レビュー M2: 「未保存の変更ありか」の判定（<c>QuestionEditorView.FormHasUnsavedChanges</c>）は
        /// 素の <see cref="QuestionFormData"/> 同士を比較せず、<see cref="QuestionFormConverter.ToQuestion"/> で
        /// 正規化した結果同士を比較する。ここでは、その正規化アルゴリズムそのものを検証する
        /// （readingText の null vs 空文字列・前後の空白だけの違いは「変更なし」と判定されること）。
        /// </summary>
        [Test]
        public void ToQuestionThenFromQuestion_TrailingWhitespaceAndNullVsEmptyReadingText_NormalizesToSameValues()
        {
            var original = new Question("q1", QuestionType.FreeText, "問題文", readingText: null, answers: new[] { "回答" });
            var baseline = QuestionFormConverter.FromQuestion(original);

            // 見た目上は「問題文」の末尾に空白を1つ追加し、readingText を空白のみにしただけ
            // （保存前バリデーションで弾かれる入力ではなく、トリム後は元の値と同じになる入力）。
            var edited = baseline.WithText("問題文 ").WithReadingText("   ");

            var candidate = QuestionFormConverter.ToQuestion(original.Id, edited);
            var normalizedEdited = QuestionFormConverter.FromQuestion(candidate);

            Assert.IsTrue(
                normalizedEdited.HasSameValuesAs(baseline),
                "末尾空白・null相当の readingText だけの違いは、正規化後は差分なしと判定されること。");
        }

        [Test]
        public void ToQuestionThenFromQuestion_ActualTextChange_NormalizesToDifferentValues()
        {
            var original = new Question("q1", QuestionType.FreeText, "問題文", answers: new[] { "回答" });
            var baseline = QuestionFormConverter.FromQuestion(original);

            var edited = baseline.WithText("変更後の問題文");
            var candidate = QuestionFormConverter.ToQuestion(original.Id, edited);
            var normalizedEdited = QuestionFormConverter.FromQuestion(candidate);

            Assert.IsFalse(
                normalizedEdited.HasSameValuesAs(baseline),
                "実際にテキストを変更した場合は、正規化後も差分ありと判定されること。");
        }
    }
}
