using UnityEngine.UIElements;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// <see cref="IView.OnShow"/> に渡される、表示された View に関する情報。
    /// </summary>
    public readonly struct ViewContext
    {
        /// <summary>ViewRouter に登録されている View 名（ケバブケース）。</summary>
        public string ViewName { get; }

        /// <summary>この View の UXML から生成されたルート要素。</summary>
        public VisualElement Root { get; }

        /// <summary>画面遷移を行うための ViewRouter への参照。</summary>
        public ViewRouter Router { get; }

        public ViewContext(string viewName, VisualElement root, ViewRouter router)
        {
            ViewName = viewName;
            Root = root;
            Router = router;
        }
    }
}
