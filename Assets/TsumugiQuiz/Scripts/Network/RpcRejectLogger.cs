using System;
using TsumugiQuiz.Core;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// RPC 受信時の棄却ログを、クライアント単位で間引いて <see cref="Debug.LogWarning(object)"/> するヘルパー
    /// （docs/network.md §9）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GameSession"/> / <see cref="QuestionDistributor"/> / <see cref="TtsSyncCoordinator"/> は、
    /// それぞれ自前の <c>Dictionary&lt;ulong, double&gt;</c> と <c>LogRejected</c> で同じ間引きを
    /// 個別に実装していた（#12 / #13 / #23）。本クラスは、その 3 系統を
    /// <see cref="RejectLogThrottle"/>（Core、#52）を包む形で 1 つに統合する（#72）。
    /// </para>
    /// <para>
    /// 間引かれた場合は何もログを出さない。間引かれずに出す番が来たときは、
    /// 前回ログ出力からここまでに間引かれた件数を「ほか N 件を間引きました」として末尾に添える
    /// （<see cref="RejectLogDecision.FormatSuppressedSuffix"/>、<see cref="ConnectionApprovalHandler"/> /
    /// <see cref="RpcRateGuard"/> と同じ書式）。
    /// </para>
    /// <para>
    /// <see cref="RpcRateGuard"/> 自身の警告ログ間引きは対象外。<see cref="RpcRateGuard"/> は元から
    /// <see cref="RejectLogThrottle"/> を直接使っており、二重に統合すると差分が無駄に増えるため
    /// 本クラスには置き換えない（#72 の作業内容は <c>GameSession</c> / <c>QuestionDistributor</c> /
    /// <c>TtsSyncCoordinator</c> の <c>LogRejected</c> のみ）。
    /// </para>
    /// </remarks>
    public sealed class RpcRejectLogger
    {
        /// <summary>既定の間引き間隔（秒）。クライアントごとに 1 秒 1 回（docs/network.md §9）。</summary>
        public const double DefaultIntervalSeconds = 1.0;

        private readonly RejectLogThrottle _throttle;
        private readonly NetworkManager _networkManager;

        /// <summary>
        /// ロガーを作る。<paramref name="networkManager"/> の捕捉タイミングは呼び出し元
        /// （<see cref="GameSession"/> 等）の <c>RejectLogger</c> プロパティが初回の棄却時（サーバー RPC の中、
        /// すでにスポーン済み）に遅延生成するときであり、デスポーンのたびに作り直す（#72・#83）。
        /// </summary>
        /// <param name="networkManager">
        /// <see cref="LogRejected(ulong, string)"/> でサーバー時刻（<c>ServerTime.Time</c>）を取るために使う。
        /// null は許可しない（<see cref="RpcRateGuard"/> と同じ方針、#83 レビュー M1）。
        /// </param>
        /// <param name="intervalSeconds">
        /// 同じクライアントでログを出してよい最短間隔（秒）。既定 1 秒。<paramref name="capacity"/> とあわせて
        /// 名前付き引数で渡すこと（#83 レビュー L4）。
        /// </param>
        /// <param name="capacity">
        /// 保持するクライアント数の上限（<see cref="RejectLogThrottle"/> に委譲。既定
        /// <see cref="RejectLogThrottle.DefaultCapacity"/> 件・LRU）。
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="networkManager"/> が null のとき。</exception>
        public RpcRejectLogger(
            NetworkManager networkManager,
            double intervalSeconds = DefaultIntervalSeconds,
            int capacity = RejectLogThrottle.DefaultCapacity)
        {
            _networkManager = networkManager != null
                ? networkManager
                : throw new ArgumentNullException(nameof(networkManager));
            _throttle = new RejectLogThrottle(intervalSeconds, capacity);
        }

        /// <summary>
        /// テスト専用（<c>internal</c>）。<see cref="NetworkManager"/> を用意しづらい EditMode テストのために、
        /// サーバー時刻を明示的に渡す <see cref="LogRejected(ulong, double, string)"/> だけを使う想定で作る
        /// （#83 レビュー M1）。このコンストラクタで作った場合、<see cref="LogRejected(ulong, string)"/>
        /// （<see cref="NetworkManager"/> 経由でサーバー時刻を取る版）はサーバー時刻を 0 秒として扱う。
        /// </summary>
        /// <param name="intervalSeconds">
        /// 同じクライアントでログを出してよい最短間隔（秒）。既定 1 秒。<paramref name="capacity"/> とあわせて
        /// 名前付き引数で渡すこと（#83 レビュー L4）。
        /// </param>
        /// <param name="capacity">
        /// 保持するクライアント数の上限（<see cref="RejectLogThrottle"/> に委譲。既定
        /// <see cref="RejectLogThrottle.DefaultCapacity"/> 件・LRU）。
        /// </param>
        internal RpcRejectLogger(
            double intervalSeconds = DefaultIntervalSeconds,
            int capacity = RejectLogThrottle.DefaultCapacity)
        {
            _networkManager = null;
            _throttle = new RejectLogThrottle(intervalSeconds, capacity);
        }

        /// <summary>
        /// 棄却をログに残す（間引かれた場合は何もしない）。コンストラクタで渡した <see cref="NetworkManager"/> の
        /// サーバー時刻を使う（<see cref="GameSession"/> 等の呼び出し元が個別に
        /// <c>NetworkManager.ServerTime.Time</c> を取り出す重複を解消するための入口、#83）。
        /// </summary>
        /// <param name="clientId">棄却したクライアント ID（間引きのキー）。</param>
        /// <param name="message">ログに残す内容。null・空文字なら何もしない（間引きの状態も変えない）。</param>
        public void LogRejected(ulong clientId, string message)
        {
            var serverNow = _networkManager != null ? _networkManager.ServerTime.Time : 0.0;
            LogRejected(clientId, serverNow, message);
        }

        /// <summary>
        /// 棄却をログに残す（間引かれた場合は何もしない）。サーバー時刻を呼び出し側が明示的に渡す版
        /// （<see cref="NetworkManager"/> を持たないテストから、間引きの境界を決定的に検証するために使う）。
        /// </summary>
        /// <param name="clientId">棄却したクライアント ID（間引きのキー）。</param>
        /// <param name="nowSeconds">現在時刻（秒）。呼び出し側のサーバー時刻を渡すこと。</param>
        /// <param name="message">ログに残す内容。null・空文字なら何もしない（間引きの状態も変えない）。</param>
        public void LogRejected(ulong clientId, double nowSeconds, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            var decision = _throttle.Evaluate(clientId, nowSeconds);
            if (!decision.ShouldLog)
            {
                return;
            }

            Debug.LogWarning(message + decision.FormatSuppressedSuffix());
        }

        /// <summary>このクライアントの間引き記録を破棄する（切断時に呼ぶ）。</summary>
        /// <param name="clientId">クライアント ID。</param>
        public void Forget(ulong clientId) => _throttle.Forget(clientId);
    }
}
