using System;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// ViewRouter の Inspector に表示される、1 View 分の「View名 ⇔ UXML テンプレート」の対応付け。
    /// コントローラ（<see cref="IView"/>）の割り当ては <see cref="ViewControllerRegistry"/> 側の責務であり、
    /// ここではテンプレートの参照のみを保持する（シリアライズ可能な値のみで構成する）。
    /// </summary>
    [Serializable]
    public sealed class ViewDefinition
    {
        public string ViewName;
        public VisualTreeAsset Template;
    }
}
