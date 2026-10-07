using System;
using System.Collections.Generic;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// View 名ごとの <see cref="IView"/> コントローラ生成（ファクトリ）を保持するレジストリ。
    /// どの View にどのコントローラを割り当てるかという「登録」の関心を <see cref="ViewRouter"/> 本体から分離する。
    /// ファクトリを介するため、View を表示するたびに新しいコントローラインスタンスが生成される
    /// （複数 View で共有されるコントローラでも状態を持ち越さない）。
    /// </summary>
    public sealed class ViewControllerRegistry
    {
        private readonly Dictionary<string, Func<IView>> _factories = new();

        /// <summary>
        /// 指定した View 名に対するコントローラ生成関数を登録する。同じ View 名に対して呼び出すと上書きされる。
        /// </summary>
        public void Register(string viewName, Func<IView> factory)
        {
            if (string.IsNullOrEmpty(viewName))
            {
                throw new ArgumentException("viewName が null または空です。", nameof(viewName));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            _factories[viewName] = factory;
        }

        /// <summary>
        /// 指定した View 名のコントローラを新規生成する。未登録の場合は null を返す
        /// （その View は UXML の静的表示のみで、コントローラを持たない扱いになる）。
        /// </summary>
        public IView CreateController(string viewName)
        {
            return _factories.TryGetValue(viewName, out var factory) ? factory() : null;
        }

        /// <summary>指定した View 名にコントローラが登録されているか。</summary>
        public bool HasController(string viewName) => _factories.ContainsKey(viewName);
    }
}
