namespace TsumugiQuiz.UI
{
    /// <summary>
    /// SE（効果音）の種類。<see cref="SeAssetPaths"/> で各種の wav ファイルと対応付ける。
    /// docs/tasks/setup-brief.md K17 で定義された 6 種類。
    /// </summary>
    public enum SeKind
    {
        /// <summary>早押し（ボタン押下時）。</summary>
        Buzz,

        /// <summary>正解。</summary>
        Correct,

        /// <summary>不正解。</summary>
        Wrong,

        /// <summary>タイムアップ（制限時間切れ）。</summary>
        TimeUp,

        /// <summary>ゲーム開始（開始ジングル）。</summary>
        Start,

        /// <summary>参加者の入室通知。</summary>
        Join,
    }
}
