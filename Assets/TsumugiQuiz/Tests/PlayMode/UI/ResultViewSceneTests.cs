using System;
using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Network.Nat;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.Tests.Shared.Network.Nat;
using TsumugiQuiz.Tests.PlayMode.UI.HostSetup;
using TsumugiQuiz.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// Main シーンで Title → HostSetup → ロビー → ゲーム進行 → Result View → ロビーへ戻る、
    /// の一連の流れを確認する PlayMode テスト（issue #20）。
    /// このテストではホスト自身に実際に早押し・回答させる代わりに、司会の強制正解（#20）で判定を進める。
    /// 実ネットワーク・実プレハブ・実際の GameView 遷移を経由する。
    /// </summary>
    public class ResultViewSceneTests
    {
        private const string HostPlayerName = "リザルトホスト";

        private static QuizTimeLimits FastLimits =>
            new QuizTimeLimits(buzzTimeLimitSec: 5.0, answerTimeLimitSec: 5.0, collectWindowSec: 0.15);

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
        public IEnumerator SessionFinished_ShowsRankedResult_ThenReturnsToLobby()
        {
            VisualElement panelRoot = null;
            yield return LoadMainThroughBootAndInstallFakes(root => panelRoot = root);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            yield return SimulateClickRoutine(hostButton);

            yield return StartHostingWithAutoPort(panelRoot);

            var gameSession = NetworkBootstrap.Instance.Service.ActiveGameSession;
            Assert.IsNotNull(gameSession, "ホスト開始で GameSession がスポーンされるはず。");

            // --- Configure してから「ゲーム開始」を押し、実際に GameView（#14）へ遷移させる ---
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

            yield return WaitForElement<Button>(panelRoot, "buzz-button", _ => { });

            // 問題配信の受信確認（Ack）を挟むぶん、UI 要素探索より長めに待つ（docs/network.md §8.4）。
            yield return WaitUntilWithTimeout(
                () => gameSession.Phase.Value == QuizPhase.BuzzOpen,
                $"受付が開きませんでした（{gameSession.Phase.Value}）。");

            Assert.IsTrue(gameSession.RequestBuzz(), "押下を送れるはず。");
            yield return WaitUntilWithTimeout(
                () => gameSession.Phase.Value == QuizPhase.Answering,
                $"回答フェーズに進みませんでした（{gameSession.Phase.Value}）。");

            // このホストは通常モード（host.role == player）なので、強制正解（#20）は
            // 統括判断 M5 により拒否される（司会専用モードのホストのみ許可）。ここでは
            // 通常のプレイヤーとして正解を送る。
            Assert.IsTrue(gameSession.RequestAnswer("とうきょう"), "本人の回答は受理されるはず。");
            yield return WaitUntilWithTimeout(
                () => gameSession.Phase.Value == QuizPhase.Result,
                $"結果フェーズに進みませんでした（{gameSession.Phase.Value}）。");

            Assert.IsTrue(gameSession.FinishSession(), "結果表示から終了できるはず。");

            // --- GameView（#14）が Finished を検知して自動的に Result View へ切り替える ---
            yield return WaitUntilWithTimeout(
                () => gameSession.Phase.Value == QuizPhase.Finished,
                $"Finished になりませんでした（{gameSession.Phase.Value}）。");

            VisualElement rankList = null;
            yield return WaitForElement<VisualElement>(panelRoot, "result-rank-list", found => rankList = found);
            yield return WaitUntilWithTimeout(() => rankList.childCount == 1, "順位表示に 1 行出るはず。");

            var row = rankList[0];
            var rankLabel = row.Q<Label>(className: "result-rank-badge");
            var nameLabel = row.Q<Label>(className: "result-rank-name");
            var scoreLabel = row.Q<Label>(className: "result-rank-score");
            Assert.IsNotNull(rankLabel);
            Assert.IsNotNull(nameLabel);
            Assert.IsNotNull(scoreLabel);
            StringAssert.Contains("1", rankLabel.text);
            Assert.AreEqual(HostPlayerName, nameLabel.text, "ホストの名前がロビー名簿から解決されるはず。");
            Assert.IsFalse(nameLabel.enableRichText, "#206: 名前は参加者が決める文字列なので、タグを解釈しないはず。");
            StringAssert.Contains(ScoreRules.DefaultCorrectPoints.ToString(), scoreLabel.text);

            // このテストは単問経路（GameSession.StartQuestion(0)）で出題しており、
            // TotalQuestions は StartSession 経由でのみ設定される（#19）ため、
            // ここでは要素の存在だけを確かめる（空表示も仕様どおり）。
            var summaryLabel = panelRoot.Q<Label>("result-summary-label");
            Assert.IsNotNull(summaryLabel);

            var returnButton = panelRoot.Q<Button>("result-return-to-lobby-button");
            Assert.IsNotNull(returnButton, "ホストには「ロビーへ戻る」が表示されるはず。");
            Assert.AreEqual(DisplayStyle.Flex, returnButton.style.display.value);

            // --- ロビーへ戻る ---
            yield return SimulateClickRoutine(returnButton);

            VisualElement playerList = null;
            yield return WaitForElement<VisualElement>(panelRoot, "lobby-player-list", found => playerList = found);
            yield return WaitUntil(() => playerList.childCount == 1, "ロビーに戻ったらホストが名簿に 1 件出るはず。");
            Assert.AreEqual(QuizPhase.Lobby, gameSession.ServerPhase, "サーバー側もロビー状態に戻っているはず。");

            // 後片付け。
            Button leaveButton = null;
            yield return WaitForElement<Button>(panelRoot, "leave-button", found => leaveButton = found);
            yield return SimulateClickRoutine(leaveButton);
            yield return WaitForElement<Button>(panelRoot, "host-button", _ => { });
        }

        /// <summary>
        /// 必須 HIGH（PR #81 最終レビュー）: 「もう一度（同じ設定で）」（<c>ResultView.OnRestartClicked</c>）を
        /// 実際の UI 経路で確かめる。修正前は <c>GameSession.StartSession()</c> がホスト自身の
        /// <c>Phase.Value</c> を同期的に書き換えるため、<c>HandlePhaseChanged</c> → <c>ShowView(Game)</c> →
        /// <c>OnHide()</c> で <c>_router</c> が null 化されたあと、<c>OnRestartClicked</c> 側で重ねて呼んでいた
        /// <c>ShowView</c> が <see cref="NullReferenceException"/> を起こしていた。
        /// </summary>
        /// <remarks>
        /// ロビーの「ゲーム開始」（#95）は問題フォルダを読み直して Configure をやり直すため、
        /// このテストが仕込んだ問題・進行設定が使われない。「もう一度」ボタンを表示させる
        /// （＝ <c>ActiveSessionSettings</c> が設定され、設定の引き継ぎが成立する）状態を確実に作るため、
        /// ボタンを経由せず <c>GameSession.StartSession</c> を直接呼ぶ（<c>GameViewSceneTests</c> と同じ作法）。
        /// 続けて呼ぶ <c>ViewRouter.ShowView(ViewNames.Game)</c> は、#95 で <c>LobbyView</c> が
        /// <c>Phase == Reading</c> を見て同じ遷移を行うようになったため実質は冪等（同一 View への
        /// <c>ShowView</c> は <c>ViewRouter</c> が無視する）だが、ロビー以外から呼ばれても成立するよう残す。
        /// </remarks>
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator RestartWithSameSettings_ReturnsToGameView_WithoutError()
        {
            VisualElement panelRoot = null;
            yield return LoadMainThroughBootAndInstallFakes(root => panelRoot = root);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            yield return SimulateClickRoutine(hostButton);

            yield return StartHostingWithAutoPort(panelRoot);

            var gameSession = NetworkBootstrap.Instance.Service.ActiveGameSession;
            Assert.IsNotNull(gameSession, "ホスト開始で GameSession がスポーンされるはず。");
            gameSession.Configure(new TestQuestionSource(TestQuestionSource.FreeText("q-1", "日本の首都はどこ？", "とうきょう")));

            var settings = new SessionSettings(
                new QuestionSelectionSettings(count: QuestionSelectionSettings.AllQuestions, shuffleOrder: false),
                FastLimits,
                ScoringSettings.Default,
                SessionSettings.ManualAdvance);

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");

            Assert.IsTrue(gameSession.StartSession(settings, shuffleSeed: 1), "セッションを開始できるはず（ActiveSessionSettings が設定される）。");
            router.ShowView(ViewNames.Game);

            yield return WaitForElement<Button>(panelRoot, "buzz-button", _ => { });
            yield return WaitUntilWithTimeout(
                () => gameSession.Phase.Value == QuizPhase.BuzzOpen,
                $"1 回目の受付が開きませんでした（{gameSession.Phase.Value}）。");

            Assert.IsTrue(gameSession.RequestBuzz(), "押下を送れるはず。");
            yield return WaitUntilWithTimeout(
                () => gameSession.Phase.Value == QuizPhase.Answering,
                $"回答フェーズに進みませんでした（{gameSession.Phase.Value}）。");

            Assert.IsTrue(gameSession.RequestAnswer("とうきょう"), "本人の回答は受理されるはず。");
            yield return WaitUntilWithTimeout(
                () => gameSession.Phase.Value == QuizPhase.Result,
                $"結果フェーズに進みませんでした（{gameSession.Phase.Value}）。");

            Assert.IsTrue(gameSession.FinishSession(), "結果表示から終了できるはず。");
            yield return WaitUntilWithTimeout(
                () => gameSession.Phase.Value == QuizPhase.Finished,
                $"Finished になりませんでした（{gameSession.Phase.Value}）。");

            Button restartButton = null;
            yield return WaitForElement<Button>(panelRoot, "result-restart-button", found => restartButton = found);
            yield return WaitUntilWithTimeout(
                () => restartButton.resolvedStyle.display == DisplayStyle.Flex,
                "ActiveSessionSettings が設定済みなので「もう一度」ボタンが表示されるはず。");

            // --- 「もう一度（同じ設定で）」 ---
            // 修正前は ResultView.OnRestartClicked() 内で StartSession() が同期的に Phase.Value を
            // Reading へ書き換え、HandlePhaseChanged → ShowView(Game) → OnHide() で _router が
            // null 化された直後に、OnRestartClicked 側の重ねての ShowView 呼び出しが
            // NullReferenceException を起こしていた。実測（このテストを一度リグレッションさせて確認）では、
            // その例外は UI Toolkit の clicked コールバックの呼び出し経路を通じて SimulateClick の
            // 呼び出し元（このテストメソッド自身）まで実際に伝播し、テストが Error として失敗する。
            // そのため LogAssert.Expect / NoUnexpectedReceived は使わない。
            //
            // なお LogAssert.NoUnexpectedReceived() も検討したが採用しなかった: Boot からロビー到達までの
            // 経路だけで内容・出現有無が環境やテスト実行順に左右される Debug.Log/Warning が何本も出るため
            // （実際に試したところ ignoreFailingMessages を併用しても「未消費のログ」扱いは解消されず、
            // 無関係なログで誤って失敗した）、この統合テストでは実用的でないと判断した。
            yield return SimulateClickRoutine(restartButton);

            yield return WaitUntilWithTimeout(
                () => gameSession.Phase.Value == QuizPhase.BuzzOpen,
                $"「もう一度」のあと 2 回目の受付が開きませんでした（{gameSession.Phase.Value}）。");

            Button buzzButtonAfterRestart = null;
            yield return WaitForElement<Button>(panelRoot, "buzz-button", found => buzzButtonAfterRestart = found);
            Assert.IsNotNull(buzzButtonAfterRestart, "「もう一度」のあとは Game View（早押しボタン）に戻っているはず。");
        }

        private static IEnumerator LoadMainThroughBootAndInstallFakes(Action<VisualElement> onRootFound)
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

        private static IEnumerator StartHostingWithAutoPort(VisualElement panelRoot)
        {
            TextField playerNameField = null;
            yield return WaitForElement<TextField>(panelRoot, "player-name-field", found => playerNameField = found);
            IntegerField portField = null;
            yield return WaitForElement<IntegerField>(panelRoot, "port-field", found => portField = found);
            Button startHostButton = null;
            yield return WaitForElement<Button>(panelRoot, "start-host-button", found => startHostButton = found);

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

        /// <summary>
        /// このテストクラス専用の実時間ベースの待機（既定 20 秒）。
        /// 問題配信の受信確認（Ack）を挟むフェーズ遷移は、UI 要素の出現待ちより余裕を見る必要があるため。
        /// </summary>
        private static IEnumerator WaitUntilWithTimeout(
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
    }
}
