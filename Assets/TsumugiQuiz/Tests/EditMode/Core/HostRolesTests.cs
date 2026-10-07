using NUnit.Framework;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="HostRoles"/> の変換（docs/room-settings.md §1 の <c>host.role</c>）の検証。
    /// </summary>
    public class HostRolesTests
    {
        [Test]
        public void Keys_MatchRoomSettingsDocument()
        {
            Assert.AreEqual("player", HostRoles.PlayerKey);
            Assert.AreEqual("moderator", HostRoles.ModeratorKey);
        }

        [TestCase("player", HostRole.Player)]
        [TestCase("moderator", HostRole.Moderator)]
        public void Parse_KnownKeys_ReturnsRole(string key, HostRole expected)
        {
            Assert.AreEqual(expected, HostRoles.Parse(key));
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("Moderator")]
        [TestCase("観客")]
        public void Parse_UnknownValue_FallsBackToPlayer(string key)
        {
            // docs/room-settings.md §5: 範囲外の値は既定値にクランプする。
            Assert.AreEqual(HostRole.Player, HostRoles.Parse(key));
        }

        [Test]
        public void ToKey_RoundTrips()
        {
            Assert.AreEqual(HostRole.Player, HostRoles.Parse(HostRoles.ToKey(HostRole.Player)));
            Assert.AreEqual(HostRole.Moderator, HostRoles.Parse(HostRoles.ToKey(HostRole.Moderator)));
        }

        [Test]
        public void ToDisplayName_IsNotEmpty()
        {
            Assert.IsNotEmpty(HostRoles.ToDisplayName(HostRole.Player));
            Assert.IsNotEmpty(HostRoles.ToDisplayName(HostRole.Moderator));
        }
    }
}
