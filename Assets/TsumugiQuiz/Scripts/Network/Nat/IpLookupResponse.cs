namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// IP 確認サービスへの 1 回のリクエスト結果（不変）。
    /// 通信失敗も例外にせず、この値で返す（docs/network-nat.md §2「段 2 が失敗しても例外を投げない」）。
    /// </summary>
    public readonly struct IpLookupResponse
    {
        private IpLookupResponse(bool success, long statusCode, string text, string error)
        {
            Success = success;
            StatusCode = statusCode;
            Text = text ?? string.Empty;
            Error = error ?? string.Empty;
        }

        /// <summary>HTTP 200 で本文を取得できたか。</summary>
        public bool Success { get; }

        /// <summary>HTTP ステータスコード（取得できなければ 0）。</summary>
        public long StatusCode { get; }

        /// <summary>応答本文（前後の空白は呼び出し側で処理する）。</summary>
        public string Text { get; }

        /// <summary>失敗理由（成功時は空文字）。ログ用。</summary>
        public string Error { get; }

        /// <summary>成功結果を作る。</summary>
        /// <param name="statusCode">HTTP ステータスコード。</param>
        /// <param name="text">応答本文。</param>
        public static IpLookupResponse Ok(long statusCode, string text) => new IpLookupResponse(true, statusCode, text, string.Empty);

        /// <summary>失敗結果を作る。</summary>
        /// <param name="error">失敗理由。</param>
        /// <param name="statusCode">HTTP ステータスコード（不明なら 0）。</param>
        public static IpLookupResponse Fail(string error, long statusCode = 0) => new IpLookupResponse(false, statusCode, string.Empty, error);
    }
}
