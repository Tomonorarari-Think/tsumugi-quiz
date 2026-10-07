using System;
using System.Collections.Generic;
using System.Linq;
using TsumugiQuiz.Questions;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.QuestionEditor
{
    /// <summary>
    /// <see cref="QuestionEditorView"/> のうち、編集フォーム（issue #31）の choice 用
    /// 「選択肢（choices）」の可変リスト UI と「正解（correctIndex）」のラジオ選択を扱う部分クラス。
    /// </summary>
    public sealed partial class QuestionEditorView
    {
        private void BuildChoicesSection(VisualElement parent)
        {
            var heading = new Label($"選択肢（{QuestionLimits.MinChoiceCount}〜{QuestionLimits.MaxChoiceCount}件）");
            heading.AddToClassList("body-text");
            parent.Add(heading);

            _formChoicesContainer = new VisualElement { name = "question-form-choices-container" };
            _formChoicesContainer.AddToClassList("question-editor-list");
            parent.Add(_formChoicesContainer);
            RenderChoiceRows();

            var addButton = new Button(OnAddChoiceRowClicked) { text = "選択肢を追加" };
            addButton.SetEnabled(_form.Choices.Count < QuestionLimits.MaxChoiceCount);
            parent.Add(addButton);

            BuildCorrectIndexGroup(parent);
        }

        private void RenderChoiceRows()
        {
            _formChoicesContainer.Clear();

            for (var i = 0; i < _form.Choices.Count; i++)
            {
                _formChoicesContainer.Add(CreateChoiceRow(i));
            }
        }

        private VisualElement CreateChoiceRow(int index)
        {
            var row = new VisualElement();
            row.AddToClassList("question-editor-row");

            var field = new TextField { value = _form.Choices[index] };
            field.AddToClassList("question-editor-row-label");
            field.RegisterValueChangedCallback(evt => _form = _form.WithChoiceAt(index, evt.newValue));
            row.Add(field);

            var removeButton = new Button(() => OnRemoveChoiceRowClicked(index)) { text = "削除" };
            removeButton.SetEnabled(_form.Choices.Count > QuestionLimits.MinChoiceCount);
            row.Add(removeButton);

            return row;
        }

        private void OnAddChoiceRowClicked()
        {
            if (_form.Choices.Count >= QuestionLimits.MaxChoiceCount)
            {
                return;
            }

            _form = _form.WithChoices(_form.Choices.Append(string.Empty).ToArray());
            RenderForm();
        }

        private void OnRemoveChoiceRowClicked(int index)
        {
            if (_form.Choices.Count <= QuestionLimits.MinChoiceCount)
            {
                return;
            }

            var updated = _form.Choices.Where((_, i) => i != index).ToArray();
            var newCorrectIndex = ClampCorrectIndex(_form.CorrectIndex, updated.Length);
            _form = _form.WithChoices(updated).WithCorrectIndex(newCorrectIndex);
            RenderForm();
        }

        /// <summary>
        /// 正解の選択肢を選ぶ UI。<c>choices</c> のテキスト自体は毎キー入力ごとに同期させず、
        /// 「選択肢 N」という位置ラベルで選ばせる（仮決め: 本ファイル。行の追加・削除時にのみ
        /// <see cref="RenderForm"/> 経由で作り直されるため、ラベルと実際の choices テキストの
        /// 追従漏れが起きない）。
        /// </summary>
        private void BuildCorrectIndexGroup(VisualElement parent)
        {
            var labels = Enumerable.Range(1, _form.Choices.Count).Select(n => $"選択肢 {n} を正解にする").ToList();
            var correctIndex = ClampCorrectIndex(_form.CorrectIndex, _form.Choices.Count);
            if (correctIndex != _form.CorrectIndex)
            {
                _form = _form.WithCorrectIndex(correctIndex);
            }

            _formCorrectIndexGroup = new RadioButtonGroup("正解", labels)
            {
                value = correctIndex,
                name = "question-form-correct-index-group",
            };
            _formCorrectIndexGroup.RegisterValueChangedCallback(evt => _form = _form.WithCorrectIndex(evt.newValue));
            parent.Add(_formCorrectIndexGroup);
        }

        private static int ClampCorrectIndex(int correctIndex, int choiceCount)
        {
            if (choiceCount <= 0)
            {
                return 0;
            }

            return Math.Max(0, Math.Min(correctIndex, choiceCount - 1));
        }
    }
}
