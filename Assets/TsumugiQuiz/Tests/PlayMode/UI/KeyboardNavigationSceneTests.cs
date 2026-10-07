using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// キーボードによる UI 操作（Tab / Shift+Tab でのフォーカス移動、Enter での実行）の回帰テスト（issue #148）。
    ///
    /// <para>
    /// <b>入力の経路</b>: 本プロジェクトのシーンには <c>EventSystem</c>（com.unity.ugui）も
    /// <c>InputSystemUIInputModule</c> も置いていない。Unity 2023.2 以降の UI Toolkit はそれらが無くても
    /// <c>UnityEngine.InputForUI</c> 経由で入力を受け取り（Input System のドキュメント
    /// <c>understand-ui-compatibility.md</c>「UI Toolkit (2023.2+) … UI Input Module component: Not required」）、
    /// その実装 <c>InputSystemProvider</c>（com.unity.inputsystem の
    /// <c>InputSystem/Runtime/Plugins/InputForUI/InputSystemProvider.cs</c>）が
    /// <c>&lt;Keyboard&gt;/tab</c> を Next / Previous（Shift 併用）の <see cref="NavigationMoveEvent"/> に、
    /// プロジェクト全体のアクション（<c>Assets/Settings/InputSystem_Actions.inputactions</c>）の
    /// <c>UI/Submit</c> を <see cref="NavigationSubmitEvent"/> に変換して配送する。
    /// ビルドしたプレイヤーでは Tab / Enter がこの経路で効くことを実測済み（docs/architecture.md §10.9）。
    /// </para>
    ///
    /// <para>
    /// <b>このテストで確かめる範囲</b>: batchmode の Editor ではアプリがフォーカスを持たない
    /// （<c>Application.isFocused == false</c>）ため、Input System は仮想キーボードの状態を処理せず、
    /// UI Toolkit の既定イベントシステムもフォーカスの無いアプリへのイベントを捨てる（実測）。
    /// そのためキー → InputForUI の区間は PlayMode テストで再現できない。ここでは
    /// (1) InputForUI が送るのと同じ <see cref="NavigationMoveEvent"/> でフォーカスが期待どおり動くこと、
    /// (2) InputForUI が読む <c>UI/Submit</c> の割り当てが Enter を含み Space を含まないこと
    /// を確認する。
    /// </para>
    ///
    /// <para>
    /// Space が <c>UI/Submit</c> に含まれないのは、早押し（<c>GameView.Input.cs</c> の
    /// <c>&lt;Keyboard&gt;/space</c>）と衝突させないための前提である。#14 レビュー M-6 で Game 画面の
    /// 「次へ」「退出」を <c>focusable="false"</c> にした意図と合わせてここで固定する。
    /// </para>
    /// </summary>
    public class KeyboardNavigationSceneTests
    {
        /// <summary>フォーカス移動を待つ上限（秒）。</summary>
        private const float NavigationTimeoutSeconds = 5f;

        /// <summary>Game 画面で Next を送る回数（フォーカスできる要素を 1 周以上回る回数）。</summary>
        private const int GameViewNavigationSteps = 10;

        /// <summary>InputForUI が <see cref="NavigationSubmitEvent"/> の元にするアクション（InputSystemProvider.Actions.SubmitAction）。</summary>
        private const string SubmitActionPath = "UI/Submit";

        /// <summary>InputForUI が方向キーのナビゲーションに使うアクション（InputSystemProvider.Actions.MoveAction）。</summary>
        private const string NavigateActionPath = "UI/Navigate";

        /// <summary>Input System がプロジェクト全体のアクションを記録する EditorBuildSettings のキー（ProjectWideActionsBuildProvider.EditorBuildSettingsActionsConfigKey）。</summary>
        private const string ProjectWideActionsConfigKey = "com.unity.input.settings.actions";

        private MainSceneTestHelpers.ConsentFileScope _consentScope;

        [SetUp]
        public void SeedConsentedState()
        {
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
        public IEnumerator TearDownScene()
        {
            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();
        }

        /// <summary>
        /// Tab 相当の <see cref="NavigationMoveEvent.Direction.Next"/> で Title の次のボタンへ
        /// フォーカスが移り、Shift+Tab 相当の Previous で戻る。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TitleView_NavigationMoveNextAndPrevious_MovesFocusBetweenButtons()
        {
            var title = new TitleButtons();
            yield return LoadTitle(title);

            title.Host.Focus();
            Assert.AreSame(title.Host, FocusedElement(title.Host), "Focus() で host-button にフォーカスが当たりませんでした。");

            SendNavigationMove(title.Host, NavigationMoveEvent.Direction.Next);
            yield return WaitForFocus(title.Join, "Next のナビゲーションで host-button → join-button へ移りませんでした。");

            SendNavigationMove(title.Join, NavigationMoveEvent.Direction.Previous);
            yield return WaitForFocus(title.Host, "Previous のナビゲーションで join-button → host-button へ戻りませんでした。");
        }

        /// <summary>
        /// どこにもフォーカスが無い状態（起動直後の Title）から Next を送ると、主要導線の「ホストとして開始」に
        /// 最初にフォーカスが当たる。フォーカス順は視覚ツリーの深さ優先順のため、title-view.uxml で右上の
        /// 読み上げ状態ボタンをメニューの後ろに置いている（#148。以前は 1 回目の Tab で読み上げ状態ボタンに当たっていた）。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TitleView_NavigationMoveNextWithoutFocus_FocusesHostButtonFirst()
        {
            var title = new TitleButtons();
            yield return LoadTitle(title);

            var panelRoot = title.Host.panel.visualTree;
            title.Host.panel.focusController.focusedElement?.Blur();
            Assert.IsNull(FocusedElement(title.Host), "前提: フォーカスがどこにも無い状態にできませんでした。");

            SendNavigationMove(panelRoot, NavigationMoveEvent.Direction.Next);
            yield return WaitForFocus(title.Host, "フォーカスが無い状態から Next を送っても最初に host-button へフォーカスが当たりませんでした。");
        }

        /// <summary>
        /// 読み上げ状態ボタンが Tab の巡回に含まれる（「クレジット」の次に到達できる）ことを確認する。
        /// 巡回は末尾から先頭へ折り返すため、読み上げ状態ボタンが先頭にあった旧い並び順でもこのテストは通る。
        /// 並び順（主要導線が先）の回帰は <see cref="TitleView_NavigationMoveNextWithoutFocus_FocusesHostButtonFirst"/> で検出する。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TitleView_NavigationMoveNextFromCredits_ReachesTtsStatusButton()
        {
            var title = new TitleButtons();
            yield return LoadTitle(title);

            title.Credits.Focus();
            SendNavigationMove(title.Credits, NavigationMoveEvent.Direction.Next);
            yield return WaitForFocus(title.TtsStatus, "credits-button の次に tts-status-button へフォーカスが移りませんでした。");
        }

        /// <summary>
        /// InputForUI が読む <c>UI/Submit</c> は Enter を含み、Space を含まない。
        /// Space を足すと、Game 画面でフォーカスを持つボタン（早押しボタン等）が早押しキーと同時に実行される。
        /// </summary>
        [Test]
        public void ProjectWideUiSubmit_IncludesEnter_ButNotSpace()
        {
            var submit = FindProjectWideAction(SubmitActionPath);
            var keyboard = InputSystem.AddDevice<Keyboard>(nameof(KeyboardNavigationSceneTests) + "Keyboard");
            try
            {
                Assert.IsTrue(IsBoundTo(submit, keyboard.enterKey),
                    $"{SubmitActionPath} に Enter が含まれていません。キーボードでボタンを実行できなくなります。");
                Assert.IsFalse(IsBoundTo(submit, keyboard.spaceKey),
                    $"{SubmitActionPath} に Space が含まれています。早押しキー（Space）とボタンの実行が衝突します。");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        /// <summary>
        /// InputForUI は <c>UI</c> マップを持つプロジェクト全体のアクションを優先して使う
        /// （無ければパッケージ既定の DefaultInputActions に切り替わる。InputSystemProvider.SelectInputActionAsset）。
        /// 前提となる <c>UI/Submit</c> / <c>UI/Navigate</c> が存在することを確認する。
        /// </summary>
        [Test]
        public void ProjectWideActions_HaveUiMapUsedByInputForUi()
        {
            Assert.IsNotNull(LoadProjectWideActions().FindActionMap("UI"), "プロジェクト全体の Input Actions に UI マップがありません。");
            FindProjectWideAction(SubmitActionPath);
            FindProjectWideAction(NavigateActionPath);
        }

        /// <summary>
        /// Game 画面の「次へ」「退出」はキーボードのナビゲーションでフォーカスされない（#14 レビュー M-6 の
        /// <c>focusable="false"</c>）。Next を 1 周以上回しても到達しないことを確認する。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator GameView_NavigationMoveNext_NeverFocusesNextOrExitButton()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);
            var router = FindRouter();
            router.ShowView(ViewNames.Game);

            Button buzzButton = null;
            Button nextButton = null;
            Button exitButton = null;
            VisualElement hostControls = null;
            yield return WaitForElement<Button>(panelRoot, "buzz-button", found => buzzButton = found);
            yield return WaitForElement<Button>(panelRoot, "next-button", found => nextButton = found);
            yield return WaitForElement<Button>(panelRoot, "exit-button", found => exitButton = found);
            yield return WaitForElement<VisualElement>(panelRoot, "host-controls", found => hostControls = found);
            yield return WaitForPanelAttachment(exitButton);

            Assert.IsFalse(nextButton.focusable, "next-button は focusable=\"false\" のはずです（#14 レビュー M-6）。");
            Assert.IsFalse(exitButton.focusable, "exit-button は focusable=\"false\" のはずです（#14 レビュー M-6）。");

            var focusController = panelRoot.panel.focusController;
            var visited = new List<Focusable>();
            for (var i = 0; i < GameViewNavigationSteps; i++)
            {
                // ネットワーク無しで開いた Game 画面は GameSession が無く、早押しボタンは無効・「次へ」は非表示の
                // ままになる。そのままではフォーカスできる要素が 1 つも無く検証が空振りするため、
                // 早押し受付中のホスト画面に相当する状態（早押しボタン有効・「次へ」表示）を毎回作り直す
                // （GameView の定期更新が元に戻す可能性があるため、送出の直前に設定する）。
                buzzButton.SetEnabled(true);
                hostControls.style.display = DisplayStyle.Flex;

                var target = focusController.focusedElement as VisualElement ?? panelRoot.panel.visualTree;
                SendNavigationMove(target, NavigationMoveEvent.Direction.Next);
                yield return null;

                var focused = focusController.focusedElement;
                visited.Add(focused);
                Assert.AreNotSame(nextButton, focused, $"Next {i + 1} 回目で next-button にフォーカスが当たりました。");
                Assert.AreNotSame(exitButton, focused, $"Next {i + 1} 回目で exit-button にフォーカスが当たりました。");
            }

            // 何にもフォーカスが当たらないまま終わると検証が空振りになるため、到達先を確認する。
            Assert.IsTrue(visited.Any(f => ReferenceEquals(f, buzzButton)),
                "Next を送っても早押しボタンにフォーカスが当たりませんでした（検証が成立していません）。");
            Assert.AreEqual(ViewNames.Game, router.CurrentViewName, "ナビゲーションで Game 画面から遷移してしまいました。");
        }

        private sealed class TitleButtons
        {
            public Button Host;
            public Button Join;
            public Button Credits;
            public Button TtsStatus;
        }

        private static IEnumerator LoadTitle(TitleButtons buttons)
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            yield return WaitForElement<Button>(panelRoot, "host-button", found => buttons.Host = found);
            yield return WaitForElement<Button>(panelRoot, "join-button", found => buttons.Join = found);
            yield return WaitForElement<Button>(panelRoot, "credits-button", found => buttons.Credits = found);
            yield return WaitForElement<Button>(panelRoot, "tts-status-button", found => buttons.TtsStatus = found);
            Assert.IsNotNull(buttons.Host, "Title View の host-button が見つかりません。");
            Assert.IsNotNull(buttons.Join, "Title View の join-button が見つかりません。");
            Assert.IsNotNull(buttons.Credits, "Title View の credits-button が見つかりません。");
            Assert.IsNotNull(buttons.TtsStatus, "Title View の tts-status-button が見つかりません。");

            yield return WaitForPanelAttachment(buttons.Join);
        }

        private static ViewRouter FindRouter()
        {
            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            return router;
        }

        /// <summary>
        /// プレイヤーに同梱されるプロジェクト全体の Input Actions（<c>EditorBuildSettings</c> の
        /// <c>com.unity.input.settings.actions</c>。Input System の <c>ProjectWideActionsBuildProvider</c> が
        /// ビルドに含めるもの）を取得する。
        /// <see cref="InputSystem.actions"/> は、単体実行では取得できたが verify.ps1 の全体実行では null だった
        /// （実測。同じプロセスで先に走る <c>InputTestFixture</c> 系のテストが Input System の状態を退避・
        /// 初期化することが影響していると推測するが、原因は未特定）。実行順に依存しないよう設定そのものを読む。
        /// </summary>
        private static InputActionAsset LoadProjectWideActions()
        {
#if UNITY_EDITOR
            UnityEditor.EditorBuildSettings.TryGetConfigObject(ProjectWideActionsConfigKey, out InputActionAsset asset);
            Assert.IsNotNull(asset, $"EditorBuildSettings の {ProjectWideActionsConfigKey} にプロジェクト全体の Input Actions が設定されていません。");
            return asset;
#else
            Assert.Ignore("プロジェクト全体の Input Actions の設定は Editor でのみ確認できます。");
            return null;
#endif
        }

        private static InputAction FindProjectWideAction(string actionPath)
        {
            var action = LoadProjectWideActions().FindAction(actionPath);
            Assert.IsNotNull(action, $"プロジェクト全体の Input Actions に {actionPath} がありません。");
            return action;
        }

        /// <summary><paramref name="action"/> のいずれかのバインド（合成バインドの親を除く）が <paramref name="control"/> に当たるか。</summary>
        private static bool IsBoundTo(InputAction action, InputControl control)
        {
            return action.bindings.Any(binding =>
                !binding.isComposite
                && !string.IsNullOrEmpty(binding.effectivePath)
                && InputControlPath.Matches(binding.effectivePath, control));
        }

        private static Focusable FocusedElement(VisualElement anyElementInPanel)
        {
            return anyElementInPanel.panel?.focusController?.focusedElement;
        }

        private static void SendNavigationMove(VisualElement target, NavigationMoveEvent.Direction direction)
        {
            using var evt = NavigationMoveEvent.GetPooled(direction);
            evt.target = target;
            target.SendEvent(evt);
        }

        private static IEnumerator WaitForFocus(VisualElement expected, string failureMessage)
        {
            yield return WaitUntil(
                () => ReferenceEquals(FocusedElement(expected), expected),
                NavigationTimeoutSeconds,
                () => $"{failureMessage}（実際のフォーカス: {Describe(FocusedElement(expected))}）");
        }

        private static string Describe(Focusable focusable)
        {
            return focusable is VisualElement element
                ? $"{element.GetType().Name} '{element.name}'"
                : focusable == null ? "なし" : focusable.GetType().Name;
        }
    }
}
