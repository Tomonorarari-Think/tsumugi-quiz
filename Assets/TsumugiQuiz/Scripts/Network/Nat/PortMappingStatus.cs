namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// 自動ポート開放の結果種別（docs/network-nat.md §1）。
    /// <see cref="Success"/> 以外はすべて手動ポート開放案内（§1.5）へ進む。
    /// </summary>
    public enum PortMappingStatus
    {
        /// <summary>まだ試していない。</summary>
        NotAttempted = 0,

        /// <summary><c>upnp.enabled</c> が false のため試していない。</summary>
        Disabled,

        /// <summary>マッピングを作成できた。</summary>
        Success,

        /// <summary>探索のタイムアウト内に NAT デバイスが応答しなかった。</summary>
        Timeout,

        /// <summary>探索は終わったが NAT デバイスを利用できなかった（UPnP / NAT-PMP が無効など）。</summary>
        DeviceNotFound,

        /// <summary>NAT デバイスがマッピングを拒否した（権限なし、ポート競合、未対応）。</summary>
        Refused,

        /// <summary>それ以外の失敗。</summary>
        Failed,
    }
}
