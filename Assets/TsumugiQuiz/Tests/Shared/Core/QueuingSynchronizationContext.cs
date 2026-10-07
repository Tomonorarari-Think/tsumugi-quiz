using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace TsumugiQuiz.Tests.Shared.Core
{
    /// <summary>
    /// Unity のメインスレッド用 <see cref="SynchronizationContext"/> の代わりに使う、
    /// テスト用のキュー式コンテキスト（#97）。<see cref="Post"/> された継続をその場では実行せず貯めておき、
    /// 呼び出し元が任意のタイミング（例: スピンウェイトのポーリングループ）で <see cref="Drain"/> を呼んで
    /// 吐き出す。
    ///
    /// <b>EditMode 専用の用途を想定している</b>（#97 L-3）。EditMode では Unity の
    /// <see cref="SynchronizationContext"/> がテストの実行中にポンプされないため、
    /// <c>SynchronizationContext.SetSynchronizationContext(null)</c> に差し替えるだけでは、
    /// メインスレッド専用の処理（UI Toolkit の要素操作、<c>Resources.Load</c> 等）を含む継続が
    /// スレッドプール上で実行されてしまう危険がある（<c>TtsStatusPanel</c> の「再試行」ハンドラで
    /// 実際に発生した。#97 参照）。本番同様に「メインスレッドへ Post → メインスレッドのポンプが処理する」
    /// 形を保つため、null の代わりにこのコンテキストを使い、スピンウェイト自身をポンプにする
    /// （待機ループがブロックする前に必ず自分で drain するのでデッドロックしない）。
    /// PlayMode では Unity 自身の <see cref="SynchronizationContext"/> が毎フレームポンプされるため、
    /// このクラスを使う必要はない。
    /// </summary>
    internal sealed class QueuingSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object State)> _queue = new();

        public override void Post(SendOrPostCallback d, object state) => _queue.Enqueue((d, state));

        /// <summary>
        /// このテストコンテキストは <see cref="Send"/>（同期呼び出し）を扱わない（#97 M-2）。
        /// 本番の Unity メインスレッドの同期コンテキストも通常は <see cref="Post"/> でしか使わないため、
        /// <see cref="Send"/> を呼ぶコードが混入していたら握りつぶさずここで気付けるようにする。
        /// </summary>
        public override void Send(SendOrPostCallback d, object state)
            => throw new NotSupportedException("このテストコンテキストは Send を扱いません（#97）。Post を使ってください。");

        /// <summary>
        /// 貯めてある継続をすべて（呼び出し元のスレッドで）実行する。
        /// 個々のコールバックが例外を投げても他のコールバックの実行は止めず（#97 L-2）、
        /// すべて吐き出した後にまとめて再送出する（1件なら <see cref="Exception"/> を投げた
        /// コールバック自身の例外、複数件なら <see cref="AggregateException"/>）。
        /// </summary>
        public void Drain()
        {
            List<Exception> exceptions = null;

            while (_queue.TryDequeue(out var item))
            {
                try
                {
                    item.Callback(item.State);
                }
                catch (Exception e)
                {
                    (exceptions ??= new List<Exception>()).Add(e);
                }
            }

            if (exceptions == null) return;
            if (exceptions.Count == 1) throw exceptions[0];
            throw new AggregateException(exceptions);
        }
    }
}
