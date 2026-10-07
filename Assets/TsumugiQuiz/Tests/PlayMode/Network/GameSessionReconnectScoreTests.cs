using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Core.Participants;
using TsumugiQuiz.Network;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 再接続で得点・お手つきペナルティが席（名簿エントリ）ごと引き継がれることを、実際に接続させて
    /// 検証する PlayMode テスト（#84 の受け入れ条件、docs/network.md §2.4 の引き継ぎ表）。
    /// </summary>
    /// <remarks>
    /// 名簿（<see cref="LobbyState"/>）と進行（<see cref="GameSession"/>）の両方が要るため、
    /// #69 の <see cref="LobbyStateTestFixture"/>（ホスト + クライアント 2）に
    /// <c>GameSession</c> のスポーンを足して使う（<see cref="UsesGameSession"/>）。
    /// NGO はクライアント ID を使い回さない（NGO 2.13.2 の <c>m_NextClientId++</c>）ので、
    /// 復帰したクライアントは必ず別の <c>clientId</c> になる。
    /// </remarks>
    public class GameSessionReconnectScoreTests : LobbyStateTestFixture
    {
        private const string PlayerName = "つむぎ";
        private const string OtherPlayerName = "ひかり";
        private const string QuestionText = "日本の首都はどこ？";
        private const string SecondQuestionText = "日本でいちばん高い山は？";
        private const string SecondCorrectAnswer = "ふじさん";
        private const string CorrectAnswer = "とうきょう";
        private const string WrongAnswer = "おおさか";

        /// <summary>この部屋で <c>network.allowLateJoin</c> に設定した値（出題後も保たれることを確かめる）。</summary>
        private bool _allowLateJoin;

        /// <inheritdoc />
        protected override bool UsesGameSession => true;

        [UnityTest]
        public IEnumerator Score_IsCarriedOverToTheNewClientId_WhenReconnectingWithToken()
        {
            StartRoom();

            yield return ConnectClient(0, PlayerName);
            var token = SessionToken.None;
            yield return WaitForSessionToken(0, received => token = received);
            Assert.IsTrue(token.HasValue, "ホストは承認したクライアントへトークンを発行するはず。");

            var previousClientId = GetClientId(0);
            yield return AnswerCurrentQuestion(0, CorrectAnswer);

            yield return WaitUntil(
                () => HostSession.GetServerScore(previousClientId) == ScoreRules.DefaultCorrectPoints,
                () => $"正解で加点されるはず（実際: {HostSession.GetServerScore(previousClientId)}）。");

            // --- 切断しても席は残るので、得点も残る ---
            yield return DisconnectClient(0);
            yield return WaitUntil(
                () => TryFindEntryByClientId(HostLobby, previousClientId, out var e) && !e.IsConnected,
                () => "切断が名簿へ反映されるはず。");
            Assert.AreEqual(
                ScoreRules.DefaultCorrectPoints,
                HostSession.GetServerScore(previousClientId),
                "切断しただけでは得点を捨てない（席は保持期間の間そのまま）。");

            // --- トークンで同じ席へ復帰する ---
            yield return ConnectClient(0, PlayerName, token);
            var clientId = GetClientId(0);
            Assert.AreNotEqual(
                previousClientId, clientId, "NGO はクライアント ID を使い回さないので別の ID になるはず。");

            yield return WaitUntil(
                () => HostSession.GetServerScore(clientId) == ScoreRules.DefaultCorrectPoints,
                () => "復帰したクライアントの新しい ID へ得点が移るはず"
                      + $"（実際: {HostSession.GetServerScore(clientId)}）。");

            Assert.AreEqual(0, HostSession.GetServerScore(previousClientId), "古い ID の得点は残らない。");
            Assert.AreEqual(1, HostSession.ScoreCount, "得点表の行は増えない（別人として二重に載らない）。");

            // --- クライアント側（NetworkList の同期）にも新しい ID の行が届く ---
            GameSession rejoinedSession = null;
            yield return WaitUntil(
                () =>
                {
                    rejoinedSession = FindClientSession(0);
                    return rejoinedSession != null
                           && rejoinedSession.GetScore(clientId) == ScoreRules.DefaultCorrectPoints;
                },
                () => "復帰したクライアントへ自分の得点が同期されるはず"
                      + $"（行数: {(FindClientSession(0) == null ? -1 : FindClientSession(0).ScoreCount)}）。");
            Assert.AreEqual(1, rejoinedSession.ScoreCount, "クライアント側の得点表も 1 行のまま。");
        }

        /// <summary>
        /// #163: 旧接続がまだ生きている（ホストが切断を検知していない）うちに、同じ名前・同じトークンの
        /// 2 本目が接続したら、旧接続を切って席（得点）を 2 本目へ引き継ぐ。
        /// クライアントの強制終了直後の再起動と同じ状況を、旧接続を実際に生かしたまま作る。
        /// </summary>
        [UnityTest]
        public IEnumerator Takeover_WhileOldConnectionIsAlive_MovesSeatAndScoreAndDisconnectsOldClient()
        {
            StartRoom();

            yield return ConnectClient(0, PlayerName);
            var token = SessionToken.None;
            yield return WaitForSessionToken(0, received => token = received);
            Assert.IsTrue(token.HasValue, "ホストは承認したクライアントへトークンを発行するはず。");

            var oldClientId = GetClientId(0);
            yield return AnswerCurrentQuestion(0, CorrectAnswer);
            yield return WaitUntil(
                () => HostSession.GetServerScore(oldClientId) == ScoreRules.DefaultCorrectPoints,
                () => $"正解で加点されるはず（実際: {HostSession.GetServerScore(oldClientId)}）。");

            // 旧接続（クライアント 0）は生きたまま。同じ名前・同じトークンでクライアント 1 が接続する。
            var oldClientManager = ClientManagers[0];
            yield return ConnectClient(1, PlayerName, token);
            var newClientId = GetClientId(1);
            Assert.AreNotEqual(oldClientId, newClientId);

            // --- 旧接続はホストから切られる（理由付き） ---
            yield return WaitUntil(
                () => !oldClientManager.IsConnectedClient && !oldClientManager.IsListening,
                () => "席を明け渡した旧接続はホストから切断されるはず。");
            StringAssert.Contains(
                LobbyState.SeatTakenOverDisconnectReason,
                GetDisconnectReason(0),
                "旧接続には「別の接続が同じ席を引き継いだ」理由が届くはず（L-1）。");

            // --- 席と得点は新しいクライアント ID へ ---
            yield return WaitUntil(
                () => HostSession.GetServerScore(newClientId) == ScoreRules.DefaultCorrectPoints,
                () => $"得点が新しい ID へ移るはず（実際: {HostSession.GetServerScore(newClientId)}）。");
            Assert.AreEqual(0, HostSession.GetServerScore(oldClientId), "旧 ID の得点は残らない。");
            Assert.AreEqual(1, HostSession.ScoreCount, "得点表の行は増えない（別人として二重に載らない）。");

            // --- 名簿: 同名の接続中エントリは 1 つだけで、それが新しい ID ---
            yield return WaitUntil(
                () => !TryFindEntryByClientId(HostLobby, oldClientId, out _),
                () => "旧 ID のエントリは名簿に残らないはず（席ごと新しい ID へ移る）。");
            var connectedWithName = 0;
            foreach (var entry in HostLobby.Players)
            {
                if (entry.IsConnected && entry.GetName() == PlayerName)
                {
                    connectedWithName++;
                    Assert.AreEqual(newClientId, entry.ClientId, "接続中のエントリは新しい ID のはず。");
                }
            }

            Assert.AreEqual(1, connectedWithName, "同名の接続中エントリが 2 つ残らないこと。");
        }

        [UnityTest]
        public IEnumerator ScoreTable_KeepsOtherPlayersRows_WhenOneClientReconnects()
        {
            // 得点表を 2 行にして、付け替え（Clear → 全件 Add の書き直し）が
            // 他の人の行を壊さないこと・並び順が保たれることを確かめる（レビュー M-1）。
            StartRoom();

            yield return ConnectClient(0, PlayerName);
            var token = SessionToken.None;
            yield return WaitForSessionToken(0, received => token = received);
            yield return ConnectClient(1, OtherPlayerName);

            var previousClientId = GetClientId(0);
            var otherClientId = GetClientId(1);

            yield return AnswerCurrentQuestion(0, CorrectAnswer);
            yield return WaitUntil(
                () => HostSession.GetServerScore(previousClientId) == ScoreRules.DefaultCorrectPoints,
                () => "1 人目の正解で加点されるはず。");

            yield return AnswerCurrentQuestion(1, CorrectAnswer);
            yield return WaitUntil(
                () => HostSession.GetServerScore(otherClientId) == ScoreRules.DefaultCorrectPoints,
                () => "2 人目の正解で加点されるはず。");
            Assert.AreEqual(2, HostSession.ScoreCount, "得点表は 2 行。");

            // --- 1 人目だけが切断 → トークンで復帰 ---
            yield return DisconnectClient(0);
            yield return WaitUntil(
                () => TryFindEntryByClientId(HostLobby, previousClientId, out var e) && !e.IsConnected,
                () => "切断が名簿へ反映されるはず。");

            yield return ConnectClient(0, PlayerName, token);
            var clientId = GetClientId(0);

            yield return WaitUntil(
                () => HostSession.GetServerScore(clientId) == ScoreRules.DefaultCorrectPoints,
                () => $"復帰した本人へ得点が移るはず（実際: {HostSession.GetServerScore(clientId)}）。");

            Assert.AreEqual(2, HostSession.ScoreCount, "行数は変わらない。");
            Assert.AreEqual(
                ScoreRules.DefaultCorrectPoints,
                HostSession.GetServerScore(otherClientId),
                "付け替えで他の人の得点が壊れないこと。");
            Assert.AreEqual(0, HostSession.GetServerScore(previousClientId), "古い ID の行は残らない。");
            AssertScoreSnapshotIsSortedByClientId(HostSession, "ホスト");

            // --- 復帰したクライアントにも 2 行そろって届く ---
            GameSession rejoinedSession = null;
            yield return WaitUntil(
                () =>
                {
                    rejoinedSession = FindClientSession(0);
                    return rejoinedSession != null
                           && rejoinedSession.ScoreCount == 2
                           && rejoinedSession.GetScore(clientId) == ScoreRules.DefaultCorrectPoints;
                },
                () => "復帰したクライアントへ得点表の全行が届くはず"
                      + $"（行数: {(FindClientSession(0) == null ? -1 : FindClientSession(0).ScoreCount)}）。");
            Assert.AreEqual(
                ScoreRules.DefaultCorrectPoints,
                rejoinedSession.GetScore(otherClientId),
                "他の人の得点も正しく届くこと。");
            AssertScoreSnapshotIsSortedByClientId(rejoinedSession, "復帰したクライアント");

            // --- つながったままのクライアントも、書き直しの差分を受けて 2 行のまま ---
            var stayedSession = FindClientSession(1);
            Assert.IsNotNull(stayedSession, "つながったままのクライアントにも GameSession があるはず。");
            yield return WaitUntil(
                () => stayedSession.ScoreCount == 2
                      && stayedSession.GetScore(clientId) == ScoreRules.DefaultCorrectPoints
                      && stayedSession.GetScore(otherClientId) == ScoreRules.DefaultCorrectPoints,
                () => "つながったままのクライアントにも付け替え後の得点表が届くはず"
                      + $"（行数: {stayedSession.ScoreCount}）。");
            AssertScoreSnapshotIsSortedByClientId(stayedSession, "つながったままのクライアント");
        }

        [UnityTest]
        public IEnumerator SkipNextPenalty_IsCarriedOverToTheNewClientId_WhenReconnectingWithToken()
        {
            StartRoom();

            yield return ConnectClient(0, PlayerName);
            var token = SessionToken.None;
            yield return WaitForSessionToken(0, received => token = received);

            var previousClientId = GetClientId(0);
            yield return AnswerCurrentQuestion(0, WrongAnswer);

            yield return WaitUntil(
                () => HostSession.IsServerPenalized(previousClientId),
                () => "お手つき（誤答）でペナルティが付くはず。");

            yield return DisconnectClient(0);
            yield return WaitUntil(
                () => TryFindEntryByClientId(HostLobby, previousClientId, out var e) && !e.IsConnected,
                () => "切断が名簿へ反映されるはず。");
            Assert.IsTrue(
                HostSession.IsServerPenalized(previousClientId),
                "切断しただけではペナルティを捨てない（捨てると切断で罰から逃れられる）。");

            yield return ConnectClient(0, PlayerName, token);
            var clientId = GetClientId(0);

            yield return WaitUntil(
                () => HostSession.IsServerPenalized(clientId),
                () => "復帰したクライアントの新しい ID へペナルティが移るはず。");
            Assert.IsFalse(
                HostSession.IsServerPenalized(previousClientId), "古い ID のペナルティは残らない。");

            // #194: 参加者パネルの「回答権なし」も新しい ID で復帰したクライアントへ届く
            // （NetworkVariable なのでスポーン時の同期で現在値が届き、再同期の RPC に載せ直す必要がない）。
            GameSession rejoinedSession = null;
            yield return WaitUntil(
                () =>
                {
                    rejoinedSession = FindClientSession(0);
                    return rejoinedSession != null
                           && rejoinedSession.GetQuestionProgress().TryGet(clientId, out var progress)
                           && progress.Flags.Has(ParticipantProgressFlags.WrongAnswered);
                },
                () => "復帰したクライアントへ新しい ID の「回答権なし」が同期されるはず。");
            Assert.IsFalse(
                rejoinedSession.GetQuestionProgress().TryGet(previousClientId, out _),
                "古い ID の行は残らない。");
            Assert.IsTrue(rejoinedSession.GetQuestionProgress().IsExcludedFromBuzzing(clientId));
        }

        [UnityTest]
        public IEnumerator ImposterWithoutToken_DoesNotInheritTheScoreOfADisconnectedPlayer()
        {
            // 途中参加を許可した部屋（許可しないと進行中の新規参加が拒否され、なりすましを試せない）。
            StartRoom(allowLateJoin: true);

            yield return ConnectClient(0, PlayerName);
            var token = SessionToken.None;
            yield return WaitForSessionToken(0, received => token = received);

            var previousClientId = GetClientId(0);
            yield return AnswerCurrentQuestion(0, CorrectAnswer);
            yield return WaitUntil(
                () => HostSession.GetServerScore(previousClientId) == ScoreRules.DefaultCorrectPoints,
                () => "正解で加点されるはず。");

            yield return DisconnectClient(0);
            yield return WaitUntil(
                () => TryFindEntryByClientId(HostLobby, previousClientId, out var e) && !e.IsConnected,
                () => "切断が名簿へ反映されるはず。");

            // --- 同名・トークン無しの別クライアントは「新規参加」なので、別の席になる ---
            yield return ConnectClient(1, PlayerName);
            var imposterClientId = GetClientId(1);

            yield return WaitUntil(
                () => CountEntries(HostLobby) == 3,
                () => $"同名でも新規の席として増えるはず（実際: {CountEntries(HostLobby)}）。");

            Assert.AreEqual(0, HostSession.GetServerScore(imposterClientId), "なりすましに得点は付かない。");
            Assert.AreEqual(
                ScoreRules.DefaultCorrectPoints,
                HostSession.GetServerScore(previousClientId),
                "得点は本人の席に残ったまま。");
            Assert.AreEqual(1, HostSession.ScoreCount, "得点表の行も増えない。");

            // --- 本人がトークンで復帰すると、得点は本人の新しい ID へ移る ---
            yield return ConnectClient(0, PlayerName, token);
            var clientId = GetClientId(0);

            yield return WaitUntil(
                () => HostSession.GetServerScore(clientId) == ScoreRules.DefaultCorrectPoints,
                () => "本人の復帰で得点が引き継がれるはず"
                      + $"（実際: {HostSession.GetServerScore(clientId)}）。");
            Assert.AreEqual(
                0, HostSession.GetServerScore(imposterClientId), "復帰してもなりすましの得点は 0 のまま。");
            Assert.AreEqual(1, HostSession.ScoreCount, "得点表は 1 行のまま。");
        }

        /// <summary>
        /// #109: 得点を持ったクライアントが出題中に切断 → トークンで復帰すると、
        /// 現在出題中の問題（DTO）が送り直され、得点も引き継がれていること。
        /// </summary>
        /// <remarks>
        /// 問題データ（DTO）と提示の合図は RPC なので、再接続で <c>NetworkObject</c> が
        /// スポーンし直されたクライアントには届かない（キャッシュも空になる）。
        /// サーバー（<see cref="LobbyState"/>）が接続完了時に
        /// <c>GameSession.ResyncClient</c> を呼ぶ配線が無いと、復帰したクライアントは
        /// 次の問題まで問題文が空のままになる。
        /// </remarks>
        [UnityTest]
        public IEnumerator CurrentQuestion_IsResentToTheReconnectedClient_WithTheCarriedOverScore()
        {
            StartRoom(withSecondQuestion: true);

            yield return ConnectClient(0, PlayerName);
            var token = SessionToken.None;
            yield return WaitForSessionToken(0, received => token = received);
            Assert.IsTrue(token.HasValue, "ホストは承認したクライアントへトークンを発行するはず。");

            var previousClientId = GetClientId(0);

            // --- 1 問目に正解して得点を作る ---
            yield return AnswerCurrentQuestion(0, CorrectAnswer);
            yield return WaitUntil(
                () => HostSession.GetServerScore(previousClientId) == ScoreRules.DefaultCorrectPoints,
                () => $"正解で加点されるはず（実際: {HostSession.GetServerScore(previousClientId)}）。");

            // --- 2 問目を出題して「出題中」の状態を作る ---
            yield return WaitUntil(
                () => HostSession.Phase.Value == QuizPhase.Result,
                () => $"1 問目の結果表示に入るはず（現在: {HostSession.Phase.Value}）。");
            Assert.IsTrue(HostSession.StartQuestion(1), "2 問目を出題できるはず。");

            // --- 出題中に切断 → トークンで同じ席へ復帰 ---
            yield return DisconnectClient(0);
            yield return WaitUntil(
                () => TryFindEntryByClientId(HostLobby, previousClientId, out var e) && !e.IsConnected,
                () => "切断が名簿へ反映されるはず。");
            Assert.IsTrue(
                QuizPhases.IsQuestionInProgress(HostSession.Phase.Value),
                $"復帰時点でも 2 問目が進行中のはず（現在: {HostSession.Phase.Value}）。");

            yield return ConnectClient(0, PlayerName, token);
            var clientId = GetClientId(0);
            Assert.AreNotEqual(
                previousClientId, clientId, "NGO はクライアント ID を使い回さないので別の ID になるはず。");

            // --- 現在問の DTO が再送され、クライアント側の配信キャッシュに入る ---
            GameSession rejoinedSession = null;
            QuestionDto resentQuestion = null;
            yield return WaitUntil(
                () =>
                {
                    rejoinedSession = FindClientSession(0);
                    return rejoinedSession != null
                           && rejoinedSession.Distributor != null
                           && rejoinedSession.Distributor.TryGetQuestion(1, out resentQuestion);
                },
                () => "復帰したクライアントへ現在出題中の問題が再送されるはず"
                      + $"（サーバーのフェーズ: {HostSession.Phase.Value}）。");

            Assert.AreEqual(SecondQuestionText, resentQuestion.Text, "再送されるのは現在出題中の問題。");

            // NetworkVariable の同期（問題インデックス・フェーズ）は次の tick で届くので待ってから見る。
            yield return WaitUntil(
                () => rejoinedSession.QuestionIndex.Value == 1
                      && QuizPhases.IsQuestionInProgress(rejoinedSession.Phase.Value),
                () => "復帰したクライアントへ現在の問題インデックスと進行中フェーズが同期されるはず"
                      + $"（クライアント: {rejoinedSession.QuestionIndex.Value} / {rejoinedSession.Phase.Value}、"
                      + $"サーバー: {HostSession.QuestionIndex.Value} / {HostSession.ServerPhase}）。");

            // --- 得点は席ごと引き継がれている（#84） ---
            yield return WaitUntil(
                () => rejoinedSession.GetScore(clientId) == ScoreRules.DefaultCorrectPoints,
                () => "復帰したクライアントへ自分の得点が同期されるはず"
                      + $"（実際: {rejoinedSession.GetScore(clientId)}）。");
            Assert.AreEqual(
                ScoreRules.DefaultCorrectPoints,
                HostSession.GetServerScore(clientId),
                "サーバー側でも新しいクライアント ID へ得点が移っているはず。");
        }

        /// <summary>
        /// ホストを立て、freeText の供給元（既定は 1 問）を設定する。
        /// </summary>
        /// <param name="allowLateJoin">途中参加を許可するか（<c>network.allowLateJoin</c>）。</param>
        /// <param name="withSecondQuestion">
        /// 2 問目（<see cref="SecondQuestionText"/>）も供給するか。出題中の再接続を試すテスト（#109）で使う。
        /// </param>
        private void StartRoom(bool allowLateJoin = false, bool withSecondQuestion = false)
        {
            StartHostAndSpawnLobby(maxPlayers: 6, allowLateJoin: allowLateJoin);
            _allowLateJoin = allowLateJoin;

            var questions = withSecondQuestion
                ? new[]
                {
                    TestQuestionSource.FreeText("q-reconnect-1", QuestionText, CorrectAnswer),
                    TestQuestionSource.FreeText("q-reconnect-2", SecondQuestionText, SecondCorrectAnswer),
                }
                : new[] { TestQuestionSource.FreeText("q-reconnect-1", QuestionText, CorrectAnswer) };

            if (withSecondQuestion)
            {
                // 2 問目を「出題中のまま」保ちたいので、早押し受付のタイムアウトを十分長く取る
                // （既定の 10 秒だと、切断 → 再接続を待つ間に結果表示へ進んでしまう）。
                HostSession.Configure(
                    new TestQuestionSource(questions),
                    limits: new QuizTimeLimits(
                        buzzTimeLimitSec: 60.0, answerTimeLimitSec: 60.0, collectWindowSec: 0.15));
                return;
            }

            HostSession.Configure(new TestQuestionSource(questions));
        }

        /// <summary>
        /// 出題 → 指定クライアントが押下 → 回答、まで進める。
        /// </summary>
        /// <param name="index">回答するクライアントの番号。</param>
        /// <param name="answer">送る回答文字列。</param>
        private IEnumerator AnswerCurrentQuestion(int index, string answer)
        {
            GameSession clientSession = null;
            yield return WaitUntil(
                () => (clientSession = FindClientSession(index)) != null,
                () => $"クライアント {index} 側に GameSession が生成されませんでした。");

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            // #27: 出題はルーム設定を確定（ロック）させ、その通知でロビーの設定が書き直される。
            // テストの前提（途中参加の可否）が静かに変わっていないことをここで固定する（レビュー H-1）。
            Assert.AreEqual(
                _allowLateJoin,
                HostLobby.AllowLateJoin.Value,
                "出題（ルーム設定の確定）で network.allowLateJoin が書き換わらないこと。");

            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"早押し受付が開きませんでした（現在: {clientSession.Phase.Value}）。");

            Assert.IsTrue(clientSession.RequestBuzz(), "受付中なので押下を送れるはず。");

            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Answering,
                () => $"回答フェーズに進みませんでした（現在: {clientSession.Phase.Value}）。");

            Assert.IsTrue(clientSession.RequestAnswer(answer), "勝者は回答を送れるはず。");
        }

        /// <summary>得点表（<c>NetworkList</c>）がクライアント ID の昇順に並んでいることを確かめる。</summary>
        /// <param name="session">確かめる <see cref="GameSession"/>。</param>
        /// <param name="label">失敗メッセージに出す対象の名前。</param>
        private static void AssertScoreSnapshotIsSortedByClientId(GameSession session, string label)
        {
            var rows = session.GetScoreSnapshot();
            for (var i = 1; i < rows.Count; i++)
            {
                Assert.Less(
                    rows[i - 1].ClientId,
                    rows[i].ClientId,
                    $"{label}の得点表はクライアント ID の昇順のはず。");
            }
        }
    }
}
