using System;
using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.Tests.PlayMode.UI.HostSetup;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.Game;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI.Game
{
    /// <summary>
    /// Game 画面の参加者パネル（#194）を Main シーン + 実プレハブで確かめる PlayMode テスト。
    /// プレイヤーを兼ねるホスト自身が押して、押下順位と「回答中」がパネルに出ること、
    /// <c>display.showScores</c> が OFF なら得点の列が出ないことを見る。
    /// </summary>
    public class GameViewParticipantPanelSceneTests
    {
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
            // GameViewModeratorSceneTests と同じ順序（ホスト停止 → Main アンロード → シングルトン破棄）。
            var bootstrap = NetworkBootstrap.Instance;
            bootstrap?.Service?.Stop();

            yield return UnloadMainSceneRoutine();

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
        public IEnumerator PlayerHost_Buzz_ShowsRankAnsweringAndScoreInParticipantPanel()
        {
            VisualElement panelRoot = null;
            GameSession gameSession = null;
            yield return StartPlayerHostQuestion(root => panelRoot = root, session => gameSession = session, showScores: true);

            yield return GameViewModeratorSceneTests.WaitUntilWithTimeout(
                () => gameSession.Phase.Value == QuizPhase.BuzzOpen, $"早押し受付が開きませんでした（{gameSession.Phase.Value}）。");

            var hostClientId = gameSession.NetworkManager.LocalClientId;
            var row = default(VisualElement);
            yield return GameViewModeratorSceneTests.WaitUntilWithTimeout(
                () => (row = panelRoot.Q<VisualElement>($"participant-row-{hostClientId}")) != null,
                "参加者パネルにホスト自身の行が出るはず。");
            Assert.IsTrue(row.ClassListContains(ParticipantPanelPresenter.RowLocalClass), "自分の行は強調される。");
            Assert.AreEqual("0点", row.Q<Label>("participant-score")?.text, "既定（display.showScores = true）では得点を出す。");
            // #206: 名前は参加者が決める文字列なので、名前・押した人を出す表示欄はリッチテキストのタグを解釈しない。
            Assert.IsFalse(row.Q<Label>("participant-name").enableRichText, "参加者パネルの名前はタグを解釈しないはず。");
            Assert.IsFalse(panelRoot.Q<Label>("buzz-result-label").enableRichText, "回答権を得た人の表示はタグを解釈しないはず。");

            var summary = panelRoot.Q<Label>(ParticipantPanel.SummaryLabelName);
            Assert.AreEqual("回答権あり 1 / 1 人", summary.text);

            Assert.IsTrue(gameSession.RequestBuzz(), "受付中なのでホストも押下を送れるはず。");

            yield return GameViewModeratorSceneTests.WaitUntilWithTimeout(
                () =>
                {
                    row = panelRoot.Q<VisualElement>($"participant-row-{hostClientId}");
                    return row != null && row.Q<Label>("participant-rank")?.text == "1着";
                },
                "押下順位（1着）がパネルに出るはず。");
            yield return GameViewModeratorSceneTests.WaitUntilWithTimeout(
                () => panelRoot.Q<VisualElement>($"participant-row-{hostClientId}")?.Q<Label>("participant-status")?.text == "回答中",
                "回答権を得たら「回答中」が出るはず。");
        }

        [UnityTest]
        [Timeout(90000)]
        public IEnumerator ShowScoresOff_HidesScoreColumn()
        {
            VisualElement panelRoot = null;
            GameSession gameSession = null;
            yield return StartPlayerHostQuestion(root => panelRoot = root, session => gameSession = session, showScores: false);

            var hostClientId = gameSession.NetworkManager.LocalClientId;
            var row = default(VisualElement);
            yield return GameViewModeratorSceneTests.WaitUntilWithTimeout(
                () => (row = panelRoot.Q<VisualElement>($"participant-row-{hostClientId}")) != null,
                "参加者パネルにホスト自身の行が出るはず。");

            Assert.IsFalse(gameSession.SettingsSync.Current.ShowScores, "確定したルーム設定でも OFF のまま。");
            Assert.IsNull(row.Q<Label>("participant-score"), "display.showScores が OFF なら得点の列を出さない。");
            Assert.AreEqual(
                DisplayStyle.Flex,
                panelRoot.Q<Label>(ParticipantPanel.SummaryLabelName).style.display.value,
                "回答順・未回答者（要約）は設定に関わらず表示する。");
        }

        /// <summary>
        /// お手つきの「次問休み」の問題では、早押しボタンが押せず、パネルにも「休み」が出る（#194 の食い違いの修正）。
        /// 誤答のローカルの印は次の問題の提示でクリアされるので、同期された進行状態だけでボタンを止められることを見る。
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator SkipNextPenalty_DisablesBuzzButtonOnTheNextQuestion()
        {
            var source = new TestQuestionSource(
                TestQuestionSource.FreeText("q-1", "日本の首都はどこ？", "とうきょう"),
                TestQuestionSource.FreeText("q-2", "日本でいちばん高い山は？", "ふじさん"));
            var limits = new QuizTimeLimits(buzzTimeLimitSec: 6.0, answerTimeLimitSec: 15.0, collectWindowSec: 0.15);

            VisualElement panelRoot = null;
            GameSession gameSession = null;
            yield return StartPlayerHostQuestion(
                root => panelRoot = root, session => gameSession = session, showScores: true, source, limits);

            // #200: 押せる参加者が居なくなると受付は時間切れを待たずに締まる。この画面テストはホスト 1 人なので、
            // 2 問目（ホストが次問休み）の受付が 1 tick で締まり、受付中のボタンの状態を観測できない。
            // 「ほかに押せる参加者が居る」状況を判定の差し替えで作り、同期された進行状態だけで止まることを見る。
            gameSession.EligibleBuzzersOverrideForTests = () => true;

            yield return GameViewModeratorSceneTests.WaitUntilWithTimeout(
                () => gameSession.Phase.Value == QuizPhase.BuzzOpen, $"早押し受付が開きませんでした（{gameSession.Phase.Value}）。");
            Assert.IsTrue(gameSession.RequestBuzz());
            yield return GameViewModeratorSceneTests.WaitUntilWithTimeout(
                () => gameSession.Phase.Value == QuizPhase.Answering, $"回答フェーズに進みませんでした（{gameSession.Phase.Value}）。");
            Assert.IsTrue(gameSession.RequestAnswer("おおさか"), "誤答を送る。");

            // 再開放後、誰も押さずに持ち時間（6 秒）が切れて結果へ。
            yield return GameViewModeratorSceneTests.WaitUntilWithTimeout(
                () => gameSession.Phase.Value == QuizPhase.Result, $"結果へ進みませんでした（{gameSession.Phase.Value}）。");

            Assert.IsTrue(gameSession.StartQuestion(1), "2 問目を出題できるはず。");
            yield return GameViewModeratorSceneTests.WaitUntilWithTimeout(
                () => gameSession.Phase.Value == QuizPhase.BuzzOpen && gameSession.QuestionIndex.Value == 1,
                $"2 問目の早押し受付が開きませんでした（{gameSession.Phase.Value} / {gameSession.QuestionIndex.Value}）。");

            var hostClientId = gameSession.NetworkManager.LocalClientId;
            yield return GameViewModeratorSceneTests.WaitUntilWithTimeout(
                () => panelRoot.Q<VisualElement>($"participant-row-{hostClientId}")?.Q<Label>("participant-status")?.text
                      == "休み（お手つき）",
                "次問休みの問題ではパネルに「休み」が出るはず。");

            // 受付が開いている間に判定する（持ち時間が切れて Result に入るとフェーズの理由で押せなくなり、
            // 同期値による無効化を確かめたことにならない）。
            var buzzButton = panelRoot.Q<Button>("buzz-button");
            Assert.IsNotNull(buzzButton);
            yield return null;
            Assert.AreEqual(QuizPhase.BuzzOpen, gameSession.Phase.Value, "判定は受付中に行う。");
            Assert.IsFalse(buzzButton.enabledSelf, "次問休みの問題では早押しボタンを押せないはず。");
        }

        /// <summary>プレイヤーを兼ねるホストを開始し、freeText の 1 問目を出題するところまで進める。</summary>
        private static IEnumerator StartPlayerHostQuestion(
            Action<VisualElement> onRoot,
            Action<GameSession> onSession,
            bool showScores,
            TestQuestionSource questionSource = null,
            QuizTimeLimits limits = null)
        {
            VisualElement panelRoot = null;
            yield return GameViewModeratorSceneTests.LoadMainThroughBootAndInstallFakes(root => panelRoot = root);
            onRoot(panelRoot);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            yield return SimulateClickRoutine(hostButton);

            yield return GameViewModeratorSceneTests.StartHostingWithAutoPort(panelRoot, moderatorOnly: false);

            var gameSession = NetworkBootstrap.Instance.Service.ActiveGameSession;
            Assert.IsNotNull(gameSession, "ホスト開始で GameSession がスポーンされるはず。");
            onSession(gameSession);
            gameSession.Configure(
                questionSource ?? new TestQuestionSource(TestQuestionSource.FreeText("q-1", "日本の首都はどこ？", "とうきょう")),
                limits: limits);

            // 出題（ルーム設定の確定）より前に、ホストとして display.showScores を書く。
            var sync = gameSession.SettingsSync;
            Assert.IsNotNull(sync, "GameSession プレハブに RoomSettingsSync が載っているはず。");
            Assert.IsTrue(sync.TrySetSettings(sync.Current.WithShowScores(showScores)), "ロック前のホストは設定を書けるはず。");

            // GameViewModeratorSceneTests と同じく、ロビーの同期が済んでから単問経路で出題する（PR #104 レビュー M-5）。
            Button startGameButton = null;
            yield return WaitForElement<Button>(panelRoot, "start-game-button", found => startGameButton = found);
            yield return WaitUntil(
                () => startGameButton.enabledSelf, "ロビーの同期が済めば「ゲーム開始」が有効になるはず。");

            Assert.IsTrue(gameSession.StartQuestion(0), "1 問目の出題を開始できるはず。");
        }
    }
}
