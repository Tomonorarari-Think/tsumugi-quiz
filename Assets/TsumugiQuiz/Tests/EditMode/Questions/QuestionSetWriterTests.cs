using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="QuestionSetWriter"/>（PR #88 レビュー H1/H3）のテスト。
    /// <c>TryWrite</c> はアトミックな置き換え（更新用）、<c>TryWriteNew</c> は
    /// <see cref="FileMode.CreateNew"/> による「存在したら失敗」の新規作成専用。
    /// </summary>
    public class QuestionSetWriterTests
    {
        private string _tempDir;

        [SetUp]
        public void CreateTempDir()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TsumugiQuizQuestionSetWriterTests_" + Guid.NewGuid().ToString("N"));
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

        private static QuestionSet MakeSet(string setId)
            => new QuestionSet(1, setId, "セット", string.Empty,
                new[] { new Question("q1", QuestionType.FreeText, "問題", answers: new[] { "回答" }) });

        [Test]
        public void TryWrite_NewFile_DoesNotLeaveTempFile()
        {
            var path = Path.Combine(_tempDir, "set.json");

            Assert.IsTrue(QuestionSetWriter.TryWrite(path, MakeSet("set-1"), out var error), error);

            Assert.IsTrue(File.Exists(path));
            Assert.IsFalse(File.Exists(path + ".tmp"));
        }

        [Test]
        public void TryWrite_ExistingFileLocked_DoesNotLeaveTempFile_AndFails()
        {
            var path = Path.Combine(_tempDir, "set.json");
            File.WriteAllText(path, "{}");

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                LogAssert.Expect(LogType.Error, new Regex(@"\[QuestionSetWriter\].*書き込みに失敗しました"));
                var result = QuestionSetWriter.TryWrite(path, MakeSet("set-1"), out var error);
                Assert.IsFalse(result);
                Assert.IsNotNull(error);
            }

            Assert.IsFalse(File.Exists(path + ".tmp"), "失敗後に .tmp が残っていないこと。");
        }

        [Test]
        public void TryWriteNew_FileDoesNotExist_CreatesFile()
        {
            var path = Path.Combine(_tempDir, "new-set.json");

            Assert.IsTrue(QuestionSetWriter.TryWriteNew(path, MakeSet("new-set"), out var error), error);

            Assert.IsTrue(File.Exists(path));
        }

        [Test]
        public void TryWriteNew_FileAlreadyExists_FailsWithoutOverwriting()
        {
            var path = Path.Combine(_tempDir, "existing.json");
            File.WriteAllText(path, "元の内容");

            var result = QuestionSetWriter.TryWriteNew(path, MakeSet("existing"), out var error);

            Assert.IsFalse(result, "既に存在するファイルへの TryWriteNew は失敗すること。");
            Assert.IsNotNull(error);
            Assert.AreEqual("元の内容", File.ReadAllText(path), "既存ファイルの内容が上書きされていないこと。");
            CollectionAssert.IsEmpty(
                FindTempFiles(path), "衝突後に .tmp が残っていないこと（PR #88 レビュー LOW）。");
        }

        [Test]
        public void TryWriteNew_FileDoesNotExist_DoesNotLeaveTempFile()
        {
            // L6（PR #93 レビュー持ち越し、issue #32）: 一時ファイル名に GUID を含めるようにしたため、
            // 固定名（path + ".tmp"）ではなくワイルドカードで .tmp の残留を確認する。
            var path = Path.Combine(_tempDir, "new-set.json");

            Assert.IsTrue(QuestionSetWriter.TryWriteNew(path, MakeSet("new-set"), out var error), error);

            CollectionAssert.IsEmpty(FindTempFiles(path), "成功後に .tmp が残っていないこと。");
        }

        /// <summary>
        /// <paramref name="targetPath"/> と同じファイル名を基にした <c>*.tmp</c>（GUID を挟んだものを含む）を探す。
        /// </summary>
        private static string[] FindTempFiles(string targetPath)
        {
            var directory = Path.GetDirectoryName(targetPath);
            var fileName = Path.GetFileName(targetPath);
            return Directory.GetFiles(directory, fileName + "*.tmp");
        }
    }
}
