namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 再接続トークンを保存するときの「どのホストからもらったトークンか」を表すキー（issue #69）。
    ///
    /// 参加コードはホストのアドレスとポートから復元できる（docs/network-joincode.md §1）ので、
    /// キーは正規化しやすい <c>アドレス:ポート</c> にする。参加コードを直接使うと、同じホストに
    /// LAN 経由と WAN 経由で入ったときに別のキーになってしまう。
    /// </summary>
    public static class SessionTokenHostKey
    {
        /// <summary>キーの最大長（保存ファイルの肥大化を防ぐための上限）。</summary>
        public const int MaxLength = 64;

        /// <summary>
        /// アドレスとポートからキーを組み立てる。
        /// </summary>
        /// <param name="address">ホストのアドレス（IPv4 / IPv6 / ホスト名）。</param>
        /// <param name="port">ホストのポート。0 は不可。</param>
        /// <param name="hostKey">組み立てたキー。失敗時は空文字。</param>
        /// <returns>キーを作れたら true。</returns>
        public static bool TryCreate(string address, ushort port, out string hostKey)
        {
            hostKey = string.Empty;

            if (string.IsNullOrWhiteSpace(address) || port == 0)
            {
                return false;
            }

            var normalized = address.Trim().ToLowerInvariant();
            if (normalized.Length == 0)
            {
                return false;
            }

            var candidate = normalized + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (candidate.Length > MaxLength)
            {
                return false;
            }

            hostKey = candidate;
            return true;
        }

        /// <summary>キーとして妥当か（保存ファイルから読んだ値の検証に使う）。</summary>
        public static bool IsValid(string hostKey)
            => !string.IsNullOrWhiteSpace(hostKey) && hostKey.Length <= MaxLength && hostKey.IndexOf(':') > 0;
    }
}
