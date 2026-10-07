using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// クライアント側で再接続トークンを保持する純 C# ロジック（issue #69、docs/network.md §2.3）。
    /// ホストごとに 1 件だけ持ち、期限切れのレコードは読み書きのたびに捨てる。
    ///
    /// ファイルへの読み書きは <see cref="ISessionTokenStorage"/> の実装（Network 層）に委ねる。
    /// </summary>
    /// <remarks>
    /// トークンはホストのプロセス寿命の間だけ有効（ホストを立て直すと名簿ごと消える）。
    /// クライアント側の保存は「同じホストに入り直すまでの控え」でしかないので、
    /// 期限（既定 24 時間）を過ぎたものは使わずに捨てる。
    /// </remarks>
    public sealed class SessionTokenStore
    {
        /// <summary>保存したトークンの既定の有効期間（時間）。</summary>
        public const double DefaultLifetimeHours = 24.0;

        /// <summary>保持するレコードの最大件数（ファイルの肥大化防止。超えたら期限が近いものから捨てる）。</summary>
        public const int MaxRecordCount = 32;

        private readonly ISessionTokenStorage _storage;
        private readonly TimeSpan _lifetime;

        /// <summary>
        /// ストアを作る。
        /// </summary>
        /// <param name="storage">保存先。</param>
        /// <param name="lifetime">
        /// 保存したトークンの有効期間。null なら <see cref="DefaultLifetimeHours"/>。
        /// 0 以下は指定できない。
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="storage"/> が null。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetime"/> が 0 以下。</exception>
        public SessionTokenStore(ISessionTokenStorage storage, TimeSpan? lifetime = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));

            var effective = lifetime ?? TimeSpan.FromHours(DefaultLifetimeHours);
            if (effective <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(lifetime), effective, "有効期間は 0 より大きい必要があります。");
            }

            _lifetime = effective;
        }

        /// <summary>保存したトークンの有効期間。</summary>
        public TimeSpan Lifetime => _lifetime;

        /// <summary>
        /// 指定したホストのトークンを取り出す。期限切れ・未保存なら false。
        /// </summary>
        /// <param name="hostKey">ホストの識別子（<see cref="SessionTokenHostKey"/>）。</param>
        /// <param name="nowUtc">現在時刻（UTC）。</param>
        /// <param name="token">取り出したトークン。失敗時は <see cref="SessionToken.None"/>。</param>
        /// <returns>有効なトークンがあれば true。</returns>
        public bool TryGet(string hostKey, DateTime nowUtc, out SessionToken token)
        {
            token = SessionToken.None;

            if (!SessionTokenHostKey.IsValid(hostKey))
            {
                return false;
            }

            foreach (var record in LoadValid(nowUtc))
            {
                if (!string.Equals(record.HostKey, hostKey, StringComparison.Ordinal))
                {
                    continue;
                }

                token = record.Token;
                return true;
            }

            return false;
        }

        /// <summary>
        /// トークンを保存する（同じホストの既存レコードは置き換える）。
        /// 期限切れのレコードはこの機会にまとめて捨てる。
        /// </summary>
        /// <param name="hostKey">ホストの識別子。</param>
        /// <param name="token">保存するトークン。</param>
        /// <param name="nowUtc">現在時刻（UTC）。期限は <see cref="Lifetime"/> を足した時刻になる。</param>
        /// <returns>保存したら true（キーやトークンが不正なら false）。</returns>
        /// <remarks>
        /// 保存先（<see cref="ISessionTokenStorage"/>）が投げた例外はそのまま呼び出し側へ抜ける
        /// （レビュー M1。ここで握りつぶすと「保存した」と誤って報告してしまう）。
        /// </remarks>
        public bool Save(string hostKey, SessionToken token, DateTime nowUtc)
        {
            if (!SessionTokenHostKey.IsValid(hostKey) || !token.HasValue)
            {
                return false;
            }

            var utcNow = ToUtc(nowUtc);
            var records = new List<SessionTokenRecord>(LoadValid(utcNow));
            records.RemoveAll(record => string.Equals(record.HostKey, hostKey, StringComparison.Ordinal));
            records.Add(new SessionTokenRecord(hostKey, token, utcNow + _lifetime));

            TrimToMaxCount(records);

            _storage.Save(records);
            return true;
        }

        /// <summary>指定したホストのトークンを消す（拒否されたときなどに呼ぶ）。</summary>
        /// <returns>1 件以上消したら true。</returns>
        public bool Remove(string hostKey, DateTime nowUtc)
        {
            if (!SessionTokenHostKey.IsValid(hostKey))
            {
                return false;
            }

            var records = new List<SessionTokenRecord>(LoadValid(nowUtc));
            var removed = records.RemoveAll(record => string.Equals(record.HostKey, hostKey, StringComparison.Ordinal));
            if (removed == 0)
            {
                return false;
            }

            _storage.Save(records);
            return true;
        }

        /// <summary>
        /// 期限内かつ形式の正しいレコードだけを返す（保存内容は信用せず必ず検証する）。
        /// </summary>
        public IReadOnlyList<SessionTokenRecord> LoadValid(DateTime nowUtc)
        {
            var loaded = _storage.Load();
            if (loaded == null || loaded.Count == 0)
            {
                return Array.Empty<SessionTokenRecord>();
            }

            var utcNow = ToUtc(nowUtc);
            var valid = new List<SessionTokenRecord>(loaded.Count);
            foreach (var record in loaded)
            {
                if (record.IsAliveAt(utcNow))
                {
                    valid.Add(record);
                }
            }

            return valid;
        }

        private static void TrimToMaxCount(List<SessionTokenRecord> records)
        {
            if (records.Count <= MaxRecordCount)
            {
                return;
            }

            // 期限が近い（＝古い）ものから捨てる。
            records.Sort((left, right) => left.ExpiresAtUtc.CompareTo(right.ExpiresAtUtc));
            records.RemoveRange(0, records.Count - MaxRecordCount);
        }

        /// <summary>
        /// UTC へ正規化する。<see cref="DateTimeKind.Unspecified"/> は UTC とみなす
        /// （レビュー L4。<see cref="SessionTokenRecord"/> と同じ解釈にする）。
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
