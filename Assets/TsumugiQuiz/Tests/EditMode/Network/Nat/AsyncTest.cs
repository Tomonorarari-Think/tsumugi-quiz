using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TsumugiQuiz.Tests.EditMode.Network.Nat
{
    /// <summary>
    /// EditMode の <c>[UnityTest]</c> から <see cref="Task"/> を待つためのヘルパ。
    ///
    /// <c>Task.Wait()</c> をメインスレッドで呼ぶと、継続が Unity の同期コンテキストへ
    /// ポストされる実装ではデッドロックする。ここでは <c>yield return null</c> で
    /// エディタの更新を回しながら完了を待つ（= 実際の同期コンテキストの挙動をそのまま検証できる）。
    /// </summary>
    internal static class AsyncTest
    {
        /// <summary>完了を待つ既定の上限（秒）。</summary>
        private const double DefaultTimeoutSeconds = 15.0;

        /// <summary>タスクの完了を待つ。失敗したら例外をそのまま投げ直す。</summary>
        /// <param name="task">待つタスク。</param>
        /// <param name="timeoutSeconds">上限（秒）。</param>
        public static IEnumerator Await(Task task, double timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            var startedAt = DateTime.UtcNow;
            while (!task.IsCompleted)
            {
                if ((DateTime.UtcNow - startedAt).TotalSeconds > timeoutSeconds)
                {
                    Assert.Fail($"タスクが {timeoutSeconds} 秒以内に完了しませんでした。");
                }

                yield return null;
            }

            // IsFaulted の場合は AggregateException ではなく元の例外を投げる。
            task.GetAwaiter().GetResult();
        }

        /// <summary>タスクの完了を待ち、結果を <paramref name="onCompleted"/> に渡す。</summary>
        /// <typeparam name="T">結果の型。</typeparam>
        /// <param name="task">待つタスク。</param>
        /// <param name="onCompleted">結果の受け取り先。</param>
        /// <param name="timeoutSeconds">上限（秒）。</param>
        public static IEnumerator Await<T>(Task<T> task, Action<T> onCompleted, double timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (onCompleted == null)
            {
                throw new ArgumentNullException(nameof(onCompleted));
            }

            yield return Await(task, timeoutSeconds);
            onCompleted(task.Result);
        }
    }
}
