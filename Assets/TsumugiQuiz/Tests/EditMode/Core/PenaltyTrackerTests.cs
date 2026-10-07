using System;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="PenaltyTracker"/>（「次問休み」ペナルティ）のテスト
    /// （docs/room-settings.md §1「得点」、docs/network.md §6.4、#18）。
    /// </summary>
    public class PenaltyTrackerTests
    {
        private const ulong Client1 = 1;
        private const ulong Client2 = 2;

        [Test]
        public void Empty_HasNoPenalties()
        {
            var tracker = PenaltyTracker.Empty;

            Assert.IsEmpty(tracker.Pending);
            Assert.IsEmpty(tracker.SuspendedFor(0));
            Assert.AreEqual(PenaltyTracker.NoQuestionIndex, tracker.SuspendedQuestionIndex);
            Assert.IsFalse(tracker.IsSuspended(0, Client1));
        }

        [Test]
        public void WithSkipNext_ReturnsNewInstanceAndKeepsOriginal()
        {
            var tracker = PenaltyTracker.Empty;

            var next = tracker.WithSkipNext(Client1);

            Assert.IsTrue(next.IsPending(Client1));
            Assert.IsFalse(tracker.IsPending(Client1), "元のインスタンスは変わらない。");
            Assert.AreNotSame(tracker, next);
        }

        [Test]
        public void WithSkipNext_SameClientTwice_IsNotDuplicated()
        {
            var tracker = PenaltyTracker.Empty.WithSkipNext(Client1).WithSkipNext(Client1);

            Assert.AreEqual(1, tracker.Pending.Count);
        }

        [Test]
        public void WithQuestionStarted_MovesPendingToThatQuestion()
        {
            var tracker = PenaltyTracker.Empty.WithSkipNext(Client1).WithSkipNext(Client2);

            var started = tracker.WithQuestionStarted(3);

            Assert.IsEmpty(started.Pending, "確定したので次問用の予定は空になる。");
            Assert.AreEqual(3, started.SuspendedQuestionIndex);
            CollectionAssert.AreEquivalent(new ulong[] { Client1, Client2 }, started.SuspendedFor(3));
            Assert.IsTrue(started.IsSuspended(3, Client1));
            Assert.IsFalse(started.IsSuspended(4, Client1), "確定していない問題では休みではない。");
            Assert.IsEmpty(started.SuspendedFor(2));
        }

        [Test]
        public void WithQuestionStarted_Again_DropsPreviousQuestionSet()
        {
            var tracker = PenaltyTracker.Empty.WithSkipNext(Client1).WithQuestionStarted(1);

            var nextQuestion = tracker.WithQuestionStarted(2);

            Assert.IsEmpty(nextQuestion.SuspendedFor(1), "ペナルティは 1 問で解ける。");
            Assert.IsEmpty(nextQuestion.SuspendedFor(2));
            Assert.IsTrue(tracker.IsSuspended(1, Client1), "元のインスタンスは変わらない。");
        }

        [Test]
        public void WithQuestionStarted_KeepsPenaltyWhenIndexIsSkipped()
        {
            // 司会が問題番号を飛ばしても、次に出題された問題で休みになる。
            var tracker = PenaltyTracker.Empty.WithSkipNext(Client1).WithQuestionStarted(7);

            Assert.IsTrue(tracker.IsSuspended(7, Client1));
        }

        [Test]
        public void WithQuestionStarted_WithNegativeIndex_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PenaltyTracker.Empty.WithQuestionStarted(-1));
        }

        [Test]
        public void WithoutClient_RemovesFromPendingAndSuspended()
        {
            var tracker = PenaltyTracker.Empty
                .WithSkipNext(Client1)
                .WithQuestionStarted(0)
                .WithSkipNext(Client2);

            var cleaned = tracker.WithoutClient(Client1).WithoutClient(Client2);

            Assert.IsFalse(cleaned.IsSuspended(0, Client1));
            Assert.IsFalse(cleaned.IsPending(Client2));
            Assert.IsTrue(tracker.IsSuspended(0, Client1), "元のインスタンスは変わらない。");
            Assert.IsTrue(tracker.IsPending(Client2));
        }

        [Test]
        public void SuspendedFor_WithNegativeIndex_IsEmpty()
        {
            var tracker = PenaltyTracker.Empty.WithSkipNext(Client1).WithQuestionStarted(0);

            Assert.IsEmpty(tracker.SuspendedFor(-1));
            Assert.IsFalse(tracker.IsSuspended(-1, Client1));
        }

        // --- 再接続の引き継ぎ（#84） ---

        [Test]
        public void WithClientIdChanged_MovesPendingAndSuspended()
        {
            var tracker = PenaltyTracker.Empty
                .WithSkipNext(Client1)
                .WithQuestionStarted(0)
                .WithSkipNext(Client1);

            Assert.IsTrue(tracker.IsSuspended(0, Client1), "確定済みの休み。");
            Assert.IsTrue(tracker.IsPending(Client1), "次問ぶんの予定。");

            var moved = tracker.WithClientIdChanged(Client1, 41UL);

            Assert.IsTrue(moved.IsSuspended(0, 41UL));
            Assert.IsTrue(moved.IsPending(41UL));
            Assert.IsFalse(moved.IsSuspended(0, Client1));
            Assert.IsFalse(moved.IsPending(Client1));
            Assert.IsTrue(tracker.IsPending(Client1), "元のインスタンスは変わらない（不変）。");
        }

        [Test]
        public void WithClientIdChanged_WithUnknownClient_ReturnsSameInstance()
        {
            var tracker = PenaltyTracker.Empty.WithSkipNext(Client1);

            Assert.IsFalse(tracker.Contains(Client2));
            Assert.AreSame(tracker, tracker.WithClientIdChanged(Client2, 41UL), "移すものが無ければ作り直さない。");
            Assert.AreSame(tracker, tracker.WithClientIdChanged(Client1, Client1), "同じ ID なら何もしない。");
        }

        [Test]
        public void WithClientIdChanged_WhenTargetAlreadyPenalized_DoesNotDuplicate()
        {
            var tracker = PenaltyTracker.Empty.WithSkipNext(Client1).WithSkipNext(Client2);

            var moved = tracker.WithClientIdChanged(Client1, Client2);

            Assert.IsTrue(moved.IsPending(Client2));
            Assert.IsFalse(moved.IsPending(Client1));
            Assert.AreEqual(1, moved.Pending.Count, "同じ ID を二重に積まない。");
        }
    }
}
