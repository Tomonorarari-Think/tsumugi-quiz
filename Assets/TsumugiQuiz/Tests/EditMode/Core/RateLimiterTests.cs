using System;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="RateLimiter"/>（クライアントごとのトークンバケット式レート制限）のテスト
    /// （docs/network.md §9「毎秒 20 メッセージ、バースト 60」、#52）。
    /// </summary>
    /// <remarks>
    /// 「直近 N 秒間で何回超過したか」の集計・切断判断は <c>TsumugiQuiz.Network.RpcRateGuard</c> の責務
    /// （<c>RpcRateLimitTests</c>、PlayMode）。本クラスは 1 回ごとの許可判定（レート・バースト・回復）だけを見る。
    /// </remarks>
    public class RateLimiterTests
    {
        private const ulong Client1 = 1;
        private const ulong Client2 = 2;

        [Test]
        public void Allow_WithinBurst_AllAllowed()
        {
            var limiter = new RateLimiter(ratePerSecond: 20, burstCapacity: 60);

            for (var i = 0; i < 60; i++)
            {
                Assert.IsTrue(limiter.Allow(Client1, 0.0), $"{i + 1} 件目はバースト内なので許可されるはず。");
            }
        }

        [Test]
        public void Allow_ExceedingBurst_RejectsExtra()
        {
            var limiter = new RateLimiter(ratePerSecond: 20, burstCapacity: 60);
            for (var i = 0; i < 60; i++)
            {
                limiter.Allow(Client1, 0.0);
            }

            Assert.IsFalse(limiter.Allow(Client1, 0.0), "バーストを使い切ったら 61 件目は拒否されるはず。");
        }

        [Test]
        public void Allow_RecoversTokensAtConfiguredRate()
        {
            var limiter = new RateLimiter(ratePerSecond: 20, burstCapacity: 60);
            for (var i = 0; i < 60; i++)
            {
                limiter.Allow(Client1, 0.0);
            }

            Assert.IsFalse(limiter.Allow(Client1, 0.0), "バーストを使い切った直後は拒否されるはず。");

            for (var i = 0; i < 20; i++)
            {
                Assert.IsTrue(limiter.Allow(Client1, 1.0), $"1 秒後に毎秒 20 個回復した分の {i + 1} 個目。");
            }

            Assert.IsFalse(limiter.Allow(Client1, 1.0), "回復した 20 個を使い切ったら再び拒否されるはず。");
        }

        [Test]
        public void Allow_PartialElapsedTime_RefillsProportionally()
        {
            var limiter = new RateLimiter(ratePerSecond: 20, burstCapacity: 60);
            for (var i = 0; i < 60; i++)
            {
                limiter.Allow(Client1, 0.0);
            }

            // 0.5 秒経過なら 10 個分だけ回復するはず。
            for (var i = 0; i < 10; i++)
            {
                Assert.IsTrue(limiter.Allow(Client1, 0.5), $"0.5 秒後に回復した 10 個のうち {i + 1} 個目。");
            }

            Assert.IsFalse(limiter.Allow(Client1, 0.5), "0.5 秒分（10 個）を使い切ったら拒否されるはず。");
        }

        [Test]
        public void Allow_DifferentClients_AreIndependent()
        {
            var limiter = new RateLimiter(ratePerSecond: 20, burstCapacity: 60);
            for (var i = 0; i < 60; i++)
            {
                limiter.Allow(Client1, 0.0);
            }

            Assert.IsFalse(limiter.Allow(Client1, 0.0), "Client1 はバーストを使い切っている。");
            Assert.IsTrue(limiter.Allow(Client2, 0.0), "Client2 は Client1 の消費に影響されないはず。");
        }

        [Test]
        public void Forget_ResetsClientToFreshState()
        {
            var limiter = new RateLimiter(ratePerSecond: 20, burstCapacity: 60);
            for (var i = 0; i < 60; i++)
            {
                limiter.Allow(Client1, 0.0);
            }

            Assert.IsFalse(limiter.Allow(Client1, 0.0));

            limiter.Forget(Client1);

            Assert.IsTrue(limiter.Allow(Client1, 0.0), "Forget 後は満タンのバーストから再開するはず。");
        }

        [Test]
        public void Allow_TimeGoingBackwards_DoesNotRefillOrThrow()
        {
            var limiter = new RateLimiter(ratePerSecond: 20, burstCapacity: 60);
            for (var i = 0; i < 60; i++)
            {
                limiter.Allow(Client1, 10.0);
            }

            Assert.DoesNotThrow(() => limiter.Allow(Client1, 5.0));
            Assert.IsFalse(limiter.Allow(Client1, 5.0), "時刻が逆行しても不正に回復してはいけない。");
        }

        [Test]
        public void Allow_TimeGoingBackwardsThenForward_RecoversFromTheLatestObservedTime()
        {
            // 1 回だけ異常に大きい時刻が来ても、その後は最新の観測時刻を基準に正しく回復できることを確かめる
            // （L9: RateLimiter と RpcRateGuard で時刻の巻き戻りの扱いを揃える）。
            var limiter = new RateLimiter(ratePerSecond: 20, burstCapacity: 60);
            for (var i = 0; i < 60; i++)
            {
                limiter.Allow(Client1, 1000.0); // 異常に先の時刻。
            }

            // 以後は正常な時刻（0 起点）に戻ったとする。この 1 件で基準時刻が 0.0 に更新される。
            Assert.IsFalse(limiter.Allow(Client1, 0.0), "逆行直後はまだ回復していないはず。");

            // 更新された基準（0.0）から 1 秒進んだので、毎秒 20 個の回復が効くはず。
            for (var i = 0; i < 20; i++)
            {
                Assert.IsTrue(limiter.Allow(Client1, 1.0), $"基準を更新した後、1 秒後に回復した 20 個のうち {i + 1} 個目。");
            }

            Assert.IsFalse(limiter.Allow(Client1, 1.0), "回復した 20 個を使い切ったら再び拒否されるはず。");
        }

        [Test]
        public void Constructor_WithNonPositiveRate_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RateLimiter(ratePerSecond: 0));
        }

        [Test]
        public void Constructor_WithNonPositiveBurst_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RateLimiter(burstCapacity: -1));
        }
    }
}
