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
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI.Game
{
    /// <summary>
    /// issue #185: 画像付きの問題（FR-12）で、配信された画像が Game View の <c>question-image</c> に
    /// 表示されることを実シーンで確かめる PlayMode テスト。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 配信そのもの（チャンク・Ack/NAK・キャッシュ）は <see cref="QuestionImageDistributionTests"/> が確かめている。
    /// 本テストは「受け取った画像を UI が貼るか」「画像の無い問題では画像エリアを畳んで問題パネルを上に詰めるか」
    /// 「次の問題で消えるか」「画像エリアが中央列の残りの高さを埋め（#193）、1600x900 基準の作業領域（836px）に
    /// 収まるか」を見る。3 つの論理ビューポートでの縦幅は <see cref="GameViewVerticalFitSceneTests"/> が見る。
    /// </para>
    /// <para>
    /// クライアント役の確認は <see cref="GameViewSceneTests"/> と同じ作法で、1 プロセス内に立てた生の
    /// <see cref="NetworkManager"/> をホストとし、Boot → Main を読み込んだ側をクライアントとして駆動する。
    /// 画像ファイルは一時フォルダに実際に書き出し、本番と同じ <see cref="QuestionImageSource"/> で解決させる。
    /// </para>
    /// </remarks>
    public sealed class GameViewQuestionImageSceneTests
    {
        private const string LoopbackAddress = "127.0.0.1";
        private const string FirstImageText = "この画像に写っているものは？（1 問目）";
        private const string NoImageText = "画像の無い問題（2 問目）";
        private const string ThirdImageText = "この画像に写っているものは？（3 問目）";
        private const string Answer = "こたえ";

        /// <summary>
        /// 作業領域の縦幅（docs/architecture.md §10.2: 1600x900 基準の論理ビューポート 900px −
        /// <c>.screen-root</c> の padding 32px × 2）。
        /// </summary>
        private const float WorkingAreaHeight = 836f;

        /// <summary>レイアウト値の許容誤差（論理 px）。<see cref="GameViewLayoutProbe.LayoutTolerance"/> と同じ。</summary>
        private const float LayoutTolerance = GameViewLayoutProbe.LayoutTolerance;

        /// <summary>1 問目の画像（横長）。</summary>
        private const int FirstImageWidth = 320;
        private const int FirstImageHeight = 160;

        /// <summary>3 問目の画像（縦長）。</summary>
        private const int ThirdImageWidth = 120;
        private const int ThirdImageHeight = 480;

        private MainSceneTestHelpers.ConsentFileScope _consentScope;
        private RoomSettingsDraftScope _roomSettingsDraftScope;
        private string _questionsFolder;

        private GameObject _hostObject;
        private NetworkManager _hostManager;
        private NetworkService _hostService;
        private GameSession _hostSession;

        [SetUp]
        public void SetUp()
        {
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();
            _roomSettingsDraftScope = RoomSettingsDraftScope.Redirect();

            var store = ConsentGate.CreateDefaultStore();
            store.RecordConsent(TermsCatalog.LoadRequiredTerms(), Application.version, DateTime.UtcNow);

            _questionsFolder = Path.Combine(Path.GetTempPath(), "tq-i185-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_questionsFolder, "images"));
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
            // GameViewSceneTests と同じ順序（停止完了を待ってから破棄 → Main をアンロード → シングルトン破棄）。
            var bootService = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.Service : null;
            bootService?.Stop();
            _hostService?.Stop();

            yield return WaitUntil(
                () => !IsNetworkBusy(bootService) && !IsNetworkBusy(_hostService),
                DefaultTimeoutSeconds,
                "ホスト / クライアントの停止が完了しませんでした。");
            yield return null;

            _hostService?.Dispose();
            _hostService = null;
            _hostSession = null;
            _hostManager = null;

            if (_hostObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostObject);
                _hostObject = null;
            }

            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();

            TearDownMainSceneAndBootstrapSingletons();
        }

        /// <summary>
        /// クライアントの Game View で、画像付きの問題では画像が表示され、画像の無い問題では隠れ、
        /// 次の問題へ進むと前の問題の画像が残らない（差し替わる）こと。
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator ClientGameView_ShowsImage_HidesForNoImageQuestion_AndReplacesOnNextQuestion()
        {
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);

            WriteImage("q1.png", TestImageFactory.CreatePng(FirstImageWidth, FirstImageHeight, seed: 1));
            WriteImage("q3.png", TestImageFactory.CreatePng(ThirdImageWidth, ThirdImageHeight, seed: 3));
            var port = StartInProcessHost(new TestQuestionSource(
                ImageFreeText("q-img-1", FirstImageText, "images/q1.png"),
                TestQuestionSource.FreeText("q-img-2", NoImageText, Answer),
                ImageFreeText("q-img-3", ThirdImageText, "images/q3.png")));

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

            VisualElement container = null;
            Image image = null;
            Label questionText = null;
            yield return WaitForElement<VisualElement>(panelRoot, "question-image-container", found => container = found);
            yield return WaitForElement<Image>(panelRoot, "question-image", found => image = found);
            yield return WaitForElement<Label>(panelRoot, "question-text-sizer", found => questionText = found);

            Assert.AreEqual(DisplayStyle.None, container.resolvedStyle.display, "出題前は画像を出さないはず。");

            // --- 1 問目（画像あり） ---
            Assert.IsTrue(_hostSession.StartQuestion(0), "1 問目を出題できるはず。");
            yield return WaitForShownImage(container, image, panelRoot, "1 問目");

            Assert.IsTrue(clientSession.Distributor.TryGetImage(0, out var firstTexture), "クライアントが 1 問目の画像を持っているはず。");
            Assert.AreSame(firstTexture, image.image, "配信器が保持しているテクスチャをそのまま貼るはず。");
            Assert.AreEqual(FirstImageWidth, image.image.width);

            // --- 2 問目（画像なし） ---
            yield return WaitForResult(_hostSession);
            Assert.IsTrue(_hostSession.StartQuestion(1), "2 問目を出題できるはず。");
            yield return WaitUntil(
                () => questionText.text == NoImageText,
                DefaultTimeoutSeconds,
                () => $"2 問目が表示されませんでした（表示中: '{questionText.text}'）。",
                panelRoot);
            yield return null;

            Assert.AreEqual(
                DisplayStyle.None, container.resolvedStyle.display, "画像の無い問題では画像エリアを畳むはず（前問の画像を残さない）。");
            Assert.IsNull(image.image, "画像の無い問題ではテクスチャの参照も外すはず。");
            AssertHeaderAtTopOfColumn(panelRoot, "2 問目（画像なし）");

            // --- 3 問目（別の画像） ---
            yield return WaitForResult(_hostSession);
            Assert.IsTrue(_hostSession.StartQuestion(2), "3 問目を出題できるはず。");
            yield return WaitUntil(
                () => questionText.text == ThirdImageText,
                DefaultTimeoutSeconds,
                () => $"3 問目が表示されませんでした（表示中: '{questionText.text}'）。",
                panelRoot);
            yield return WaitForShownImage(container, image, panelRoot, "3 問目");

            Assert.IsTrue(clientSession.Distributor.TryGetImage(2, out var thirdTexture), "クライアントが 3 問目の画像を持っているはず。");
            Assert.AreSame(thirdTexture, image.image, "3 問目の画像に差し替わるはず。");
            Assert.AreEqual(ThirdImageHeight, image.image.height);
        }

        /// <summary>
        /// ホスト（通常プレイヤー）の画面（1600x900 基準の論理ビューポート）で、選択式 4 択の画像付きの問題では
        /// 画像エリアが中央列の最上部にあって残りの高さを埋め（#193）、回答中・判定後ともスクロールせずに
        /// 作業領域（836px）へ収まること。判定結果が出ると、その分だけ画像エリアが縮むこと。
        /// 画像の無い問題では画像エリアを畳み、問題パネルが中央列の上に詰まること。内訳をログに残す。
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator HostGameView_ChoiceWithImage_ImageAreaFillsRemainingHeight_AndFitsWorkingArea()
        {
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);

            var hostService = NetworkBootstrap.Instance.Service;
            NetworkStartResult startResult = default;
            yield return hostService.StartHostWhenReady(startPort: 0, onCompleted: r => startResult = r);
            Assert.IsTrue(startResult.Success, startResult.Message);

            var session = hostService.ActiveGameSession;
            Assert.IsNotNull(session, "ホスト開始で GameSession がスポーンされるはず。");

            WriteImage("choice.png", TestImageFactory.CreatePng(ThirdImageWidth, ThirdImageHeight, seed: 7));
            session.Configure(
                new TestQuestionSource(
                    ChoiceQuestion("q-img-choice", "images/choice.png"),
                    ChoiceQuestion("q-noimg-choice", null)),
                new QuizTimeLimits(
                    buzzTimeLimitSec: 1.0, answerTimeLimitSec: 1.0, choiceTimeLimitSec: 3.0, collectWindowSec: 0.15));
            Assert.IsTrue(session.ConfigureImages(new QuestionImageSource(_questionsFolder)), "画像の供給元を設定できるはず。");

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Game);

            VisualElement container = null;
            Image image = null;
            VisualElement gameRoot = null;
            yield return WaitForElement<VisualElement>(panelRoot, "question-image-container", found => container = found);
            yield return WaitForElement<Image>(panelRoot, "question-image", found => image = found);
            yield return WaitForElement<VisualElement>(panelRoot, "game-root", found => gameRoot = found);
            var main = panelRoot.Q<VisualElement>("game-content-main");
            var scroll = panelRoot.Q<ScrollView>("game-scroll-view");
            Assert.IsNotNull(main, "game-content-main が見つかりません。");
            Assert.IsNotNull(scroll, "game-scroll-view が見つかりません。");

            // バッチ実行の画面サイズは固定なので、game-root を 1600x900 基準の論理ビューポートに固定する
            // （GameViewVerticalFitSceneTests と同じ作法）。
            GameViewLayoutProbe.SetLogicalViewport(gameRoot, PanelScaleProbe.Viewport16By9);

            Assert.IsTrue(session.StartQuestion(0), "出題できるはず。");
            yield return WaitForShownImage(container, image, panelRoot, "選択式");

            // --- 回答中（選択肢 4 つ + 退出） ---
            yield return WaitUntil(
                () => session.Phase.Value == QuizPhase.ChoiceAnswering,
                DefaultTimeoutSeconds,
                () => $"選択式の回答受付が開きませんでした（現在: {session.Phase.Value}）。");
            yield return WaitForLayout();
            GameViewLayoutProbe.LogBreakdown("#193 実測", "画像エリア 回答中", gameRoot, main, scroll);
            AssertImageAreaLayout(container, image, main, scroll, gameRoot, "回答中");
            var answeringAreaHeight = container.layout.height;

            // --- 判定後（判定結果セクションと「次へ」も並ぶ、いちばん縦に長い状態） ---
            yield return WaitForResult(session);
            var resultSection = panelRoot.Q<VisualElement>("result-section");
            var hostControls = panelRoot.Q<VisualElement>("host-controls");
            yield return WaitUntil(
                () => resultSection.resolvedStyle.display == DisplayStyle.Flex
                      && hostControls.resolvedStyle.display == DisplayStyle.Flex,
                DefaultTimeoutSeconds,
                "判定後は判定結果セクションと「次へ」が表示されるはず。",
                panelRoot);
            yield return WaitForLayout();
            GameViewLayoutProbe.LogBreakdown("#193 実測", "画像エリア 判定後", gameRoot, main, scroll);

            Assert.AreEqual(DisplayStyle.Flex, container.resolvedStyle.display, "判定後も画像は出したままのはず。");
            AssertImageAreaLayout(container, image, main, scroll, gameRoot, "判定後");
            Assert.That(
                container.layout.height,
                Is.LessThan(answeringAreaHeight - LayoutTolerance),
                "判定結果が出た分だけ画像エリアが縮むはず（中央列の残りの高さを埋めるため）。");
            GameViewLayoutProbe.AssertFullyVisible(panelRoot.Q<Button>("next-button"), gameRoot, scroll, "判定後の次へ");
            GameViewLayoutProbe.AssertFullyVisible(panelRoot.Q<Button>("exit-button"), gameRoot, scroll, "判定後の退出");

            // --- 画像の無い問題: 画像エリアを畳み、問題パネルが中央列の上に詰まる ---
            Assert.IsTrue(session.StartQuestion(1), "2 問目を出題できるはず。");
            yield return WaitUntil(
                () => session.Phase.Value == QuizPhase.ChoiceAnswering,
                DefaultTimeoutSeconds,
                () => $"2 問目の回答受付が開きませんでした（現在: {session.Phase.Value}）。");
            yield return WaitForLayout();
            Assert.AreEqual(DisplayStyle.None, container.resolvedStyle.display, "画像の無い問題では画像エリアを畳むはず。");
            AssertHeaderAtTopOfColumn(panelRoot, "画像なしの選択式");
        }

        /// <summary>
        /// 画像エリアが中央列の最上部にあり、エリアの高さが上下限の範囲で、画像がエリアの内側いっぱいに
        /// ScaleToFit で置かれ、中央列の残りの高さを埋めて（上限に達していなければ下段が中央列の下端に届き）、
        /// 作業領域にスクロールなしで収まること。
        /// </summary>
        private static void AssertImageAreaLayout(
            VisualElement container, Image image, VisualElement main, ScrollView scroll, VisualElement gameRoot, string label)
        {
            AssertImageFillsArea(container, image, label);
            Assert.That(
                container.worldBound.yMin,
                Is.EqualTo(main.worldBound.yMin).Within(LayoutTolerance),
                $"{label}: 画像エリアは中央列の最上部に置くはず。");
            Assert.IsTrue(
                GameViewLayoutProbe.FitsWithoutScrolling(scroll),
                $"{label}: スクロールせずに収まるはず（本来の縦幅 {GameViewLayoutProbe.NaturalHeight(main, scroll):F1}px）。");

            var (top, bottom) = GameViewLayoutProbe.WorkingArea(gameRoot);
            Assert.That(bottom - top, Is.EqualTo(WorkingAreaHeight).Within(LayoutTolerance), "作業領域の前提（836px）が崩れている。");
            Assert.That(
                GameViewLayoutProbe.NaturalHeight(main, scroll),
                Is.LessThanOrEqualTo(WorkingAreaHeight + LayoutTolerance),
                $"{label}: 中央列の本来の縦幅は 1600x900 基準の作業領域に収まるはず（docs/architecture.md §10.2）。");
            if (container.layout.height < QuestionImageLayout.MaxAreaHeight - LayoutTolerance)
            {
                Assert.That(
                    GameViewLayoutProbe.NaturalHeight(main, scroll),
                    Is.EqualTo(main.layout.height).Within(LayoutTolerance),
                    $"{label}: 画像エリアが上限に達していない間は、中央列の残りの高さを画像エリアが埋めるはず。");
            }
        }

        /// <summary>画像が無いとき、問題パネル（game-header）が中央列の最上部に詰まっていること。</summary>
        private static void AssertHeaderAtTopOfColumn(VisualElement panelRoot, string label)
        {
            var main = panelRoot.Q<VisualElement>("game-content-main");
            var header = panelRoot.Q<VisualElement>("game-header");
            Assert.IsNotNull(header, "game-header が見つかりません。");
            Assert.That(
                header.worldBound.yMin,
                Is.EqualTo(main.worldBound.yMin).Within(LayoutTolerance),
                $"{label}: 画像エリアを畳み、問題パネルが中央列の上に詰まるはず。");
        }

        /// <summary>
        /// 画像エリアの高さが上下限（USS の min-height / max-height）の範囲にあり、画像がエリアの内側（padding の内側）
        /// いっぱいに広がって ScaleToFit で縦横比を保って描かれること。
        /// </summary>
        private static void AssertImageFillsArea(VisualElement container, Image image, string label)
        {
            Assert.AreEqual(ScaleMode.ScaleToFit, image.scaleMode, $"{label}: 縦横比を保って収めるはず。");
            Assert.That(
                container.layout.height,
                Is.InRange(
                    QuestionImageLayout.MinAreaHeight - LayoutTolerance, QuestionImageLayout.MaxAreaHeight + LayoutTolerance),
                $"{label}: 画像エリアの高さは上下限の範囲のはず。");
            Assert.That(
                image.layout.width,
                Is.EqualTo(container.layout.width - QuestionImageLayout.AreaPadding * 2f).Within(LayoutTolerance),
                $"{label}: 画像はエリアの内側の幅いっぱいに広げるはず。");
            Assert.That(
                image.layout.height,
                Is.EqualTo(container.layout.height - QuestionImageLayout.AreaPadding * 2f).Within(LayoutTolerance),
                $"{label}: 画像はエリアの内側の高さいっぱいに広げるはず。");
        }

        private static IEnumerator WaitForLayout()
        {
            for (var i = 0; i < 4; i++)
            {
                yield return null;
            }
        }

        private static Question ChoiceQuestion(string id, string imagePath)
        {
            return new Question(
                id,
                QuestionType.Choice,
                "画像を見て答えてください。この画像の説明として正しいものはどれでしょう？ 選択肢から 1 つ選んでください。",
                choices: new[] { "選択肢 A の説明文", "選択肢 B の説明文", "選択肢 C の説明文", "選択肢 D の説明文" },
                correctIndex: 1,
                imagePath: imagePath);
        }

        private static IEnumerator WaitForShownImage(
            VisualElement container, Image image, VisualElement panelRoot, string label)
        {
            yield return WaitUntil(
                () => container.resolvedStyle.display == DisplayStyle.Flex && image.image != null,
                DefaultTimeoutSeconds,
                () => $"{label}の画像が表示されませんでした（display: {container.resolvedStyle.display}、"
                      + $"image: {(image.image != null ? image.image.name : "null")}）。",
                panelRoot);

            // レイアウトの解決を待つ（1 フレームでは解決し切らないことがある）。
            yield return null;
            yield return null;

            Assert.Greater(image.layout.width, 0f, $"{label}の画像の幅が 0 のまま。");
            Assert.Greater(image.layout.height, 0f, $"{label}の画像の高さが 0 のまま。");
            AssertImageFillsArea(container, image, label);
        }

        private static IEnumerator WaitForResult(GameSession session)
        {
            yield return WaitUntil(
                () => session.Phase.Value == QuizPhase.Result,
                DefaultTimeoutSeconds,
                () => $"時間切れで Result へ進みませんでした（現在: {session.Phase.Value}）。");
        }

        private static Question ImageFreeText(string id, string text, string imagePath)
        {
            return new Question(id, QuestionType.FreeText, text, answers: new[] { Answer }, imagePath: imagePath);
        }

        private void WriteImage(string fileName, byte[] bytes)
        {
            File.WriteAllBytes(Path.Combine(_questionsFolder, "images", fileName), bytes);
        }

        /// <summary>
        /// 1 プロセス内にもう 1 つ <see cref="NetworkManager"/> を立て、指定した問題と画像の供給元を設定した
        /// ホストとして開始する（<see cref="GameViewSceneTests"/> の StartInProcessHost と同じ手順 +
        /// <see cref="GameSession.ConfigureImages"/>）。制限時間は短くして、時間切れで Result へ進める。
        /// </summary>
        private ushort StartInProcessHost(IQuestionSource questionSource)
        {
            _hostObject = new GameObject(nameof(GameViewQuestionImageSceneTests) + "-Host");
            _hostObject.SetActive(false);

            var transport = _hostObject.AddComponent<UnityTransport>();
            transport.MaxPayloadSize = NetworkConstants.MaxPayloadSizeBytes;

            _hostManager = _hostObject.AddComponent<NetworkManager>();
            _hostManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = null,
            };

            _hostManager.AddNetworkPrefab(NetworkTestPrefabs.LoadGameSession());
            _hostManager.AddNetworkPrefab(NetworkTestPrefabs.LoadLobbyState());

            _hostObject.SetActive(true);

            _hostService = new NetworkService(_hostManager);
            var result = _hostService.StartHost(startPort: 0);
            Assert.IsTrue(result.Success, result.Message);

            _hostSession = _hostService.ActiveGameSession;
            Assert.IsNotNull(_hostSession, "ホスト開始時に GameSession がスポーンされるはず。");
            _hostSession.Configure(
                questionSource,
                limits: new QuizTimeLimits(buzzTimeLimitSec: 1.0, answerTimeLimitSec: 1.0, collectWindowSec: 0.15));
            Assert.IsTrue(
                _hostSession.ConfigureImages(new QuestionImageSource(_questionsFolder)), "画像の供給元を設定できるはず。");

            return _hostService.ActivePort;
        }

        private static bool IsNetworkBusy(NetworkService service)
            => service != null && (service.IsListening || service.IsClient || service.IsShutdownInProgress);
    }
}
