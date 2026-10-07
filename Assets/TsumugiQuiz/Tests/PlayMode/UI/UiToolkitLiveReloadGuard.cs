using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// PlayMode テストの実行中だけ、UI Toolkit の「ライブリロード」（Editor 専用機能）を止める（issue #108）。
    ///
    /// <para>
    /// <b>何を止めるのか。</b> Editor で Play 中、<c>VisualTreeAssetChangeTrackerUpdater</c>
    /// （UnityEditor.UIElements）は一定間隔（実装定数 <c>kMinUpdateDelayMs</c>。本プロジェクトの実測でも
    /// 約 1 秒間隔だった。<c>UiToolkitLiveReloadGuardTests</c> のコメント参照）で、パネルが追跡している
    /// <see cref="VisualTreeAsset"/>（＝ <c>ViewRouter</c> が <c>Instantiate()</c> した UXML）の
    /// <c>EditorUtility.GetDirtyCount</c> を監視している。値が変わると
    /// <c>PanelComponentVisualTreeAssetTracker.OnVisualTreeAssetChanged()</c> →
    /// <c>UIDocument.HandleLiveReload()</c> → <c>UIDocument.RecreateUI()</c> が走り、
    /// <see cref="UIDocument.rootVisualElement"/> が別のインスタンスに作り直される。このとき、
    /// </para>
    /// <list type="bullet">
    ///   <item>それまでのルート（とその配下の全要素）は Panel から切り離され、<c>element.panel</c> が
    ///     永久に null になる（ツリー自体はメモリに残るので <c>Q()</c> では見つかる）</item>
    ///   <item>新しいルートは空で、<c>ViewRouter</c> は <c>Start()</c> 済みのため再描画しない</item>
    /// </list>
    /// <para>
    /// これは issue #108 の症状（「要素は見つかるのにクリックが届かない」＝
    /// 'back-button' がまだ Panel にアタッチされていません）と一致する。
    /// </para>
    /// <para>
    /// <b>確定していないこと。</b> 上記は「症状を再現できる機構」であり、
    /// 自然発生していたフレークがこの経路によるものだと確定したわけではない。
    /// 実測では、テスト実行中に <c>.uxml</c> が書き戻された形跡はゼロで
    /// （再インポートされていたのは動的フォントアセットと <c>theme.uss</c> のみ）、
    /// 「追跡対象の UXML の dirty カウントが自然に変わる経路」までは特定できていない。
    /// 本ガードは、その未確定の経路ごと封じるための予防措置である
    /// （同時に、シーンが実際にはアンロードされていなかった実バグも
    /// <see cref="MainSceneTestHelpers.UnloadMainSceneRoutine"/> で修正している。
    /// 自然発生時にどちらが効いていたかは未確定）。
    /// </para>
    /// <para>
    /// <b>実装。</b> ライブリロードの ON/OFF は <c>BaseVisualElementPanel.enableAssetReload</c>
    /// （internal 型の public プロパティ）でしか切り替えられず、公開 API が無いためリフレクションで設定する。
    /// プロパティが見つからない場合は警告を 1 度だけ出して何もしない（テストは従来どおり動き、
    /// <c>UiToolkitLiveReloadGuardTests.LoadMainScene_DisablesAssetLiveReload</c> が失敗して気づける）。
    /// </para>
    /// </summary>
    internal static class UiToolkitLiveReloadGuard
    {
        /// <summary><c>BaseVisualElementPanel.enableAssetReload</c>（Unity 6000.6.0f1 で実在を確認）。</summary>
        private const string EnableAssetReloadPropertyName = "enableAssetReload";

        private static bool _missingPropertyWarned;

        /// <summary>
        /// 指定パネルのライブリロードを止める。
        /// <paramref name="panel"/> が null（＝ UIDocument のルートがまだどのパネルにも属していない）場合は
        /// 何もしない。パネルの生成待ちは呼び出し側の責務で、これは「プロパティが見つからない」
        /// （＝ Unity 側の仕様変更）とは別の状況なので警告も出さない。
        /// </summary>
        public static void Disable(IPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            var property = FindProperty(panel);
            if (property == null || !property.CanWrite)
            {
                WarnPropertyUnusableOnce(panel);
                return;
            }

            property.SetValue(panel, false);
        }

        /// <summary>
        /// 指定パネルのライブリロードを（再度）有効にする（issue #137）。
        /// <see cref="Disable"/> の対になるメソッドで、ライブリロードによる
        /// <c>UIDocument.RecreateUI()</c> を意図的に発生させたいテスト
        /// （<c>ViewRouterLiveReloadRedrawTests</c>）専用。<paramref name="panel"/> が null の場合は何もしない。
        /// </summary>
        public static void Enable(IPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            var property = FindProperty(panel);
            if (property == null || !property.CanWrite)
            {
                WarnPropertyUnusableOnce(panel);
                return;
            }

            property.SetValue(panel, true);
        }

        /// <summary>
        /// 指定パネルのライブリロードが有効かどうか。
        /// <paramref name="panel"/> が null、またはプロパティを読めない場合は null を返す。
        /// </summary>
        public static bool? IsEnabled(IPanel panel)
        {
            if (panel == null)
            {
                return null;
            }

            var property = FindProperty(panel);
            if (property == null || !property.CanRead)
            {
                return null;
            }

            return (bool)property.GetValue(panel);
        }

        private static PropertyInfo FindProperty(IPanel panel)
        {
            return panel.GetType().GetProperty(
                EnableAssetReloadPropertyName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private static void WarnPropertyUnusableOnce(IPanel panel)
        {
            if (_missingPropertyWarned)
            {
                return;
            }

            _missingPropertyWarned = true;
            Debug.LogWarning(
                $"[UiToolkitLiveReloadGuard] '{EnableAssetReloadPropertyName}' を " +
                $"{panel.GetType().FullName} に対して書き込めないため、UI Toolkit のライブリロードを" +
                "止められませんでした。Unity のバージョンアップでプロパティ名・アクセサが変わった可能性があります（issue #108）。");
        }
    }
}
