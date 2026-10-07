using System;
using System.Collections.Generic;
using TsumugiQuiz.Questions;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.QuestionEditor
{
    /// <summary>
    /// <see cref="QuestionEditorView"/> のうち、編集フォーム（issue #31）の画像選択 UI を扱う部分クラス。
    /// docs/question-data.md §8 の仮決め通り、Windows 標準のファイル選択ダイアログではなく
    /// <c>Questions/images/</c> フォルダ内の一覧から選ぶ簡易 UI（<see cref="QuestionSetEditorService.ListImages"/>）
    /// + 「フォルダを開く」（<see cref="TsumugiQuiz.Questions.QuestionFolderLauncher"/>）+ 再読込とする。
    /// </summary>
    public sealed partial class QuestionEditorView
    {
        private const string NoImageOptionLabel = "（画像を使用しない）";

        private void BuildImageSection(VisualElement parent)
        {
            _formImageSection = new VisualElement();

            var heading = new Label("画像（省略可）");
            heading.AddToClassList("body-text");
            _formImageSection.Add(heading);

            _formSelectedImageLabel = new Label();
            _formSelectedImageLabel.AddToClassList("body-text");
            _formImageSection.Add(_formSelectedImageLabel);

            _formImageListContainer = new VisualElement { name = "question-form-image-list-container" };
            _formImageListContainer.AddToClassList("question-editor-list");
            _formImageSection.Add(_formImageListContainer);

            _formImageStatusLabel = new Label(string.Empty) { name = "question-form-image-status-label" };
            _formImageStatusLabel.AddToClassList("error-text");
            _formImageStatusLabel.style.display = DisplayStyle.None;
            _formImageSection.Add(_formImageStatusLabel);

            var buttonRow = new VisualElement();
            buttonRow.AddToClassList("menu-row");
            buttonRow.Add(new Button(OnOpenImagesFolderClicked) { text = "フォルダを開く" });
            buttonRow.Add(new Button(OnReloadImagesClicked) { text = "再読込" });
            _formImageSection.Add(buttonRow);

            parent.Add(_formImageSection);

            RenderImageList();
        }

        private void RenderImageList()
        {
            _formSelectedImageLabel.text = string.IsNullOrEmpty(_form.ImagePath)
                ? "選択中の画像: なし"
                : $"選択中の画像: {_form.ImagePath}";

            _formImageListContainer.Clear();
            _formImageListContainer.Add(CreateImageRow(NoImageOptionLabel, string.Empty));

            IReadOnlyList<string> images;
            try
            {
                images = _service.ListImages();
            }
            catch (Exception ex)
            {
                ShowImageStatus($"画像一覧の取得に失敗しました: {ex.Message}");
                return;
            }

            foreach (var image in images)
            {
                _formImageListContainer.Add(CreateImageRow(image, image));
            }

            // 件数上限で打ち切られた場合の警告（PR #93 レビュー M3。
            // セット一覧の FolderWarnings と同じ作法で、利用者に「全部は出ていない」ことを伝える）。
            var warnings = _service.ImageFolderWarnings;
            if (warnings != null && warnings.Count > 0)
            {
                ShowImageStatus(string.Join(" ", warnings));
            }
        }

        private VisualElement CreateImageRow(string label, string relativePath)
        {
            var row = new VisualElement();
            row.AddToClassList("question-editor-row");

            if (_form.ImagePath == relativePath)
            {
                row.AddToClassList("question-editor-row--selected");
            }

            var displayLabel = new Label(label);
            displayLabel.AddToClassList("question-editor-row-label");
            row.Add(displayLabel);

            row.RegisterCallback<ClickEvent>(_ => OnImageRowClicked(relativePath));

            return row;
        }

        private void OnImageRowClicked(string relativePath)
        {
            _form = _form.WithImagePath(relativePath);
            ShowImageStatus(string.Empty);
            RenderImageList();
        }

        private void OnOpenImagesFolderClicked()
        {
            var opened = QuestionFolderLauncher.OpenInExplorer(_service.ImagesFolderPath);
            ShowImageStatus(opened ? string.Empty : $"フォルダを開けませんでした: {_service.ImagesFolderPath}");
        }

        private void OnReloadImagesClicked()
        {
            ShowImageStatus(string.Empty);
            RenderImageList();
        }

        private void ShowImageStatus(string message)
        {
            _formImageStatusLabel.text = message ?? string.Empty;
            _formImageStatusLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
