using System;
using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Network.Nat;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.Tests.Shared.Network.Nat;
using TsumugiQuiz.Tests.PlayMode.UI.HostSetup;
using TsumugiQuiz.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI.Game
{
    /// <summary>
    /// 司会専用モード（<c>host.role == "moderator"</c>）の Game View 表示を確かめる PlayMode テスト（#20、M6）。
    /// Main シーン + 実プレハブで、司会専用モードのホストが早押し・回答 UI（<c>buzz-section</c>）の代わりに
    /// 司会操作パネル（<c>moderator-controls-container</c>）を見ることを確認する。
    /// </summary>
    public class GameViewModeratorSceneTests
    {
        private const string HostPlayerName = "モデレーター";

        private ConsentFileScope _consentScope;
        private HostSetupPreferencesScope _preferencesScope;

        [SetUp]
        public void SeedConsentedStateAndBackupPreferences()
        {
            _consentScope = ConsentFileScope.Backup();

            var store = ConsentGate.CreateDefaultStore();
            var requiredTerms = TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, DateTime.UtcNow);

            _preferencesScope = HostSetupPreferencesScope.Backup();
        }

        [TearDown]
        public void RestoreConsentFileAndPreferences()
        {
            _consentScope?.Restore();
            _preferencesScope?.Restore();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // ホスト停止はアンロードより先に行う（アンロード中も通信が走り続けるのを避けるため）。
            var bootstrap = NetworkBootstrap.Instance;
            bootstrap?.Service?.Stop();

            // #101 / #108 レビュー M-2: 先に Main シーンをアンロードして表示中の View を畳んでから
            // シングルトン（NetworkBootstrap = NetworkService）を破棄する。逆順にすると、
            // View のコールバックが破棄済みのサービスを触りうる（GameViewSceneTests と同じ順序）。
            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();

            if (bootstrap != null)
            {
                UnityEngine.Object.DestroyImmediate(bootstrap.gameObject);
            }

            var sePlayer = SePlayer.Instance;
            if (sePlayer != null)
            {
                UnityEngine.Object.DestroyImmediate(sePlayer.gameObject);
            }
        }

        [UnityTest]
        [Timeout(90000)]
        public IEnumerator ModeratorHost_ShowsModeratorControls_AndHidesBuzzSection()
        {
            VisualElement panelRoot = null;
            yield return LoadMainThroughBootAndInstallFakes(root => panelRoot = root);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            yield return SimulateClickRoutine(hostButton);

            yield return StartHostingWithAutoPort(panelRoot, moderatorOnly: true);

            var gameSession = NetworkBootstrap.Instance.Service.ActiveGameSession;
            Assert.IsNotNull(gameSession, "ホスト開始で GameSession がスポーンされるはず。");
            gameSession.Configure(new TestQuestionSource(TestQuestionSource.FreeText("q-1", "日本の首都はどこ？", "とうきょう")));

            // #95 以降、ロビーの「ゲーム開始」は問題フォルダ（Documents 配下）を読み直して
            // Configure をやり直すため、ここで仕込んだ TestQuestionSource は使われない。
            // 本テストの対象はロビーの開始経路ではない（それは LobbyGameStartSceneTests が見る）ので、
            // 出題は単問経路（StartQuestion）で直接始める。Lobby → Game の画面遷移は
            // GameSession.Phase が Lobby 以外になった合図で LobbyView が行う（#95）。
            //
            // 「ゲーム開始」が活性になる＝ LobbyView がロビーの同期を済ませた目安。ここまで待ってから
            // 出題することで、Phase の変化を LobbyView が取りこぼさないようにする（PR #104 レビュー M-5）。
            Button startGameButton = null;
            yield return WaitForElement<Button>(panelRoot, "start-game-button", found => startGameButton = found);
            yield return WaitUntil(
                () => startGameButton.enabledSelf, "ロビーの同期が済めば「ゲーム開始」が有効になるはず。");

            Assert.IsTrue(gameSession.StartQuestion(0), "1 問目の出題を開始できるはず。");

            // --- 司会操作パネルが表示され、早押しセクションは隠れる ---
            VisualElement moderatorControlsContainer = null;
            yield return WaitForElement<VisualElement>(
                panelRoot, "moderator-controls-container", found => moderatorControlsContainer = found);
            yield return WaitUntil(
                () => moderatorControlsContainer.style.display.value == DisplayStyle.Flex,
                "司会操作パネルが表示されるはず。");
            Assert.AreEqual(1, moderatorControlsContainer.childCount, "司会操作パネルの中身が組み込まれているはず。");

            // #144（仕様 5、PR #166 レビュー L-5）: 司会画面は文字送りせず、最初に出た時点で全文。
            Label questionTextLabel = null;
            yield return WaitForElement<Label>(panelRoot, "question-text-label", found => questionTextLabel = found);
            yield return WaitUntil(
                () => !string.IsNullOrEmpty(questionTextLabel.text), "司会画面に問題文が表示されるはず。");
            Assert.AreEqual("日本の首都はどこ？", questionTextLabel.text, "司会画面は最初から全文のはず。");

            var buzzSection = panelRoot.Q<VisualElement>("buzz-section");
            Assert.IsNotNull(buzzSection, "buzz-section が見つかりません。");
            Assert.AreEqual(
                DisplayStyle.None, buzzSection.style.display.value, "司会専用モードでは早押しセクションを隠すはず。");

            var nextButton = panelRoot.Q<Button>("moderator-next-button");
            var pauseButton = panelRoot.Q<Button>("moderator-pause-button");
            var forceCorrectButton = panelRoot.Q<Button>("moderator-force-correct-button");
            var forceWrongButton = panelRoot.Q<Button>("moderator-force-wrong-button");
            Assert.IsNotNull(nextButton);
            Assert.IsNotNull(pauseButton);
            Assert.IsNotNull(forceCorrectButton);
            Assert.IsNotNull(forceWrongButton);

            // host-controls（汎用の「次へ」）は司会操作パネルと重複させないため、結果表示中でも隠れたまま。
            var hostControls = panelRoot.Q<VisualElement>("host-controls");
            Assert.IsNotNull(hostControls);
            Assert.AreEqual(
                DisplayStyle.None, hostControls.style.display.value, "汎用の host-controls は表示しないはず。");

            // #113 レビュー A-2: 司会操作パネル・退出ボタンを含む本文（game-content-row）が
            // ウィンドウ下端からはみ出していないことを実測で担保する（目視確認の代替）。
            // レイアウト解決を待ってから判定する（#105 レビュー M-2 と同じ理由）。
            yield return null;
            yield return null;

            var gameContentRow = panelRoot.Q<VisualElement>("game-content-row");
            Assert.IsNotNull(gameContentRow, "game-content-row が見つかりません。");
            Assert.That(gameContentRow.worldBound.yMax, Is.LessThanOrEqualTo(panelRoot.worldBound.yMax),
                "司会操作パネルを含む本文がウィンドウ下端に収まっているはず（実際: "
                + $"game-content-row.yMax={gameContentRow.worldBound.yMax}, panelRoot.yMax={panelRoot.worldBound.yMax}）。");
        }

        /// <summary>
        /// LOW（PR #81 最終レビュー）: M-A（選択式でも司会専用ホストに選択 UI を出さない）を、
        /// 実際に選択式（choice）の問題で確かめる。上のテスト（freeText のみ）では
        /// <c>choice-section</c> はそもそも常に非表示のため、M-A の回帰確認にはならない。
        /// </summary>
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator ModeratorHost_HidesChoiceSection_ForChoiceQuestion()
        {
            VisualElement panelRoot = null;
            yield return LoadMainThroughBootAndInstallFakes(root => panelRoot = root);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            yield return SimulateClickRoutine(hostButton);

            yield return StartHostingWithAutoPort(panelRoot, moderatorOnly: true);

            var gameSession = NetworkBootstrap.Instance.Service.ActiveGameSession;
            Assert.IsNotNull(gameSession, "ホスト開始で GameSession がスポーンされるはず。");
            gameSession.Configure(new TestQuestionSource(
                TestQuestionSource.Choice("q-choice-1", "日本の首都は？", 1, "おおさか", "とうきょう", "きょうと")));

            // #95 以降、ロビーの「ゲーム開始」は問題フォルダ（Documents 配下）を読み直して
            // Configure をやり直すため、ここで仕込んだ TestQuestionSource は使われない。
            // 本テストの対象はロビーの開始経路ではない（それは LobbyGameStartSceneTests が見る）ので、
            // 出題は単問経路（StartQuestion）で直接始める。Lobby → Game の画面遷移は
            // GameSession.Phase が Lobby 以外になった合図で LobbyView が行う（#95）。
            //
            // 「ゲーム開始」が活性になる＝ LobbyView がロビーの同期を済ませた目安。ここまで待ってから
            // 出題することで、Phase の変化を LobbyView が取りこぼさないようにする（PR #104 レビュー M-5）。
            Button startGameButton = null;
            yield return WaitForElement<Button>(panelRoot, "start-game-button", found => startGameButton = found);
            yield return WaitUntil(
                () => startGameButton.enabledSelf, "ロビーの同期が済めば「ゲーム開始」が有効になるはず。");

            Assert.IsTrue(gameSession.StartQuestion(0), "1 問目の出題を開始できるはず。");

            yield return WaitUntilWithTimeout(
                () => gameSession.Phase.Value == QuizPhase.ChoiceAnswering,
                $"選択式の受付が開きませんでした（{gameSession.Phase.Value}）。");

            var choiceSection = panelRoot.Q<VisualElement>("choice-section");
            Assert.IsNotNull(choiceSection, "choice-section が見つかりません。");
            Assert.AreEqual(
                DisplayStyle.None, choiceSection.style.display.value,
                "司会専用モードでは選択式のセクションも隠すはず（M-A）。");
        }

        /// <summary>
        /// このテストクラス専用の実時間ベースの待機（既定 20 秒、<c>ResultViewSceneTests</c> と同じ作法）。
        /// 問題配信の受信確認〔Ack〕を挟むフェーズ遷移は、UI 要素の出現待ちより余裕を見る必要があるため。
        /// </summary>
        internal static IEnumerator WaitUntilWithTimeout(
            Func<bool> condition, string failureMessage, float timeoutSeconds = 20f)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < timeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsTrue(condition(), failureMessage);
        }

        /// <summary>Boot → Main を読み込み、NAT まわりを偽物に差し替える（#194 の参加者パネルのテストからも使う）。</summary>
        internal static IEnumerator LoadMainThroughBootAndInstallFakes(Action<VisualElement> onRootFound)
        {
            yield return SceneManager.LoadSceneAsync("Boot", LoadSceneMode.Single);

            // #73/#87 で develop 側が MaxWaitFrames（固定フレーム数）から実時間ベースの待機
            // （DefaultTimeoutSeconds + MinPollCount）に置き換えたため、HostSetupFlowTests と
            // 同じ作法に合わせる。
            var sceneDeadline = Time.realtimeSinceStartupAsDouble + DefaultTimeoutSeconds;
            var scenePolls = 0;
            while (SceneManager.GetActiveScene().name != "Main"
                   && (Time.realtimeSinceStartupAsDouble < sceneDeadline || scenePolls < MinPollCount))
            {
                scenePolls++;
                yield return null;
            }

            Assert.AreEqual("Main", SceneManager.GetActiveScene().name, "Main シーンへ遷移しているはず。");

            var uiDocument = FindViewRouterUIDocument();
            var documentDeadline = Time.realtimeSinceStartupAsDouble + DefaultTimeoutSeconds;
            var documentPolls = 0;
            while ((uiDocument == null || uiDocument.rootVisualElement == null)
                   && (Time.realtimeSinceStartupAsDouble < documentDeadline || documentPolls < MinPollCount))
            {
                documentPolls++;
                yield return null;
                uiDocument = FindViewRouterUIDocument();
            }

            Assert.IsNotNull(uiDocument, "ViewRouter の UIDocument が Main シーンに見つかりません。");
            Assert.IsNotNull(uiDocument.rootVisualElement, "UIDocument.rootVisualElement が null のままです。");

            var bootstrap = NetworkBootstrap.Instance;
            Assert.IsNotNull(bootstrap, "NetworkBootstrap が見つかりません。");
            bootstrap.HostConnectivityFactory = () => new HostConnectivityService(
                discovery: new FakeNatDiscovery(device: null),
                lookupClient: new FakeIpLookupClient { DefaultResponse = IpLookupResponse.Ok(200, "203.0.113.9") },
                lanIpProvider: () => "192.168.10.7");

            // #108 レビュー L-R1: ここまでの脱出条件は rootVisualElement != null までで、
            // パネルへのアタッチは待っていない。共通ヘルパーでアタッチを待ち、
            // UI Toolkit のライブリロード（= UIDocument のツリー作り直し）を止める。
            yield return MainSceneTestHelpers.DisableLiveReloadWhenPanelReady(uiDocument);

            onRootFound(uiDocument.rootVisualElement);
        }

        /// <summary>
        /// HostSetup 画面から（<paramref name="moderatorOnly"/> なら司会専用モードで）ホストを開始し、ロビーまで進める
        /// （#194 の参加者パネルのテストからも使う）。
        /// </summary>
        internal static IEnumerator StartHostingWithAutoPort(VisualElement panelRoot, bool moderatorOnly)
        {
            TextField playerNameField = null;
            yield return WaitForElement<TextField>(panelRoot, "player-name-field", found => playerNameField = found);
            IntegerField portField = null;
            yield return WaitForElement<IntegerField>(panelRoot, "port-field", found => portField = found);
            Button startHostButton = null;
            yield return WaitForElement<Button>(panelRoot, "start-host-button", found => startHostButton = found);

            var hostOnlyToggle = panelRoot.Q<Toggle>("host-only-toggle");
            Assert.IsNotNull(hostOnlyToggle, "host-only-toggle が見つかりません。");
            hostOnlyToggle.value = moderatorOnly;

            playerNameField.value = HostPlayerName;
            portField.value = 0;

            yield return SimulateClickRoutine(startHostButton);

            Label internetCodeLabel = null;
            yield return WaitForElement<Label>(panelRoot, "internet-code-label", found => internetCodeLabel = found);
            yield return WaitUntil(
                () => !string.IsNullOrEmpty(internetCodeLabel.text) && internetCodeLabel.text != "-",
                "参加コードが表示されませんでした。");

            Button lobbyButton = null;
            yield return WaitForElement<Button>(panelRoot, "lobby-button", found => lobbyButton = found);
            yield return WaitUntil(() => lobbyButton.enabledSelf, "lobby-button が有効になるはず。");
            yield return SimulateClickRoutine(lobbyButton);

            yield return WaitForElement<VisualElement>(panelRoot, "lobby-player-list", _ => { });
        }
    }
}
