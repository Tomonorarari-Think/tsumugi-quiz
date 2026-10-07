namespace TsumugiQuiz.Network.Nat
{
    /// <summary>グローバル IP をどこから取得したか（docs/network-nat.md §2 の段）。</summary>
    public enum PublicIpSource
    {
        /// <summary>取得できなかった（手入力へ落とす）。</summary>
        None = 0,

        /// <summary>段 1: NAT デバイスの <c>GetExternalIPAsync</c>。</summary>
        NatDevice,

        /// <summary>段 2: HTTPS の IP 確認サービス。</summary>
        IpLookupService,
    }
}
