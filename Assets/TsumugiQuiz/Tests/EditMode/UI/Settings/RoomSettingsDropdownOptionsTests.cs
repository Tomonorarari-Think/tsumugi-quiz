using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.UI.Views.Settings;

namespace TsumugiQuiz.Tests.EditMode.UI.Settings
{
    /// <summary>
    /// <see cref="RoomSettingsDropdownOptions"/>（issue #28）の値⇔ラベル変換の往復・フォールバックを検証する。
    /// </summary>
    public class RoomSettingsDropdownOptionsTests
    {
        [Test]
        public void HostRole_LabelFor_And_ValueFor_RoundTrip()
        {
            var playerLabel = RoomSettingsDropdownOptions.LabelFor(RoomSettingsDropdownOptions.HostRole, HostRoles.PlayerKey);
            var moderatorLabel = RoomSettingsDropdownOptions.LabelFor(RoomSettingsDropdownOptions.HostRole, HostRoles.ModeratorKey);

            Assert.AreEqual(HostRoles.PlayerKey, RoomSettingsDropdownOptions.ValueFor(RoomSettingsDropdownOptions.HostRole, playerLabel));
            Assert.AreEqual(HostRoles.ModeratorKey, RoomSettingsDropdownOptions.ValueFor(RoomSettingsDropdownOptions.HostRole, moderatorLabel));
            Assert.AreNotEqual(playerLabel, moderatorLabel);
        }

        [Test]
        public void QuestionsTypeFilter_AllValues_RoundTrip()
        {
            foreach (var expected in new[] { "both", "freeText", "choice" })
            {
                var label = RoomSettingsDropdownOptions.LabelFor(RoomSettingsDropdownOptions.QuestionsTypeFilter, expected);
                var actual = RoomSettingsDropdownOptions.ValueFor(RoomSettingsDropdownOptions.QuestionsTypeFilter, label);
                Assert.AreEqual(expected, actual);
            }
        }

        [Test]
        public void ScorePenaltyType_AllValues_RoundTrip()
        {
            foreach (var expected in new[] { "skipNext", "minusPoints", "none" })
            {
                var label = RoomSettingsDropdownOptions.LabelFor(RoomSettingsDropdownOptions.ScorePenaltyType, expected);
                var actual = RoomSettingsDropdownOptions.ValueFor(RoomSettingsDropdownOptions.ScorePenaltyType, label);
                Assert.AreEqual(expected, actual);
            }
        }

        [Test]
        public void LabelFor_UnknownValue_FallsBackToFirstOption()
        {
            var label = RoomSettingsDropdownOptions.LabelFor(RoomSettingsDropdownOptions.ScorePenaltyType, "unknown-value");
            Assert.AreEqual(RoomSettingsDropdownOptions.ScorePenaltyType[0].Label, label);
        }

        [Test]
        public void ValueFor_UnknownLabel_FallsBackToFirstOption()
        {
            var value = RoomSettingsDropdownOptions.ValueFor(RoomSettingsDropdownOptions.QuestionsTypeFilter, "unknown-label");
            Assert.AreEqual(RoomSettingsDropdownOptions.QuestionsTypeFilter[0].Value, value);
        }

        [Test]
        public void LabelsOf_ReturnsLabelsInOrder()
        {
            var labels = RoomSettingsDropdownOptions.LabelsOf(RoomSettingsDropdownOptions.HostRole);

            Assert.AreEqual(2, labels.Count);
            Assert.AreEqual(RoomSettingsDropdownOptions.HostRole[0].Label, labels[0]);
            Assert.AreEqual(RoomSettingsDropdownOptions.HostRole[1].Label, labels[1]);
        }
    }
}
