using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// ゲームプレイ中に利用規約の同意を撤回したときの立ち絵の挙動（issue #139、
    /// requirements.md FR-74 / FR-75 / NFR-08）を、実ネットワーク経路の <see cref="GameSession"/> で固定する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 読み上げ側の同種のテスト（<c>TtsConsentRevocationTests</c>、#127）と対になる。
    /// <see cref="CharacterView"/> は <c>consentCheck</c> を渡さず既定
    /// （<c>TtsConsentCheckFactory.Build()</c> = <see cref="ConsentGate.HasUserConsented"/>）のまま使い、
    /// 実際の <c>consent.json</c>（テスト用データルート配下、#71）を消すことで撤回を再現する。
    /// </para>
    /// <para>
    /// 立ち絵画像そのものは二次配布禁止（docs/licenses.md §3）でリポジトリに置けないため、
    /// <c>textureResolver</c> にはテスト内で作った小さな <see cref="Texture2D"/> を返すスタブを渡す
    /// （画像の解決経路は <c>CharacterExpressionSwitchTests</c> が担当）。
    /// </para>
    /// </remarks>
    public sealed class CharacterConsentRevocationTests : GameSessionTestFixture
    {
        private const string TemplatePath = "Assets/TsumugiQuiz/UI/Templates/character-view.uxml";
        private const string PanelSettingsPath = "Assets/TsumugiQuiz/Settings/panel-settings.asset";

        private const int Seed = 20260918;

        /// <summary>
        /// 1 問目を誰も押さずにタイムアウトさせ、結果表示から 0.5 秒で 2 問目へ自動進行させる制限時間
        /// （<c>TtsConsentRevocationTests</c> と同じ値）。
        /// </summary>
        private static QuizTimeLimits FastLimits =>
            new QuizTimeLimits(buzzTimeLimitSec: 3.0, answerTimeLimitSec: 3.0, collectWindowSec: 0.15);

        private GameObject _documentObject;
        private UIDocument _document;
        private ConsentFileScope _consentScope;
        private Texture2D _texture;

        /// <summary>購読解除のために覚えておく <c>QuestionShown</c> のハンドラ。</summary>
        private GameSession _questionShownSession;
        private Action<int, QuestionDto, QuestionShownSource> _questionShownHandler;

        [SetUp]
        public void CreateDocumentAndSeedConsent()
        {
#if UNITY_EDITOR
            var template = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TemplatePath);
            Assert.IsNotNull(template, $"{TemplatePath} が見つかりません。");

            var panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            Assert.IsNotNull(panelSettings, $"{PanelSettingsPath} が見つかりません。");

            // 実際の consent.json（テスト用データルート配下、#71）を退避してから同意済み状態を作る。
            _consentScope = ConsentFileScope.Backup();
            ConsentGate.CreateDefaultStore()
                .RecordConsent(TermsCatalog.LoadRequiredTerms(), Application.version, DateTime.UtcNow);

            _documentObject = new GameObject(nameof(CharacterConsentRevocationTests));
            _document = _documentObject.AddComponent<UIDocument>();
            _document.panelSettings = panelSettings;
            Assert.IsNotNull(_document.rootVisualElement, "UIDocument.rootVisualElement が null です。");
            _document.rootVisualElement.Add(template.Instantiate());

            _texture = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false);
#else
            Assert.Ignore("PlayMode テストは Editor 上でのみ実行する（AssetDatabase を使うため）。");
