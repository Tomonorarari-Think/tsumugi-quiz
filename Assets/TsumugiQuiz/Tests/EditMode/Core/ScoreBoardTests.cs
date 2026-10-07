using System;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="ScoreRules"/> / <see cref="ScoreBoard"/> のテスト
    /// （docs/room-settings.md §1「得点」、仮決め K19、#18）。
    /// </summary>
    public class ScoreBoardTests
    {
        private const ulong Client1 = 1;
        private const ulong Client2 = 2;

        // --- ScoreRules ---

        [Test]
        public void Default_MatchesRoomSettingsDocument()
        {
            var rules = ScoreRules.Default;

            Assert.AreEqual(10, rules.CorrectPoints);
            Assert.AreEqual(0, rules.WrongPoints);
            Assert.AreEqual(PenaltyKind.SkipNext, rules.PenaltyKind);
            Assert.AreEqual(-5, rules.PenaltyPoints);
            Assert.AreEqual(10, rules.CorrectDelta);
            Assert.AreEqual(0, rules.WrongDelta, "既定は次問休みなので、誤答では得点が動かない。");
            Assert.IsTrue(rules.AppliesSkipNext);
        }

        [Test]
        public void WrongDelta_WithMinusPoints_IncludesPenalty()
        {
            var rules = ScoreRules.Default.WithPenalty(PenaltyKind.MinusPoints, -5);

            Assert.AreEqual(-5, rules.WrongDelta);
            Assert.IsFalse(rules.AppliesSkipNext, "減点ペナルティでは次問休みにしない。");
        }

        [Test]
        public void WrongDelta_AddsIncorrectPointsAndPenalty()
        {
            // docs/room-settings.md の incorrectPoints と penaltyMinusPoints は足し合わせる（#18 の判断）。
            var rules = new ScoreRules(
                correctPoints: 20, wrongPoints: -1, penaltyKind: PenaltyKind.MinusPoints, penaltyPoints: -5);

            Assert.AreEqual(20, rules.CorrectDelta);
            Assert.AreEqual(-6, rules.WrongDelta);
        }

        [Test]
        public void With_ReturnsNewInstanceAndKeepsOriginal()
        {
            var original = ScoreRules.Default;

            var changed = original.WithCorrectPoints(30).WithWrongPoints(-10);

            Assert.AreEqual(30, changed.CorrectPoints);
            Assert.AreEqual(-10, changed.WrongPoints);
            Assert.AreEqual(10, original.CorrectPoints, "元のインスタンスは変わらない。");
            Assert.AreEqual(0, original.WrongPoints);
        }

        [Test]
        public void NonePenalty_ChangesNothingOnWrongAnswer()
        {
            var rules = ScoreRules.Default.WithPenalty(PenaltyKind.None, -5);

            Assert.AreEqual(0, rules.WrongDelta, "ペナルティなしでは減点しない。");
            Assert.IsFalse(rules.AppliesSkipNext, "次問休みにもしない。");
        }

        [Test]
        public void Constructor_RejectsOutOfRangeValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScoreRules(correctPoints: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScoreRules(correctPoints: 101));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScoreRules(wrongPoints: -101));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScoreRules(wrongPoints: 101));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScoreRules(penaltyPoints: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScoreRules(penaltyPoints: -101));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScoreRules(penaltyKind: (PenaltyKind)99));

            // skipNext / minusPoints / none はいずれも受け付ける（docs/room-settings.md §1）。
            Assert.DoesNotThrow(() => new ScoreRules(penaltyKind: PenaltyKind.SkipNext));
            Assert.DoesNotThrow(() => new ScoreRules(penaltyKind: PenaltyKind.MinusPoints));
            Assert.DoesNotThrow(() => new ScoreRules(penaltyKind: PenaltyKind.None));
        }

        // --- ScoreBoard（不変更新） ---

        [Test]
        public void WithCorrect_ReturnsNewBoardAndKeepsOriginal()
        {
            var board = ScoreBoard.Empty;

            var next = board.WithCorrect(Client1);

            Assert.AreEqual(10, next.GetScore(Client1));
            Assert.AreEqual(0, board.GetScore(Client1), "元の得点表は変わらない。");
            Assert.AreEqual(0, board.Count);
            Assert.AreEqual(1, next.Count);
            Assert.AreNotSame(board, next);
        }

        [Test]
        public void WithCorrect_Accumulates()
        {
            var board = ScoreBoard.Empty.WithCorrect(Client1).WithCorrect(Client1);

            Assert.AreEqual(20, board.GetScore(Client1));
        }

        [Test]
        public void WithWrong_WithDefaultRules_KeepsScoreButAddsRow()
        {
            var board = ScoreBoard.Empty.WithWrong(Client1);

            Assert.AreEqual(0, board.GetScore(Client1));
            Assert.IsTrue(board.Contains(Client1), "得点 0 でも回答した事実を残す。");
        }

        [Test]
        public void WithWrong_WithMinusPoints_CanGoNegative()
        {
            var rules = ScoreRules.Default.WithPenalty(PenaltyKind.MinusPoints, -5);
            var board = new ScoreBoard(rules).WithWrong(Client1).WithWrong(Client1);

            Assert.AreEqual(-10, board.GetScore(Client1), "減点で得点は負になりうる。");
        }

        [Test]
        public void ClientIds_AreSortedAscending()
        {
            var board = ScoreBoard.Empty.WithCorrect(Client2).WithCorrect(Client1);

            CollectionAssert.AreEqual(new ulong[] { Client1, Client2 }, board.ClientIds);
        }

        [Test]
        public void WithRules_KeepsScores()
        {
            var board = ScoreBoard.Empty.WithCorrect(Client1);
            var rules = ScoreRules.Default.WithCorrectPoints(50);

            var next = board.WithRules(rules);

            Assert.AreEqual(10, next.GetScore(Client1), "得点は保持する。");
            Assert.AreEqual(60, next.WithCorrect(Client1).GetScore(Client1), "以後の加点は新しい規則で計算する。");
            Assert.AreEqual(10, board.Rules.CorrectPoints, "元の得点表の規則は変わらない。");
        }

        [Test]
        public void WithRules_WithNull_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ScoreBoard.Empty.WithRules(null));
        }

        [Test]
        public void Cleared_EmptiesScoresButKeepsRules()
        {
            var rules = ScoreRules.Default.WithCorrectPoints(7);
            var board = new ScoreBoard(rules).WithCorrect(Client1);

            var cleared = board.Cleared();

            Assert.AreEqual(0, cleared.Count);
            Assert.AreEqual(7, cleared.Rules.CorrectPoints);
            Assert.AreEqual(7, board.GetScore(Client1), "元の得点表は変わらない。");
        }

        // --- 再接続の引き継ぎ（#84） ---

        [Test]
        public void WithClientIdChanged_MovesTheRowToTheNewClientId()
        {
            var board = ScoreBoard.Empty.WithCorrect(Client1).WithCorrect(Client2);

            var moved = board.WithClientIdChanged(Client1, 41UL);

            Assert.AreEqual(10, moved.GetScore(41UL), "得点は新しいクライアント ID へ移る。");
            Assert.AreEqual(0, moved.GetScore(Client1), "古い行は残らない。");
            Assert.AreEqual(10, moved.GetScore(Client2), "他の人の得点は動かない。");
            Assert.AreEqual(2, moved.Count);
            Assert.AreEqual(10, board.GetScore(Client1), "元のインスタンスは変わらない（不変）。");
        }

        [Test]
        public void WithClientIdChanged_WithUnknownClient_ReturnsSameInstance()
        {
            var board = ScoreBoard.Empty.WithCorrect(Client1);

            Assert.AreSame(board, board.WithClientIdChanged(Client2, 41UL), "移す行が無ければ作り直さない。");
            Assert.AreSame(board, board.WithClientIdChanged(Client1, Client1), "同じ ID なら何もしない。");
        }

        [Test]
        public void WithClientIdChanged_WhenTargetAlreadyHasARow_OverwritesItWithTheSeatScore()
        {
            // 通常は起こらない（復帰直後のクライアントは得点を持たない）が、起きたときは
            // 「席が持っていた得点」を権威とする（レビュー L-5）。
            var board = ScoreBoard.Empty.WithDelta(Client1, 10).WithDelta(Client2, 3);

            var moved = board.WithClientIdChanged(Client1, Client2);

            Assert.AreEqual(10, moved.GetScore(Client2), "席の得点で上書きする。");
            Assert.AreEqual(0, moved.GetScore(Client1));
            Assert.AreEqual(1, moved.Count, "行は増えない。");
        }

        [Test]
        public void WithClientIdChanged_KeepsClientIdsSorted()
        {
            var board = ScoreBoard.Empty.WithCorrect(41UL).WithCorrect(Client1);

            var moved = board.WithClientIdChanged(41UL, 2UL);

            CollectionAssert.AreEqual(new[] { 1UL, 2UL }, moved.ClientIds, "ClientIds は常に昇順。");
        }
    }
}
