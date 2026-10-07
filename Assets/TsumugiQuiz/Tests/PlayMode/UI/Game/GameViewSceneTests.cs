using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.Tests.Shared.Room;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.Game;
using TsumugiQuiz.Tests.PlayMode.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI.Game
{
    /// <summary>
    /// Game View（issue #14）の PlayMode テスト。
    /// クライアント役の受け入れ条件テストは、<see cref="JoinViewSceneTests"/> と同じ作法で
    /// 1 プロセス内に立てた生の <see cref="NetworkManager"/>（実プレハブの <see cref="GameSession"/> を
    /// スポーン）をホストとし、Boot → Main シーンを実際に読み込んだ側をクライアントとして駆動する。
    /// ホスト役の経路（<see cref="NetworkService.ActiveGameSession"/> 分岐・「次へ」ボタン）は
    /// <see cref="GameView_HostPath_ActiveGameSessionAndNextButton_FinishesSession"/> で、
    /// Boot → Main 側の <see cref="NetworkService"/> 自身をホストにして確認する。
    /// 2 プロセス（実行ファイル 2 つ）を使った手動確認は #15（縦切り確認）で行う（本 PR の対象外、H-1）。
    /// </summary>
    public class GameViewSceneTests : InputTestFixture
    {
        private const string LoopbackAddress = "127.0.0.1";
        private const string QuestionText = "日本の首都はどこ？";
        private const string CorrectAnswer = "とうきょう";

        /// <summary>2 問目（PR #114 レビュー M-5。1 問目との差し替わりを問題文で見分ける）。</summary>
        private const string SecondQuestionText = "日本でいちばん高い山は？";
        private const string SecondCorrectAnswer = "ふじさん";

        /// <summary>
        /// テスト用 seam（<c>SuppressDistributedQuestionShownForTests</c>）を立てたセッション。
        /// TearDown で必ず戻す（PR #114 再レビュー LOW-7）。使っていないテストでは null のまま。
        /// </summary>
        private GameSession _suppressedQuestionShownSession;

        /// <summary>1 本のテストの中で使い回す Game View の要素をまとめたもの。</summary>
        private sealed class GameFlowElements
        {
            public Label QuestionTextLabel;
            public Button BuzzButton;
            public Label BuzzResultLabel;
            public VisualElement AnswerSection;
            public TextField AnswerField;
            public Button AnswerSubmitButton;
            public Label ResultLabel;
            public GameSession ClientSession;
        }

        private MainSceneTestHelpers.ConsentFileScope _consentScope;

        /// <summary>
        /// ホスト開始時に <c>RoomSettingsSync</c> が読む下書き（<c>room.lastApplied</c>）の隔離
        /// （PR #92 再レビュー M-1）。
        /// </summary>
        private RoomSettingsDraftScope _roomSettingsDraftScope;

        private GameObject _hostObject;
        private NetworkManager _hostManager;
        private NetworkService _hostService;
        private GameSession _hostSession;

        private Keyboard _keyboard;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        [SetUp]
        public void SeedConsentedState()
        {
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();
            _roomSettingsDraftScope = RoomSettingsDraftScope.Redirect();

            var store = ConsentGate.CreateDefaultStore();
            var requiredTerms = TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, DateTime.UtcNow);
        }

        [TearDown]
        public void RestoreConsentFile()
        {
            // テスト用 seam を確実に戻す（PR #114 再レビュー LOW-7）。
            // 途中で失敗しても次のテストへ持ち越さないよう、明示的な後始末はここへ寄せる。
            if (_suppressedQuestionShownSession != null)
            {
                _suppressedQuestionShownSession.SuppressDistributedQuestionShownForTests = false;
                _suppressedQuestionShownSession = null;
            }

            _consentScope?.Restore();
            _roomSettingsDraftScope?.Restore();
            _roomSettingsDraftScope = null;
        }

        [UnityTearDown]
        public IEnumerator TearDownSceneAndNetwork()
        {
            // NGO の Shutdown() はその場では止まらずフレーム終端で実処理が走る（docs/network.md §2.4）。
            // 実際に接続済みのクライアントがいる状態（本テストはフル接続まで進める）で停止完了を待たずに
            // DestroyImmediate すると、Transport の二重シャットダウンで
            // ObjectDisposedException（NativeList 解放済み）が飛び、テストが失敗扱いになる
            // （GameSessionTestFixture.TearDown と同じ理由・同じ対策）。
            var bootService = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.Service : null;
            bootService?.Stop();
            _hostService?.Stop();

            yield return WaitWhile(
                () => IsNetworkBusy(bootService) || IsNetworkBusy(_hostService),
                $"{DefaultTimeoutSeconds} 秒待ってもホスト / クライアントの停止が完了しませんでした。");

            // 停止処理の後始末（キューの吐き出し）が完了するよう 1 フレーム余分に回す。
            yield return null;

            _hostService?.Dispose();
            _hostService = null;
            _hostSession = null;

            if (_hostObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostObject);
                _hostObject = null;
            }

            _hostManager = null;

            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();

            TearDownMainSceneAndBootstrapSingletons();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator GameView_FullFlow_SpaceBuzz_AnswerCorrect_ShowsResult()
        {
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(r => panelRoot = r);

            var elements = new GameFlowElements();
            yield return ConnectClientAndReachAnswering(panelRoot, elements);

            Assert.IsFalse(elements.BuzzButton.enabledSelf, "押下後はボタンがロック表示（無効）になるはず。");
            Assert.IsFalse(string.IsNullOrEmpty(elements.BuzzResultLabel.text), "早押しの勝者表示が出るはず。");
            StringAssert.Contains("あなた", elements.BuzzResultLabel.text, "自分が勝者なので「あなた」が表示されるはず。");

            // --- 回答送信（カタカナ入力でも正規化して正解になる） ---
            elements.AnswerField.value = "トウキョウ";
            yield return SimulateClickRoutine(elements.AnswerSubmitButton);

            yield return WaitUntil(
                () => elements.ClientSession.Phase.Value == QuizPhase.Result,
                DefaultTimeoutSeconds,
                () => $"結果フェーズに進みませんでした（現在: {elements.ClientSession.Phase.Value}）。");

            yield return WaitUntil(
                () => !string.IsNullOrEmpty(elements.ResultLabel.text),
                DefaultTimeoutSeconds,
                "正誤結果が表示されませんでした。");

            StringAssert.Contains("正解", elements.ResultLabel.text);
            StringAssert.Contains(CorrectAnswer, elements.ResultLabel.text);
            // レビュー M-7: 自分が回答者なので「あなた」表記・得点（合計）が含まれるはず。
            StringAssert.Contains("あなた", elements.ResultLabel.text);
            StringAssert.Contains("合計", elements.ResultLabel.text);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator GameView_EnterKeyOnAnswerField_SubmitsAnswer()
        {
            // レビュー M-11: 送信ボタンのクリックではなく、回答欄での Enter 押下で送信できることを確認する。
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(r => panelRoot = r);

            var elements = new GameFlowElements();
            yield return ConnectClientAndReachAnswering(panelRoot, elements);

            elements.AnswerField.value = "トウキョウ";
            using (var keyDownEvent = KeyDownEvent.GetPooled('\r', KeyCode.Return, EventModifiers.None))
            {
                keyDownEvent.target = elements.AnswerField;
                elements.AnswerField.SendEvent(keyDownEvent);
            }

            yield return WaitUntil(
                () => elements.ClientSession.Phase.Value == QuizPhase.Result,
                DefaultTimeoutSeconds,
                () => $"結果フェーズに進みませんでした（現在: {elements.ClientSession.Phase.Value}）。Enter 送信が効いていない可能性があります。");

            yield return WaitUntil(
                () => !string.IsNullOrEmpty(elements.ResultLabel.text),
                DefaultTimeoutSeconds,
                "正誤結果が表示されませんでした。");

            StringAssert.Contains("正解", elements.ResultLabel.text);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator GameView_HostPath_ActiveGameSessionAndNextButton_FinishesSession()
        {
            // レビュー H-1: ホスト自身の Game View 経路を確認する。2 プロセスでの手動確認は #15 で行う。
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(r => panelRoot = r);

            var hostService = NetworkBootstrap.Instance.Service;
            NetworkStartResult startResult = default;
            yield return hostService.StartHostWhenReady(startPort: 0, onCompleted: r => startResult = r);
            Assert.IsTrue(startResult.Success, startResult.Message);

            var hostSession = hostService.ActiveGameSession;
            Assert.IsNotNull(hostSession, "ホスト開始時に GameSession がスポーンされるはず。");
            hostSession.Configure(new TestQuestionSource(TestQuestionSource.FreeText("q-test-1", QuestionText, CorrectAnswer)));

            // ActiveGameSession 分岐（ホストのキャッシュ済み高速経路）そのものを確認する。
            Assert.AreSame(
                hostSession, hostService.FindActiveGameSession(),
                "ホストでは FindActiveGameSession が ActiveGameSession をそのまま返すはず。");

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Game);

            Label questionTextLabel = null;
            yield return WaitForElement<Label>(panelRoot, "question-text-label", found => questionTextLabel = found);
            Button buzzButton = null;
            yield return WaitForElement<Button>(panelRoot, "buzz-button", found => buzzButton = found);
            TextField answerField = null;
            yield return WaitForElement<TextField>(panelRoot, "answer-field", found => answerField = found);
            Button answerSubmitButton = null;
            yield return WaitForElement<Button>(panelRoot, "answer-submit-button", found => answerSubmitButton = found);
            Label resultLabel = null;
            yield return WaitForElement<Label>(panelRoot, "result-label", found => resultLabel = found);
            VisualElement hostControls = null;
            yield return WaitForElement<VisualElement>(panelRoot, "host-controls", found => hostControls = found);
            Button nextButton = null;
            yield return WaitForElement<Button>(panelRoot, "next-button", found => nextButton = found);

            Assert.IsTrue(hostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => hostSession.Phase.Value == QuizPhase.BuzzOpen,
                DefaultTimeoutSeconds,
                () => $"早押し受付が開きませんでした（現在: {hostSession.Phase.Value}）。");

            yield return WaitUntil(
                () => questionTextLabel.text == QuestionText,
                DefaultTimeoutSeconds,
                "問題文が Game View に表示されませんでした。");

            // #206: 問題データ（問題文・正解）と回答者の名前を出す表示欄は、平文として表示する（タグを解釈しない）。
            Assert.IsFalse(questionTextLabel.enableRichText, "問題文はタグを解釈しないはず。");
            Assert.IsFalse(panelRoot.Q<Label>("question-text-sizer").enableRichText, "問題文の高さの先取り用の欄も平文のはず。");
            Assert.IsFalse(resultLabel.enableRichText, "結果の表示（正解・回答者の名前）はタグを解釈しないはず。");

            // ホスト自身もプレイヤーとして早押しボタンをクリックする（ホストの SE 経路も併せて通す）。
            yield return SimulateClickRoutine(buzzButton);

            yield return WaitUntil(
                () => hostSession.Phase.Value == QuizPhase.Answering,
                DefaultTimeoutSeconds,
                () => $"回答フェーズに進みませんでした（現在: {hostSession.Phase.Value}）。");

            answerField.value = "トウキョウ";
            yield return SimulateClickRoutine(answerSubmitButton);

            yield return WaitUntil(
                () => hostSession.Phase.Value == QuizPhase.Result,
                DefaultTimeoutSeconds,
                () => $"結果フェーズに進みませんでした（現在: {hostSession.Phase.Value}）。");

            yield return WaitUntil(
                () => !string.IsNullOrEmpty(resultLabel.text),
                DefaultTimeoutSeconds,
                "正誤結果が表示されませんでした。");

            // ホストの「次へ」ボタン。
            yield return WaitUntil(
                () => hostControls.resolvedStyle.display == DisplayStyle.Flex,
                DefaultTimeoutSeconds,
                "ホストの「次へ」ボタンが表示されませんでした。");

            yield return SimulateClickRoutine(nextButton);

            yield return WaitUntil(
                () => hostSession.Phase.Value == QuizPhase.Finished,
                DefaultTimeoutSeconds,
                () => $"終了フェーズに進みませんでした（現在: {hostSession.Phase.Value}）。");

            yield return WaitUntil(
                () => router.CurrentViewName == ViewNames.Result,
                DefaultTimeoutSeconds,
                "「次へ」クリック後に Result（プレースホルダ）へ遷移しませんでした。実際: " + router.CurrentViewName);
        }

        /// <summary>
        /// Game View を開いたときに問題提示（<see cref="GameSession.QuestionShown"/>）を既に
        /// 取りこぼしていても、配信済みの DTO から問題文が復元されること（#95、PR #104 レビュー M-C）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// ロビーから Game View へ切り替わるのと問題配信の到着が重なると、View が購読する前に
        /// <c>QuestionShown</c> が飛んでしまい、問題文が空のまま残ることが実機の 2 プロセス確認で再現した。
        /// 本テストは「出題が始まってから Game View を開く」という決定的な順序で同じ状況を作る。
        /// </para>
        /// <para>
        /// 復元は <c>GameView.TryAcquireSession</c>（表示直後）と <c>GameView.Tick</c>
        /// （<c>QuestionIndex</c> の同期が 1 フレーム遅れた場合）の両方で効く。
        /// どちらが先に成立するかは <c>NetworkVariable</c> のデルタと RPC の到着順しだいなので、
        /// テストとしては「最終的に問題文が表示される」ことを確認する。
        /// </para>
        /// </remarks>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator GameView_OpenedAfterQuestionStarted_RestoresQuestionFromDeliveredDto()
        {
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(r => panelRoot = r);

            var port = StartInProcessHost();

            var clientService = NetworkBootstrap.Instance.Service;
            NetworkStartResult startResult = default;
            yield return clientService.StartClientWhenReady(
                LoopbackAddress, port, "つむぎ", onCompleted: r => startResult = r);
            Assert.IsTrue(startResult.Success, startResult.Message);

            GameSession clientSession = null;
            yield return WaitUntil(
                () => (clientSession = clientService.FindActiveGameSession()) != null,
                DefaultTimeoutSeconds,
                "クライアント側に GameSession が見つかりませんでした。");

            // Game View を表示する前に出題を始める（この時点では GameView が居ないので
            // QuestionShown を受け取る購読者が居ない＝取りこぼした状態になる）。
            Assert.IsTrue(_hostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => clientSession.Distributor != null
                      && clientSession.Distributor.TryGetQuestion(0, out _),
                DefaultTimeoutSeconds,
                "クライアントへ問題が配信されませんでした。");

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Game);

            Label questionTextLabel = null;
            yield return WaitForElement<Label>(panelRoot, "question-text-label", found => questionTextLabel = found);
            yield return WaitUntil(
                () => questionTextLabel.text == QuestionText,
                DefaultTimeoutSeconds,
                () => $"取りこぼした問題文が復元されませんでした（実際: '{questionTextLabel.text}'）。");

            var questionIndexLabel = panelRoot.Q<Label>("question-index-label");
            Assert.IsNotNull(questionIndexLabel, "question-index-label が見つかりません。");
            StringAssert.Contains("1", questionIndexLabel.text, "問題番号も復元されるはず。");
        }

        /// <summary>
        /// 2 問目の提示（<see cref="GameSession.QuestionShown"/>）を取りこぼしても、
        /// 配信済みの DTO から問題文が<b>差し替わる</b>こと（PR #104 レビュー L-2 の回帰、#109）。
        /// </summary>
        /// <remarks>
        /// 取りこぼし復元の入口条件が「まだ何も表示していないか」だったころは、
        /// 1 問目を表示済みの View が 2 問目の合図を落とすと前問の問題文が残り続けた。
        /// 現在は「表示中の問題インデックス != <c>GameSession.QuestionIndex</c>」で判定する。
        /// 取りこぼしは <c>SuppressDistributedQuestionShownForTests</c>（通常配信の発火だけを止める
        /// テスト用 seam）で決定的に作る。
        /// </remarks>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator GameView_MissingSecondQuestionShown_SwitchesQuestionTextFromDeliveredDto()
        {
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(r => panelRoot = r);

            var source = new TestQuestionSource(
                TestQuestionSource.FreeText("q-test-1", QuestionText, CorrectAnswer),
                TestQuestionSource.FreeText("q-test-2", SecondQuestionText, SecondCorrectAnswer));
            var port = StartInProcessHost(source);

            var clientService = NetworkBootstrap.Instance.Service;
            NetworkStartResult startResult = default;
            yield return clientService.StartClientWhenReady(
                LoopbackAddress, port, "つむぎ", onCompleted: r => startResult = r);
            Assert.IsTrue(startResult.Success, startResult.Message);

            GameSession clientSession = null;
            yield return WaitUntil(
                () => (clientSession = clientService.FindActiveGameSession()) != null,
                DefaultTimeoutSeconds,
                "クライアント側に GameSession が見つかりませんでした。");

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Game);

            Label questionTextLabel = null;
            yield return WaitForElement<Label>(panelRoot, "question-text-label", found => questionTextLabel = found);

            // --- 1 問目は通常どおり表示される ---
            Assert.IsTrue(_hostSession.StartQuestion(0), "1 問目を出題できるはず。");
            yield return WaitUntil(
                () => questionTextLabel.text == QuestionText,
                DefaultTimeoutSeconds,
                () => $"1 問目の問題文が表示されませんでした（実際: '{questionTextLabel.text}'）。");

            // --- ここから先、このクライアントは提示の合図を受け取れない ---
            // 後始末は TearDown（RestoreConsentFile）が行う（PR #114 再レビュー LOW-7）。
            _suppressedQuestionShownSession = clientSession;
            clientSession.SuppressDistributedQuestionShownForTests = true;

            // 1 問目を決着させて結果表示へ（単問モードなので自動進行はしない）。
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.BuzzOpen,
                DefaultTimeoutSeconds,
                () => $"早押し受付が開きませんでした（現在: {clientSession.Phase.Value}）。");
            Assert.IsTrue(clientSession.RequestBuzz(), "受付中なので押下を送れるはず。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Answering,
                DefaultTimeoutSeconds,
                () => $"回答フェーズに進みませんでした（現在: {clientSession.Phase.Value}）。");
            Assert.IsTrue(clientSession.RequestAnswer(CorrectAnswer), "勝者は回答を送れるはず。");
            yield return WaitUntil(
                () => _hostSession.Phase.Value == QuizPhase.Result,
                DefaultTimeoutSeconds,
                () => $"結果表示に進みませんでした（現在: {_hostSession.Phase.Value}）。");

            // --- 2 問目（提示の合図は届かない。DTO だけが配信される） ---
            Assert.IsTrue(_hostSession.StartQuestion(1), "2 問目を出題できるはず。");

            yield return WaitUntil(
                () => questionTextLabel.text == SecondQuestionText,
                DefaultTimeoutSeconds,
                () => "取りこぼした 2 問目の問題文へ差し替わりませんでした"
                      + $"（実際: '{questionTextLabel.text}'）。",
                panelRoot);

            var questionIndexLabel = panelRoot.Q<Label>("question-index-label");
            Assert.IsNotNull(questionIndexLabel, "question-index-label が見つかりません。");
            StringAssert.Contains("2", questionIndexLabel.text, "問題番号も 2 問目に合わせて更新されるはず。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator GameView_ChoiceQuestion_ShowsShuffledChoiceButtons()
        {
            // issue #17: 選択式は早押しを介さないため、ConnectClientAndReachAnswering は使わず
            // 出題 → 選択肢ボタンの表示だけを確認する。
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(r => panelRoot = r);

            // #206: 選択肢は平文として表示する。タグを含む選択肢も、書かれたとおりの文字で出る。
            var choices = new[] { "大阪", "<b>東京</b>", "京都" };
            var questionSource = new TestQuestionSource(
                TestQuestionSource.Choice("q-choice-1", "選択式のテスト問題", 1, choices));
            var port = StartInProcessHost(questionSource);

            var clientService = NetworkBootstrap.Instance.Service;
            NetworkStartResult startResult = default;
            yield return clientService.StartClientWhenReady(
                LoopbackAddress, port, "つむぎ", onCompleted: r => startResult = r);
            Assert.IsTrue(startResult.Success, startResult.Message);

            GameSession clientSession = null;
            yield return WaitUntil(
                () => (clientSession = clientService.FindActiveGameSession()) != null,
                DefaultTimeoutSeconds,
                "クライアント側に GameSession が見つかりませんでした。");

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Game);

            VisualElement choiceSection = null;
            yield return WaitForElement<VisualElement>(panelRoot, "choice-section", found => choiceSection = found);
            VisualElement choiceButtonsContainer = null;
            yield return WaitForElement<VisualElement>(
                panelRoot, "choice-buttons-container", found => choiceButtonsContainer = found);

            Assert.IsTrue(_hostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.ChoiceAnswering,
                DefaultTimeoutSeconds,
                () => $"選択式の回答受付が開きませんでした（現在: {clientSession.Phase.Value}）。");

            yield return WaitUntil(
                () => choiceButtonsContainer.childCount == choices.Length,
                DefaultTimeoutSeconds,
                () => $"選択肢ボタンが {choices.Length} 個表示されませんでした（実際: {choiceButtonsContainer.childCount} 個）。");

            Assert.AreEqual(DisplayStyle.Flex, choiceSection.resolvedStyle.display, "選択肢セクションが表示されるはず。");

            var shownTexts = new List<string>();
            for (var i = 0; i < choiceButtonsContainer.childCount; i++)
            {
                var button = choiceButtonsContainer[i] as Button;
                Assert.IsNotNull(button, "選択肢は Button として追加されるはず。");
                Assert.IsFalse(button.enableRichText, "#206: 選択肢はタグを解釈しないはず。");
                shownTexts.Add(button.text);
            }

            CollectionAssert.AreEquivalent(choices, shownTexts, "表示順がシャッフルされても、選択肢の集合は変わらないはず。");
        }

        /// <summary>
        /// #132 レビュー M2-2: 選択式の問題では、司会専用モードでない一般プレイヤーでも
        /// 早押しセクション（<c>buzz-section</c>）を出さないことを実シーンで確認する
        /// （確定仕様 K18「選択式は早押しなし・時間切れで一斉判定」。縦幅の節約も兼ねる）。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator GameView_ChoiceQuestion_HidesBuzzSection_ForNormalPlayer()
        {
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(r => panelRoot = r);

            var choices = new[] { "大阪", "東京", "京都" };
            var questionSource = new TestQuestionSource(
                TestQuestionSource.Choice("q-choice-buzz-hidden", "選択式のテスト問題", 1, choices));
            var port = StartInProcessHost(questionSource);

            var clientService = NetworkBootstrap.Instance.Service;
            NetworkStartResult startResult = default;
            yield return clientService.StartClientWhenReady(
                LoopbackAddress, port, "つむぎ", onCompleted: r => startResult = r);
            Assert.IsTrue(startResult.Success, startResult.Message);

            GameSession clientSession = null;
            yield return WaitUntil(
                () => (clientSession = clientService.FindActiveGameSession()) != null,
                DefaultTimeoutSeconds,
                "クライアント側に GameSession が見つかりませんでした。");

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Game);

            VisualElement buzzSection = null;
            yield return WaitForElement<VisualElement>(panelRoot, "buzz-section", found => buzzSection = found);
            VisualElement choiceSection = null;
            yield return WaitForElement<VisualElement>(panelRoot, "choice-section", found => choiceSection = found);

            Assert.IsTrue(_hostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.ChoiceAnswering,
                DefaultTimeoutSeconds,
                () => $"選択式の回答受付が開きませんでした（現在: {clientSession.Phase.Value}）。");

            yield return WaitUntil(
                () => buzzSection.resolvedStyle.display == DisplayStyle.None,
                DefaultTimeoutSeconds,
                () => "選択式では早押しセクションを隠すはず（実際: "
                      + $"{buzzSection.resolvedStyle.display}）。");

            Assert.AreEqual(
                DisplayStyle.Flex, choiceSection.resolvedStyle.display, "選択肢セクションは表示されるはず。");
        }

        /// <summary>
        /// #132 レビュー M2-2: 判定結果セクション（<c>result-section</c>）が、判定が出る前は隠れていて
        /// <see cref="QuizPhase.Result"/> で現れることを実シーンで確認する
        /// （得点表示は <c>game-header-row</c> へ移したため、隠れている間も得点は見える）。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator GameView_ResultSection_HiddenBeforeJudgement_AndShownAfter()
        {
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(r => panelRoot = r);

            var hostService = NetworkBootstrap.Instance.Service;
            NetworkStartResult startResult = default;
            yield return hostService.StartHostWhenReady(startPort: 0, onCompleted: r => startResult = r);
            Assert.IsTrue(startResult.Success, startResult.Message);

            var hostSession = hostService.ActiveGameSession;
            Assert.IsNotNull(hostSession, "ホスト開始時に GameSession がスポーンされるはず。");
            hostSession.Configure(
                new TestQuestionSource(TestQuestionSource.FreeText("q-result-section", QuestionText, CorrectAnswer)));

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Game);

            VisualElement resultSection = null;
            yield return WaitForElement<VisualElement>(panelRoot, "result-section", found => resultSection = found);
            Label scoreLabel = null;
            yield return WaitForElement<Label>(panelRoot, "score-label", found => scoreLabel = found);
            Button buzzButton = null;
            yield return WaitForElement<Button>(panelRoot, "buzz-button", found => buzzButton = found);
            TextField answerField = null;
            yield return WaitForElement<TextField>(panelRoot, "answer-field", found => answerField = found);
            Button answerSubmitButton = null;
            yield return WaitForElement<Button>(panelRoot, "answer-submit-button", found => answerSubmitButton = found);

            Assert.IsTrue(hostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => hostSession.Phase.Value == QuizPhase.BuzzOpen,
                DefaultTimeoutSeconds,
                () => $"早押し受付が開きませんでした（現在: {hostSession.Phase.Value}）。");

            Assert.AreEqual(
                DisplayStyle.None,
                resultSection.resolvedStyle.display,
                "判定前は結果セクションを隠すはず。");
            Assert.AreEqual(
                DisplayStyle.Flex,
                scoreLabel.resolvedStyle.display,
                "得点はヘッダーに移したので、結果セクションが隠れていても見えるはず。");

            yield return SimulateClickRoutine(buzzButton);

            yield return WaitUntil(
                () => hostSession.Phase.Value == QuizPhase.Answering,
                DefaultTimeoutSeconds,
                () => $"回答フェーズに進みませんでした（現在: {hostSession.Phase.Value}）。");

            answerField.value = CorrectAnswer;
            yield return SimulateClickRoutine(answerSubmitButton);

            yield return WaitUntil(
                () => hostSession.Phase.Value == QuizPhase.Result,
                DefaultTimeoutSeconds,
                () => $"結果フェーズに進みませんでした（現在: {hostSession.Phase.Value}）。");

            yield return WaitUntil(
                () => resultSection.resolvedStyle.display == DisplayStyle.Flex,
                DefaultTimeoutSeconds,
                () => "判定後は結果セクションを表示するはず（実際: "
                      + $"{resultSection.resolvedStyle.display}）。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator GameView_ChoiceQuestion_ClickWrongChoice_ReportsOriginalIndexAndHighlights()
        {
            // レビュー M6: 表示順のシャッフル後にクリックした「元インデックス」が正しくサーバーへ送られ、
            // ChoiceResolved で自分の選択が不正解として返ってくることを確認する。
            // レビュー M2: 判定後にボタンが無効化（SetEnabled(false)）されても、
            // --correct / --wrong のハイライト色が Button:disabled に負けていないことも確認する。
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(r => panelRoot = r);

            // 8 択にすることで、表示順のシャッフルが恒等（シャッフル前と同じ並び）になる確率を
            // 1/8!（≈ 0.0025%）まで下げ、「クリックした元インデックスの復元」を実質的に意味のある
            // 形で検証できるようにする。
            var choices = new[] { "A", "B", "C", "D", "E", "F", "G", "H" };
            const int correctIndex = 0;
            // レビュー R-4: 判定後の状態（ハイライト・disabled）まで確かめるテストなので、
            // 締切を短くして無駄なテスト時間を減らす。
            var shortChoiceLimits = new QuizTimeLimits(
                buzzTimeLimitSec: 10.0, answerTimeLimitSec: 10.0, choiceTimeLimitSec: 2.0, collectWindowSec: 0.15);
            var questionSource = new TestQuestionSource(
                TestQuestionSource.Choice("q-choice-2", "選択式のテスト問題2", correctIndex, choices));
            var port = StartInProcessHost(questionSource, shortChoiceLimits);

            var clientService = NetworkBootstrap.Instance.Service;
            NetworkStartResult startResult = default;
            yield return clientService.StartClientWhenReady(
                LoopbackAddress, port, "つむぎ", onCompleted: r => startResult = r);
            Assert.IsTrue(startResult.Success, startResult.Message);

            GameSession clientSession = null;
            yield return WaitUntil(
                () => (clientSession = clientService.FindActiveGameSession()) != null,
                DefaultTimeoutSeconds,
                "クライアント側に GameSession が見つかりませんでした。");

            var localClientId = clientSession.NetworkManager.LocalClientId;

            // production の ChoiceShuffle と同じ計算で、実際に恒等でない表示順になることを確認する
            // （8 択なので実用上ほぼ確実に非恒等になるが、万一に備えて明示的に確認する）。
            var expectedOrder = ChoiceShuffle.BuildDisplayOrder(
                choices.Length, ChoiceShuffle.DefaultShuffleDisplay, ChoiceShuffle.BuildSeed(0, localClientId));
            var isIdentity = true;
            for (var i = 0; i < expectedOrder.Length; i++)
            {
                if (expectedOrder[i] != i)
                {
                    isIdentity = false;
                    break;
                }
            }
            Assert.IsFalse(isIdentity, "このテストは表示順が恒等でないことを前提にする（8択なら実用上ほぼ発生しない）。");

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Game);

            VisualElement choiceButtonsContainer = null;
            yield return WaitForElement<VisualElement>(
                panelRoot, "choice-buttons-container", found => choiceButtonsContainer = found);

            Assert.IsTrue(_hostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => choiceButtonsContainer.childCount == choices.Length,
                DefaultTimeoutSeconds,
                () => $"選択肢ボタンが表示されませんでした（{choiceButtonsContainer.childCount} 個）。");

            // ボタンは問題提示（Reading）と同時に作られるが、クリックしても送信されるのは
            // ChoiceAnswering フェーズに入ってから（GameViewPresenter.IsChoiceButtonInteractable）。
            // これを待たずにクリックすると無効化されたボタンを押すことになり、何も送信されない。
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.ChoiceAnswering,
                DefaultTimeoutSeconds,
                () => $"選択式の回答受付が開きませんでした（現在: {clientSession.Phase.Value}）。");

            // 正解ではない選択肢ボタンを 1 つ選ぶ（表示上の位置ではなく文字列で判定するため、
            // 実際の表示順に関わらずどのボタンが不正解かを確実に特定できる）。
            Button wrongButton = null;
            var wrongOriginalIndex = -1;
            for (var i = 0; i < choiceButtonsContainer.childCount; i++)
            {
                var candidate = choiceButtonsContainer[i] as Button;
                Assert.IsNotNull(candidate, "選択肢は Button として追加されるはず。");
                if (candidate.text != choices[correctIndex])
                {
                    wrongButton = candidate;
                    wrongOriginalIndex = Array.IndexOf(choices, candidate.text);
                    break;
                }
            }

            Assert.IsNotNull(wrongButton, "不正解の選択肢ボタンが見つかるはず。");
            Assert.AreNotEqual(-1, wrongOriginalIndex);

            int? resolvedCorrectIndex = null;
            ChoiceAnswerEntry? ownEntry = null;
            clientSession.ChoiceResolved += (correct, entries) =>
            {
                resolvedCorrectIndex = correct;
                foreach (var entry in entries)
                {
                    if (entry.ClientId == localClientId)
                    {
                        ownEntry = entry;
                    }
                }
            };

            yield return SimulateClickRoutine(wrongButton);

            yield return WaitUntil(
                () => ownEntry.HasValue,
                DefaultTimeoutSeconds,
                "自分の選択結果（ChoiceResolved）が届きませんでした。");

            Assert.AreEqual(correctIndex, resolvedCorrectIndex, "正解インデックスは元の choices 配列のものであるはず。");
            Assert.IsFalse(ownEntry.Value.IsCorrect, "不正解の選択肢を押したので不正解になるはず。");
            Assert.AreEqual(
                wrongOriginalIndex,
                ownEntry.Value.ChoiceIndex,
                "サーバーへ送る index はクリックしたボタンの表示位置ではなく元 index であるはず。");

            // レビュー M2: 判定後はボタンが無効化されるが、不正解のハイライト色は
            // Button:disabled の既定色（--color-disabled）に負けずに残るはず。
            // レビュー R-3: theme.uss の --color-error の値をテスト側に固定値として二重管理しない。
            // 「無効化の既定色と異なる」ことだけを確かめれば、--color-wrong 側の具体的な色を
            // 変更してもテストが追随できる。
            // #132: --color-disabled は素材由来のライト基調パレットに合わせて #ded0c2 に変更した。
            ColorUtility.TryParseHtmlString("#ded0c2", out var disabledColor);
            yield return WaitUntil(
                () => !wrongButton.enabledSelf,
                DefaultTimeoutSeconds,
                "判定後は選択肢ボタンが無効化されるはず。");
            yield return WaitUntil(
                () => !ColorsApproximatelyEqual(wrongButton.resolvedStyle.backgroundColor, disabledColor),
                DefaultTimeoutSeconds,
                () => "無効化後も不正解のハイライト色（Button:disabled の既定色とは別の色）が維持されるはず（実際: "
                    + $"{wrongButton.resolvedStyle.backgroundColor}）。");
        }

        private static bool ColorsApproximatelyEqual(Color a, Color b, float tolerance = 0.02f) =>
            Mathf.Abs(a.r - b.r) < tolerance
            && Mathf.Abs(a.g - b.g) < tolerance
            && Mathf.Abs(a.b - b.b) < tolerance;

        /// <summary>
        /// クライアント側（Boot → Main）を接続し、Game View を出して出題 → Space 押下（早押し）→
        /// 回答フェーズまで進める。<see cref="GameFlowElements"/> に要素とクライアント側セッションを積む。
        /// </summary>
        private IEnumerator ConnectClientAndReachAnswering(VisualElement panelRoot, GameFlowElements elements)
        {
            // クライアント側（Boot → Main）の SceneManager.LoadSceneAsync(..., LoadSceneMode.Single) は
            // それまでの読み込み済みシーンを（DontDestroyOnLoad を除き）破棄する。ホストの
            // NetworkManager 自体は NGO が自分で DontDestroyOnLoad するため生き残るが、
            // その後 Spawn する GameSession（別 GameObject）はスポーン時点のアクティブシーンに
            // 生成され、DontDestroyOnLoad ではないため、呼び出し側で先にシーン読み込みを済ませてから
            // ホストを起動すること（そうしないと GameSession がシーン切り替えで破棄されてしまう）。
            var port = StartInProcessHost();

            var clientService = NetworkBootstrap.Instance.Service;
            var connected = false;
            Action<ulong> onConnected = _ => connected = true;
            clientService.ClientConnected += onConnected;

            NetworkStartResult startResult = default;
            yield return clientService.StartClientWhenReady(
                LoopbackAddress, port, "つむぎ", onCompleted: r => startResult = r);
            Assert.IsTrue(startResult.Success, startResult.Message);

            yield return WaitUntil(() => connected, DefaultTimeoutSeconds, "クライアントが接続できませんでした。");
            clientService.ClientConnected -= onConnected;

            GameSession clientSession = null;
            yield return WaitUntil(
                () => (clientSession = clientService.FindActiveGameSession()) != null,
                DefaultTimeoutSeconds,
                "クライアント側に GameSession が見つかりませんでした。");
            elements.ClientSession = clientSession;

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Game);

            yield return WaitForElement<Label>(panelRoot, "question-text-label", found => elements.QuestionTextLabel = found);
            yield return WaitForElement<Button>(panelRoot, "buzz-button", found => elements.BuzzButton = found);
            yield return WaitForElement<Label>(panelRoot, "buzz-result-label", found => elements.BuzzResultLabel = found);
            yield return WaitForElement<VisualElement>(panelRoot, "answer-section", found => elements.AnswerSection = found);
            yield return WaitForElement<TextField>(panelRoot, "answer-field", found => elements.AnswerField = found);
            yield return WaitForElement<Button>(panelRoot, "answer-submit-button", found => elements.AnswerSubmitButton = found);
            yield return WaitForElement<Label>(panelRoot, "result-label", found => elements.ResultLabel = found);

            // --- 出題（ホストを直接操作。ロビー導線は #7 未実装のため対象外） ---
            Assert.IsTrue(_hostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.BuzzOpen,
                DefaultTimeoutSeconds,
                () => $"早押し受付が開きませんでした（現在: {clientSession.Phase.Value}）。");

            yield return WaitUntil(
                () => elements.QuestionTextLabel.text == QuestionText,
                DefaultTimeoutSeconds,
                "問題文が Game View に表示されませんでした。実際: " + elements.QuestionTextLabel.text);

            Assert.IsTrue(elements.BuzzButton.enabledSelf, "早押し受付中はボタンが有効なはず。");

            // --- Space キーで早押し（受け入れ条件「Space キーで早押しできる」） ---
            Press(_keyboard.spaceKey);
            yield return null;
            Release(_keyboard.spaceKey);

            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Answering,
                DefaultTimeoutSeconds,
                () => $"回答フェーズに進みませんでした（現在: {clientSession.Phase.Value}）。");

            yield return WaitUntil(
                () => elements.AnswerSection.resolvedStyle.display == DisplayStyle.Flex,
                DefaultTimeoutSeconds,
                "回答入力欄が表示されませんでした。");
        }

        /// <summary>
        /// 1 プロセス内にもう 1 つ <see cref="NetworkManager"/> を立て、指定した問題（既定は freeText 1 問、
        /// <see cref="QuestionText"/> / <see cref="CorrectAnswer"/>）で <see cref="GameSession"/> を
        /// Configure したホストとして開始する（<see cref="JoinViewSceneTests"/> と同じ手順）。
        /// </summary>
        /// <param name="questionSource">
        /// 出題する問題の供給元。null なら freeText 1 問（<see cref="QuestionText"/> / <see cref="CorrectAnswer"/>）。
        /// 選択式（issue #17）の確認では <see cref="TestQuestionSource.Choice"/> を渡す。
        /// </param>
        /// <param name="limits">制限時間。null なら docs/room-settings.md の既定値。</param>
        private ushort StartInProcessHost(IQuestionSource questionSource = null, QuizTimeLimits limits = null)
        {
            _hostObject = new GameObject(nameof(GameViewSceneTests) + "-Host");
            _hostObject.SetActive(false);

            var transport = _hostObject.AddComponent<UnityTransport>();
            transport.MaxPayloadSize = NetworkConstants.MaxPayloadSizeBytes;

            _hostManager = _hostObject.AddComponent<NetworkManager>();
            _hostManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = null,
            };

            // Boot シーンの NetworkManager は DefaultNetworkPrefabs.asset 経由で GameSession /
            // LobbyState を登録済みなので、同プロセスのホスト側にも同じ登録をして構成をそろえる
            // （#13/#7、ForceSamePrefabs。登録漏れがあると接続設定ハッシュが一致せず拒否される）。
            _hostManager.AddNetworkPrefab(NetworkTestPrefabs.LoadGameSession());
            _hostManager.AddNetworkPrefab(NetworkTestPrefabs.LoadLobbyState());

            _hostObject.SetActive(true);

            _hostService = new NetworkService(_hostManager);
            var result = _hostService.StartHost(startPort: 0);
            Assert.IsTrue(result.Success, result.Message);

            _hostSession = _hostService.ActiveGameSession;
            Assert.IsNotNull(_hostSession, "ホスト開始時に GameSession がスポーンされるはず。");
            _hostSession.Configure(
                questionSource ?? new TestQuestionSource(TestQuestionSource.FreeText("q-test-1", QuestionText, CorrectAnswer)),
                limits: limits);

            return _hostService.ActivePort;
        }

        private static bool IsNetworkBusy(NetworkService service)
            => service != null && (service.IsListening || service.IsClient || service.IsShutdownInProgress);

        /// <summary>
        /// 条件が満たされなくなるまで待つ（issue #73 レビュー H2: <see cref="WaitUntil"/> と同じく、
        /// <c>Time.unscaledDeltaTime</c> の積算ではなく <c>Time.realtimeSinceStartupAsDouble</c> の
        /// 差分（デッドライン方式）＋最低 <see cref="MinPollCount"/> 回のポーリングで判定する）。
        /// </summary>
        private static IEnumerator WaitWhile(Func<bool> condition, string failureMessage)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + DefaultTimeoutSeconds;
            var polls = 0;
            var conditionMet = condition();
            while (conditionMet && (Time.realtimeSinceStartupAsDouble < deadline || polls < MinPollCount))
            {
                polls++;
                yield return null;
                conditionMet = condition();
            }

            if (conditionMet)
            {
                Debug.LogError(failureMessage);
                Assert.Fail(failureMessage);
            }
        }
    }
}
