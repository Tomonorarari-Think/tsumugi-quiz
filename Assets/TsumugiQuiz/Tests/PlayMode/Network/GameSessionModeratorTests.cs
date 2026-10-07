using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Room;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 司会専用モードの進行操作（#20、docs/tasks/setup-brief.md K18）を
    /// 実ネットワーク経路で通す統合テスト。
    /// 「強制正解 → 次へ → 一時停止 → 再開 → 全問終了 → ロビーへ戻る」の一連の流れと、
    /// 司会（ホスト）以外からの操作 RPC が拒否されること、通常モード（<c>HostRole.Player</c>）の
    /// ホストからの強制判定も拒否されることを確かめる。
    /// </summary>
    public class GameSessionModeratorTests : GameSessionTestFixture
    {
        private const int Seed = 20260913;

        /// <summary>
        /// 早押し受付の締め切りまで最低限このくらいは残っていることを確認してから
        /// <c>RequestBuzz()</c> を送る（#176）。<see cref="WaitUntilBuzzOpenWithMargin"/> 参照。
        /// </summary>
        private const double BuzzMarginSeconds = 1.0;

        private GameObject _lobbyPrefab;
        private GameObject _hostLobbyObject;

        /// <summary>
        /// このテストで使う制限時間。フェーズ遷移は司会操作（強制正解・次へ・一時停止/再開）で進めるため、
        /// 早押し・回答の制限時間自体が短くても本来はテストの進行に支障はないはずだった。
        /// しかし #176: PlayMode 通し実行の負荷でフレームが大きく遅延すると、
        /// <c>RequestBuzz()</c> の RPC がサーバーに届く前に受付の締め切り（旧: 3 秒）を過ぎてしまい、
        /// 早押しが棄却されたままタイムアウトで結果まで進んでしまうことがあった。
        /// 制限時間そのものを十分長く（30 秒）取ることで RPC 到達までの余裕を確保する。
        /// 一時停止/再開の検証（<see cref="Moderator_ForceCorrectThenNextThenPauseResume_FinishesAndReturnsToLobby"/>）は、
        /// 制限時間いっぱいの実タイムアウトを待つのではなく、
        /// <see cref="GameSession.CurrentDeadlineServerTime"/>（issue #154）の差分で
        /// 「一時停止していた秒数だけ締め切りが後ろへずれる」ことだけを確かめるため、
        /// 制限時間を長く取っても実行時間は伸びない（本体コードではなくテストのみの変更）。
        /// </summary>
        private static QuizTimeLimits FastLimits =>
            new QuizTimeLimits(buzzTimeLimitSec: 30.0, answerTimeLimitSec: 30.0, collectWindowSec: 0.15);

        [UnityTearDown]
        public IEnumerator TearDownLobbyState()
        {
            // GameSessionTestFixture.TearDown（基底、ホスト/クライアントの Shutdown）より前に走る
            // （NUnit は派生クラスの TearDown を基底より先に実行する）。
            if (_hostLobbyObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostLobbyObject);
                _hostLobbyObject = null;
            }

            _lobbyPrefab = null;
            yield break;
        }

        /// <summary>
        /// <see cref="LobbyState"/> を（<see cref="GameSessionTestFixture.HostManager"/> /
        /// <see cref="GameSessionTestFixture.ClientManager"/> と同じプロセスに）スポーンし、
        /// ホストの役割（<c>host.role</c>）を設定する。<see cref="GameSessionTestFixture.ConnectHostAndClient"/> の
        /// <b>前</b>に呼ぶこと（<c>NetworkConfig.ForceSamePrefabs</c> のため、プレハブ登録は接続前に済ませる）。
        /// </summary>
        private void RegisterLobbyStatePrefab()
        {
            _lobbyPrefab = NetworkTestPrefabs.LoadLobbyState();
            HostManager.AddNetworkPrefab(_lobbyPrefab);
            ClientManager.AddNetworkPrefab(_lobbyPrefab);
        }

        /// <summary>
        /// ホスト側で <see cref="LobbyState"/> をスポーンし、役割を設定する。
        /// <c>ConnectHostAndClient</c> の後に呼ぶこと（ホストが開始済みである必要があるため）。
        /// </summary>
        private void SpawnHostLobbyState(HostRole hostRole)
        {
            var lobbyNetworkObject = _lobbyPrefab.GetComponent<NetworkObject>();
            var spawned = HostManager.SpawnManager.InstantiateAndSpawn(lobbyNetworkObject);
            Assert.IsNotNull(spawned, "LobbyState をスポーンできるはず。");

            _hostLobbyObject = spawned.gameObject;
            _hostLobbyObject.name = "LobbyState(ModeratorTest)";

            var lobby = _hostLobbyObject.GetComponent<LobbyState>();
            Assert.IsNotNull(lobby, "LobbyState プレハブに LobbyState が必要。");
            lobby.ConfigureRoom(hostRole, LobbyRoster.DefaultMaxPlayers, allowLateJoin: false);
        }

        [UnityTest]
        public IEnumerator Moderator_ForceCorrectThenNextThenPauseResume_FinishesAndReturnsToLobby()
        {
            RegisterLobbyStatePrefab();
            yield return ConnectHostAndClient(TwoQuestionSource());
            SpawnHostLobbyState(HostRole.Moderator);

            var hostSession = HostSession;
            var clientSession = ClientSession;
            var clientId = ClientManager.LocalClientId;

            var shown = new List<int>();
            clientSession.QuestionShown += (index, _, _) => shown.Add(index);

            var results = new List<QuizJudgement>();
            clientSession.QuestionResolved += (judgement, answerer, correctAnswer, score, delta) => results.Add(judgement);

            var finalScores = new List<IReadOnlyList<ScoreEntry>>();
            clientSession.SessionFinished += entries => finalScores.Add(entries);

            var returnedToLobbyOnHost = 0;
            var returnedToLobbyOnClient = 0;
            hostSession.ReturnedToLobby += () => returnedToLobbyOnHost++;
            clientSession.ReturnedToLobby += () => returnedToLobbyOnClient++;

            var settings = new SessionSettings(
                new QuestionSelectionSettings(count: QuestionSelectionSettings.AllQuestions, shuffleOrder: false),
                FastLimits,
                ScoringSettings.Default,
                SessionSettings.ManualAdvance);

            Assert.IsTrue(hostSession.StartSession(settings, Seed), "セッションを開始できるはず。");

            // --- 1 問目: 司会が「強制正解」で判定する ---
            yield return WaitUntil(
                () => shown.Count >= 1,
                () => $"1 問目が提示されませんでした（提示 {shown.Count} 件）。");
            yield return WaitUntilBuzzOpenWithMargin(
                clientSession,
                () => $"1 問目の受付が開きませんでした（{clientSession.Phase.Value}）。");

            Assert.IsTrue(clientSession.RequestBuzz(), "受付中なので押下を送れるはず。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Answering,
                () => $"回答フェーズに進みませんでした（{clientSession.Phase.Value}）。");

            // 司会（ホスト）は自分自身がサーバーなので、Request* はローカルでそのままサーバーへ届く。
            Assert.IsTrue(hostSession.RequestForceJudge(QuizJudgement.Correct), "司会は強制正解を送れるはず。");

            yield return WaitUntil(
                () => results.Count >= 1,
                () => "1 問目の結果（強制正解）が届きませんでした。");
            Assert.AreEqual(QuizJudgement.Correct, results[0], "強制正解は通常の正解と同じ判定として配られるはず。");
            Assert.AreEqual(
                ScoreRules.DefaultCorrectPoints,
                hostSession.GetServerScore(clientId),
                "強制正解でも通常の正解と同じ得点処理を通るはず。");

            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Result,
                () => $"結果フェーズに進みませんでした（{clientSession.Phase.Value}）。");

            // --- 司会の「次へ」で 2 問目へ ---
            Assert.IsTrue(hostSession.RequestNextQuestion(), "司会は「次へ」を送れるはず。");

            yield return WaitUntil(
                () => shown.Count >= 2,
                () => $"2 問目へ進みませんでした（提示 {shown.Count} 件 / フェーズ {clientSession.Phase.Value}）。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"2 問目の受付が開きませんでした（{clientSession.Phase.Value}）。");

            // --- 2 問目の受付中に一時停止する ---
            // #176: 早押しの制限時間そのものが経過するのを実時間で待つと
            // PlayMode 通し実行が遅くなるため、実タイムアウトは待たず、
            // GameSession.CurrentDeadlineServerTime（issue #154）の差分で
            // 「一時停止していた秒数だけ締め切りが後ろへずれる」ことだけを確かめる。
            var deadlineBeforePause = hostSession.CurrentDeadlineServerTime;
            var serverTimeAtPauseRequest = HostManager.ServerTime.Time;
            Assert.IsTrue(hostSession.RequestPause(), "司会は一時停止を送れるはず。");
            yield return WaitUntil(
                () => clientSession.IsPaused.Value,
                () => "一時停止がクライアントへ同期されませんでした。");
            // 一時停止直後はまだ BuzzOpen のままのはず（この時点ではまだ実時間を経過させていないので、
            // これ自体は「一時停止中にタイムアウトしない」ことの検証ではない。その検証は
            // EditMode の QuizStateMachinePauseTests.Pause_DuringBuzzOpen_StopsTimeoutFromAdvancing /
            // Resume_AfterPause_PreservesRemainingTimeBeforeTimeout でカバーしている）。
            Assert.AreEqual(
                QuizPhase.BuzzOpen, hostSession.ServerPhase, "一時停止直後はまだ BuzzOpen のままのはず。");

            // 一時停止中に実時間を経過させる（下の下限チェックで検出できる程度に十分な長さを確保する）。
            yield return WaitSeconds(1.0);

            var serverTimeAtResumeRequest = HostManager.ServerTime.Time;
            Assert.IsTrue(hostSession.RequestResume(), "司会は再開を送れるはず。");
            yield return WaitUntil(
                () => !clientSession.IsPaused.Value,
                () => "再開がクライアントへ同期されませんでした。");

            var deadlineAfterResume = hostSession.CurrentDeadlineServerTime;
            var deadlineShift = deadlineAfterResume - deadlineBeforePause;
            var expectedShift = serverTimeAtResumeRequest - serverTimeAtPauseRequest;
            Assert.AreEqual(
                expectedShift, deadlineShift, 0.3,
                "一時停止していた秒数だけ受付の締め切りが後ろへずれるはず。");
            // 上の許容誤差だけでは「締め切りが全く延びない実装」でも±0.3秒の比較次第では
            // 誤って通ってしまう余地があるため、実際に締め切りが延びたこと自体も下限で確かめる
            // （一時停止は 1 秒以上経過させているので、0.5 秒より大きく延びていれば十分）。
            Assert.Greater(deadlineShift, 0.5, "一時停止によって受付の締め切りが実際に後ろへずれていないようです。");

            // --- 再開後は司会操作（早押し→強制正解→次へ）でそのまま進める ---
            yield return WaitUntilBuzzOpenWithMargin(
                clientSession,
                () => $"再開後に受付状態へ戻りませんでした（{clientSession.Phase.Value}）。");
            Assert.IsTrue(clientSession.RequestBuzz(), "受付中なので押下を送れるはず。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Answering,
                () => $"2 問目の回答フェーズに進みませんでした（{clientSession.Phase.Value}）。");

            Assert.IsTrue(hostSession.RequestForceJudge(QuizJudgement.Correct), "司会は 2 問目も強制正解を送れるはず。");
            yield return WaitUntil(
                () => results.Count >= 2,
                () => $"2 問目の結果が届きませんでした（結果 {results.Count} 件）。");
            Assert.AreEqual(QuizJudgement.Correct, results[1], "2 問目も強制正解として配られるはず。");

            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Result,
                () => $"2 問目の結果フェーズに進みませんでした（{clientSession.Phase.Value}）。");

            // --- 次が無いので全問終了 ---
            Assert.IsTrue(hostSession.RequestNextQuestion(), "司会は「次へ」（全問終了）を送れるはず。");

            yield return WaitUntil(
                () => finalScores.Count >= 1,
                () => $"最終得点が届きませんでした（{finalScores.Count} 件）。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Finished,
                () => $"クライアントに Finished が届きませんでした（{clientSession.Phase.Value}）。");

            // --- ロビーへ戻る ---
            Assert.IsTrue(hostSession.ReturnToLobby(), "結果表示中はロビーへ戻せるはず。");

            yield return WaitUntil(
                () => returnedToLobbyOnClient >= 1,
                () => "ロビーへ戻る合図がクライアントへ届きませんでした。");
            Assert.AreEqual(1, returnedToLobbyOnHost, "ホスト自身にも合図が届くはず。");
            Assert.AreEqual(QuizPhase.Lobby, hostSession.ServerPhase);
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Lobby,
                () => $"クライアントの表示フェーズが Lobby に戻りませんでした（{clientSession.Phase.Value}）。");
        }

        [UnityTest]
        public IEnumerator NonHostClient_CallingModeratorRpcsDirectly_AreRejectedByServer()
        {
            yield return ConnectHostAndClient(
                TwoQuestionSource(), scoring: ScoringSettings.Default.WithReopenAfterWrongAnswer(false), limits: FastLimits);

            var hostSession = HostSession;
            var clientSession = ClientSession;

            var results = new List<QuizJudgement>();
            clientSession.QuestionResolved += (judgement, answerer, correctAnswer, score, delta) => results.Add(judgement);

            Assert.IsTrue(hostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntilBuzzOpenWithMargin(
                clientSession,
                () => $"受付が開きませんでした（{clientSession.Phase.Value}）。");

            // --- 司会以外（クライアント）からの一時停止は拒否される ---
            clientSession.PauseRpc();
            yield return WaitFrames(10);
            Assert.IsFalse(hostSession.IsPaused.Value, "クライアントからの一時停止は拒否されるはず。");
            Assert.AreEqual(QuizPhase.BuzzOpen, hostSession.ServerPhase, "進行も動いてはいけない。");

            // --- 司会以外からの再開も拒否される（M6） ---
            Assert.IsTrue(hostSession.RequestPause(), "比較のため、まずホスト自身が一時停止する。");
            yield return WaitUntil(
                () => clientSession.IsPaused.Value,
                () => "ホストの一時停止が同期されませんでした。");

            clientSession.ResumeRpc();
            yield return WaitFrames(10);
            Assert.IsTrue(hostSession.IsPaused.Value, "クライアントからの再開は拒否され、一時停止したままのはず。");

            Assert.IsTrue(hostSession.RequestResume(), "ホスト自身の再開は受理されるはず。");
            yield return WaitUntil(
                () => !clientSession.IsPaused.Value,
                () => "ホストの再開が同期されませんでした。");

            // --- 司会以外からの強制判定は拒否される ---
            yield return WaitUntilBuzzOpenWithMargin(
                clientSession,
                () => $"再開後に受付状態へ戻りませんでした（{clientSession.Phase.Value}）。");
            Assert.IsTrue(clientSession.RequestBuzz(), "受付中なので押下を送れるはず。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Answering,
                () => $"回答フェーズに進みませんでした（{clientSession.Phase.Value}）。");

            clientSession.ForceJudgeRpc(QuizJudgement.Correct);
            yield return WaitFrames(10);
            Assert.AreEqual(
                QuizPhase.Answering, hostSession.ServerPhase, "クライアントからの強制判定で進行が動いてはいけない。");
            Assert.IsEmpty(results, "拒否されたので結果が配信されてはいけない。");

            // --- 司会以外からの「次へ」も拒否される（Result まで正規の手順で進めてから確認する） ---
            Assert.IsTrue(clientSession.RequestAnswer("まちがい"), "本人の回答は通常どおり受理される。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Result,
                () => $"結果フェーズに進みませんでした（{clientSession.Phase.Value}）。");

            var shownBefore = hostSession.QuestionIndex.Value;
            clientSession.NextQuestionRpc();
            yield return WaitFrames(10);
            Assert.AreEqual(
                shownBefore, hostSession.QuestionIndex.Value, "クライアントからの「次へ」で問題が進んではいけない。");
            Assert.AreEqual(QuizPhase.Result, hostSession.ServerPhase, "結果表示のままのはず。");
        }

        [UnityTest]
        public IEnumerator PlayerRoleHost_ForceJudge_IsRejected()
        {
            // 統括判断 M5: 通常モード（host.role == "player"）のホストは、自分自身が回答者になりうるため、
            // 強制正解/不正解（司会専用モードの操作）を行えない。「次へ」「一時停止」「再開」はホストなら可。
            RegisterLobbyStatePrefab();
            yield return ConnectHostAndClient(TwoQuestionSource());
            SpawnHostLobbyState(HostRole.Player);

            var hostSession = HostSession;
            var clientSession = ClientSession;

            var results = new List<QuizJudgement>();
            clientSession.QuestionResolved += (judgement, answerer, correctAnswer, score, delta) => results.Add(judgement);

            Assert.IsTrue(hostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntilBuzzOpenWithMargin(
                clientSession,
                () => $"受付が開きませんでした（{clientSession.Phase.Value}）。");

            Assert.IsTrue(clientSession.RequestBuzz(), "受付中なので押下を送れるはず。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Answering,
                () => $"回答フェーズに進みませんでした（{clientSession.Phase.Value}）。");

            // ホスト自身からの呼び出しでも、host.role が player なら拒否される。
            hostSession.ForceJudgeRpc(QuizJudgement.Correct);
            yield return WaitFrames(10);
            Assert.AreEqual(
                QuizPhase.Answering, hostSession.ServerPhase, "player ロールのホストからの強制判定は拒否されるはず。");
            Assert.IsEmpty(results, "拒否されたので結果が配信されてはいけない。");

            // 一方で「一時停止」「再開」「次へ」はホストであれば player ロールでも受理される。
            Assert.IsTrue(hostSession.RequestPause(), "player ロールのホストでも一時停止は送れるはず。");
            yield return WaitUntil(
                () => clientSession.IsPaused.Value,
                () => "一時停止がクライアントへ同期されませんでした。");
            Assert.IsTrue(hostSession.RequestResume(), "player ロールのホストでも再開は送れるはず。");
        }

        [UnityTest]
        public IEnumerator ModeratorHost_CannotBuzzOrAnswer()
        {
            // H3: 司会専用モードのホストは早押し・回答をしない。サーバー側も BuzzRpc / SubmitAnswerRpc を拒否する。
            RegisterLobbyStatePrefab();
            yield return ConnectHostAndClient(TwoQuestionSource(), limits: FastLimits);
            SpawnHostLobbyState(HostRole.Moderator);

            var hostSession = HostSession;
            var clientSession = ClientSession;

            Assert.IsTrue(hostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntilBuzzOpenWithMargin(
                clientSession,
                () => $"受付が開きませんでした（{clientSession.Phase.Value}）。");

            // 司会（ホスト）自身が早押ししようとしても拒否される。
            hostSession.BuzzRpc(HostManager.LocalTime.Time);
            yield return WaitFrames(10);
            Assert.AreEqual(
                GameSession.NoClientId, hostSession.LockedClientId.Value, "司会は早押しできないはず。");
            Assert.AreEqual(QuizPhase.BuzzOpen, hostSession.ServerPhase, "進行も動いてはいけない。");

            // 通常のプレイヤー（クライアント）は引き続き早押し・回答できる。
            yield return WaitUntilBuzzOpenWithMargin(
                clientSession,
                () => $"ホストの棄却後に受付状態が続いていませんでした（{clientSession.Phase.Value}）。");
            Assert.IsTrue(clientSession.RequestBuzz(), "クライアントは早押しできるはず。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Answering,
                () => $"回答フェーズに進みませんでした（{clientSession.Phase.Value}）。");

            // 司会（ホスト）はロック保持者ではないが、それでも SubmitAnswerRpc 自体が拒否されることを確かめる
            // （防御的な二重チェック。通常は NotLockedPlayer でも棄却される）。
            hostSession.SubmitAnswerRpc(new Unity.Collections.FixedString512Bytes(CorrectAnswer));
            yield return WaitFrames(10);
            Assert.AreEqual(QuizPhase.Answering, hostSession.ServerPhase, "司会からの回答で進行が動いてはいけない。");
        }

        [UnityTest]
        public IEnumerator ModeratorHost_CannotSubmitChoice()
        {
            // M-B: 選択式（choice）でも司会専用モードのホストは選択できない。
            RegisterLobbyStatePrefab();
            yield return ConnectHostAndClient(ChoiceQuestionSource(), limits: FastLimits);
            SpawnHostLobbyState(HostRole.Moderator);

            var hostSession = HostSession;
            var clientSession = ClientSession;

            Assert.IsTrue(hostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.ChoiceAnswering,
                () => $"選択式の受付が開きませんでした（{clientSession.Phase.Value}）。");

            // 司会（ホスト）自身が選択しようとしても拒否される。
            hostSession.SubmitChoiceRpc(0);
            yield return WaitFrames(10);
            Assert.AreEqual(
                QuizPhase.ChoiceAnswering, hostSession.ServerPhase, "司会からの選択で進行が動いてはいけない。");

            // 通常のプレイヤー（クライアント）は引き続き選択できる。
            Assert.IsTrue(clientSession.RequestChoice(1), "クライアントは選択できるはず。");
            yield return WaitFrames(5);
        }

        /// <summary>choice 1 問（正解インデックス 1）。</summary>
        private static IQuestionSource ChoiceQuestionSource() =>
            new TestQuestionSource(
                TestQuestionSource.Choice("q-choice-1", "日本の首都は？", 1, "おおさか", "とうきょう", "きょうと"));

        /// <summary>freeText 2 問。</summary>
        private static IQuestionSource TwoQuestionSource() =>
            new TestQuestionSource(
                TestQuestionSource.FreeText("q-1", QuestionText, CorrectAnswer),
                TestQuestionSource.FreeText("q-2", "日本一高い山は？", "ふじさん"));

        /// <summary>実時間で指定秒数だけ待つ（一時停止中にタイムアウトが進まないことを確かめるため）。</summary>
        private static IEnumerator WaitSeconds(double seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += UnityEngine.Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>
        /// <c>BuzzOpen</c> フェーズに入っており、かつ受付の締め切り（<see cref="GameSession.CurrentDeadlineServerTime"/>、
        /// issue #154）まで <see cref="BuzzMarginSeconds"/> 秒以上残っていることを待つ（#176）。
        /// <c>RequestBuzz()</c> を送る直前に呼ぶことで、通し実行時のフレーム遅延で
        /// RPC がサーバーに届く前に締め切りを過ぎてしまう事故を防ぐ。
        /// </summary>
        /// <param name="session">確認対象のセッション（クライアント側）。</param>
        /// <param name="failureMessage">タイムアウト時の失敗メッセージ（受付締め切りまでの残り秒数を自動で追記する）。</param>
        private IEnumerator WaitUntilBuzzOpenWithMargin(GameSession session, System.Func<string> failureMessage)
        {
            yield return WaitUntil(
                () => session.Phase.Value == QuizPhase.BuzzOpen &&
                    session.CurrentDeadlineServerTime - ClientManager.ServerTime.Time >= BuzzMarginSeconds,
                () => $"{failureMessage()}（受付締め切りまでの残り: {FormatRemainingBuzzSeconds(session)} 秒）");
        }

        /// <summary>
        /// <see cref="WaitUntilBuzzOpenWithMargin"/> の失敗メッセージ用に、受付締め切りまでの残り秒数を整形する。
        /// <c>BuzzOpen</c> 以外のフェーズでは <see cref="GameSession.CurrentDeadlineServerTime"/> が
        /// <c>double.NaN</c> になるため、その場合は "N/A" と表示する。
        /// </summary>
        private string FormatRemainingBuzzSeconds(GameSession session)
        {
            var remaining = session.CurrentDeadlineServerTime - ClientManager.ServerTime.Time;
            return double.IsNaN(remaining) ? "N/A" : remaining.ToString("F2");
        }
    }
}
