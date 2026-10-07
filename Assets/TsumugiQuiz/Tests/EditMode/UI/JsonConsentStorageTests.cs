using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.UI;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.UI
{
    /// <summary>
    /// <see cref="JsonConsentStorage"/> の読み書きと、破損 JSON の扱いを検証する。
    /// 実際の Application.persistentDataPath は使わず、一時ファイルへの明示的なパス指定で検証する。
    /// </summary>
    public class JsonConsentStorageTests
    {
        private string _tempDirectory;
        private string _filePath;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "TsumugiQuizConsentTests_" + Guid.NewGuid().ToString("N"));
            _filePath = Path.Combine(_tempDirectory, "nested", "consent.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [Test]
        public void Load_FileDoesNotExist_ReturnsEmpty()
        {
            var storage = new JsonConsentStorage(_filePath);

            var records = storage.Load();

            Assert.AreEqual(0, records.Count);
        }

        [Test]
        public void Save_CreatesMissingParentDirectory()
        {
            var storage = new JsonConsentStorage(_filePath);

            storage.Save(new[] { new ConsentRecord("terms-a", "hash-a", DateTime.UtcNow, "0.1.0") });

            Assert.IsTrue(File.Exists(_filePath));
        }

        [Test]
        public void Save_ThenLoad_RoundTripsAllFields()
        {
            var storage = new JsonConsentStorage(_filePath);
            var acceptedAt = new DateTime(2026, 9, 13, 8, 30, 0, DateTimeKind.Utc);
            var original = new[]
            {
                new ConsentRecord("terms-a", "hash-a", acceptedAt, "0.1.0"),
                new ConsentRecord("terms-b", "hash-b", acceptedAt, "0.1.0"),
            };

            storage.Save(original);
            var loaded = storage.Load();

            Assert.AreEqual(2, loaded.Count);
            Assert.AreEqual("terms-a", loaded[0].TermsId);
            Assert.AreEqual("hash-a", loaded[0].Sha256Hash);
            Assert.AreEqual(acceptedAt, loaded[0].AcceptedAtUtc);
            Assert.AreEqual("0.1.0", loaded[0].AppVersion);
            Assert.AreEqual("terms-b", loaded[1].TermsId);
        }

        [Test]
        public void Save_EmptyList_ThenLoad_ReturnsEmpty()
        {
            var storage = new JsonConsentStorage(_filePath);
            storage.Save(new[] { new ConsentRecord("terms-a", "hash-a", DateTime.UtcNow, "0.1.0") });

            storage.Save(Array.Empty<ConsentRecord>());

            Assert.AreEqual(0, storage.Load().Count);
        }

        [Test]
        public void Load_CorruptedJson_ReturnsEmptyWithoutThrowing()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, "{ this is not valid json");
            var storage = new JsonConsentStorage(_filePath);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("同意記録の読み込みに失敗"));
            IReadOnlyList<ConsentRecord> records = null;
            Assert.DoesNotThrow(() => records = storage.Load());

            Assert.IsNotNull(records);
            Assert.AreEqual(0, records.Count);
        }

        [Test]
        public void Load_JsonWithMissingRequiredField_SkipsThatRecord()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            // sha256 が欠けているレコードを1件含む JSON。
            File.WriteAllText(_filePath, "[{\"termsId\":\"terms-a\",\"acceptedAtUtc\":\"2026-09-13T00:00:00Z\",\"appVersion\":\"0.1.0\"}]");
            var storage = new JsonConsentStorage(_filePath);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("不正なレコードをスキップ"));
            var records = storage.Load();

            Assert.AreEqual(0, records.Count);
        }

        [Test]
        public void Constructor_NullOrEmptyPath_Throws()
        {
            Assert.Throws<ArgumentException>(() => new JsonConsentStorage(string.Empty));
        }

        [Test]
        public void GetDefaultFilePath_IsUnderAppPathsDataRoot()
        {
            // AppPaths は静的な状態を持つため、明示設定と後始末(Reset)をこのテスト内で完結させる（#71）。
            var explicitRoot = Path.Combine(Path.GetTempPath(), "TsumugiQuizAppPathsTest_" + Guid.NewGuid().ToString("N"));
            AppPaths.Configure(explicitRoot);
            try
            {
                var path = JsonConsentStorage.GetDefaultFilePath();

                Assert.IsTrue(path.StartsWith(explicitRoot));
                Assert.IsTrue(path.EndsWith("consent.json"));
            }
            finally
            {
                AppPaths.Reset();
            }
        }
    }
}
