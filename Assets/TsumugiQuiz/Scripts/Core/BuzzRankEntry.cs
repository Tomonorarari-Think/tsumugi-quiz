namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 1 回の早押し受付（集計窓）で押下した候補の順位の 1 件（#194、参加者パネルの押下順）。
    /// <see cref="BuzzResolution.Ranking"/> の要素で、並びがそのまま順位（先頭が 1 位 = 勝者）。
    /// </summary>
    /// <remarks>
    /// 順位を付けるのは集計窓の中で受理した押下だけ（docs/network.md §6.3）。窓が閉じた後の押下は
    /// 従来どおり棄却され、順位にも載らない（統括判断 #194: 案 X、規則は変えない）。
    /// </remarks>
    public readonly struct BuzzRankEntry
    {
        /// <summary>押下したクライアントの ID。</summary>
        public ulong ClientId { get; }

        /// <summary>受付開始時刻 T0 からの経過秒（補正後）。</summary>
        public double Dt { get; }

        /// <summary>
        /// 勝者と同着（差 &lt; tieEpsilon）で抽選の対象になったか。勝者自身も、抽選だった場合は true。
        /// </summary>
        public bool TiedWithWinner { get; }

        /// <summary>値を指定して生成する。</summary>
        /// <param name="clientId">押下したクライアントの ID。</param>
        /// <param name="dt">T0 からの経過秒。</param>
        /// <param name="tiedWithWinner">勝者と同着の抽選対象だったか。</param>
        public BuzzRankEntry(ulong clientId, double dt, bool tiedWithWinner)
        {
            ClientId = clientId;
            Dt = dt;
            TiedWithWinner = tiedWithWinner;
        }

        /// <summary>クライアント ID だけを差し替えた新しい値を返す（再接続の付け替え、#84）。</summary>
        /// <param name="clientId">新しいクライアント ID。</param>
        /// <returns>新しい値。</returns>
        public BuzzRankEntry WithClientId(ulong clientId) => new BuzzRankEntry(clientId, Dt, TiedWithWinner);
    }
}
