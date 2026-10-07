using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="AnswerMatcher"/> の正誤判定（正規化後の完全一致・複数正解対応）を検証する。
    /// </summary>
    public class AnswerMatcherTests
    {
        [Test]
        public void IsCorrect_MatchesAnyOfMultipleAnswers()
        {
            var answers = new[] { "とうきょう", "tokyo" };

            Assert.That(AnswerMatcher.IsCorrect("トウキョウ", answers), Is.True);
            Assert.That(AnswerMatcher.IsCorrect(" Tokyo ", answers), Is.True);
            Assert.That(AnswerMatcher.IsCorrect("ｔｏｋｙｏ", answers), Is.True);
        }

        [Test]
        public void IsCorrect_NoAnswerMatches_ReturnsFalse()
        {
            var answers = new[] { "とうきょう", "tokyo" };

            Assert.That(AnswerMatcher.IsCorrect("おおさか", answers), Is.False);
        }

        [Test]
        public void IsCorrect_NullInput_ReturnsFalseWithoutThrowing()
        {
            var answers = new[] { "とうきょう" };

            Assert.That(AnswerMatcher.IsCorrect(null, answers), Is.False);
        }

        [Test]
        public void IsCorrect_EmptyInput_ReturnsFalse()
        {
            var answers = new[] { "とうきょう" };

            Assert.That(AnswerMatcher.IsCorrect(string.Empty, answers), Is.False);
        }

        [Test]
        public void IsCorrect_NullAnswers_ReturnsFalseWithoutThrowing()
        {
            Assert.That(AnswerMatcher.IsCorrect("とうきょう", null), Is.False);
        }

        [Test]
        public void IsCorrect_EmptyAnswers_ReturnsFalse()
        {
            Assert.That(AnswerMatcher.IsCorrect("とうきょう", new string[0]), Is.False);
        }

        [Test]
        public void IsCorrect_AnswersContainingNullOrEmpty_AreSkippedWithoutThrowing()
        {
            var answers = new[] { null, string.Empty, "とうきょう" };

            Assert.That(AnswerMatcher.IsCorrect("トウキョウ", answers), Is.True);
        }

        // 不一致例: 「がっこう」と「がこう」、「は」と「ば」は不一致として判定される。
        [Test]
        public void IsCorrect_SmallTsuMismatch_ReturnsFalse()
        {
            var answers = new[] { "がっこう" };

            Assert.That(AnswerMatcher.IsCorrect("がこう", answers), Is.False);
        }

        [Test]
        public void IsCorrect_DakutenMismatch_ReturnsFalse()
        {
            var answers = new[] { "は" };

            Assert.That(AnswerMatcher.IsCorrect("ば", answers), Is.False);
        }
    }
}
