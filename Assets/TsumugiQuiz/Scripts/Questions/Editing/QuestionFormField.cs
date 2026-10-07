namespace TsumugiQuiz.Questions.Editing
{
    /// <summary>
    /// 編集フォーム（issue #31）でバリデーションエラーをハイライトする対象フィールド。
    /// <see cref="QuestionFormValidation.MapField"/> が <see cref="ValidationError.Message"/> から推定する。
    /// </summary>
    public enum QuestionFormField
    {
        /// <summary>特定のフィールドに紐づかない、またはどのキーワードにも一致しないエラー。</summary>
        Other,
        Type,
        Text,
        ReadingText,
        Answers,
        Choices,
        CorrectIndex,
        ImagePath,
        Tags,
        Difficulty,
    }
}
