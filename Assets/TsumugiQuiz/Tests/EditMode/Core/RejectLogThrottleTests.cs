using System;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="RejectLogThrottle"/>（キー単位のログ間引き・容量上限）のテスト（docs/network.md §9、#52）。
    /// </summary>
    public class RejectLogThrottleTests
    {
        private const ulong Key1 = 1;
        private const ulong Key2 = 2;

        [Test]
        public void Evaluate_FirstCall_ShouldLogIsTrue()
        {
            var throttle = new RejectLogThrottle(1.0);

            var decision = throttle.Evaluate(Key1, 0.0);

            Assert.IsTrue(decision.ShouldLog);
            Assert.AreEqual(0, decision.SuppressedSinceLastLog);
        }

        [Test]
        public void Evaluate_WithinInterval_IsSuppressed()
        {
            var throttle = new RejectLogThrottle(1.0);
            throttle.Evaluate(Key1, 0.0);

            var decision = throttle.Evaluate(Key1, 0.5);

            Assert.IsFalse(decision.ShouldLog, "間隔内の連続呼び出しは間引かれるはず。");
        }

        [Test]
        public void Evaluate_AfterInterval_ShouldLogAgainWithSuppressedSummary()
        {
            var throttle = new RejectLogThrottle(1.0);
            throttle.Evaluate(Key1, 0.0);
            throttle.Evaluate(Key1, 0.2); // 間引かれる（1 件目）。
            throttle.Evaluate(Key1, 0.4); // 間引かれる（2 件目）。

            var decision = throttle.Evaluate(Key1, 1.0);

            Assert.IsTrue(decision.ShouldLog, "間隔が経過したら再び出してよいはず。");
            Assert.AreEqual(2, decision.SuppressedSinceLastLog, "前回ログからここまでに間引かれた件数が入るはず。");
        }

        [Test]
        public void Evaluate_AfterLoggingAgain_SuppressedCountResets()
        {
            var throttle = new RejectLogThrottle(1.0);
            throttle.Evaluate(Key1, 0.0);
            throttle.Evaluate(Key1, 0.2);
            throttle.Evaluate(Key1, 1.0); // ここでサマリが 1 件出て、間引きカウントはリセットされる。

            var decision = throttle.Evaluate(Key1, 2.0);

            Assert.IsTrue(decision.ShouldLog);
            Assert.AreEqual(0, decision.SuppressedSinceLastLog, "直前でログを出しているので、間引かれた件数は 0 のはず。");
        }

        [Test]
        public void Evaluate_DifferentKeys_AreIndependent()
        {
            var throttle = new RejectLogThrottle(1.0);
            throttle.Evaluate(Key1, 0.0);

            Assert.IsTrue(throttle.Evaluate(Key2, 0.0).ShouldLog, "別キーは影響を受けないはず。");
        }

        [Test]
        public void Forget_AllowsImmediateLogAgain()
        {
            var throttle = new RejectLogThrottle(1.0);
            throttle.Evaluate(Key1, 0.0);

            throttle.Forget(Key1);

            var decision = throttle.Evaluate(Key1, 0.1);
            Assert.IsTrue(decision.ShouldLog, "Forget 後は間隔を待たず出してよいはず。");
            Assert.AreEqual(0, decision.SuppressedSinceLastLog);
        }

        [Test]
        public void Evaluate_ExceedingCapacity_EvictsLeastRecentlyUsedKey()
        {
            var throttle = new RejectLogThrottle(1.0, capacity: 2);
            throttle.Evaluate(1, 0.0);
            throttle.Evaluate(2, 0.0);

            // 容量 2 のところへ 3 件目のキーを入れると、最も長く使われていない Key1 が追い出されるはず。
            throttle.Evaluate(3, 0.0);

            // Key1 は追い出されたので「初回」として扱われ、間隔内でも間引かれずログを出してよいはず。
            var decision = throttle.Evaluate(1, 0.1);
            Assert.IsTrue(decision.ShouldLog, "容量超過で追い出されたキーは新規キー扱いになるはず。");
        }

        [Test]
        public void Evaluate_RecentlyUsedKey_IsNotEvictedBeforeOlderKey()
        {
            var throttle = new RejectLogThrottle(1.0, capacity: 3);
            throttle.Evaluate(1, 0.0);
            throttle.Evaluate(2, 0.0);
            throttle.Evaluate(3, 0.0);

            // Key1 を再利用して「最近使った」扱いにする（間隔内なので間引かれるが、LRU 順は更新される）。
            throttle.Evaluate(1, 0.5);

            // 4 件目を入れると容量（3）を超えるので、最も長く使われていない Key2 が追い出されるはず
            // （Key3 は Key1 より古いが、直近で Key1 が触られた後もまだ Key3 の方が古い位置に残る）。
            throttle.Evaluate(4, 0.5);

            var decisionForKey2 = throttle.Evaluate(2, 0.6);
            Assert.IsTrue(decisionForKey2.ShouldLog, "Key2 は追い出されて新規キー扱いになるはず。");

            var decisionForKey1 = throttle.Evaluate(1, 0.6);
            Assert.IsFalse(decisionForKey1.ShouldLog, "直近で触った Key1 は追い出されておらず、まだ間隔内なので間引かれるはず。");
        }

        [Test]
        public void Constructor_WithNegativeInterval_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RejectLogThrottle(-1.0));
        }

        [Test]
        public void Constructor_WithNonPositiveCapacity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RejectLogThrottle(1.0, capacity: 0));
        }
    }
}
