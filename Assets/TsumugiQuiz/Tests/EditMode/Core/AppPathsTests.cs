using System;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="AppPaths"/> の優先順位（明示設定 > 環境変数 > 既定値）と入力検証を検証する（#71）。
    ///
    /// <see cref="AppPaths"/> は静的な状態を持つため、各テストは自分が使う値を明示的に設定し、
    /// 終了時に必ず <see cref="AppPaths.Reset"/> と環境変数の復元を行う（他のテストの実行順に依存しないため）。
    /// </summary>
    public class AppPathsTests
    {
        private string _originalEnvironmentValue;

        [SetUp]
        public void SetUp()
        {
            // 他のテスト（や実行環境）が既に環境変数を設定しているケースに備え、退避してから空にする。
            _originalEnvironmentValue = Environment.GetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable);
            Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, null);
            AppPaths.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            AppPaths.Reset();
            Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, _originalEnvironmentValue);
        }

        private static string MakeRoot(string suffix)
            => Path.Combine(Path.GetTempPath(), "TsumugiQuizAppPathsTests_" + suffix);

        [Test]
        public void DataRoot_NothingConfigured_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => _ = AppPaths.DataRoot);
        }

        [Test]
        public void DataRoot_DefaultOnly_ReturnsDefault()
        {
            var defaultRoot = MakeRoot("default");

            AppPaths.ConfigureDefault(defaultRoot);

            Assert.AreEqual(defaultRoot, AppPaths.DataRoot);
        }

        [Test]
        public void DataRoot_EnvironmentVariable_OverridesDefault()
        {
            var defaultRoot = MakeRoot("default");
            var envRoot = MakeRoot("env");
            AppPaths.ConfigureDefault(defaultRoot);
            Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, envRoot);

            Assert.AreEqual(envRoot, AppPaths.DataRoot);
        }

        [Test]
        public void DataRoot_Explicit_OverridesEnvironmentVariableAndDefault()
        {
            var defaultRoot = MakeRoot("default");
            var envRoot = MakeRoot("env");
            var explicitRoot = MakeRoot("explicit");
            AppPaths.ConfigureDefault(defaultRoot);
            Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, envRoot);
            AppPaths.Configure(explicitRoot);

            Assert.AreEqual(explicitRoot, AppPaths.DataRoot);
        }

        [Test]
        public void Reset_ClearsExplicitAndDefault()
        {
            AppPaths.Configure(MakeRoot("explicit"));
            AppPaths.ConfigureDefault(MakeRoot("default"));

            AppPaths.Reset();

            Assert.Throws<InvalidOperationException>(() => _ = AppPaths.DataRoot);
        }

        [Test]
        public void Configure_RelativePath_Throws()
        {
            Assert.Throws<ArgumentException>(() => AppPaths.Configure("relative/path"));
        }

        [Test]
        public void Configure_NullOrEmpty_Throws()
        {
            Assert.Throws<ArgumentException>(() => AppPaths.Configure(null));
            Assert.Throws<ArgumentException>(() => AppPaths.Configure(string.Empty));
            Assert.Throws<ArgumentException>(() => AppPaths.Configure("   "));
        }

        [Test]
        public void ConfigureDefault_RelativePath_Throws()
        {
            Assert.Throws<ArgumentException>(() => AppPaths.ConfigureDefault("relative/path"));
        }

        [Test]
        public void DataRoot_EnvironmentVariableRelativePath_Throws()
        {
            AppPaths.ConfigureDefault(MakeRoot("default"));
            Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, "relative/path");

            Assert.Throws<ArgumentException>(() => _ = AppPaths.DataRoot);
        }

        [Test]
        public void DataRoot_EnvironmentVariableWhitespace_FallsBackToDefault()
        {
            var defaultRoot = MakeRoot("default");
            AppPaths.ConfigureDefault(defaultRoot);
            Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, "   ");

            Assert.AreEqual(defaultRoot, AppPaths.DataRoot);
        }

        [Test]
        public void Combine_NoParts_ReturnsDataRoot()
        {
            var root = MakeRoot("combine-empty");
            AppPaths.Configure(root);

            Assert.AreEqual(root, AppPaths.Combine());
        }

        [Test]
        public void Combine_SingleParts_JoinsUnderDataRoot()
        {
            var root = MakeRoot("combine-single");
            AppPaths.Configure(root);

            var combined = AppPaths.Combine("consent.json");

            Assert.AreEqual(Path.Combine(root, "consent.json"), combined);
        }

        [Test]
        public void Combine_MultipleParts_JoinsInOrder()
        {
            var root = MakeRoot("combine-multi");
            AppPaths.Configure(root);

            var combined = AppPaths.Combine("TtsCache", "ab", "abcdef.wav");

            Assert.AreEqual(Path.Combine(root, "TtsCache", "ab", "abcdef.wav"), combined);
        }

        [Test]
        public void Combine_EmptyPart_Throws()
        {
            AppPaths.Configure(MakeRoot("combine-invalid"));

            Assert.Throws<ArgumentException>(() => AppPaths.Combine("valid", string.Empty));
        }

        [Test]
        public void Combine_NullPart_Throws()
        {
            AppPaths.Configure(MakeRoot("combine-null-part"));

            Assert.Throws<ArgumentException>(() => AppPaths.Combine("valid", null));
        }
    }
}
