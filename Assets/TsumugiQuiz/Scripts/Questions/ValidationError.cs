namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// <see cref="QuestionSetValidator"/> が検出した検証エラー1件分。
    /// </summary>
    public sealed class ValidationError
    {
        /// <summary>エラー内容（日本語メッセージ）。</summary>
        public string Message { get; }

        /// <summary>問題単位のエラーの場合、対象の問題 ID。セット全体に関するエラーの場合は null。</summary>
        public string QuestionId { get; }

        public ValidationError(string message, string questionId = null)
        {
            Message = message;
            QuestionId = questionId;
        }

        public override string ToString()
            => QuestionId == null ? Message : $"[id={QuestionId}] {Message}";
    }
}
