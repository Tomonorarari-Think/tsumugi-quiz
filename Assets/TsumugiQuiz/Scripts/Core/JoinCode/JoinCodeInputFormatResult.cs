namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="JoinCodeInputFormatter.Format"/> の結果（不変）。
    /// </summary>
    public readonly struct JoinCodeInputFormatResult
    {
        public JoinCodeInputFormatResult(
            string displayText,
            int caretIndex,
            JoinCodeError? error,
            (int Version, string Ip, int Port)? decodedEndpoint)
        {
            DisplayText = displayText;
            CaretIndex = caretIndex;
            Error = error;
            DecodedEndpoint = decodedEndpoint;
        }

        /// <summary>入力欄にそのまま表示する整形済み文字列（ハイフン整形済み、12 文字超過分は切り捨て）。</summary>
        public string DisplayText { get; }

        /// <summary><see cref="DisplayText"/> 上でのキャレット位置（H-2）。</summary>
        public int CaretIndex { get; }

        /// <summary>
        /// 表示すべきエラー（<see cref="JoinCodeErrorMessages.Create"/> でメッセージに変換する）。
        /// 入力途中・デコード成功のいずれの場合も null。
        /// </summary>
        public JoinCodeError? Error { get; }

        /// <summary>デコードに成功した場合の接続先。失敗・入力途中の場合は null。</summary>
        public (int Version, string Ip, int Port)? DecodedEndpoint { get; }
    }
}
