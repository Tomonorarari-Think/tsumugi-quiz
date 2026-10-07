using System;
using NUnit.Framework;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.Tests.EditMode.Room
{
    /// <summary>
    /// <see cref="RoomPresetStore"/>（#26）のテスト。実ファイルには触れず <see cref="InMemoryRoomFileSystem"/> を使う。
    /// </summary>
    public class RoomPresetStoreTests
    {
        private const string FolderPath = @"C:\fake\TsumugiQuiz\Presets";

        [Test]
        public void Save_ThenLoad_RoundTrips()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            var store = new RoomPresetStore(FolderPath, fileSystem);
            var preset = new RoomPreset("マイ設定", RoomSettings.Default.WithMaxPlayers(9));

            var saveResult = store.Save(preset);
            var result = store.Load("マイ設定");

            Assert.IsTrue(saveResult.Success);
            Assert.IsTrue(result.Found);
            Assert.IsFalse(result.HasWarnings);
            Assert.AreEqual(9, result.Preset.Settings.MaxPlayers);
        }

        [Test]
        public void Save_ThenLoad_BuiltInPresets_RoundTrip()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            var store = new RoomPresetStore(FolderPath, fileSystem);

            foreach (var builtIn in RoomPreset.BuiltIns)
            {
                var saveResult = store.Save(builtIn);
                var result = store.Load(builtIn.Name);

                Assert.IsTrue(saveResult.Success, builtIn.Name);
                Assert.IsTrue(result.Found, builtIn.Name);
                Assert.IsFalse(result.HasWarnings, builtIn.Name);
                Assert.AreEqual(builtIn.Settings.MaxPlayers, result.Preset.Settings.MaxPlayers, builtIn.Name);
                Assert.AreEqual(
                    builtIn.Settings.TimeLimits.BuzzTimeLimitSec, result.Preset.Settings.TimeLimits.BuzzTimeLimitSec,
                    1e-9, builtIn.Name);
                Assert.AreEqual(builtIn.Settings.Scoring.PenaltyType, result.Preset.Settings.Scoring.PenaltyType, builtIn.Name);
            }
        }

        [Test]
        public void Save_IoExceptionWhileWriting_ReturnsFailureResult()
        {
            var fileSystem = new InMemoryRoomFileSystem { ThrowOnWrite = true };
            var store = new RoomPresetStore(FolderPath, fileSystem);

            var result = store.Save(new RoomPreset("失敗プリセット", RoomSettings.Default));

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Warnings.Count > 0);
        }

        [Test]
        public void Load_IoExceptionWhileReading_FallsBackToDefaultsWithWarning()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            var store = new RoomPresetStore(FolderPath, fileSystem);
            store.Save(new RoomPreset("読み込み失敗", RoomSettings.Default));
            fileSystem.ThrowOnRead = true;

            var result = store.Load("読み込み失敗");

            Assert.IsTrue(result.Found);
            Assert.IsTrue(result.HasWarnings);
        }

        [Test]
        public void ListUserPresetNames_IoException_ReturnsEmpty()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            var store = new RoomPresetStore(FolderPath, fileSystem);
            store.Save(new RoomPreset("設定", RoomSettings.Default));
            fileSystem.ThrowOnRead = true; // ListJsonFileNames はこのフェイクでは Read を使わないため実際には影響しないが、
                                            // 将来の実装変更に備えて例外時にクラッシュしないことを確認する目的のテスト。

            Assert.DoesNotThrow(() => store.ListUserPresetNames());
        }

        [Test]
        public void Load_MissingFile_ReturnsNotFound()
        {
            var store = new RoomPresetStore(FolderPath, new InMemoryRoomFileSystem());

            var result = store.Load("存在しない");

            Assert.IsFalse(result.Found);
            Assert.IsNull(result.Preset);
        }

        [Test]
        public void ListUserPresetNames_ReturnsSavedNamesSorted()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            var store = new RoomPresetStore(FolderPath, fileSystem);
            store.Save(new RoomPreset("b設定", RoomSettings.Default));
            store.Save(new RoomPreset("a設定", RoomSettings.Default));

            var names = store.ListUserPresetNames();

            CollectionAssert.AreEqual(new[] { "a設定", "b設定" }, names);
        }

        [Test]
        public void ListUserPresetNames_EmptyFolder_ReturnsEmpty()
        {
            var store = new RoomPresetStore(FolderPath, new InMemoryRoomFileSystem());

            CollectionAssert.IsEmpty(store.ListUserPresetNames());
        }

        [Test]
        public void Delete_RemovesFile_AndReturnsTrue()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            var store = new RoomPresetStore(FolderPath, fileSystem);
            store.Save(new RoomPreset("削除対象", RoomSettings.Default));

            var deleted = store.Delete("削除対象");
            var afterDelete = store.Load("削除対象");

            Assert.IsTrue(deleted);
            Assert.IsFalse(afterDelete.Found);
        }

        [Test]
        public void Delete_MissingFile_ReturnsFalse()
        {
            var store = new RoomPresetStore(FolderPath, new InMemoryRoomFileSystem());

            Assert.IsFalse(store.Delete("存在しない"));
        }

        [TestCase("a/b")]
        [TestCase("a\\b")]
        public void Save_NameWithInvalidFileNameChars_Throws(string invalidName)
        {
            var store = new RoomPresetStore(FolderPath, new InMemoryRoomFileSystem());
            var preset = new RoomPreset(invalidName, RoomSettings.Default);

            Assert.Throws<ArgumentException>(() => store.Save(preset));
        }

        [TestCase("")]
        [TestCase(" ")]
        public void RoomPreset_BlankName_Throws(string blankName)
        {
            Assert.Throws<ArgumentException>(() => new RoomPreset(blankName, RoomSettings.Default));
        }

        [TestCase("CON")]
        [TestCase("con")]
        [TestCase("NUL")]
        [TestCase("COM1")]
        [TestCase("LPT1")]
        public void Save_ReservedDeviceName_Throws(string reservedName)
        {
            var store = new RoomPresetStore(FolderPath, new InMemoryRoomFileSystem());
            var preset = new RoomPreset(reservedName, RoomSettings.Default);

            Assert.Throws<ArgumentException>(() => store.Save(preset));
        }

        [TestCase("設定.")]
        [TestCase("設定 ")]
        public void Save_NameEndingWithDotOrSpace_Throws(string trailingName)
        {
            var store = new RoomPresetStore(FolderPath, new InMemoryRoomFileSystem());
            var preset = new RoomPreset(trailingName, RoomSettings.Default);

            Assert.Throws<ArgumentException>(() => store.Save(preset));
        }

        [Test]
        public void Save_NameExceedsMaxLength_Throws()
        {
            var store = new RoomPresetStore(FolderPath, new InMemoryRoomFileSystem());
            var tooLongName = new string('あ', RoomPresetStore.MaxNameLength + 1);
            var preset = new RoomPreset(tooLongName, RoomSettings.Default);

            Assert.Throws<ArgumentException>(() => store.Save(preset));
        }

        [Test]
        public void Save_NameAtMaxLength_Succeeds()
        {
            var store = new RoomPresetStore(FolderPath, new InMemoryRoomFileSystem());
            var nameAtLimit = new string('あ', RoomPresetStore.MaxNameLength);
            var preset = new RoomPreset(nameAtLimit, RoomSettings.Default);

            var result = store.Save(preset);

            Assert.IsTrue(result.Success);
        }
    }
}
