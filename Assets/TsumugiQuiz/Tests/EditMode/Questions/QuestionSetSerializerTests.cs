using System;
using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="QuestionSetSerializer"/>（issue #30「保存」）が docs/question-data.md §1 の
    /// camelCase スキーマで書き出すこと、および自分で書き出した JSON を
    /// <see cref="QuestionRepository"/> が読み戻せる（往復できる）ことを検証する。
    /// </summary>
    public class QuestionSetSerializerTests
    {
        [Test]
        public void Serialize_UsesCamelCaseKeys()
        {
            var question = new Question(
                id: "q1",
                type: QuestionType.Choice,
                text: "問題文",
                choices: new[] { "A", "B" },
                correctIndex: 0);
            var set = new QuestionSet(1, "set-1", "セット", "説明", new[] { question });

            var json = QuestionSetSerializer.Serialize(set);
            var parsed = JObject.Parse(json);

            Assert.IsNotNull(parsed["schemaVersion"]);
            Assert.IsNotNull(parsed["setId"]);
            Assert.IsNotNull(parsed["title"]);
            Assert.IsNotNull(parsed["description"]);
            Assert.IsNotNull(parsed["questions"]);

            var questionJson = (JObject)parsed["questions"][0];
            Assert.IsNotNull(questionJson["id"]);
            Assert.IsNotNull(questionJson["type"]);
            Assert.IsNotNull(questionJson["text"]);
            Assert.IsNotNull(questionJson["choices"]);
            Assert.IsNotNull(questionJson["correctIndex"]);

            // 計算プロパティ（EffectiveReadingText）は書き出し対象に含めない。
            Assert.IsNull(questionJson["effectiveReadingText"]);
        }

        [Test]
        public void Serialize_UsesTwoSpaceIndentation()
        {
            // issue #32 受け入れ条件: 「インデントが半角スペース2個で整形されている」ことを実測する。
            var question = new Question(
                id: "q1",
                type: QuestionType.FreeText,
                text: "問題文",
                answers: new[] { "回答" });
            var set = new QuestionSet(1, "set-1", "セット", "説明", new[] { question });

            var json = QuestionSetSerializer.Serialize(set);
            var lines = json.Replace("\r\n", "\n").Split('\n');

            // 1行目は "{"、2行目が最初のフィールド（"schemaVersion": 1,）で、ちょうど2個の半角スペースで
            // インデントされていること（タブ・4スペース・全角スペースではないこと）を確認する。
            Assert.AreEqual("{", lines[0]);
            StringAssert.StartsWith("  \"schemaVersion\"", lines[1]);
            Assert.IsFalse(lines[1].StartsWith("   "), "3個以上のスペースでインデントされていないこと。");
            Assert.IsFalse(lines[1].StartsWith("\t"), "タブでインデントされていないこと。");

            // PR #103 レビュー L6: ネストが深い箇所（questions[0].id、3階層目）も
            // 2スペース刻みのちょうど6個であることを確認する。
            var idLine = Array.Find(lines, line => line.TrimStart().StartsWith("\"id\""));
            Assert.IsNotNull(idLine, "\"id\" を含む行が見つかりません。");
            StringAssert.StartsWith("      \"id\"", idLine);
            Assert.IsFalse(idLine.StartsWith("       "), "7個以上のスペースでインデントされていないこと。");
        }

        [Test]
        public void Serialize_ThenLoadWithQuestionRepository_RoundTripsSameContent()
        {
            var freeText = new Question(
                id: "q1",
                type: QuestionType.FreeText,
                text: "自由入力の問題",
                readingText: "じゆうにゅうりょくのもんだい",
                answers: new[] { "こたえ", "答え" },
                tags: new[] { "タグ1" },
                difficulty: 4);
            var choice = new Question(
                id: "q2",
                type: QuestionType.Choice,
                text: "選択式の問題",
                choices: new[] { "選択肢A", "選択肢B", "選択肢C" },
                correctIndex: 2);
            var set = new QuestionSet(1, "round-trip-set", "往復テスト", "説明文", new[] { freeText, choice });

            var tempDir = Path.Combine(Path.GetTempPath(), "TsumugiQuizTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var filePath = Path.Combine(tempDir, "round-trip.json");
                Assert.IsTrue(QuestionSetWriter.TryWrite(filePath, set, out var error), error);

                var repository = new QuestionRepository(tempDir);
                var result = repository.LoadAll();

                Assert.AreEqual(0, result.SkippedSets.Count,
                    result.SkippedSets.Count > 0 ? string.Join(" / ", result.SkippedSets[0].Messages) : string.Empty);
                Assert.AreEqual(1, result.Sets.Count);

                var loaded = result.Sets[0];
                Assert.AreEqual(set.SetId, loaded.SetId);
                Assert.AreEqual(set.Title, loaded.Title);
                Assert.AreEqual(set.Description, loaded.Description);
                Assert.AreEqual(2, loaded.Questions.Count);

                var loadedFreeText = loaded.Questions[0];
                Assert.AreEqual(freeText.Id, loadedFreeText.Id);
                Assert.AreEqual(freeText.ReadingText, loadedFreeText.ReadingText);
                CollectionAssert.AreEqual(freeText.Answers, loadedFreeText.Answers);
                CollectionAssert.AreEqual(freeText.Tags, loadedFreeText.Tags);
                Assert.AreEqual(freeText.Difficulty, loadedFreeText.Difficulty);

                var loadedChoice = loaded.Questions[1];
                CollectionAssert.AreEqual(choice.Choices, loadedChoice.Choices);
                Assert.AreEqual(choice.CorrectIndex, loadedChoice.CorrectIndex);
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Test]
        public void Serialize_FreeTextQuestion_DoesNotEmitChoicesOrCorrectIndex()
        {
            // docs/schemas/question-set.schema.json は freeText に choices/correctIndex の存在自体を
            // 禁止している（allOf の not/required）。Question.Choices は常に空配列を保持するため、
            // 何もしないと "choices": [] が出力されてスキーマ違反になる（PR #88 レビュー H2）。
            var question = new Question(
                id: "q1",
                type: QuestionType.FreeText,
                text: "自由入力の問題",
                answers: new[] { "こたえ" });
            var set = new QuestionSet(1, "set-1", "セット", string.Empty, new[] { question });

            var json = QuestionSetSerializer.Serialize(set);
            var questionJson = (JObject)JObject.Parse(json)["questions"][0];

            Assert.IsNull(questionJson["choices"], "freeText の出力に choices を含めてはいけない。");
            Assert.IsNull(questionJson["correctIndex"], "freeText の出力に correctIndex を含めてはいけない。");
            Assert.IsNotNull(questionJson["answers"]);
        }

        [Test]
        public void Serialize_ChoiceQuestion_DoesNotEmitAnswers()
        {
            var question = new Question(
                id: "q1",
                type: QuestionType.Choice,
                text: "選択式の問題",
                choices: new[] { "A", "B" },
                correctIndex: 0);
            var set = new QuestionSet(1, "set-1", "セット", string.Empty, new[] { question });

            var json = QuestionSetSerializer.Serialize(set);
            var questionJson = (JObject)JObject.Parse(json)["questions"][0];

            Assert.IsNull(questionJson["answers"], "choice の出力に answers を含めてはいけない。");
            Assert.IsNotNull(questionJson["choices"]);
            Assert.IsNotNull(questionJson["correctIndex"]);
        }

        [Test]
        public void Serialize_NoTags_DoesNotEmitTagsKey()
        {
            var question = new Question(
                id: "q1",
                type: QuestionType.FreeText,
                text: "自由入力の問題",
                answers: new[] { "こたえ" });
            var set = new QuestionSet(1, "set-1", "セット", string.Empty, new[] { question });

            var json = QuestionSetSerializer.Serialize(set);
            var questionJson = (JObject)JObject.Parse(json)["questions"][0];

            Assert.IsNull(questionJson["tags"]);
        }

        [Test]
        public void Serialize_ImagePath_RoundTripsThroughJson()
        {
            var question = new Question(
                id: "q1",
                type: QuestionType.FreeText,
                text: "自由入力の問題",
                answers: new[] { "こたえ" },
                imagePath: "images/q1.png");
            var set = new QuestionSet(1, "set-1", "セット", string.Empty, new[] { question });

            var json = QuestionSetSerializer.Serialize(set);
            var questionJson = (JObject)JObject.Parse(json)["questions"][0];
            Assert.AreEqual("images/q1.png", (string)questionJson["imagePath"]);

            var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<QuestionSet>(json);
            Assert.AreEqual("images/q1.png", deserialized.Questions[0].ImagePath);
        }

        [Test]
        public void Serialize_NoReadingTextOrImagePath_DoesNotEmitNullKeys()
        {
            var question = new Question(
                id: "q1",
                type: QuestionType.FreeText,
                text: "自由入力の問題",
                answers: new[] { "こたえ" });
            var set = new QuestionSet(1, "set-1", "セット", string.Empty, new[] { question });

            var json = QuestionSetSerializer.Serialize(set);
            var questionJson = (JObject)JObject.Parse(json)["questions"][0];

            Assert.IsNull(questionJson["readingText"]);
            Assert.IsNull(questionJson["imagePath"]);
        }
    }
}
