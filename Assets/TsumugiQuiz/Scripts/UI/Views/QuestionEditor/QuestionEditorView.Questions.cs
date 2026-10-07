using System.Collections.Generic;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;
using TsumugiQuiz.UI.TextLayout;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.QuestionEditor
{
    /// <summary>
    /// <see cref="QuestionEditorView"/> のうち、選択中セット内の「問題一覧」
    /// （追加・削除・並び替え・選択）を扱う部分クラス。
    /// </summary>
    public sealed partial class QuestionEditorView
    {
        private VisualElement _questionListContainer;
        private Label _questionStatusLabel;
        private Button _addQuestionButton;
        private Button _deleteQuestionButton;
        private Button _moveQuestionUpButton;
        private Button _moveQuestionDownButton;
        private VisualElement _deleteQuestionConfirmContainer;
        private Label _deleteQuestionConfirmLabel;
        private Button _confirmDeleteQuestionButton;
        private Button _cancelDeleteQuestionButton;
        private VisualElement _questionEditFormContainer;

        private bool QueryQuestionElements(VisualElement root)
        {
            _questionListContainer = root.Q<VisualElement>("question-list-container");
            _questionStatusLabel = root.Q<Label>("question-status-label");
            _addQuestionButton = root.Q<Button>("add-question-button");
            _deleteQuestionButton = root.Q<Button>("delete-question-button");
            _moveQuestionUpButton = root.Q<Button>("move-question-up-button");
            _moveQuestionDownButton = root.Q<Button>("move-question-down-button");
            _deleteQuestionConfirmContainer = root.Q<VisualElement>("delete-question-confirm-container");
            // #206: 問題の id（問題データ）を含むため平文にする。
            _deleteQuestionConfirmLabel = PlainText.Apply(root.Q<Label>("delete-question-confirm-label"));
            _confirmDeleteQuestionButton = root.Q<Button>("confirm-delete-question-button");
            _cancelDeleteQuestionButton = root.Q<Button>("cancel-delete-question-button");
            // #31 が編集フォームを差し込むためのコンテナ。本 issue では空のまま扱う。
            _questionEditFormContainer = root.Q<VisualElement>("question-edit-form-container");

            return _questionListContainer != null && _questionStatusLabel != null && _addQuestionButton != null
                && _deleteQuestionButton != null && _moveQuestionUpButton != null && _moveQuestionDownButton != null
                && _deleteQuestionConfirmContainer != null && _deleteQuestionConfirmLabel != null
                && _confirmDeleteQuestionButton != null && _cancelDeleteQuestionButton != null
                && _questionEditFormContainer != null;
        }

        private void WireQuestionHandlers()
        {
            _addQuestionButton.clicked += OnAddQuestionClicked;
            _deleteQuestionButton.clicked += OnDeleteQuestionClicked;
            _moveQuestionUpButton.clicked += OnMoveQuestionUpClicked;
            _moveQuestionDownButton.clicked += OnMoveQuestionDownClicked;
            _confirmDeleteQuestionButton.clicked += OnConfirmDeleteQuestionClicked;
            _cancelDeleteQuestionButton.clicked += OnCancelDeleteQuestionClicked;
        }

        private void UnwireQuestionHandlers()
        {
            if (_addQuestionButton != null) _addQuestionButton.clicked -= OnAddQuestionClicked;
            if (_deleteQuestionButton != null) _deleteQuestionButton.clicked -= OnDeleteQuestionClicked;
            if (_moveQuestionUpButton != null) _moveQuestionUpButton.clicked -= OnMoveQuestionUpClicked;
            if (_moveQuestionDownButton != null) _moveQuestionDownButton.clicked -= OnMoveQuestionDownClicked;
            if (_confirmDeleteQuestionButton != null) _confirmDeleteQuestionButton.clicked -= OnConfirmDeleteQuestionClicked;
            if (_cancelDeleteQuestionButton != null) _cancelDeleteQuestionButton.clicked -= OnCancelDeleteQuestionClicked;
        }

        private void ClearQuestionElementReferences()
        {
            _questionListContainer = null;
            _questionStatusLabel = null;
            _addQuestionButton = null;
            _deleteQuestionButton = null;
            _moveQuestionUpButton = null;
            _moveQuestionDownButton = null;
            _deleteQuestionConfirmContainer = null;
            _deleteQuestionConfirmLabel = null;
            _confirmDeleteQuestionButton = null;
            _cancelDeleteQuestionButton = null;
            _questionEditFormContainer = null;
        }

        private void RenderQuestionList()
        {
            _questionListContainer.Clear();

            var set = _selection.SelectedSet?.Set;
            if (set == null)
            {
                return;
            }

            for (var i = 0; i < set.Questions.Count; i++)
            {
                _questionListContainer.Add(CreateQuestionRow(set.Questions[i], i));
            }
        }

        private VisualElement CreateQuestionRow(Question question, int displayIndex)
        {
            var row = new VisualElement();
            row.AddToClassList("question-editor-row");

            if (_selection.SelectedQuestionId == question.Id)
            {
                row.AddToClassList("question-editor-row--selected");
            }

            // #206: 問題文の要約（問題データ）は、ゲーム画面と同じく平文として表示する。
            var label = PlainText.CreateLabel(QuestionEditorPresenter.QuestionSummary(question, displayIndex));
            label.AddToClassList("question-editor-row-label");
            row.Add(label);

            row.RegisterCallback<ClickEvent>(_ => OnQuestionRowClicked(question.Id));

            return row;
        }

        private void OnQuestionRowClicked(string questionId)
        {
            // PR #103 レビュー H1: ShowQuestionStatus(string.Empty) は SelectQuestion より前に呼ぶこと。
            // SelectQuestion は _selection.Changed を同期的に発火し、OnSelectionChangedForForm が
            // 「未保存の変更を破棄しました。」を表示しうる（M4）。後から呼ぶと、この典型的な
            // 「別の問題行をクリック」経路でその文言が即座に消えてしまう（OnReloadSetsClicked / OnSetRowClicked
            // と同じく、クリア→操作の順に揃える）。
            ShowQuestionStatus(string.Empty);
            _selection.SelectQuestion(questionId);
            SetDeleteQuestionConfirmVisible(false);
            RenderQuestionList();
            UpdateQuestionButtonStates();
        }

        private void OnAddQuestionClicked()
        {
            var entry = _selection.SelectedSet;
            if (entry?.Set == null)
            {
                ShowQuestionStatus("先に問題セットを選択してください。");
                return;
            }

            var result = _service.AddEmptyQuestion(entry);
            if (!result.Success)
            {
                ShowQuestionStatus(result.ErrorMessage);
                return;
            }

            ShowQuestionStatus(string.Empty);
            ReplaceEntryEverywhere(result.Entry);

            var addedQuestion = result.Entry.Set.Questions[result.Entry.Set.Questions.Count - 1];
            _selection.SelectQuestion(addedQuestion.Id);
            RenderQuestionList();
            UpdateQuestionButtonStates();
        }

        private void OnDeleteQuestionClicked()
        {
            var question = _selection.SelectedQuestion;
            if (question == null)
            {
                ShowQuestionStatus("削除する問題を選択してください。");
                return;
            }

            _deleteQuestionConfirmLabel.text = QuestionEditorPresenter.DeleteQuestionConfirmMessage(question);
            SetDeleteQuestionConfirmVisible(true);
        }

        private void OnConfirmDeleteQuestionClicked()
        {
            SetDeleteQuestionConfirmVisible(false);

            var entry = _selection.SelectedSet;
            var questionId = _selection.SelectedQuestionId;
            if (entry?.Set == null || questionId == null)
            {
                return;
            }

            var result = _service.RemoveQuestion(entry, questionId);
            if (!result.Success)
            {
                ShowQuestionStatus(result.ErrorMessage);
                return;
            }

            ShowQuestionStatus(string.Empty);
            ReplaceEntryEverywhere(result.Entry);
        }

        private void OnCancelDeleteQuestionClicked() => SetDeleteQuestionConfirmVisible(false);

        private void OnMoveQuestionUpClicked() => MoveSelectedQuestion(-1);

        private void OnMoveQuestionDownClicked() => MoveSelectedQuestion(1);

        private void MoveSelectedQuestion(int delta)
        {
            var entry = _selection.SelectedSet;
            var questionId = _selection.SelectedQuestionId;
            if (entry?.Set == null || questionId == null)
            {
                return;
            }

            var questions = entry.Set.Questions;
            var index = IndexOfQuestion(questions, questionId);
            if (index < 0)
            {
                return;
            }

            var targetIndex = index + delta;
            if (targetIndex < 0 || targetIndex >= questions.Count)
            {
                return;
            }

            var result = _service.MoveQuestion(entry, index, targetIndex);
            if (!result.Success)
            {
                ShowQuestionStatus(result.ErrorMessage);
                return;
            }

            ShowQuestionStatus(string.Empty);
            ReplaceEntryEverywhere(result.Entry);
        }

        private void UpdateQuestionButtonStates()
        {
            var entry = _selection.SelectedSet;
            var hasValidSet = entry?.Set != null;
            _addQuestionButton.SetEnabled(hasValidSet);

            var question = _selection.SelectedQuestion;
            var hasQuestionSelection = hasValidSet && question != null;
            var count = hasValidSet ? entry.Set.Questions.Count : 0;

            _deleteQuestionButton.SetEnabled(hasQuestionSelection && QuestionListEditor.CanRemoveQuestion(count));

            var index = hasQuestionSelection ? IndexOfQuestion(entry.Set.Questions, question.Id) : -1;
            _moveQuestionUpButton.SetEnabled(index > 0);
            _moveQuestionDownButton.SetEnabled(index >= 0 && index < count - 1);
        }

        private void SetDeleteQuestionConfirmVisible(bool visible)
        {
            _deleteQuestionConfirmContainer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ShowQuestionStatus(string message)
        {
            _questionStatusLabel.text = message ?? string.Empty;
            _questionStatusLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static int IndexOfQuestion(IReadOnlyList<Question> questions, string id)
        {
            for (var i = 0; i < questions.Count; i++)
            {
                if (questions[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
