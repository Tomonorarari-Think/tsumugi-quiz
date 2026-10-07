using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// <see cref="QuestionDistributor"/> を <see cref="GameSession"/> より先に載せた
    /// （＝ <c>NetworkBehaviour</c> のインデックスと <c>NetworkTickSystem.Tick</c> の購読順が逆の）
    /// プレハブでも、配信 → 受信確認 → 出題が同じように進むことを確かめる（#13 レビュー H-1）。
    /// </summary>
    /// <remarks>
    /// 受付開始の保留時刻は Ack 期限 + <see cref="QuestionDistributor.AckHoldMarginSec"/> なので、
    /// タイムアウト判定（配信器の tick）と受付開始（状態機械の tick）が同値にならず、
    /// どちらの tick が先に走っても結果が変わらない。
    /// </remarks>
    public class QuestionDistributionComponentOrderTests : GameSessionTestFixture
    {
        /// <summary>実資産（および他のテスト）と衝突しないプレハブ識別子。</summary>
        private const uint ReversedOrderPrefabHash = 812_000_013u;

        /// <inheritdoc />
        protected override GameObject CreateGameSessionPrefab()
        {
            return NetworkTestPrefabs.CreateRuntimeGameSessionPrefab(
                "GameSessionPrefab(DistributorFirst)", ReversedOrderPrefabHash, distributorFirst: true);
        }

        /// <inheritdoc />
        protected override void ReleaseGameSessionPrefab(GameObject prefab)
        {
            if (prefab != null)
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [UnityTest]
        public IEnumerator Distribution_WithDistributorBeforeGameSession_ShowsQuestionAndOpensBuzz()
        {
            yield return ConnectHostAndClient();

            // 本当にコンポーネント順が逆になっていること（テストが前提を満たしていることの確認）。
            var behaviours = HostSession.GetComponents<NetworkBehaviour>();
            Assert.IsInstanceOf<QuestionDistributor>(
                behaviours[0], "このテストでは QuestionDistributor が GameSession より前に載っているはず。");

            var shown = new List<QuestionDto>();
            ClientSession.QuestionShown += (index, question, _) => shown.Add(question);

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"クライアントが BuzzOpen を受け取れませんでした（現在: {ClientSession.Phase.Value}）。");

            Assert.AreEqual(1, shown.Count, "コンポーネント順が逆でも提示は 1 度だけ届くはず。");
            Assert.AreEqual(QuestionText, shown[0].Text);
            Assert.IsTrue(
                ClientDistributor.TryGetQuestion(0, out var cached), "クライアントが DTO を保持しているはず。");
            Assert.AreEqual(QuestionText, cached.Text);
            Assert.IsFalse(HostDistributor.IsAwaitingAck, "受信確認は揃っているはず。");
        }
    }
}
