using System;
using System.Collections.Generic;
using System.Linq;

namespace TsumugiQuiz.Questions.Editing
{
    /// <summary>
    /// 編集フォーム（issue #31）の入力値（<see cref="QuestionFormData"/>）と
    /// <see cref="Question"/> との相互変換。Unity API に依存しない純 C#。
    /// </summary>
    public static class QuestionFormConverter
    {
        private const char TagSeparator = ',';

        /// <summary>既存の問題からフォームの初期値を組み立てる。</summary>
        public static QuestionFormData FromQuestion(Question question)
        {
            if (question == null)
            {
                throw new ArgumentNullException(nameof(question));
            }

            return new QuestionFormData(
                question.Type ?? QuestionType.FreeText,
                question.Text,
                question.ReadingText,
                question.Answers,
                question.Choices,
                question.CorrectIndex ?? 0,
                question.ImagePath,
                question.Tags,
                question.Difficulty);
        }

        /// <summary>
        /// フォームの入力値から、保存対象の <see cref="Question"/> を組み立てる。
        /// <paramref name="id"/> はフォームでは編集しない（一覧・並び替えの識別子のため）。
        /// <paramref name="form"/>.Type に応じて不要なフィールド（freeText の choices/correctIndex、
        /// choice の answers）は組み立てない（docs/question-data.md §1）。
        /// </summary>
        public static Question ToQuestion(string id, QuestionFormData form)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("id を指定してください。", nameof(id));
            }

            if (form == null)
            {
                throw new ArgumentNullException(nameof(form));
            }

            var trimmedText = form.Text?.Trim();

            // 空白のみの readingText は「未入力」と同じ扱いにし、JSON に readingText: "" を書き出さない
            // （PR #93 レビュー L8。docs/question-data.md §1 の readingText は省略可）。
            var trimmedReadingText = form.ReadingText?.Trim();
            if (string.IsNullOrEmpty(trimmedReadingText))
            {
                trimmedReadingText = null;
            }

            var imagePath = string.IsNullOrEmpty(form.ImagePath) ? null : form.ImagePath;

            return form.Type == QuestionType.Choice
                ? new Question(
                    id,
                    QuestionType.Choice,
                    trimmedText,
                    trimmedReadingText,
                    answers: null,
                    choices: TrimEach(form.Choices),
                    correctIndex: form.CorrectIndex,
                    imagePath: imagePath,
                    tags: form.Tags,
                    difficulty: form.Difficulty)
                : new Question(
                    id,
                    QuestionType.FreeText,
                    trimmedText,
                    trimmedReadingText,
                    answers: TrimEach(form.Answers),
                    choices: null,
                    correctIndex: null,
                    imagePath: imagePath,
                    tags: form.Tags,
                    difficulty: form.Difficulty);
        }

        /// <summary>
        /// <c>answers</c> / <c>choices</c> の各行の前後の空白を落とす（PR #93 レビュー M1）。
        /// 空白のみの行は空文字列になり、保存前バリデーション
        /// （<see cref="QuestionSetValidator"/> の「空文字列を含めることはできません」）で弾かれる。
        /// </summary>
        private static IReadOnlyList<string> TrimEach(IReadOnlyList<string> values)
            => values == null ? Array.Empty<string>() : values.Select(v => v?.Trim() ?? string.Empty).ToArray();

        /// <summary>
        /// タグの1行テキスト入力（カンマ区切り）を配列へ分解する。前後の空白は落とし、空要素は除く
        /// （仮決め: 本ファイル。docs/question-data.md §8 はタグ入力 UI の形を規定していないため、
        /// リスト UI の <c>answers</c>/<c>choices</c> と違い、簡易な単一行テキストにしている）。
        /// </summary>
        public static IReadOnlyList<string> ParseTags(string rawText)
        {
            if (string.IsNullOrEmpty(rawText))
            {
                return Array.Empty<string>();
            }

            return rawText
                .Split(TagSeparator)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToArray();
        }

        /// <summary>タグ配列を1行テキスト（カンマ区切り）へ整形する（フォーム表示用）。</summary>
        public static string FormatTags(IReadOnlyList<string> tags)
            => tags == null || tags.Count == 0 ? string.Empty : string.Join(", ", tags);
    }
}
