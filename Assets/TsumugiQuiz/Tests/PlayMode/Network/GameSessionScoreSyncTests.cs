using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 得点表（<c>NetworkList&lt;ScoreEntry&gt;</c>）の同期のテスト（#18）。
    /// 増減の RPC は参加前の分を受け取れないため、得点は <c>NetworkList</c> で配っている。
    /// 途中参加したクライアントに現在の得点表が届くことを確かめる。
    /// </summary>
    public class GameSessionScoreSyncTests : GameSessionTestFixture
    {
        [UnityTest]
        public IEnumerator LateJoiningClient_ReceivesCurrentScoreTable()
        {
            yield return ConnectHostAndClient();
            var hostSession = HostSession;
            var clientSession = ClientSession;
            var clientA = ClientManager.LocalClientId;

            // --- 1 問だけ進めて得点を発生させる ---
            Assert.IsTrue(hostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"クライアントが BuzzOpen を受け取れませんでした（現在: {clientSession.Phase.Value}）。");

            Assert.IsTrue(clientSession.RequestBuzz(), "受付中なので押下を送れるはず。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Answering,
                () => $"回答フェーズに進みませんでした（現在: {clientSession.Phase.Value}）。");

            Assert.IsTrue(clientSession.RequestAnswer(CorrectAnswer), "勝者は回答を送れるはず。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Result,
                () => $"結果フェーズに進みませんでした（現在: {clientSession.Phase.Value}）。");
            yield return WaitUntil(
                () => clientSession.GetScore(clientA) == ScoreRules.DefaultCorrectPoints,
                () => $"先に参加していたクライアントへ得点が同期されませんでした（{clientSession.GetScore(clientA)}）。");

            // --- 得点が入ったあとに 2 人目が参加する ---
            yield return ConnectSecondClient();
            var lateSession = SecondClientSession;

            var tableChanged = 0;
            lateSession.ScoreTableChanged += () => tableChanged++;

            yield return WaitUntil(
                () => lateSession.GetScore(clientA) == ScoreRules.DefaultCorrectPoints,
                () => "途中参加したクライアントへ現在の得点表が届きませんでした"
                    + $"（得点: {lateSession.GetScore(clientA)} / 行数: {lateSession.ScoreCount}）。");

            var clientB = SecondClientManager.LocalClientId;
            Assert.AreEqual(1, lateSession.ScoreCount, "得点表は 1 行（回答した A のみ）。");
            Assert.AreEqual(0, lateSession.GetScore(clientB), "参加直後の自分は 0 点。");
            Assert.AreEqual(
                ScoreRules.DefaultCorrectPoints,
                hostSession.GetScore(clientA),
                "ホスト側の得点表も同じ値を返すはず。");

            // --- 参加後の更新は ScoreTableChanged で拾える ---
            Assert.IsTrue(hostSession.StartQuestion(), "Result から次の出題を始められるはず。");
            yield return WaitUntil(
                () => lateSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"途中参加したクライアントが BuzzOpen を受け取れませんでした（現在: {lateSession.Phase.Value}）。");

            Assert.IsTrue(lateSession.RequestBuzz(), "途中参加したクライアントも押下を送れるはず。");
            yield return WaitUntil(
                () => lateSession.Phase.Value == QuizPhase.Answering,
                () => $"回答フェーズに進みませんでした（現在: {lateSession.Phase.Value}）。");

            Assert.IsTrue(lateSession.RequestAnswer(CorrectAnswer), "勝者は回答を送れるはず。");
            yield return WaitUntil(
                () => lateSession.GetScore(clientB) == ScoreRules.DefaultCorrectPoints,
                () => $"途中参加したクライアントの得点が同期されませんでした（{lateSession.GetScore(clientB)}）。");

            Assert.AreEqual(2, lateSession.ScoreCount, "得点表は 2 行になる。");
            Assert.Greater(tableChanged, 0, "得点表の変更が通知されるはず。");
        }
    }
}
