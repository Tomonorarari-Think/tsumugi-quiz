using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 承認ペイロードの符号化・復号（docs/network.md §2.3 / §9）のテスト。
    /// 再接続トークン（issue #69）の有無・不正長も含む。
    /// </summary>
    public class ConnectionPayloadCodecTests
    {
        /// <summary>ホストが発行するのと同じ 128bit トークンを作る。</summary>
        private static SessionToken CreateToken()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                return SessionToken.CreateRandom(rng);
            }
        }

        [Test]
        public void Serialize_Then_Deserialize_RoundTrips()
        {
            var original = new ConnectionPayload(7, "つむぎ", "abc123");

            Assert.IsTrue(ConnectionPayloadCodec.TrySerialize(original, out var bytes, out var serializeReason));
            Assert.AreEqual(ConnectionRejectionReason.None, serializeReason);
            Assert.IsTrue(ConnectionPayloadCodec.TryDeserialize(bytes, out var restored, out var deserializeReason));
            Assert.AreEqual(ConnectionRejectionReason.None, deserializeReason);

            Assert.AreEqual(original.ProtocolVersion, restored.ProtocolVersion);
            Assert.AreEqual(original.PlayerName, restored.PlayerName);
            Assert.AreEqual(original.ClientBuildHash, restored.ClientBuildHash);
        }

        [Test]
        public void Serialize_EmptyStrings_ProducesHeaderOnly()
        {
            var payload = new ConnectionPayload(1, string.Empty, string.Empty);

            Assert.IsTrue(ConnectionPayloadCodec.TrySerialize(payload, out var bytes, out _));

            // protocolVersion(2) + 長さバイト 3 個（名前・ビルドハッシュ・トークン） = 5 バイト。
            Assert.AreEqual(5, bytes.Length);
        }

        [Test]
        public void Serialize_UsesLittleEndianProtocolVersion()
        {
            var payload = new ConnectionPayload(0x0102, string.Empty, string.Empty);

            Assert.IsTrue(ConnectionPayloadCodec.TrySerialize(payload, out var bytes, out _));

            Assert.AreEqual(0x02, bytes[0]);
            Assert.AreEqual(0x01, bytes[1]);
        }

        [Test]
        public void Serialize_StaysWithinPayloadLimit_ForMaxLengthName()
        {
            // 16 文字すべてを 3 バイトの日本語にした最悪ケース。
            var longestName = new string('あ', ProtocolConstants.MaxPlayerNameLength);
            var longestHash = new string('a', ProtocolConstants.MaxClientBuildHashBytes);
            var payload = new ConnectionPayload(ProtocolConstants.Version, longestName, longestHash);

            Assert.IsTrue(ConnectionPayloadCodec.TrySerialize(payload, out var bytes, out _));
            Assert.LessOrEqual(bytes.Length, ProtocolConstants.MaxApprovalPayloadBytes);
        }

        [Test]
        public void Serialize_RejectsOversizedClientBuildHash()
        {
            var payload = new ConnectionPayload(1, "name", new string('a', ProtocolConstants.MaxClientBuildHashBytes + 1));

            Assert.IsFalse(ConnectionPayloadCodec.TrySerialize(payload, out _, out var reason));
            Assert.AreEqual(ConnectionRejectionReason.InvalidClientBuildHash, reason);
        }

        [Test]
        public void Deserialize_RejectsNullOrEmpty()
        {
            Assert.IsFalse(ConnectionPayloadCodec.TryDeserialize(null, out _, out var nullReason));
            Assert.AreEqual(ConnectionRejectionReason.PayloadMissing, nullReason);

            Assert.IsFalse(ConnectionPayloadCodec.TryDeserialize(new byte[0], out _, out var emptyReason));
            Assert.AreEqual(ConnectionRejectionReason.PayloadMissing, emptyReason);
        }

        [Test]
        public void Deserialize_RejectsOversizedPayload()
        {
            var bytes = new byte[ProtocolConstants.MaxApprovalPayloadBytes + 1];

            Assert.IsFalse(ConnectionPayloadCodec.TryDeserialize(bytes, out _, out var reason));
            Assert.AreEqual(ConnectionRejectionReason.PayloadTooLarge, reason);
        }

        [Test]
        public void Deserialize_RejectsTruncatedPayload()
        {
            Assert.IsTrue(ConnectionPayloadCodec.TrySerialize(new ConnectionPayload(1, "name", "hash"), out var bytes, out _));
            var truncated = new byte[bytes.Length - 1];
            System.Array.Copy(bytes, truncated, truncated.Length);

            Assert.IsFalse(ConnectionPayloadCodec.TryDeserialize(truncated, out _, out var reason));
            Assert.AreEqual(ConnectionRejectionReason.PayloadMalformed, reason);
        }

        [Test]
        public void Deserialize_RejectsTrailingBytes()
        {
            Assert.IsTrue(ConnectionPayloadCodec.TrySerialize(new ConnectionPayload(1, "name", "hash"), out var bytes, out _));
            var padded = new byte[bytes.Length + 1];
            System.Array.Copy(bytes, padded, bytes.Length);

            Assert.IsFalse(ConnectionPayloadCodec.TryDeserialize(padded, out _, out var reason));
            Assert.AreEqual(ConnectionRejectionReason.PayloadMalformed, reason);
        }

        [Test]
        public void Deserialize_RejectsInconsistentNameLength()
        {
            Assert.IsTrue(ConnectionPayloadCodec.TrySerialize(new ConnectionPayload(1, "name", "hash"), out var bytes, out _));
            // 名前の長さを実際より大きく詐称する。
            bytes[2] = 200;

            Assert.IsFalse(ConnectionPayloadCodec.TryDeserialize(bytes, out _, out var reason));
            Assert.AreEqual(ConnectionRejectionReason.PayloadMalformed, reason);
        }

        [Test]
        public void Deserialize_RejectsInvalidUtf8()
        {
            var nameBytes = new byte[] { 0xC3, 0x28 }; // 不正な UTF-8 シーケンス
            var bytes = new byte[5 + nameBytes.Length];
            bytes[0] = 1;
            bytes[1] = 0;
            bytes[2] = (byte)nameBytes.Length;
            System.Array.Copy(nameBytes, 0, bytes, 3, nameBytes.Length);
            bytes[3 + nameBytes.Length] = 0;     // clientBuildHash の長さ
            bytes[4 + nameBytes.Length] = 0;     // reconnectToken の長さ

            Assert.IsFalse(ConnectionPayloadCodec.TryDeserialize(bytes, out _, out var reason));
            Assert.AreEqual(ConnectionRejectionReason.PayloadMalformed, reason);
        }

        [Test]
        public void Serialize_DoesNotThrowForDefaultPayload()
        {
            // default(ConnectionPayload) は文字列が null。例外を投げずに空文字として扱う。
            Assert.IsTrue(ConnectionPayloadCodec.TrySerialize(default, out var bytes, out var reason));
            Assert.AreEqual(ConnectionRejectionReason.None, reason);
            Assert.AreEqual(5, bytes.Length);

            Assert.IsTrue(ConnectionPayloadCodec.TryDeserialize(bytes, out var restored, out _));
            Assert.AreEqual(0, restored.ProtocolVersion);
            Assert.AreEqual(string.Empty, restored.PlayerName);
            Assert.AreEqual(string.Empty, restored.ClientBuildHash);
            Assert.IsFalse(restored.ReconnectToken.HasValue);
        }

        [Test]
        public void Serialize_RejectsLoneSurrogateWithoutThrowing()
        {
            // 単独サロゲートは UTF-8 にできない。例外ではなく拒否理由で返すこと。
            var loneHighSurrogate = "ab" + (char)0xD800 + "cd";
            var payload = new ConnectionPayload(1, loneHighSurrogate, string.Empty);

            Assert.IsFalse(ConnectionPayloadCodec.TrySerialize(payload, out var bytes, out var reason));
            Assert.AreEqual(ConnectionRejectionReason.InvalidPlayerName, reason);
            Assert.AreEqual(0, bytes.Length);
        }

        [Test]
        public void Serialize_RejectsLoneSurrogateInClientBuildHashWithoutThrowing()
        {
            var loneLowSurrogate = "ab" + (char)0xDC00 + "cd";
            var payload = new ConnectionPayload(1, "name", loneLowSurrogate);

            Assert.IsFalse(ConnectionPayloadCodec.TrySerialize(payload, out _, out var reason));
            Assert.AreEqual(ConnectionRejectionReason.InvalidClientBuildHash, reason);
        }

        [Test]
        public void TryReadProtocolVersion_ReadsFirstTwoBytes()
        {
            Assert.IsTrue(ConnectionPayloadCodec.TrySerialize(new ConnectionPayload(0x0201, "name", "hash"), out var bytes, out _));

            Assert.IsTrue(ConnectionPayloadCodec.TryReadProtocolVersion(bytes, out var version));
            Assert.AreEqual(0x0201, version);
        }

        [Test]
        public void TryReadProtocolVersion_ReadsVersionEvenFromUnknownFormat()
        {
            // 形式が違っても先頭 2 バイトだけは読める（承認判定でバージョンを先に見るため）。
            Assert.IsTrue(ConnectionPayloadCodec.TryReadProtocolVersion(new byte[] { 0x09, 0x00, 0xFF }, out var version));
            Assert.AreEqual(9, version);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void TryReadProtocolVersion_FailsForTooShortPayload(int length)
        {
            Assert.IsFalse(ConnectionPayloadCodec.TryReadProtocolVersion(new byte[length], out var version));
            Assert.AreEqual(0, version);
        }

        [Test]
        public void TryReadProtocolVersion_FailsForNull()
        {
            Assert.IsFalse(ConnectionPayloadCodec.TryReadProtocolVersion(null, out _));
        }

        [Test]
        public void Serialize_Then_Deserialize_RoundTripsReconnectToken()
        {
            // issue #69: トークンを載せたペイロードが往復すること。
            var token = CreateToken();
            var original = new ConnectionPayload(ProtocolConstants.Version, "つむぎ", "abc123", token);

            Assert.IsTrue(ConnectionPayloadCodec.TrySerialize(original, out var bytes, out var serializeReason));
            Assert.AreEqual(ConnectionRejectionReason.None, serializeReason);
            Assert.IsTrue(ConnectionPayloadCodec.TryDeserialize(bytes, out var restored, out _));

            Assert.IsTrue(restored.ReconnectToken.HasValue);
            Assert.AreEqual(token, restored.ReconnectToken);
            Assert.AreEqual(original.PlayerName, restored.PlayerName);
        }

        [Test]
        public void Serialize_WithoutToken_WritesZeroLengthSection()
        {
            var payload = new ConnectionPayload(ProtocolConstants.Version, "name", "hash");

            Assert.IsTrue(ConnectionPayloadCodec.TrySerialize(payload, out var bytes, out _));
            Assert.AreEqual(0, bytes[bytes.Length - 1], "トークン無しのときは長さ 0 のバイトで終わる。");

            Assert.IsTrue(ConnectionPayloadCodec.TryDeserialize(bytes, out var restored, out _));
            Assert.IsFalse(restored.ReconnectToken.HasValue);
        }

        [TestCase(1)]
        [TestCase(8)]
        [TestCase(15)]
        [TestCase(17)]
        public void Deserialize_RejectsWrongTokenLength(int tokenLength)
        {
            // issue #69: トークン区画は「0 バイト」か「16 バイトちょうど」だけを受け付ける。
            var bytes = new byte[5 + tokenLength];
            bytes[0] = (byte)(ProtocolConstants.Version & 0xFF);
            bytes[1] = (byte)((ProtocolConstants.Version >> 8) & 0xFF);
            bytes[2] = 0;                  // playerName は空
            bytes[3] = 0;                  // clientBuildHash は空
            bytes[4] = (byte)tokenLength;  // 詐称したトークン長

            Assert.IsFalse(ConnectionPayloadCodec.TryDeserialize(bytes, out _, out var reason));
            Assert.AreEqual(ConnectionRejectionReason.InvalidReconnectToken, reason);
        }

        [Test]
        public void Deserialize_RejectsTokenLengthLongerThanPayload()
        {
            // 長さだけ 16 と名乗って実体が足りないケースは「形式が壊れている」として弾く。
            var bytes = new byte[5 + 4];
            bytes[0] = (byte)(ProtocolConstants.Version & 0xFF);
            bytes[1] = (byte)((ProtocolConstants.Version >> 8) & 0xFF);
            bytes[2] = 0;
            bytes[3] = 0;
            bytes[4] = SessionToken.ByteCount;

            Assert.IsFalse(ConnectionPayloadCodec.TryDeserialize(bytes, out _, out var reason));
            Assert.AreEqual(ConnectionRejectionReason.PayloadMalformed, reason);
        }

        [Test]
        public void Serialize_WithTokenStaysWithinPayloadLimit()
        {
            var longestName = new string('あ', ProtocolConstants.MaxPlayerNameLength);
            var longestHash = new string('a', ProtocolConstants.MaxClientBuildHashBytes);
            var payload = new ConnectionPayload(
                ProtocolConstants.Version, longestName, longestHash, CreateToken());

            Assert.IsTrue(ConnectionPayloadCodec.TrySerialize(payload, out var bytes, out _));
            Assert.LessOrEqual(bytes.Length, ProtocolConstants.MaxApprovalPayloadBytes);
        }

        [Test]
        public void ProtocolVersion_WasBumpedForTheTokenSection()
        {
            // ペイロード形式を変えたらプロトコルバージョンを上げる規約（ProtocolConstants）。
            Assert.GreaterOrEqual(ProtocolConstants.Version, 2);
            Assert.AreEqual(SessionToken.ByteCount, ProtocolConstants.ReconnectTokenBytes);
        }

        [Test]
        public void Deserialize_AcceptsMultiByteName()
        {
            var name = "春日部つむぎ";
            Assert.IsTrue(ConnectionPayloadCodec.TrySerialize(new ConnectionPayload(3, name, string.Empty), out var bytes, out _));

            Assert.AreEqual(Encoding.UTF8.GetByteCount(name), bytes[2]);
            Assert.IsTrue(ConnectionPayloadCodec.TryDeserialize(bytes, out var restored, out _));
            Assert.AreEqual(name, restored.PlayerName);
        }
    }
}
