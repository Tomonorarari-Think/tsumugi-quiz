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
    /// ViewRouter.SwapTo() が VisualTreeAsset.Instantiate() で生成する TemplateContainer に付与する
    /// <c>view-container</c>（theme.uss）が、Document.rootVisualElement いっぱいに伸びることの検証（#105）。
    /// 加えて、実際に各 View のルート要素（<c>title-root</c> / <c>host-setup-root</c>）自身も
    /// 画面全体まで伸びることを検証する（#105 レビュー M-1。view-container 自体が伸びていても、
    /// 各 View のルート要素側に <c>screen-root</c> 系クラスが付いていなければ、そのルート要素は
    /// 中身に合わせて縮んでしまい、症状は再発する）。
    ///
    /// TemplateContainer は既定では中身に合わせて縮む（height: auto）ため、view-container が無いと
    /// 各 View のルート要素（.screen-root 等）の flex-grow: 1 が効かず、ウィンドウ上部の一部にしか
    /// UI パネルが収まらない回帰が起きる（#105 の症状そのもの）。
    /// </summary>
    public sealed class ViewContainerSizingTests
    {
        /// <summary>
        /// theme.uss の <c>--color-background</c>（#f6ece2。#132 で素材由来のライト基調パレットに変更）。
        /// #113 レビュー L-3:
        /// ルート要素が実際に不透明な背景色で塗られていることの検証に使う
        /// （幅・高さが一致していても、background-color 自体が無ければ #105/#113 の症状は再発するため）。
        /// #113 レビュー A-4: 比較は <see cref="Color32"/>（8bit）同士で行う（<see cref="Color"/> の
        /// float 同士の厳密一致は、色空間変換の丸め誤差でまれに false negative になりうるため）。
        /// </summary>
        private static readonly Color32 ExpectedBackgroundColor = ParseColor("#f6ece2");

        /// <summary>
        /// HTML カラー文字列をパースする。#113 レビュー A-5: 静的フィールド初期化子から
        /// <see cref="Assert"/> を呼ぶとテストランナーの外（型初期化時）で例外の扱いが不安定になるため、
        /// パース失敗時は通常の例外を明示的に投げる（本テストのソース内の固定文字列が対象であり、
        /// 実行時入力ではないため、失敗は実装ミスとして即座に検出できればよい）。
        /// </summary>
        private static Color32 ParseColor(string html)
        {
            if (!ColorUtility.TryParseHtmlString(html, out var color))
            {
                throw new System.ArgumentException($"色のパースに失敗しました: {html}", nameof(html));
            }

            return color;
        }

        private MainSceneTestHelpers.ConsentFileScope _consentScope;
        private MainSceneTestHelpers.AppSettingsFileScope _appSettingsScope;

        [SetUp]
        public void SeedConsentedState()
        {
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();
            // #105 レビュー M-1: HostSetupRootはDocumentRootVisualElement全体を覆う() で
            // ShowView(ViewNames.HostSetup) すると HostSetupView.OnShow → LoadPreferences が、
            // 旧 PlayerPrefs キーが存在する場合に実際の app-settings.json を書き換えてしまう
            // （HostSetupFlowTests / JoinViewSceneTests と同じ理由）。他テストへ影響しないよう退避・復元する。
            _appSettingsScope = MainSceneTestHelpers.AppSettingsFileScope.Backup();

            var store = ConsentGate.CreateDefaultStore();
            var requiredTerms = TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, System.DateTime.UtcNow);
        }

        [TearDown]
        public void RestoreConsentFile()
        {
            _consentScope?.Restore();
            _appSettingsScope?.Restore();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();

            // #113 レビュー L-2: GameRootはDocumentRootVisualElement全体を覆う() が
            // router.ShowView(ViewNames.Game) で GameView を表示すると、OnShow が
            // schedule.Execute(Tick).Every(...) で繰り返しの再探索コールバックを仕込む
            // （#95 レビュー M-9）。JoinViewSceneTests 等の Boot 経由テストと同じく、
            // シーンアンロード後に明示的に呼んで ViewRouter/常駐シングルトンの後片付けを揃える。
            TearDownMainSceneAndBootstrapSingletons();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ViewコンテナはDocumentRootVisualElement全体を覆う()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            VisualElement viewContainer = null;
            yield return WaitUntil(
                () =>
                {
                    viewContainer = panelRoot.Q<VisualElement>(className: "view-container");
                    return viewContainer != null;
                },
                "'view-container' クラスの要素が見つかりません。",
                dumpRoot: panelRoot);

            // レイアウト解決を待つ。
            yield return null;
            yield return null;

            // #105 レビュー M-2: レイアウト未解決のまま両辺が 0 == 0 で一致してしまう空振りを防ぐ。
            Assert.That(panelRoot.resolvedStyle.height, Is.GreaterThan(0f),
                "panelRoot.resolvedStyle.height が 0 のままです（レイアウトが未解決の可能性）。");

            Assert.That(viewContainer.parent, Is.SameAs(panelRoot),
                "view-container は Document.rootVisualElement の直下に追加されていること。");
            Assert.That(viewContainer.resolvedStyle.width, Is.EqualTo(panelRoot.resolvedStyle.width).Within(1f),
                "view-container の幅が画面全体と一致すること。");
            Assert.That(viewContainer.resolvedStyle.height, Is.EqualTo(panelRoot.resolvedStyle.height).Within(1f),
                "view-container の高さが画面全体と一致すること（#105）。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TitleRootはDocumentRootVisualElement全体を覆う()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            yield return AssertScreenRootFillsPanel(panelRoot, "title-root");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator HostSetupRootはDocumentRootVisualElement全体を覆う()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            var router = Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.HostSetup);

            yield return AssertScreenRootFillsPanel(panelRoot, "host-setup-root");
        }

        /// <summary>
        /// #113: game-view.uxml のルートに screen-root を併用した後も画面全体まで伸びることの検証。
        /// GameView.OnShow は GameSession が見つからなくても表示できる（TryAcquireSession が
        /// 「セッションを探しています…」を出すだけ）ため、ネットワークのセットアップは不要
        /// （HostSetupRootはDocumentRootVisualElement全体を覆う と同じ最小構成）。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator GameRootはDocumentRootVisualElement全体を覆う()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            var router = Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Game);

            yield return AssertScreenRootFillsPanel(panelRoot, "game-root");
        }

        /// <summary>
        /// <paramref name="rootElementName"/> の要素（View 側のルート要素。<c>screen-root</c> 系クラスで
        /// flex-grow: 1 が付いている想定）が <paramref name="panelRoot"/> と同じ高さ・幅まで
        /// 伸びていること、かつ実際に不透明な背景色で塗られていることを検証する共通処理
        /// （#105 レビュー M-1・M-2、#113 レビュー L-3）。
        /// </summary>
        private static IEnumerator AssertScreenRootFillsPanel(VisualElement panelRoot, string rootElementName)
        {
            VisualElement screenRoot = null;
            yield return WaitUntil(
                () =>
                {
                    screenRoot = panelRoot.Q<VisualElement>(rootElementName);
                    return screenRoot != null;
                },
                $"'{rootElementName}' が見つかりません。",
                dumpRoot: panelRoot);

            // レイアウト解決を待つ。
            yield return null;
            yield return null;

            // #105 レビュー M-2: レイアウト未解決のまま両辺が 0 == 0 で一致してしまう空振りを防ぐ。
            Assert.That(panelRoot.resolvedStyle.height, Is.GreaterThan(0f),
                "panelRoot.resolvedStyle.height が 0 のままです（レイアウトが未解決の可能性）。");

            Assert.That(screenRoot.resolvedStyle.width, Is.EqualTo(panelRoot.resolvedStyle.width).Within(1f),
                $"'{rootElementName}' の幅が画面全体と一致すること。");
            Assert.That(screenRoot.resolvedStyle.height, Is.EqualTo(panelRoot.resolvedStyle.height).Within(1f),
                $"'{rootElementName}' の高さが画面全体と一致すること（#105）。");

            // #113 レビュー L-3: 幅・高さが一致していても background-color が透明（既定の
            // Color.clear）のままだと未塗装領域は再発するため、実際に --color-background で
            // 塗られていることも確認する。
            // #113 レビュー A-4: Color（float）同士の厳密一致ではなく Color32（8bit）に変換して比較する。
            Color32 actualBackgroundColor = screenRoot.resolvedStyle.backgroundColor;
            Assert.That(actualBackgroundColor, Is.EqualTo(ExpectedBackgroundColor),
                $"'{rootElementName}' の背景色が --color-background と一致しません（実際: {actualBackgroundColor}）。");
        }
    }
}