#endif
        }

        [TearDown]
        public void DestroyDocumentAndRestoreConsent()
        {
            if (_questionShownSession != null && _questionShownHandler != null)
            {
                _questionShownSession.QuestionShown -= _questionShownHandler;
            }

            _questionShownSession = null;
            _questionShownHandler = null;

            if (_documentObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_documentObject);
            }

            _documentObject = null;
            _document = null;

            if (_texture != null)
            {
                UnityEngine.Object.DestroyImmediate(_texture);
                _texture = null;
            }

            _consentScope?.Restore();
            _consentScope = null;
        }

        [UnityTest]
        [Timeout(90000)]
        public IEnumerator 同意を撤回すると次の出題で立ち絵が消える()
        {
            // docs/dev-workflow.md §3.5: 自前で UIDocument を作るテストでも、要素を掴んだまま
            // 複数フレーム待つならライブリロードを止める（#108）。本テストはネットワークの
            // 接続待ちで数百フレーム回る。
            yield return DisableLiveReloadWhenPanelReady(_document);

            yield return ConnectHostAndClient(TwoQuestionSource());

            var shown = new List<int>();
            _questionShownSession = ClientSession;
            _questionShownHandler = (index, _, _) => shown.Add(index);
            ClientSession.QuestionShown += _questionShownHandler;

            // consentCheck は渡さない（既定 = TtsConsentCheckFactory.Build()）。本番と同じ経路で
            // consent.json を読ませるため。
            var view = new CharacterView(GetCharacterRoot(), _ => _texture);

            try
            {
                yield return null;
                Assert.IsTrue(view.IsVisible, "同意済み・画像ありなので表示されるはず。");

                view.Bind(ttsSyncPlayer: null, gameSession: ClientSession);

                // --- 1 問目 ---
                Assert.IsTrue(HostSession.StartSession(FastSettings(), Seed), "セッションを開始できるはず。");
                yield return WaitUntil(() => shown.Count >= 1, () => "1 問目が提示されませんでした。");
                yield return null;
                Assert.IsTrue(view.IsVisible, "1 問目の時点では同意済みなので表示されたまま。");

                // --- 同意を撤回（設定・クレジット画面からの撤回に相当。FR-75） ---
                _consentScope.DeleteCurrentFile();

                yield return WaitFrames(5);
                Assert.IsTrue(
                    view.IsVisible,
                    "撤回した瞬間に消えるのではなく、次の出題で評価し直す仕様（毎フレーム consent.json を読まない）。");

                // --- 2 問目（1 問目のタイムアウト → 自動進行） ---
                yield return WaitUntil(
                    () => shown.Count >= 2,
                    () => $"2 問目へ自動進行しませんでした（提示 {shown.Count} 件 / "
                          + $"問題 {ClientSession.QuestionIndex.Value} / フェーズ {ClientSession.Phase.Value}）。");
                yield return null;

                Assert.IsFalse(
                    view.IsVisible,
                    "撤回後に提示された問題では立ち絵を出さないこと（FR-74 / FR-75、#139）。");
                Assert.IsNull(
                    GetCharacterImage().image,
                    "非表示にするだけでなくテクスチャの参照も手放すこと（PR #135 レビュー L4）。");

                // --- 同意し直せば戻る ---
                ConsentGate.CreateDefaultStore()
                    .RecordConsent(TermsCatalog.LoadRequiredTerms(), Application.version, DateTime.UtcNow);
                view.RefreshVisibility();
                yield return null;

                Assert.IsTrue(view.IsVisible, "同意し直せば立ち絵に戻るはず（FR-75）。");
            }
            finally
            {
                view.Dispose();
            }
        }

        /// <summary>
        /// 撤回操作の画面（Terms / Credits）から Game View へ戻る経路。<c>GameView</c> は View の表示ごとに
        /// 新しい <see cref="CharacterView"/> を生成する（<c>ViewControllerRegistry</c>）ため、
        /// 構築時の評価で非表示になることを確認する（issue #139）。
        /// </summary>
        [UnityTest]
        public IEnumerator 撤回後に立ち絵を作り直すと最初から非表示()
        {
            yield return DisableLiveReloadWhenPanelReady(_document);

            var before = new CharacterView(GetCharacterRoot(), _ => _texture);
            yield return null;
            Assert.IsTrue(before.IsVisible, "撤回前は表示されるはず。");
            before.Dispose();

            _consentScope.DeleteCurrentFile();

            var after = new CharacterView(GetCharacterRoot(), _ => _texture);

            try
            {
                yield return null;
                Assert.IsFalse(after.IsVisible, "撤回後に Game View へ戻れば、構築時の評価で非表示になるはず。");
                Assert.IsNull(GetCharacterImage().image, "テクスチャも適用されないこと。");
            }
            finally
            {
                after.Dispose();
            }
        }

        private VisualElement GetCharacterRoot()
        {
            var root = _document.rootVisualElement.Q<VisualElement>("character-root");
            Assert.IsNotNull(root, "character-root がロードした UXML から見つかりません。");
            return root;
        }

        private Image GetCharacterImage() => GetCharacterRoot().Q<Image>("character-image");

        private static SessionSettings FastSettings() =>
            new SessionSettings(
                new QuestionSelectionSettings(count: QuestionSelectionSettings.AllQuestions, shuffleOrder: false),
                FastLimits,
                ScoringSettings.Default.WithReopenAfterWrongAnswer(false),
                resultAutoAdvanceSec: 0.5);

        private static IQuestionSource TwoQuestionSource() =>
            new TestQuestionSource(
                TestQuestionSource.FreeText("q-character-1", QuestionText, CorrectAnswer),
                TestQuestionSource.FreeText("q-character-2", "日本でいちばん高い山は？", "ふじさん"));
    }
}
