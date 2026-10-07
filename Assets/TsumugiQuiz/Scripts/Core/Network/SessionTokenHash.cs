using System;
using System.Security.Cryptography;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 再接続トークン（<see cref="SessionToken"/>）の SHA-256 ハッシュ（不変値、issue #69）。
    ///
    /// サーバーは名簿（<see cref="LobbyRoster"/>）にトークン本体ではなくハッシュだけを持つ。
    /// 名簿はホストのメモリ上にしか無いが、ログ出力・デバッグ表示・将来の永続化で
    /// トークン本体が漏れると席を乗っ取られるため、突き合わせに必要な最小限だけを保持する。
    /// </summary>
    /// <remarks>
    /// 32 バイトを 4 個の <see cref="ulong"/> として保持する（配列を持つと不変にできないため）。
    /// 比較は途中で打ち切らない（タイミング攻撃を避けるため全ワードを XOR してから 0 と比べる）。
    /// </remarks>
    public readonly struct SessionTokenHash : IEquatable<SessionTokenHash>
    {
        /// <summary>SHA-256 のバイト数。</summary>
        public const int ByteCount = 32;

        private readonly ulong _w0;
        private readonly ulong _w1;
        private readonly ulong _w2;
        private readonly ulong _w3;
        private readonly bool _hasValue;

        private SessionTokenHash(ulong w0, ulong w1, ulong w2, ulong w3)
        {
            _w0 = w0;
            _w1 = w1;
            _w2 = w2;
            _w3 = w3;
            _hasValue = true;
        }

        /// <summary>ハッシュ無し（トークンを発行していないエントリ）を表す値。</summary>
        public static SessionTokenHash None => default;

        /// <summary>ハッシュを保持していないか。</summary>
        public bool IsEmpty => !_hasValue;

        /// <summary>
        /// トークンのハッシュを計算する。トークン無し（<see cref="SessionToken.HasValue"/> が false）なら
        /// <see cref="None"/> を返す。
        /// </summary>
        public static SessionTokenHash Of(SessionToken token)
        {
            if (!token.HasValue)
            {
                return None;
            }

            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(token.ToBytes());
                return new SessionTokenHash(
                    ReadUInt64(hash, 0),
                    ReadUInt64(hash, 8),
                    ReadUInt64(hash, 16),
                    ReadUInt64(hash, 24));
            }
        }

        /// <summary>
        /// 提示されたトークンがこのハッシュに対応するかを判定する。
        /// ハッシュ無し・トークン無しのときは必ず false（「トークンが無ければ復帰できない」）。
        /// </summary>
        public bool Matches(SessionToken token)
        {
            if (!_hasValue || !token.HasValue)
            {
                return false;
            }

            return Equals(Of(token));
        }

        /// <inheritdoc />
        public bool Equals(SessionTokenHash other)
        {
            if (!_hasValue || !other._hasValue)
            {
                return _hasValue == other._hasValue;
            }

            // 途中で打ち切らずに全ワードを比較する（比較時間から一致バイト数を推測させない）。
            var diff = (_w0 ^ other._w0) | (_w1 ^ other._w1) | (_w2 ^ other._w2) | (_w3 ^ other._w3);
            return diff == 0UL;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is SessionTokenHash other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _hasValue ? (_w0 ^ _w1 ^ _w2 ^ _w3).GetHashCode() : 0;

        /// <summary>ログ・デバッガ向けの表現（ハッシュ値そのものは出さない）。</summary>
        public override string ToString() => _hasValue ? "SessionTokenHash(設定済み)" : "SessionTokenHash(なし)";

        private static ulong ReadUInt64(byte[] bytes, int offset)
        {
            ulong value = 0;
            for (var i = 0; i < sizeof(ulong); i++)
            {
                value |= (ulong)bytes[offset + i] << (8 * i);
            }

            return value;
        }
    }
}
