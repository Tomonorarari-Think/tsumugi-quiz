using System;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="QuestionImageFolder"/>（issue #31。画像選択 UI 向けの画像一覧の純ロジック）を検証する。
    /// </summary>
    public class QuestionImageFolderTests
    {
        private string _tempDir;

        [SetUp]
        public void CreateTempDir()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TsumugiQuizImageFolderTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
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
        public void GetImagesFolderPath_CombinesQuestionsFolderAndImages()
        {
            var path = QuestionImageFolder.GetImagesFolderPath(@"C:\Questions");

            Assert.AreEqual(Path.Combine(@"C:\Questions", "images"), path);
        }

        [Test]
        public void GetImagesFolderPath_EmptyPath_Throws()
        {
            Assert.Throws<ArgumentException>(() => QuestionImageFolder.GetImagesFolderPath(string.Empty));
        }

        [Test]
        public void ListImageRelativePaths_FolderDoesNotExist_ReturnsEmpty()
        {
            var result = QuestionImageFolder.ListImageRelativePaths(Path.Combine(_tempDir, "images"));

            CollectionAssert.IsEmpty(result.RelativePaths);
            CollectionAssert.IsEmpty(result.FolderWarnings);
        }

        [Test]
        public void ListImageRelativePaths_MixedFiles_ReturnsOnlyAllowedExtensionsSortedByName()
        {
            var imagesDir = Path.Combine(_tempDir, "images");
            Directory.CreateDirectory(imagesDir);
            File.WriteAllText(Path.Combine(imagesDir, "b.png"), "dummy");
            File.WriteAllText(Path.Combine(imagesDir, "a.JPG"), "dummy");
            File.WriteAllText(Path.Combine(imagesDir, "c.jpeg"), "dummy");
            File.WriteAllText(Path.Combine(imagesDir, "readme.txt"), "not an image");

            var result = QuestionImageFolder.ListImageRelativePaths(imagesDir);

            CollectionAssert.AreEqual(
                new[] { "images/a.JPG", "images/b.png", "images/c.jpeg" }, result.RelativePaths);
            CollectionAssert.IsEmpty(result.FolderWarnings);
        }

        [Test]
        public void ListImageRelativePaths_SubFolder_IsNotIncluded()
        {
            var imagesDir = Path.Combine(_tempDir, "images");
            Directory.CreateDirectory(Path.Combine(imagesDir, "sub"));
            File.WriteAllText(Path.Combine(imagesDir, "sub", "nested.png"), "dummy");

            var result = QuestionImageFolder.ListImageRelativePaths(imagesDir);

            CollectionAssert.IsEmpty(result.RelativePaths);
        }

        [Test]
        public void ListImageRelativePaths_ExceedsMaxFileCount_TruncatesAndWarns()
        {
            var imagesDir = Path.Combine(_tempDir, "images");
            Directory.CreateDirectory(imagesDir);

            const int extra = 3;
            var total = QuestionLimits.MaxQuestionImageFileCount + extra;
            for (var i = 0; i < total; i++)
            {
                // 打ち切り後に残るのがファイル名昇順（Ordinal）の先頭であることも確かめたいので、
                // 桁数をそろえた連番にする。
                File.WriteAllText(Path.Combine(imagesDir, $"img-{i:D4}.png"), "dummy");
            }

            var result = QuestionImageFolder.ListImageRelativePaths(imagesDir);

            Assert.AreEqual(QuestionLimits.MaxQuestionImageFileCount, result.RelativePaths.Count);
            Assert.AreEqual("images/img-0000.png", result.RelativePaths[0]);
            Assert.AreEqual(
                $"images/img-{QuestionLimits.MaxQuestionImageFileCount - 1:D4}.png",
                result.RelativePaths[result.RelativePaths.Count - 1]);

            Assert.AreEqual(1, result.FolderWarnings.Count);
            StringAssert.Contains("一部のみ表示しています", result.FolderWarnings[0]);
            StringAssert.Contains(total.ToString(), result.FolderWarnings[0]);
        }
    }
}
