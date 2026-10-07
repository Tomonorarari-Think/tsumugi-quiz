namespace TsumugiQuiz.UI
{
    /// <summary>
    /// ViewRouter が切り替える各画面（View）のライフサイクル。
    /// View が表示されるときに <see cref="OnShow"/>、隠れるときに <see cref="OnHide"/> が呼ばれる。
    /// </summary>
    public interface IView
    {
        /// <summary>この View が表示され、UXML のインスタンスがルートに追加された直後に呼ばれる。</summary>
        void OnShow(ViewContext context);

        /// <summary>この View が別の View に切り替わる直前に呼ばれる。</summary>
        void OnHide();
    }
}
