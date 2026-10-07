using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="QuestionFormValidation"/>（issue #31。編集フォームの保存前バリデーション）を検証する。
    /// </summary>
    public class QuestionFormValidationTests
    {
        private static QuestionSet MakeSet(params Question[] questions)
            => new QuestionSet(1, "set-id", "セット", string.Empty, questions);

        [Test]
        public void Validate_ValidCandidate_ReturnsNoErrors()
        {
            var existing = new Question("q1", QuestionType.FreeText, "既存の問題", answers: new[] { "回答" });
            var set = MakeSet(existing);
            var candidate = new Question("q1", QuestionType.FreeText, "更新後の問題", answers: new[] { "回答" });

            var errors = QuestionFormValidation.Validate(set, candidate, setBaseDirectory: null);

            CollectionAssert.IsEmpty(errors);
        }

        [Test]
        public void Validate_ChoiceWithOneChoice_ReturnsChoiceCountError()
        {
            var existing = new Question("q1", QuestionType.FreeText, "既存の問題", answers: new[] { "回答" });
            var set = MakeSet(existing);
            var candidate = new Question("q1", QuestionType.Choice, "問題", choices: new[] { "A" }, correctIndex: 0);

            var errors = QuestionFormValidation.Validate(set, candidate, setBaseDirectory: null);

            Assert.IsTrue(errors.Any(e => e.Message.Contains("choices")));
        }

        [Test]
        public void Validate_CorrectIndexOutOfRange_ReturnsCorrectIndexError()
        {
            var existing = new Question("q1", QuestionType.FreeText, "既存の問題", answers: new[] { "回答" });
            var set = MakeSet(existing);
            var candidate = new Question(
                "q1", QuestionType.Choice, "問題", choices: new[] { "A", "B" }, correctIndex: 5);

            var errors = QuestionFormValidation.Validate(set, candidate, setBaseDirectory: null);

            Assert.IsTrue(errors.Any(e => e.Message.Contains("correctIndex")));
        }

        [Test]
        public void Validate_OnlyReturnsErrorsForCandidateQuestion_NotSiblingQuestions()
        {
            // 兄弟の問題（q2）が既に無効（answers 無し）でも、q1 を検証した結果には含めない。
            var sibling = new Question("q2", QuestionType.FreeText, "壊れた問題", answers: null);
            var candidate = new Question("q1", QuestionType.FreeText, "問題", answers: new[] { "回答" });
            var set = new QuestionSet(1, "set-id", "セット", string.Empty, new[] { candidate, sibling });

            var errors = QuestionFormValidation.Validate(set, candidate, setBaseDirectory: null);

            CollectionAssert.IsEmpty(errors);
        }

        [Test]
        public void Validate_NullSet_Throws()
        {
            var candidate = new Question("q1", QuestionType.FreeText, "問題", answers: new[] { "回答" });
            Assert.Throws<System.ArgumentNullException>(
                () => QuestionFormValidation.Validate(null, candidate, null));
        }

        [Test]
        public void Validate_NullCandidate_Throws()
        {
            var set = MakeSet(new Question("q1", QuestionType.FreeText, "問題", answers: new[] { "回答" }));
            Assert.Throws<System.ArgumentNullException>(
                () => QuestionFormValidation.Validate(set, null, null));
        }

        [TestCase("text が指定されていません。", ExpectedResult = QuestionFormField.Text)]
        [TestCase("readingText が 500 文字を超えています（実際: 600 文字）。", ExpectedResult = QuestionFormField.ReadingText)]
        [TestCase("freeText には answers が1件以上必要です。", ExpectedResult = QuestionFormField.Answers)]
        [TestCase("choices は2〜8件である必要があります（実際: 1件）。", ExpectedResult = QuestionFormField.Choices)]
        [TestCase("correctIndex が範囲外です（実際: 5、choices件数: 2）。", ExpectedResult = QuestionFormField.CorrectIndex)]
        [TestCase("imagePath の画像ファイルが見つかりません: images/x.png", ExpectedResult = QuestionFormField.ImagePath)]
        [TestCase("tags は20件以内である必要があります（実際: 21件）。", ExpectedResult = QuestionFormField.Tags)]
        [TestCase("difficulty は1〜5の範囲である必要があります（実際: 9）。", ExpectedResult = QuestionFormField.Difficulty)]
        [TestCase("type が指定されていません。", ExpectedResult = QuestionFormField.Type)]
        [TestCase("type が不正です（実際: 3）。", ExpectedResult = QuestionFormField.Type)]
        [TestCase("id が指定されていません。", ExpectedResult = QuestionFormField.Other)]
        [TestCase(null, ExpectedResult = QuestionFormField.Other)]
        [TestCase("", ExpectedResult = QuestionFormField.Other)]
        public QuestionFormField MapField_VariousMessages(string message)
            => QuestionFormValidation.MapField(message);
    }
}
