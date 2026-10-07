namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// NAT デバイス操作の失敗種別。Mono.Nat の <c>ErrorCode</c> を層の外に漏らさないための分類。
    /// </summary>
    public enum NatFailureKind
    {
        /// <summary>原因が特定できない失敗。</summary>
        Unknown = 0,

        /// <summary>ルーターがマッピングを拒否した（UPnP が無効、権限なしなど）。</summary>
        Refused,

        /// <summary>別のマッピングと競合した（同じ外部ポートが既に使われている）。</summary>
        Conflict,

        /// <summary>ルーターがその操作に対応していない。</summary>
        Unsupported,

        /// <summary>通信に失敗した（応答なし、タイムアウトなど）。</summary>
        Network,
    }
}
