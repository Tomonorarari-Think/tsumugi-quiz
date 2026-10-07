using System;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="AtomicFileWriter"/>（PR #88 レビュー H1。<c>FileSystemRoomFileSystemTests</c> と同じ観点）のテスト。
    /// </summary>
    public class AtomicFileWriterTests
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "TsumugiQuizAtomicFileWriterTests_" + Guid.NewGuid().ToString("N"));
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
            var path = Path.Combine(_tempDirectory, "new-file.json");

            AtomicFileWriter.WriteAllText(path, "{ \"a\": 1 }");

            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual("{ \"a\": 1 }", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(path + ".tmp"), ".tmp が残っていないこと。");
        }

        [Test]
        public void WriteAllText_ExistingFile_ReplacesContent_AndDoesNotLeaveTempFile()
        {
            var path = Path.Combine(_tempDirectory, "existing-file.json");
            File.WriteAllText(path, "{ \"a\": 1 }");

            AtomicFileWriter.WriteAllText(path, "{ \"a\": 2 }");

            Assert.AreEqual("{ \"a\": 2 }", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(path + ".tmp"), ".tmp が残っていないこと。");
        }

        [Test]
        public void WriteAllText_ReplaceFailsBecauseFileIsLocked_DeletesTempFile_AndRethrows()
        {
            var path = Path.Combine(_tempDirectory, "locked-file.json");
            File.WriteAllText(path, "{ \"a\": 1 }");

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<IOException>(() => AtomicFileWriter.WriteAllText(path, "{ \"a\": 2 }"));
            }

            Assert.IsFalse(File.Exists(path + ".tmp"), "失敗後に .tmp が残っていないこと。");
            Assert.AreEqual("{ \"a\": 1 }", File.ReadAllText(path), "元のファイルの内容は変わらない。");
        }
    }
}
