using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Images;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.Tests.Shared.Questions;
using TsumugiQuiz.Tests.Shared.Room;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.Game;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI.Game
{
    /// <summary>
    /// issue #187 / #193: Game 画面（3 列レイアウト）の中央列が作業領域の縦幅に収まり、操作中のフェーズでは主要操作
    /// （選択肢ボタン / 早押しボタン / 回答欄）、判定後は「次へ」「退出」が必ず表示範囲に入ることを、
    /// 論理ビューポートの大きさを変えて実シーンで確かめる PlayMode テスト。
    /// </summary>
    /// <remarks>
    /// <para>
    /// バッチ実行の画面サイズは固定なので、<c>game-root</c>（<c>.screen-root</c>）の幅・高さを
    /// 目的の論理ビューポート（docs/architecture.md §10.2 の表。<see cref="PanelScaleProbe.LogicalViewportFor"/>）に固定して再現する。
    /// 中央列の幅は論理幅で変わる（左右の列が割合 + 上下限で決まるため）ので、ビューポートごとに問題文の折り返しも変わる。
    /// </para>
    /// <para>
    /// 立ち絵の素材はテストに同梱できない（docs/licenses.md §3）ため、「立ち絵あり」は右列（<c>character-view-instance</c>）を
    /// 表示し、中央列の <see cref="GameView.NoCharacterMainClassName"/> を外して再現する（右列の幅は USS で決まり、
    /// 立ち絵そのものの有無は縦幅に寄与しない）。中央列がいちばん狭くなる条件（＝問題文の折り返しが最も多い条件）は、
    /// 900x750 と 16:9 では立ち絵あり。21:9 では中央列が立ち絵の有無にかかわらず最大幅 880px で頭打ちになる（#213）ので、
    /// 立ち絵あり・なしの両方を測る（中央列の幅は同じで、立ち絵ありは 3 列ごと中央に寄る）。
    /// </para>
    /// <para>
    /// ホスト（通常プレイヤー）として、選択式 4 択（画像あり / なし、2 行の問題文）、早押し（画像あり、2 行）、
    /// 選択式 8 択（docs/question-data.md の上限）・3 行の問題文（画像なし。#193 の 2 列化後はどのビューポートでも
    /// スクロールなしで収まる。docs/architecture.md §10.2）を順に出す。画像エリアは
    /// 中央列の残りの高さを埋める（#193）ので、画像付きの問題は「画像以外が作業領域に収まり、画像エリアが上下限の範囲に
    /// 入るか」を見る。ホストの「次へ」は「退出」と同じ行に並ぶため、「次へ」の無いクライアントでも縦幅は同じになる。
    /// </para>
    /// </remarks>
    public sealed class GameViewVerticalFitSceneTests
    {
        private const string LogTag = "#193 実測";

        /// <summary>中央列の幅で 2 行になる問題文。</summary>
        private const string TwoLineText =
            "画像を見て答えてください。この画像の説明として正しいものはどれでしょう？ 選択肢から 1 つ選んでください。";

        /// <summary>3 行以上になる問題文（想定より長い問題。8 択と組み合わせて最も縦に長い画像なしの問題にする）。</summary>
        private const string ThreeLineText =
            "とても長い問題文の例です。問題文が 3 行以上になると本体の縦幅がさらに伸びます。"
            + "低い論理縦幅ではスクロールで選択肢を表示範囲に入れ、下段の「次へ」「退出」は常に見えている必要があります。";

        private const string Answer = "こたえ";

        /// <summary>縦長の画像（#185 と同じ）。</summary>
        private const int ImageWidth = 120;
        private const int ImageHeight = 480;

        private static readonly string[] ChoiceLetters = { "A", "B", "C", "D", "E", "F", "G", "H" };

        private MainSceneTestHelpers.ConsentFileScope _consentScope;
        private RoomSettingsDraftScope _roomSettingsDraftScope;
        private string _questionsFolder;

        [SetUp]
        public void SetUp()
        {
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();
            _roomSettingsDraftScope = RoomSettingsDraftScope.Redirect();

            var store = ConsentGate.CreateDefaultStore();
            store.RecordConsent(TermsCatalog.LoadRequiredTerms(), Application.version, DateTime.UtcNow);

            _questionsFolder = Path.Combine(Path.GetTempPath(), "tq-i193-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_questionsFolder, "images"));
            File.WriteAllBytes(
                Path.Combine(_questionsFolder, "images", "tall.png"),
                TestImageFactory.CreatePng(ImageWidth, ImageHeight, seed: 11));
        }

        [TearDown]
        public void TearDown()
        {
            if (_questionsFolder != null && Directory.Exists(_questionsFolder))
            {
                Directory.Delete(_questionsFolder, recursive: true);
            }

            _questionsFolder = null;
            _consentScope?.Restore();
            _roomSettingsDraftScope?.Restore();
            _roomSettingsDraftScope = null;
        }

        [UnityTearDown]
        public IEnumerator TearDownSceneAndNetwork()
        {
            var bootService = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.Service : null;
            bootService?.Stop();

            yield return WaitUntil(
                () => bootService == null
                      || (!bootService.IsListening && !bootService.IsClient && !bootService.IsShutdownInProgress),
                DefaultTimeoutSeconds,
                "ホストの停止が完了しませんでした。");
            yield return null;

            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();
            TearDownMainSceneAndBootstrapSingletons();
        }

        /// <summary>1600x900 基準（16:9 フルスクリーン・1280x720 ウィンドウ）+ 立ち絵あり: 論理 1600x900、作業領域 836px。</summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Logical16By9_WithCharacter_OperationsAndNextStayVisible()
            => RunScenario(new Scenario(
                "16:9+立ち絵", PanelScaleProbe.Viewport16By9, withCharacter: true, mainReachesMaxWidth: false, imageLatePhasesFit: true));

        /// <summary>21:9（2560x1080 フルスクリーン）+ 立ち絵なし（中央列 880px）: 論理約 1829x771、作業領域約 707px。</summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator LogicalUltraWide_NoCharacter_OperationsAndNextStayVisible()
            => RunScenario(new Scenario(
                "21:9・立ち絵なし", PanelScaleProbe.ViewportUltraWide, withCharacter: false, mainReachesMaxWidth: true, imageLatePhasesFit: false));

        /// <summary>
        /// 21:9 + 立ち絵あり: 中央列は左右の列の残り（約 994px）を埋めず、立ち絵なしと同じ 880px で頭打ちになり、
        /// 3 列ごと中央に寄る（#213。以前は約 994px まで広がり、立ち絵なしより問題文の行が長かった）。
        /// </summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator LogicalUltraWide_WithCharacter_OperationsAndNextStayVisible()
            => RunScenario(new Scenario(
                "21:9+立ち絵", PanelScaleProbe.ViewportUltraWide, withCharacter: true, mainReachesMaxWidth: true, imageLatePhasesFit: false));

        /// <summary>想定最小ウィンドウ 900x750 + 立ち絵あり（中央列が最も狭い約 630px）: 論理約 1290x1075、作業領域約 1011px。</summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator LogicalMinimumWindow_WithCharacter_OperationsAndNextStayVisible()
            => RunScenario(new Scenario(
                "900x750+立ち絵", PanelScaleProbe.ViewportMinimumWindow, withCharacter: true, mainReachesMaxWidth: false, imageLatePhasesFit: true));

        private IEnumerator RunScenario(Scenario scenario)
        {
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);
            AssertPanelSettingsMatchProbe();

            var hostService = NetworkBootstrap.Instance.Service;
            NetworkStartResult startResult = default;
            yield return hostService.StartHostWhenReady(startPort: 0, onCompleted: r => startResult = r);
            Assert.IsTrue(startResult.Success, startResult.Message);

            var session = hostService.ActiveGameSession;
            Assert.IsNotNull(session, "ホスト開始で GameSession がスポーンされるはず。");
            session.Configure(
                new TestQuestionSource(
                    Choice("q-choice-img", TwoLineText, 4, "images/tall.png"),
                    Choice("q-choice-noimg", TwoLineText, 4, null),
                    new Question(
                        "q-free-img", QuestionType.FreeText, TwoLineText, answers: new[] { Answer }, imagePath: "images/tall.png"),
                    Choice("q-choice-long", ThreeLineText, 8, null)),
                new QuizTimeLimits(
                    buzzTimeLimitSec: 10.0, answerTimeLimitSec: 10.0, choiceTimeLimitSec: 3.0, collectWindowSec: 0.15));
            Assert.IsTrue(session.ConfigureImages(new QuestionImageSource(_questionsFolder)), "画像の供給元を設定できるはず。");

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Game);

            var ui = new Elements();
            yield return ui.Find(panelRoot);
            GameViewLayoutProbe.SetLogicalViewport(ui.GameRoot, scenario.Viewport);
            ui.ApplyCharacterColumn(scenario.WithCharacter);

            yield return null;
            yield return null;

            var (top, bottom) = GameViewLayoutProbe.WorkingArea(ui.GameRoot);
            Debug.Log($"[{LogTag}] {scenario.Label}: 論理ビューポート {ui.GameRoot.layout.width:F1}x{ui.GameRoot.layout.height:F1}、"
                      + $"作業領域の縦 {bottom - top:F1}px、列幅 左 {ui.ParticipantPanel.layout.width:F1} / 中央 {ui.Main.layout.width:F1}"
                      + $" / 右 {(scenario.WithCharacter ? ui.CharacterSlot.layout.width : 0f):F1}"
                      + $"（パネル {panelRoot.layout.width}x{panelRoot.layout.height}）");
            ui.AssertColumns(scenario);

            yield return RunChoice(
                session, ui, 0, $"{scenario.Label} 選択式4択+画像", fitsWhileAnswering: true, fitsAfterJudgement: scenario.ImageLatePhasesFit);
            yield return RunChoice(
                session, ui, 1, $"{scenario.Label} 選択式4択・画像なし", fitsWhileAnswering: true, fitsAfterJudgement: true);
            yield return RunFreeText(session, ui, 2, $"{scenario.Label} 早押し+画像", scenario.ImageLatePhasesFit);
            yield return RunChoice(
                session, ui, 3, $"{scenario.Label} 選択式8択・3行（上限）", fitsWhileAnswering: true, fitsAfterJudgement: true);
        }

        private static void AssertPanelSettingsMatchProbe()
        {
            var settings = FindViewRouterUIDocument().panelSettings;
            Assert.AreEqual(PanelScaleMode.ScaleWithScreenSize, settings.scaleMode, "panel-settings.asset の前提が変わった。");
            Assert.AreEqual(PanelScreenMatchMode.MatchWidthOrHeight, settings.screenMatchMode, "panel-settings.asset の前提が変わった。");
            Assert.AreEqual(PanelScaleProbe.Match, settings.match, 0.0001f, "panel-settings.asset の前提が変わった。");
            Assert.AreEqual(
                new Vector2Int((int)PanelScaleProbe.ReferenceResolution.x, (int)PanelScaleProbe.ReferenceResolution.y),
                settings.referenceResolution,
                "panel-settings.asset の基準解像度が変わった（docs/architecture.md §10.2 の表も見直すこと）。");
        }

        private static IEnumerator RunChoice(
            GameSession session, Elements ui, int index, string label, bool fitsWhileAnswering, bool fitsAfterJudgement)
        {
            Assert.IsTrue(session.StartQuestion(index), $"{label}: 出題できるはず。");
            yield return WaitUntil(
                () => session.Phase.Value == QuizPhase.ChoiceAnswering && ui.ChoiceButtons.childCount > 0,
                DefaultTimeoutSeconds,
                () => $"{label}: 選択式の回答受付が開きませんでした（現在: {session.Phase.Value}）。");
            yield return WaitForLayout(ui);

            GameViewLayoutProbe.LogBreakdown(LogTag, $"{label} 回答中", ui.GameRoot, ui.Main, ui.Scroll);
            ui.AssertImageArea($"{label} 回答中");
            if (fitsWhileAnswering)
            {
                AssertFitsWithoutScrolling(ui, $"{label} 回答中");
            }

            foreach (var child in ui.ChoiceButtons.Children())
            {
                var button = (Button)child;
                GameViewLayoutProbe.AssertFullyVisible(button, ui.GameRoot, ui.Scroll, $"{label} 回答中の選択肢「{button.text}」");
            }

            GameViewLayoutProbe.AssertFullyVisible(ui.ExitButton, ui.GameRoot, ui.Scroll, $"{label} 回答中の退出");

            yield return WaitForResultWithNext(session, ui, label);
            GameViewLayoutProbe.LogBreakdown(LogTag, $"{label} 判定後（次へあり）", ui.GameRoot, ui.Main, ui.Scroll);
            Assert.IsTrue(
                ui.ChoiceSection.ClassListContains("game-choice-section--compact"), $"{label}: 判定後は選択肢を詰めて表示するはず。");
            ui.AssertImageArea($"{label} 判定後");
            AssertNextAndExitVisible(ui, label);
            if (fitsAfterJudgement)
            {
                AssertFitsWithoutScrolling(ui, $"{label} 判定後");
            }
        }

        private static IEnumerator RunFreeText(GameSession session, Elements ui, int index, string label, bool lateFits)
        {
            Assert.IsTrue(session.StartQuestion(index), $"{label}: 出題できるはず。");
            yield return WaitUntil(
                () => session.Phase.Value == QuizPhase.BuzzOpen,
                DefaultTimeoutSeconds,
                () => $"{label}: 早押し受付が開きませんでした（現在: {session.Phase.Value}）。");
            yield return WaitForLayout(ui);

            GameViewLayoutProbe.LogBreakdown(LogTag, $"{label} 早押し受付中", ui.GameRoot, ui.Main, ui.Scroll);
            ui.AssertImageArea($"{label} 早押し受付中");
            AssertFitsWithoutScrolling(ui, $"{label} 早押し受付中");
            GameViewLayoutProbe.AssertFullyVisible(ui.BuzzButton, ui.GameRoot, ui.Scroll, $"{label} 早押しボタン");

            yield return SimulateClickRoutine(ui.BuzzButton);
            yield return WaitUntil(
                () => session.Phase.Value == QuizPhase.Answering && ui.AnswerSection.resolvedStyle.display == DisplayStyle.Flex,
                DefaultTimeoutSeconds,
                () => $"{label}: 回答欄が開きませんでした（現在: {session.Phase.Value}）。");
            yield return WaitForLayout(ui);

            GameViewLayoutProbe.LogBreakdown(LogTag, $"{label} 回答入力中", ui.GameRoot, ui.Main, ui.Scroll);
            Assert.AreEqual(
                DisplayStyle.None, ui.BuzzButton.resolvedStyle.display, $"{label}: 自分が回答している間は早押しボタンを隠すはず。");
            ui.AssertImageArea($"{label} 回答入力中");
            if (lateFits)
            {
                AssertFitsWithoutScrolling(ui, $"{label} 回答入力中");
            }

            GameViewLayoutProbe.AssertFullyVisible(ui.AnswerField, ui.GameRoot, ui.Scroll, $"{label} 回答欄");
            GameViewLayoutProbe.AssertFullyVisible(ui.AnswerSubmitButton, ui.GameRoot, ui.Scroll, $"{label} 回答するボタン");

            ui.AnswerField.value = Answer;
            yield return SimulateClickRoutine(ui.AnswerSubmitButton);

            yield return WaitForResultWithNext(session, ui, label);
            GameViewLayoutProbe.LogBreakdown(LogTag, $"{label} 判定後（次へあり）", ui.GameRoot, ui.Main, ui.Scroll);
            ui.AssertImageArea($"{label} 判定後");
            AssertNextAndExitVisible(ui, label);
            if (lateFits)
            {
                AssertFitsWithoutScrolling(ui, $"{label} 判定後");
            }
        }

        private static void AssertFitsWithoutScrolling(Elements ui, string label)
        {
            Assert.IsTrue(
                GameViewLayoutProbe.FitsWithoutScrolling(ui.Scroll),
                $"{label}: スクロールせずに作業領域へ収まるはず（本来の縦幅 "
                + $"{GameViewLayoutProbe.NaturalHeight(ui.Main, ui.Scroll):F1}px、スクロール領域 {ui.Scroll.layout.height:F1}px）。");
        }

        private static IEnumerator WaitForResultWithNext(GameSession session, Elements ui, string label)
        {
            yield return WaitUntil(
                () => session.Phase.Value == QuizPhase.Result
                      && ui.ResultSection.resolvedStyle.display == DisplayStyle.Flex
                      && ui.HostControls.resolvedStyle.display == DisplayStyle.Flex,
                DefaultTimeoutSeconds,
                () => $"{label}: 判定後に判定結果と「次へ」が表示されませんでした（現在: {session.Phase.Value}）。");
            yield return WaitForLayout(ui);
        }

        private static void AssertNextAndExitVisible(Elements ui, string label)
        {
            GameViewLayoutProbe.AssertFullyVisible(ui.NextButton, ui.GameRoot, ui.Scroll, $"{label} 判定後の次へ");
            GameViewLayoutProbe.AssertFullyVisible(ui.ExitButton, ui.GameRoot, ui.Scroll, $"{label} 判定後の退出");
            Assert.That(
                ui.NextButton.worldBound.yMin,
                Is.EqualTo(ui.ExitButton.worldBound.yMin).Within(GameViewLayoutProbe.LayoutTolerance),
                $"{label}: 「次へ」と「退出」は同じ行に並ぶはず。");
        }

        /// <summary>表示切り替え・スクロール（GameView.Scroll.cs の schedule.Execute）が反映されるまで待つ。</summary>
        private static IEnumerator WaitForLayout(Elements ui)
        {
            for (var i = 0; i < 4; i++)
            {
                yield return null;
            }

            Assert.IsNotNull(ui.GameRoot.panel, "Game 画面がパネルから外れています。");
        }

        private static Question Choice(string id, string text, int count, string imagePath)
        {
            var choices = new string[count];
            for (var i = 0; i < count; i++)
            {
                choices[i] = $"選択肢 {ChoiceLetters[i]} の説明文";
            }

            return new Question(id, QuestionType.Choice, text, choices: choices, correctIndex: 1, imagePath: imagePath);
        }

        /// <summary>1 回の実測の条件。</summary>
        private readonly struct Scenario
        {
            public Scenario(string label, Vector2 viewport, bool withCharacter, bool mainReachesMaxWidth, bool imageLatePhasesFit)
            {
                Label = label;
                Viewport = viewport;
                WithCharacter = withCharacter;
                MainReachesMaxWidth = mainReachesMaxWidth;
                ImageLatePhasesFit = imageLatePhasesFit;
            }

            public string Label { get; }

            public Vector2 Viewport { get; }

            /// <summary>右列（立ち絵）を表示するか。</summary>
            public bool WithCharacter { get; }

            /// <summary>
            /// 中央列が最大幅（<see cref="GameViewLayoutProbe.MainMaxWidth"/>）で頭打ちになる条件か（#213）。
            /// 21:9 は立ち絵の有無にかかわらず頭打ちになる。16:9（799px）・900x750（632px）は左右の列の残りが
            /// 最大幅より狭いので、最大幅は効かない。
            /// </summary>
            public bool MainReachesMaxWidth { get; }

            /// <summary>
            /// 画像付きの問題の「後半のフェーズ」（選択式の判定後、早押しの回答入力中・判定後）がスクロールなしで収まる前提か。
            /// 21:9 では画像エリアが最小（160px）でも作業領域（約 707px）を超え、スクロールする
            /// （docs/architecture.md §10.2。主要操作と「次へ」「退出」は表示範囲に入ることを検証する）。
            /// 回答中・早押し受付中（画像がいちばん大事な場面）と画像なしの問題は、どのビューポートでも収まる。
            /// </summary>
            public bool ImageLatePhasesFit { get; }
        }

        /// <summary>テストで参照する Game 画面の要素。</summary>
        private sealed class Elements
        {
            public VisualElement GameRoot;
            public VisualElement Row;
            public VisualElement ParticipantPanel;
            public VisualElement Main;
            public VisualElement CharacterSlot;
            public VisualElement ImageArea;
            public ScrollView Scroll;
            public VisualElement ChoiceSection;
            public VisualElement ChoiceButtons;
            public VisualElement ResultSection;
            public VisualElement AnswerSection;
            public VisualElement HostControls;
            public Button BuzzButton;
            public TextField AnswerField;
            public Button AnswerSubmitButton;
            public Button NextButton;
            public Button ExitButton;

            public IEnumerator Find(VisualElement panelRoot)
            {
                yield return WaitForElement<VisualElement>(panelRoot, "game-root", found => GameRoot = found);
                yield return WaitForElement<ScrollView>(panelRoot, "game-scroll-view", found => Scroll = found);
                yield return WaitForElement<Button>(panelRoot, "exit-button", found => ExitButton = found);
                Row = panelRoot.Q<VisualElement>("game-content-row");
                ParticipantPanel = panelRoot.Q<VisualElement>("participant-panel");
                Main = panelRoot.Q<VisualElement>("game-content-main");
                CharacterSlot = panelRoot.Q<VisualElement>("character-view-instance");
                ImageArea = panelRoot.Q<VisualElement>("question-image-container");
                ChoiceSection = panelRoot.Q<VisualElement>("choice-section");
                ChoiceButtons = panelRoot.Q<VisualElement>("choice-buttons-container");
                ResultSection = panelRoot.Q<VisualElement>("result-section");
                AnswerSection = panelRoot.Q<VisualElement>("answer-section");
                HostControls = panelRoot.Q<VisualElement>("host-controls");
                BuzzButton = panelRoot.Q<Button>("buzz-button");
                AnswerField = panelRoot.Q<TextField>("answer-field");
                AnswerSubmitButton = panelRoot.Q<Button>("answer-submit-button");
                NextButton = panelRoot.Q<Button>("next-button");

                Assert.IsNotNull(Row, "game-content-row が見つかりません。");
                Assert.IsNotNull(ParticipantPanel, "participant-panel が見つかりません。");
                Assert.IsNotNull(Main, "game-content-main が見つかりません。");
                Assert.IsNotNull(CharacterSlot, "character-view-instance が見つかりません。");
                Assert.IsNotNull(ImageArea, "question-image-container が見つかりません。");
                Assert.IsNotNull(ChoiceSection, "choice-section が見つかりません。");
                Assert.IsNotNull(ChoiceButtons, "choice-buttons-container が見つかりません。");
                Assert.IsNotNull(ResultSection, "result-section が見つかりません。");
                Assert.IsNotNull(AnswerSection, "answer-section が見つかりません。");
                Assert.IsNotNull(HostControls, "host-controls が見つかりません。");
                Assert.IsNotNull(BuzzButton, "buzz-button が見つかりません。");
                Assert.IsNotNull(AnswerField, "answer-field が見つかりません。");
                Assert.IsNotNull(AnswerSubmitButton, "answer-submit-button が見つかりません。");
                Assert.IsNotNull(NextButton, "next-button が見つかりません。");
            }

            /// <summary>
            /// 立ち絵の有無を再現する。立ち絵ありは右列を表示し、中央列の修飾クラス（立ち絵なしの印）を外す。
            /// #213 以降、修飾クラスはスタイルを持たず、中央列の最大幅（880px）は立ち絵の有無にかかわらず付くので、
            /// レイアウトが変わるのは右列の表示だけである。素材の無いテストでは GameView が立ち絵なしの状態
            /// （右列を隠し、修飾クラスを付ける）にしているので、立ち絵なしは何もしない。
            /// </summary>
            /// <remarks>
            /// 右列の display とクラスをテストからインライン指定で上書きしている。これが出題中に GameView に
            /// 戻されないのは、GameView が右列・クラスを付け直すきっかけが <c>CharacterView.VisibilityChanged</c>
            /// （立ち絵の可視性の値が<b>変わったとき</b>だけ発火する。出題ごとの再評価でも値が同じなら発火しない）
            /// に限られ、素材の無いテストでは可視性が「非表示」のまま変わらないという前提による
            /// （GameView.Character.cs の <c>UpdateGameContentLayoutForCharacterVisibility</c>）。
            /// この前提が変わった場合（毎回付け直すようにした等）は、ここでの再現方法も見直すこと。
            /// </remarks>
            public void ApplyCharacterColumn(bool withCharacter)
            {
                Assert.IsTrue(
                    Main.ClassListContains(GameView.NoCharacterMainClassName),
                    "素材の無いテストでは立ち絵なしの状態から始まるはず。");
                if (!withCharacter)
                {
                    return;
                }

                CharacterSlot.style.display = DisplayStyle.Flex;
                Main.RemoveFromClassList(GameView.NoCharacterMainClassName);
            }

            /// <summary>
            /// 3 列が作業領域の高さいっぱいに並び、中央列は左右の列の残りを埋めて最大幅で頭打ちになる（21:9 は立ち絵の
            /// 有無にかかわらず 880px、16:9・900x750 の立ち絵ありは残りの幅のまま。#213）こと。
            /// </summary>
            public void AssertColumns(Scenario scenario)
            {
                const float tolerance = GameViewLayoutProbe.LayoutTolerance;
                var (top, bottom) = GameViewLayoutProbe.WorkingArea(GameRoot);
                Assert.That(ParticipantPanel.layout.height, Is.EqualTo(bottom - top).Within(tolerance), "参加者パネルは縦長（作業領域の高さ）のはず。");
                Assert.That(Main.layout.height, Is.EqualTo(bottom - top).Within(tolerance), "中央列は作業領域の高さのはず。");
                Assert.That(Main.worldBound.xMin, Is.GreaterThan(ParticipantPanel.worldBound.xMax), "中央列は参加者パネルの右にあるはず。");
                if (scenario.WithCharacter)
                {
                    Assert.That(CharacterSlot.layout.height, Is.EqualTo(bottom - top).Within(tolerance), "右列（立ち絵）は縦長のはず。");
                    GameViewLayoutProbe.AssertMainColumnWithCharacter(Row, ParticipantPanel, Main, CharacterSlot, scenario.Label);
                }
                else
                {
                    Assert.AreEqual(DisplayStyle.None, CharacterSlot.resolvedStyle.display, "立ち絵なしでは右列を隠すはず。");
                }

                if (scenario.MainReachesMaxWidth)
                {
                    Assert.That(
                        Main.layout.width, Is.EqualTo(GameViewLayoutProbe.MainMaxWidth).Within(tolerance),
                        $"{scenario.Label}: 中央列は最大幅で頭打ちになるはず。");
                }
                else
                {
                    Assert.That(
                        Main.layout.width, Is.LessThan(GameViewLayoutProbe.MainMaxWidth - tolerance),
                        $"{scenario.Label}: このビューポートでは中央列は最大幅に届かないはず（最大幅は効かない）。");
                }
            }

            /// <summary>
            /// 画像が表示されているときは、画像エリアが中央列の最上部にあり、高さが上下限（USS）の範囲にあること。
            /// 画像の無い問題では画像エリアを畳み、問題パネルが中央列の最上部に詰まっていること。
            /// </summary>
            public void AssertImageArea(string label)
            {
                const float tolerance = GameViewLayoutProbe.LayoutTolerance;
                var header = Scroll.contentContainer.Q<VisualElement>("game-header");
                Assert.IsNotNull(header, "game-header が見つかりません。");
                if (ImageArea.resolvedStyle.display == DisplayStyle.None)
                {
                    Assert.That(header.worldBound.yMin, Is.EqualTo(Main.worldBound.yMin).Within(tolerance), $"{label}: 画像が無ければ問題パネルが上に詰まるはず。");
                    return;
                }

                Assert.That(ImageArea.worldBound.yMin, Is.EqualTo(Main.worldBound.yMin).Within(tolerance), $"{label}: 画像エリアは中央列の最上部のはず。");
                Assert.That(
                    ImageArea.layout.height,
                    Is.InRange(QuestionImageLayout.MinAreaHeight - tolerance, QuestionImageLayout.MaxAreaHeight + tolerance),
                    $"{label}: 画像エリアの高さは上下限（{QuestionImageLayout.MinAreaHeight}〜{QuestionImageLayout.MaxAreaHeight}px）の範囲のはず。");
            }
        }
    }
}
