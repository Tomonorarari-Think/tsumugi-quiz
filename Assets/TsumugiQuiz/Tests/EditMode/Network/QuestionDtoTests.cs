using System;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using Unity.Collections;
using Unity.Netcode;

namespace TsumugiQuiz.Tests.EditMode.Network
{
    /// <summary>
    /// 配信用 DTO（<see cref="QuestionDto"/>）の変換・検証・シリアライズのテスト
    /// （docs/question-data.md §7、docs/network.md §8.1、#13）。
    /// </summary>
    public class QuestionDtoTests
    {
        private const string Text = "日本の首都はどこ？";
        private const string ReadingText = "にほんのしゅとはどこ";
        private const string Answer = "とうきょう";

        /// <summary>
        /// DTO の型そのものに正解を積む場所が無いこと。
        /// 「詰め忘れ」ではなく「構造上送れない」ことを保証する（docs/question-data.md §7）。
        /// </summary>
        [Test]
        public void QuestionDto_HasNoMemberForAnswers()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static;

            var memberNames = typeof(QuestionDto).GetFields(flags).Select(f => f.Name)
                .Concat(typeof(QuestionDto).GetProperties(flags).Select(p => p.Name))
                .ToArray();

            var suspicious = memberNames
                .Where(name => name.IndexOf("answer", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("correct", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();

            CollectionAssert.IsEmpty(
                suspicious,
                "QuestionDto に正解を保持しうるメンバーがあってはいけない: " + string.Join(", ", suspicious));
        }

        [Test]
        public void From_FreeTextQuestion_CopiesDisplayFieldsOnly()
        {
            var question = new Question(
                "q-1",
                QuestionType.FreeText,
                Text,
                ReadingText,
                new[] { Answer, "トウキョウ" },
                tags: new[] { "地理", "首都" },
                difficulty: 2);

            var dto = QuestionDto.From(question);

            Assert.AreEqual("q-1", dto.Id);
            Assert.AreEqual(QuestionType.FreeText, dto.Type);
            Assert.AreEqual(Text, dto.Text);
            Assert.AreEqual(ReadingText, dto.ReadingText);
            Assert.AreEqual(2, dto.Difficulty);
            CollectionAssert.AreEqual(new[] { "地理", "首都" }, dto.Tags);
            CollectionAssert.IsEmpty(dto.Choices, "freeText では選択肢を送らない。");
            Assert.IsTrue(dto.TryValidate(out var error), error);
        }

        [Test]
        public void From_WithoutReadingText_FallsBackToText()
        {
            var question = new Question("q-2", QuestionType.FreeText, Text, answers: new[] { Answer });

            var dto = QuestionDto.From(question);

            Assert.AreEqual(Text, dto.ReadingText, "readingText 省略時は text を埋めて送る。");
        }

        [Test]
        public void From_ChoiceQuestion_CopiesChoices()
        {
            var question = new Question(
                "q-3",
                QuestionType.Choice,
                Text,
                choices: new[] { "東京", "大阪" },
                correctIndex: 0);

            var dto = QuestionDto.From(question);

            Assert.AreEqual(QuestionType.Choice, dto.Type);
            CollectionAssert.AreEqual(new[] { "東京", "大阪" }, dto.Choices);
            Assert.IsTrue(dto.TryValidate(out var error), error);
        }

        [Test]
        public void From_NullQuestion_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => QuestionDto.From(null));
        }

        [Test]
        public void From_QuestionWithoutType_Throws()
        {
            var question = new Question("q-4", null, Text, answers: new[] { Answer });

            Assert.Throws<ArgumentException>(() => QuestionDto.From(question));
        }

        /// <summary>
        /// シリアライズ往復で内容が一致し、かつバイト列に正解が現れないこと。
        /// </summary>
        [Test]
        public void Serialize_RoundTrips_AndNeverCarriesAnswer()
        {
            var question = new Question(
                "q-5",
                QuestionType.FreeText,
                Text,
                ReadingText,
                answers: new[] { Answer, "トウキョウ" },
                tags: new[] { "地理", "首都" },
                difficulty: 4);

            var dto = QuestionDto.From(question);
            var bytes = Serialize(dto);
            var restored = Deserialize(bytes);

            Assert.IsTrue(restored.TryValidate(out var error), error);
            Assert.AreEqual(dto.Id, restored.Id);
            Assert.AreEqual(dto.Type, restored.Type);
            Assert.AreEqual(dto.Text, restored.Text);
            Assert.AreEqual(dto.ReadingText, restored.ReadingText);
            Assert.AreEqual(dto.Difficulty, restored.Difficulty);
            CollectionAssert.IsEmpty(restored.Choices);
            CollectionAssert.AreEqual(dto.Tags, restored.Tags);

            Assert.IsTrue(Contains(bytes, Text), "問題文はバイト列に含まれるはず（検査方法の妥当性確認）。");
            Assert.IsFalse(Contains(bytes, Answer), "正解文字列がバイト列に含まれてはいけない。");
            Assert.IsFalse(Contains(bytes, "トウキョウ"), "別表記の正解候補も含まれてはいけない。");
        }

