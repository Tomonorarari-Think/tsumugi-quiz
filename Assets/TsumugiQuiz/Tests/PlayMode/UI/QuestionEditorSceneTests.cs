using System;
using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// Title → QuestionEditor → 戻る の画面遷移と、一覧コンテナが描画されることを検証する
    /// 最小限の PlayMode テスト（issue #30）。
    ///
    /// 実データの新規作成・削除・複製・並び替えは、このテストでは行わない
    /// （<see cref="TsumugiQuiz.Questions.Editing.QuestionSetEditorService"/> は
    /// <see cref="TsumugiQuiz.Questions.QuestionRepository.GetDefaultQuestionsFolderPath"/> を既定値に使う。
    /// <see cref="TsumugiQuiz.UI.Views.HostSetup.HostSetupView"/> の <c>QuestionLibrary</c> と同じ前提）。
    /// 純ロジック（並び替え・ID採番・削除可否）は EditMode の
    /// <c>QuestionSetEditorServiceTests</c> / <c>QuestionListEditorTests</c> / <c>QuestionEditorNamingTests</c>
    /// で検証済み。
    ///
    /// 副作用について（PR #88 レビュー LOW → #112 で解消）: QuestionEditor 画面を開くだけで
    /// <c>QuestionSetEditorService.ListSets()</c>（<c>EnsureFolderExists</c>）が呼ばれ、問題フォルダが
    /// 無ければ新規作成する。その問題フォルダは <c>PlayModeTestAssemblySetUp</c> がアセンブリ単位で
    /// 一時フォルダへ隔離している（<c>DocumentsRootScope</c>）ため、実ユーザーの
    /// <c>%USERPROFILE%\Documents\TsumugiQuiz\Questions\</c> は作成・変更されない。
    /// </summary>
    public class QuestionEditorSceneTests
    {
        private MainSceneTestHelpers.ConsentFileScope _consentScope;

        [SetUp]
        public void SeedConsentedState()
        {
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();

            var store = ConsentGate.CreateDefaultStore();
            var requiredTerms = TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, DateTime.UtcNow);
        }

        [TearDown]
        public void RestoreConsentFile()
        {
            _consentScope?.Restore();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TitleView_QuestionEditorButton_ShowsQuestionEditorView_WithSetAndQuestionLists()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            Button questionEditorButton = null;
            yield return WaitForElement<Button>(panelRoot, "question-editor-button", found => questionEditorButton = found);
            Assert.IsNotNull(questionEditorButton, "Title View の question-editor-button が見つかりません。");

            yield return SimulateClickRoutine(questionEditorButton);

            VisualElement setListContainer = null;
            yield return WaitForElement<VisualElement>(panelRoot, "set-list-container", found => setListContainer = found);
            Assert.IsNotNull(setListContainer, "question-editor-button クリック後に QuestionEditor View（set-list-container）へ遷移していません。");

            var questionListContainer = panelRoot.Q<VisualElement>("question-list-container");
            Assert.IsNotNull(questionListContainer, "question-list-container が見つかりません。");

            var newSetButton = panelRoot.Q<Button>("new-set-button");
            var duplicateSetButton = panelRoot.Q<Button>("duplicate-set-button");
            var deleteSetButton = panelRoot.Q<Button>("delete-set-button");
            var reloadSetsButton = panelRoot.Q<Button>("reload-sets-button");
            Assert.IsNotNull(newSetButton, "new-set-button が見つかりません。");
            Assert.IsNotNull(duplicateSetButton, "duplicate-set-button が見つかりません。");
            Assert.IsNotNull(deleteSetButton, "delete-set-button が見つかりません。");
            Assert.IsNotNull(reloadSetsButton, "reload-sets-button が見つかりません。");

            // 未選択のうちは複製・削除ができない（issue #30 受け入れ条件: 一覧操作の前提整合性）。
            Assert.IsFalse(duplicateSetButton.enabledSelf, "セット未選択の時点では複製ボタンは無効のはずです。");
            Assert.IsFalse(deleteSetButton.enabledSelf, "セット未選択の時点では削除ボタンは無効のはずです。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator QuestionEditorView_BackButton_ReturnsToTitle()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            Button questionEditorButton = null;
            yield return WaitForElement<Button>(panelRoot, "question-editor-button", found => questionEditorButton = found);
            yield return SimulateClickRoutine(questionEditorButton);

            Button backButton = null;
            yield return WaitForElement<Button>(panelRoot, "back-button", found => backButton = found);
            Assert.IsNotNull(backButton, "QuestionEditor View の back-button が見つかりません。");

            yield return SimulateClickRoutine(backButton);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            Assert.IsNotNull(hostButton, "戻るボタンのクリック後に Title View（host-button）へ戻っていません。");
            Assert.IsNull(panelRoot.Q<VisualElement>("set-list-container"), "戻った後も QuestionEditor View の要素が残っています。");
        }
    }
}
