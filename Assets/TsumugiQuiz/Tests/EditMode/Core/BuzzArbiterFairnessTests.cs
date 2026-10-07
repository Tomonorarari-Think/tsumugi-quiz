using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// RTT の異なるクライアント間で早押しが公平になること（docs/network.md §6.5）の検証。
    /// クライアントは自分の LocalTime が T0 に達した時点で受付を開始するため、
    /// 報告値は「T0 + 反応時間」になり、片道遅延は到着時刻（serverNow）側にだけ現れる。
    /// </summary>
    public class BuzzArbiterFairnessTests
    {
        private const double T0 = 1000.0;
        private const double Window = 0.15;

        private const ulong FastClient = 1;    // RTT 10ms 相当
        private const ulong SlowClient = 2;    // RTT 200ms 相当
        private const double FastOneWaySec = 0.005;
        private const double SlowOneWaySec = 0.100;

        [Test]
        public void SameReactionTimeWithDifferentRtt_AlwaysTie_AndWinRateConvergesToHalf()
        {
            const int rounds = 2000;
            const double reactionSec = 0.30;

            var rng = new DeterministicRandom(20260913UL);
            var fastWins = 0;

            for (var i = 0; i < rounds; i++)
            {
                var arbiter = new BuzzArbiter(T0, Window);

                // 2 人とも「自分の時計で T0 から 0.30 秒後」に押す。
                var reported = T0 + reactionSec;
                var fastArrival = reported + FastOneWaySec;
                var slowArrival = reported + SlowOneWaySec;

                Assert.IsTrue(arbiter.Accept(FastClient, reported, fastArrival, out _));
                Assert.IsTrue(arbiter.Accept(SlowClient, reported, slowArrival, out _));

                // 遅い側も集計窓（150ms）の内側に収まる。
                Assert.Less(slowArrival, arbiter.DeadlineServerTime.Value);

                Assert.IsTrue(arbiter.TryResolve(fastArrival + Window, rng, out var resolution));
                Assert.IsTrue(resolution.Value.WasTie, "同じ dt なので必ず同着抽選になること");
                Assert.AreEqual(BuzzClamp.None, resolution.Value.WinnerClamp, "丸め補正は起きないこと");

                if (resolution.Value.WinnerClientId == FastClient)
                {
                    fastWins++;
                }
            }

            var winRate = (double)fastWins / rounds;
            Assert.That(winRate, Is.EqualTo(0.5).Within(0.05),
                $"RTT の小さいクライアントの勝率が偏っています: {winRate:F3}（{fastWins}/{rounds}）");
        }

        [Test]
        public void SlowerClientWithSmallerDt_WinsDespiteLateArrival()
        {
            var arbiter = new BuzzArbiter(T0, Window);

            // 速い回線のクライアントは 0.32 秒、遅い回線のクライアントは 0.30 秒で押した。
            var fastReported = T0 + 0.32;
            var slowReported = T0 + 0.30;
            var fastArrival = fastReported + FastOneWaySec;
            var slowArrival = slowReported + SlowOneWaySec;   // 到着は遅い側が後

            Assert.Less(fastArrival, slowArrival, "到着順は速い回線のクライアントが先であること");
            Assert.IsTrue(arbiter.Accept(FastClient, fastReported, fastArrival, out _));
            Assert.IsTrue(arbiter.Accept(SlowClient, slowReported, slowArrival, out _));

            Assert.IsTrue(arbiter.TryResolve(fastArrival + Window, new QueuedRandom(), out var resolution));

            Assert.AreEqual(SlowClient, resolution.Value.WinnerClientId);
            Assert.IsFalse(resolution.Value.WasTie);
        }

        [Test]
        public void BuzzArrivingAfterWindow_IsRejected_NotSilentlyIgnored()
        {
            var arbiter = new BuzzArbiter(T0, Window);
            var fastArrival = T0 + 0.30 + FastOneWaySec;
            Assert.IsTrue(arbiter.Accept(FastClient, T0 + 0.30, fastArrival, out _));

            // 集計窓を過ぎて届いた押下は、裁定前でも棄却される（理由が残る）。
            var accepted = arbiter.Accept(SlowClient, T0 + 0.25, fastArrival + Window + 0.05, out var reason);

            Assert.IsFalse(accepted);
            Assert.AreEqual(BuzzReject.WindowClosed, reason);

            // 裁定後に届いた場合は NotOpen になる。
            Assert.IsTrue(arbiter.TryResolve(fastArrival + Window, new QueuedRandom(), out _));
            Assert.IsFalse(arbiter.Accept(SlowClient, T0 + 0.25, fastArrival + Window + 0.10, out var lateReason));
            Assert.AreEqual(BuzzReject.NotOpen, lateReason);
        }
    }
}
