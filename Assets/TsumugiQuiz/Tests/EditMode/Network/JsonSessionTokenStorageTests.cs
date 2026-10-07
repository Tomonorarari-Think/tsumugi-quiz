using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.Network
{
    /// <summary>
    /// <see cref="JsonSessionTokenStorage"/> の読み書きと、壊れた / 手編集された JSON の扱いを検証する
    /// （issue #69）。実際のデータルート（<c>AppPaths.DataRoot</c>）は使わず一時ファイルで検証する。
    /// </summary>
    public class JsonSessionTokenStorageTests
    {
        private const string HostKey = "192.168.0.2:7777";

        private string _tempDirectory;
        private string _filePath;

        private static SessionToken CreateToken()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                return SessionToken.CreateRandom(rng);
            }
        }

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "TsumugiQuizTokenTests_" + Guid.NewGuid().ToString("N"));
            _filePath = Path.Combine(_tempDirectory, "nested", JsonSessionTokenStorage.FileName);
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
        public void Constructor_RejectsEmptyPath()
        {
            Assert.Throws<ArgumentException>(() => new JsonSessionTokenStorage(string.Empty));
            Assert.Throws<ArgumentException>(() => new JsonSessionTokenStorage(null));
        }

        [Test]
        public void GetDefaultFilePath_IsUnderAppPathsDataRoot()
        {
            // AppPaths は静的な状態を持つため、明示設定と後始末（Reset）をこのテスト内で完結させる（#71）。
            var explicitRoot = Path.Combine(Path.GetTempPath(), "TsumugiQuizAppPathsTokenTest_" + Guid.NewGuid().ToString("N"));
            AppPaths.Configure(explicitRoot);
            try
            {
                var path = JsonSessionTokenStorage.GetDefaultFilePath();

                Assert.IsTrue(path.StartsWith(explicitRoot));
                StringAssert.EndsWith(JsonSessionTokenStorage.FileName, path);
            }
            finally
            {
                AppPaths.Reset();
            }
        }

        [Test]
        public void Load_FileDoesNotExist_ReturnsEmpty()
        {
            Assert.AreEqual(0, new JsonSessionTokenStorage(_filePath).Load().Count);
        }

        [Test]
        public void Save_Then_Load_RoundTrips()
        {
            var storage = new JsonSessionTokenStorage(_filePath);
            var token = CreateToken();
            var expiresAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

            storage.Save(new[] { new SessionTokenRecord(HostKey, token, expiresAt) });

            var loaded = storage.Load();
            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual(HostKey, loaded[0].HostKey);
            Assert.AreEqual(token, loaded[0].Token);
            Assert.AreEqual(expiresAt, loaded[0].ExpiresAtUtc);
            Assert.AreEqual(DateTimeKind.Utc, loaded[0].ExpiresAtUtc.Kind);
        }

        [Test]
        public void Save_UsesCamelCaseKeys_AndDoesNotStoreAnythingElse()
        {
            var storage = new JsonSessionTokenStorage(_filePath);
            storage.Save(new[] { new SessionTokenRecord(HostKey, CreateToken(), DateTime.UtcNow.AddHours(1)) });

            var json = File.ReadAllText(_filePath);
            StringAssert.Contains("\"hostKey\"", json);
            StringAssert.Contains("\"token\"", json);
            StringAssert.Contains("\"expiresAtUtc\"", json);
            StringAssert.Contains("\"version\"", json);
        }

        [Test]
        public void Save_SkipsInvalidRecords()
        {
            var storage = new JsonSessionTokenStorage(_filePath);

            storage.Save(new[]
            {
                new SessionTokenRecord("キーが不正", CreateToken(), DateTime.UtcNow.AddHours(1)),
                new SessionTokenRecord(HostKey, SessionToken.None, DateTime.UtcNow.AddHours(1)),
                new SessionTokenRecord(HostKey, CreateToken(), DateTime.UtcNow.AddHours(1)),
            });

            Assert.AreEqual(1, storage.Load().Count);
        }

        [Test]
        public void Save_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new JsonSessionTokenStorage(_filePath).Save(null));
        }

        [Test]
        public void Load_BrokenJson_ReturnsEmptyAndWarns()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? _tempDirectory);
            File.WriteAllText(_filePath, "{ これは JSON ではない");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("JsonSessionTokenStorage"));

            Assert.AreEqual(0, new JsonSessionTokenStorage(_filePath).Load().Count);
        }

        [Test]
        public void Load_SkipsEntriesWithMalformedToken()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? _tempDirectory);
            File.WriteAllText(
                _filePath,
                "{\"version\":1,\"entries\":[" +
                "{\"hostKey\":\"192.168.0.2:7777\",\"token\":\"zzzz\",\"expiresAtUtc\":\"2099-01-01T00:00:00Z\"}," +
                "{\"hostKey\":\"\",\"token\":\"00112233445566778899aabbccddeeff\",\"expiresAtUtc\":\"2099-01-01T00:00:00Z\"}," +
                "{\"hostKey\":\"10.0.0.1:7777\",\"token\":\"00112233445566778899aabbccddeeff\",\"expiresAtUtc\":\"2099-01-01T00:00:00Z\"}" +
                "]}");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("JsonSessionTokenStorage"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("JsonSessionTokenStorage"));

            IReadOnlyList<SessionTokenRecord> loaded = new JsonSessionTokenStorage(_filePath).Load();

            Assert.AreEqual(1, loaded.Count, "形式が正しい 1 件だけを採用する。");
            Assert.AreEqual("10.0.0.1:7777", loaded[0].HostKey);
        }

        [Test]
        public void Save_WhenTheDirectoryCannotBeCreated_ThrowsInsteadOfSwallowing()
        {
            // レビュー M1: 保存失敗を握りつぶすと、呼び出し側が「保存しました」と誤って記録してしまう。
            // 保存先ディレクトリと同じ名前のファイルを置いて、ディレクトリ作成を失敗させる。
            Directory.CreateDirectory(_tempDirectory);
            File.WriteAllText(Path.Combine(_tempDirectory, "nested"), "ディレクトリを作れなくするための障害物");

            var storage = new JsonSessionTokenStorage(_filePath);

            Assert.Throws<IOException>(
                () => storage.Save(new[] { new SessionTokenRecord(HostKey, CreateToken(), DateTime.UtcNow.AddHours(1)) }));
        }

        [Test]
        public void Save_OverwritesExistingFileAtomically()
        {
            var storage = new JsonSessionTokenStorage(_filePath);
            storage.Save(new[] { new SessionTokenRecord(HostKey, CreateToken(), DateTime.UtcNow.AddHours(1)) });

            var second = CreateToken();
            storage.Save(new[] { new SessionTokenRecord(HostKey, second, DateTime.UtcNow.AddHours(2)) });

            var loaded = storage.Load();
            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual(second, loaded[0].Token);
            Assert.IsFalse(File.Exists(_filePath + ".tmp"), "一時ファイルは残らない。");
        }
    }
}
