namespace TsumugiQuiz.Questions.Images
{
    /// <summary>問題画像として受け入れる形式（docs/question-data.md §2: PNG / JPG のみ）。</summary>
    public enum ImageFormat
    {
        /// <summary>PNG でも JPEG でもない（受け入れない）。</summary>
        Unknown = 0,

        /// <summary>PNG。</summary>
        Png = 1,

        /// <summary>JPEG（JPG）。</summary>
        Jpeg = 2,
    }
}
