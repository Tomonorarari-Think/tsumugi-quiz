using System.Threading;
using System.Threading.Tasks;
using TsumugiQuiz.Network.Nat;

namespace TsumugiQuiz.Tests.Shared.Network.Nat
{
    /// <summary>
    /// テスト用の NAT デバイス探索。
    /// 「すぐ見つかる」「探索時間いっぱい待って見つからない（タイムアウト）」
    /// 「即座に見つからない（デバイス未検出）」を切り替えられる。
    /// </summary>
    internal sealed class FakeNatDiscovery : INatDiscovery
    {
        private readonly INatDevice _device;
        private readonly bool _consumeTimeout;

        /// <summary>
        /// 探索を作る。
        /// </summary>
        /// <param name="device">見つかるデバイス。null なら見つからない。</param>
        /// <param name="consumeTimeout">true なら指定タイムアウトいっぱい待ってから結果を返す。</param>
        public FakeNatDiscovery(INatDevice device, bool consumeTimeout = false)
        {
            _device = device;
            _consumeTimeout = consumeTimeout;
        }

        /// <summary><see cref="DiscoverAsync"/> が呼ばれた回数。</summary>
        public int CallCount { get; private set; }

        /// <summary>最後に渡されたタイムアウト（ミリ秒）。</summary>
        public int LastTimeoutMs { get; private set; }

        /// <inheritdoc />
        public async Task<INatDevice> DiscoverAsync(int timeoutMs, CancellationToken cancellationToken)
        {
            CallCount++;
            LastTimeoutMs = timeoutMs;

            if (_consumeTimeout)
            {
                await Task.Delay(timeoutMs, cancellationToken).ConfigureAwait(false);
            }

            return _device;
        }
    }
}
