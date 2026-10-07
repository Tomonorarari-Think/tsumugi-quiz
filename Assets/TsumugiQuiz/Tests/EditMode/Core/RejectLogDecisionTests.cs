using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="RejectLogDecision"/>（<see cref="RejectLogThrottle.Evaluate"/> の判定結果）のテスト
    /// （docs/network.md §9、#52）。判定そのもの（間引き・LRU）は <see cref="RejectLogThrottleTests"/> で
    /// カバー済みなので、ここでは <see cref="RejectLogDecision.FormatSuppressedSuffix"/>（#83、
    /// サマリ書式の一本化）だけを確かめる。
    /// </summary>
    public class RejectLogDecisionTests
    {
        [Test]
        public void FormatSuppressedSuffix_WithZeroSuppressed_ReturnsEmpty()
        {
            var decision = new RejectLogDecision(shouldLog: true, suppressedSinceLastLog: 0);

            Assert.AreEqual(string.Empty, decision.FormatSuppressedSuffix());
        }

        [Test]
        public void FormatSuppressedSuffix_WithSuppressedCount_ReturnsFormattedSummary()
        {
            var decision = new RejectLogDecision(shouldLog: true, suppressedSinceLastLog: 3);

            Assert.AreEqual("（ほか 3 件を間引きました）", decision.FormatSuppressedSuffix());
        }
    }
}
