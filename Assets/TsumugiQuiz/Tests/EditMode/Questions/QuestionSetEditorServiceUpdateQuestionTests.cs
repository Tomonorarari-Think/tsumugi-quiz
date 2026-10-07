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
    /// <see cref="QuestionSetEditorService.UpdateQuestion"/>（issue #31。編集フォームの保存）を検証する。
    /// 外部変更検出・アトミック書き込みの経路は <see cref="QuestionSetEditorServiceTests"/> の
    /// Add/Remove/Move と共通（<c>WriteUpdatedQuestions</c>）なので、ここでは Update 固有の分岐のみ検証する。
    /// </summary>
    public class QuestionSetEditorServiceUpdateQuestionTests
    {
        private string _tempDir;

        [SetUp]
        public void CreateTempDir()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TsumugiQuizUpdateQuestionTests_" + Guid.NewGuid().ToString("N"));
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

        private static QuestionSet MakeSet(params Question[] questions)
            => new QuestionSet(1, "set-id", "セット", string.Empty, questions);

        private static Question MakeQuestion(string id)
            => new Question(id, QuestionType.FreeText, "問題:" + id, answers: new[] { "回答" });

        [Test]
        public void UpdateQuestion_ExistingId_ReplacesContentAndPersists()
        {
            WriteSet("set", MakeSet(MakeQuestion("q1"), MakeQuestion("q2")));
            var service = new QuestionSetEditorService(_tempDir);
            var entry = service.ListSets().Single();

            var updated = new Question("q1", QuestionType.Choice, "更新後の問題", choices: new[] { "A", "B" }, correctIndex: 1);
            var result = service.UpdateQuestion(entry, updated);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            var q1 = result.Entry.Set.Questions.Single(q => q.Id == "q1");
            Assert.AreEqual(QuestionType.Choice, q1.Type);
            Assert.AreEqual("更新後の問題", q1.Text);
            // 他の問題（q2）は変更されないこと。
            Assert.AreEqual(2, result.Entry.Set.Questions.Count);

            var reloaded = service.ListSets().Single();
            var reloadedQ1 = reloaded.Set.Questions.Single(q => q.Id == "q1");
            Assert.AreEqual(QuestionType.Choice, reloadedQ1.Type);
        }

        [Test]
        public void UpdateQuestion_UnknownId_FailsWithoutWriting()
        {
            WriteSet("set", MakeSet(MakeQuestion("q1")));
            var service = new QuestionSetEditorService(_tempDir);
            var entry = service.ListSets().Single();
            var originalWriteTimeUtc = File.GetLastWriteTimeUtc(entry.FilePath);

            var updated = new Question("does-not-exist", QuestionType.FreeText, "問題", answers: new[] { "回答" });
            var result = service.UpdateQuestion(entry, updated);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(originalWriteTimeUtc, File.GetLastWriteTimeUtc(entry.FilePath));
        }

        [Test]
        public void UpdateQuestion_InvalidContent_FailsWithoutWriting()
        {
            WriteSet("set", MakeSet(MakeQuestion("q1")));
            var service = new QuestionSetEditorService(_tempDir);
            var entry = service.ListSets().Single();
            var originalWriteTimeUtc = File.GetLastWriteTimeUtc(entry.FilePath);

            // choice なのに choices が1件しかない（QuestionLimits.MinChoiceCount=2 未満）。
            var invalid = new Question("q1", QuestionType.Choice, "問題", choices: new[] { "A" }, correctIndex: 0);
            LogAssert.Expect(LogType.Error, new Regex(@"\[QuestionSetEditorService\].*更新後の検証に失敗しました"));
            var result = service.UpdateQuestion(entry, invalid);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(originalWriteTimeUtc, File.GetLastWriteTimeUtc(entry.FilePath));
        }

        [Test]
        public void UpdateQuestion_NullQuestion_Throws()
        {
            WriteSet("set", MakeSet(MakeQuestion("q1")));
            var service = new QuestionSetEditorService(_tempDir);
            var entry = service.ListSets().Single();

            Assert.Throws<ArgumentNullException>(() => service.UpdateQuestion(entry, null));
        }

        [Test]
        public void UpdateQuestion_FileModifiedExternally_FailsWithoutOverwriting()
        {
            WriteSet("set", MakeSet(MakeQuestion("q1")));
            var service = new QuestionSetEditorService(_tempDir);
            var entry = service.ListSets().Single();

            System.Threading.Thread.Sleep(20);
            WriteSet("set", MakeSet(MakeQuestion("q1"), MakeQuestion("q2")));

            var updated = new Question("q1", QuestionType.FreeText, "更新後", answers: new[] { "回答" });
            var result = service.UpdateQuestion(entry, updated);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("外部で変更", result.ErrorMessage);
        }
    }
}
