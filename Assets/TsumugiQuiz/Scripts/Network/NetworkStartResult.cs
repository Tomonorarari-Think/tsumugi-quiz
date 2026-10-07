namespace TsumugiQuiz.Network
{
    /// <summary>
    /// ホスト開始 / クライアント接続開始の結果（不変）。
    /// 失敗時の <see cref="Message"/> はそのまま UI に出せる日本語の定型文にする。
    /// </summary>
    public readonly struct NetworkStartResult
    {
        private NetworkStartResult(bool success, ushort port, string message)
        {
            Success = success;
            Port = port;
            Message = message;
        }

        /// <summary>開始できたか。</summary>
        public bool Success { get; }

        /// <summary>実際に使用したポート（失敗時は 0）。</summary>
        public ushort Port { get; }

        /// <summary>失敗理由（成功時は空文字）。</summary>
        public string Message { get; }

        /// <summary>成功結果を作る。</summary>
        public static NetworkStartResult Ok(ushort port) => new NetworkStartResult(true, port, string.Empty);

        /// <summary>失敗結果を作る。</summary>
        public static NetworkStartResult Fail(string message) => new NetworkStartResult(false, 0, message ?? string.Empty);
    }
}
