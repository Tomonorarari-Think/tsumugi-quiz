using System;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.Tests.EditMode.Room
{
    /// <summary>
    /// <see cref="FileSystemRoomFileSystem"/>（#26 統括判断 H3/M1）のテスト。
    /// 実ファイルシステムに一時フォルダを作って検証し、テスト終了時に削除する。
    /// </summary>
    public class FileSystemRoomFileSystemTests
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "TsumugiQuizFileSystemRoomFileSystemTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
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
        public void WriteAllText_NewFile_DoesNotLeaveTempFile()
        {
            var fileSystem = new FileSystemRoomFileSystem();
            var path = Path.Combine(_tempDirectory, "new-file.json");

            fileSystem.WriteAllText(path, "{ \"a\": 1 }");

            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual("{ \"a\": 1 }", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(path + ".tmp"), ".tmp が残っていないこと。");
        }

        [Test]
        public void WriteAllText_ExistingFile_ReplacesContent_AndDoesNotLeaveTempFile()
        {
            var fileSystem = new FileSystemRoomFileSystem();
            var path = Path.Combine(_tempDirectory, "existing-file.json");
            File.WriteAllText(path, "{ \"a\": 1 }");

            fileSystem.WriteAllText(path, "{ \"a\": 2 }");

            Assert.AreEqual("{ \"a\": 2 }", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(path + ".tmp"), ".tmp が残っていないこと。");
        }

        [Test]
        public void WriteAllText_ReplaceFailsBecauseFileIsLocked_DeletesTempFile_AndRethrows()
        {
            // 対象ファイルを排他ロックした状態で書き込みを試み、File.Replace が失敗する状況を再現する
            // （#26 統括判断 M1/LOW-B）。.tmp が残らず、元の例外（IOException）がそのまま伝播すること、
            // 元のファイルの内容が書き換わっていないことを確認する。
            var fileSystem = new FileSystemRoomFileSystem();
            var path = Path.Combine(_tempDirectory, "locked-file.json");
            File.WriteAllText(path, "{ \"a\": 1 }");

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<IOException>(() => fileSystem.WriteAllText(path, "{ \"a\": 2 }"));
            }

            Assert.IsFalse(File.Exists(path + ".tmp"), "失敗後に .tmp が残っていないこと。");
            Assert.AreEqual("{ \"a\": 1 }", File.ReadAllText(path), "元のファイルの内容は変わらない。");
        }
    }
}
