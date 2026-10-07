using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;
using TsumugiQuiz.UI.Views.QuestionEditor;

namespace TsumugiQuiz.Tests.EditMode.UI.QuestionEditor
{
    /// <summary>
    /// <see cref="QuestionEditorPresenter"/>（issue #30。一覧行・確認ダイアログ文言の組み立て）を検証する。
    /// </summary>
    public class QuestionEditorPresenterTests
    {
        [Test]
        public void SetSummary_ValidEntry_ShowsTitleQuestionCountAndFileName()
        {
            var question = new Question("q1", QuestionType.FreeText, "問題", answers: new[] { "回答" });
            var set = new QuestionSet(1, "set-id", "サンプルセット", string.Empty, new[] { question });
            var entry = new QuestionSetFileEntry("dummy/sample.json", "sample", set, System.Array.Empty<string>(), System.DateTime.MinValue);

            var summary = QuestionEditorPresenter.SetSummary(entry);

            StringAssert.Contains("サンプルセット", summary);
            StringAssert.Contains("1問", summary);
            StringAssert.Contains("sample.json", summary);
        }

        [Test]
        public void SetSummary_InvalidEntry_ShowsFileNameAndErrorReason()
        {
            var entry = new QuestionSetFileEntry("dummy/broken.json", "broken", null, new[] { "JSON の解析に失敗しました: 予期しない文字" }, System.DateTime.MinValue);

            var summary = QuestionEditorPresenter.SetSummary(entry);

            StringAssert.Contains("broken.json", summary);
            StringAssert.Contains("読み込みエラー", summary);
        }

        [Test]
        public void QuestionSummary_FreeText_ShowsIndexTypeIdAndText()
        {
            var question = new Question("q1", QuestionType.FreeText, "日本の首都はどこ？", answers: new[] { "東京" });

            var summary = QuestionEditorPresenter.QuestionSummary(question, displayIndex: 0);

            StringAssert.Contains("1.", summary);
            StringAssert.Contains("自由入力", summary);
            StringAssert.Contains("q1", summary);
            StringAssert.Contains("日本の首都はどこ？", summary);
        }

        [Test]
        public void QuestionSummary_Choice_ShowsChoiceLabel()
        {
            var question = new Question("q2", QuestionType.Choice, "選択式の問題", choices: new[] { "A", "B" }, correctIndex: 0);

            var summary = QuestionEditorPresenter.QuestionSummary(question, displayIndex: 1);

            StringAssert.Contains("2.", summary);
            StringAssert.Contains("選択式", summary);
        }

        [Test]
        public void QuestionSummary_LongText_IsTruncatedWithEllipsis()
        {
            var longText = new string('あ', 100);
            var question = new Question("q3", QuestionType.FreeText, longText, answers: new[] { "回答" });

            var summary = QuestionEditorPresenter.QuestionSummary(question, displayIndex: 0);

            StringAssert.Contains("…", summary);
            Assert.Less(summary.Length, longText.Length);
        }

        [Test]
        public void DeleteSetConfirmMessage_ValidEntry_UsesTitle()
        {
            var question = new Question("q1", QuestionType.FreeText, "問題", answers: new[] { "回答" });
            var set = new QuestionSet(1, "set-id", "削除対象セット", string.Empty, new[] { question });
            var entry = new QuestionSetFileEntry("dummy/set.json", "set", set, System.Array.Empty<string>(), System.DateTime.MinValue);

            var message = QuestionEditorPresenter.DeleteSetConfirmMessage(entry);

            StringAssert.Contains("削除対象セット", message);
            StringAssert.Contains("削除しますか", message);
        }

        [Test]
        public void DeleteQuestionConfirmMessage_UsesQuestionId()
        {
            var question = new Question("q1", QuestionType.FreeText, "問題", answers: new[] { "回答" });

            var message = QuestionEditorPresenter.DeleteQuestionConfirmMessage(question);

            StringAssert.Contains("q1", message);
            StringAssert.Contains("削除しますか", message);
        }

        [Test]
        public void SetSummary_NullEntry_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() => QuestionEditorPresenter.SetSummary(null));
        }

        [Test]
        public void QuestionSummary_NullQuestion_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() => QuestionEditorPresenter.QuestionSummary(null, 0));
        }

        [Test]
        public void DeleteSetConfirmMessage_NullEntry_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() => QuestionEditorPresenter.DeleteSetConfirmMessage(null));
        }

        [Test]
        public void DeleteQuestionConfirmMessage_NullQuestion_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() => QuestionEditorPresenter.DeleteQuestionConfirmMessage(null));
        }
    }
}
