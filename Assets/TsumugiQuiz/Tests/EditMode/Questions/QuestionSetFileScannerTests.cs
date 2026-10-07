using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="QuestionSetFileScanner"/>（PR #88 レビュー M2）のファイルサイズ・件数上限のテスト。
    /// 上限値は <see cref="QuestionRepository"/> と共有しているため、値そのものは
    /// <c>QuestionRepositoryTests</c> の上限テストと重複させず、共有できていることの確認にとどめる。
    /// </summary>
    public class QuestionSetFileScannerTests
    {
        private string _tempDir;

        [SetUp]
        public void CreateTempDir()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TsumugiQuizQuestionSetFileScannerTests_" + Guid.NewGuid().ToString("N"));
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
        public void ScanFolder_FileExceedsMaxSize_SkipsParsing_AndReturnsErrorEntry()
        {
            var oversizedPath = Path.Combine(_tempDir, "oversized.json");
            // 5MB を超える内容（中身は問わない。パースを試みないことを確認したいだけ）。
            var oversizedContent = new string('a', (int)QuestionRepository.MaxFileSizeBytes + 1);
            File.WriteAllText(oversizedPath, oversizedContent);

            var result = QuestionSetFileScanner.ScanFolder(_tempDir);

            Assert.AreEqual(1, result.Entries.Count);
            var entry = result.Entries[0];
            Assert.IsFalse(entry.IsValid);
            StringAssert.Contains("サイズ", entry.Errors[0]);
        }

        [Test]
        public void ScanFolder_FileCountExceedsMax_SkipsExcess_AndAddsFolderWarning()
        {
            var fileCount = QuestionRepository.MaxFileCount + 3;
            for (var i = 0; i < fileCount; i++)
            {
                var fileName = $"set-{i:D4}.json";
                File.WriteAllText(
                    Path.Combine(_tempDir, fileName),
                    "{ \"schemaVersion\": 1, \"setId\": \"s" + i
                        + "\", \"title\": \"t\", \"questions\": [ { \"id\": \"q1\", \"type\": \"freeText\", "
                        + "\"text\": \"t\", \"answers\": [\"a\"] } ] }");
            }

            var result = QuestionSetFileScanner.ScanFolder(_tempDir);

            Assert.AreEqual(QuestionRepository.MaxFileCount, result.Entries.Count, "上限件数だけが一覧に含まれること。");
            Assert.AreEqual(1, result.FolderWarnings.Count);
            StringAssert.Contains(QuestionRepository.MaxFileCount.ToString(), result.FolderWarnings[0]);
        }

        [Test]
        public void ScanFolder_FileCountWithinLimit_NoFolderWarning()
        {
            File.WriteAllText(
                Path.Combine(_tempDir, "set.json"),
                "{ \"schemaVersion\": 1, \"setId\": \"s1\", \"title\": \"t\", \"questions\": [ { \"id\": \"q1\", "
                    + "\"type\": \"freeText\", \"text\": \"t\", \"answers\": [\"a\"] } ] }");

            var result = QuestionSetFileScanner.ScanFolder(_tempDir);

            Assert.AreEqual(1, result.Entries.Count);
            CollectionAssert.IsEmpty(result.FolderWarnings);
        }
    }
}
