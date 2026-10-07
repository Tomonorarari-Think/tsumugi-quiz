using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using Unity.Netcode;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 1 問の正常系（出題 → クライアント押下 → 回答 → 正解 → Result → Finished）を
    /// 実際のネットワーク経路で通す統合テスト（docs/network.md §6.6）。
    /// </summary>
    public class GameSessionTests : GameSessionTestFixture
    {
        [UnityTest]
        public IEnumerator OneQuestion_BuzzAndCorrectAnswer_ReachesResultOnClient()
        {
            yield return ConnectHostAndClient();
            var hostSession = HostSession;
            var clientSession = ClientSession;

            var observedPhases = new List<QuizPhase> { clientSession.Phase.Value };
            clientSession.Phase.OnValueChanged += (previous, next) => observedPhases.Add(next);

            var shownTexts = new List<string>();
            clientSession.QuestionShown += (index, question, _) => shownTexts.Add(question.Text);

            var buzzWinners = new List<ulong>();
            clientSession.BuzzLocked += (winner, lockedAt, wasTie) => buzzWinners.Add(winner);

            var results = new List<(QuizJudgement Judgement, ulong ClientId, string Answer, int Score, int Delta)>();
            clientSession.QuestionResolved += (judgement, clientId, answer, score, delta)
                => results.Add((judgement, clientId, answer, score, delta));

            var scoreChanges = new List<(ulong ClientId, int Delta, int Total)>();
            clientSession.ScoreChanged += (clientId, delta, total) => scoreChanges.Add((clientId, delta, total));

            AssertClientCannotWrite(clientSession);

            // --- 出題 → 受付開始 ---
            Assert.IsTrue(hostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"クライアントが BuzzOpen を受け取れませんでした（現在: {clientSession.Phase.Value}）。");

            Assert.AreEqual(0, clientSession.QuestionIndex.Value, "問題インデックスが同期しているはず。");
            Assert.Greater(clientSession.BuzzOpenServerTime.Value, 0.0, "受付開始 T0 が同期しているはず。");
            Assert.AreEqual(
                clientSession.BuzzOpenServerTime.Value + QuizTimeLimits.DefaultBuzzTimeLimitSec,
                clientSession.CurrentDeadlineServerTime,
                1e-9,
                "残り時間は T0 + 制限時間から計算できるはず。");
            CollectionAssert.AreEqual(new[] { QuestionText }, shownTexts, "問題文が 1 度だけ届くはず。");
            Assert.IsEmpty(results, "Result になる前に正解が届いてはいけない。");

            // --- クライアントが押す ---
            Assert.IsTrue(clientSession.RequestBuzz(), "受付中なので押下を送れるはず。");

            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Answering,
                () => $"回答フェーズに進みませんでした（現在: {clientSession.Phase.Value}）。");

            Assert.AreEqual(ClientManager.LocalClientId, clientSession.LockedClientId.Value, "押したクライアントが勝者になるはず。");
            CollectionAssert.AreEqual(new[] { ClientManager.LocalClientId }, buzzWinners, "裁定結果が 1 度だけ届くはず。");

            // --- 回答（カタカナ入力でも正規化して正解になる） ---
            Assert.IsTrue(clientSession.RequestAnswer("トウキョウ"), "勝者は回答を送れるはず。");

            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Result,
                () => $"結果フェーズに進みませんでした（現在: {clientSession.Phase.Value}）。");

            Assert.AreEqual(1, results.Count, "結果通知は 1 度だけ届くはず。");
            Assert.AreEqual(QuizJudgement.Correct, results[0].Judgement);
            Assert.AreEqual(ClientManager.LocalClientId, results[0].ClientId);
            Assert.AreEqual(CorrectAnswer, results[0].Answer, "正解は Result で初めてクライアントへ届く。");
            Assert.AreEqual(QuizStateMachine.DefaultCorrectPoints, results[0].Score);
            Assert.AreEqual(QuizStateMachine.DefaultCorrectPoints, results[0].Delta, "正解の加点が差分として届くはず。");

            // 得点はスコア表（NetworkList）でも同期される（#18）。
            CollectionAssert.AreEqual(
                new[] { (ClientManager.LocalClientId, QuizStateMachine.DefaultCorrectPoints, QuizStateMachine.DefaultCorrectPoints) },
                scoreChanges,
                "得点の増減が 1 度だけ届くはず。");
            Assert.AreEqual(
                QuizStateMachine.DefaultCorrectPoints,
                clientSession.GetScore(ClientManager.LocalClientId),
                "クライアント側の得点表にも反映されるはず。");

            // --- 1 問だけなので、そのまま終了させる ---
            Assert.IsTrue(hostSession.FinishSession(), "Result から終了できるはず。");

            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Finished,
                () => $"終了フェーズに進みませんでした（現在: {clientSession.Phase.Value}）。");

            // --- サーバー側の遷移が docs/network.md §6.6 のとおりであること ---
            CollectionAssert.AreEqual(
                new[]
                {
                    QuizPhase.Lobby,
                    QuizPhase.Reading,
                    QuizPhase.BuzzOpen,
                    QuizPhase.Locked,
                    QuizPhase.Answering,
                    QuizPhase.Judging,
                    QuizPhase.Result,
                    QuizPhase.Finished,
                },
                hostSession.ServerPhaseHistory,
                "サーバーのフェーズ遷移が状態遷移図と一致するはず。");
            Assert.AreEqual(QuizStateMachine.DefaultCorrectPoints, hostSession.GetServerScore(ClientManager.LocalClientId));

            // クライアントが観測したフェーズは、サーバーの遷移の（順序を保った）部分列であること。
            AssertIsOrderedSubsequence(observedPhases, hostSession.ServerPhaseHistory);
            CollectionAssert.IsSubsetOf(
                new[] { QuizPhase.BuzzOpen, QuizPhase.Answering, QuizPhase.Result, QuizPhase.Finished },
                observedPhases,
                "クライアントは主要フェーズを受け取るはず。観測: " + string.Join(" → ", observedPhases));
        }

        /// <summary>
        /// クライアント側のガード（<see cref="GameSession.RequestBuzz"/> /
        /// <see cref="GameSession.RequestAnswer"/>）を通さず RPC を直接叩いても、
        /// サーバーがフェーズ・二重押下・勝者本人かを検証して弾くこと（docs/network.md §9）。
        /// 併せて誤答（Wrong）の経路を実際の RPC 経路で通す。
        /// </summary>
        private static void AssertClientCannotWrite(GameSession session)
        {
            var variables = new NetworkVariableBase[]
            {
                session.Phase,
                session.QuestionIndex,
                session.PhaseStartServerTime,
                session.BuzzOpenServerTime,
                session.LockedClientId,
                session.Scores,
            };

            foreach (var variable in variables)
            {
                Assert.AreEqual(
                    NetworkVariableWritePermission.Server,
                    variable.WritePerm,
                    $"{variable.Name} はサーバーのみ書き込み可能であるはず。");
            }
        }

        private static void AssertIsOrderedSubsequence(IReadOnlyList<QuizPhase> observed, IReadOnlyList<QuizPhase> expected)
        {
            var cursor = 0;
            foreach (var phase in observed)
            {
                while (cursor < expected.Count && expected[cursor] != phase)
                {
                    cursor++;
                }

                Assert.Less(
                    cursor,
                    expected.Count,
                    $"クライアントが観測したフェーズ列がサーバーの遷移と矛盾します。観測: {string.Join(" → ", observed)}");
                cursor++;
            }
        }

    }
}
