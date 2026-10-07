using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Settings
{
    /// <summary>
    /// <see cref="SettingsView"/> の各タブが UXML から要素を取得する際の共通ヘルパ（PR #92 レビュー M5）。
    /// フィールドごとに個別の null チェックを書く代わりに、見つからなかった要素をログしつつ
    /// <paramref name="allFound"/> に集約する。
    /// </summary>
    internal static class SettingsFieldBinder
    {
        /// <summary>
        /// <paramref name="root"/> から名前 <paramref name="name"/> の要素を取得する。見つからなければ
        /// ログを残し、<paramref name="allFound"/> を false にする（呼び出し側は最後にまとめて判定する）。
        /// </summary>
        public static T Require<T>(VisualElement root, string name, ref bool allFound) where T : VisualElement
        {
            var element = root.Q<T>(name);
            if (element == null)
            {
                Debug.LogError($"[SettingsView] '{name}' が見つかりません。settings-view.uxml を確認してください。");
                allFound = false;
            }

            return element;
        }
    }
}
