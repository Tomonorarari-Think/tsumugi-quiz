using System;
using System.Collections.Generic;
using TsumugiQuiz.Questions;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// PlayMode テスト用の <see cref="IQuestionSource"/>。指定した問題を並び順そのままで供給する。
    /// </summary>
    internal sealed class TestQuestionSource : IQuestionSource
    {
        private readonly Question[] _questions;

        public TestQuestionSource(params Question[] questions)
        {
            _questions = questions ?? Array.Empty<Question>();
        }

        /// <inheritdoc />
        public int Count => _questions.Length;

        /// <summary>freeText の問題を組み立てる。</summary>
        /// <param name="id">問題 ID。</param>
        /// <param name="text">問題文。</param>
        /// <param name="answers">正解候補（1 件以上）。</param>
        /// <returns>問題。</returns>
        public static Question FreeText(string id, string text, params string[] answers)
        {
            return new Question(id, QuestionType.FreeText, text, answers: answers);
        }

        /// <summary>読み上げテキスト・タグ・難易度を指定した freeText の問題を組み立てる。</summary>
        /// <param name="id">問題 ID。</param>
        /// <param name="text">問題文。</param>
        /// <param name="readingText">読み上げ用テキスト（null なら省略扱い）。</param>
        /// <param name="tags">タグ。</param>
        /// <param name="difficulty">難易度。</param>
        /// <param name="answers">正解候補。</param>
        /// <returns>問題。</returns>
        public static Question FreeText(
            string id,
            string text,
            string readingText,
            IReadOnlyList<string> tags,
            int difficulty,
            params string[] answers)
        {
            return new Question(
                id,
                QuestionType.FreeText,
                text,
                readingText,
                answers,
                tags: tags,
                difficulty: difficulty);
        }

        /// <summary>選択式（choice）の問題を組み立てる（#17）。</summary>
        /// <param name="id">問題 ID。</param>
        /// <param name="text">問題文。</param>
        /// <param name="correctIndex">正解の <c>choices</c> インデックス。</param>
        /// <param name="choices">選択肢（2〜8件）。</param>
        /// <returns>問題。</returns>
        public static Question Choice(string id, string text, int correctIndex, params string[] choices)
        {
            return new Question(id, QuestionType.Choice, text, choices: choices, correctIndex: correctIndex);
        }

        /// <inheritdoc />
        public bool TryGetQuestion(int index, out Question question)
        {
            if (index < 0 || index >= _questions.Length)
            {
                question = null;
                return false;
            }

            question = _questions[index];
            return true;
        }
    }
}
