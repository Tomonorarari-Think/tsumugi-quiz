using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// NGO に登録した <c>ConnectionApprovalCallback</c> の経路（docs/network.md §2.3）。
    /// 2 プロセスを立てずに、登録済みのコールバックを直接呼んで検証する。
    /// 実際に接続させる検証は <see cref="NetworkApprovalIntegrationTests"/>。
    /// </summary>
    public class NetworkServiceApprovalTests : NetworkServiceTestFixture
    {
        [UnityTest]
        public IEnumerator ApprovalCallback_ApprovesMatchingProtocolVersion()
        {
            yield return StartHost();

            var response = InvokeApproval(ProtocolConstants.Version, "つむぎ");

            Assert.IsTrue(response.Approved);
            Assert.IsTrue(string.IsNullOrEmpty(response.Reason));
            Assert.IsFalse(response.CreatePlayerObject);

            Service.Stop();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ApprovalCallback_RejectsMismatchedProtocolVersion_WithReason()
        {
            yield return StartHost();

            var clientVersion = (ushort)(ProtocolConstants.Version + 1);
            var response = InvokeApproval(clientVersion, "つむぎ");

            Assert.IsFalse(response.Approved);
            StringAssert.Contains("バージョン", response.Reason);
            StringAssert.Contains(clientVersion.ToString(), response.Reason);

            Service.Stop();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ApprovalCallback_RejectsWhenRoomIsFull()
        {
            // ホスト自身が 1 人としてカウントされる状態で上限 1 を設定する。
            var result = Service.StartHost(startPort: 0, maxPlayers: 1);
            Assert.IsTrue(result.Success, result.Message);
            yield return null;

            var response = InvokeApproval(ProtocolConstants.Version, "つむぎ");

            Assert.IsFalse(response.Approved);
            Assert.AreEqual(ConnectionRejectionMessages.RoomFull, response.Reason);

            Service.Stop();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ApprovalCallback_RejectsInvalidPlayerName()
        {
            yield return StartHost();

            var response = InvokeApproval(ProtocolConstants.Version, "   ");

            Assert.IsFalse(response.Approved);
            Assert.AreEqual(ConnectionRejectionMessages.InvalidPlayerName, response.Reason);

            Service.Stop();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ApprovalCallback_ApprovesHostItself_EvenWithEmptyPayload()
        {
            yield return StartHost();

            // ホスト自身（NetworkManager.ServerClientId）は NGO 側で拒否できない扱いなので、
            // ペイロードが空でも承認する。
            var response = InvokeApproval(System.Array.Empty<byte>(), NetworkManager.ServerClientId);

            Assert.IsTrue(response.Approved);
            Assert.IsTrue(string.IsNullOrEmpty(response.Reason));
            Assert.IsFalse(response.CreatePlayerObject);

            Service.Stop();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ApprovalHandler_RaisesEvaluatedEvent()
        {
            yield return StartHost();

            ulong? evaluatedClientId = null;
            ConnectionApprovalDecision? decision = null;
            Service.ApprovalHandler.Evaluated += (clientId, result) =>
            {
                evaluatedClientId = clientId;
                decision = result;
            };

            InvokeApproval(ProtocolConstants.Version, "つむぎ");

            Assert.AreEqual(1UL, evaluatedClientId);
            Assert.IsTrue(decision.HasValue);
            Assert.IsTrue(decision.Value.Approved);
            Assert.AreEqual("つむぎ", decision.Value.Payload.PlayerName);

            Service.Stop();
            yield return null;
        }

        [UnityTest]
        public IEnumerator StartClient_WithoutConnectionApproval_FailsWithoutStarting()
        {
            // 土台が作る NetworkConfig は ConnectionApproval が既定の false。
            // この状態では NGO が承認ペイロードを送らないため、開始前に失敗させる。
            Assert.IsFalse(NetworkManagerUnderTest.NetworkConfig.ConnectionApproval);
            LogAssert.Expect(LogType.Error, new Regex("ConnectionApproval"));

            var result = Service.StartClient("127.0.0.1", 7777, "つむぎ");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("承認", result.Message);
            Assert.IsFalse(NetworkManagerUnderTest.IsClient, "接続は開始されないはず。");

            yield return null;
        }

        [UnityTest]
        public IEnumerator StartClient_WithInvalidPlayerName_Fails()
        {
            // ConnectionApproval を有効にして、名前の検証だけを見る。
            NetworkManagerUnderTest.NetworkConfig.ConnectionApproval = true;

            var result = Service.StartClient("127.0.0.1", 7777, "   ");

            Assert.IsFalse(result.Success);
            Assert.AreEqual(ConnectionRejectionMessages.InvalidPlayerName, result.Message);
            Assert.IsFalse(NetworkManagerUnderTest.IsClient);

            yield return null;
        }

        private IEnumerator StartHost()
        {
            var result = Service.StartHost(startPort: 0);
            Assert.IsTrue(result.Success, result.Message);
            yield return null;
        }
    }
}
