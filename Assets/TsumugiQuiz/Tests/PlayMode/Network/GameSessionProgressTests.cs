using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Participants;
using TsumugiQuiz.Network;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 参加者パネル（#194）へ配る現在の問題の進行状態（<c>NetworkVariable&lt;QuestionProgressPayload&gt;</c>）が、
    /// ホスト + クライアント 2 台の実ネットワーク経路で同期されることを確かめる統合テスト。
    /// </summary>
    public class GameSessionProgressTests : GameSessionTestFixture
    {
        private const int CorrectIndex = 1;

        private static ParticipantProgress Get(GameSession session, ulong clientId)
        {
            session.GetQuestionProgress().TryGet(clientId, out var progress);
            return progress;
        }

        [UnityTest]
        public IEnumerator FreeText_BuzzRanksAndWrongMarkAreSyncedToTheOtherClient()
        {
            // 2 台がほぼ同時に押しても両方が集計窓に入るよう、窓を広めに取る（順位は押下時刻で決まる）。
            yield return ConnectHostAndClient(
                limits: new QuizTimeLimits(buzzTimeLimitSec: 30.0, answerTimeLimitSec: 15.0, collectWindowSec: 0.5));
            yield return ConnectSecondClient();

            var sessionA = ClientSession;
            var sessionB = SecondClientSession;
            var clientA = ClientManager.LocalClientId;
            var clientB = SecondClientManager.LocalClientId;

            var changedOnB = 0;
            sessionB.QuestionProgressChanged += () => changedOnB++;

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => sessionA.Phase.Value == QuizPhase.BuzzOpen && sessionB.Phase.Value == QuizPhase.BuzzOpen,
                () => $"両クライアントが BuzzOpen を受け取れませんでした（A: {sessionA.Phase.Value} / B: {sessionB.Phase.Value}）。");

            Assert.AreEqual(0, sessionB.GetQuestionProgress().Entries.Count, "押す前は誰にも状態が無い。");

            Assert.IsTrue(sessionA.RequestBuzz());
            Assert.IsTrue(sessionB.RequestBuzz());

            yield return WaitUntil(
                () => Get(sessionB, clientA).BuzzRank != 0 && Get(sessionB, clientB).BuzzRank != 0,
                () => $"両者の押下順位がクライアントへ届きませんでした（{sessionB.GetQuestionProgress().Entries.Count} 行）。");

            var winner = HostSession.LockedClientId.Value;
            var loser = winner == clientA ? clientB : clientA;
            Assert.AreEqual(1, Get(sessionB, winner).BuzzRank, "勝者が 1 位。");
            Assert.AreEqual(2, Get(sessionB, loser).BuzzRank, "集計窓の中の 2 番手も順位が届く。");
            Assert.AreEqual(1, Get(sessionB, winner).AnswerOrder);
            Assert.Greater(changedOnB, 0, "変更通知が発火するはず。");

            // --- 勝者が誤答 → 再開放 ---
            var winnerSession = winner == clientA ? sessionA : sessionB;
            yield return WaitUntil(
                () => winnerSession.Phase.Value == QuizPhase.Answering,
                () => $"勝者の回答フェーズに進みませんでした（現在: {winnerSession.Phase.Value}）。");
            Assert.IsTrue(winnerSession.RequestAnswer("おおさか"));

            var loserSession = winner == clientA ? sessionB : sessionA;
            yield return WaitUntil(
                () => Get(loserSession, winner).Flags.Has(ParticipantProgressFlags.WrongAnswered),
                () => "誤答した人の「回答権なし」が相手のクライアントへ届きませんでした。");

            var reopened = loserSession.GetQuestionProgress();
            Assert.IsTrue(reopened.IsExcludedFromBuzzing(winner));
            Assert.IsFalse(reopened.IsExcludedFromBuzzing(loser), "まだ押せる人は除外されない。");
            Assert.AreEqual(0, Get(loserSession, winner).BuzzRank, "開き直した受付では前の押下順を消す。");
            Assert.AreEqual(0, Get(loserSession, loser).BuzzRank);
            Assert.AreEqual(1, Get(loserSession, winner).AnswerOrder, "回答順は残る。");
            Assert.AreEqual(HostSession.QuestionIndex.Value, reopened.QuestionIndex);
        }

        [UnityTest]
        public IEnumerator Choice_SubmittedIsSyncedWithoutJudgementUntilResult()
        {
            var questionSource = new TestQuestionSource(
                TestQuestionSource.Choice("q-progress-choice", "日本の首都は？", CorrectIndex, "大阪", "東京", "京都"));
            yield return ConnectHostAndClient(
                questionSource,
                limits: new QuizTimeLimits(buzzTimeLimitSec: 10.0, answerTimeLimitSec: 10.0, choiceTimeLimitSec: 3.0, collectWindowSec: 0.15));
            yield return ConnectSecondClient();

            var clientA = ClientManager.LocalClientId;
            var clientB = SecondClientManager.LocalClientId;

            Assert.IsTrue(HostSession.StartQuestion());
            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.ChoiceAnswering,
                () => $"選択式の回答受付が開きませんでした（現在: {ClientSession.Phase.Value}）。");

            Assert.IsTrue(ClientSession.RequestChoice(0), "不正解を選ぶ。");

            yield return WaitUntil(
                () => Get(SecondClientSession, clientA).Flags.Has(ParticipantProgressFlags.ChoiceSubmitted),
                () => "「回答済み」が相手のクライアントへ届きませんでした。");

            Assert.AreEqual(QuizPhase.ChoiceAnswering, SecondClientSession.Phase.Value, "判定の前に届く（リアルタイム）。");
            Assert.AreEqual(
                ParticipantProgressFlags.ChoiceSubmitted,
                Get(SecondClientSession, clientA).Flags,
                "判定までは正誤のビットを載せない（選んだ番号はペイロードの構造上そもそも無い）。");
            Assert.IsFalse(SecondClientSession.GetQuestionProgress().TryGet(clientB, out _), "未回答の人には行が無い。");

            yield return WaitUntil(
                () => Get(SecondClientSession, clientA).Flags.Has(ParticipantProgressFlags.WrongAnswered),
                () => $"判定後の正誤が届きませんでした（フェーズ: {SecondClientSession.Phase.Value}）。");
            Assert.IsTrue(Get(SecondClientSession, clientA).Flags.Has(ParticipantProgressFlags.ChoiceSubmitted));
        }
    }
}
