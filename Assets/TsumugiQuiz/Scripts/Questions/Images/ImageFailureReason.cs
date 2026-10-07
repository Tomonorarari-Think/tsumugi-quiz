namespace TsumugiQuiz.Questions.Images
{
    /// <summary>
    /// 問題画像の分割送信が失敗した理由（docs/network.md §8.4 の NAK に載せる値、#16）。
    /// クライアント → サーバーへ送るため、値は明示して固定する（バージョン差で意味が変わらないように）。
    /// </summary>
    /// <remarks>
    /// サーバーは <see cref="IsRetryable"/> が true の理由に対してのみ再送する（1 回だけ）。
    /// 再送しても結果が変わらない理由（形式・解像度・メタ情報の不正）で待たせ続けないため。
    /// </remarks>
    public enum ImageFailureReason
    {
        /// <summary>失敗していない（Ack 相当）。</summary>
        None = 0,

        /// <summary>チャンクが欠落したまま受信が止まった。</summary>
        MissingChunk = 1,

        /// <summary>全チャンクを受け取ったが SHA-256 が一致しなかった。</summary>
        HashMismatch = 2,

        /// <summary>チャンク番号・チャンク数・チャンク長が不正だった。</summary>
        InvalidChunk = 3,

        /// <summary>同じチャンク番号を内容違いで 2 度受け取った。</summary>
        DuplicateChunk = 4,

        /// <summary>総バイト数が上限（<see cref="QuestionLimits.MaxImageSizeBytes"/>）を超えた。</summary>
        SizeExceeded = 5,

        /// <summary>メタ情報を受け取っていない問題インデックスのチャンクが届いた。</summary>
        UnknownQuestion = 6,

        /// <summary>メタ情報（総バイト数・チャンク数・ハッシュ長）が不正だった。</summary>
        InvalidMeta = 7,

        /// <summary>PNG/JPG ではない、解像度が上限を超える、または復号に失敗した。</summary>
        DecodeFailed = 8,
    }

    /// <summary><see cref="ImageFailureReason"/> の補助。</summary>
    public static class ImageFailureReasons
    {
        /// <summary>
        /// 再送すれば直る可能性がある理由か。
        /// 形式・解像度・メタ情報の不正は同じデータを送り直しても直らないので false を返す。
        /// </summary>
        /// <param name="reason">失敗理由。</param>
        /// <returns>再送する価値があるなら true。</returns>
        public static bool IsRetryable(ImageFailureReason reason)
        {
            switch (reason)
            {
                case ImageFailureReason.MissingChunk:
                case ImageFailureReason.HashMismatch:
                case ImageFailureReason.InvalidChunk:
                case ImageFailureReason.DuplicateChunk:
                case ImageFailureReason.UnknownQuestion:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>ログ用の日本語の説明を返す（クライアントへは返さない、docs/network.md §9）。</summary>
        /// <param name="reason">失敗理由。</param>
        /// <returns>説明文。</returns>
        public static string Describe(ImageFailureReason reason)
        {
            switch (reason)
            {
                case ImageFailureReason.None:
                    return "成功";
                case ImageFailureReason.MissingChunk:
                    return "チャンクの欠落";
                case ImageFailureReason.HashMismatch:
                    return "ハッシュ不一致";
                case ImageFailureReason.InvalidChunk:
                    return "チャンク番号・長さの不正";
                case ImageFailureReason.DuplicateChunk:
                    return "同じチャンクの内容違いの重複";
                case ImageFailureReason.SizeExceeded:
                    return "総バイト数の上限超過";
                case ImageFailureReason.UnknownQuestion:
                    return "メタ情報の無い問題インデックス";
                case ImageFailureReason.InvalidMeta:
                    return "メタ情報の不正";
                case ImageFailureReason.DecodeFailed:
                    return "画像の復号失敗（形式・解像度）";
                default:
                    return $"不明な理由（{(int)reason}）";
            }
        }
    }
}
