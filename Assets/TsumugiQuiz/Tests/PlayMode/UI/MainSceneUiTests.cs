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
    /// Main シーンをロードし、Title View の表示・ViewRouter によるボタン駆動の画面切替・
    /// 共通テーマ（USS カスタムプロパティ）の解決を確認する PlayMode テスト。
    /// issue #37 で Main シーンの初期 View が利用規約の同意状況（ConsentStore）で
    /// Terms / Title に振り分けられるようになったため、このテスト群では事前に「同意済み」の
    /// consent.json を用意して Title が表示される状態を維持する
    /// （未同意時に Terms が表示されることは <see cref="TermsConsentSceneTests"/> で検証する）。
    /// テスト前後で実際の consent.json（Application.persistentDataPath 配下）は退避・復元する。
    /// </summary>
    public class MainSceneUiTests
    {
        private MainSceneTestHelpers.ConsentFileScope _consentScope;

        [SetUp]
        public void SeedConsentedState()
        {
            // 退避（バックアップの読み取り）が完了してから初めて consent.json を書き換える（M-8）。
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
        public IEnumerator MainScene_ShowsTitleView_AndSwitchesViewOnButtonClick()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            Assert.IsNotNull(hostButton, "Title View の host-button が見つかりません。同意済みなのに Title View が表示されていない可能性があります。");

            yield return SimulateClickRoutine(hostButton);

            Button backButton = null;
            yield return WaitForElement<Button>(panelRoot, "back-button", found => backButton = found);
            Assert.IsNotNull(backButton, "host-button クリック後にプレースホルダ View の back-button が見つかりません。View が切り替わっていない可能性があります。");
            Assert.IsNull(panelRoot.Q<Button>("host-button"), "View 切替後も Title View の要素が残っています。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TitleView_NavigateAway_ThenGoBack_ReturnsToTitle()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            Assert.IsNotNull(hostButton, "host-button が見つかりません。");

            yield return SimulateClickRoutine(hostButton);

            Button backButton = null;
            yield return WaitForElement<Button>(panelRoot, "back-button", found => backButton = found);
            Assert.IsNotNull(backButton, "back-button が見つかりません。");
            Assert.IsTrue(backButton.enabledSelf, "host-setup（未実装 View）に遷移した直後は履歴が2件になり、戻るボタンが有効になっているはずです。");

            yield return SimulateClickRoutine(backButton);

            Button hostButtonAfterBack = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButtonAfterBack = found);
            Assert.IsNotNull(hostButtonAfterBack, "戻るボタンのクリック後に Title View（host-button）へ戻っていません。");
            Assert.IsNull(panelRoot.Q<Button>("back-button"), "戻った後も Placeholder View の要素が残っています。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TitleView_ScreenRoot_ResolvesThemeBackgroundColorFromCustomProperty()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            VisualElement titleRoot = null;
            yield return WaitForElement<VisualElement>(panelRoot, "title-root", found => titleRoot = found);
            Assert.IsNotNull(titleRoot, "title-root が見つかりません。");

            // theme.uss の --color-background: #f6ece2（#132 で素材由来のライト基調パレットへ変更）が
            // 実際に解決されていることを確認する。
            // :root のカスタムプロパティは PanelSettings のテーマ（tsumugi-theme.tss）経由でないと
            // TemplateContainer 配下では解決されない問題があったため（H-1 の回帰確認）。
            var backgroundColor = titleRoot.resolvedStyle.backgroundColor;
            Assert.That(backgroundColor.r, Is.EqualTo(0xf6 / 255f).Within(0.02f), "背景色の R 成分が theme.uss の --color-background と一致しません。");
            Assert.That(backgroundColor.g, Is.EqualTo(0xec / 255f).Within(0.02f), "背景色の G 成分が theme.uss の --color-background と一致しません。");
            Assert.That(backgroundColor.b, Is.EqualTo(0xe2 / 255f).Within(0.02f), "背景色の B 成分が theme.uss の --color-background と一致しません。");
        }

        /// <summary>
        /// #132 レビュー L-7: theme.uss だけでなく、画面別に分割したスタイルシート
        /// （theme-views-misc.uss の <c>.unity-button.tts-status-corner</c>）の値も
        /// 実行時に解決されていることを確認する。tsumugi-theme.tss の @import 追加漏れや
        /// 分割ファイルの取りこぼしを PlayMode 側でも検出できるようにする。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TitleView_TtsStatusCorner_ResolvesThemeViewsSurfaceColor()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            Button ttsStatusButton = null;
            yield return WaitForElement<Button>(panelRoot, "tts-status-button", found => ttsStatusButton = found);
            Assert.IsNotNull(ttsStatusButton, "tts-status-button が見つかりません。");

            // theme-views-misc.uss: .unity-button.tts-status-corner { background-color: var(--color-surface); }
            // --color-surface は #fffaf4（#132）。
            var backgroundColor = ttsStatusButton.resolvedStyle.backgroundColor;
            Assert.That(backgroundColor.r, Is.EqualTo(0xff / 255f).Within(0.02f),
                "tts-status-corner の背景 R 成分が theme-views-misc.uss の --color-surface と一致しません"
                + "（画面別 uss がテーマに読み込まれていない可能性があります）。");
            Assert.That(backgroundColor.g, Is.EqualTo(0xfa / 255f).Within(0.02f),
                "tts-status-corner の背景 G 成分が --color-surface と一致しません。");
            Assert.That(backgroundColor.b, Is.EqualTo(0xf4 / 255f).Within(0.02f),
                "tts-status-corner の背景 B 成分が --color-surface と一致しません。");
        }
    }
}
