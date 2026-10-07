using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.UI.Views.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 押せる参加者が居なくなったら、早押しの時間切れ（<c>buzz.timeLimitSec</c>）を待たずに Result へ進むこと
    /// （#200、docs/network.md §6.6）を、ホスト + クライアント 2 台の実ネットワーク経路で通す統合テスト。
    /// </summary>
    /// <remarks>
    /// 早押しの持ち時間を上限の 60 秒にしてあり、待ち（<see cref="GameSessionTestFixture"/> の 20 秒）の間に
    /// 時間切れで Result へ進むことはない。修正が無い（判定シームが繋がっていない）と、
    /// 全員が誤答しても受付を開き直して 60 秒待つため、これらのテストは失敗する。
    /// 切断中の席・参加者 0 人の扱いは名簿が要るので <see cref="GameSessionNoEligibleBuzzerSeatTests"/> で確かめる。
    /// </remarks>
    public class GameSessionNoEligibleBuzzerTests : GameSessionTestFixture
    {
        private static QuizTimeLimits LongBuzzLimits =>
            new QuizTimeLimits(
                buzzTimeLimitSec: QuizTimeLimits.MaxBuzzOrAnswerTimeLimitSec,
                answerTimeLimitSec: 15.0,
                collectWindowSec: 0.15);

        private GameObject _lobbyPrefab;
        private GameObject _hostLobbyObject;

        [UnityTearDown]
        public IEnumerator TearDownLobbyState()
        {
            // 基底の TearDown（Shutdown）より前に走る（NUnit は派生クラスの TearDown を先に実行する）。
            if (_hostLobbyObject != null)
            {
                Object.DestroyImmediate(_hostLobbyObject);
                _hostLobbyObject = null;
            }

            _lobbyPrefab = null;
            yield break;
        }

        [UnityTest]
        public IEnumerator EveryoneWrong_GoesToResultWithoutWaitingForBuzzTimeLimit()
        {
            // ホストも参加者（LobbyState なし = host.role 既定の player と同じ扱い）。3 人とも誤答する。
            yield return ConnectHostAndClient(limits: LongBuzzLimits);
            yield return ConnectSecondClient();

            var hostSession = HostSession;
            var sessionA = ClientSession;
            var sessionB = SecondClientSession;
            var hostId = HostManager.LocalClientId;
            var clientA = ClientManager.LocalClientId;
            var clientB = SecondClientManager.LocalClientId;

            var results = new List<(QuizJudgement Judgement, ulong ClientId)>();
            sessionB.QuestionResolved += (judgement, clientId, answer, score, delta) => results.Add((judgement, clientId));

            Assert.IsTrue(hostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntilAllBuzzOpen();

            yield return BuzzAndAnswerWrong(sessionA, clientA, "A");
            yield return WaitUntilAllBuzzOpen();

            // 早押しボタンの有効/無効（同期された進行状態で判定、#194）: まだ押せる B は有効、誤答した A は無効。
            yield return WaitUntil(
                () => IsBuzzButtonEnabled(sessionB, clientB) && !IsBuzzButtonEnabled(sessionA, clientA),
                () => "再開放後の早押しボタンの状態が想定と違います"
                    + $"（A: {IsBuzzButtonEnabled(sessionA, clientA)} / B: {IsBuzzButtonEnabled(sessionB, clientB)}）。");

            yield return BuzzAndAnswerWrong(sessionB, clientB, "B");
            yield return WaitUntilAllBuzzOpen();
            Assert.IsEmpty(results, "ホストがまだ押せるので結果は出ない。");

            // 最後の 1 人（ホスト）が誤答すると、押せる人が居なくなる。
            yield return BuzzAndAnswerWrong(hostSession, hostId, "ホスト");

            yield return WaitUntil(
                () => results.Count == 1 && sessionA.Phase.Value == QuizPhase.Result && sessionB.Phase.Value == QuizPhase.Result,
                () => $"全員が誤答したのに結果へ進みませんでした（A: {sessionA.Phase.Value} / B: {sessionB.Phase.Value}、"
                    + $"サーバー: {hostSession.ServerPhase}、結果 {results.Count} 件）。");

            Assert.AreEqual(QuizJudgement.Wrong, results[0].Judgement, "最後の回答者の誤答として結果を出す。");
            Assert.AreEqual(hostId, results[0].ClientId);

            var history = hostSession.ServerPhaseHistory;
            CollectionAssert.AreEqual(
                new[] { QuizPhase.Answering, QuizPhase.Judging, QuizPhase.Result },
                history.Skip(history.Count - 3).ToArray(),
                "最後の誤答のあと受付を開き直さずに Result へ進むはず。");
            Assert.AreEqual(
                3,
                history.Count(phase => phase == QuizPhase.Judging),
                "3 人ぶんの判定を経ているはず。");

            // 参加者パネル（#194）の同期状態: 全員が誤答済み、早押しボタンは全員無効。
            yield return WaitUntil(
                () => AllWrongAnswered(sessionA, hostId, clientA, clientB) && AllWrongAnswered(sessionB, hostId, clientA, clientB),
                () => "参加者パネルの進行状態（全員の誤答済み）がクライアントへ同期されませんでした。");
            Assert.IsFalse(IsBuzzButtonEnabled(sessionA, clientA));
            Assert.IsFalse(IsBuzzButtonEnabled(sessionB, clientB));
        }

        [UnityTest]
        public IEnumerator ModeratorHost_IsNotCounted_TwoClientsWrongGoesToResult()
        {
            RegisterLobbyStatePrefab();
            yield return ConnectHostAndClient(limits: LongBuzzLimits);
            yield return ConnectSecondClient();
            SpawnHostLobbyState(HostRole.Moderator);

            var hostSession = HostSession;
            var sessionA = ClientSession;
            var sessionB = SecondClientSession;
            var clientA = ClientManager.LocalClientId;
            var clientB = SecondClientManager.LocalClientId;

            var results = new List<(QuizJudgement Judgement, ulong ClientId)>();
            sessionA.QuestionResolved += (judgement, clientId, answer, score, delta) => results.Add((judgement, clientId));

            Assert.IsTrue(hostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntilAllBuzzOpen();

            yield return BuzzAndAnswerWrong(sessionA, clientA, "A");
            yield return WaitUntilAllBuzzOpen();

            yield return BuzzAndAnswerWrong(sessionB, clientB, "B");

            yield return WaitUntil(
                () => results.Count == 1 && sessionA.Phase.Value == QuizPhase.Result,
                () => "司会専任のホストしか残っていないのに結果へ進みませんでした"
                    + $"（A: {sessionA.Phase.Value} / サーバー: {hostSession.ServerPhase}）。");
            Assert.AreEqual(QuizJudgement.Wrong, results[0].Judgement);
            Assert.AreEqual(clientB, results[0].ClientId);
        }

        private IEnumerator WaitUntilAllBuzzOpen()
        {
            yield return WaitUntil(
                () => HostSession.ServerPhase == QuizPhase.BuzzOpen
                    && ClientSession.Phase.Value == QuizPhase.BuzzOpen
                    && (SecondClientSession == null
                        || !SecondClientManager.IsConnectedClient
                        || SecondClientSession.Phase.Value == QuizPhase.BuzzOpen),
                () => $"受付が開きませんでした（サーバー: {HostSession.ServerPhase} / A: {ClientSession.Phase.Value}）。");
        }

        /// <summary>受付中の <paramref name="session"/> から押して、勝者になったら誤答を送る。</summary>
        private IEnumerator BuzzAndAnswerWrong(GameSession session, ulong clientId, string label)
        {
            Assert.IsTrue(session.RequestBuzz(), $"{label} は受付中なので押下を送れるはず。");
            yield return WaitUntil(
                () => session.Phase.Value == QuizPhase.Answering && session.LockedClientId.Value == clientId,
                () => $"{label} の回答フェーズに進みませんでした（{session.Phase.Value}、ロック {session.LockedClientId.Value}）。");
            Assert.IsTrue(session.RequestAnswer("おおさか"), $"勝者 {label} は回答を送れるはず。");
            yield return WaitUntil(
                () => HostSession.ServerPhase != QuizPhase.Answering,
                () => $"{label} の回答が判定されませんでした（サーバー: {HostSession.ServerPhase}）。");
        }

        /// <summary>画面（GameView）と同じ判定で、早押しボタンが有効になるか（押下錠は掛かっていない前提）。</summary>
        private static bool IsBuzzButtonEnabled(GameSession session, ulong localClientId)
        {
            var progress = session.GetQuestionProgress();
            var excluded = progress.QuestionIndex == session.QuestionIndex.Value
                && progress.IsExcludedFromBuzzing(localClientId);
            return GameViewPresenter.IsBuzzButtonEnabled(session.Phase.Value, hasBuzzedLocally: false, excluded);
        }

        private static bool AllWrongAnswered(GameSession session, params ulong[] clientIds)
        {
            var progress = session.GetQuestionProgress();
            return progress.QuestionIndex == session.QuestionIndex.Value
                && clientIds.All(progress.IsExcludedFromBuzzing);
        }

        /// <summary>
        /// <see cref="LobbyState"/> のプレハブを全員に登録する（<c>ForceSamePrefabs</c> のため接続前に呼ぶ）。
        /// </summary>
        private void RegisterLobbyStatePrefab()
        {
            _lobbyPrefab = NetworkTestPrefabs.LoadLobbyState();
            HostManager.AddNetworkPrefab(_lobbyPrefab);
            ClientManager.AddNetworkPrefab(_lobbyPrefab);
            SecondClientManager.AddNetworkPrefab(_lobbyPrefab);
        }

        /// <summary>ホスト側で <see cref="LobbyState"/> をスポーンし、ホストの役割を設定する（接続後に呼ぶ）。</summary>
        private void SpawnHostLobbyState(HostRole hostRole)
        {
            var spawned = HostManager.SpawnManager.InstantiateAndSpawn(_lobbyPrefab.GetComponent<NetworkObject>());
            Assert.IsNotNull(spawned, "LobbyState をスポーンできるはず。");

            _hostLobbyObject = spawned.gameObject;
            _hostLobbyObject.name = "LobbyState(NoEligibleBuzzerTest)";

            var lobby = _hostLobbyObject.GetComponent<LobbyState>();
            Assert.IsNotNull(lobby, "LobbyState プレハブに LobbyState が必要。");
            lobby.ConfigureRoom(hostRole, LobbyRoster.DefaultMaxPlayers, allowLateJoin: false);
        }
    }
}
