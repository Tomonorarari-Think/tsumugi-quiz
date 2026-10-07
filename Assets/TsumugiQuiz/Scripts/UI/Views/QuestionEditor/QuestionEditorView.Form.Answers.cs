using System.Linq;
using TsumugiQuiz.Questions;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.QuestionEditor
{
    /// <summary>
    /// <see cref="QuestionEditorView"/> のうち、編集フォーム（issue #31）の freeText 用
    /// 「正解候補（answers）」の可変リスト UI を扱う部分クラス。
    /// </summary>
    public sealed partial class QuestionEditorView
    {
        private void BuildAnswersSection(VisualElement parent)
        {
            var heading = new Label($"正解候補（いずれかに一致すれば正解、最大{QuestionLimits.MaxAnswerCount}件）");
            heading.AddToClassList("body-text");
            parent.Add(heading);

            _formAnswersContainer = new VisualElement { name = "question-form-answers-container" };
            _formAnswersContainer.AddToClassList("question-editor-list");
            parent.Add(_formAnswersContainer);
            RenderAnswerRows();

            var addButton = new Button(OnAddAnswerRowClicked)
                { text = "正解候補を追加", name = "question-form-add-answer-button" };
            addButton.SetEnabled(_form.Answers.Count < QuestionLimits.MaxAnswerCount);
            parent.Add(addButton);
        }

        private void RenderAnswerRows()
        {
            _formAnswersContainer.Clear();

            for (var i = 0; i < _form.Answers.Count; i++)
            {
                _formAnswersContainer.Add(CreateAnswerRow(i));
            }
        }

        private VisualElement CreateAnswerRow(int index)
        {
            var row = new VisualElement();
            row.AddToClassList("question-editor-row");

            var field = new TextField { value = _form.Answers[index], name = $"question-form-answer-field-{index}" };
            field.AddToClassList("question-editor-row-label");
            field.RegisterValueChangedCallback(evt => _form = _form.WithAnswerAt(index, evt.newValue));
            row.Add(field);

            var removeButton = new Button(() => OnRemoveAnswerRowClicked(index)) { text = "削除" };
            removeButton.SetEnabled(_form.Answers.Count > 1);
            row.Add(removeButton);

            return row;
        }

        private void OnAddAnswerRowClicked()
        {
            if (_form.Answers.Count >= QuestionLimits.MaxAnswerCount)
            {
                return;
            }

            _form = _form.WithAnswers(_form.Answers.Append(string.Empty).ToArray());
            RenderForm();
        }

        private void OnRemoveAnswerRowClicked(int index)
        {
            if (_form.Answers.Count <= 1)
            {
                return;
            }

            var updated = _form.Answers.Where((_, i) => i != index).ToArray();
            _form = _form.WithAnswers(updated);
            RenderForm();
        }
    }
}
