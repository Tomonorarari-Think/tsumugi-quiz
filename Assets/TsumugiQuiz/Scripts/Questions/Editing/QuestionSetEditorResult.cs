namespace TsumugiQuiz.Questions.Editing
{
    /// <summary>
    /// <see cref="QuestionSetEditorService"/> の各操作（新規作成・削除・複製・問題の追加/削除/並び替え）の結果。
    /// 成功時は更新後の <see cref="Entry"/>（削除の場合は null）を、失敗時はユーザー向けの
    /// <see cref="ErrorMessage"/> を持つ（CLAUDE.md「エラーを握りつぶさない」）。
    /// </summary>
    public sealed class QuestionSetEditorResult
    {
        public bool Success { get; }
        public string ErrorMessage { get; }
        public QuestionSetFileEntry Entry { get; }

        private QuestionSetEditorResult(bool success, string errorMessage, QuestionSetFileEntry entry)
        {
            Success = success;
            ErrorMessage = errorMessage;
            Entry = entry;
        }

        public static QuestionSetEditorResult Ok(QuestionSetFileEntry entry) => new QuestionSetEditorResult(true, null, entry);

        public static QuestionSetEditorResult Fail(string errorMessage) => new QuestionSetEditorResult(false, errorMessage, null);
    }
}
