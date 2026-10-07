namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 集計窓に積まれた早押し候補（docs/network.md §6.3）。
    /// </summary>
    public readonly struct BuzzCandidate
    {
        /// <summary>押下したクライアントの ID。</summary>
        public ulong ClientId { get; }

        /// <summary>受付開始時刻 T0 からの経過秒。小さいほど速い。常に 0 以上。</summary>
        public double Dt { get; }

        /// <summary>タイムスタンプに入った丸め補正（docs/network.md §6.4）。</summary>
        public BuzzClamp Clamp { get; }

        public BuzzCandidate(ulong clientId, double dt, BuzzClamp clamp)
        {
            ClientId = clientId;
            Dt = dt;
            Clamp = clamp;
        }
    }
}