        [Test]
        public void Serialize_ChoiceQuestion_RoundTripsChoices()
        {
            var question = new Question(
                "q-6",
                QuestionType.Choice,
                Text,
                ReadingText,
                choices: new[] { "東京", "大阪", "京都" },
                correctIndex: 2);

            var dto = QuestionDto.From(question);
            var restored = Deserialize(Serialize(dto));

            Assert.IsTrue(restored.TryValidate(out var error), error);
            Assert.AreEqual(QuestionType.Choice, restored.Type);
            CollectionAssert.AreEqual(new[] { "東京", "大阪", "京都" }, restored.Choices);
        }

        [Test]
        public void Deserialize_WithTooManyChoices_IsRejectedWithoutAllocating()
        {
            // choices の件数だけを上限超過（999 件）に改竄したバイト列を組み立てる。
            using var writer = new FastBufferWriter(256, Allocator.Temp);
            writer.WriteValueSafe("q-broken");
            writer.WriteValueSafe(QuestionType.FreeText);
            writer.WriteValueSafe(Text);
            writer.WriteValueSafe(ReadingText);
            writer.WriteValueSafe(999);

            var restored = Deserialize(writer.ToArray());

            CollectionAssert.IsEmpty(restored.Choices, "上限を超える件数では配列を確保しない。");
            Assert.IsFalse(restored.TryValidate(out var error), "壊れた受信データは不正として弾くはず。");
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void Deserialize_WithTooManyTags_IsRejectedWithoutAllocating()
        {
            // tags の件数だけを上限超過（999 件）に改竄したバイト列を組み立てる。
            using var writer = new FastBufferWriter(256, Allocator.Temp);
            writer.WriteValueSafe("q-broken-tags");
            writer.WriteValueSafe(QuestionType.FreeText);
            writer.WriteValueSafe(Text);
            writer.WriteValueSafe(ReadingText);
            writer.WriteValueSafe(0);                        // choices 件数
            writer.WriteValueSafe(Question.DefaultDifficulty);
            writer.WriteValueSafe(999);                      // tags 件数（改竄）

            var restored = Deserialize(writer.ToArray());

            CollectionAssert.IsEmpty(restored.Tags, "上限を超える件数では配列を確保しない。");
            Assert.IsFalse(restored.TryValidate(out var error), "壊れた受信データは不正として弾くはず。");
            Assert.IsNotEmpty(error);
        }

        /// <summary>
        /// 上限いっぱいの DTO でも 1 メッセージに収まること（docs/network.md §8.2）。
        /// RPC は全メッセージ <c>ReliableFragmentedSequenced</c> で送られるため 1296 バイト制限は
        /// かからないが、<c>UnityTransport.MaxPayloadSize</c>（32768）に対して十分小さいことを確かめる。
        /// </summary>
        [Test]
        public void Serialize_MaximumSizedDto_FitsWithinTransportPayload()
        {
            var dto = new QuestionDto(
                new string('a', QuestionDto.MaxIdLength),
                QuestionType.Choice,
                new string('あ', QuestionDto.MaxTextLength),
                new string('い', QuestionDto.MaxReadingTextLength),
                Enumerable.Range(0, QuestionDto.MaxChoiceCount)
                    .Select(_ => new string('う', QuestionDto.MaxChoiceLength)).ToArray(),
                QuestionDto.MaxDifficulty,
                Enumerable.Range(0, QuestionDto.MaxTagCount)
                    .Select(_ => new string('え', QuestionDto.MaxTagLength)).ToArray());

            var bytes = Serialize(dto);

            // 文字列は UTF-16（2 バイト/文字）で書かれるため、上限構成でおよそ 8KB。
            Assert.Less(
                bytes.Length,
                NetworkConstants.MaxPayloadSizeBytes,
                $"上限構成の DTO は MaxPayloadSize に収まるはず（実際: {bytes.Length} バイト）。");
            Assert.Greater(bytes.Length, 7000, $"上限構成では 7KB を超えるはず（実際: {bytes.Length} バイト）。");
            Assert.Less(bytes.Length, 10000, $"上限構成でも 10KB は超えないはず（実際: {bytes.Length} バイト）。");
        }

        /// <summary>
        /// 上限を超える <c>tags</c> は切り詰めて配信する（出題を止めない。統括判断 2026-09-13）。
        /// </summary>
        [Test]
        public void From_WithTooManyTags_TruncatesInsteadOfFailing()
        {
            var tags = Enumerable.Range(0, QuestionDto.MaxTagCount + 5).Select(i => $"tag{i}").ToArray();
            var question = new Question(
                "q-tags", QuestionType.FreeText, Text, answers: new[] { Answer }, tags: tags);

            var dto = QuestionDto.From(question, out var truncated);

            Assert.IsTrue(truncated, "切り詰めたことを呼び出し側へ伝えるはず。");
            Assert.AreEqual(QuestionDto.MaxTagCount, dto.Tags.Count);
            Assert.AreEqual("tag0", dto.Tags[0], "先頭から上限件数分を残すはず。");
            Assert.IsTrue(dto.TryValidate(out var error), error);
        }

        /// <summary>1 件が長すぎるタグも切り詰める。</summary>
        [Test]
        public void From_WithTooLongTag_TruncatesTagText()
        {
            var question = new Question(
                "q-tag-length",
                QuestionType.FreeText,
                Text,
                answers: new[] { Answer },
                tags: new[] { new string('え', QuestionDto.MaxTagLength + 10) });

            var dto = QuestionDto.From(question, out var truncated);

            Assert.IsTrue(truncated);
            Assert.AreEqual(QuestionDto.MaxTagLength, dto.Tags[0].Length);
            Assert.IsTrue(dto.TryValidate(out var error), error);
        }

        /// <summary>公開しているコレクションは書き換えられないこと（内部配列を渡さない）。</summary>
        [Test]
        public void Choices_And_Tags_AreReadOnly()
        {
            var dto = new QuestionDto(
                "q-readonly", QuestionType.Choice, Text, ReadingText, new[] { "東京", "大阪" }, 3, new[] { "地理" });

            Assert.IsNotInstanceOf<string[]>(dto.Choices, "内部配列をそのまま公開してはいけない。");
            Assert.IsNotInstanceOf<string[]>(dto.Tags, "内部配列をそのまま公開してはいけない。");
        }

        [Test]
        public void TryValidate_ValidFreeText_ReturnsTrue()
        {
            var dto = new QuestionDto("q-7", QuestionType.FreeText, Text, ReadingText, null, 3, new[] { "地理" });

            Assert.IsTrue(dto.TryValidate(out var error), error);
        }

        [Test]
        public void TryValidate_MaximumSizes_ReturnsTrue()
        {
            var dto = new QuestionDto(
                new string('a', QuestionDto.MaxIdLength),
                QuestionType.Choice,
                new string('あ', QuestionDto.MaxTextLength),
                new string('い', QuestionDto.MaxReadingTextLength),
                Enumerable.Range(0, QuestionDto.MaxChoiceCount)
                    .Select(_ => new string('う', QuestionDto.MaxChoiceLength)).ToArray(),
                QuestionDto.MaxDifficulty,
                Enumerable.Range(0, QuestionDto.MaxTagCount)
                    .Select(_ => new string('え', QuestionDto.MaxTagLength)).ToArray());

            Assert.IsTrue(dto.TryValidate(out var error), error);
        }

        [Test]
        public void TryValidate_EmptyId_ReturnsFalse()
        {
            var dto = new QuestionDto(string.Empty, QuestionType.FreeText, Text);

            Assert.IsFalse(dto.TryValidate(out _));
        }

        [Test]
        public void TryValidate_IdTooLong_ReturnsFalse()
        {
            var dto = new QuestionDto(new string('a', QuestionDto.MaxIdLength + 1), QuestionType.FreeText, Text);

            Assert.IsFalse(dto.TryValidate(out _));
        }

        [Test]
        public void TryValidate_EmptyText_ReturnsFalse()
        {
            var dto = new QuestionDto("q-8", QuestionType.FreeText, string.Empty);

            Assert.IsFalse(dto.TryValidate(out _));
        }

        [Test]
        public void TryValidate_TextTooLong_ReturnsFalse()
        {
            var dto = new QuestionDto("q-9", QuestionType.FreeText, new string('あ', QuestionDto.MaxTextLength + 1));

            Assert.IsFalse(dto.TryValidate(out _));
        }

        [Test]
        public void TryValidate_ReadingTextTooLong_ReturnsFalse()
        {
            var dto = new QuestionDto(
                "q-10", QuestionType.FreeText, Text, new string('い', QuestionDto.MaxReadingTextLength + 1));

            Assert.IsFalse(dto.TryValidate(out _));
        }

        [Test]
        public void TryValidate_TooManyChoices_ReturnsFalse()
        {
            var choices = Enumerable.Range(0, QuestionDto.MaxChoiceCount + 1).Select(i => $"選択肢{i}").ToArray();
            var dto = new QuestionDto("q-11", QuestionType.Choice, Text, ReadingText, choices);

            Assert.IsFalse(dto.TryValidate(out _));
        }

        [Test]
        public void TryValidate_ChoiceTooLong_ReturnsFalse()
        {
            var choices = new[] { "東京", new string('う', QuestionDto.MaxChoiceLength + 1) };
            var dto = new QuestionDto("q-12", QuestionType.Choice, Text, ReadingText, choices);

            Assert.IsFalse(dto.TryValidate(out _));
        }

        [Test]
        public void TryValidate_ChoiceQuestionWithTooFewChoices_ReturnsFalse()
        {
            var dto = new QuestionDto("q-13", QuestionType.Choice, Text, ReadingText, new[] { "東京" });

            Assert.IsFalse(dto.TryValidate(out _));
        }

        [Test]
        public void TryValidate_FreeTextWithChoices_ReturnsFalse()
        {
            var dto = new QuestionDto("q-14", QuestionType.FreeText, Text, ReadingText, new[] { "東京", "大阪" });

            Assert.IsFalse(dto.TryValidate(out _));
        }

        [Test]
        public void TryValidate_TooManyTags_ReturnsFalse()
        {
            var tags = Enumerable.Range(0, QuestionDto.MaxTagCount + 1).Select(i => $"tag{i}").ToArray();
            var dto = new QuestionDto("q-15", QuestionType.FreeText, Text, ReadingText, null, 3, tags);

            Assert.IsFalse(dto.TryValidate(out _));
        }

        [Test]
        public void TryValidate_TagTooLong_ReturnsFalse()
        {
            var tags = new[] { new string('え', QuestionDto.MaxTagLength + 1) };
            var dto = new QuestionDto("q-16", QuestionType.FreeText, Text, ReadingText, null, 3, tags);

            Assert.IsFalse(dto.TryValidate(out _));
        }

        [TestCase(QuestionDto.MinDifficulty - 1)]
        [TestCase(QuestionDto.MaxDifficulty + 1)]
        public void TryValidate_DifficultyOutOfRange_ReturnsFalse(int difficulty)
        {
            var dto = new QuestionDto("q-17", QuestionType.FreeText, Text, ReadingText, null, difficulty);

            Assert.IsFalse(dto.TryValidate(out _));
        }

        private static byte[] Serialize(QuestionDto dto)
        {
            using var writer = new FastBufferWriter(8192, Allocator.Temp);
            writer.WriteNetworkSerializable(dto);
            return writer.ToArray();
        }

        private static QuestionDto Deserialize(byte[] bytes)
        {
            using var reader = new FastBufferReader(bytes, Allocator.Temp);
            reader.ReadNetworkSerializable(out QuestionDto dto);
            return dto;
        }

        /// <summary>バイト列に文字列が（UTF-16LE / UTF-8 のいずれかで）含まれているか。</summary>
        private static bool Contains(byte[] haystack, string needle)
        {
            return Contains(haystack, Encoding.Unicode.GetBytes(needle))
                || Contains(haystack, Encoding.UTF8.GetBytes(needle));
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            if (needle.Length == 0 || haystack.Length < needle.Length)
            {
                return false;
            }

            for (var offset = 0; offset <= haystack.Length - needle.Length; offset++)
            {
                var matched = true;
                for (var i = 0; i < needle.Length; i++)
                {
                    if (haystack[offset + i] != needle[i])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
