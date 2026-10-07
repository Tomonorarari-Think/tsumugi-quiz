using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// コマンドライン引数の解析（docs/network.md §10.3）のテスト。
    /// </summary>
    public class CommandLineOptionsTests
    {
        [Test]
        public void Parse_ReadsKeyValuePairs()
        {
            var options = CommandLineOptions.Parse(new[] { "TsumugiQuiz.exe", "-tq-port", "7778" });

            Assert.IsTrue(options.TryGetString(out var value, "-tq-port"));
            Assert.AreEqual("7778", value);
        }

        [Test]
        public void Parse_IgnoresNonOptionTokens()
        {
            var options = CommandLineOptions.Parse(new[] { "TsumugiQuiz.exe", "stray", "-tq-port", "7778" });

            Assert.AreEqual(1, options.Count);
        }

        [Test]
        public void Parse_TreatsKeyFollowedByKeyAsFlag()
        {
            var options = CommandLineOptions.Parse(new[] { "-tq-host", "-tq-port", "7778" });

            Assert.IsTrue(options.Contains("-tq-host"));
            Assert.IsFalse(options.TryGetString(out _, "-tq-host"));
            Assert.IsTrue(options.TryGetUInt16(out var port, "-tq-port"));
            Assert.AreEqual(7778, port);
        }

        [Test]
        public void Parse_ReadsEqualsSeparatedValue()
        {
            var options = CommandLineOptions.Parse(new[] { "TsumugiQuiz.exe", "-tq-port=7778" });

            Assert.IsTrue(options.TryGetUInt16(out var port, "-tq-port"));
            Assert.AreEqual(7778, port);
        }

        [Test]
        public void Parse_EqualsFormKeepsValueContainingHyphen()
        {
            var options = CommandLineOptions.Parse(new[] { "-tq-name=my-name" });

            Assert.IsTrue(options.TryGetString(out var value, "-tq-name"));
            Assert.AreEqual("my-name", value);
        }

        [Test]
        public void Parse_EqualsFormWithEmptyValueIsFlag()
        {
            var options = CommandLineOptions.Parse(new[] { "-tq-port=" });

            Assert.IsTrue(options.Contains("-tq-port"));
            Assert.IsFalse(options.TryGetString(out _, "-tq-port"));
        }

        [Test]
        public void Parse_IsCaseInsensitive()
        {
            var options = CommandLineOptions.Parse(new[] { "-TQ-PORT", "7778" });

            Assert.IsTrue(options.TryGetUInt16(out var port, "-tq-port"));
            Assert.AreEqual(7778, port);
        }

        [Test]
        public void Parse_LastValueWinsForDuplicateKeys()
        {
            var options = CommandLineOptions.Parse(new[] { "-tq-port", "1000", "-tq-port", "2000" });

            Assert.IsTrue(options.TryGetUInt16(out var port, "-tq-port"));
            Assert.AreEqual(2000, port);
        }

        [Test]
        public void Parse_HandlesNullAndEmptyInput()
        {
            Assert.AreEqual(0, CommandLineOptions.Parse(null).Count);
            Assert.AreEqual(0, CommandLineOptions.Parse(new string[0]).Count);
            Assert.AreEqual(0, CommandLineOptions.Empty.Count);
        }

        [Test]
        public void TryGetString_UsesFirstMatchingAlias()
        {
            var options = CommandLineOptions.Parse(new[] { "-port", "7000", "-tq-port", "8000" });

            Assert.IsTrue(options.TryGetString(out var value, "-tq-port", "-port"));
            Assert.AreEqual("8000", value);
        }

        [Test]
        public void TryGetString_FallsBackToAlias()
        {
            var options = CommandLineOptions.Parse(new[] { "-port", "7000" });

            Assert.IsTrue(options.TryGetString(out var value, "-tq-port", "-port"));
            Assert.AreEqual("7000", value);
        }

        [Test]
        public void TryGetUInt16_ReturnsFalseForNonNumericValue()
        {
            var options = CommandLineOptions.Parse(new[] { "-tq-port", "abc" });

            Assert.IsFalse(options.TryGetUInt16(out var port, "-tq-port"));
            Assert.AreEqual(0, port);
        }

        [Test]
        public void TryGetUInt16_ReturnsFalseForOutOfRangeValue()
        {
            var options = CommandLineOptions.Parse(new[] { "-tq-port", "70000" });

            Assert.IsFalse(options.TryGetUInt16(out _, "-tq-port"));
        }

        // issue #8: -tq-host / -tq-join / -tq-name / -tq-data-root / -tq-window の新引数。
        // -tq-host / -tq-join / -tq-name / -tq-data-root は既存の汎用 API（Contains / TryGetString）で
        // そのまま解析できることを確認する。-tq-window だけは "x,y,w,h" の構造化パースが必要なため、
        // 専用の TryGetWindowRect を追加した。

        [Test]
        public void Parse_TqHostIsAFlagWithNoValue()
        {
            var options = CommandLineOptions.Parse(new[] { "-tq-host", "-tq-name", "Host" });

            Assert.IsTrue(options.Contains("-tq-host"));
            Assert.IsFalse(options.TryGetString(out _, "-tq-host"));
        }

        [Test]
        public void Parse_TqJoinReadsCode()
        {
            var options = CommandLineOptions.Parse(new[] { "-tq-join", "60N0-0HE7-K12R" });

            Assert.IsTrue(options.TryGetString(out var code, "-tq-join"));
            Assert.AreEqual("60N0-0HE7-K12R", code);
        }

        [Test]
        public void Parse_TqDataRootReadsPath()
        {
            var options = CommandLineOptions.Parse(new[] { "-tq-data-root", @"C:\tmp\tq-host" });

            Assert.IsTrue(options.TryGetString(out var path, "-tq-data-root"));
            Assert.AreEqual(@"C:\tmp\tq-host", path);
        }

        [Test]
        public void TryGetWindowRect_ValidValue_ReturnsRect()
        {
            var options = CommandLineOptions.Parse(new[] { "-tq-window", "0,0,960,540" });

            Assert.IsTrue(options.TryGetWindowRect(out var rect, "-tq-window"));
            Assert.AreEqual(0, rect.X);
            Assert.AreEqual(0, rect.Y);
            Assert.AreEqual(960, rect.Width);
            Assert.AreEqual(540, rect.Height);
        }

        [Test]
        public void TryGetWindowRect_MissingKey_ReturnsFalse()
        {
            var options = CommandLineOptions.Parse(new[] { "-tq-host" });

            Assert.IsFalse(options.TryGetWindowRect(out _, "-tq-window"));
        }

        [Test]
        public void TryGetWindowRect_MalformedValue_ReturnsFalse()
        {
            var options = CommandLineOptions.Parse(new[] { "-tq-window", "not-a-rect" });

            Assert.IsFalse(options.TryGetWindowRect(out _, "-tq-window"));
        }
    }
}
