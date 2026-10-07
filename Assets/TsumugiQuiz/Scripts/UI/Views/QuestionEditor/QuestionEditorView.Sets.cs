using System.Linq;
using TsumugiQuiz.Questions.Editing;
using TsumugiQuiz.UI.TextLayout;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.QuestionEditor
{
    /// <summary>
    /// <see cref="QuestionEditorView"/> のうち、「セット一覧」（新規作成・削除・複製・選択）を扱う部分クラス。
    /// </summary>
    public sealed partial class QuestionEditorView
    {
        private VisualElement _setListContainer;
        private Label _setStatusLabel;
        private VisualElement _setFolderWarningsContainer;
        private Button _newSetButton;
        private Button _duplicateSetButton;
        private Button _deleteSetButton;
        private Button _reloadSetsButton;
        private VisualElement _deleteSetConfirmContainer;
        private Label _deleteSetConfirmLabel;
        private Button _confirmDeleteSetButton;
        private Button _cancelDeleteSetButton;

        private bool QuerySetElements(VisualElement root)
        {
            _setListContainer = root.Q<VisualElement>("set-list-container");
            _setStatusLabel = root.Q<Label>("set-status-label");
            _setFolderWarningsContainer = root.Q<VisualElement>("set-folder-warnings-container");
            _newSetButton = root.Q<Button>("new-set-button");
            _duplicateSetButton = root.Q<Button>("duplicate-set-button");
            _deleteSetButton = root.Q<Button>("delete-set-button");
            _reloadSetsButton = root.Q<Button>("reload-sets-button");
            _deleteSetConfirmContainer = root.Q<VisualElement>("delete-set-confirm-container");
            // #206: セットの題名（問題データ）を含むため平文にする。
            _deleteSetConfirmLabel = PlainText.Apply(root.Q<Label>("delete-set-confirm-label"));
            _confirmDeleteSetButton = root.Q<Button>("confirm-delete-set-button");
            _cancelDeleteSetButton = root.Q<Button>("cancel-delete-set-button");

            return _setListContainer != null && _setStatusLabel != null && _setFolderWarningsContainer != null
                && _newSetButton != null && _duplicateSetButton != null && _deleteSetButton != null
                && _reloadSetsButton != null
                && _deleteSetConfirmContainer != null && _deleteSetConfirmLabel != null
                && _confirmDeleteSetButton != null && _cancelDeleteSetButton != null;
        }

        private void WireSetHandlers()
        {
            _newSetButton.clicked += OnNewSetClicked;
            _duplicateSetButton.clicked += OnDuplicateSetClicked;
            _deleteSetButton.clicked += OnDeleteSetClicked;
            _reloadSetsButton.clicked += OnReloadSetsClicked;
            _confirmDeleteSetButton.clicked += OnConfirmDeleteSetClicked;
            _cancelDeleteSetButton.clicked += OnCancelDeleteSetClicked;
        }

        private void UnwireSetHandlers()
        {
            if (_newSetButton != null) _newSetButton.clicked -= OnNewSetClicked;
            if (_duplicateSetButton != null) _duplicateSetButton.clicked -= OnDuplicateSetClicked;
            if (_deleteSetButton != null) _deleteSetButton.clicked -= OnDeleteSetClicked;
            if (_reloadSetsButton != null) _reloadSetsButton.clicked -= OnReloadSetsClicked;
            if (_confirmDeleteSetButton != null) _confirmDeleteSetButton.clicked -= OnConfirmDeleteSetClicked;
            if (_cancelDeleteSetButton != null) _cancelDeleteSetButton.clicked -= OnCancelDeleteSetClicked;
        }

        private void ClearSetElementReferences()
        {
            _setListContainer = null;
            _setStatusLabel = null;
            _setFolderWarningsContainer = null;
            _newSetButton = null;
            _duplicateSetButton = null;
            _deleteSetButton = null;
            _reloadSetsButton = null;
            _deleteSetConfirmContainer = null;
            _deleteSetConfirmLabel = null;
            _confirmDeleteSetButton = null;
            _cancelDeleteSetButton = null;
        }

        private void RenderSetList()
        {
            _setListContainer.Clear();

            foreach (var entry in _sets)
            {
                _setListContainer.Add(CreateSetRow(entry));
            }

            RenderFolderWarnings();
        }

        /// <summary>
        /// フォルダ単位の警告（ファイル数上限超過等、issue #30 のスキャナ上限。PR #88 レビュー M2）を
        /// 一覧の上部に表示する。
        /// </summary>
        private void RenderFolderWarnings()
        {
            _setFolderWarningsContainer.Clear();

            var warnings = _service?.FolderWarnings;
            if (warnings == null)
            {
                return;
            }

            foreach (var warning in warnings)
            {
                var label = new Label(warning);
                label.AddToClassList("body-text");
                label.AddToClassList("host-setup-warning");
                _setFolderWarningsContainer.Add(label);
            }
        }

        private VisualElement CreateSetRow(QuestionSetFileEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("question-editor-row");

            var isSelected = _selection.SelectedSet != null && _selection.SelectedSet.FilePath == entry.FilePath;
            if (isSelected)
            {
                row.AddToClassList("question-editor-row--selected");
            }

            // #206: セットの題名（問題データ）は平文として表示する。
            var label = PlainText.CreateLabel(QuestionEditorPresenter.SetSummary(entry));
            label.AddToClassList("question-editor-row-label");
            row.Add(label);

            row.RegisterCallback<ClickEvent>(_ => OnSetRowClicked(entry));

            return row;
        }

        private void OnSetRowClicked(QuestionSetFileEntry entry)
        {
            _selection.SelectSet(entry);
            SetDeleteSetConfirmVisible(false);
            ShowSetStatus(string.Empty);
            RefreshAll();
        }

        private void OnNewSetClicked()
        {
            var result = _service.CreateNewSet(_sets);
            if (!result.Success)
            {
                ShowSetStatus(result.ErrorMessage);
                return;
            }

            ShowSetStatus(string.Empty);
            _sets = SortEntriesLikeScanner(_sets.Append(result.Entry));
            _selection.SelectSet(result.Entry);
            RefreshAll();
        }

        private void OnDuplicateSetClicked()
        {
            var source = _selection.SelectedSet;
            if (source == null)
            {
                ShowSetStatus("複製するセットを選択してください。");
                return;
            }

            var result = _service.DuplicateSet(source, _sets);
            if (!result.Success)
            {
                ShowSetStatus(result.ErrorMessage);
                return;
            }

            ShowSetStatus(string.Empty);
            _sets = SortEntriesLikeScanner(_sets.Append(result.Entry));
            _selection.SelectSet(result.Entry);
            RefreshAll();
        }

        /// <summary>
        /// 一覧を実フォルダの内容で読み直す（PR #88 レビュー H3）。in-memory の一覧が古いために
        /// 新規作成・複製が失敗した場合（<see cref="QuestionSetWriter.TryWriteNew"/> の衝突）や、
        /// 外部変更検出で失敗した場合に、利用者がこのボタンで最新の状態へ揃えられるようにする。
        /// </summary>
        private void OnReloadSetsClicked()
        {
            ShowSetStatus(string.Empty);
            // 再読込は問題一覧・編集フォームの選択状態も作り直すため、古いエラー表示を残さない
            // （PR #88 レビュー LOW）。
            ShowQuestionStatus(string.Empty);
            SetDeleteSetConfirmVisible(false);
            ReloadSets();
        }

        private void OnDeleteSetClicked()
        {
            var entry = _selection.SelectedSet;
            if (entry == null)
            {
                ShowSetStatus("削除するセットを選択してください。");
                return;
            }

            _deleteSetConfirmLabel.text = QuestionEditorPresenter.DeleteSetConfirmMessage(entry);
            SetDeleteSetConfirmVisible(true);
        }

        private void OnConfirmDeleteSetClicked()
        {
            SetDeleteSetConfirmVisible(false);

            var entry = _selection.SelectedSet;
            if (entry == null)
            {
                return;
            }

            var result = _service.DeleteSet(entry);
            if (!result.Success)
            {
                ShowSetStatus(result.ErrorMessage);
                return;
            }

            ShowSetStatus(string.Empty);
            _sets = _sets.Where(e => e.FilePath != entry.FilePath).ToArray();
            _selection.Clear();
            RefreshAll();
        }

        private void OnCancelDeleteSetClicked() => SetDeleteSetConfirmVisible(false);

        private void UpdateSetButtonStates()
        {
            var hasSelection = _selection.SelectedSet != null;
            _duplicateSetButton.SetEnabled(hasSelection);
            _deleteSetButton.SetEnabled(hasSelection);
        }

        private void SetDeleteSetConfirmVisible(bool visible)
        {
            _deleteSetConfirmContainer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ShowSetStatus(string message)
        {
            _setStatusLabel.text = message ?? string.Empty;
            _setStatusLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
