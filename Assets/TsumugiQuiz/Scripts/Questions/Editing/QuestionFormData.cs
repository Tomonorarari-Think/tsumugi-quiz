using System;
using System.Collections.Generic;
using System.Linq;

namespace TsumugiQuiz.Questions.Editing
{
    /// <summary>
    /// 編集フォーム（issue #31）が保持する、保存前の入力値の不変スナップショット。
    /// Unity API に依存しない純 C# で、<c>QuestionEditorView</c>（UI asmdef）
    /// が入力値の変更ごとに <c>With*</c> で新しいインスタンスへ差し替える（CLAUDE.md「不変データを優先する」）。
    /// </summary>
    public sealed class QuestionFormData
    {
        public QuestionType Type { get; }
        public string Text { get; }
        public string ReadingText { get; }

        /// <summary>freeText の正解候補。行の追加・削除に応じた要素数（空文字列の行も含みうる）。</summary>
        public IReadOnlyList<string> Answers { get; }

        /// <summary>choice の選択肢。行の追加・削除に応じた要素数（空文字列の行も含みうる）。</summary>
        public IReadOnlyList<string> Choices { get; }

        /// <summary>choice の正解インデックス（0始まり）。<see cref="Choices"/> の範囲外になりうる。</summary>
        public int CorrectIndex { get; }

        /// <summary>選択中の画像の相対パス（例: <c>images/q1.png</c>）。未選択なら空文字列。</summary>
        public string ImagePath { get; }

        public IReadOnlyList<string> Tags { get; }
        public int Difficulty { get; }

        public QuestionFormData(
            QuestionType type,
            string text,
            string readingText,
            IReadOnlyList<string> answers,
            IReadOnlyList<string> choices,
            int correctIndex,
            string imagePath,
            IReadOnlyList<string> tags,
            int difficulty)
        {
            Type = type;
            Text = text ?? string.Empty;
            ReadingText = readingText ?? string.Empty;
            Answers = answers?.ToArray() ?? Array.Empty<string>();
            Choices = choices?.ToArray() ?? Array.Empty<string>();
            CorrectIndex = correctIndex;
            ImagePath = imagePath ?? string.Empty;
            Tags = tags?.ToArray() ?? Array.Empty<string>();
            Difficulty = difficulty;
        }

        public QuestionFormData WithType(QuestionType type)
            => new QuestionFormData(type, Text, ReadingText, Answers, Choices, CorrectIndex, ImagePath, Tags, Difficulty);

        public QuestionFormData WithText(string text)
            => new QuestionFormData(Type, text, ReadingText, Answers, Choices, CorrectIndex, ImagePath, Tags, Difficulty);

        public QuestionFormData WithReadingText(string readingText)
            => new QuestionFormData(Type, Text, readingText, Answers, Choices, CorrectIndex, ImagePath, Tags, Difficulty);

        public QuestionFormData WithAnswers(IReadOnlyList<string> answers)
            => new QuestionFormData(Type, Text, ReadingText, answers, Choices, CorrectIndex, ImagePath, Tags, Difficulty);

        public QuestionFormData WithChoices(IReadOnlyList<string> choices)
            => new QuestionFormData(Type, Text, ReadingText, Answers, choices, CorrectIndex, ImagePath, Tags, Difficulty);

        public QuestionFormData WithCorrectIndex(int correctIndex)
            => new QuestionFormData(Type, Text, ReadingText, Answers, Choices, correctIndex, ImagePath, Tags, Difficulty);

        public QuestionFormData WithImagePath(string imagePath)
            => new QuestionFormData(Type, Text, ReadingText, Answers, Choices, CorrectIndex, imagePath, Tags, Difficulty);

        public QuestionFormData WithTags(IReadOnlyList<string> tags)
            => new QuestionFormData(Type, Text, ReadingText, Answers, Choices, CorrectIndex, ImagePath, tags, Difficulty);

        public QuestionFormData WithDifficulty(int difficulty)
            => new QuestionFormData(Type, Text, ReadingText, Answers, Choices, CorrectIndex, ImagePath, Tags, difficulty);

        /// <summary>
        /// 指定インデックスの <see cref="Answers"/> を差し替える（1件分のテキスト編集用）。
        /// </summary>
        public QuestionFormData WithAnswerAt(int index, string value)
            => WithAnswers(ReplaceAt(Answers, index, value));

        /// <summary>
        /// 指定インデックスの <see cref="Choices"/> を差し替える（1件分のテキスト編集用）。
        /// </summary>
        public QuestionFormData WithChoiceAt(int index, string value)
            => WithChoices(ReplaceAt(Choices, index, value));

        private static IReadOnlyList<string> ReplaceAt(IReadOnlyList<string> source, int index, string value)
        {
            if (index < 0 || index >= source.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index), index, $"0 以上 {source.Count} 未満でなければなりません（境界での入力検証。CLAUDE.md）。");
            }

            var result = source.ToArray();
            result[index] = value;
            return result;
        }

        /// <summary>
        /// 保存前バリデーションの対象となる値がすべて等しいかどうかを比較する（issue #32、PR #93 レビュー M4）。
        /// 「別の問題を選択した／再読込した際に、実際に入力値が変わっていた場合だけ破棄の警告を出す」判定に使う。
        /// </summary>
        public bool HasSameValuesAs(QuestionFormData other)
        {
            if (other == null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Type == other.Type
                && Text == other.Text
                && ReadingText == other.ReadingText
                && Answers.SequenceEqual(other.Answers)
                && Choices.SequenceEqual(other.Choices)
                && CorrectIndex == other.CorrectIndex
                && ImagePath == other.ImagePath
                && Tags.SequenceEqual(other.Tags)
                && Difficulty == other.Difficulty;
        }
    }
}
