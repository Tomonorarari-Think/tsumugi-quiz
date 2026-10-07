namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 日本語の文言に対応づけた切断理由（<see cref="DisconnectReasonLocalizer.Localize"/> の結果、issue #208）。
    /// 不変。
    /// </summary>
    public readonly struct LocalizedDisconnectReason
    {
        /// <summary>
        /// 結果を作る。
        /// </summary>
        /// <param name="message">画面に出す日本語の文言。</param>
        /// <param name="category">どう扱ったか。</param>
        public LocalizedDisconnectReason(string message, DisconnectReasonCategory category)
        {
            Message = message ?? string.Empty;
            Category = category;
        }

        /// <summary>画面に出す日本語の文言。空にならない（既定値のインスタンスを除く）。</summary>
        public string Message { get; }

        /// <summary>どう扱ったか。</summary>
        public DisconnectReasonCategory Category { get; }

        /// <summary>
        /// 元の理由をそのまま表示したか（<see cref="DisconnectReasonCategory.AppMessage"/>）。
        /// false なら元の理由とは別の文言にしたので、元の理由は詳細ログに残す。
        /// </summary>
        public bool IsOriginalShown => Category == DisconnectReasonCategory.AppMessage;
    }
}
