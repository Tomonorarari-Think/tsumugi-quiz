using System;
using TsumugiQuiz.Questions;
using TsumugiQuiz.UI.TextLayout;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.HostSetup
{
    /// <summary>
    /// <see cref="HostSetupView"/> のうち、問題フォルダの読み込み状況表示・再読込・
    /// 「フォルダを開く」を扱う部分クラス（統括メモ、#29 からの引き継ぎ）。
    /// <see cref="QuestionLibrary"/> は本 View が生成し、<see cref="HostSetupView.OnHide"/> で
    /// 破棄する（Boot 常駐化するかどうかは #7 で判断する）。
    /// </summary>
    public sealed partial class HostSetupView
    {
        private QuestionLibrary _questionLibrary;

        /// <summary>
        /// 問題フォルダの監視・読み込みを開始する。
        /// </summary>
        /// <remarks>
        /// PR #118 レビュー M-4: <see cref="QuestionLibrary"/> のコンストラクタは既定フォルダの解決
        /// （<c>DocumentsPaths.Root</c>）で例外を投げうる（ユーザープロファイルの破損、
        /// 不正な <c>TSUMUGI_DOCUMENTS_ROOT</c> 等）。ここで捕まえないと HostSetup 画面の
        /// <c>OnShow</c> ごと落ちてホストを開始できなくなるため、<c>QuestionEditorView</c> と同じ方針で
        /// 「画面は表示したまま、理由を状態表示に出し、問題フォルダ関連の操作だけ無効化する」。
        /// </remarks>
        private void InitializeQuestionLibrary()
        {
            try
            {
                _questionLibrary = new QuestionLibrary();
            }
            catch (Exception ex)
            {
                _questionLibrary = null;
                Debug.LogError($"[HostSetupView] 問題フォルダの初期化に失敗しました: {ex}");
                ShowQuestionStatus($"問題フォルダを開けませんでした: {ex.Message}");
                SetQuestionFolderControlsEnabled(false);
                return;
            }

            SetQuestionFolderControlsEnabled(true);
            _questionLibrary.Changed += OnQuestionReportChanged;
            RefreshQuestionSection(_questionLibrary.CurrentReport);
        }

        /// <summary>問題フォルダに依存するボタンの活性状態をまとめて切り替える。</summary>
        private void SetQuestionFolderControlsEnabled(bool enabled)
        {
            _reloadQuestionsButton?.SetEnabled(enabled);
            _openQuestionFolderButton?.SetEnabled(enabled);
        }

        private void OnReloadQuestionsClicked()
        {
            if (_questionLibrary == null)
            {
                // 初期化に失敗している（上記 M-4）。ボタンは無効化してあるが、念のため。
                ShowQuestionStatus("問題フォルダを開けないため再読込できません。");
                return;
            }

            ShowQuestionStatus(string.Empty);
            _questionLibrary.Reload();
        }

        private void OnOpenQuestionFolderClicked()
        {
            if (_questionLibrary == null)
            {
                ShowQuestionStatus("問題フォルダを開けないため表示できません。");
                return;
            }

            // M-5: 失敗時（フォルダが見つからない・エクスプローラーの起動に失敗した等）は
            // 画面にも理由を出す（従来は Debug ログのみで、UI からは何も起きていないように見えた）。
            var opened = QuestionFolderLauncher.OpenInExplorer(_questionLibrary.FolderPath);
            ShowQuestionStatus(opened ? string.Empty : $"フォルダを開けませんでした: {_questionLibrary.FolderPath}");
        }

        private void OnQuestionReportChanged(QuestionLoadReport report)
        {
            if (_questionSummaryLabel == null)
            {
                // OnHide 済み（View 切替後）に監視イベントが届いた場合は何もしない。
                return;
            }

            RefreshQuestionSection(report);
        }

        private void RefreshQuestionSection(QuestionLoadReport report)
        {
            var totalQuestions = 0;
            foreach (var set in report.Sets)
            {
                totalQuestions += set.Questions.Count;
            }

            _questionSummaryLabel.text =
                $"{report.Sets.Count} セット / {totalQuestions} 問（最終読み込み: {report.LoadedAt:HH:mm:ss}）";

            _questionIssuesContainer.Clear();
            // M-10: 行の組み立て（重複除去含む）は純 C# の HostSetupPresenter に移し、EditMode でテストする。
            foreach (var line in HostSetupPresenter.BuildQuestionIssueLines(report))
            {
                // #206: 読み込みの問題点は問題データ（セットの題名・id など）を含みうるため平文にする。
                var label = PlainText.CreateLabel(line);
                label.AddToClassList("small-text");
                label.AddToClassList("question-issue-line");
                _questionIssuesContainer.Add(label);
            }
        }

        private void ShowQuestionStatus(string message)
        {
            if (_questionStatusLabel == null)
            {
                return;
            }

            _questionStatusLabel.text = message ?? string.Empty;
            _questionStatusLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void DisposeQuestionLibrary()
        {
            if (_questionLibrary == null)
            {
                return;
            }

            _questionLibrary.Changed -= OnQuestionReportChanged;
            _questionLibrary.Dispose();
            _questionLibrary = null;
        }
    }
}
