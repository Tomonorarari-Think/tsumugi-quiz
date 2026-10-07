using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 押せる参加者の判定（#200）のうち、名簿（<see cref="LobbyState"/>）が要るものを実際の接続で確かめる PlayMode テスト。
    /// 席を保持している切断中の参加者は数え、席が消えたら数えなくなること（PR #203 レビュー H-1 / L-1）と、
    /// 参加者が 0 人なら締めないこと（同 M-1）。
    /// </summary>
    /// <remarks>
    /// <see cref="LobbyStateTestFixture"/>（ホスト + クライアント 2、承認を通して名簿に載る）に <c>GameSession</c> を足して使う。
    /// ホストは司会専任（押せる人に数えない）にして、クライアントだけで判定が決まるようにする。
    /// </remarks>
    public class GameSessionNoEligibleBuzzerSeatTests : LobbyStateTestFixture
    {
        private const string QuestionText = "日本の首都はどこ？";
        private const string CorrectAnswer = "とうきょう";
        private const string WrongAnswer = "おおさか";

        /// <summary>席が残っている間は締めないことを確かめるときに、実際に待つ秒数。</summary>
        private const float RetainedSeatObservationSeconds = 1.5f;

        /// <inheritdoc />
        protected override bool UsesGameSession => true;

        [UnityTest]
        public IEnumerator RetainedDisconnectedSeat_KeepsBuzzOpen_UntilTheSeatIsRemoved()
        {
            StartModeratorRoom();
            yield return ConnectClient(0, "つむぎ");
            yield return ConnectClient(1, "ひかり");
            var clientA = GetClientId(0);
            var clientB = GetClientId(1);

            GameSession sessionA = null;
            yield return WaitUntil(
                () => (sessionA = FindClientSession(0)) != null,
                () => "クライアント 0 側に GameSession が生成されませんでした。");

            var results = new List<QuizJudgement>();
            sessionA.QuestionResolved += (judgement, clientId, answer, score, delta) => results.Add(judgement);

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => sessionA.Phase.Value == QuizPhase.BuzzOpen,
                () => $"受付が開きませんでした（A: {sessionA.Phase.Value}）。");

            // --- A が誤答 → B が残っているので開き直す ---
            Assert.IsTrue(sessionA.RequestBuzz(), "A は受付中なので押下を送れるはず。");
            yield return WaitUntil(
                () => sessionA.Phase.Value == QuizPhase.Answering && sessionA.LockedClientId.Value == clientA,
                () => $"A の回答フェーズに進みませんでした（{sessionA.Phase.Value}）。");
            Assert.IsTrue(sessionA.RequestAnswer(WrongAnswer), "勝者 A は回答を送れるはず。");
            yield return WaitUntil(
                () => HostSession.ServerPhase == QuizPhase.BuzzOpen
                    && HostSession.IsServerPenalized(clientA),
                () => $"B が残っているので受付を開き直すはず（サーバー: {HostSession.ServerPhase}）。");

            // --- B が切断する。席は保持期間中なので、戻ってくる可能性がある人として数える ---
            yield return DisconnectClient(1);
            yield return WaitUntil(
                () => TryFindEntryByClientId(HostLobby, clientB, out var entry) && !entry.IsConnected,
                () => "B の切断が名簿へ反映されるはず。");

            yield return new WaitForSecondsRealtime(RetainedSeatObservationSeconds);
            Assert.AreEqual(
                QuizPhase.BuzzOpen,
                HostSession.ServerPhase,
                "席を保持している切断中の B は数えるので、受付は締まらないはず（PR #203 レビュー H-1）。");
            Assert.IsEmpty(results, "まだ結果は出ない。");

            // --- ホストが切断中の席を削除する（ForgetSeat）と、押せる人が居なくなって締まる ---
            Assert.AreEqual(1, HostLobby.RemoveDisconnectedEntries(), "切断中の B の席を削除できるはず。");

            yield return WaitUntil(
                () => results.Count == 1 && sessionA.Phase.Value == QuizPhase.Result,
                () => $"席が消えて押せる人が居なくなったのに締まりませんでした（サーバー: {HostSession.ServerPhase}）。");
            Assert.AreEqual(
                QuizJudgement.NoEligibleBuzzers,
                results[0],
                "時間切れとは区別できる判定で届くはず（PR #203 レビュー M-2）。");
        }

        [UnityTest]
        public IEnumerator ModeratorOnlyRoomWithoutParticipants_DoesNotCloseBuzz()
        {
            // 司会専任のホストだけ（参加者 0 人）で出題しても、判定の材料が無いので締めない（PR #203 レビュー M-1）。
            StartModeratorRoom();

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => HostSession.ServerPhase == QuizPhase.BuzzOpen,
                () => $"受付が開きませんでした（サーバー: {HostSession.ServerPhase}）。");

            yield return new WaitForSecondsRealtime(RetainedSeatObservationSeconds);
            Assert.AreEqual(
                QuizPhase.BuzzOpen,
                HostSession.ServerPhase,
                "参加者の居ない部屋で問題が締まって流れないはず（従来どおり時間切れまで待つ）。");
        }

        /// <summary>
        /// #204 L-A: 席を保持している切断中の参加者を集める処理は、名簿（<see cref="LobbyState"/>）を自分で解決する。
        /// 以前は直前の <c>IsHostModerator()</c> が解決していることに頼っていたので、メモ化した名簿が無い状態
        /// （<c>OnNetworkSpawn</c> の時点で名簿がまだ無かった場合）で単独で呼ぶと、切断中の参加者を数えられなかった。
        /// </summary>
        [UnityTest]
        public IEnumerator CollectRetainedDisconnectedParticipants_ResolvesLobbyStateByItself()
        {
            StartModeratorRoom();
            yield return ConnectClient(0, "つむぎ");
            yield return ConnectClient(1, "ひかり");
            var clientB = GetClientId(1);

            yield return DisconnectClient(1);
            yield return WaitUntil(
                () => TryFindEntryByClientId(HostLobby, clientB, out var entry) && !entry.IsConnected,
                () => "B の切断が名簿へ反映されるはず。");

            // 名簿をまだ解決していない状態にしてから、IsHostModerator() を通さずに直接呼ぶ。
            HostSession.ForgetResolvedLobbyStateForTests();
            var retained = HostSession.CollectRetainedDisconnectedParticipantsForTests();

            CollectionAssert.AreEqual(
                new[] { clientB },
                retained,
                "呼び出し順に頼らず名簿を引き、席を保持している切断中の B だけを集めるはず。");
        }

        /// <summary>司会専任のホストで部屋を立て、早押しの持ち時間を上限（60 秒）にした 1 問を設定する。</summary>
        private void StartModeratorRoom()
        {
            StartHostAndSpawnLobby(maxPlayers: 6, hostRole: HostRole.Moderator);
            HostSession.Configure(
                new TestQuestionSource(TestQuestionSource.FreeText("q-no-eligible-seat", QuestionText, CorrectAnswer)),
                limits: new QuizTimeLimits(
                    buzzTimeLimitSec: QuizTimeLimits.MaxBuzzOrAnswerTimeLimitSec,
                    answerTimeLimitSec: 15.0,
                    collectWindowSec: 0.15));
        }
    }
}
