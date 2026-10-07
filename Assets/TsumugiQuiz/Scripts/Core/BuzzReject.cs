namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 早押し押下（BuzzRpc）を棄却した理由。docs/network.md §6.4 の表に対応する。
    /// サーバーのログに残す用途を想定し、クライアントへはそのまま返さない。
    /// </summary>
    public enum BuzzReject
    {
        /// <summary>棄却していない（受理された）。</summary>
        None = 0,

        /// <summary>早押し受付フェーズ外（受付前・集計後・タイムアウト後）。</summary>
        NotOpen = 1,

        /// <summary>同一クライアントの 2 回目以降の押下。</summary>
        Duplicate = 2,

        /// <summary>お手つきペナルティ（次問休み）中のクライアント。</summary>
        Penalized = 3,

        /// <summary>報告された時刻が NaN または Infinity。</summary>
        NonFiniteTimestamp = 4,

        /// <summary>報告された時刻がサーバー受信時刻より 1 秒以上先（改竄か時刻破綻）。</summary>
        TooFarInFuture = 5,

        /// <summary>報告された時刻が受付開始 T0 より 50ms 以上前（改竄か時刻破綻）。</summary>
        TooFarInPast = 6,

        /// <summary>集計窓の締め切りを過ぎてから届いた押下。</summary>
        WindowClosed = 7,

        /// <summary>司会が一時停止中（#20、docs/tasks/setup-brief.md K18）。</summary>
        Paused = 8,
    }
}
