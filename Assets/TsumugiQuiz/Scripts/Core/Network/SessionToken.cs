using System;
using System.Security.Cryptography;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 再接続トークン（128bit の不変値、issue #69、docs/network.md §2.3 の K-N1）。
    ///
    /// ホストは接続を承認したクライアントごとにこのトークンを 1 つ発行し、クライアントは
    /// それを保存して再接続時に承認ペイロード（<see cref="ConnectionPayload"/>）へ載せる。
    /// ホストは名簿（<see cref="LobbyRoster"/>）にハッシュ（<see cref="SessionTokenHash"/>）だけを
    /// 持ち、「プレイヤー名の一致」だけでは席・得点へ復帰できないようにする。
    /// </summary>
    /// <remarks>
    /// 値は 16 バイトだが、配列を持つと「不変」を保てない（参照を渡すと外から書き換えられる）ため、
    /// 内部では 2 個の <see cref="ulong"/> として保持する。バイト列との相互変換はリトルエンディアン固定。
    /// <see cref="HasValue"/> を別に持つのは、乱数として全 0 が出た場合に「トークン無し」と
    /// 区別できるようにするため（<c>default(SessionToken)</c> は常にトークン無し）。
    /// </remarks>
    public readonly struct SessionToken : IEquatable<SessionToken>
    {
        /// <summary>トークンのバイト数（128bit）。</summary>
        public const int ByteCount = 16;

        /// <summary>16 進表記にしたときの文字数。</summary>
        public const int HexLength = ByteCount * 2;

        private const string HexDigits = "0123456789abcdef";

        private readonly ulong _low;
        private readonly ulong _high;
        private readonly bool _hasValue;

        private SessionToken(ulong low, ulong high)
        {
            _low = low;
            _high = high;
            _hasValue = true;
        }

        /// <summary>トークン無し（未発行 / 未保存）を表す値。</summary>
        public static SessionToken None => default;

        /// <summary>トークンを保持しているか。</summary>
        public bool HasValue => _hasValue;

        /// <summary>
        /// 暗号論的乱数から新しいトークンを作る（サーバーが承認時に呼ぶ）。
        /// </summary>
        /// <param name="rng">乱数源。呼び出し側が所有権を持つ（本メソッドは破棄しない）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="rng"/> が null。</exception>
        public static SessionToken CreateRandom(RandomNumberGenerator rng)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            var buffer = new byte[ByteCount];
            rng.GetBytes(buffer);
            return new SessionToken(ReadUInt64(buffer, 0), ReadUInt64(buffer, sizeof(ulong)));
        }

        /// <summary>
        /// バイト列からトークンを復元する（承認ペイロードの復号で使う。信用できない入力を受け取る境界）。
        /// </summary>
        /// <param name="bytes">読み取り元。null 可。</param>
        /// <param name="offset">読み取り開始位置。</param>
        /// <param name="token">復元したトークン。失敗時は <see cref="None"/>。</param>
        /// <returns>ちょうど <see cref="ByteCount"/> バイト読み取れたら true。</returns>
        public static bool TryFromBytes(byte[] bytes, int offset, out SessionToken token)
        {
            token = None;

            if (bytes == null || offset < 0 || offset > bytes.Length - ByteCount)
            {
                return false;
            }

            token = new SessionToken(ReadUInt64(bytes, offset), ReadUInt64(bytes, offset + sizeof(ulong)));
            return true;
        }

        /// <summary>
        /// 16 進文字列（32 文字、大文字小文字どちらでも可）からトークンを復元する。
        /// RPC で受け取った文字列など、信用できない入力の検証に使う（docs/network.md §9）。
        /// </summary>
        /// <param name="hex">16 進文字列。null 可。</param>
        /// <param name="token">復元したトークン。失敗時は <see cref="None"/>。</param>
        /// <returns>形式が正しければ true。</returns>
        public static bool TryParseHex(string hex, out SessionToken token)
        {
            token = None;

            if (hex == null || hex.Length != HexLength)
            {
                return false;
            }

            var buffer = new byte[ByteCount];
            for (var i = 0; i < ByteCount; i++)
            {
                if (!TryReadHexDigit(hex[i * 2], out var high) || !TryReadHexDigit(hex[(i * 2) + 1], out var low))
                {
                    return false;
                }

                buffer[i] = (byte)((high << 4) | low);
            }

            return TryFromBytes(buffer, 0, out token);
        }

        /// <summary>
        /// トークンを新しいバイト配列として取り出す（<see cref="HasValue"/> が false なら空配列）。
        /// 呼び出しごとに新しい配列を返すので、書き換えても元の値には影響しない。
        /// </summary>
        public byte[] ToBytes()
        {
            if (!_hasValue)
            {
                return Array.Empty<byte>();
            }

            var buffer = new byte[ByteCount];
            WriteUInt64(buffer, 0, _low);
            WriteUInt64(buffer, sizeof(ulong), _high);
            return buffer;
        }

        /// <summary>
        /// 既存のバッファへ書き出す（承認ペイロードの符号化で使う）。
        /// </summary>
        /// <param name="destination">書き出し先。</param>
        /// <param name="offset">書き出し開始位置。</param>
        /// <returns>書き出せたら true（トークン無し・領域不足なら false）。</returns>
        public bool TryWriteTo(byte[] destination, int offset)
        {
            if (!_hasValue || destination == null || offset < 0 || offset > destination.Length - ByteCount)
            {
                return false;
            }

            WriteUInt64(destination, offset, _low);
            WriteUInt64(destination, offset + sizeof(ulong), _high);
            return true;
        }

        /// <summary>16 進表記（小文字 32 文字）。トークン無しなら空文字。</summary>
        public string ToHex()
        {
            if (!_hasValue)
            {
                return string.Empty;
            }

            var bytes = ToBytes();
            var chars = new char[HexLength];
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = HexDigits[(bytes[i] >> 4) & 0xF];
                chars[(i * 2) + 1] = HexDigits[bytes[i] & 0xF];
            }

            return new string(chars);
        }

        /// <inheritdoc />
        public bool Equals(SessionToken other)
            => _hasValue == other._hasValue && _low == other._low && _high == other._high;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is SessionToken other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            if (!_hasValue)
            {
                return 0;
            }

            return (_low ^ _high).GetHashCode();
        }

        /// <summary>
        /// ログ・デバッガ向けの表現。**トークン本体は出さない**（漏洩すると席を奪われるため）。
        /// </summary>
        public override string ToString() => _hasValue ? "SessionToken(設定済み)" : "SessionToken(なし)";

        private static ulong ReadUInt64(byte[] bytes, int offset)
        {
            ulong value = 0;
            for (var i = 0; i < sizeof(ulong); i++)
            {
                value |= (ulong)bytes[offset + i] << (8 * i);
            }

            return value;
        }

        private static void WriteUInt64(byte[] bytes, int offset, ulong value)
        {
            for (var i = 0; i < sizeof(ulong); i++)
            {
                bytes[offset + i] = (byte)((value >> (8 * i)) & 0xFF);
            }
        }

        private static bool TryReadHexDigit(char c, out int value)
        {
            if (c >= '0' && c <= '9')
            {
                value = c - '0';
                return true;
            }

            if (c >= 'a' && c <= 'f')
            {
                value = (c - 'a') + 10;
                return true;
            }

            if (c >= 'A' && c <= 'F')
            {
                value = (c - 'A') + 10;
                return true;
            }

            value = 0;
            return false;
        }
    }
}
