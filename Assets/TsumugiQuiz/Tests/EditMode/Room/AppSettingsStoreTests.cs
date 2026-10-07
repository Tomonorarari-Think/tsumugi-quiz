using System;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.Tests.EditMode.Room
{
    /// <summary>
    /// <see cref="AppSettingsStore"/>（#26）のテスト。実ファイルには触れず <see cref="InMemoryRoomFileSystem"/> を使う。
    /// </summary>
    public class AppSettingsStoreTests
    {
        private const string FilePath = @"C:\fake\TsumugiQuiz\app-settings.json";

        [Test]
        public void GetDefaultFilePath_IsUnderAppPathsDataRoot()
        {
            // AppPaths は静的な状態を持つため、明示設定と後始末(Reset)をこのテスト内で完結させる（#71）。
            var explicitRoot = Path.Combine(Path.GetTempPath(), "TsumugiQuizAppPathsTest_" + Guid.NewGuid().ToString("N"));
            AppPaths.Configure(explicitRoot);
            try
            {
                var path = AppSettingsStore.GetDefaultFilePath();

                Assert.IsTrue(path.StartsWith(explicitRoot));
                Assert.IsTrue(path.EndsWith("app-settings.json"));
            }
            finally
            {
                AppPaths.Reset();
            }
        }

        [Test]
        public void Load_MissingFile_ReturnsDefaultsWithoutWarnings()
        {
            var store = new AppSettingsStore(FilePath, new InMemoryRoomFileSystem());

            var result = store.Load();

            Assert.IsFalse(result.HasWarnings);
            Assert.AreEqual(AppSettings.Default.NetworkPort, result.Settings.NetworkPort);
        }

        [Test]
        public void Save_ThenLoad_RoundTrips()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            var store = new AppSettingsStore(FilePath, fileSystem);
            var settings = AppSettings.Create(
                playerName: "つむぎ", networkPort: 8888, characterEnabled: false);

            var saveResult = store.Save(settings);
            var result = store.Load();

            Assert.IsTrue(saveResult.Success);
            Assert.IsFalse(saveResult.Warnings.Count > 0);
            Assert.IsFalse(result.HasWarnings);
            Assert.AreEqual("つむぎ", result.Settings.PlayerName);
            Assert.AreEqual(8888, result.Settings.NetworkPort);
            Assert.IsFalse(result.Settings.CharacterEnabled);
        }

        [Test]
        public void Load_CorruptJson_FallsBackToDefaultsWithWarning()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            fileSystem.EnsureDirectory(@"C:\fake\TsumugiQuiz");
            fileSystem.WriteAllText(FilePath, "{ not valid json ");
            var store = new AppSettingsStore(FilePath, fileSystem);

            var result = store.Load();

            Assert.IsTrue(result.HasWarnings);
            Assert.AreEqual(AppSettings.Default.NetworkPort, result.Settings.NetworkPort);
        }

        [Test]
        public void Load_PartialJson_FillsRemainingWithDefaults()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            fileSystem.WriteAllText(FilePath, "{ \"player.name\": \"部分設定\" }");
            var store = new AppSettingsStore(FilePath, fileSystem);

            var result = store.Load();

            Assert.IsFalse(result.HasWarnings);
            Assert.AreEqual("部分設定", result.Settings.PlayerName);
            Assert.AreEqual(AppSettings.DefaultNetworkPort, result.Settings.NetworkPort);
        }

        [Test]
        public void Load_InvalidPlayerName_FallsBackToEmptyWithWarning()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            fileSystem.WriteAllText(FilePath, "{ \"player.name\": \"12345678901234567\" }"); // 17文字（上限16超え）
            var store = new AppSettingsStore(FilePath, fileSystem);

            var result = store.Load();

            Assert.IsTrue(result.HasWarnings);
            Assert.AreEqual("", result.Settings.PlayerName);
        }

        [Test]
        public void Load_OutOfRangeNetworkPort_ClampsWithWarning()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            fileSystem.WriteAllText(FilePath, "{ \"network.port\": 999999 }");
            var store = new AppSettingsStore(FilePath, fileSystem);

            var result = store.Load();

            Assert.IsTrue(result.HasWarnings);
            Assert.AreEqual(AppSettings.MaxNetworkPort, result.Settings.NetworkPort);
        }

        [Test]
        public void Load_NetworkPortExceedsIntRange_FallsBackToDefaultWithWarning()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            // int の範囲を超える整数（long には収まる）。JsonInputReader.GetInt が例外を投げず既定値+警告にすること（H1）。
            fileSystem.WriteAllText(FilePath, "{ \"network.port\": 99999999999 }");
            var store = new AppSettingsStore(FilePath, fileSystem);

            AppSettingsLoadResult result = null;
            Assert.DoesNotThrow(() => result = store.Load());

            Assert.IsTrue(result.HasWarnings);
            Assert.AreEqual(AppSettings.DefaultNetworkPort, result.Settings.NetworkPort);
        }

        [Test]
        public void Load_TtsCacheMaxBytesExceedsLongRange_RecoversOnlyThatKey()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            // long の範囲すら超える巨大な整数（Newtonsoft は BigInteger として保持する）。
            // JsonInputReader.GetLong が例外を投げず、そのキーだけ既定値+警告にすること（#26 統括判断 H1）。
            // 他の正しいキー（player.name）は反映されたままであること。
            fileSystem.WriteAllText(
                FilePath,
                "{ \"tts.cacheMaxBytes\": 999999999999999999999999999999, \"player.name\": \"つむぎ\" }");
            var store = new AppSettingsStore(FilePath, fileSystem);

            AppSettingsLoadResult result = null;
            Assert.DoesNotThrow(() => result = store.Load());

            Assert.IsTrue(result.HasWarnings);
            Assert.AreEqual(AppSettings.DefaultTtsCacheMaxBytes, result.Settings.TtsCacheMaxBytes);
            Assert.AreEqual("つむぎ", result.Settings.PlayerName, "他の正しいキーは反映される。");
        }

        [Test]
        public void Load_InvalidIpLookupUrl_FallsBackWithWarning()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            fileSystem.WriteAllText(FilePath, "{ \"network.ipLookupUrls\": [\"http://insecure.example\"] }");
            var store = new AppSettingsStore(FilePath, fileSystem);

            var result = store.Load();

            Assert.IsTrue(result.HasWarnings);
            CollectionAssert.AreEqual(AppSettings.DefaultIpLookupUrls, result.Settings.IpLookupUrls);
        }

        [Test]
        public void Load_IoExceptionWhileReading_FallsBackToDefaultsWithWarning()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            fileSystem.WriteAllText(FilePath, "{}");
            fileSystem.ThrowOnRead = true;
            var store = new AppSettingsStore(FilePath, fileSystem);

            var result = store.Load();

            Assert.IsTrue(result.HasWarnings);
            Assert.AreEqual(AppSettings.Default.NetworkPort, result.Settings.NetworkPort);
        }

        [Test]
        public void Save_IoExceptionWhileWriting_ReturnsFailureWithWarning()
        {
            var fileSystem = new InMemoryRoomFileSystem { ThrowOnWrite = true };
            var store = new AppSettingsStore(FilePath, fileSystem);

            var result = store.Save(AppSettings.Default);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Warnings.Count > 0);
        }
    }
}
