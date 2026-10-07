using System;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.Tests.EditMode.Room
{
    /// <summary>
    /// <see cref="ScoringSettings"/>（ルーム設定の得点・ペナルティ部分）のテスト
    /// （docs/room-settings.md §1「得点」「早押し詳細」、仮決め K19、#18）。
    /// </summary>
    public class ScoringSettingsTests
    {
        [Test]
        public void Default_MatchesRoomSettingsDocument()
        {
            var settings = ScoringSettings.Default;

            Assert.AreEqual(10, settings.CorrectPoints);
            Assert.AreEqual(0, settings.IncorrectPoints);
            Assert.AreEqual(PenaltyKind.SkipNext, settings.PenaltyType);
            Assert.AreEqual(-5, settings.PenaltyMinusPoints);
            Assert.IsTrue(settings.ReopenAfterWrongAnswer);
            Assert.IsTrue(settings.SingleAttemptOnly);
        }

        [Test]
        public void ToQuizRules_CarriesEveryValue()
        {
            var settings = new ScoringSettings(
                correctPoints: 20,
                incorrectPoints: -1,
                penaltyType: PenaltyKind.MinusPoints,
                penaltyMinusPoints: -10,
                reopenAfterWrongAnswer: false,
                singleAttemptOnly: false);

            var rules = settings.ToQuizRules();

            Assert.AreEqual(20, rules.Score.CorrectPoints);
            Assert.AreEqual(-1, rules.Score.WrongPoints);
            Assert.AreEqual(PenaltyKind.MinusPoints, rules.Score.PenaltyKind);
            Assert.AreEqual(-10, rules.Score.PenaltyPoints);
            Assert.AreEqual(-11, rules.Score.WrongDelta, "誤答の得点変化と減点は足し合わせる。");
            Assert.IsFalse(rules.ReopenAfterWrongAnswer);
            Assert.IsFalse(rules.SingleAttemptOnly);
            Assert.AreSame(settings.ToScoreRules(), rules.Score, "同じ得点規則を使い回す。");
        }

        [Test]
        public void With_ReturnsNewInstanceAndKeepsOriginal()
        {
            var original = ScoringSettings.Default;

            var changed = original
                .WithPenalty(PenaltyKind.MinusPoints, -20)
                .WithPoints(30, -5)
                .WithReopenAfterWrongAnswer(false)
                .WithSingleAttemptOnly(false);

            Assert.AreEqual(PenaltyKind.MinusPoints, changed.PenaltyType);
            Assert.AreEqual(-20, changed.PenaltyMinusPoints);
            Assert.AreEqual(30, changed.CorrectPoints);
            Assert.AreEqual(-5, changed.IncorrectPoints);
            Assert.IsFalse(changed.ReopenAfterWrongAnswer);
            Assert.IsFalse(changed.SingleAttemptOnly);

            Assert.AreEqual(PenaltyKind.SkipNext, original.PenaltyType, "元のインスタンスは変わらない。");
            Assert.AreEqual(10, original.CorrectPoints);
            Assert.IsTrue(original.ReopenAfterWrongAnswer);
            Assert.IsTrue(original.SingleAttemptOnly);
        }

        [Test]
        public void Constructor_RejectsOutOfRangeValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScoringSettings(correctPoints: 101));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScoringSettings(incorrectPoints: -101));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScoringSettings(penaltyMinusPoints: 1));
        }

        /// <summary>
        /// docs/room-settings.md §3 の組み込みプリセット「早押し重視」の得点部分が表現できること。
        /// </summary>
        [Test]
        public void BuzzFocusedPresetValues_AreExpressible()
        {
            var settings = ScoringSettings.Default.WithPenalty(PenaltyKind.MinusPoints, -5);

            Assert.AreEqual(-5, settings.ToScoreRules().WrongDelta);
            Assert.IsFalse(settings.ToScoreRules().AppliesSkipNext);
        }

        /// <summary>
        /// docs/room-settings.md §3 の組み込みプリセット「のんびり」（ペナルティなし）が表現できること。
        /// </summary>
        [Test]
        public void RelaxedPresetValues_AreExpressible()
        {
            var settings = ScoringSettings.Default.WithPenalty(PenaltyKind.None, -5);

            Assert.AreEqual(PenaltyKind.None, settings.PenaltyType);
            Assert.AreEqual(0, settings.ToScoreRules().WrongDelta, "誤答ペナルティなし。");
            Assert.IsFalse(settings.ToScoreRules().AppliesSkipNext);
            Assert.IsTrue(settings.ReopenAfterWrongAnswer);
        }
    }
}
