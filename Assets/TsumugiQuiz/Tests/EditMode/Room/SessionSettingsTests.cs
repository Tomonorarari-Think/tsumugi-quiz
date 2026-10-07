using System;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.Tests.EditMode.Room
{
    /// <summary>
    /// <see cref="QuestionSelectionSettings"/> / <see cref="SessionSettings"/>（#19）のテスト。
    /// 既定値は docs/room-settings.md §1「問題選択」「制限時間」と §2（<c>network.allowLateJoin</c>）。
    /// </summary>
    public class SessionSettingsTests
    {
        [Test]
        public void QuestionSelectionSettings_Default_MatchesDocumentedValues()
        {
            var settings = QuestionSelectionSettings.Default;

            CollectionAssert.IsEmpty(settings.SetIds, "questions.setIds の既定は空（全セット）。");
            Assert.AreEqual(QuestionTypeFilter.Both, settings.TypeFilter);
            Assert.IsFalse(settings.ImageOnly);
            CollectionAssert.IsEmpty(settings.TagFilter);
            Assert.AreEqual(10, settings.Count, "questions.count の既定は 10。");
            Assert.IsTrue(settings.ShuffleOrder, "questions.shuffleOrder の既定は true。");
            Assert.IsFalse(settings.IsAllQuestions);
        }

        [Test]
        public void QuestionSelectionSettings_CountZero_MeansAllQuestions()
        {
            var settings = QuestionSelectionSettings.Default.WithCount(QuestionSelectionSettings.AllQuestions);

            Assert.IsTrue(settings.IsAllQuestions);
        }

        [Test]
        public void QuestionSelectionSettings_NegativeCount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new QuestionSelectionSettings(count: -1));
        }

        [Test]
        public void QuestionSelectionSettings_UndefinedTypeFilter_Throws()
        {
            Assert.Throws<ArgumentException>(() => new QuestionSelectionSettings(typeFilter: (QuestionTypeFilter)42));
        }

        [Test]
        public void QuestionSelectionSettings_NormalizesSetIdsAndTags()
        {
            var settings = new QuestionSelectionSettings(
                setIds: new[] { " set-a ", "set-a", string.Empty, null, "set-b" },
                tagFilter: new[] { "地理", "  ", "地理", " 歴史" });

            CollectionAssert.AreEqual(new[] { "set-a", "set-b" }, settings.SetIds, "空白除去・重複除去されるはず。");
            CollectionAssert.AreEqual(new[] { "地理", "歴史" }, settings.TagFilter);
        }

        [Test]
        public void QuestionSelectionSettings_With_ReturnsNewInstanceAndKeepsOthers()
        {
            var original = new QuestionSelectionSettings(
                setIds: new[] { "set-a" },
                typeFilter: QuestionTypeFilter.FreeText,
                imageOnly: true,
                tagFilter: new[] { "地理" },
                count: 5,
                shuffleOrder: false);

            var changed = original.WithCount(7);

            Assert.AreNotSame(original, changed, "不変オブジェクトなので新しいインスタンスを返す。");
            Assert.AreEqual(5, original.Count, "元のインスタンスは変わらない。");
            Assert.AreEqual(7, changed.Count);
            CollectionAssert.AreEqual(original.SetIds, changed.SetIds);
            Assert.AreEqual(QuestionTypeFilter.FreeText, changed.TypeFilter);
            Assert.IsTrue(changed.ImageOnly);
            CollectionAssert.AreEqual(original.TagFilter, changed.TagFilter);
            Assert.IsFalse(changed.ShuffleOrder);
        }

        [Test]
        public void SessionSettings_Default_MatchesDocumentedValues()
        {
            var settings = SessionSettings.Default;

            Assert.AreSame(QuestionSelectionSettings.Default, settings.Questions);
            Assert.AreSame(QuizTimeLimits.Default, settings.TimeLimits);
            Assert.AreSame(ScoringSettings.Default, settings.Scoring);
            Assert.AreEqual(5.0, settings.ResultAutoAdvanceSec, 1e-9, "result.autoAdvanceSec の既定は 5 秒（仮決め）。");
            Assert.IsTrue(settings.AutoAdvancesAfterResult);
            Assert.IsFalse(settings.AllowLateJoin, "network.allowLateJoin の既定は false。");
        }

        [Test]
        public void SessionSettings_ManualAdvance_DoesNotAutoAdvance()
        {
            var settings = SessionSettings.Default.WithResultAutoAdvanceSec(SessionSettings.ManualAdvance);

            Assert.IsFalse(settings.AutoAdvancesAfterResult, "0 は「司会の『次へ』待ち」。");
        }

        [Test]
        public void SessionSettings_OutOfRangeAutoAdvance_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SessionSettings(resultAutoAdvanceSec: -1.0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SessionSettings(resultAutoAdvanceSec: SessionSettings.MaxResultAutoAdvanceSec + 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SessionSettings(resultAutoAdvanceSec: double.NaN));
        }

        [Test]
        public void SessionSettings_With_KeepsOtherValues()
        {
            var limits = new QuizTimeLimits(6.0, 8.0, 0.15);
            var scoring = ScoringSettings.Default.WithPoints(20, -5);
            var questions = QuestionSelectionSettings.Default.WithCount(3);
            var original = new SessionSettings(questions, limits, scoring, 2.0, allowLateJoin: true);

            var changed = original.WithAllowLateJoin(false);

            Assert.IsTrue(original.AllowLateJoin);
            Assert.IsFalse(changed.AllowLateJoin);
            Assert.AreSame(questions, changed.Questions);
            Assert.AreSame(limits, changed.TimeLimits);
            Assert.AreSame(scoring, changed.Scoring);
            Assert.AreEqual(2.0, changed.ResultAutoAdvanceSec, 1e-9);
        }

        [Test]
        public void SessionSettings_NullArguments_FallBackToDefaults()
        {
            var settings = new SessionSettings(null, null, null);

            Assert.AreSame(QuestionSelectionSettings.Default, settings.Questions);
            Assert.AreSame(QuizTimeLimits.Default, settings.TimeLimits);
            Assert.AreSame(ScoringSettings.Default, settings.Scoring);
        }
    }
}
