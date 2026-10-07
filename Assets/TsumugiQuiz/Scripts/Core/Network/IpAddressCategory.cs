namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// IPv4 アドレスの分類（docs/network-nat.md §1.6 の表）。
    /// <see cref="Public"/> 以外はインターネット越しの直接接続に使えない。
    /// </summary>
    public enum IpAddressCategory
    {
        /// <summary>IPv4 として解釈できない（空文字・書式不正・IPv6 など）。</summary>
        Invalid = 0,

        /// <summary>
        /// <c>0.0.0.0</c>。UPnP デバイスが WAN 側アドレスを持っていないときに返すことがある。
        /// </summary>
        Unspecified,

        /// <summary><c>127.0.0.0/8</c> ループバック。</summary>
        Loopback,

        /// <summary><c>10.0.0.0/8</c>, <c>172.16.0.0/12</c>, <c>192.168.0.0/16</c>（RFC 1918）。</summary>
        Private,

        /// <summary><c>100.64.0.0/10</c> CGNAT（RFC 6598 Shared Address Space）。</summary>
        CarrierGradeNat,

        /// <summary><c>169.254.0.0/16</c> リンクローカル（RFC 3927）。</summary>
        LinkLocal,

        /// <summary><c>224.0.0.0/4</c> マルチキャスト。</summary>
        Multicast,

        /// <summary><c>255.255.255.255</c> リミテッドブロードキャスト。</summary>
        Broadcast,

        /// <summary><c>0.0.0.0/8</c>, <c>240.0.0.0/4</c> 予約。</summary>
        Reserved,

        /// <summary>上記以外。インターネットから到達しうるアドレス。</summary>
        Public,
    }
}
