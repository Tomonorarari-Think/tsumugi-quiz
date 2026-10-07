namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="JoinCodeError"/> から、参加画面（Join View）にそのまま表示できる日本語の定型文を作る。
    /// M-2: <see cref="JoinCodeCodec"/> 内のメッセージ（<see cref="JoinCodeCodec.Decode"/> の例外メッセージ、
    /// <see cref="JoinCodeCodec.TryDecode(string, out (int, string, int), out JoinCodeError, out string)"/>
    /// の <c>message</c> 引数）はすべてここの定数を参照しており、本クラスが文言の単一の情報源になっている
    /// （docs/network-joincode.md §1.6・§1.5）。
    /// </summary>
    public static class JoinCodeErrorMessages
    {
        /// <summary>長さが 12 文字でないときの文言（§1.6 手順 5）。</summary>
        public const string InvalidLength = "参加コードの長さが違います（ハイフンを除いて12文字です）。";

        /// <summary>Crockford Base32 の 32 記号に含まれない文字があるときの文言（§1.3）。</summary>
        public const string InvalidCharacter = "参加コードに使えない文字が含まれています。";

        /// <summary>チェック値が不正、またはチェックが一致しないときの文言（§1.2・§1.5）。</summary>
        public const string InvalidChecksum = "参加コードが正しくありません。入力を確認してください。";

        /// <summary>
        /// 失敗理由に対応する表示用メッセージを返す。
        /// </summary>
        /// <param name="error">
        /// <see cref="JoinCodeCodec.TryDecode"/> が返す失敗理由。<see cref="JoinCodeError.None"/> なら空文字。
        /// </param>
        public static string Create(JoinCodeError error)
        {
            switch (error)
            {
                case JoinCodeError.None:
                    return string.Empty;
                case JoinCodeError.InvalidLength:
                    return InvalidLength;
                case JoinCodeError.InvalidCharacter:
                    return InvalidCharacter;
                case JoinCodeError.CheckOutOfRange:
                case JoinCodeError.ChecksumMismatch:
                    return InvalidChecksum;
                default:
                    // InvalidAddress / InvalidPort / InvalidVersion は Encode 側でしか発生しないが、
                    // 想定外の値が来ても UI を壊さないよう安全側の定型文を返す。
                    return InvalidChecksum;
            }
        }
    }
}
