using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;

namespace TsumugiQuiz.Tests.EditMode.Network
{
    /// <summary>
    /// 起動時のコマンドライン引数からネットワーク設定を決める処理のテスト（docs/network.md §10.3）。
    /// </summary>
    public class NetworkRuntimeOptionsTests
    {
        [Test]
        public void FromCommandLine_WithoutArguments_UsesDefaultPort()
        {
            var options = NetworkRuntimeOptions.FromCommandLine(new[] { "TsumugiQuiz.exe" });

            Assert.AreEqual(NetworkConstants.DefaultPort, options.Port);
            Assert.AreEqual(string.Empty, options.PlayerName);
        }

        [Test]
        public void FromCommandLine_ReadsPortArgument()
        {
            var options = NetworkRuntimeOptions.FromCommandLine(new[] { "TsumugiQuiz.exe", "-tq-port", "7801" });

            Assert.AreEqual(7801, options.Port);
        }

        [Test]
        public void FromCommandLine_ReadsPortAlias()
        {
            var options = NetworkRuntimeOptions.FromCommandLine(new[] { "TsumugiQuiz.exe", "-port", "7802" });

            Assert.AreEqual(7802, options.Port);
        }

        [Test]
        public void FromCommandLine_ReadsEqualsSeparatedPort()
        {
            var options = NetworkRuntimeOptions.FromCommandLine(new[] { "TsumugiQuiz.exe", "-tq-port=7803" });

            Assert.AreEqual(7803, options.Port);
        }

        [Test]
        public void FromCommandLine_PrefersPrefixedArgumentOverAlias()
        {
            var options = NetworkRuntimeOptions.FromCommandLine(
                new[] { "TsumugiQuiz.exe", "-port", "7000", "-tq-port", "8000" });

            Assert.AreEqual(8000, options.Port);
        }

        [TestCase("abc")]
        [TestCase("0")]
        [TestCase("70000")]
        [TestCase("-1")]
        public void FromCommandLine_FallsBackToDefaultPortForInvalidValue(string rawPort)
        {
            var options = NetworkRuntimeOptions.FromCommandLine(new[] { "TsumugiQuiz.exe", "-tq-port", rawPort });

            Assert.AreEqual(NetworkConstants.DefaultPort, options.Port);
        }

        [Test]
        public void FromCommandLine_ReadsAndNormalizesPlayerName()
        {
            var options = NetworkRuntimeOptions.FromCommandLine(
                new[] { "TsumugiQuiz.exe", NetworkRuntimeOptions.PlayerNameArgument, "  つむぎ  " });

            Assert.AreEqual("つむぎ", options.PlayerName);
        }

        [Test]
        public void FromCommandLine_IgnoresInvalidPlayerName()
        {
            var tooLong = new string('a', ProtocolConstants.MaxPlayerNameLength + 1);

            var options = NetworkRuntimeOptions.FromCommandLine(
                new[] { "TsumugiQuiz.exe", NetworkRuntimeOptions.PlayerNameArgument, tooLong });

            Assert.AreEqual(string.Empty, options.PlayerName);
        }

        [Test]
        public void FromCommandLine_WithNullOptions_ReturnsDefault()
        {
            var options = NetworkRuntimeOptions.FromCommandLine((TsumugiQuiz.Core.CommandLineOptions)null);

            Assert.AreEqual(NetworkConstants.DefaultPort, options.Port);
        }

        [Test]
        public void NetworkConstants_MatchOperationalDecisions()
        {
            // docs/tasks/setup-brief.md K14 / docs/network.md §2.1・§8.3 の確定値。
            Assert.AreEqual(7777, NetworkConstants.DefaultPort);
            Assert.AreEqual(10, NetworkConstants.PortRetryCount);
            Assert.AreEqual(32768, NetworkConstants.MaxPayloadSizeBytes);
            Assert.AreEqual(16384, NetworkConstants.ImageChunkBytes);
            Assert.AreEqual("0.0.0.0", NetworkConstants.AnyAddress);
            Assert.AreEqual(ProtocolConstants.Version, NetworkConstants.ProtocolVersion);
        }
    }
}
