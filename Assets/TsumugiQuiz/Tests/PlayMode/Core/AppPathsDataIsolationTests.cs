using System;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.UI;

namespace TsumugiQuiz.Tests.PlayMode.Core
{
    /// <summary>
    /// <c>scripts/verify.ps1</c> が worktree ごとに設定する環境変数
    /// <see cref="AppPaths.DataRootEnvironmentVariable"/>（<c>TSUMUGI_DATA_ROOT</c>、#71）が、
    /// 実際に consent.json の保存先へ反映されることを検証する PlayMode テスト。
    ///
    /// <see cref="Environment.SetEnvironmentVariable(string, string)"/> はプロセス全体に影響するため、
    /// 他のテストに波及しないよう SetUp / TearDown で確実に退避・復元する。
    /// </summary>
    public class AppPathsDataIsolationTests
    {
        private string _tempDataRoot;
        private string _originalEnvironmentValue;

        [SetUp]
        public void SetUp()
        {
            _tempDataRoot = Path.Combine(
                Path.GetTempPath(), "TsumugiQuizDataRootTests_" + Guid.NewGuid().ToString("N"));
            _originalEnvironmentValue = Environment.GetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable);
            Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, _tempDataRoot);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, _originalEnvironmentValue);

            if (Directory.Exists(_tempDataRoot))
            {
                Directory.Delete(_tempDataRoot, recursive: true);
            }
        }

        [Test]
        public void JsonConsentStorage_DefaultPath_UsesEnvironmentVariableDataRoot()
        {
            var path = JsonConsentStorage.GetDefaultFilePath();

            Assert.IsTrue(
                path.StartsWith(_tempDataRoot),
                $"consent.json のパスが環境変数のデータルート配下ではありません: {path}");
            Assert.IsTrue(path.EndsWith("consent.json"));
        }

        [Test]
        public void ConsentStore_RecordConsent_WritesConsentJsonUnderEnvironmentVariableDataRoot()
        {
            var store = new ConsentStore(new JsonConsentStorage());
            var terms = new[] { new TermsDefinition("terms-a", "hash-a") };

            store.RecordConsent(terms, "0.1.0-test", DateTime.UtcNow);

            var expectedPath = Path.Combine(_tempDataRoot, "consent.json");
            Assert.IsTrue(
                File.Exists(expectedPath),
                $"consent.json が環境変数のデータルート配下に書かれていません: {expectedPath}");
        }
    }
}
