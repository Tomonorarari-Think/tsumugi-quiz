using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Questions;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="QuestionRepository"/> の正常系・異常系（セット単位スキップ）を検証する。
    /// テストデータは Assets/TsumugiQuiz/Tests/EditMode/Questions/TestData/ 配下に
    /// シナリオごとのフォルダとして配置している（docs/question-data.md §2・§9 参照）。
    /// </summary>
    public class QuestionRepositoryTests
    {
        private const long TwoMegabytes = 2 * 1024 * 1024;

        private static string TestDataRoot =>
            Path.Combine(Application.dataPath, "TsumugiQuiz/Tests/EditMode/Questions/TestData");

        private static string ScenarioPath(string scenarioFolderName)
            => Path.Combine(TestDataRoot, scenarioFolderName);

        [Test]
        public void LoadAll_ValidSample_ReadsAllThreeQuestions()
        {
            var repository = new QuestionRepository(ScenarioPath("ValidSample"));

            var result = repository.LoadAll();

            Assert.AreEqual(0, result.SkippedSets.Count, "正常系のセットはスキップされないこと");
            Assert.AreEqual(1, result.Sets.Count);

            var set = result.Sets[0];
            Assert.AreEqual(1, set.SchemaVersion);
            Assert.AreEqual("sample-set-01", set.SetId);
            Assert.AreEqual("サンプル問題セット", set.Title);
            StringAssert.Contains("sample-image.bytes に同梱している", set.Description);
            Assert.AreEqual(3, set.Questions.Count);

            var q1 = set.Questions.Single(q => q.Id == "q1");
            Assert.AreEqual(QuestionType.FreeText, q1.Type);
            Assert.AreEqual("日本の首都はどこ？", q1.Text);
            Assert.AreEqual("日本の首都はどこでしょう？", q1.ReadingText);
            Assert.AreEqual(q1.ReadingText, q1.EffectiveReadingText);
            CollectionAssert.AreEquivalent(new[] { "東京", "とうきょう", "Tokyo" }, q1.Answers);
            CollectionAssert.AreEquivalent(new[] { "地理", "日本" }, q1.Tags);
            Assert.AreEqual(1, q1.Difficulty);

            var q2 = set.Questions.Single(q => q.Id == "q2");
            Assert.AreEqual(QuestionType.Choice, q2.Type);
            CollectionAssert.AreEqual(new[] { "春日部つむぎ", "初音ミク", "洛天依", "IA" }, q2.Choices);
            Assert.AreEqual(0, q2.CorrectIndex);
            Assert.AreEqual(2, q2.Difficulty);

            var q3 = set.Questions.Single(q => q.Id == "q3");
            Assert.AreEqual("images/sample.png", q3.ImagePath);
            CollectionAssert.AreEquivalent(new[] { "ねこ", "猫", "cat" }, q3.Answers);
        }

        [Test]
        public void LoadAll_OmittedOptionalFields_AppliesDefaults()
        {
            // C2/M5: readingText 省略時は text へフォールバック、tags 省略時は空配列、
            // difficulty 省略時は 3（Question.DefaultDifficulty）になることを確認する。
            var repository = new QuestionRepository(ScenarioPath("OmittedOptionalFields"));

            var result = repository.LoadAll();

            Assert.AreEqual(0, result.SkippedSets.Count);
            Assert.AreEqual(1, result.Sets.Count);

            var question = result.Sets[0].Questions.Single();
            Assert.IsNull(question.ReadingText);
            Assert.AreEqual(question.Text, question.EffectiveReadingText, "readingText省略時はtextにフォールバックすること");
            Assert.AreEqual(0, question.Tags.Count, "tags省略時は空配列であること");
            Assert.AreEqual(Question.DefaultDifficulty, question.Difficulty, "difficulty省略時は既定値(3)であること");
        }

        [Test]
        public void LoadAll_DuplicateId_SkipsWholeSet()
        {
            AssertScenarioIsSkipped("DuplicateId", "重複");
        }

        [Test]
        public void LoadAll_CorrectIndexOutOfRange_SkipsWholeSet()
        {
            AssertScenarioIsSkipped("CorrectIndexOutOfRange", "correctIndex", "範囲外");
        }

        [Test]
        public void LoadAll_ChoicesCountInvalid_SkipsWholeSet()
        {
            AssertScenarioIsSkipped("ChoicesCountInvalid", "choices");
        }

        [Test]
        public void LoadAll_TextTooLong_SkipsWholeSet()
        {
            AssertScenarioIsSkipped("TextTooLong", "text", "500");
        }

        [Test]
        public void LoadAll_AnswerTooLong_SkipsWholeSet()
        {
            AssertScenarioIsSkipped("AnswerTooLong", "answers", "100");
        }

        [Test]
        public void LoadAll_FreeTextWithChoices_SkipsWholeSet()
        {
            AssertScenarioIsSkipped("FreeTextWithChoices", "freeText", "choices");
        }

        [Test]
        public void LoadAll_SchemaVersionMismatch_SkipsWholeSet()
        {
            AssertScenarioIsSkipped("SchemaVersionMismatch", "schemaVersion");
        }

        [Test]
        public void LoadAll_ImageMissing_SkipsWholeSet()
        {
            AssertScenarioIsSkipped("ImageMissing", "画像ファイルが見つかりません");
        }

        [Test]
        public void LoadAll_ImageBadExtension_SkipsWholeSet()
        {
            AssertScenarioIsSkipped("ImageBadExtension", "拡張子");
        }

        [Test]
        public void LoadAll_ImagePathTraversal_SkipsWholeSet()
        {
            // C1: "../" でセットフォルダの外へ出るパスは拒否する。
            AssertScenarioIsSkipped("ImagePathTraversal", "セットフォルダの外");
        }

        [Test]
        public void LoadAll_ImagePathAbsolute_SkipsWholeSet()
        {
            // C1: 絶対パス（例: C:\Windows\x.png）は拒否する。
            AssertScenarioIsSkipped("ImagePathAbsolute", "絶対パス");
        }

        [Test]
        public void LoadAll_ImageTooLarge_SkipsWholeSet()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TsumugiQuizTests_" + Guid.NewGuid().ToString("N"));
            var imagesDir = Path.Combine(tempDir, "images");
            Directory.CreateDirectory(imagesDir);

            try
            {
                const string json = @"{
                    ""schemaVersion"": 1,
                    ""setId"": ""test-image-too-large"",
                    ""title"": ""画像2MB超テスト"",
                    ""questions"": [
                        { ""id"": ""q1"", ""type"": ""freeText"", ""text"": ""問題"", ""answers"": [""a""], ""imagePath"": ""images/big.png"" }
                    ]
                }";
                File.WriteAllText(Path.Combine(tempDir, "set.json"), json);

                // 拡張子・存在チェックは通過させ、サイズ超過のみを発生させるためのダミーバイト列（2MB + 1バイト）
                File.WriteAllBytes(Path.Combine(imagesDir, "big.png"), new byte[TwoMegabytes + 1]);

                var repository = new QuestionRepository(tempDir);
                var result = repository.LoadAll();

                Assert.AreEqual(0, result.Sets.Count);
                Assert.AreEqual(1, result.SkippedSets.Count);
                var messages = string.Join(" / ", result.SkippedSets[0].Messages);
                StringAssert.Contains("2MB", messages);
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Test]
        public void LoadAll_FileCountExceedsLimit_ReportsAsFolderErrorWithTruncatedFileList()
        {
            // issue #29（PR #42 統括申し送り L6・レビューM7/M14）: ファイル数上限（200件）を超えた分は
            // スキップし、個々のファイルに紐づかない「フォルダ単位のエラー」（FolderErrors）に
            // 理由を記録する（QuestionSetLoadError には載せない）。
            // 超過分の列挙は先頭5件＋「他N件」に丸める。
            var tempDir = Path.Combine(Path.GetTempPath(), "TsumugiQuizTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                const int fileCount = 210; // 超過 = 10件（先頭5件 + 他5件、に丸められることを確認する）
                for (var i = 0; i < fileCount; i++)
                {
                    var setId = $"set-{i:D3}";
                    var json = $@"{{
                        ""schemaVersion"": 1,
                        ""setId"": ""{setId}"",
                        ""title"": ""件数上限テスト"",
                        ""questions"": [
                            {{ ""id"": ""q1"", ""type"": ""freeText"", ""text"": ""問題"", ""answers"": [""a""] }}
                        ]
                    }}";
                    File.WriteAllText(Path.Combine(tempDir, $"{setId}.json"), json);
                }

                var repository = new QuestionRepository(tempDir);
                var result = repository.LoadAll();

                Assert.AreEqual(200, result.Sets.Count, "上限（200件）までは読み込まれること");
                Assert.AreEqual(0, result.SkippedSets.Count, "超過分は QuestionSetLoadError（ファイル単位）ではなくフォルダ単位のエラーになること");
                Assert.AreEqual(1, result.FolderErrors.Count, "超過分はまとめて1件のフォルダエラーになること");

                var message = result.FolderErrors[0];
                StringAssert.Contains("超過分 10件", message, "超過件数（210-200=10件）が明記されること");
                StringAssert.Contains("他5件", message, "先頭5件を超える分は「他N件」に丸められること");
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Test]
        public void LoadAll_FileLockedByAnotherProcess_RetriesThenReportsAsTransientError()
        {
            // issue #29 レビューM9: 他プロセスに開かれている等の一時的な IOException は
            // 短くリトライしたうえで、通常の「読み込みに失敗しました」とは異なるメッセージで報告する。
            var tempDir = Path.Combine(Path.GetTempPath(), "TsumugiQuizTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var filePath = Path.Combine(tempDir, "locked.json");
            File.WriteAllText(filePath, "{}");

            try
            {
                using (new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var repository = new QuestionRepository(tempDir);
                    var result = repository.LoadAll();

                    Assert.AreEqual(0, result.Sets.Count);
                    Assert.AreEqual(1, result.SkippedSets.Count);

                    var message = string.Join(" / ", result.SkippedSets[0].Messages);
                    StringAssert.Contains("一時的に読み取れませんでした", message);
                    StringAssert.Contains("他アプリが使用中", message);
                }
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Test]
        public void LoadAll_FileSizeExceedsLimit_SkipsFileWithoutParsing()
        {
            // issue #29（PR #42 統括申し送り L6）: 1ファイルの上限（5MB）を超えるファイルは
            // パースを試みず（＝JSON構文エラーとしてではなく）サイズ超過としてスキップする。
            var tempDir = Path.Combine(Path.GetTempPath(), "TsumugiQuizTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                const long fiveMegabytes = 5L * 1024 * 1024;
                var oversizedContent = new string('a', (int)(fiveMegabytes + 1));
                File.WriteAllText(Path.Combine(tempDir, "too-big.json"), oversizedContent);

                var repository = new QuestionRepository(tempDir);
                var result = repository.LoadAll();

                Assert.AreEqual(0, result.Sets.Count);
                Assert.AreEqual(1, result.SkippedSets.Count);

                var message = string.Join(" / ", result.SkippedSets[0].Messages);
                StringAssert.Contains("5MB", message);
                StringAssert.DoesNotContain("読み込みに失敗しました", message,
                    "サイズ超過は JSON パースを試みる前に検出され、パースエラーとは区別されること");
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Test]
        public void LoadAll_FolderMissing_ReturnsEmptyResultWithoutThrowing()
        {
            var nonExistentFolder = Path.Combine(TestDataRoot, "DoesNotExist_" + Guid.NewGuid().ToString("N"));
            var repository = new QuestionRepository(nonExistentFolder);

            var result = repository.LoadAll();

            Assert.AreEqual(0, result.Sets.Count);
            Assert.AreEqual(0, result.SkippedSets.Count);
            Assert.AreEqual(1, result.FolderErrors.Count, "フォルダ不在はフォルダ単位のエラーとして報告されること（H1/M7）");
            StringAssert.Contains("見つかりません", result.FolderErrors[0]);
        }

        [Test]
        public void LoadAll_MixedValidAndInvalidInSameFolder_LoadsValidAndSkipsInvalidIndependently()
        {
            // H5: 同一フォルダに正常セットと異常セットが混在していても、正常セットだけが
            // 読み込まれ、異常セットだけが（他を巻き込まず）スキップされること。
            var repository = new QuestionRepository(ScenarioPath("MixedValidAndInvalid"));

            var result = repository.LoadAll();

            Assert.AreEqual(1, result.Sets.Count);
            Assert.AreEqual("test-mixed-valid", result.Sets[0].SetId);

            Assert.AreEqual(1, result.SkippedSets.Count);
            StringAssert.Contains("invalid-set.json", result.SkippedSets[0].FilePath);
        }

        [Test]
        public void LoadAll_MalformedJson_SkipsWholeSet()
        {
            // H6: JSON 構文エラー（末尾カンマ・閉じ括弧欠落）はパースエラーとしてセット単位でスキップする。
            LogAssert.Expect(LogType.Warning, new Regex(@"\[QuestionRepository\].*malformed\.json"));

            var repository = new QuestionRepository(ScenarioPath("MalformedJson"));
            var result = repository.LoadAll();

            Assert.AreEqual(0, result.Sets.Count);
            Assert.AreEqual(1, result.SkippedSets.Count);
        }

        [Test]
        public void LoadAll_EmptyFile_SkipsWholeSet()
        {
            // H6: 内容が空の JSON ファイルもセット単位でスキップする。
            LogAssert.Expect(LogType.Warning, new Regex(@"\[QuestionRepository\].*empty\.json"));

            var repository = new QuestionRepository(ScenarioPath("EmptyFile"));
            var result = repository.LoadAll();

            Assert.AreEqual(0, result.Sets.Count);
            Assert.AreEqual(1, result.SkippedSets.Count);
        }

        private static void AssertScenarioIsSkipped(string scenarioFolderName, params string[] expectedMessageFragments)
        {
            var repository = new QuestionRepository(ScenarioPath(scenarioFolderName));

            var result = repository.LoadAll();

            Assert.AreEqual(0, result.Sets.Count, $"{scenarioFolderName}: 不正なセットは読み込まれないこと");
            Assert.AreEqual(1, result.SkippedSets.Count, $"{scenarioFolderName}: セット単位でスキップされること");

            var messages = string.Join(" / ", result.SkippedSets[0].Messages);
            foreach (var fragment in expectedMessageFragments)
            {
                StringAssert.Contains(fragment, messages,
                    $"{scenarioFolderName}: エラーメッセージに '{fragment}' が含まれること（実際: {messages}）");
            }
        }
    }
}
