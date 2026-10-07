namespace TsumugiQuiz.Questions.Images
{
    /// <summary>
    /// <c>imagePath</c> の解決に失敗した理由（docs/question-data.md §2 の画像の規則、#16）。
    /// </summary>
    public enum ImagePathError
    {
        /// <summary>問題なし。</summary>
        None = 0,

        /// <summary><c>imagePath</c> が空。</summary>
        Empty = 1,

        /// <summary>絶対パスが指定された（セットフォルダの外を直接指すため拒否する）。</summary>
        Rooted = 2,

        /// <summary>拡張子が PNG / JPG ではない。</summary>
        UnsupportedExtension = 3,

        /// <summary>「../」等でセットフォルダの外を指している。</summary>
        OutsideBaseDirectory = 4,

        /// <summary>ファイルが存在しない。</summary>
        NotFound = 5,

        /// <summary>ファイルが上限（<see cref="QuestionLimits.MaxImageSizeBytes"/>）を超えている。</summary>
        TooLarge = 6,

        /// <summary>パスの形式が不正でファイルシステムに問い合わせられなかった。</summary>
        Invalid = 7,
    }
}
