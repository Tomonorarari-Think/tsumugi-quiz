using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views
{
    /// <summary>
    /// まだ実体が用意されていない View 共通のプレースホルダ表示。「戻る」ボタンで直前の View に戻る。
    /// <see cref="ViewNames.PlaceholderViews"/> に挙がっている View 名にだけ登録される。
    /// 現時点では全 View が実体を持つため、この一覧は空（= 本クラスは今後 View を追加するときの足場）。
    /// 利用規約の確認・撤回（requirements.md FR-75）は Credits / Settings から行えるため、
    /// 本クラスは「戻る」だけを持つ（PR #92 レビュー L3 で利用規約ボタンを削除）。
    /// </summary>
    public sealed class PlaceholderView : IView
    {
        private Button _backButton;
        private Action _backHandler;

        public void OnShow(ViewContext context)
        {
            var root = context.Root;

            var heading = root.Q<Label>("placeholder-heading");
            if (heading != null)
            {
                heading.text = $"{context.ViewName}（未実装）";
            }

            var router = context.Router;

            _backButton = root.Q<Button>("back-button");
            if (_backButton == null)
            {
                Debug.LogError("[PlaceholderView] back-button が見つかりません。placeholder-view.uxml を確認してください。");
                return;
            }

            _backHandler = () => router.GoBack();
            _backButton.SetEnabled(router.CanGoBack);
            _backButton.clicked += _backHandler;
        }

        public void OnHide()
        {
            if (_backButton != null && _backHandler != null)
            {
                _backButton.clicked -= _backHandler;
            }

            _backButton = null;
            _backHandler = null;
        }
    }
}
