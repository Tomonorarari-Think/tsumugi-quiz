using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="QuestionSetEditorService"/>（issue #30。問題エディタの一覧操作）を、実ファイルを使った
    /// EditMode テストで検証する。<see cref="QuestionRepositoryTests"/> と同じ一時フォルダの作法を使う。
    /// </summary>
    public class QuestionSetEditorServiceTests
    {
        private string _tempDir;

        [SetUp]
        public void CreateTempDir()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TsumugiQuizTests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void DeleteTempDir()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        private void WriteSet(string fileName, QuestionSet set)
        {
            Directory.CreateDirectory(_tempDir);
            Assert.IsTrue(QuestionSetWriter.TryWrite(Path.Combine(_tempDir, fileName + ".json"), set, out var error), error);
        }

        private static QuestionSet MakeSet(string setId, string title, params Question[] questions)
            => new QuestionSet(1, setId, title, string.Empty, questions);

        private static Question MakeQuestion(string id)
            => new Question(id, QuestionType.FreeText, "問題:" + id, answers: new[] { "回答" });

        [Test]
        public void ListSets_FolderDoesNotExist_CreatesFolderAndReturnsEmpty()
        {
            var service = new QuestionSetEditorService(_tempDir);

            var entries = service.ListSets();

            Assert.IsTrue(Directory.Exists(_tempDir), "初回アクセス時にフォルダが作成されること");
            CollectionAssert.IsEmpty(entries);
        }

        [Test]
        public void ListSets_ValidAndInvalidFiles_ReturnsBothWithErrorsOnInvalid()
        {
            WriteSet("valid", MakeSet("valid-set", "有効なセット", MakeQuestion("q1")));
            File.WriteAllText(Path.Combine(_tempDir, "broken.json"), "{ not valid json");

            var service = new QuestionSetEditorService(_tempDir);
            var entries = service.ListSets();

            Assert.AreEqual(2, entries.Count);
            var valid = entries.Single(e => e.FileName == "valid");
            var broken = entries.Single(e => e.FileName == "broken");

            Assert.IsTrue(valid.IsValid);
            Assert.AreEqual("valid-set", valid.Set.SetId);

            Assert.IsFalse(broken.IsValid);
            Assert.IsTrue(broken.Errors.Count > 0);
        }

        [Test]
        public void CreateNewSet_NoExistingSets_CreatesFileWithOneQuestion()
        {
            var service = new QuestionSetEditorService(_tempDir);

            var result = service.CreateNewSet(Array.Empty<QuestionSetFileEntry>());

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsTrue(File.Exists(result.Entry.FilePath));
            Assert.AreEqual(1, result.Entry.Set.Questions.Count);

            // 書き出した直後のファイルを再読込しても検証を通ること。
            var reloaded = service.ListSets();
            Assert.AreEqual(1, reloaded.Count);
            Assert.IsTrue(reloaded[0].IsValid);
        }

        [Test]
        public void CreateNewSet_ExistingNewSet_AssignsUniqueIdAndFileName()
        {
            WriteSet("new-set", MakeSet("new-set", "既存の新規セット", MakeQuestion("q1")));
            var service = new QuestionSetEditorService(_tempDir);
            var existing = service.ListSets();

            var result = service.CreateNewSet(existing);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual("new-set-2", result.Entry.FileName);
            Assert.AreEqual("new-set-2", result.Entry.Set.SetId);
        }

        [Test]
        public void DeleteSet_RemovesFile()
        {
            WriteSet("to-delete", MakeSet("to-delete", "削除対象", MakeQuestion("q1")));
            var service = new QuestionSetEditorService(_tempDir);
            var entry = service.ListSets().Single();

            var result = service.DeleteSet(entry);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsFalse(File.Exists(entry.FilePath));
        }

        [Test]
        public void DuplicateSet_ValidSet_CreatesNewFileWithUniqueSetIdAndCopyTitle()
        {
            WriteSet("original", MakeSet("original-set", "元のセット", MakeQuestion("q1")));
            var service = new QuestionSetEditorService(_tempDir);
            var source = service.ListSets().Single();

            var result = service.DuplicateSet(source, new[] { source });

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual("original-copy", result.Entry.FileName);
            Assert.AreEqual("original-set-copy", result.Entry.Set.SetId);
            Assert.AreEqual("元のセットのコピー", result.Entry.Set.Title);
            Assert.AreEqual(1, result.Entry.Set.Questions.Count);

            // 元のファイルは変更されないこと。
            Assert.IsTrue(File.Exists(source.FilePath));
        }

        [Test]
        public void DuplicateSet_BrokenFile_CopiesRawBytesWithoutValidating()
        {
            Directory.CreateDirectory(_tempDir);
            var brokenPath = Path.Combine(_tempDir, "broken.json");
            File.WriteAllText(brokenPath, "{ not valid json");

            var service = new QuestionSetEditorService(_tempDir);
            var source = service.ListSets().Single();
            Assert.IsFalse(source.IsValid);

            var result = service.DuplicateSet(source, new[] { source });

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsFalse(result.Entry.IsValid);
            Assert.AreEqual("broken-copy", result.Entry.FileName);
            Assert.AreEqual(File.ReadAllText(brokenPath), File.ReadAllText(result.Entry.FilePath));
        }

        [Test]
        public void AddEmptyQuestion_AppendsQuestionAndPersists()
        {
            WriteSet("set", MakeSet("set-id", "セット", MakeQuestion("q1")));
            var service = new QuestionSetEditorService(_tempDir);
            var entry = service.ListSets().Single();

            var result = service.AddEmptyQuestion(entry);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(2, result.Entry.Set.Questions.Count);

            var reloaded = service.ListSets().Single();
            Assert.AreEqual(2, reloaded.Set.Questions.Count);
        }

        [Test]
        public void RemoveQuestion_LastRemainingQuestion_FailsWithoutWritingFile()
        {
            WriteSet("set", MakeSet("set-id", "セット", MakeQuestion("q1")));
            var service = new QuestionSetEditorService(_tempDir);
            var entry = service.ListSets().Single();
            var originalWriteTimeUtc = File.GetLastWriteTimeUtc(entry.FilePath);

            var result = service.RemoveQuestion(entry, "q1");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("1件以上", result.ErrorMessage);
            Assert.AreEqual(originalWriteTimeUtc, File.GetLastWriteTimeUtc(entry.FilePath), "拒否された場合はファイルを書き換えないこと");
        }

        [Test]
        public void RemoveQuestion_WithMultipleQuestions_RemovesAndPersists()
        {
            WriteSet("set", MakeSet("set-id", "セット", MakeQuestion("q1"), MakeQuestion("q2")));
            var service = new QuestionSetEditorService(_tempDir);
            var entry = service.ListSets().Single();

            var result = service.RemoveQuestion(entry, "q1");

            Assert.IsTrue(result.Success, result.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "q2" }, result.Entry.Set.Questions.Select(q => q.Id).ToArray());
        }

        [Test]
        public void MoveQuestion_ReordersAndPersists()
        {
            WriteSet("set", MakeSet("set-id", "セット", MakeQuestion("q1"), MakeQuestion("q2"), MakeQuestion("q3")));
            var service = new QuestionSetEditorService(_tempDir);
            var entry = service.ListSets().Single();

            var result = service.MoveQuestion(entry, fromIndex: 0, toIndex: 2);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            CollectionAssert.AreEqual(
                new[] { "q2", "q3", "q1" },
                result.Entry.Set.Questions.Select(q => q.Id).ToArray());

            var reloaded = service.ListSets().Single();
            CollectionAssert.AreEqual(
                new[] { "q2", "q3", "q1" },
                reloaded.Set.Questions.Select(q => q.Id).ToArray());
        }

        // --- PR #88 レビュー H3: 新規作成・複製の書き込み衝突検出 ---

        [Test]
        public void CreateNewSet_FileAlreadyExistsButNotInStaleEntryList_FailsWithoutOverwriting()
        {
            var service = new QuestionSetEditorService(_tempDir);

            // in-memory の一覧（View 側が保持する古いキャッシュ）を空のまま、
            // 実際には既に "new-set.json" が存在する状況を再現する。
            WriteSet("new-set", MakeSet("new-set", "既に存在するセット", MakeQuestion("q1")));

            var result = service.CreateNewSet(Array.Empty<QuestionSetFileEntry>());

            Assert.IsFalse(result.Success, "採番元の一覧が古く、実際には同名ファイルが存在する場合は失敗すること。");
            var reloaded = service.ListSets().Single();
            Assert.AreEqual("既に存在するセット", reloaded.Set.Title, "既存ファイルが上書きされていないこと。");
        }

        [Test]
        public void DuplicateSet_TargetFileAlreadyExistsButNotInStaleEntryList_FailsWithoutOverwriting()
        {
            WriteSet("original", MakeSet("original-set", "元のセット", MakeQuestion("q1")));
            WriteSet("original-copy", MakeSet("original-set-copy", "既に存在する複製", MakeQuestion("q1")));
            var service = new QuestionSetEditorService(_tempDir);
            var source = service.ListSets().Single(e => e.FileName == "original");

            // currentEntries に "original-copy" を含めない（古いキャッシュを模す）。
            var result = service.DuplicateSet(source, new[] { source });

            Assert.IsFalse(result.Success);
            var existing = service.ListSets().Single(e => e.FileName == "original-copy");
            Assert.AreEqual("既に存在する複製", existing.Set.Title, "既存の複製先ファイルが上書きされていないこと。");
        }

        // --- PR #88 レビュー H3: 外部変更の検出（AddEmptyQuestion/RemoveQuestion/MoveQuestion） ---

        [Test]
        public void AddEmptyQuestion_FileModifiedExternallySinceListSets_FailsWithoutWriting()
        {
            WriteSet("set", MakeSet("set-id", "セット", MakeQuestion("q1")));
            var service = new QuestionSetEditorService(_tempDir);
            var entry = service.ListSets().Single();

            // 別ウィンドウ・別プロセスによる外部変更を模す。
            System.Threading.Thread.Sleep(20);
            WriteSet("set", MakeSet("set-id", "外部で変更されたセット", MakeQuestion("q1"), MakeQuestion("q2")));

            var result = service.AddEmptyQuestion(entry);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("外部で変更", result.ErrorMessage);
            var reloaded = service.ListSets().Single();
            Assert.AreEqual(2, reloaded.Set.Questions.Count, "外部変更後の内容が保持され、上書きされていないこと。");
        }

        [Test]
        public void MoveQuestion_FileNotModifiedSinceListSets_Succeeds()
        {
            WriteSet("set", MakeSet("set-id", "セット", MakeQuestion("q1"), MakeQuestion("q2")));
            var service = new QuestionSetEditorService(_tempDir);
            var entry = service.ListSets().Single();

            var result = service.MoveQuestion(entry, 0, 1);

            Assert.IsTrue(result.Success, result.ErrorMessage);
        }

        // --- PR #88 レビュー H4: 複製時のタイトル切り詰めと検証 ---

        [Test]
        public void DuplicateSet_TitleAtMaxLength_TruncatesAndSucceeds()
        {
            var longTitle = new string('あ', 100);
            WriteSet("original", MakeSet("original-set", longTitle, MakeQuestion("q1")));
            var service = new QuestionSetEditorService(_tempDir);
            var source = service.ListSets().Single();

            var result = service.DuplicateSet(source, new[] { source });

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.LessOrEqual(result.Entry.Set.Title.Length, 100);
            StringAssert.EndsWith("のコピー", result.Entry.Set.Title);
        }

        // --- PR #88 レビュー M4: フォルダ外パスの拒否 ---

        [Test]
        public void DeleteSet_PathOutsideFolder_FailsWithoutDeleting()
        {
            var outsideDir = Path.Combine(Path.GetTempPath(), "TsumugiQuizOutside_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outsideDir);
            try
            {
                var outsidePath = Path.Combine(outsideDir, "outside.json");
                File.WriteAllText(outsidePath, "{}");
                var entry = new QuestionSetFileEntry(outsidePath, "outside", null, Array.Empty<string>(), DateTime.MinValue);

                var service = new QuestionSetEditorService(_tempDir);
                LogAssert.Expect(LogType.Error, new Regex(@"\[QuestionSetEditorService\].*問題フォルダ外のファイルは削除できません"));
                var result = service.DeleteSet(entry);

                Assert.IsFalse(result.Success);
                Assert.IsTrue(File.Exists(outsidePath), "フォルダ外のファイルが削除されていないこと。");
            }
            finally
            {
                Directory.Delete(outsideDir, recursive: true);
            }
        }
    }
}
