using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// クライアントから不正な RPC を送った場合に、サーバーが棄却して進行が動かないことを
    /// 実際のネットワーク経路で確かめるテスト（docs/network.md §1.5 / §9）。
    /// </summary>
    public class GameSessionRejectionTests : GameSessionTestFixture
    {
        [UnityTest]
        public IEnumerator RawRpcs_OutsidePhaseOrFromNonWinner_AreRejectedByServer()
        {
            // 誤答で Result まで進む経路を見たいので、受付の再開放（#18）は切っておく。
            yield return ConnectHostAndClient(ScoringSettings.Default.WithReopenAfterWrongAnswer(false));
            var hostSession = HostSession;
            var clientSession = ClientSession;

            var results = new List<(QuizJudgement Judgement, ulong ClientId, string Answer, int Score)>();
            clientSession.QuestionResolved += (judgement, clientId, answer, score, delta)
                => results.Add((judgement, clientId, answer, score));

            var buzzWinners = new List<ulong>();
            clientSession.BuzzLocked += (winner, lockedAt, wasTie) => buzzWinners.Add(winner);

            Assert.IsTrue(hostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"クライアントが BuzzOpen を受け取れませんでした（現在: {clientSession.Phase.Value}）。");

            // --- 受付中の回答送信は棄却される（NotAnswering） ---
            clientSession.SubmitAnswerRpc(new FixedString512Bytes(CorrectAnswer));
            yield return WaitFrames(10);

            Assert.AreEqual(QuizPhase.BuzzOpen, hostSession.ServerPhase, "フェーズ外の回答で進行が動いてはいけない。");
            Assert.AreEqual(QuizJudgement.None, hostSession.ServerJudgement);
            Assert.IsEmpty(results, "棄却された回答で結果が配信されてはいけない。");

            // --- 二重押下は 1 件だけ受理される（Duplicate） ---
            var buzzTime = ClientManager.LocalTime.Time;
            clientSession.BuzzRpc(buzzTime);
            clientSession.BuzzRpc(buzzTime + 0.001);

            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Answering,
                () => $"回答フェーズに進みませんでした（現在: {clientSession.Phase.Value}）。");

            Assert.AreEqual(ClientManager.LocalClientId, clientSession.LockedClientId.Value);
            CollectionAssert.AreEqual(
                new[] { ClientManager.LocalClientId },
                buzzWinners,
                "二重押下でも裁定結果は 1 度だけ配信されるはず。");

            // --- 勝者以外（ホスト自身）の回答は棄却される（NotLockedPlayer） ---
            hostSession.SubmitAnswerRpc(new FixedString512Bytes(CorrectAnswer));
            yield return WaitFrames(10);

            Assert.AreEqual(QuizPhase.Answering, hostSession.ServerPhase, "勝者以外の回答で進行が動いてはいけない。");
            Assert.IsEmpty(results);

            // --- 勝者の誤答は Wrong として Result に進み、得点は増えない ---
            Assert.IsTrue(clientSession.RequestAnswer("おおさか"), "勝者は回答を送れるはず。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Result,
                () => $"結果フェーズに進みませんでした（現在: {clientSession.Phase.Value}）。");

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(QuizJudgement.Wrong, results[0].Judgement);
            Assert.AreEqual(CorrectAnswer, results[0].Answer, "誤答でも正解は Result で提示する。");
            Assert.AreEqual(QuizStateMachine.DefaultIncorrectPoints, results[0].Score);
            Assert.AreEqual(
                QuizStateMachine.DefaultIncorrectPoints,
                hostSession.GetServerScore(ClientManager.LocalClientId));

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
                },
                hostSession.ServerPhaseHistory,
                "棄却された RPC は余分なフェーズ遷移を起こさないはず。");
        }

        /// <summary>
        /// 同じクライアントから短時間に連続して棄却される RPC を送っても、警告ログは
        /// <c>RpcRejectLogger</c>（#72）により 1 秒 1 回へ間引かれ、1 本しか出ないことを確かめる
        /// （レビュー M3）。
        /// </summary>
        [UnityTest]
        public IEnumerator RepeatedRejectedSubmitAnswerRpc_FromSameClient_LogsOnlyOnceWithinInterval()
        {
            yield return ConnectHostAndClient();
            var hostSession = HostSession;
            var clientSession = ClientSession;

            // ConnectHostAndClient は TtsSyncCoordinator（ホスト・クライアント双方）の初期化に伴う
            // 「TtsService が見つからない」警告を必ず 2 件出す（本テストの主眼とは無関係）。
            // LogAssert.NoUnexpectedReceived() は未消費のログを 1 件でも見つけると失敗するため、
            // ここで先に消費しておく。
            LogAssert.Expect(LogType.Warning, new Regex("TtsService が見つかりません"));
            LogAssert.Expect(LogType.Warning, new Regex("TtsService が見つかりません"));

            // 同じ理由で、出題時にホスト・クライアント双方の TtsSyncCoordinator が読み上げスキップの
            // ログを 1 件ずつ出す（TtsService 未登録で IsReadingPossible が false、issue #164）。
            LogAssert.Expect(LogType.Log, new Regex("問題 0 は読み上げません（未同意・読み上げ無効・準備未完了のいずれか）"));
            LogAssert.Expect(LogType.Log, new Regex("問題 0 は読み上げません（未同意・読み上げ無効・準備未完了のいずれか）"));

            Assert.IsTrue(hostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"クライアントが BuzzOpen を受け取れませんでした（現在: {clientSession.Phase.Value}）。");

            // 受付中（BuzzOpen）の回答送信は毎回棄却されるが、同じクライアントからの連打なら
            // 警告ログは 1 本にまとまるはず（バースト制限（60）は超えない範囲の件数に留める）。
            LogAssert.Expect(LogType.Warning, new Regex("回答を受け付けませんでした"));

            for (var i = 0; i < 5; i++)
            {
                clientSession.SubmitAnswerRpc(new FixedString512Bytes(CorrectAnswer));
            }

            yield return WaitFrames(10);

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(QuizPhase.BuzzOpen, hostSession.ServerPhase, "棄却された回答で進行が動いてはいけない。");
        }

        /// <summary>
        /// サーバー → 全員の RPC（<c>QuestionResultRpc</c>）はクライアントから送れないこと
        /// （<c>InvokePermission = RpcInvokePermission.Server</c>、docs/network.md §1.5）。
        /// 偽の結果通知を他のピアへ配れないことを確かめる。
        /// </summary>
        [UnityTest]
        public IEnumerator ServerOnlyRpc_InvokedFromClient_ThrowsAndIsNotDelivered()
        {
            yield return ConnectHostAndClient();
            var hostSession = HostSession;
            var clientSession = ClientSession;

            var hostResults = new List<QuizJudgement>();
            hostSession.QuestionResolved += (judgement, clientId, answer, score, delta) => hostResults.Add(judgement);

            var clientResults = new List<QuizJudgement>();
            clientSession.QuestionResolved += (judgement, clientId, answer, score, delta) => clientResults.Add(judgement);

            // 外部から呼べないよう private にしてあるので、敵対的な呼び出しはリフレクションで再現する。
            var method = typeof(GameSession).GetMethod(
                "QuestionResultRpc", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "QuestionResultRpc が見つかりません（名前を変えたら本テストも直すこと）。");

            var thrown = Assert.Throws<TargetInvocationException>(
                () => method.Invoke(
                    clientSession,
                    new object[] { QuizJudgement.Correct, 0UL, new FixedString512Bytes("にせのこたえ"), 999, 999 }),
                "クライアントからサーバー専用 RPC を送れてはいけない。");
            Assert.IsInstanceOf<RpcException>(
                thrown.InnerException,
                $"RpcException を期待したが {thrown.InnerException?.GetType().Name} だった。");

            yield return WaitFrames(10);

            Assert.IsEmpty(hostResults, "クライアント発の結果通知がホストに届いてはいけない。");
            Assert.IsEmpty(clientResults, "ローカルでも結果通知は発火しないはず。");
            Assert.AreEqual(QuizPhase.Lobby, hostSession.ServerPhase, "進行も動いてはいけない。");
        }
    }
}
