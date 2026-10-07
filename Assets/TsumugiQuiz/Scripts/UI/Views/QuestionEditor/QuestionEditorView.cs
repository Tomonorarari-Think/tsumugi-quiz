using System;
using System.Collections.Generic;
using System.Linq;
using TsumugiQuiz.Questions.Editing;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.QuestionEditor
{
    /// <summary>
    /// 問題エディタ画面のコントローラ（issue #30）。
    /// 「セット一覧」（新規作成・削除・複製）と「問題一覧」（追加・削除・並び替え）の一覧操作までを扱う。
    /// 編集フォーム・保存・読み上げプレビューは #31/#32 の範囲（<see cref="_questionEditFormContainer"/> に
    /// 差し込める構造にしてある）。
    ///
    /// 実データの読み書きは <see cref="QuestionSetEditorService"/>（Questions asmdef）を経由し、
    /// 保存直前の検証は <see cref="TsumugiQuiz.Questions.QuestionSetValidator"/> に一本化されている。
    /// 「現在選択中のセット・問題」は Unity 非依存の <see cref="QuestionEditorSelectionState"/> に切り出し、
    /// 一覧行の組み立て・確認文言は <see cref="QuestionEditorPresenter"/>（いずれも純 C#）に切り出している。
    /// </summary>
    public sealed partial class QuestionEditorView : IView
    {
        private ViewRouter _router;
        private VisualElement _root;
        private QuestionSetEditorService _service;
        private readonly QuestionEditorSelectionState _selection = new QuestionEditorSelectionState();
        private IReadOnlyList<QuestionSetFileEntry> _sets = Array.Empty<QuestionSetFileEntry>();

        private Button _backButton;

        public void OnShow(ViewContext context)
        {
            _router = context.Router;
            _root = context.Root;
            var root = context.Root;

            if (!QueryElements(root))
            {
                Debug.LogError("[QuestionEditorView] 必要な UI 要素が見つかりません。question-editor-view.uxml を確認してください。");
                return;
            }

            // 戻るボタンの配線を最初に行う（PR #88 レビュー LOW）。
            // 問題フォルダの初期化（DocumentsPaths.Root、下の try/catch）が例外を投げても、
            // 利用者が Title へ戻れるようにするため。
            _backButton.clicked += OnBackClicked;

            SetDeleteSetConfirmVisible(false);
            SetDeleteQuestionConfirmVisible(false);
            ShowSetStatus(string.Empty);
            ShowQuestionStatus(string.Empty);

            try
            {
                _service = new QuestionSetEditorService();
            }
            catch (Exception ex)
            {
                // DocumentsPaths.Root（ユーザーの Documents フォルダ取得）が失敗した場合等。
                // 画面自体は表示したまま、エラーを状態表示に出して一覧操作は行わせない。
                Debug.LogError($"[QuestionEditorView] 問題フォルダの初期化に失敗しました: {ex.Message}");
                ShowSetStatus($"問題フォルダを開けませんでした: {ex.Message}");
                return;
            }

            WireSetHandlers();
            WireQuestionHandlers();
            WireFormHandlers();

            ReloadSets();
        }

        public void OnHide()
        {
            UnwireSetHandlers();
            UnwireQuestionHandlers();
            UnwireFormHandlers();

            if (_backButton != null)
            {
                _backButton.clicked -= OnBackClicked;
            }

            ClearSetElementReferences();
            ClearQuestionElementReferences();
            ClearFormElementReferences();

            _backButton = null;
            _service = null;
            _sets = Array.Empty<QuestionSetFileEntry>();
            _router = null;
            _root = null;
        }

        private bool QueryElements(VisualElement root)
        {
            _backButton = root.Q<Button>("back-button");

            return _backButton != null && QuerySetElements(root) && QueryQuestionElements(root);
        }

        private void OnBackClicked() => NavigateBack();

        private void NavigateBack()
        {
            if (_router.CanGoBack)
            {
                _router.GoBack();
            }
            else
            {
                _router.ShowView(ViewNames.Title);
            }
        }

        /// <summary>セット一覧を読み直し、選択状態を保ったまま画面全体を再描画する。</summary>
        private void ReloadSets()
        {
            _sets = _service.ListSets();

            if (_selection.SelectedSet != null)
            {
                var stillExists = _sets.FirstOrDefault(e => e.FilePath == _selection.SelectedSet.FilePath);
                _selection.ReplaceSelectedSet(stillExists);
            }

            RefreshAll();
        }

        private void RefreshAll()
        {
            RenderSetList();
            RenderQuestionList();
            UpdateSetButtonStates();
            UpdateQuestionButtonStates();
        }

        /// <summary>更新済みの1エントリを <see cref="_sets"/> と選択状態の両方に反映する。</summary>
        private void ReplaceEntryEverywhere(QuestionSetFileEntry updatedEntry)
        {
            _sets = _sets.Select(e => e.FilePath == updatedEntry.FilePath ? updatedEntry : e).ToArray();

            // 選択中のセットと FilePath が一致するときだけ選択を差し替える（PR #88 レビュー LOW）。
            // 一致しない場合に無条件で差し替えると、別セットを選択し直した直後の非同期な書き込み結果が
            // 遅れて返ってきたとき等に選択状態を意図せず奪ってしまう。
            if (_selection.SelectedSet != null && _selection.SelectedSet.FilePath == updatedEntry.FilePath)
            {
                _selection.ReplaceSelectedSet(updatedEntry);
            }

            RefreshAll();
        }

        /// <summary>
        /// <see cref="QuestionSetFileScanner.ScanFolder"/> と同じ並び順（フルパスの
        /// <see cref="StringComparer.Ordinal"/>）に揃える（PR #88 レビュー LOW）。
        /// 新規作成・複製の直後に追加したエントリを末尾に足すだけだと、次回 <see cref="ReloadSets"/> と
        /// 表示順がずれるため。
        /// </summary>
        private static IReadOnlyList<QuestionSetFileEntry> SortEntriesLikeScanner(IEnumerable<QuestionSetFileEntry> entries)
            => entries.OrderBy(e => e.FilePath, StringComparer.Ordinal).ToArray();
    }
}
