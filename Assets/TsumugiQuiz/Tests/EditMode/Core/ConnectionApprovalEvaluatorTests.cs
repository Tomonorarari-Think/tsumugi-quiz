using NUnit.Framework;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 接続承認の判定（docs/network.md §2.3 の 1〜3、§9）のテスト。
    /// </summary>
    public class ConnectionApprovalEvaluatorTests
    {
        private static byte[] Payload(ushort protocolVersion, string playerName, string clientBuildHash = "")
        {
            Assert.IsTrue(
                ConnectionPayloadCodec.TrySerialize(
                    new ConnectionPayload(protocolVersion, playerName, clientBuildHash),
                    out var bytes,
                    out _),
                "テスト用ペイロードの符号化に失敗しました。");
            return bytes;
        }

        [Test]
        public void Evaluate_ApprovesMatchingProtocolVersion()
        {
            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, "つむぎ"),
                connectedPlayerCount: 1,
                ConnectionApprovalPolicy.Default);

            Assert.IsTrue(decision.Approved);
            Assert.AreEqual(ConnectionRejectionReason.None, decision.Reason);
            Assert.AreEqual(string.Empty, decision.ReasonMessage);
            Assert.AreEqual("つむぎ", decision.Payload.PlayerName);
        }

        [Test]
        public void Evaluate_TrimsPlayerNameOnApproval()
        {
            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, "  つむぎ  "),
                connectedPlayerCount: 0,
                ConnectionApprovalPolicy.Default);

            Assert.IsTrue(decision.Approved);
            Assert.AreEqual("つむぎ", decision.Payload.PlayerName);
        }

        [Test]
        public void Evaluate_RejectsProtocolVersionMismatch_WithReason()
        {
            var clientVersion = (ushort)(ProtocolConstants.Version + 1);

            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(clientVersion, "つむぎ"),
                connectedPlayerCount: 0,
                ConnectionApprovalPolicy.Default);

            Assert.IsFalse(decision.Approved);
            Assert.AreEqual(ConnectionRejectionReason.ProtocolVersionMismatch, decision.Reason);
            StringAssert.Contains(ProtocolConstants.Version.ToString(), decision.ReasonMessage);
            StringAssert.Contains(clientVersion.ToString(), decision.ReasonMessage);
        }

        [Test]
        public void Evaluate_RejectsMissingPayload()
        {
            var decision = ConnectionApprovalEvaluator.Evaluate(
                null,
                connectedPlayerCount: 0,
                ConnectionApprovalPolicy.Default);

            Assert.IsFalse(decision.Approved);
            Assert.AreEqual(ConnectionRejectionReason.PayloadMissing, decision.Reason);
            Assert.AreEqual(ConnectionRejectionMessages.InvalidPayload, decision.ReasonMessage);
        }

        [Test]
        public void Evaluate_RejectsOversizedPayload()
        {
            // バージョンは一致させたうえでサイズだけ超過させる（判定順序がバージョン → 形式のため）。
            var oversized = new byte[ProtocolConstants.MaxApprovalPayloadBytes + 1];
            oversized[0] = (byte)(ProtocolConstants.Version & 0xFF);
            oversized[1] = (byte)((ProtocolConstants.Version >> 8) & 0xFF);

            var decision = ConnectionApprovalEvaluator.Evaluate(
                oversized,
                connectedPlayerCount: 0,
                ConnectionApprovalPolicy.Default);

            Assert.IsFalse(decision.Approved);
            Assert.AreEqual(ConnectionRejectionReason.PayloadTooLarge, decision.Reason);
        }

        [Test]
        public void Evaluate_ReportsVersionMismatch_EvenWhenPayloadFormatIsUnknown()
        {
            // ペイロード形式そのものを変えた将来版クライアントを想定し、
            // 先頭 2 バイト（バージョン）以外はこちらの形式として成立しないバイト列を渡す。
            var futureVersion = (ushort)(ProtocolConstants.Version + 7);
            var unknownFormat = new byte[]
            {
                (byte)(futureVersion & 0xFF),
                (byte)((futureVersion >> 8) & 0xFF),
                0xFF, 0xFF, 0xFF,
            };

            var decision = ConnectionApprovalEvaluator.Evaluate(
                unknownFormat,
                connectedPlayerCount: 0,
                ConnectionApprovalPolicy.Default);

            Assert.IsFalse(decision.Approved);
            Assert.AreEqual(ConnectionRejectionReason.ProtocolVersionMismatch, decision.Reason);
            StringAssert.Contains(futureVersion.ToString(), decision.ReasonMessage);
        }

        [Test]
        public void Evaluate_RejectsPayloadShorterThanVersionField()
        {
            var decision = ConnectionApprovalEvaluator.Evaluate(
                new byte[] { 0x01 },
                connectedPlayerCount: 0,
                ConnectionApprovalPolicy.Default);

            Assert.IsFalse(decision.Approved);
            Assert.AreEqual(ConnectionRejectionReason.PayloadMalformed, decision.Reason);
            Assert.AreEqual(ConnectionRejectionMessages.InvalidPayload, decision.ReasonMessage);
        }

        [Test]
        public void Evaluate_RejectsEmptyPlayerName()
        {
            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, "   "),
                connectedPlayerCount: 0,
                ConnectionApprovalPolicy.Default);

            Assert.IsFalse(decision.Approved);
            Assert.AreEqual(ConnectionRejectionReason.InvalidPlayerName, decision.Reason);
            Assert.AreEqual(ConnectionRejectionMessages.InvalidPlayerName, decision.ReasonMessage);
        }

        [TestCase("つむ\u200Bぎ")]
        [TestCase("つむ\u202Eぎ")]
        [TestCase("つ\u0301\u0301\u0301\u0301\u0301")]
        public void Evaluate_RejectsPlayerNameWithHiddenCharacters(string playerName)
        {
            // #209: 見えない文字・5 個以上重ねた結合記号を含む名前は、承認時に InvalidPlayerName で拒否する。
            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, playerName),
                connectedPlayerCount: 0,
                ConnectionApprovalPolicy.Default);

            Assert.IsFalse(decision.Approved);
            Assert.AreEqual(ConnectionRejectionReason.InvalidPlayerName, decision.Reason);
            Assert.AreEqual(ConnectionRejectionMessages.InvalidPlayerName, decision.ReasonMessage);
        }

        [Test]
        public void Evaluate_RejectsControlCharacterInClientBuildHash()
        {
            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, "つむぎ", "abc\u0001"),
                connectedPlayerCount: 0,
                ConnectionApprovalPolicy.Default);

            Assert.IsFalse(decision.Approved);
            Assert.AreEqual(ConnectionRejectionReason.InvalidClientBuildHash, decision.Reason);
        }

        [Test]
        public void Evaluate_RejectsWhenRoomIsFull()
        {
            var policy = ConnectionApprovalPolicy.Default.WithMaxPlayers(2);

            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, "つむぎ"),
                connectedPlayerCount: 2,
                policy);

            Assert.IsFalse(decision.Approved);
            Assert.AreEqual(ConnectionRejectionReason.RoomFull, decision.Reason);
            Assert.AreEqual(ConnectionRejectionMessages.RoomFull, decision.ReasonMessage);
        }

        [Test]
        public void Evaluate_ApprovesWhenRoomHasSpace()
        {
            var policy = ConnectionApprovalPolicy.Default.WithMaxPlayers(2);

            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, "つむぎ"),
                connectedPlayerCount: 1,
                policy);

            Assert.IsTrue(decision.Approved);
        }

        [Test]
        public void Evaluate_WithoutMaxPlayers_DoesNotRejectOnCount()
        {
            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, "つむぎ"),
                connectedPlayerCount: 999,
                ConnectionApprovalPolicy.Default);

            Assert.IsTrue(decision.Approved);
        }

        [Test]
        public void Evaluate_ChecksProtocolVersionBeforePlayerName()
        {
            // バージョン不一致と名前不正が同時に起きたら、バージョン不一致を優先して返す。
            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload((ushort)(ProtocolConstants.Version + 1), string.Empty),
                connectedPlayerCount: 0,
                ConnectionApprovalPolicy.Default);

            Assert.AreEqual(ConnectionRejectionReason.ProtocolVersionMismatch, decision.Reason);
        }

        [Test]
        public void Evaluate_RejectionMessageDoesNotLeakInternals()
        {
            // バージョンは一致しているが長さが足りないペイロード。
            var malformed = new byte[]
            {
                (byte)(ProtocolConstants.Version & 0xFF),
                (byte)((ProtocolConstants.Version >> 8) & 0xFF),
            };

            var decision = ConnectionApprovalEvaluator.Evaluate(
                malformed,
                connectedPlayerCount: 0,
                ConnectionApprovalPolicy.Default);

            Assert.IsFalse(decision.Approved);
            Assert.AreEqual(ConnectionRejectionMessages.InvalidPayload, decision.ReasonMessage);
        }
    }
}
