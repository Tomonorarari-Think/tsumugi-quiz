using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;
using TsumugiQuiz.UI.Views.QuestionEditor;

namespace TsumugiQuiz.Tests.EditMode.UI.QuestionEditor
{
    /// <summary>
    /// <see cref="QuestionEditorSelectionState"/>（issue #30。#31 が拾う「選択中の問題」の公開元）を検証する。
    /// </summary>
    public class QuestionEditorSelectionStateTests
    {
        private static Question MakeQuestion(string id)
            => new Question(id, QuestionType.FreeText, "問題:" + id, answers: new[] { "回答" });

        private static QuestionSetFileEntry MakeEntry(string filePath, params Question[] questions)
        {
            var set = new QuestionSet(1, "set-id", "セット", string.Empty, questions);
            return new QuestionSetFileEntry(filePath, "set", set, System.Array.Empty<string>(), System.DateTime.MinValue);
        }

        [Test]
        public void Initial_NothingSelected()
        {
            var state = new QuestionEditorSelectionState();

            Assert.IsNull(state.SelectedSet);
            Assert.IsNull(state.SelectedQuestionId);
            Assert.IsNull(state.SelectedQuestion);
        }

        [Test]
        public void SelectSet_ClearsQuestionSelection()
        {
            var state = new QuestionEditorSelectionState();
            var entry = MakeEntry("a.json", MakeQuestion("q1"));
            state.SelectSet(entry);
            state.SelectQuestion("q1");

            state.SelectSet(entry);

            Assert.IsNull(state.SelectedQuestionId);
            Assert.IsNull(state.SelectedQuestion);
        }

        [Test]
        public void SelectQuestion_ResolvesSelectedQuestionFromSet()
        {
            var state = new QuestionEditorSelectionState();
            var entry = MakeEntry("a.json", MakeQuestion("q1"), MakeQuestion("q2"));
            state.SelectSet(entry);

            state.SelectQuestion("q2");

            Assert.AreEqual("q2", state.SelectedQuestion.Id);
        }

        [Test]
        public void Changed_FiresOnEverySelectionChange()
        {
            var state = new QuestionEditorSelectionState();
            var callCount = 0;
            state.Changed += () => callCount++;

            var entry = MakeEntry("a.json", MakeQuestion("q1"));
            state.SelectSet(entry);
            state.SelectQuestion("q1");
            state.Clear();

            Assert.AreEqual(3, callCount);
        }

        [Test]
        public void ReplaceSelectedSet_QuestionStillExists_KeepsQuestionSelection()
        {
            var state = new QuestionEditorSelectionState();
            var entry = MakeEntry("a.json", MakeQuestion("q1"), MakeQuestion("q2"));
            state.SelectSet(entry);
            state.SelectQuestion("q2");

            var updatedEntry = MakeEntry("a.json", MakeQuestion("q1"), MakeQuestion("q2"), MakeQuestion("q3"));
            state.ReplaceSelectedSet(updatedEntry);

            Assert.AreEqual("q2", state.SelectedQuestionId);
            Assert.AreEqual("q2", state.SelectedQuestion.Id);
        }

        [Test]
        public void ReplaceSelectedSet_QuestionNoLongerExists_ClearsQuestionSelection()
        {
            var state = new QuestionEditorSelectionState();
            var entry = MakeEntry("a.json", MakeQuestion("q1"), MakeQuestion("q2"));
            state.SelectSet(entry);
            state.SelectQuestion("q2");

            var updatedEntry = MakeEntry("a.json", MakeQuestion("q1"));
            state.ReplaceSelectedSet(updatedEntry);

            Assert.IsNull(state.SelectedQuestionId);
            Assert.IsNull(state.SelectedQuestion);
        }

        [Test]
        public void ReplaceSelectedSet_Null_ClearsBothSelections()
        {
            var state = new QuestionEditorSelectionState();
            var entry = MakeEntry("a.json", MakeQuestion("q1"));
            state.SelectSet(entry);
            state.SelectQuestion("q1");

            state.ReplaceSelectedSet(null);

            Assert.IsNull(state.SelectedSet);
            Assert.IsNull(state.SelectedQuestionId);
        }
    }
}
