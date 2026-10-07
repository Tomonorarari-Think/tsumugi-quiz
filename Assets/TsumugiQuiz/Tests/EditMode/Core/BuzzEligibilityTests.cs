using System;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 「押せる参加者が 1 人以上居るか」の純関数（<see cref="BuzzEligibility"/>、#200）のテスト。
    /// </summary>
    public class BuzzEligibilityTests
    {
        private const ulong Host = 0;
        private const ulong ClientA = 1;
        private const ulong ClientB = 2;
        private const ulong ClientC = 3;

        private static readonly ulong[] None = Array.Empty<ulong>();

        private static Func<ulong, bool> Penalized(params ulong[] clientIds)
        {
            var set = new HashSet<ulong>(clientIds);
            return set.Contains;
        }

        // --- 接続中の参加者 ---

        [Test]
        public void OneConnectedClientWithoutPenalty_ReturnsTrue()
        {
            Assert.IsTrue(BuzzEligibility.HasEligibleBuzzer(new[] { ClientA }, None, null, Penalized()));
        }

        [Test]
        public void AllConnectedClientsPenalized_ReturnsFalse()
        {
            Assert.IsFalse(
                BuzzEligibility.HasEligibleBuzzer(new[] { Host, ClientA, ClientB }, None, null, Penalized(Host, ClientA, ClientB)),
                "誤答済み・次問休みの人しか居なければ押せる人は居ない。");
        }

        [Test]
        public void OneOfSeveralNotPenalized_ReturnsTrue()
        {
            Assert.IsTrue(
                BuzzEligibility.HasEligibleBuzzer(new[] { Host, ClientA, ClientB }, None, null, Penalized(Host, ClientA)));
        }

        [Test]
        public void PlayerHost_IsCountedAsEligible()
        {
            Assert.IsTrue(
                BuzzEligibility.HasEligibleBuzzer(new[] { Host, ClientA }, None, null, Penalized(ClientA)),
                "ホストも参加者なら押せる人に数える（受付側もホストの押下を受理する）。");
        }

        [Test]
        public void ModeratorHost_IsNotCountedAsEligible()
        {
            Assert.IsFalse(
                BuzzEligibility.HasEligibleBuzzer(new[] { Host, ClientA }, None, Host, Penalized(ClientA)),
                "司会専任のホストは早押しできない（BuzzRpc が棄却する）ので数えない。");
        }

        [Test]
        public void ModeratorHost_OtherParticipantsAreStillCounted()
        {
            Assert.IsTrue(BuzzEligibility.HasEligibleBuzzer(new[] { Host, ClientA }, None, Host, Penalized()));
        }

        // --- 切断中だが席を保持している参加者（PR #203 レビュー H-1） ---

        [Test]
        public void RetainedDisconnectedParticipantWithoutPenalty_IsCounted()
        {
            Assert.IsTrue(
                BuzzEligibility.HasEligibleBuzzer(new[] { ClientA }, new[] { ClientB }, null, Penalized(ClientA)),
                "一瞬切断しただけの人は戻ってくる可能性があるので数える。");
        }

        [Test]
        public void RetainedDisconnectedParticipantWithPenalty_IsNotCounted()
        {
            Assert.IsFalse(
                BuzzEligibility.HasEligibleBuzzer(new[] { ClientA }, new[] { ClientB }, null, Penalized(ClientA, ClientB)),
                "切断中でも誤答済み・次問休みなら数えない（切断時の ID のペナルティで判定する）。");
        }

        [Test]
        public void ForgottenSeat_IsNoLongerCounted()
        {
            // 席が名簿から消えた（保持期間切れ・手動削除）人は切断中の一覧にも居ない。
            Assert.IsFalse(BuzzEligibility.HasEligibleBuzzer(new[] { ClientA }, None, null, Penalized(ClientA)));
        }

        [Test]
        public void ReconnectedBeforeSeatTransfer_NewIdIsCountedAsSafeSide()
        {
            // 再接続の直後、席の引き継ぎが終わるまでは新しい ID（ClientC）にペナルティが無い。
            // 「居る」側に倒れる（締めるのが遅れるだけ）ことを固定する（PR #203 レビュー L-2）。
            Assert.IsTrue(
                BuzzEligibility.HasEligibleBuzzer(new[] { ClientA, ClientC }, new[] { ClientB }, null, Penalized(ClientA, ClientB)));
        }

        // --- 参加者が 0 人（判定の材料が無い、PR #203 レビュー M-1） ---

        [Test]
        public void NoParticipantsAtAll_ReturnsTrue()
        {
            Assert.IsTrue(
                BuzzEligibility.HasEligibleBuzzer(None, None, null, Penalized()),
                "参加者が 0 人なら判定の材料が無いので締めない（時間切れまで待つ）。");
        }

        [Test]
        public void OnlyModeratorHost_ReturnsTrue()
        {
            Assert.IsTrue(
                BuzzEligibility.HasEligibleBuzzer(new[] { Host }, None, Host, Penalized()),
                "司会専任のホストしか居ない部屋で、問題が次々に締まって流れないようにする。");
        }

        // --- 引数の検証 ---

        [Test]
        public void NullConnectedClientIds_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => BuzzEligibility.HasEligibleBuzzer(null, None, null, Penalized()));
        }

        [Test]
        public void NullRetainedDisconnectedClientIds_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => BuzzEligibility.HasEligibleBuzzer(new[] { ClientA }, null, null, Penalized()));
        }

        [Test]
        public void NullIsPenalized_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => BuzzEligibility.HasEligibleBuzzer(new[] { ClientA }, None, null, null));
        }
    }
}
