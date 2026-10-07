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
    /// issue #137 の回帰テスト。Editor の UI Toolkit ライブリロードが
    /// <see cref="UIDocument.rootVisualElement"/> を作り直しても、<see cref="ViewRouter"/> が
    /// 現在の View を再描画し、画面が空にならないことを確認する。
    ///
    /// <para>
    /// <see cref="UiToolkitLiveReloadGuardTests"/>（issue #108）はライブリロードを止めることを
    /// 検証する回帰テストだが、本テストは逆に「ライブリロードが実際に走っても壊れない」ことを
    /// 確認したいので、<see cref="MainSceneTestHelpers.LoadMainSceneAndGetRoot"/> が末尾で適用する
    /// ガード（<see cref="UiToolkitLiveReloadGuard.Disable"/>）を、このテストの中だけ明示的に
    /// 解除してから <see cref="UnityEditor.EditorUtility.SetDirty"/> で再生成を起こす。
    /// 他のテストのガードには一切触れない。
    /// </para>
    /// <para>
    /// レビュー H-1: ライブリロードの ON/OFF は <b>パネル単位</b>の設定で、シーンをアンロードしても
    /// 自動では元に戻らない（<see cref="UiToolkitLiveReloadGuard"/> は <c>panel.enableAssetReload</c>
    /// を直接書き換えるだけで、パネル自体の破棄以外に自動リセットの仕組みは無い）。本テストは唯一
    /// 明示的にライブリロードを有効化するテストのため、再生成前の状態（<c>wasEnabled</c>）を控え、
    /// <c>finally</c> で必ず元の状態に戻す。加えて、テストが異常終了して <c>finally</c> を通らなかった
    /// 場合の保険として、対象パネルをフィールドに保持し <see cref="TearDown"/> でも戻す
    /// （通常経路では <c>finally</c> で既に戻っているため二重実行になるだけで副作用は無い）。
    /// </para>
    /// </summary>
    public sealed class ViewRouterLiveReloadRedrawTests
    {
        /// <summary>
        /// レビュー H-1: <c>finally</c> でライブリロードを戻し損ねた場合に備えて
        /// <see cref="TearDown"/> からも戻すための対象パネル。通常経路では <c>finally</c> の時点で
        /// null に戻しているため、ここで <c>null</c> でなければ異常終了したということ。
        /// </summary>
        private IPanel _panelToRestoreOnTearDown;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // レビュー H-1: 保険。通常は finally で既に戻して null にしている。
            if (_panelToRestoreOnTearDown != null)
            {
                UiToolkitLiveReloadGuard.Disable(_panelToRestoreOnTearDown);
                _panelToRestoreOnTearDown = null;
            }

            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator LiveReloadRecreatesRoot_ViewRouterRedrawsCurrentView()
        {
#if UNITY_EDITOR
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            var router = Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "ViewRouter が Main シーンに見つかりません。");
            var viewNameBeforeReload = router.CurrentViewName;
            Assert.IsFalse(string.IsNullOrEmpty(viewNameBeforeReload), "ライブリロード前に View が表示されていません。");

            var routerDocument = router.GetComponent<UIDocument>();
            Assert.IsNotNull(routerDocument, "ViewRouter の UIDocument が取得できません。");

            yield return WaitUntil(
                () => panelRoot.Q<Button>() != null,
                "Main シーンの View に Button が 1 つも現れませんでした。");
            var trackedTemplate = FindCurrentViewTemplateAsset(panelRoot);
            Assert.IsNotNull(
                trackedTemplate,
                "表示中の View の元 UXML（VisualTreeAsset）を特定できませんでした。ViewRouter の生成方法が変わった可能性があります。");

            // レビュー H-1: 再生成前の状態を控えてから有効化する。LoadMainSceneAndGetRoot が直前に
            // 止めているので wasEnabled は通常 false のはずだが、決め打ちにせず実測して戻す。
            var panel = panelRoot.panel;
            var wasEnabled = UiToolkitLiveReloadGuard.IsEnabled(panel);
            _panelToRestoreOnTearDown = panel;

            // LoadMainSceneAndGetRoot が末尾で止めたライブリロードを、このテストだけ明示的に再度有効化し、
            // 実際の UIDocument.RecreateUI() を起こす（issue #137）。#108 の対策自体は変更しない。
            UiToolkitLiveReloadGuard.Enable(panel);

            UnityEditor.EditorUtility.SetDirty(trackedTemplate);
            try
            {
                // レビュー L-3: 固定時間待たず、ルート参照が変わり次第すぐに先へ進む
                // （LiveReloadObservationSeconds は「これだけ待っても変わらなければ前提が崩れている」上限）。
                yield return WaitUntil(
                    () => !ReferenceEquals(routerDocument.rootVisualElement, panelRoot),
                    "前提: ライブリロードで UIDocument.rootVisualElement が作り直されませんでした"
                    + "（テストの前提が崩れています。issue #108 の対策範囲が変わった可能性があります）。",
                    timeoutSeconds: LiveReloadObservationSeconds);

                var newRoot = routerDocument.rootVisualElement;

                yield return WaitUntil(
                    () => newRoot.Q<Button>()?.panel != null,
                    "issue #137: ライブリロード後、ViewRouter が現在の View を再描画しませんでした"
                    + "（新しいルートに Panel アタッチ済みの Button が現れません）。",
                    timeoutSeconds: 2f);

                Assert.Greater(
                    newRoot.childCount, 0,
                    "issue #137: ライブリロード後、ViewRouter が現在の View を再描画しませんでした（新しいルートが空のままです）。");

                Assert.AreEqual(
                    viewNameBeforeReload, router.CurrentViewName,
                    "issue #137: 再生成後に表示している View 名が変わってしまいました。");
            }
            finally
            {
                // レビュー H-1: 先にライブリロードを元の状態へ戻してから（wasEnabled が false のときだけ
                // Disable）、dirty を解除する（放置すると次のアセット保存で UXML が実際に書き戻されてしまう）。
                if (wasEnabled == false)
                {
                    UiToolkitLiveReloadGuard.Disable(panel);
                }
                _panelToRestoreOnTearDown = null;

                UnityEditor.EditorUtility.ClearDirty(trackedTemplate);
            }
#else
            Assert.Ignore("Editor 上でのみ意味のあるテスト（ライブリロードは Editor 専用機能）。");
            yield break;
#endif
        }
    }
}
