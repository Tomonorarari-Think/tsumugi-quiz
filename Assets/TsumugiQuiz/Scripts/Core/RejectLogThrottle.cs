using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 同じキーから連続して発生するログを一定間隔に間引く、Unity API 非依存の小さなユーティリティ
    /// （docs/network.md §9、#52）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>TsumugiQuiz.Network.GameSession</c> / <c>TsumugiQuiz.Network.QuestionDistributor</c> /
    /// <c>TsumugiQuiz.Network.TtsSyncCoordinator</c> は元々この方針を各ファイル内で個別に実装している
    /// （#12 / #13 / #23）。本クラスは、それらとは別に生成する <c>RpcRateGuard</c> の警告ログや
    /// <c>ConnectionApprovalHandler</c> の拒否ログ間引きのために新設した（既存ファイルの実装を差し替えると
    /// 衝突リスクが上がるため触らない。3 系統の間引き実装を 1 つに統合する件は別 issue で扱う、#52 レビュー M6）。
    /// </para>
    /// <para>
    /// キーは「クライアント ID」とは限らない（<c>ConnectionApprovalHandler</c> は NGO が接続試行ごとに
    /// 新しい ID を払い出すため、クライアント ID 単位の間引きが機能しない。代わりに拒否理由
    /// （<c>ConnectionRejectionReason</c>）をキーにしてグローバルに間引く）。そのため <see cref="Evaluate"/> は
    /// 汎用的に <c>ulong</c> のキーを受け取る。
    /// </para>
    /// <para>
    /// キーの数が呼び出し側の想定を超えて増え続けても辞書が無制限に伸びないよう、
    /// 既定で <see cref="DefaultCapacity"/> 件の上限を持ち、超過時は最も長く使われていないキーから削除する
    /// （LRU）。
    /// </para>
    /// </remarks>
    public sealed class RejectLogThrottle
    {
        /// <summary>既定の保持上限（キーの種類数）。</summary>
        public const int DefaultCapacity = 256;

        private readonly double _intervalSeconds;
        private readonly int _capacity;
        private readonly Dictionary<ulong, LinkedListNode<Entry>> _lookup = new Dictionary<ulong, LinkedListNode<Entry>>();

        /// <summary>先頭が最も最近使われたキー、末尾が最も長く使われていないキー（LRU）。</summary>
        private readonly LinkedList<Entry> _lruOrder = new LinkedList<Entry>();

        /// <summary>
        /// 間引き器を作る。
        /// </summary>
        /// <param name="intervalSeconds">同じキーでログを出してよい最短間隔（秒）。</param>
        /// <param name="capacity">保持するキーの種類数の上限。超過分は最も長く使われていないものから削除する。</param>
        /// <exception cref="ArgumentOutOfRangeException">間隔が負、または容量が 0 以下のとき。</exception>
        public RejectLogThrottle(double intervalSeconds, int capacity = DefaultCapacity)
        {
            if (intervalSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "間隔は 0 以上である必要があります。");
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "容量は 1 以上である必要があります。");
            }

            _intervalSeconds = intervalSeconds;
            _capacity = capacity;
        }

        /// <summary>
        /// このキーで今ログを出してよいか判定する。<see cref="RejectLogDecision.ShouldLog"/> が true の場合、
        /// 呼び出し側はそのタイミングで実際にログを出す想定（内部の最終ログ時刻もこの呼び出しで更新する）。
        /// 間引かれた場合は <see cref="RejectLogDecision.SuppressedSinceLastLog"/> に「前回ログ出力から
        /// 間引かれた件数」が入るので、次にログを出すときにまとめてサマリとして書ける
        /// （例:「ほか N 件を間引きました」）。
        /// </summary>
        /// <param name="key">キー（クライアント ID、拒否理由など呼び出し側が決める）。</param>
        /// <param name="nowSeconds">現在時刻（秒）。</param>
        /// <returns>間引きの判定結果。</returns>
        public RejectLogDecision Evaluate(ulong key, double nowSeconds)
        {
            if (_lookup.TryGetValue(key, out var node))
            {
                var entry = node.Value;
                _lruOrder.Remove(node);

                if (nowSeconds >= entry.LastLoggedSeconds && nowSeconds - entry.LastLoggedSeconds < _intervalSeconds)
                {
                    Touch(new Entry(key, entry.LastLoggedSeconds, entry.SuppressedSinceLastLog + 1));
                    return new RejectLogDecision(false, 0);
                }

                var suppressed = entry.SuppressedSinceLastLog;
                Touch(new Entry(key, nowSeconds, 0));
                return new RejectLogDecision(true, suppressed);
            }

            EvictIfAtCapacity();
            Touch(new Entry(key, nowSeconds, 0));
            return new RejectLogDecision(true, 0);
        }

        /// <summary>このキーの記録を破棄する（切断時などに呼ぶ）。</summary>
        /// <param name="key">キー。</param>
        public void Forget(ulong key)
        {
            if (_lookup.TryGetValue(key, out var node))
            {
                _lruOrder.Remove(node);
                _lookup.Remove(key);
            }
        }

        /// <summary>キーの値を更新し、LRU の先頭（最も最近使われた位置）に移動する。</summary>
        private void Touch(Entry entry)
        {
            var node = new LinkedListNode<Entry>(entry);
            _lruOrder.AddFirst(node);
            _lookup[entry.Key] = node;
        }

        /// <summary>容量に達していたら、最も長く使われていないキーから削除する。</summary>
        private void EvictIfAtCapacity()
        {
            while (_lookup.Count >= _capacity && _lruOrder.Last != null)
            {
                var oldest = _lruOrder.Last;
                _lruOrder.RemoveLast();
                _lookup.Remove(oldest.Value.Key);
            }
        }

        /// <summary>キー 1 件分の間引き状態（不変値）。</summary>
        private readonly struct Entry
        {
            public Entry(ulong key, double lastLoggedSeconds, int suppressedSinceLastLog)
            {
                Key = key;
                LastLoggedSeconds = lastLoggedSeconds;
                SuppressedSinceLastLog = suppressedSinceLastLog;
            }

            public ulong Key { get; }

            public double LastLoggedSeconds { get; }

            public int SuppressedSinceLastLog { get; }
        }
    }
}
