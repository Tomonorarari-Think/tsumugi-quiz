using System.Text;
using TsumugiQuiz.UI;
using UnityEngine.UIElements;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// <see cref="MainSceneTestHelpers"/> の要素待ち・クリック送出ヘルパーが失敗したときに使う診断系ヘルパー
    /// （issue #150、issue #108 / #137 で <c>MainSceneTestHelpers.cs</c> に増えたものを切り出した）。
    /// 挙動は移設前と変わらない（純粋な移動）。
    /// </summary>
    internal static class MainSceneDiagnostics
    {
        /// <summary>
        /// Panel アタッチ待ちが失敗したとき、「掴んでいるツリーが古いのかどうか」を切り分けるための情報
        /// （issue #108）。UI Toolkit のライブリロードや画面遷移で <see cref="UIDocument.rootVisualElement"/> が
        /// 作り直されると、テストが保持している要素は「見つかるが Panel に属さない」状態のまま固定される。
        /// その場合ここで「要素のルート != 現在の UIDocument のルート」と報告される。
        /// </summary>
        internal static string DescribeDetachedRoot(VisualElement element)
        {
            var elementRoot = element;
            while (elementRoot.hierarchy.parent != null)
            {
                elementRoot = elementRoot.hierarchy.parent;
            }

            var document = MainSceneTestHelpers.FindViewRouterUIDocument();
            var currentRoot = document != null ? document.rootVisualElement : null;
            if (currentRoot == null)
            {
                return "[診断] ViewRouter の UIDocument が見つからないため、現在のルートと比較できません。";
            }

            if (ReferenceEquals(elementRoot, currentRoot))
            {
                return "[診断] 要素のルートは現在の UIDocument.rootVisualElement と同一です"
                       + $"（ルート自体が Panel 未アタッチ: {currentRoot.panel == null}）。";
            }

            // issue #137 レビュー L-4: ViewRouter は Editor 実行中に限り、作り直された新ルートへ
            // 現在の View を自動で再表示する（#137）。そのため「現在のルートの子要素数」が 0 より大きくても、
            // それは ViewRouter が新しい要素で再描画した結果であり、取得済みの要素（古いツリー側）が
            // 再アタッチされたわけではない点に注意（古いツリー自体はもう捨てられている）。
            return "[診断] 要素のルートが現在の UIDocument.rootVisualElement と異なります。"
                   + "UIDocument がツリーを作り直した（ライブリロード等、issue #108）可能性が高く、"
                   + "取得済みの要素（古いツリー側）は二度とアタッチされません。"
                   + $"（現在のルート＝新しいツリーの子要素数: {currentRoot.childCount}。"
                   + "issue #137 により、Editor 実行中は ViewRouter がここへ現在の View を自動再描画しているはずです）";
        }

        /// <summary>
        /// 要素の診断用の説明文字列を作る。<see cref="VisualElement.name"/> が設定されていればそれを使い、
        /// 未設定（空文字列）の場合は型名・クラス一覧・<see cref="VisualElement.userData"/> を代わりに使う
        /// （issue #102。一覧行など <c>name</c> を設定していない要素で「'' がまだ Panel にアタッチされて
        /// いません」とだけ表示され、どの要素かを特定できなかったための対応）。
        /// </summary>
        internal static string DescribeElementForDiagnostics(VisualElement element)
        {
            if (!string.IsNullOrEmpty(element.name))
            {
                return $"'{element.name}'";
            }

            var classList = string.Join(".", element.GetClasses());
            var classSuffix = string.IsNullOrEmpty(classList) ? string.Empty : $" class=\"{classList}\"";
            var userDataSuffix = element.userData != null ? $" userData=\"{element.userData}\"" : string.Empty;
            return $"<{element.GetType().Name}{classSuffix}{userDataSuffix}>（name 未設定）";
        }

        /// <summary>
        /// <see cref="MainSceneTestHelpers.WaitForElement{T}"/> /
        /// <see cref="MainSceneTestHelpers.WaitUntil(System.Func{bool}, string, float, VisualElement)"/>
        /// が失敗したときの原因調査用の診断情報（issue #73 レビュー H3）。
        /// <paramref name="root"/> 自身が Panel にアタッチされているか、Main シーンの
        /// <see cref="ViewRouter"/> が見つかるか、現在の表示 View 名、<paramref name="root"/> が
        /// その ViewRouter の UIDocument.rootVisualElement と同一かどうかを出力したうえで、
        /// 画面階層ダンプ（<see cref="DumpHierarchy"/>）を続ける。
        /// </summary>
        internal static string BuildFailureDiagnostics(VisualElement root)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[診断情報]");
            sb.AppendLine($"root.panel == null: {root == null || root.panel == null}");

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            sb.AppendLine($"ViewRouter が見つかったか: {router != null}");
            if (router != null)
            {
                sb.AppendLine($"ViewRouter.CurrentViewName: {router.CurrentViewName ?? "(null)"}");
                var routerDocument = router.GetComponent<UIDocument>();
                var sameRoot = root != null && routerDocument != null
                    && ReferenceEquals(root, routerDocument.rootVisualElement);
                sb.AppendLine($"root == ViewRouter の UIDocument.rootVisualElement: {sameRoot}");
            }

            sb.Append(DumpHierarchy(root));
            return sb.ToString();
        }

        /// <summary>
        /// <paramref name="root"/> 配下の画面階層を、要素種別・name・クラス一覧・resolvedStyle.display・
        /// （<see cref="TextElement"/> ならテキストの先頭 40 文字）付きでダンプする（issue #73）。
        /// 要素数が多い画面（Credits のライセンス一覧等）でログが肥大化しすぎないよう、出力ノード数の
        /// 上限を設ける。
        /// </summary>
        private static string DumpHierarchy(VisualElement root)
        {
            if (root == null)
            {
                return "(root が null のため画面階層をダンプできません)";
            }

            const int maxNodes = 200;
            var sb = new StringBuilder();
            sb.AppendLine("[画面階層ダンプ]");
            var nodeCount = 0;
            var truncated = false;
            AppendElement(sb, root, 0, maxNodes, ref nodeCount, ref truncated);
            if (truncated)
            {
                sb.AppendLine($"...(上限 {maxNodes} ノードに達したため以降省略)");
            }

            return sb.ToString();
        }

        private static void AppendElement(
            StringBuilder sb, VisualElement element, int depth, int maxNodes, ref int nodeCount, ref bool truncated)
        {
            if (nodeCount >= maxNodes)
            {
                truncated = true;
                return;
            }

            nodeCount++;

            var indent = new string(' ', depth * 2);
            var classList = string.Join(".", element.GetClasses());
            var classSuffix = string.IsNullOrEmpty(classList) ? string.Empty : $" .{classList}";
            var textSuffix = string.Empty;
            if (element is TextElement textElement && !string.IsNullOrEmpty(textElement.text))
            {
                var text = textElement.text;
                var preview = text.Length > 40 ? text.Substring(0, 40) + "…" : text;
                textSuffix = $" text=\"{preview}\"";
            }

            sb.AppendLine(
                $"{indent}<{element.GetType().Name} name=\"{element.name}\"{classSuffix} display={element.resolvedStyle.display}{textSuffix}>");

            foreach (var child in element.Children())
            {
                AppendElement(sb, child, depth + 1, maxNodes, ref nodeCount, ref truncated);
            }
        }
    }
}
