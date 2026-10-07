using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 選択式（<c>choice</c>）の進行（早押しなし・全員が回答・一斉判定、issue #17）を
    /// 実ネットワーク経路で通す統合テスト（docs/network.md §6.6 の choice 分岐、docs/question-data.md §6）。
    /// </summary>
    public class GameSessionChoiceTests : GameSessionTestFixture
    {
        private const string QuestionText = "日本の首都は？";
        private const int CorrectIndex = 1;

        /// <summary>選択式の回答受付を短くして、テストを現実的な時間で終わらせる。</summary>
        private static QuizTimeLimits FastChoiceLimits => new QuizTimeLimits(
            buzzTimeLimitSec: 10.0, answerTimeLimitSec: 10.0, choiceTimeLimitSec: 1.0, collectWindowSec: 0.15);

        [UnityTest]
        public IEnumerator ChoiceQuestion_TwoClientsSelect_TimeUp_CorrectAnswererScores()
        {
            var questionSource = new TestQuestionSource(
                TestQuestionSource.Choice("q-choice-1", QuestionText, CorrectIndex, "大阪", "東京", "京都"));

            yield return ConnectHostAndClient(questionSource, limits: FastChoiceLimits);
            yield return ConnectSecondClient();

            var clientId = ClientManager.LocalClientId;
            var secondClientId = SecondClientManager.LocalClientId;

            var resolvedCorrectIndex = -1;
            var resolvedEntries = new List<ChoiceAnswerEntry>();
            ClientSession.ChoiceResolved += (correctIndex, entries) =>
            {
                resolvedCorrectIndex = correctIndex;
                resolvedEntries.AddRange(entries);
            };

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.ChoiceAnswering,
                () => $"選択式の回答受付が開きませんでした（現在: {ClientSession.Phase.Value}）。");

            // --- 2 クライアントがそれぞれ選択する（早押しを介さない、#17） ---
            Assert.IsTrue(ClientSession.RequestChoice(CorrectIndex), "正解を選択できるはず。");
            Assert.IsTrue(SecondClientSession.RequestChoice(0), "不正解を選択できるはず。");

            // --- 時間切れで一斉判定 → Result ---
            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.Result,
                () => $"結果フェーズに進みませんでした（現在: {ClientSession.Phase.Value}）。");

            yield return WaitUntil(
                () => resolvedEntries.Count == 2,
                () => $"選択式の判定結果が届きませんでした（{resolvedEntries.Count} 件）。");

            Assert.AreEqual(CorrectIndex, resolvedCorrectIndex, "正解インデックスは元の choices 配列のものであるはず。");

            var correctEntry = Find(resolvedEntries, clientId);
            Assert.IsTrue(correctEntry.IsCorrect, "正解を選んだクライアントは正解扱いになるはず。");
            Assert.AreEqual(ScoreRules.DefaultCorrectPoints, correctEntry.ScoreDelta);
            Assert.AreEqual(ScoreRules.DefaultCorrectPoints, correctEntry.TotalScore);

            var wrongEntry = Find(resolvedEntries, secondClientId);
            Assert.IsFalse(wrongEntry.IsCorrect, "不正解を選んだクライアントは不正解扱いになるはず。");
            Assert.AreEqual(ScoreRules.DefaultWrongPoints, wrongEntry.ScoreDelta);

            // --- 得点表（NetworkList）にも正解者の得点が反映される ---
            yield return WaitUntil(
                () => ClientSession.GetScore(clientId) == ScoreRules.DefaultCorrectPoints,
                () => $"正解者の得点が同期されませんでした（{ClientSession.GetScore(clientId)}）。");
        }

        private static ChoiceAnswerEntry Find(List<ChoiceAnswerEntry> entries, ulong clientId)
        {
            foreach (var entry in entries)
            {
                if (entry.ClientId == clientId)
                {
                    return entry;
                }
            }

            Assert.Fail($"クライアント {clientId} の選択結果が見つかりません。");
            return default;
        }
    }
}
