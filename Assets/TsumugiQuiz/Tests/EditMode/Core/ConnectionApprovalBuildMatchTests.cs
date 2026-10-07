using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 接続承認でビルドの一致を求める判定（#204、docs/network.md §2.3「バージョンとビルドの一致」）のテスト。
    /// ビルドの違う相手には、既存のバージョン不一致の書式で拒否の理由を返し、#208 以降のクライアントが日本語でそのまま出せること。
    /// </summary>
    public class ConnectionApprovalBuildMatchTests
    {
        private const string HostBuildHash = "0123456789abcdef0123456789abcdef";
        private const string OtherBuildHash = "fedcba9876543210fedcba9876543210";

        /// <summary>
        /// #208 と #209 のクライアントの <see cref="DisconnectReasonLocalizer"/> がバージョン不一致の理由と認める書式（凍結した写し）。
        /// 現行の <see cref="DisconnectReasonLocalizer"/> ではなく固定の文字列で確かめ、書式を変えたら落ちるようにする。
        /// </summary>
        private static readonly Regex PreviousReleaseVersionMismatchPattern = new Regex(
            @"\Aバージョンが異なります（ホスト: [0-9]{1,5} / あなた: [0-9]{1,5}）。\z", RegexOptions.CultureInvariant);

        private static ConnectionApprovalPolicy HostPolicy =>
            ConnectionApprovalPolicy.Default.WithExpectedClientBuildHash(HostBuildHash);

        private static byte[] Payload(ushort protocolVersion, string playerName, string clientBuildHash)
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
        public void Evaluate_ApprovesMatchingBuild()
        {
            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, "つむぎ", HostBuildHash), connectedPlayerCount: 0, HostPolicy);

            Assert.IsTrue(decision.Approved);
            Assert.AreEqual(HostBuildHash, decision.Payload.ClientBuildHash);
        }

        [Test]
        public void Evaluate_RejectsDifferentBuild_WithVersionMismatchFormat()
        {
            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, "つむぎ", OtherBuildHash), connectedPlayerCount: 0, HostPolicy);

            Assert.IsFalse(decision.Approved);
            Assert.AreEqual(ConnectionRejectionReason.ClientBuildMismatch, decision.Reason);

            var expected = string.Format(
                ConnectionRejectionMessages.ProtocolVersionMismatchFormat,
                BuildDisplayNumber.From(HostBuildHash),
                BuildDisplayNumber.From(OtherBuildHash));
            Assert.AreEqual(expected, decision.ReasonMessage);
            Assert.AreNotEqual(BuildDisplayNumber.From(HostBuildHash), BuildDisplayNumber.From(OtherBuildHash));
            AssertShownAsIsByCurrentAndPreviousClients(decision.ReasonMessage);
        }

        [Test]
        public void Evaluate_RejectsClientWithoutBuildHash_ShowingZeroForClient()
        {
            // 識別子を載せないクライアント（#204 より前の版は空を送る）。
            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, "つむぎ", string.Empty), connectedPlayerCount: 0, HostPolicy);

            Assert.AreEqual(ConnectionRejectionReason.ClientBuildMismatch, decision.Reason);
            Assert.AreEqual(
                $"バージョンが異なります（ホスト: {BuildDisplayNumber.From(HostBuildHash)} / あなた: 0）。",
                decision.ReasonMessage);
            AssertShownAsIsByCurrentAndPreviousClients(decision.ReasonMessage);
        }

        [Test]
        public void Evaluate_ComparesBuildHashOrdinally()
        {
            // 大文字小文字だけ違う識別子も別のビルドとして扱う。
            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, "つむぎ", HostBuildHash.ToUpperInvariant()), connectedPlayerCount: 0, HostPolicy);

            Assert.AreEqual(ConnectionRejectionReason.ClientBuildMismatch, decision.Reason);
        }

        [Test]
        public void Evaluate_ChecksBuildBeforePlayerName()
        {
            // ビルドが違い、名前も今の版の規則に合わない（見えない文字を含む）。名前の規則や文言は版で違いうるので、
            // ビルドの違う相手には必ずバージョン不一致を返す。
            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, "つむ​ぎ", OtherBuildHash), connectedPlayerCount: 0, HostPolicy);

            Assert.AreEqual(ConnectionRejectionReason.ClientBuildMismatch, decision.Reason);
        }

        [Test]
        public void Evaluate_ChecksBuildBeforeRoomCapacity()
        {
            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, "つむぎ", OtherBuildHash),
                connectedPlayerCount: 8,
                HostPolicy.WithMaxPlayers(2));

            Assert.AreEqual(ConnectionRejectionReason.ClientBuildMismatch, decision.Reason);
        }

        [Test]
        public void Evaluate_InvalidBuildHashCharacters_AreRejectedAsInvalidHashFirst()
        {
            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, "つむぎ", "abc\u0001"), connectedPlayerCount: 0, HostPolicy);

            Assert.AreEqual(ConnectionRejectionReason.InvalidClientBuildHash, decision.Reason);
        }

        [Test]
        public void Evaluate_DefaultPolicy_ExpectsEmptyBuildHash()
        {
            // Editor（識別子が空）と同じ条件。空どうしは一致し、空でない識別子は別のビルド。
            Assert.AreEqual(string.Empty, ConnectionApprovalPolicy.Default.ExpectedClientBuildHash);
            Assert.AreEqual(string.Empty, default(ConnectionApprovalPolicy).ExpectedClientBuildHash, "既定値でも null にならないはず。");

            var approved = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, "つむぎ", string.Empty), connectedPlayerCount: 0, ConnectionApprovalPolicy.Default);
            var rejected = ConnectionApprovalEvaluator.Evaluate(
                Payload(ProtocolConstants.Version, "つむぎ", HostBuildHash), connectedPlayerCount: 0, ConnectionApprovalPolicy.Default);

            Assert.IsTrue(approved.Approved);
            Assert.AreEqual(ConnectionRejectionReason.ClientBuildMismatch, rejected.Reason);
            Assert.AreEqual(
                $"バージョンが異なります（ホスト: 0 / あなた: {BuildDisplayNumber.From(HostBuildHash)}）。",
                rejected.ReasonMessage);
        }

        [Test]
        public void Policy_WithMaxPlayers_KeepsBuildHash()
        {
            // ロビーが人数上限を差し替えても（LobbyState.Server.cs）、ビルドの照合は外れない。
            var policy = HostPolicy.WithMaxPlayers(4).WithMaxPlayers(null);

            Assert.AreEqual(HostBuildHash, policy.ExpectedClientBuildHash);
            Assert.AreEqual(ProtocolConstants.Version, policy.ExpectedProtocolVersion);
        }

        [Test]
        public void Evaluate_ClientOfPreviousProtocolVersion_GetsMessageThatPreviousClientsRecognize()
        {
            // #208〜#209 の版のクライアントはプロトコルバージョン 2・識別子は空で接続してくる。
            // #204 でバージョンを 3 に上げたので、ペイロードの残りを読む前にバージョン不一致で拒否する。
            Assert.AreEqual(3, ProtocolConstants.Version, "#204 で 2 から 3 に上げた。上げたら下の期待値も見直すこと。");

            var decision = ConnectionApprovalEvaluator.Evaluate(
                Payload(2, "つむぎ", string.Empty), connectedPlayerCount: 0, HostPolicy);

            Assert.AreEqual(ConnectionRejectionReason.ProtocolVersionMismatch, decision.Reason);
            Assert.AreEqual("バージョンが異なります（ホスト: 3 / あなた: 2）。", decision.ReasonMessage);
            AssertShownAsIsByCurrentAndPreviousClients(decision.ReasonMessage);
        }

        /// <summary>
        /// 今の版の <see cref="DisconnectReasonLocalizer"/> が自前の文言としてそのまま出し、#208〜#209 の版の書式とも一致すること。
        /// </summary>
        private static void AssertShownAsIsByCurrentAndPreviousClients(string reasonMessage)
        {
            var localized = DisconnectReasonLocalizer.Localize(reasonMessage);
            Assert.AreEqual(DisconnectReasonCategory.AppMessage, localized.Category, reasonMessage);
            Assert.AreEqual(reasonMessage, localized.Message);
            Assert.IsTrue(
                PreviousReleaseVersionMismatchPattern.IsMatch(reasonMessage),
                $"#208〜#209 の版のクライアントが認める書式と一致するはず: {reasonMessage}");
        }
    }
}
