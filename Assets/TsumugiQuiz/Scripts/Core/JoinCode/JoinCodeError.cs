namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 参加コードの処理が失敗した理由。
    /// UI 側でメッセージを出し分けたり、ログに理由を残したりするために使う。
    /// </summary>
    public enum JoinCodeError
    {
        /// <summary>失敗していない（<see cref="JoinCodeCodec.TryDecode"/> が成功したときの値）。</summary>
        None = 0,

        /// <summary>正規化後の文字数が 12 文字ではない（docs/network-joincode.md §1.6 手順 5）。</summary>
        InvalidLength = 1,

        /// <summary>Crockford Base32 の 32 記号に含まれない文字が含まれている（§1.3）。</summary>
        InvalidCharacter = 2,

        /// <summary>チェック値が 1021〜1023 で、正規のエンコーダーが生成し得ない値である（§1.5）。</summary>
        CheckOutOfRange = 3,

        /// <summary>チェック値がペイロードと一致しない（§1.2）。</summary>
        ChecksumMismatch = 4,

        /// <summary>エンコード対象の IPv4 アドレスが不正である。</summary>
        InvalidAddress = 5,

        /// <summary>エンコード対象のポート番号が 0〜65535 の範囲外である。</summary>
        InvalidPort = 6,

        /// <summary>エンコード対象のバージョンが 0〜3（2bit）の範囲外である。</summary>
        InvalidVersion = 7,
    }
}
