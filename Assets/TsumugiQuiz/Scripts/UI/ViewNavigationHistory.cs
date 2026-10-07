using System;
using System.Collections.Generic;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// View 名（string）の表示履歴（戻るスタック）を管理する、Unity API に依存しない純 C# ロジック。
    /// <see cref="ViewRouter"/> から分離しており、EditMode でユニットテスト可能。
    /// </summary>
    public sealed class ViewNavigationHistory
    {
        private readonly List<string> _stack = new();

        /// <summary>現在表示中の View 名。履歴が空の場合は null。</summary>
        public string Current => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        /// <summary>戻り先の履歴が 1 件以上あるか（現在の View 以外に履歴が残っているか）。</summary>
        public bool CanGoBack => _stack.Count > 1;

        /// <summary>現在の履歴の件数。</summary>
        public int Count => _stack.Count;

        /// <summary>
        /// 新しい View を履歴に積む（現在の View として扱う）。
        /// </summary>
        /// <exception cref="ArgumentException">viewName が null または空文字の場合。</exception>
        public void Push(string viewName)
        {
            if (string.IsNullOrEmpty(viewName))
            {
                throw new ArgumentException("viewName が null または空です。", nameof(viewName));
            }

            _stack.Add(viewName);
        }

        /// <summary>
        /// 直前の View に戻る。現在の View を履歴から取り除き、戻り先の View 名を返す。
        /// </summary>
        /// <exception cref="InvalidOperationException">戻り先の履歴がない場合。</exception>
        public string GoBack()
        {
            if (!CanGoBack)
            {
                throw new InvalidOperationException("戻り先の履歴がありません。");
            }

            _stack.RemoveAt(_stack.Count - 1);
            return Current;
        }

        /// <summary>履歴をすべて消去する。</summary>
        public void Reset()
        {
            _stack.Clear();
        }
    }
}
