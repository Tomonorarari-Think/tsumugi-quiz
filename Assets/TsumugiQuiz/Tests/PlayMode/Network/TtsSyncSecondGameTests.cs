using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Tests.PlayMode.Tts;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 2 回目のゲームで読み上げの再生開始（<c>PlayAtRpc</c>）が正しく扱われることの統合テスト（#144 再レビュー NH-1）。
    /// </summary>
    /// <remarks>
    /// <see cref="GameSession"/> はゲームの終了で despawn されないため、「もう一度」・ロビー経由の 2 回目のゲームでも
    /// 同じ <see cref="TtsSyncCoordinator"/> が使われる。直近の再生開始の記録（<c>LastReadingQuestionIndex</c> ほか）を
    /// 出題の合図で戻さないと、1 回目の最後の問題番号より小さい問題の <c>PlayAtRpc</c> が
    /// 「古い配信」として捨てられ、読み上げが鳴らない。
    /// </remarks>
    public sealed class TtsSyncSecondGameTests : GameSessionTestFixture
    {
        private const string SecondQuestionText = "日本でいちばん高い山は？";
        private const string SecondCorrectAnswer = "ふじさん";
        private const double ReadingDurationSec = 0.5d;

        [UnityTest]
        public IEnumerator SecondGame_PlayAtForLowerQuestionIndex_IsNotDiscarded()
        {
            yield return ConnectHostAndClient(new TestQuestionSource(
                TestQuestionSource.FreeText("q-1", QuestionText, CorrectAnswer),
                TestQuestionSource.FreeText("q-2", SecondQuestionText, SecondCorrectAnswer)));

            var hostCoordinator = ReadingPlaybackTestRig.GetCoordinator(HostSession);
            var clientCoordinator = ReadingPlaybackTestRig.GetCoordinator(ClientSession);
            var clientPlayback = new FixedDurationReadingPlayback(ReadingDurationSec);
            hostCoordinator.SetPlayback(new FixedDurationReadingPlayback(ReadingDurationSec));
            clientCoordinator.SetPlayback(clientPlayback);

            // --- 1 回目のゲーム: 問題 1（インデックス 1）を出して決着させる ---
            Assert.IsTrue(HostSession.StartQuestion(1), "1 回目のゲームの出題を開始できるはず。");
            yield return WaitUntil(
                () => clientCoordinator.LastReadingQuestionIndex == 1,
                () => $"1 回目の再生開始時刻が届きませんでした（{clientCoordinator.LastReadingQuestionIndex}）。");
            yield return AnswerCorrectly(SecondCorrectAnswer);

            // --- ロビーへ戻って 2 回目のゲーム: 問題 0（1 回目より小さい番号） ---
            Assert.IsTrue(HostSession.ReturnToLobby(), "結果表示からロビーへ戻せるはず。");
            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.Lobby,
                () => $"ロビーへ戻りませんでした（{ClientSession.Phase.Value}）。");

            var scheduledBefore = clientPlayback.ScheduleCallCount;
            Assert.IsTrue(HostSession.StartQuestion(0), "2 回目のゲームの出題を開始できるはず。");
            yield return WaitUntil(
                () => clientPlayback.ScheduleCallCount > scheduledBefore,
                () => "2 回目のゲームの問題 0 の PlayAtRpc が捨てられました（読み上げが予約されない）。");

            Assert.AreEqual(0, clientPlayback.LastScheduledQuestionIndex, "2 回目のゲームの問題 0 を予約するはず。");
            Assert.AreEqual(0, clientCoordinator.LastReadingQuestionIndex, "直近の記録も 2 回目のゲームのものになるはず。");
            Assert.AreEqual(ReadingDurationSec, clientCoordinator.LastDurationSec, 1e-9);
        }

        /// <summary>受付が開くのを待って早押しし、正解を送って結果表示まで進める。</summary>
        private IEnumerator AnswerCorrectly(string answer)
        {
            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"早押し受付が開きませんでした（{ClientSession.Phase.Value}）。");
            Assert.IsTrue(ClientSession.RequestBuzz(), "受付中なので押下を送れるはず。");
            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.Answering,
                () => $"回答フェーズに進みませんでした（{ClientSession.Phase.Value}）。");
            Assert.IsTrue(ClientSession.RequestAnswer(answer), "勝者は回答を送れるはず。");
            yield return WaitUntil(
                () => HostSession.Phase.Value == QuizPhase.Result,
                () => $"結果表示に進みませんでした（{HostSession.Phase.Value}）。");
        }
    }
}
