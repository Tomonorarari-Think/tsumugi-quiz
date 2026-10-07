using System;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Questions.Editing;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="QuestionSetEditorService"/> の画像フォルダ関連（issue #31。画像選択 UI）を検証する。
    /// </summary>
    public class QuestionSetEditorServiceImagesTests
    {
        private string _tempDir;

        [SetUp]
        public void CreateTempDir()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TsumugiQuizEditorImagesTests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void DeleteTempDir()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [Test]
        public void ImagesFolderPath_IsImagesSubfolderOfFolderPath()
        {
            var service = new QuestionSetEditorService(_tempDir);

            Assert.AreEqual(Path.Combine(_tempDir, "images"), service.ImagesFolderPath);
        }

        [Test]
        public void ListImages_FolderDoesNotExist_CreatesFolderAndReturnsEmpty()
        {
            var service = new QuestionSetEditorService(_tempDir);

            var images = service.ListImages();

            Assert.IsTrue(Directory.Exists(service.ImagesFolderPath), "初回アクセス時に images フォルダが作成されること。");
            CollectionAssert.IsEmpty(images);
        }

        [Test]
        public void ListImages_ExistingImages_ReturnsRelativePaths()
        {
            var service = new QuestionSetEditorService(_tempDir);
            Directory.CreateDirectory(service.ImagesFolderPath);
            File.WriteAllText(Path.Combine(service.ImagesFolderPath, "q1.png"), "dummy");

            var images = service.ListImages();

            CollectionAssert.AreEqual(new[] { "images/q1.png" }, images);
        }
    }
}
