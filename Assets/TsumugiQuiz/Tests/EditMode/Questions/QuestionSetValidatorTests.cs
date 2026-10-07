using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Questions;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="QuestionSetValidator"/> の純 C# 単体テスト（ファイル I/O を伴わない）。
    /// docs/question-data.md §1（型・件数・文字数などスキーマ自体の制約）と
    /// §2（追加のバリデーション規則）の個別ルールを、ファイルを介さず直接検証する。
    /// ファイルシステムを介した統合的な確認（imagePath の実在・サイズ等）は
    /// <see cref="QuestionRepositoryTests"/> 側で行う。
    /// </summary>
    public class QuestionSetValidatorTests
    {
        private readonly QuestionSetValidator _validator = new QuestionSetValidator();

        private static Question FreeText(
            string id = "q1",
            string text = "問題",
            IReadOnlyList<string> answers = null,
            IReadOnlyList<string> choices = null,
            int? correctIndex = null,
            string imagePath = null,
            int? difficulty = null,
            IReadOnlyList<string> tags = null)
            => new Question(id, QuestionType.FreeText, text,
                answers: answers ?? new[] { "a" },
                choices: choices,
                correctIndex: correctIndex,
                imagePath: imagePath,
                tags: tags,
                difficulty: difficulty);

        private static Question Choice(
            string id = "q1",
            string text = "問題",
            IReadOnlyList<string> choices = null,
            int? correctIndex = 0,
            IReadOnlyList<string> answers = null,
            int? difficulty = null)
            => new Question(id, QuestionType.Choice, text,
                choices: choices ?? new[] { "a", "b" },
                correctIndex: correctIndex,
                answers: answers,
                difficulty: difficulty);

        private static QuestionSet Set(
            IReadOnlyList<Question> questions,
            int schemaVersion = 1,
            string setId = "valid-set-01",
            string title = "有効なセット",
            string description = null)
            => new QuestionSet(schemaVersion, setId, title, description, questions);

        private IReadOnlyList<ValidationError> Validate(QuestionSet set, string baseDirectory = null)
            => _validator.Validate(set, baseDirectory);

        [Test]
        public void Validate_ValidFreeTextSet_ReturnsNoErrors()
        {
            var set = Set(new[] { FreeText() });
            CollectionAssert.IsEmpty(Validate(set));
        }

        [Test]
        public void Validate_ValidChoiceSet_ReturnsNoErrors()
        {
            var set = Set(new[] { Choice() });
            CollectionAssert.IsEmpty(Validate(set));
        }

        [Test]
        public void Validate_SchemaVersionMismatch_ReturnsError()
        {
            var set = Set(new[] { FreeText() }, schemaVersion: 2);
            StringAssert.Contains("schemaVersion", Join(Validate(set)));
        }

        [Test]
        public void Validate_SetIdWithInvalidCharacters_ReturnsError()
        {
            var set = Set(new[] { FreeText() }, setId: "invalid id!!");
            StringAssert.Contains("setId", Join(Validate(set)));
        }

        [Test]
        public void Validate_TitleTooLong_ReturnsError()
        {
            var set = Set(new[] { FreeText() }, title: new string('た', 101));
            StringAssert.Contains("title", Join(Validate(set)));
        }

        [Test]
        public void Validate_DescriptionTooLong_ReturnsError()
        {
            var set = Set(new[] { FreeText() }, description: new string('せ', 501));
            StringAssert.Contains("description", Join(Validate(set)));
        }

        [Test]
        public void Validate_DuplicateQuestionId_ReturnsError()
        {
            var set = Set(new[] { FreeText(id: "dup"), FreeText(id: "dup") });
            StringAssert.Contains("重複", Join(Validate(set)));
        }

        [Test]
        public void Validate_TypeMissing_ReturnsError()
        {
            var question = new Question("q1", null, "問題", answers: new[] { "a" });
            var set = Set(new[] { question });
            StringAssert.Contains("type が指定されていません", Join(Validate(set)));
        }

        [Test]
        public void Validate_TextTooLong_ReturnsError()
        {
            var set = Set(new[] { FreeText(text: new string('あ', 501)) });
            StringAssert.Contains("text", Join(Validate(set)));
        }

        /// <summary>
        /// <c>readingText</c> も <c>text</c> と同じ 500 文字上限（docs/question-data.md §1 / §2）。
        /// 配信用 DTO（<c>QuestionDto</c>、§7）の上限と一致させ、
        /// 「読み込めたのに配信できない問題」を作らないため（#13）。
        /// </summary>
        [Test]
        public void Validate_ReadingTextTooLong_ReturnsError()
        {
            var tooLong = Set(new[]
            {
                new Question("q1", QuestionType.FreeText, "問題", new string('い', 501), new[] { "a" }),
            });
            StringAssert.Contains("readingText", Join(Validate(tooLong)));

            var atLimit = Set(new[]
            {
                new Question("q1", QuestionType.FreeText, "問題", new string('い', 500), new[] { "a" }),
            });
            CollectionAssert.IsEmpty(Validate(atLimit), "500 文字ちょうどは許容する。");
        }

        /// <summary>
        /// <c>id</c> は 100 文字以内（docs/question-data.md §1 / §2）。
        /// 配信用 DTO の上限（<see cref="QuestionLimits.MaxIdLength"/>）と同じ値。
        /// </summary>
        [Test]
        public void Validate_IdTooLong_ReturnsError()
        {
            var set = Set(new[] { FreeText(id: new string('a', QuestionLimits.MaxIdLength + 1)) });
            StringAssert.Contains("id", Join(Validate(set)));

            var atLimit = Set(new[] { FreeText(id: new string('a', QuestionLimits.MaxIdLength)) });
            CollectionAssert.IsEmpty(Validate(atLimit), "100 文字ちょうどは許容する。");
        }

        /// <summary>
        /// <c>tags</c> は 20 件以内・各 100 文字以内（docs/question-data.md §1 / §2）。
        /// 読み込み時は不正として弾き、配信時は切り詰める（出題を止めない）。
        /// </summary>
        [Test]
        public void Validate_TagsExceedingLimits_ReturnError()
        {
            var tooMany = Enumerable
                .Range(0, QuestionLimits.MaxTagCount + 1)
                .Select(i => $"tag{i}")
                .ToArray();
            StringAssert.Contains("tags", Join(Validate(Set(new[] { FreeText(tags: tooMany) }))));

            var tooLong = new[] { new string('え', QuestionLimits.MaxTagLength + 1) };
            StringAssert.Contains("tags", Join(Validate(Set(new[] { FreeText(tags: tooLong) }))));

            var atLimit = Enumerable
                .Range(0, QuestionLimits.MaxTagCount)
                .Select(_ => new string('え', QuestionLimits.MaxTagLength))
                .ToArray();
            CollectionAssert.IsEmpty(
                Validate(Set(new[] { FreeText(tags: atLimit) })), "上限ちょうどは許容する。");
        }

        [Test]
        public void Validate_DifficultyOutOfRange_ReturnsError()
        {
            var tooLow = Set(new[] { FreeText(difficulty: 0) });
            var tooHigh = Set(new[] { FreeText(difficulty: 6) });

            StringAssert.Contains("difficulty", Join(Validate(tooLow)));
            StringAssert.Contains("difficulty", Join(Validate(tooHigh)));
        }

        [Test]
        public void Validate_FreeTextWithoutAnswers_ReturnsError()
        {
            var set = Set(new[] { FreeText(answers: new string[0]) });
            StringAssert.Contains("answers", Join(Validate(set)));
        }

        [Test]
        public void Validate_FreeTextTooManyAnswers_ReturnsError()
        {
            // L3（PR #93 レビュー持ち越し、issue #32）: answers は QuestionLimits.MaxAnswerCount 件まで。
            var tooMany = Set(new[]
            {
                FreeText(answers: Enumerable.Range(0, QuestionLimits.MaxAnswerCount + 1).Select(i => "a" + i).ToArray()),
            });
            var atLimit = Set(new[]
            {
                FreeText(answers: Enumerable.Range(0, QuestionLimits.MaxAnswerCount).Select(i => "a" + i).ToArray()),
            });

            StringAssert.Contains("answers", Join(Validate(tooMany)));
            CollectionAssert.IsEmpty(Validate(atLimit), "上限ちょうどは許容する。");
        }

        [Test]
        public void Validate_FreeTextAnswerContainsNullOrEmpty_ReturnsError()
        {
            // M1: answers に null / 空文字列の要素があれば拒否する。
            var withEmpty = Set(new[] { FreeText(answers: new[] { "a", "" }) });
            var withNull = Set(new[] { FreeText(answers: new[] { "a", null }) });

            StringAssert.Contains("空文字列", Join(Validate(withEmpty)));
            StringAssert.Contains("空文字列", Join(Validate(withNull)));
        }

        [Test]
        public void Validate_FreeTextWithChoicesOrCorrectIndex_ReturnsError()
        {
            var withChoices = Set(new[] { FreeText(choices: new[] { "x", "y" }) });
            var withCorrectIndex = Set(new[] { FreeText(correctIndex: 0) });

            StringAssert.Contains("choices", Join(Validate(withChoices)));
            StringAssert.Contains("correctIndex", Join(Validate(withCorrectIndex)));
        }

        [Test]
        public void Validate_ChoiceCountOutOfRange_ReturnsError()
        {
            var tooFew = Set(new[] { Choice(choices: new[] { "a" }, correctIndex: 0) });
            var tooMany = Set(new[] { Choice(choices: Enumerable.Range(0, 9).Select(i => "c" + i).ToArray(), correctIndex: 0) });

            StringAssert.Contains("choices", Join(Validate(tooFew)));
            StringAssert.Contains("choices", Join(Validate(tooMany)));
        }

        [Test]
        public void Validate_ChoiceElementContainsNullOrEmpty_ReturnsError()
        {
            // M1: choices に null / 空文字列の要素があれば拒否する。
            var withEmpty = Set(new[] { Choice(choices: new[] { "a", "" }, correctIndex: 0) });
            var withNull = Set(new[] { Choice(choices: new[] { "a", null }, correctIndex: 0) });

            StringAssert.Contains("空文字列", Join(Validate(withEmpty)));
            StringAssert.Contains("空文字列", Join(Validate(withNull)));
        }

        [Test]
        public void Validate_ChoiceElementTooLong_ReturnsError()
        {
            // H3: choices の各要素は100文字以内（docs/question-data.md §1）。
            var set = Set(new[] { Choice(choices: new[] { "a", new string('c', 101) }, correctIndex: 0) });
            StringAssert.Contains("choices", Join(Validate(set)));
        }

        [Test]
        public void Validate_CorrectIndexOutOfRange_ReturnsError()
        {
            var set = Set(new[] { Choice(choices: new[] { "a", "b" }, correctIndex: 5) });
            StringAssert.Contains("correctIndex", Join(Validate(set)));
        }

        [Test]
        public void Validate_CorrectIndexMissing_ReturnsError()
        {
            var set = Set(new[] { Choice(correctIndex: null) });
            StringAssert.Contains("correctIndex", Join(Validate(set)));
        }

        [Test]
        public void Validate_ChoiceWithAnswers_ReturnsError()
        {
            var set = Set(new[] { Choice(answers: new[] { "a" }) });
            StringAssert.Contains("answers", Join(Validate(set)));
        }

        [Test]
        public void Validate_ImageAbsolutePath_ReturnsErrorWithoutTouchingFileSystem()
        {
            // C1: 絶対パスは setBaseDirectory の有無に関わらず即座に拒否する（存在チェックへ進まない）。
            var set = Set(new[] { FreeText(imagePath: @"C:\Windows\x.png") });
            StringAssert.Contains("絶対パス", Join(Validate(set, @"C:\FakeSetFolder")));
        }

        [Test]
        public void Validate_ImagePathTraversal_ReturnsError()
        {
            // C1: セットフォルダの外へ出る相対パス（"../"）は拒否する。
            var set = Set(new[] { FreeText(imagePath: "../outside.png") });
            StringAssert.Contains("セットフォルダの外", Join(Validate(set, @"C:\FakeSetFolder\Sub")));
        }

        [Test]
        public void Validate_ImageBadExtensionWithoutBaseDirectory_ReturnsExtensionErrorOnly()
        {
            // setBaseDirectory が null の場合は拡張子チェックのみ行い、実在チェックは行わない。
            var set = Set(new[] { FreeText(imagePath: "images/photo.gif") });
            StringAssert.Contains("拡張子", Join(Validate(set, null)));
        }

        private static string Join(IReadOnlyList<ValidationError> errors)
            => string.Join(" / ", errors.Select(e => e.ToString()));
    }
}
