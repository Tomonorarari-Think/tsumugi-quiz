using System;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// クライアントが保存する再接続トークン 1 件分（不変値、issue #69）。
    /// 保存先のファイル形式（<c>session-token.json</c>）は Network 層の実装が決める。
    /// </summary>
    public readonly struct SessionTokenRecord
    {
        /// <summary>
        /// レコードを作る。
        /// </summary>
        /// <param name="hostKey">ホストの識別子（<see cref="SessionTokenHostKey"/>）。</param>
        /// <param name="token">ホストから受け取ったトークン。</param>
        /// <param name="expiresAtUtc">
        /// 有効期限（UTC）。これを過ぎたレコードは使わずに捨てる。
        /// <see cref="DateTimeKind.Unspecified"/> は **UTC として解釈する**（レビュー L4。
        /// 保存ファイルから読んだ値・手編集された値の扱いを <c>JsonSessionTokenStorage</c> と揃える。
        /// ローカル時刻として解釈すると、同じ文字列が環境のタイムゾーンで別の時刻になってしまう）。
        /// </param>
        public SessionTokenRecord(string hostKey, SessionToken token, DateTime expiresAtUtc)
        {
            HostKey = hostKey ?? string.Empty;
            Token = token;
            ExpiresAtUtc = ToUtc(expiresAtUtc);
        }

        /// <summary>ホストの識別子（<c>アドレス:ポート</c>）。</summary>
        public string HostKey { get; }

        /// <summary>ホストから受け取ったトークン。</summary>
        public SessionToken Token { get; }

        /// <summary>有効期限（UTC）。</summary>
        public DateTime ExpiresAtUtc { get; }

        /// <summary>レコードとして妥当か（キーが正しく、トークンを保持している）。</summary>
        public bool IsValid => SessionTokenHostKey.IsValid(HostKey) && Token.HasValue;

        /// <summary>
        /// 指定時刻（UTC）の時点で有効か。
        /// <paramref name="nowUtc"/> の <see cref="DateTimeKind.Unspecified"/> も UTC として解釈する（L4）。
        /// </summary>
        public bool IsAliveAt(DateTime nowUtc) => IsValid && ExpiresAtUtc > ToUtc(nowUtc);

        /// <summary>
        /// UTC へ正規化する。<see cref="DateTimeKind.Unspecified"/> は UTC とみなし、
        /// <see cref="DateTimeKind.Local"/> だけ変換する（L4）。
        /// </summary>
        private static DateTime ToUtc(DateTime value)
        {
            switch (value.Kind)
            {
                case DateTimeKind.Utc:
                    return value;
                case DateTimeKind.Local:
                    return value.ToUniversalTime();
                default:
                    return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }
        }
    }
}
