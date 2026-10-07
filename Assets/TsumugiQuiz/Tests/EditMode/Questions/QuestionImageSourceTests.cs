using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Images;
using TsumugiQuiz.Tests.Shared.Questions;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="QuestionImagePathResolver"/> と <see cref="QuestionImageSource"/>
    /// （docs/question-data.md §2 の画像の規則、#16）。
    /// </summary>
    public class QuestionImageSourceTests
    {
        private string _root;
        private string _imagesFolder;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "tq-image-source-" + Guid.NewGuid().ToString("N"));
            _imagesFolder = Path.Combine(_root, "images");
            Directory.CreateDirectory(_imagesFolder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Test]
        public void ValidateFormat_RejectsEmptyRootedAndBadExtension()
        {
            Assert.AreEqual(ImagePathError.Empty, QuestionImagePathResolver.ValidateFormat(null));
            Assert.AreEqual(ImagePathError.Empty, QuestionImagePathResolver.ValidateFormat(string.Empty));
            Assert.AreEqual(
                ImagePathError.Rooted, QuestionImagePathResolver.ValidateFormat(@"C:\Windows\x.png"));
            Assert.AreEqual(
                ImagePathError.UnsupportedExtension, QuestionImagePathResolver.ValidateFormat("images/a.gif"));
            Assert.AreEqual(
                ImagePathError.UnsupportedExtension, QuestionImagePathResolver.ValidateFormat("images/a"));
        }

        [Test]
        public void ValidateFormat_AcceptsPngAndJpgCaseInsensitively()
        {
            Assert.AreEqual(ImagePathError.None, QuestionImagePathResolver.ValidateFormat("images/a.png"));
            Assert.AreEqual(ImagePathError.None, QuestionImagePathResolver.ValidateFormat("images/a.PNG"));
            Assert.AreEqual(ImagePathError.None, QuestionImagePathResolver.ValidateFormat("images/a.jpg"));
            Assert.AreEqual(ImagePathError.None, QuestionImagePathResolver.ValidateFormat("images/a.JPEG"));
        }

        [Test]
        public void TryResolve_WithExistingFile_ReturnsFullPath()
        {
            WriteImage("q1.png", 128);

            Assert.IsTrue(
                QuestionImagePathResolver.TryResolve(_root, "images/q1.png", out var fullPath, out var error));
            Assert.AreEqual(ImagePathError.None, error);
            Assert.IsTrue(File.Exists(fullPath));
        }

        [Test]
        public void TryResolve_WithMissingFile_IsNotFound()
        {
            Assert.IsFalse(
                QuestionImagePathResolver.TryResolve(_root, "images/missing.png", out var fullPath, out var error));
            Assert.AreEqual(ImagePathError.NotFound, error);
            Assert.IsNull(fullPath);
        }

        [Test]
        public void TryResolve_WithTraversal_IsOutsideBaseDirectory()
        {
            Assert.IsFalse(
                QuestionImagePathResolver.TryResolve(_imagesFolder, "../outside.png", out _, out var error));
            Assert.AreEqual(ImagePathError.OutsideBaseDirectory, error);
        }

        [Test]
        public void TryResolve_WithOversizedFile_IsTooLargeAndReportsLength()
        {
            var length = QuestionLimits.MaxImageSizeBytes + 1;
            WriteImage("big.png", length);

            Assert.IsFalse(
                QuestionImagePathResolver.TryResolveExisting(
                    _root, "images/big.png", out _, out var fileLengthBytes, out var error));
            Assert.AreEqual(ImagePathError.TooLarge, error);
            Assert.AreEqual(length, fileLengthBytes, "エラーメッセージに載せるため実際のバイト数を返すはず。");
        }

        [Test]
        public void QuestionImageSource_WithImagePath_ResolvesAbsolutePath()
        {
            WriteImage("q1.png", 64);
            var source = new QuestionImageSource(_root);

            Assert.IsTrue(source.TryGetImagePath(CreateQuestion("images/q1.png"), out var path, out var error));
            Assert.IsNull(error);
            Assert.IsTrue(File.Exists(path));
        }

        [Test]
        public void QuestionImageSource_WithoutImagePath_ReturnsFalseWithoutError()
        {
            var source = new QuestionImageSource(_root);

            Assert.IsFalse(source.TryGetImagePath(CreateQuestion(null), out var path, out var error));
            Assert.IsNull(path);
            Assert.IsNull(error, "画像なしの問題は警告の対象ではないので error は null にする。");
        }

        [Test]
        public void QuestionImageSource_WithBrokenImagePath_ReturnsError()
        {
            var source = new QuestionImageSource(_root);

            Assert.IsFalse(source.TryGetImagePath(CreateQuestion("images/missing.png"), out var path, out var error));
            Assert.IsNull(path);
            StringAssert.Contains("見つかりません", error);
        }

        [Test]
        public void QuestionImageSource_WithNullQuestion_ReturnsError()
        {
            var source = new QuestionImageSource(_root);

            Assert.IsFalse(source.TryGetImagePath(null, out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void QuestionImageSource_WithInvalidArguments_Throws()
        {
            Assert.Throws<ArgumentException>(() => new QuestionImageSource(null));
            Assert.Throws<ArgumentException>(() => new QuestionImageSource(string.Empty));
        }

        [Test]
        public void TryReadHeader_ReadsOnlyTheBeginningOfTheFile()
        {
            var png = TestImageFactory.CreatePng(8, 8);
            File.WriteAllBytes(Path.Combine(_imagesFolder, "small.png"), png);

            Assert.IsTrue(
                QuestionImagePathResolver.TryReadHeader(
                    Path.Combine(_imagesFolder, "small.png"), out var header));
            Assert.AreEqual(png.Length, header.Length, "ファイルが小さければ全体が返る。");

            Assert.IsFalse(QuestionImagePathResolver.TryReadHeader(null, out _));
            Assert.IsFalse(
                QuestionImagePathResolver.TryReadHeader(Path.Combine(_imagesFolder, "none.png"), out _));
        }

        /// <summary>
        /// 読み込み時の検証で、画像の中身（形式・解像度）も確かめること
        /// （docs/question-data.md §2、#16 レビュー M2）。
        /// </summary>
        [Test]
        public void Validate_ImageWithinLimits_HasNoImageError()
        {
            File.WriteAllBytes(Path.Combine(_imagesFolder, "q1.png"), TestImageFactory.CreatePng(16, 16));

            var errors = Validate("images/q1.png");

            CollectionAssert.IsEmpty(errors, string.Join(" / ", errors.Select(e => e.ToString())));
        }

        [Test]
        public void Validate_ImageOverDimensionLimit_ReturnsError()
        {
            File.WriteAllBytes(
                Path.Combine(_imagesFolder, "huge.png"),
                TestImageFactory.CreatePngHeaderOnly(QuestionLimits.MaxImageDimension + 1, 16));

            var message = string.Join(" / ", Validate("images/huge.png").Select(e => e.ToString()));

            StringAssert.Contains(QuestionLimits.MaxImageDimension.ToString(), message);
        }

        [Test]
        public void Validate_ImageThatIsNotPngOrJpg_ReturnsError()
        {
            // 拡張子は .png だが中身が違う場合も弾く。
            File.WriteAllBytes(Path.Combine(_imagesFolder, "fake.png"), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            var message = string.Join(" / ", Validate("images/fake.png").Select(e => e.ToString()));

            StringAssert.Contains("PNG/JPG", message);
        }

        /// <summary>画像 1 枚だけを持つ問題セットを検証する。</summary>
        private IReadOnlyList<ValidationError> Validate(string imagePath)
        {
            var set = new QuestionSet(
                1,
                "set-image",
                "画像テスト",
                null,
                new[] { CreateQuestion(imagePath) });

            return new QuestionSetValidator().Validate(set, _root);
        }

        private void WriteImage(string fileName, int length)
        {
            File.WriteAllBytes(Path.Combine(_imagesFolder, fileName), new byte[length]);
        }

        private static Question CreateQuestion(string imagePath)
        {
            return new Question(
                "q-image-1",
                QuestionType.FreeText,
                "画像の問題",
                answers: new[] { "こたえ" },
                imagePath: imagePath);
        }

    }
}
