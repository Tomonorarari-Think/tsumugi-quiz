using System;
using System.Text;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 承認ペイロード（<see cref="ConnectionPayload"/>）のバイト列への符号化・復号。
    /// NGO の <c>NetworkConfig.ConnectionData</c> はただの <c>byte[]</c> なので、形式は本アプリで決める。
    ///
    /// 形式（リトルエンディアン、全体 <see cref="ProtocolConstants.MaxApprovalPayloadBytes"/> バイト以下）:
    /// <code>
    /// offset 0 : ushort protocolVersion
    /// offset 2 : byte   playerNameByteCount
    /// offset 3 : byte[] playerName      (UTF-8)
    /// offset . : byte   clientBuildHashByteCount
    /// offset . : byte[] clientBuildHash (UTF-8)
    /// offset . : byte   reconnectTokenByteCount (0 または 16)
    /// offset . : byte[] reconnectToken  (issue #69。無い場合は 0 バイト)
    /// </code>
    /// JSON ではなく固定長ヘッダ + 長さ付き文字列にしているのは、
    /// 256 バイト上限（docs/network.md §9）を確実に守るためと、Core を Unity 非依存に保つため。
    ///
    /// 再接続トークンの区画を足した時点で形式が変わるため、
    /// <see cref="ProtocolConstants.Version"/> を 1 から 2 へ上げてある（旧クライアントは
    /// 「形式が不正」ではなく「バージョンが異なります」で拒否される）。
    /// </summary>
    public static class ConnectionPayloadCodec
    {
        /// <summary>protocolVersion(2) + 長さバイト 3 個（名前・ビルドハッシュ・トークン）。</summary>
        private const int HeaderBytes = 5;

        /// <summary>不正なバイト列を例外として検出する UTF-8 エンコーダ。</summary>
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>
        /// ペイロードをバイト列へ符号化する。
        /// </summary>
        /// <param name="payload">符号化するペイロード。</param>
        /// <param name="bytes">符号化結果。失敗時は空配列。</param>
        /// <param name="reason">失敗理由。成功時は <see cref="ConnectionRejectionReason.None"/>。</param>
        /// <returns>符号化できたら true。</returns>
        public static bool TrySerialize(ConnectionPayload payload, out byte[] bytes, out ConnectionRejectionReason reason)
        {
            bytes = Array.Empty<byte>();

            // default(ConnectionPayload) では文字列が null になりうるので空文字として扱う。
            // また単独サロゲートなど UTF-8 にできない文字列では StrictUtf8 が例外を投げるため、
            // ここで捕まえて拒否理由に変換する（この API は例外を投げない）。
            byte[] nameBytes;
            byte[] hashBytes;
            try
            {
                nameBytes = StrictUtf8.GetBytes(payload.PlayerName ?? string.Empty);
            }
            catch (EncoderFallbackException)
            {
                reason = ConnectionRejectionReason.InvalidPlayerName;
                return false;
            }

            try
            {
                hashBytes = StrictUtf8.GetBytes(payload.ClientBuildHash ?? string.Empty);
            }
            catch (EncoderFallbackException)
            {
                reason = ConnectionRejectionReason.InvalidClientBuildHash;
                return false;
            }

            if (nameBytes.Length > byte.MaxValue)
            {
                reason = ConnectionRejectionReason.InvalidPlayerName;
                return false;
            }

            if (hashBytes.Length > ProtocolConstants.MaxClientBuildHashBytes)
            {
                reason = ConnectionRejectionReason.InvalidClientBuildHash;
                return false;
            }

            var tokenBytes = payload.ReconnectToken.HasValue ? SessionToken.ByteCount : 0;
            var total = HeaderBytes + nameBytes.Length + hashBytes.Length + tokenBytes;
            if (total > ProtocolConstants.MaxApprovalPayloadBytes)
            {
                reason = ConnectionRejectionReason.PayloadTooLarge;
                return false;
            }

            var buffer = new byte[total];
            buffer[0] = (byte)(payload.ProtocolVersion & 0xFF);
            buffer[1] = (byte)((payload.ProtocolVersion >> 8) & 0xFF);
            buffer[2] = (byte)nameBytes.Length;
            Array.Copy(nameBytes, 0, buffer, 3, nameBytes.Length);
            var hashLengthOffset = 3 + nameBytes.Length;
            buffer[hashLengthOffset] = (byte)hashBytes.Length;
            Array.Copy(hashBytes, 0, buffer, hashLengthOffset + 1, hashBytes.Length);

            var tokenLengthOffset = hashLengthOffset + 1 + hashBytes.Length;
            buffer[tokenLengthOffset] = (byte)tokenBytes;
            if (tokenBytes > 0 && !payload.ReconnectToken.TryWriteTo(buffer, tokenLengthOffset + 1))
            {
                // TryWriteTo が失敗するのは領域計算を誤ったときだけ（到達しない想定）。
                reason = ConnectionRejectionReason.InvalidReconnectToken;
                return false;
            }

            bytes = buffer;
            reason = ConnectionRejectionReason.None;
            return true;
        }

        /// <summary>
        /// 先頭 2 バイトのプロトコルバージョンだけを読む。
        /// 形式全体が壊れていても（将来版のクライアントがペイロード形式を変えていても）
        /// バージョン不一致だけは正しく伝えられるようにするため、復号とは別に用意している。
        /// </summary>
        /// <param name="bytes">受信したバイト列。null 可。</param>
        /// <param name="protocolVersion">読み取れたバージョン。失敗時は 0。</param>
        /// <returns>2 バイト以上あり読み取れたら true。</returns>
        public static bool TryReadProtocolVersion(byte[] bytes, out ushort protocolVersion)
        {
            if (bytes == null || bytes.Length < 2)
            {
                protocolVersion = 0;
                return false;
            }

            protocolVersion = (ushort)(bytes[0] | (bytes[1] << 8));
            return true;
        }

        /// <summary>
        /// バイト列からペイロードを復号する。信用できない入力を受け取る境界なので、
        /// 長さ・UTF-8 の妥当性・余剰バイトをすべて検査する。
        /// </summary>
        /// <param name="bytes">受信したバイト列。null 可。</param>
        /// <param name="payload">復号結果。失敗時は既定値。</param>
        /// <param name="reason">失敗理由。成功時は <see cref="ConnectionRejectionReason.None"/>。</param>
        /// <returns>復号できたら true。</returns>
        public static bool TryDeserialize(byte[] bytes, out ConnectionPayload payload, out ConnectionRejectionReason reason)
        {
            payload = default;

            if (bytes == null || bytes.Length == 0)
            {
                reason = ConnectionRejectionReason.PayloadMissing;
                return false;
            }

            if (bytes.Length > ProtocolConstants.MaxApprovalPayloadBytes)
            {
                reason = ConnectionRejectionReason.PayloadTooLarge;
                return false;
            }

            if (bytes.Length < HeaderBytes)
            {
                reason = ConnectionRejectionReason.PayloadMalformed;
                return false;
            }

            var protocolVersion = (ushort)(bytes[0] | (bytes[1] << 8));

            int nameLength = bytes[2];
            var nameOffset = 3;
            var hashLengthOffset = nameOffset + nameLength;
            if (hashLengthOffset >= bytes.Length)
            {
                reason = ConnectionRejectionReason.PayloadMalformed;
                return false;
            }

            int hashLength = bytes[hashLengthOffset];
            var hashOffset = hashLengthOffset + 1;
            var tokenLengthOffset = hashOffset + hashLength;
            if (tokenLengthOffset >= bytes.Length)
            {
                // 長さ不足（トークン長のバイトまで届いていない）。
                reason = ConnectionRejectionReason.PayloadMalformed;
                return false;
            }

            if (hashLength > ProtocolConstants.MaxClientBuildHashBytes)
            {
                reason = ConnectionRejectionReason.InvalidClientBuildHash;
                return false;
            }

            int tokenLength = bytes[tokenLengthOffset];
            var tokenOffset = tokenLengthOffset + 1;
            if (tokenOffset + tokenLength != bytes.Length)
            {
                // 長さ不足も余剰バイトも不正として扱う。
                reason = ConnectionRejectionReason.PayloadMalformed;
                return false;
            }

            // トークンは「無し（0 バイト）」か「128bit ちょうど」のどちらかしか受け付けない（#69）。
            if (tokenLength != 0 && tokenLength != SessionToken.ByteCount)
            {
                reason = ConnectionRejectionReason.InvalidReconnectToken;
                return false;
            }

            var reconnectToken = SessionToken.None;
            if (tokenLength == SessionToken.ByteCount
                && !SessionToken.TryFromBytes(bytes, tokenOffset, out reconnectToken))
            {
                reason = ConnectionRejectionReason.InvalidReconnectToken;
                return false;
            }

            string playerName;
            string clientBuildHash;
            try
            {
                playerName = StrictUtf8.GetString(bytes, nameOffset, nameLength);
                clientBuildHash = StrictUtf8.GetString(bytes, hashOffset, hashLength);
            }
            catch (ArgumentException)
            {
                // 不正な UTF-8。例外の内容はクライアントに返さない。
                reason = ConnectionRejectionReason.PayloadMalformed;
                return false;
            }

            payload = new ConnectionPayload(protocolVersion, playerName, clientBuildHash, reconnectToken);
            reason = ConnectionRejectionReason.None;
            return true;
        }
    }
}
