using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// クライアントごとのトークンバケット式レート制限（純 C#、Unity API 非依存。
    /// docs/network.md §9「RPC の頻度：クライアントごとに毎秒 20 メッセージ、バースト 60」、#52）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 時刻は呼び出し側が渡す（サーバー時刻軸の秒）。<c>DateTime.Now</c> 等に依存させないことで、
    /// EditMode テストから任意の時刻を注入できるようにしてある。
    /// </para>
    /// <para>
    /// クライアントごとの状態は内部辞書（<see cref="_buckets"/>）で持つが、
    /// 各バケットの値そのものは不変（<see cref="Bucket"/> は読み取り専用構造体で、更新のたびに
    /// 新しい値を辞書へ入れ直す）。
    /// </para>
    /// <para>
    /// 「超過が続いたら切断する」という判断（直近 N 秒間の超過回数の集計）は、本クラスの責務ではなく
    /// 呼び出し側（<c>TsumugiQuiz.Network.RpcRateGuard</c>）が行う。本クラスが返すのは
    /// 「このトークンで許可できるか」という 1 回ごとの判定だけであり、
    /// 累計・窓ごとの超過回数はここでは持たない（レビュー指摘: 2 箇所で似た超過カウントを持つと
    /// どちらが正か曖昧になるため、集計は <c>RpcRateGuard</c> 側の 1 箇所に寄せた）。
    /// </para>
    /// </remarks>
    public sealed class RateLimiter
    {
        /// <summary>docs/network.md §9 の既定レート（毎秒 20 メッセージ）。</summary>
        public const double DefaultRatePerSecond = 20.0;

        /// <summary>docs/network.md §9 の既定バースト（60 メッセージ）。</summary>
        public const double DefaultBurstCapacity = 60.0;

        private readonly double _ratePerSecond;
        private readonly double _burstCapacity;
        private readonly Dictionary<ulong, Bucket> _buckets = new Dictionary<ulong, Bucket>();

        /// <summary>
        /// レート制限器を作る。
        /// </summary>
        /// <param name="ratePerSecond">毎秒補充されるトークン数（毎秒許容するメッセージ数）。</param>
        /// <param name="burstCapacity">トークンの最大貯蔵数（瞬間的に許容するバースト数）。</param>
        /// <exception cref="ArgumentOutOfRangeException">どちらかが 0 以下のとき。</exception>
        public RateLimiter(double ratePerSecond = DefaultRatePerSecond, double burstCapacity = DefaultBurstCapacity)
        {
            if (ratePerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ratePerSecond), "レートは正の値である必要があります。");
            }

            if (burstCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(burstCapacity), "バーストは正の値である必要があります。");
            }

            _ratePerSecond = ratePerSecond;
            _burstCapacity = burstCapacity;
        }

        /// <summary>
        /// このクライアントからの 1 件を許可するかどうかを判定する。許可した場合はトークンを 1 個消費する。
        /// </summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <param name="nowSeconds">
        /// 現在時刻（秒、任意の原点でよい）。前回より過去（または同時刻）の値が来ても例外にはせず、
        /// 単に補充を行わない（NGO の時刻はほぼ単調増加だが、tick の丸めで僅かに前後する可能性があるため）。
        /// ただし基準時刻は常に最後に観測した <paramref name="nowSeconds"/> へ更新する
        /// （1 回だけ異常に大きい値が来て以後ずっと補充できなくなる、という固着を避けるため。
        /// <c>RpcRateGuard</c> の集計窓も同じ方針で時刻の巻き戻りを扱う）。
        /// </param>
        /// <returns>許可するなら true。</returns>
        public bool Allow(ulong clientId, double nowSeconds)
        {
            var bucket = _buckets.TryGetValue(clientId, out var existing)
                ? existing.Refill(nowSeconds, _ratePerSecond, _burstCapacity)
                : new Bucket(_burstCapacity, nowSeconds);

            bool allowed;
            Bucket updated;
            if (bucket.Tokens >= 1.0)
            {
                allowed = true;
                updated = bucket.WithTokensConsumed();
            }
            else
            {
                allowed = false;
                updated = bucket;
            }

            _buckets[clientId] = updated;
            return allowed;
        }

        /// <summary>
        /// このクライアントの状態を破棄する（切断時・辞書を無制限に伸ばさないために呼ぶ、docs/network.md §9）。
        /// 次に <see cref="Allow"/> を呼ぶと、新規クライアントと同じ満タンの状態から始まる。
        /// </summary>
        /// <param name="clientId">クライアント ID。</param>
        public void Forget(ulong clientId) => _buckets.Remove(clientId);

        /// <summary>クライアント 1 人分のトークンバケットの状態（不変値）。</summary>
        private readonly struct Bucket
        {
            public Bucket(double tokens, double lastRefillSeconds)
            {
                Tokens = tokens;
                LastRefillSeconds = lastRefillSeconds;
            }

            /// <summary>現在の残トークン数。</summary>
            public double Tokens { get; }

            /// <summary>直近に観測した時刻（秒）。補充の基準点であり、時刻が巻き戻っても常に最新の値に更新する。</summary>
            public double LastRefillSeconds { get; }

            /// <summary>
            /// 経過時間分のトークンを補充した新しい値を返す。
            /// 時刻が進んでいなければ（同時刻・逆行）トークンは変えないが、基準時刻は今回の値に合わせておく
            /// （逆行が一時的なもので、その後また正しく前進し始めたときに経過時間を正しく計算するため）。
            /// </summary>
            public Bucket Refill(double nowSeconds, double ratePerSecond, double burstCapacity)
            {
                var elapsed = nowSeconds - LastRefillSeconds;
                if (elapsed <= 0)
                {
                    return new Bucket(Tokens, nowSeconds);
                }

                var refilled = Math.Min(burstCapacity, Tokens + (elapsed * ratePerSecond));
                return new Bucket(refilled, nowSeconds);
            }

            /// <summary>トークンを 1 個消費した新しい値を返す。</summary>
            public Bucket WithTokensConsumed() => new Bucket(Tokens - 1.0, LastRefillSeconds);
        }
    }
}
