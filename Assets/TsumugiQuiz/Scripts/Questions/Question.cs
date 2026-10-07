using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// 問題1件分の不変データ。docs/question-data.md §1 参照。
    /// Newtonsoft.Json によって JSON からこのクラスへ直接デシリアライズされる
    /// （コンストラクタ引数と JSON の camelCase プロパティ名が一致することで解決される）。
    /// バリデーション（必須項目・文字数・件数など）は <see cref="QuestionSetValidator"/> が担う。
    /// </summary>
    public sealed class Question
    {
        /// <summary>既定の難易度（docs/question-data.md §1: 省略時は3）。</summary>
        public const int DefaultDifficulty = 3;

        public string Id { get; }

        /// <summary>
        /// 出題形式。JSON で省略された場合は null（<see cref="QuestionSetValidator"/> が必須項目エラーとして検出する）。
        /// </summary>
        public QuestionType? Type { get; }

        public string Text { get; }
        public string ReadingText { get; }
        public IReadOnlyList<string> Answers { get; }
        public IReadOnlyList<string> Choices { get; }
        public int? CorrectIndex { get; }
        public string ImagePath { get; }
        public IReadOnlyList<string> Tags { get; }
        public int Difficulty { get; }

        /// <summary>
        /// 読み上げ用テキスト。<see cref="ReadingText"/> が省略されている場合は <see cref="Text"/> にフォールバックする。
        /// </summary>
        /// <remarks>
        /// 計算プロパティのため JSON への書き出し対象から除外する（#30 <c>QuestionSetSerializer</c>。
        /// 含めてしまうと <c>QuestionRepository</c> の <c>MissingMemberHandling.Error</c> により
        /// 自分で書き出した JSON を自分で読み込めなくなる）。
        /// </remarks>
        [JsonIgnore]
        public string EffectiveReadingText => string.IsNullOrEmpty(ReadingText) ? Text : ReadingText;

        [JsonConstructor]
        public Question(
            string id,
            QuestionType? type,
            string text,
            string readingText = null,
            IReadOnlyList<string> answers = null,
            IReadOnlyList<string> choices = null,
            int? correctIndex = null,
            string imagePath = null,
            IReadOnlyList<string> tags = null,
            int? difficulty = null)
        {
            // 注意: Newtonsoft.Json 13 系は [JsonConstructor] のコンストラクタ引数に指定した
            // C# の既定値（例: difficulty = 3）を、JSON 側にプロパティが無い場合に適用しない
            // （value 型は default(int) = 0 になる）。そのため difficulty は int? で受け取り、
            // ここで明示的に既定値へフォールバックする。
            Id = id;
            Type = type;
            Text = text;
            ReadingText = readingText;
            Answers = answers?.ToArray() ?? Array.Empty<string>();
            Choices = choices?.ToArray() ?? Array.Empty<string>();
            CorrectIndex = correctIndex;
            ImagePath = imagePath;
            Tags = tags?.ToArray() ?? Array.Empty<string>();
            Difficulty = difficulty ?? DefaultDifficulty;
        }
    }
}
