using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;
using TsumugiQuiz.Tests.Shared.Questions;
using TsumugiQuiz.Tests.Shared.Tts;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.QuestionEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// 編集フォーム（issue #31）の PlayMode テスト。問題フォルダ
    /// （<see cref="QuestionRepository.GetDefaultQuestionsFolderPath"/>）へ、テスト専用の GUID 付き
    /// ファイル名（他の問題セットと衝突しない）を <see cref="SetUp"/> で直接書き込み、
    /// <see cref="TearDown"/> で必ず削除する（issue #30 の「新規作成ボタンはクリックしない」方針を
    /// さらに進め、UI 経由の採番にも頼らない）。
    /// #112: その問題フォルダは <c>PlayModeTestAssemblySetUp</c> がアセンブリ単位で一時フォルダへ
    /// 隔離している（<c>DocumentsRootScope</c>）ため、実ユーザーの Documents には触れない。
    ///
    /// 検証する範囲は issue #31 の受け入れ条件のうち PlayMode で確認すべき最小限
    /// （フォームが出る・type 切り替えで項目が変わる・保存前のエラー表示・画像を選んで保存すると
    /// <c>imagePath</c> が JSON まで永続化される）とする。
    /// </summary>
    public class QuestionEditorFormSceneTests
    {
        private MainSceneTestHelpers.ConsentFileScope _consentScope;
        private string _testSetFilePath;
        private string _testSetTitle;
        private string _testImageFilePath;
        private string _testImageRelativePath;
        private bool _imagesFolderCreatedByTest;

        [SetUp]
        public void SeedConsentedStateAndTestQuestionSet()
        {
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();

            var store = ConsentGate.CreateDefaultStore();
            var requiredTerms = TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, DateTime.UtcNow);

            var folder = QuestionRepository.GetDefaultQuestionsFolderPath();
            Directory.CreateDirectory(folder);

            var uniqueSuffix = Guid.NewGuid().ToString("N");
            var fileName = "i31-playmode-test-" + uniqueSuffix;
            _testSetFilePath = Path.Combine(folder, fileName + ".json");
            _testSetTitle = "i31 PlayModeテストセット " + uniqueSuffix;

            var question = new Question(
                "q1", QuestionType.FreeText, "PlayModeテスト問題", answers: new[] { "回答" });
            // H1（PR #103 レビュー）: 「別の問題行をクリック」を再現するための2件目（q2）。
            var otherQuestion = new Question(
                "q2", QuestionType.FreeText, "もう1つのPlayModeテスト問題", answers: new[] { "別解" });
            var set = new QuestionSet(1, fileName, _testSetTitle, string.Empty, new[] { question, otherQuestion });
            Assert.IsTrue(QuestionSetWriter.TryWriteNew(_testSetFilePath, set, out var error), error);

            // 画像選択 → 保存の経路を通すためのテスト用 PNG（PR #93 レビュー M5）。
            // 問題セットと同じく GUID 付きの名前にして、実ユーザーの画像と衝突・混同しないようにする。
            var imagesFolder = QuestionImageFolder.GetImagesFolderPath(folder);
            _imagesFolderCreatedByTest = !Directory.Exists(imagesFolder);
            Directory.CreateDirectory(imagesFolder);

            var imageFileName = "i31-playmode-test-" + uniqueSuffix + ".png";
            _testImageFilePath = Path.Combine(imagesFolder, imageFileName);
            _testImageRelativePath = QuestionImageFolder.ImagesFolderName + "/" + imageFileName;
            File.WriteAllBytes(_testImageFilePath, TestImageFactory.CreatePng(8, 8));
        }

        [TearDown]
        public void DeleteTestQuestionSetFileAndRestoreConsent()
        {
            if (_testSetFilePath != null && File.Exists(_testSetFilePath))
            {
                File.Delete(_testSetFilePath);
            }

            DeleteTestImage();

            _consentScope?.Restore();
        }

        /// <summary>
        /// テスト用 PNG を必ず削除し、テストが作った <c>images</c> フォルダが空になったときだけ
        /// フォルダも削除する（PR #93 レビュー L7。#112 の隔離ルート配下だが、同一実行内の
        /// 他テストが置いた画像を巻き込まないよう、この後始末はそのまま残す）。
        /// </summary>
        private void DeleteTestImage()
        {
            if (_testImageFilePath != null && File.Exists(_testImageFilePath))
            {
                File.Delete(_testImageFilePath);
            }

            if (!_imagesFolderCreatedByTest)
            {
                return;
            }

            var imagesFolder = QuestionImageFolder.GetImagesFolderPath(QuestionRepository.GetDefaultQuestionsFolderPath());
            if (Directory.Exists(imagesFolder) && !Directory.EnumerateFileSystemEntries(imagesFolder).Any())
            {
                Directory.Delete(imagesFolder);
            }
        }

        [UnityTearDown]
        public IEnumerator TearDownScene()
        {
            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator SelectingFreeTextQuestion_ThenSwitchingType_ChangesFormFields()
        {
            VisualElement panelRoot = null;
            yield return OpenEditorAndSelectTestQuestion(r => panelRoot = r);

            // フォームが出る（受け入れ条件）。
            DropdownField typeField = null;
            yield return WaitForElement<DropdownField>(panelRoot, "question-form-type-field", found => typeField = found);
            Assert.AreEqual(0, typeField.index, "freeText の問題を選択した直後は「自由入力」が選ばれていること。");

            Assert.IsNotNull(
                panelRoot.Q<VisualElement>("question-form-answers-container"),
                "freeText では正解候補（answers）のリストが表示されること。");
            Assert.IsNull(
                panelRoot.Q<VisualElement>("question-form-choices-container"),
                "freeText では選択肢（choices）のリストは表示されないこと。");

            // type 切り替えでフォーム項目が変化する（受け入れ条件）。
            typeField.value = "選択式";

            VisualElement choicesContainer = null;
            yield return WaitForElement<VisualElement>(panelRoot, "question-form-choices-container", found => choicesContainer = found);
            Assert.IsNotNull(choicesContainer, "choice に切り替えた後は選択肢（choices）のリストが表示されること。");
            Assert.IsNotNull(
                panelRoot.Q<RadioButtonGroup>("question-form-correct-index-group"),
                "choice に切り替えた後は正解（correctIndex）のラジオ選択が表示されること。");
            Assert.IsNull(
                panelRoot.Q<VisualElement>("question-form-answers-container"),
                "choice に切り替えた後は正解候補（answers）のリストは表示されないこと。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator SavingWithEmptyAnswer_ShowsValidationErrorWithoutCrashing()
        {
            VisualElement panelRoot = null;
            yield return OpenEditorAndSelectTestQuestion(r => panelRoot = r);

            TextField answerField = null;
            yield return WaitForElement<TextField>(panelRoot, "question-form-answer-field-0", found => answerField = found);
            answerField.value = string.Empty;

            Button saveButton = null;
            yield return WaitForElement<Button>(panelRoot, "question-form-save-button", found => saveButton = found);
            yield return SimulateClickRoutine(saveButton);

            VisualElement errorListContainer = null;
            yield return WaitForElement<VisualElement>(
                panelRoot, "question-form-error-list-container", found => errorListContainer = found);

            yield return WaitUntil(
                () => errorListContainer.style.display == DisplayStyle.Flex && errorListContainer.childCount > 0,
                "不正な入力（空の回答）で保存しても、エラー一覧が表示されないままです。",
                dumpRoot: panelRoot);

            var answersContainer = panelRoot.Q<VisualElement>("question-form-answers-container");
            Assert.IsNotNull(answersContainer);
            Assert.IsTrue(
                answersContainer.ClassListContains("question-editor-field--invalid"),
                "エラーの原因（answers）にハイライト用クラスが付与されていること。");

            // 保存されておらず（外部変更検出が発火しない = ファイル内容が保持されている）ことも確認する。
            var persisted = QuestionSetFileScanner.ScanFolder(Path.GetDirectoryName(_testSetFilePath))
                .Entries.Single(e => e.FilePath == _testSetFilePath);
            Assert.IsTrue(persisted.IsValid);
            CollectionAssert.AreEqual(new[] { "回答" }, persisted.Set.Questions.Single(q => q.Id == "q1").Answers);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator SelectingImage_ThenSaving_PersistsImagePath()
        {
            VisualElement panelRoot = null;
            yield return OpenEditorAndSelectTestQuestion(r => panelRoot = r);

            VisualElement imageListContainer = null;
            yield return WaitForElement<VisualElement>(
                panelRoot, "question-form-image-list-container", found => imageListContainer = found);

            // SetUp で images フォルダに置いた GUID 付き PNG の行が一覧に出るまで待ってからクリックする。
            VisualElement imageRow = null;
            yield return WaitUntil(
                () => (imageRow = FindRowByLabelText(imageListContainer, _testImageRelativePath)) != null,
                $"テスト用画像「{_testImageRelativePath}」の一覧行が見つかりません。",
                dumpRoot: panelRoot);
            yield return SimulateRowClickRoutine(imageRow, dumpRoot: panelRoot);

            Button saveButton = null;
            yield return WaitForElement<Button>(panelRoot, "question-form-save-button", found => saveButton = found);
            yield return SimulateClickRoutine(saveButton);

            // 保存が JSON まで届いていること（再スキャンして imagePath が永続化されている）を確認する。
            Question persisted = null;
            yield return WaitUntil(
                () =>
                {
                    var entry = QuestionSetFileScanner.ScanFolder(Path.GetDirectoryName(_testSetFilePath))
                        .Entries.SingleOrDefault(e => e.FilePath == _testSetFilePath);
                    persisted = entry?.Set?.Questions.SingleOrDefault(q => q.Id == "q1");
                    return persisted != null && persisted.ImagePath == _testImageRelativePath;
                },
                MainSceneTestHelpers.DefaultTimeoutSeconds,
                () => "保存後にファイルへ imagePath が書き戻されていません"
                    + $"（期待: {_testImageRelativePath}、実際: {persisted?.ImagePath ?? "(null)"}）。",
                dumpRoot: panelRoot);

            Assert.AreEqual(_testImageRelativePath, persisted.ImagePath);
        }

        /// <summary>
        /// PR #103 レビュー H1: 「問題文を編集 → 別の問題行をクリック」という最も典型的な経路で、
        /// 「未保存の変更を破棄しました。」が実際に表示され続けること（クリア→選択の順に固定する回帰テスト）。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator EditingText_ThenClickingAnotherQuestionRow_ShowsDiscardedChangesNotice()
        {
            VisualElement panelRoot = null;
            yield return OpenEditorAndSelectTestQuestion(r => panelRoot = r);

            TextField textField = null;
            yield return WaitForElement<TextField>(panelRoot, "question-form-text-field", found => textField = found);
            textField.value = "編集後の問題文（未保存のまま切り替える）";

            VisualElement questionListContainer = null;
            yield return WaitForElement<VisualElement>(
                panelRoot, "question-list-container", found => questionListContainer = found);

            VisualElement otherQuestionRow = null;
            yield return WaitUntil(
                () => (otherQuestionRow = FindRowByLabelText(questionListContainer, "q2")) != null,
                "テスト用問題（q2）の一覧行が見つかりません。",
                dumpRoot: panelRoot);

            // #206: 問題データ（問題文の要約・セットの題名）は、ゲーム画面と同じく平文として表示する。
            Assert.IsFalse(otherQuestionRow.Q<Label>().enableRichText, "問題の一覧行はタグを解釈しないはず。");
            var setRow = FindRowByLabelText(panelRoot.Q<VisualElement>("set-list-container"), _testSetTitle);
            Assert.IsNotNull(setRow, $"テスト用セット「{_testSetTitle}」の一覧行が見つかりません。");
            Assert.IsFalse(setRow.Q<Label>().enableRichText, "セットの一覧行はタグを解釈しないはず。");

            yield return SimulateRowClickRoutine(otherQuestionRow);

            Label questionStatusLabel = null;
            yield return WaitForElement<Label>(panelRoot, "question-status-label", found => questionStatusLabel = found);

            yield return WaitUntil(
                () => questionStatusLabel.style.display == DisplayStyle.Flex
                    && questionStatusLabel.text == "未保存の変更を破棄しました。",
                MainSceneTestHelpers.DefaultTimeoutSeconds,
                () => "別の問題行をクリックしても「未保存の変更を破棄しました。」が表示されませんでした"
                    + $"（実際のテキスト: \"{questionStatusLabel.text}\"）。",
                dumpRoot: panelRoot);

            // LogAssert.NoUnexpectedReceived() は使わない: 実測したところ Debug.Log レベルの通常の
            // 情報ログ（TtsService の初期化ログ等）まで「未処理」として検知してしまい、
            // 個々のログを LogAssert.Expect で網羅しないと通らなくなるため過剰に厳格だった。
            // Unity のテストランナーは既定で Error/Exception ログを検知した時点で自動的にテストを
            // 失敗させるため、それに委ねる（PR #103 レビュー M4 の意図はこちらで満たされる）。
        }

        /// <summary>
        /// 読み上げプレビュー（issue #32）: ボタン押下で、フェイクエンジンの合成要求に
        /// フォーム上の読み上げ用テキスト（<c>readingText</c>）がそのまま渡ること。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ClickingTtsPreviewButton_PassesReadingTextToSynthesis()
        {
            var fakeEngine = new FakeTtsSynthesisEngine();
            var ttsGameObject = CreateFakeTtsService((_, __) => fakeEngine, out var cacheRoot);

            try
            {
                VisualElement panelRoot = null;
                yield return OpenEditorAndSelectTestQuestion(r => panelRoot = r);

                TextField readingTextField = null;
                yield return WaitForElement<TextField>(
                    panelRoot, "question-form-reading-text-field", found => readingTextField = found);
                const string readingText = "てすとのよみあげぷれびゅー";
                readingTextField.value = readingText;

                Button previewButton = null;
                yield return WaitForElement<Button>(
                    panelRoot, "question-form-tts-preview-button", found => previewButton = found);
                yield return SimulateClickRoutine(previewButton);

                yield return WaitUntil(
                    () => fakeEngine.SynthesizeCount > 0,
                    "読み上げプレビューを押しても合成が呼ばれませんでした。",
                    dumpRoot: panelRoot);

                Assert.AreEqual(readingText, fakeEngine.LastRequestedText, "readingText がそのまま合成へ渡ること。");

                // PR #103 レビュー M4: プレビューを止めずにテストを終えると、シーン破棄後も進行中の
                // 非同期処理（再生完了のポーリング等）が残り、AudioSource の GameObject や破棄済み要素への
                // 参照を後続テストへ持ち越しうる。フェイクエンジンの音声はごく短い（既定240フレーム）ため、
                // 「停止」を押す前に自然完了（Idle）へ戻っている可能性もある。合成中（「準備中…」）でなく
                // なるまで待ってから、まだ再生中（「停止」表示）ならクリックして止め、
                // 最終的に Idle（「読み上げを試す」表示）に戻るまで待つ。
                yield return WaitUntil(
                    () => previewButton.text != QuestionEditorView.TtsPreviewSynthesizingButtonText,
                    "合成中の状態（「準備中…」）から変化しませんでした。",
                    dumpRoot: panelRoot);

                if (previewButton.text == QuestionEditorView.TtsPreviewPlayingButtonText)
                {
                    yield return SimulateClickRoutine(previewButton);
                }

                yield return WaitUntil(
                    () => previewButton.text == QuestionEditorView.TtsPreviewIdleButtonText,
                    "待機状態（「読み上げを試す」表示）に戻りませんでした。",
                    dumpRoot: panelRoot);

                // LogAssert.NoUnexpectedReceived() は使わない（上の EditingText_ThenClickingAnotherQuestionRow_...
                // のコメント参照）。Error/Exception の自動検知に委ねる。
            }
            finally
            {
                DestroyFakeTtsService(ttsGameObject, cacheRoot);
            }
        }

        /// <summary>
        /// 読み上げプレビュー（issue #32）: External 未配置等で <c>TtsService</c> が利用できない場合、
        /// #25 のフォールバック UI（<see cref="TtsStatusMessages"/>）と同じ見出しの短い案内 +
        /// 「読み上げの状態を確認」ボタンがフォーム内に表示されること（PR #103 レビュー M1）。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ClickingTtsPreviewButton_WhenNotConfigured_ShowsFallbackGuidance()
        {
            // 実際に voicevox_core.dll が見つからない場合と同じ分類（TtsUnavailableReason.MissingCoreDll）で
            // 初期化を失敗させる。TtsSynthesisEngine.Create が本番で投げるのと同じ形（TtsSetupException）。
            var ttsGameObject = CreateFakeTtsService(
                (_, __) => throw new TtsSetupException("テスト用: 未配置を模擬", TtsUnavailableReason.MissingCoreDll),
                out var cacheRoot);

            try
            {
                VisualElement panelRoot = null;
                yield return OpenEditorAndSelectTestQuestion(r => panelRoot = r);

                Button previewButton = null;
                yield return WaitForElement<Button>(
                    panelRoot, "question-form-tts-preview-button", found => previewButton = found);
                yield return SimulateClickRoutine(previewButton);

                Label statusLabel = null;
                yield return WaitForElement<Label>(
                    panelRoot, "question-form-tts-status-label", found => statusLabel = found);
                Button checkStatusButton = null;
                yield return WaitForElement<Button>(
                    panelRoot, "question-form-tts-check-status-button", found => checkStatusButton = found);

                var expectedMessage = TtsStatusMessages.For(TtsUnavailableReason.MissingCoreDll);
                yield return WaitUntil(
                    () => statusLabel.style.display == DisplayStyle.Flex
                        && statusLabel.text.Contains(expectedMessage.Headline)
                        && checkStatusButton.style.display == DisplayStyle.Flex,
                    "未配置時の案内（見出し）と「読み上げの状態を確認」ボタンが表示されませんでした。",
                    dumpRoot: panelRoot);

                // LogAssert.NoUnexpectedReceived() は使わない（上の EditingText_ThenClickingAnotherQuestionRow_...
                // のコメント参照）。Error/Exception の自動検知に委ねる。
            }
            finally
            {
                DestroyFakeTtsService(ttsGameObject, cacheRoot);
            }
        }

        /// <summary>
        /// PR #103 レビュー H3: 利用規約に未同意（撤回済み）の状態で「読み上げを試す」を押しても、
        /// 合成そのものが走らない（FR-74/75）こと、および #25 のフォールバック UI と同じ見出しの案内が
        /// 出ることを固定する。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ClickingTtsPreviewButton_WhenConsentNotGiven_DoesNotSynthesize()
        {
            var fakeEngine = new FakeTtsSynthesisEngine();
            var ttsGameObject = CreateFakeTtsService((_, __) => fakeEngine, out var cacheRoot);

            try
            {
                VisualElement panelRoot = null;
                yield return OpenEditorAndSelectTestQuestion(r => panelRoot = r);

                // 画面遷移（Title → QuestionEditor）自体は SetUp で記録した同意状態のまま行い、
                // 「読み上げを試す」を押す直前にだけ同意を取り消す（FR-75: 撤回後は実行しない）。
                _consentScope.DeleteCurrentFile();

                Button previewButton = null;
                yield return WaitForElement<Button>(
                    panelRoot, "question-form-tts-preview-button", found => previewButton = found);
                yield return SimulateClickRoutine(previewButton);

                Label statusLabel = null;
                yield return WaitForElement<Label>(
                    panelRoot, "question-form-tts-status-label", found => statusLabel = found);
                Button checkStatusButton = null;
                yield return WaitForElement<Button>(
                    panelRoot, "question-form-tts-check-status-button", found => checkStatusButton = found);

                var expectedMessage = TtsStatusMessages.For(TtsUnavailableReason.ConsentNotGiven);
                yield return WaitUntil(
                    () => statusLabel.style.display == DisplayStyle.Flex
                        && statusLabel.text.Contains(expectedMessage.Headline)
                        && checkStatusButton.style.display == DisplayStyle.Flex,
                    MainSceneTestHelpers.DefaultTimeoutSeconds,
                    () => "未同意時の案内（見出し）と「読み上げの状態を確認」ボタンが表示されません"
                        + $"でした（実際: \"{statusLabel.text}\"）。",
                    dumpRoot: panelRoot);

                Assert.AreEqual(0, fakeEngine.SynthesizeCount, "未同意のときは合成そのものが走らないこと（FR-74/75）。");

                // LogAssert.NoUnexpectedReceived() は使わない（上の EditingText_ThenClickingAnotherQuestionRow_...
                // のコメント参照）。Error/Exception の自動検知に委ねる。
            }
            finally
            {
                DestroyFakeTtsService(ttsGameObject, cacheRoot);
            }
        }

        /// <summary>
        /// テスト用のフェイク合成エンジンを使う <see cref="TtsService"/> を作る。既存の
        /// <see cref="TtsService.Instance"/>（他テストの残留）があれば先に破棄してから作り直す
        /// （issue #73 で判明した「TTS の初期化状態が後続テストに持ち越される」問題と同種の対策）。
        /// </summary>
        private static GameObject CreateFakeTtsService(TtsSynthesisEngineFactory engineFactory, out string cacheRoot)
        {
            if (TtsService.Instance != null)
            {
                UnityEngine.Object.DestroyImmediate(TtsService.Instance.gameObject);
            }

            cacheRoot = Path.Combine(
                Path.GetTempPath(), "TsumugiQuizI32TtsPreviewTests_" + Guid.NewGuid().ToString("N"));

            var gameObject = new GameObject("I32TtsPreviewTest_TtsService");
            var service = gameObject.AddComponent<TtsService>();
            service.Initialize(cacheRootOverride: cacheRoot, engineFactory: engineFactory);
            return gameObject;
        }

        private static void DestroyFakeTtsService(GameObject gameObject, string cacheRoot)
        {
            if (gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            if (cacheRoot == null || !Directory.Exists(cacheRoot))
            {
                return;
            }

            try
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
            catch (IOException)
            {
                // 一時ディレクトリなので、まだ書き込み中でも放置してよい。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上（ロック中・権限なし）。
            }
        }

        /// <summary>
        /// Main シーンを読み込み、問題エディタを開いてテスト用セット → テスト用問題（q1）を選択し、
        /// 編集フォームが出た状態にする（各テストの共通の前段）。
        /// </summary>
        private IEnumerator OpenEditorAndSelectTestQuestion(Action<VisualElement> onPanelRoot)
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            Button questionEditorButton = null;
            yield return WaitForElement<Button>(panelRoot, "question-editor-button", found => questionEditorButton = found);
            yield return SimulateClickRoutine(questionEditorButton);

            VisualElement setListContainer = null;
            yield return WaitForElement<VisualElement>(panelRoot, "set-list-container", found => setListContainer = found);

            VisualElement setRow = null;
            yield return WaitUntil(
                () => (setRow = FindRowByLabelText(setListContainer, _testSetTitle)) != null,
                $"テスト用セット「{_testSetTitle}」の一覧行が見つかりません。",
                dumpRoot: panelRoot);
            yield return SimulateRowClickRoutine(setRow, dumpRoot: panelRoot);

            VisualElement questionListContainer = null;
            yield return WaitForElement<VisualElement>(panelRoot, "question-list-container", found => questionListContainer = found);

            VisualElement questionRow = null;
            yield return WaitUntil(
                () => (questionRow = FindRowByLabelText(questionListContainer, "q1")) != null,
                "テスト用問題（q1）の一覧行が見つかりません。",
                dumpRoot: panelRoot);
            yield return SimulateRowClickRoutine(questionRow, dumpRoot: panelRoot);

            onPanelRoot(panelRoot);
        }

        /// <summary>一覧コンテナの直下の行から、指定テキストを含む <see cref="Label"/> を持つ行を探す。</summary>
        private static VisualElement FindRowByLabelText(VisualElement listContainer, string containedText)
        {
            foreach (var row in listContainer.Children())
            {
                var label = row.Q<Label>();
                if (label != null && label.text != null && label.text.Contains(containedText))
                {
                    return row;
                }
            }

            return null;
        }
    }
}
