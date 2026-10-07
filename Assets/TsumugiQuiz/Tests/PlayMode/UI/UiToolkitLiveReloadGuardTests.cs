using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// issue #108 の回帰テスト。Editor の UI Toolkit ライブリロードが走っても、テストが掴んでいる
    /// 要素が Panel から切り離されない（＝クリックが無視されない）ことを確認する。
    ///
    /// <para>
    /// <see cref="ForcedAssetDirty_DoesNotDetachHeldElements"/> は #108 のフレークと同じ状態
    /// （要素は <c>Q()</c> で見つかるのに <c>element.panel</c> が null）を、追跡対象 UXML の
    /// dirty カウントを動かして意図的に作り出す。対策（<see cref="UiToolkitLiveReloadGuard"/>）を外すと
    /// 必ず失敗することを実測済み（負の対照。対策前は <c>button.panel == null</c> / ルート差し替え）。
    /// issue #137 レビュー L-4: 当時（#137 の修正前）は新ルートが空のまま（子要素数 0）だったが、
    /// #137 で <c>ViewRouter</c> がライブリロードによるルート差し替えを検知して自動で再表示するように
    /// なったため、現在この負の対照を再現すると新ルートは <c>ViewRouter</c> によって再描画され、
    /// 子要素数は 0 ではなくなる（＝ <c>heldButton</c> が握っている「差し替え前」の要素だけが
    /// Panel から切り離されたままになる）。
    /// </para>
    /// <para>
    /// なお、このテストが再現するのは「症状を起こしうる機構」であって、自然発生していたフレークが
    /// この経路だったと確定したわけではない（<see cref="UiToolkitLiveReloadGuard"/> の説明を参照）。
    /// </para>
    /// </summary>
    public sealed class UiToolkitLiveReloadGuardTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();

            // Boot 経由のテスト（BootPath_...）で常駐したシングルトンを片付ける。
            TearDownMainSceneAndBootstrapSingletons();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator LoadMainScene_DisablesAssetLiveReload()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            AssertLiveReloadDisabled(panelRoot, "LoadMainSceneAndGetRoot");
        }

        /// <summary>
        /// <c>LobbyViewSceneTests</c> 等が持つ独自のロードヘルパー（<c>LoadMainThroughBootAndInstallFakes</c>、
        /// <see cref="MainSceneTestHelpers"/> を通さない Boot 経由の経路）でもライブリロードが
        /// 止まっていることを確認する（issue #108 レビュー HIGH-1）。
        /// 独自ヘルパーもルート取得には <see cref="MainSceneTestHelpers.FindViewRouterUIDocument"/> を
        /// 使うので、そこで止まるのが仕様。
        /// </summary>
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator BootPath_WithCustomLoadHelper_DisablesAssetLiveReload()
        {
            VisualElement panelRoot = null;
            yield return LobbyViewSceneTests.LoadMainThroughBootAndInstallFakes(root => panelRoot = root);

            AssertLiveReloadDisabled(panelRoot, "LobbyViewSceneTests.LoadMainThroughBootAndInstallFakes");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ForcedAssetDirty_DoesNotDetachHeldElements()
        {
#if UNITY_EDITOR
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            yield return WaitUntil(
                () => panelRoot.Q<Button>() != null,
                "Main シーンの View に Button が 1 つも現れませんでした。");
            var heldButton = panelRoot.Q<Button>();
            Assert.IsNotNull(heldButton.panel, "前提: 取得直後の要素は Panel にアタッチされている。");

            var trackedTemplate = FindCurrentViewTemplateAsset(panelRoot);
            Assert.IsNotNull(
                trackedTemplate,
                "表示中の View の元 UXML（VisualTreeAsset）を特定できませんでした。ViewRouter の生成方法が変わった可能性があります。");

            UnityEditor.EditorUtility.SetDirty(trackedTemplate);
            try
            {
                var deadline = Time.realtimeSinceStartupAsDouble + LiveReloadObservationSeconds;
                while (Time.realtimeSinceStartupAsDouble < deadline)
                {
                    yield return null;
                }

                var document = FindViewRouterUIDocument();
                Assert.IsNotNull(document, "ViewRouter の UIDocument が消えました。");
                Assert.IsTrue(
                    ReferenceEquals(panelRoot, document.rootVisualElement),
                    "UIDocument.rootVisualElement が作り直されました（ライブリロードが止まっていない、issue #108）。");
                Assert.IsNotNull(
                    heldButton.panel,
                    "取得済みの要素が Panel から切り離されました（ライブリロードが止まっていない、issue #108）。");
            }
            finally
            {
                // dirty のまま放置すると、この後のアセット保存で UXML が実際に書き戻されてしまう。
                UnityEditor.EditorUtility.ClearDirty(trackedTemplate);
            }
#else
            Assert.Ignore("Editor 上でのみ意味のあるテスト（ライブリロードは Editor 専用機能）。");
            yield break;
#endif
        }

        private static void AssertLiveReloadDisabled(VisualElement panelRoot, string loaderName)
        {
            Assert.IsNotNull(panelRoot?.panel, $"{loaderName} が返したルートが Panel にアタッチされていません。");

            var enabled = UiToolkitLiveReloadGuard.IsEnabled(panelRoot.panel);
            Assert.IsNotNull(
                enabled,
                "パネルのライブリロード状態を取得できませんでした。Unity 側のプロパティ名が変わった可能性があります（issue #108）。");
            Assert.IsFalse(
                enabled.Value,
                $"PlayMode テスト中は UI Toolkit のライブリロードを止めておくこと（{loaderName} 経由、issue #108）。");
        }
    }
}
