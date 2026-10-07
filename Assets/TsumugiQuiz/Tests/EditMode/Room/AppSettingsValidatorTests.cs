using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.Tests.EditMode.Room
{
    /// <summary>
    /// <see cref="AppSettingsValidator"/> のうち、PR #92 Phase 2 で追加した
    /// 「未対応キー（<c>network.tickRate</c>）の扱い」のテスト。
    /// </summary>
    public class AppSettingsValidatorTests
    {
        [Test]
        public void Validate_NetworkTickRateSpecified_WarnsAndIgnores()
        {
            var result = AppSettingsValidator.Validate(new AppSettingsInput { NetworkTickRate = 60 });

            Assert.IsTrue(
                result.Warnings.Any(warning => warning.Contains("network.tickRate")),
                "network.tickRate を指定したら「未対応なので無視した」旨の警告が出ること。");
        }

        [Test]
        public void Validate_NetworkTickRateOmitted_HasNoTickRateWarning()
        {
            var result = AppSettingsValidator.Validate(new AppSettingsInput());

            Assert.IsFalse(
                result.Warnings.Any(warning => warning.Contains("network.tickRate")),
                "指定が無ければ network.tickRate の警告は出ないこと。");
        }

        /// <summary>issue #155 M-4: 大文字小文字違い（Ordinal 比較で不一致）は不正値として既定値にクランプする。</summary>
        [TestCase("Moderator")]
        [TestCase("admin")]
        public void Validate_InvalidHostRole_WarnsAndFallsBackToDefault(string invalidHostRole)
        {
            var result = AppSettingsValidator.Validate(new AppSettingsInput { HostRole = invalidHostRole });

            Assert.AreEqual(HostRole.Player, result.Settings.HostRole);
            Assert.IsTrue(
                result.Warnings.Any(warning => warning.Contains("host.role")),
                "不正な host.role の値は警告付きで既定値にクランプされること。");
        }

        [Test]
        public void Validate_HostRoleOmitted_HasNoHostRoleWarning()
        {
            var result = AppSettingsValidator.Validate(new AppSettingsInput());

            Assert.IsFalse(
                result.Warnings.Any(warning => warning.Contains("host.role")),
                "指定が無ければ既定値（player）として扱い、警告は出ないこと。");
        }

        [Test]
        public void Validate_HostRoleModerator_IsAccepted()
        {
            var result = AppSettingsValidator.Validate(new AppSettingsInput { HostRole = "moderator" });

            Assert.AreEqual(HostRole.Moderator, result.Settings.HostRole);
            Assert.IsFalse(result.Warnings.Any(warning => warning.Contains("host.role")));
        }
    }
}
