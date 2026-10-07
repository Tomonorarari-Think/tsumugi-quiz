using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.Tests.PlayMode.UI.HostSetup;
using TsumugiQuiz.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// ロビーの「ゲーム開始」から実際に出題が始まり Game View へ到達することを、UI 操作だけで確認する
    /// PlayMode テスト（issue #95）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// #95 の不具合（本番コードから <see cref="GameSession.Configure"/> が一度も呼ばれておらず、
    /// ロビーから一度もゲームを開始できない）は、既存の PlayMode テストが
    /// <see cref="GameSession.Configure"/> を直接呼んでいたために検出できなかった。
    /// 本テストは <b>テストコードから Configure / StartSession を一切呼ばず</b>、
    /// ロビーのボタンを押すだけで出題まで進むことを確認する（同種の配線漏れの回帰テスト）。
    /// </para>
    /// <para>
    /// 問題データは <see cref="QuestionEditorFormSceneTests"/> と同じ方針で、問題フォルダ
    /// （<see cref="QuestionRepository.GetDefaultQuestionsFolderPath"/>）に GUID 付きのテスト専用ファイルを
    /// 1 件だけ書き、<see cref="DeleteTestQuestionSetAndRestorePreferences"/> で必ず削除する
    /// （読み込み元は本番と同じ経路を通したいので、コード側では別フォルダに差し替えない）。
    /// </para>
    /// <para>
    /// #112: その問題フォルダ自体は <c>PlayModeTestAssemblySetUp</c> がアセンブリ単位で
    /// <c>AppPaths.DataRoot</c> 配下の一時フォルダへ隔離している（<c>DocumentsRootScope</c>）。
    /// そのため実ユーザーの <c>Documents\TsumugiQuiz\Questions\</c> には触れず、並行して実機確認
    /// （<c>scripts/run-multi.ps1</c>）を行ってもテスト用ファイルが混入しない。
    /// 隔離先は実行のたびに空なので、以前あった「利用者の問題セットが 200 件あると切り捨てられうる」
    /// という前提（PR #104 レビュー L-7）も解消している。
    /// </para>
    /// </remarks>
    public class LobbyGameStartSceneTests
    {
        private const string MainSceneName = "Main";
        private const string LoopbackAddress = "127.0.0.1";
        private const string MidGameQuestionText = "進行中に合流したときの問題";
        private const string MidGameAnswerText = "こたえ";
        private const string TestHostPlayerName = "テストホスト";

        /// <summary>結果画面で自分の行に付くバッジの文言（<c>ResultView.YouBadgeText</c>）。</summary>
        private const string LocalPlayerBadgeText = "あなた";

        /// <summary>「ロビーへ戻る」のあと、跳ね返らないことを見張るフレーム数（PR #123 レビュー M-3）。</summary>
        private const int LobbyStayFrameCount = 60;

        private ConsentFileScope _consentScope;
        private HostSetupPreferencesScope _preferencesScope;
        private string _testSetFilePath;

        // 途中参加の確認（H-1）で使う、同一プロセス内の別ホスト。使わないテストでは null のまま。
        private GameObject _hostObject;
        private NetworkManager _hostManager;
        private NetworkService _hostService;
        private GameSession _hostSession;

        // #109 の再送確認で使うホスト側の名簿（LobbyState）。スポーンしないテストでは null のまま。
        private GameObject _hostLobbyObject;
        private LobbyState _hostLobby;

        [SetUp]
        public void SeedConsentedStateAndTestQuestionSet()
        {
            _consentScope = ConsentFileScope.Backup();

            var store = ConsentGate.CreateDefaultStore();
            var requiredTerms = TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, DateTime.UtcNow);

            _preferencesScope = HostSetupPreferencesScope.Backup();

            var folder = QuestionRepository.GetDefaultQuestionsFolderPath();
            Directory.CreateDirectory(folder);

            var uniqueSuffix = Guid.NewGuid().ToString("N");
            var fileName = "i95-playmode-test-" + uniqueSuffix;
            _testSetFilePath = Path.Combine(folder, fileName + ".json");

            var question = new Question(
                "q1", QuestionType.FreeText, "i95 のテスト問題", answers: new[] { "こたえ" });
            var set = new QuestionSet(1, fileName, "i95 PlayModeテストセット", string.Empty, new[] { question });
            Assert.IsTrue(QuestionSetWriter.TryWriteNew(_testSetFilePath, set, out var error), error);
        }

        [TearDown]
        public void DeleteTestQuestionSetAndRestorePreferences()
        {
            if (_testSetFilePath != null && File.Exists(_testSetFilePath))
            {
                File.Delete(_testSetFilePath);
            }

            _preferencesScope?.Restore();
            _consentScope?.Restore();
        }

        [UnityTearDown]
        public IEnumerator TearDownSceneAndNetwork()
        {
            // NGO の Shutdown() はフレーム終端で実処理が走るため、接続済みの状態で待たずに破棄すると
            // Transport の二重シャットダウンで例外になる（GameSessionTestFixture と同じ理由・対策）。
            var bootService = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.Service : null;
            bootService?.Stop();
            _hostService?.Stop();

            yield return WaitUntil(
                () => !IsNetworkBusy(bootService) && !IsNetworkBusy(_hostService),
                DefaultTimeoutSeconds,
                "ホスト / クライアントの停止が完了しませんでした。");
            yield return null;

            _hostService?.Dispose();
            _hostService = null;
            _hostSession = null;
            _hostLobby = null;
            _hostManager = null;

            if (_hostLobbyObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostLobbyObject);
                _hostLobbyObject = null;
            }

            if (_hostObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostObject);
                _hostObject = null;
            }

            // #101 / #108 レビュー M-2: 先に Main シーンをアンロードして表示中の View を畳んでから
            // シングルトン（NetworkBootstrap = NetworkService）を破棄する。逆順にすると、
            // View のコールバックが破棄済みのサービスを触りうる（GameViewSceneTests と同じ順序）。
            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();

            var bootstrap = NetworkBootstrap.Instance;
            if (bootstrap != null)
            {
                UnityEngine.Object.DestroyImmediate(bootstrap.gameObject);
            }

            var sePlayer = SePlayer.Instance;
            if (sePlayer != null)
            {
                UnityEngine.Object.DestroyImmediate(sePlayer.gameObject);
            }
        }

        private static bool IsNetworkBusy(NetworkService service)
            => service != null && (service.IsListening || service.IsClient || service.IsShutdownInProgress);

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator HostStartGame_FromLobby_ConfiguresSessionAndReachesGameView()
        {
            VisualElement panelRoot = null;
            yield return LobbyViewSceneTests.LoadMainThroughBootAndInstallFakes(root => panelRoot = root);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            yield return SimulateClickRoutine(hostButton);

            yield return LobbyViewSceneTests.StartHostingWithAutoPort(panelRoot);

            Button lobbyButton = null;
            yield return WaitForElement<Button>(panelRoot, "lobby-button", found => lobbyButton = found);
            yield return WaitUntil(() => lobbyButton.enabledSelf, "到達性の解決完了後は lobby-button が有効になるはず。");
            yield return SimulateClickRoutine(lobbyButton);

            // --- ロビー画面 ---
            Button startGameButton = null;
            yield return WaitForElement<Button>(panelRoot, "start-game-button", found => startGameButton = found);
            yield return WaitUntil(
                () => startGameButton.enabledSelf,
                "ロビーの同期が済めば「ゲーム開始」が有効になるはず。");

            var session = NetworkBootstrap.Instance.Service.ActiveGameSession;
            Assert.IsNotNull(session, "ホスト開始時に GameSession がスポーンされるはず。");
            Assert.IsNull(session.ActiveSessionSettings, "「ゲーム開始」前は進行設定が確定していないはず。");
            Assert.IsNotNull(session.SettingsSync, "GameSession プレハブに RoomSettingsSync が載っているはず。");
            Assert.IsFalse(session.SettingsSync.IsLocked, "「ゲーム開始」前はルーム設定がロックされていないはず。");

            var statusLabel = panelRoot.Q<Label>("lobby-status-label");
            Assert.IsNotNull(statusLabel, "lobby-status-label が見つかりません。");

            // --- 「ゲーム開始」 ---
            yield return SimulateClickRoutine(startGameButton);

            yield return WaitUntil(
                () => panelRoot.Q<Button>("buzz-button") != null,
                DefaultTimeoutSeconds,
                () => "「ゲーム開始」を押しても Game View へ遷移しませんでした"
                      + $"（ロビーの状況表示: '{statusLabel.text}'）。",
                panelRoot);

            // 出題が始まっている（Configure → StartSession が通っている）。
            Assert.IsNotNull(session.ActiveSessionSettings, "StartSession 経由で進行設定が確定するはず。");
            Assert.GreaterOrEqual(session.TotalQuestions.Value, 1, "出題列が 1 問以上あるはず。");
            Assert.AreNotEqual(QuizPhase.Lobby, session.Phase.Value, "出題が始まっているはず。");
            Assert.IsTrue(
                session.SettingsSync.IsLocked,
                "ゲーム開始操作でルーム設定がロックされるはず（docs/room-settings.md §4）。");

            // Game View に問題文が出ている。
            Label questionTextLabel = null;
            yield return WaitForElement<Label>(
                panelRoot, "question-text-label", found => questionTextLabel = found);
            yield return WaitUntil(
                () => !string.IsNullOrEmpty(questionTextLabel.text),
                "Game View に問題文が表示されるはず。",
                DefaultTimeoutSeconds,
                panelRoot);
        }

        /// <summary>
        /// PR #104 レビュー H-1 / M-4: 既に出題が始まっているルームへ後から参加したクライアントが、
        /// ロビー画面に取り残されずそのまま Game View へ移ること。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>NetworkVariable.OnValueChanged</c> は値が変わったときにしか発火しないため、
        /// 購読開始時の現在値を見ないと「次の <c>Reading</c> まで」ロビーに取り残される。
        /// 本テストはホスト側で<b>先に</b>出題を始めてからクライアントを合流させるので、
        /// その取りこぼしを検出できる。
        /// </para>
        /// <para>
        /// ホストは <c>GameViewSceneTests</c> と同じ作法で同一プロセス内に生の
        /// <see cref="NetworkManager"/> として立てる。このホストは <c>LobbyState</c> を
        /// スポーンしないため、クライアントのロビーは名簿を取得できないまま
        /// （<c>LobbyRoutine</c> がタイムアウトする）になるが、Game View への追従は
        /// 独立したコルーチンで行うので成立する（レビュー M-4 の確認も兼ねる）。
        /// </para>
        /// </remarks>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator ClientJoiningAfterQuestionStarted_MovesFromLobbyToGameView()
        {
            // 先に Boot → Main をロードする（LoadSceneMode.Single は現在のシーンの GameObject を
            // すべて破棄するので、ホスト役の GameObject はシーンロードのあとに作る）。
            // Main シーンの初期表示は Title View なので、この時点ではロビーはまだ開いていない。
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);

            var port = StartInProcessHost();
            Assert.IsTrue(_hostSession.StartQuestion(0), "ホスト側で 1 問目の出題を開始できるはず。");

            var clientService = NetworkBootstrap.Instance.Service;
            NetworkStartResult startResult = default;
            yield return clientService.StartClientWhenReady(
                LoopbackAddress, port, "あとから参加", onCompleted: r => startResult = r);
            Assert.IsTrue(startResult.Success, startResult.Message);

            // JoinView が接続成功時に行う遷移と同じ（本テストは参加コード入力の経路は対象外）。
            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Lobby);

            yield return WaitUntil(
                () => panelRoot.Q<Button>("buzz-button") != null,
                DefaultTimeoutSeconds,
                () => "進行中のルームへ合流したクライアントが Game View へ移りませんでした"
                      + $"（サーバーのフェーズ: {_hostSession.Phase.Value}）。",
                panelRoot);

            Assert.AreEqual(
                ViewNames.Game, router.CurrentViewName, "ロビーではなく Game View を表示しているはず。");

            var clientSession = clientService.FindActiveGameSession();
            Assert.IsNotNull(clientSession, "クライアント側にも GameSession が同期されているはず。");
            Assert.AreNotEqual(
                QuizPhase.Lobby, clientSession.Phase.Value, "合流時点で進行中のフェーズが同期されているはず。");

            // 注: このホストは LobbyState をスポーンしないので、現在問の再送（#109、
            // LobbyState → GameSession.ResyncClient）は走らない。問題文が届くことは
            // ClientJoiningAfterQuestionStarted_ReceivesCurrentQuestionText で確認する。
        }

        /// <summary>
        /// #109: 出題中のルームへ途中参加（<c>network.allowLateJoin</c>）したクライアントの
        /// Game View に、いま出題中の問題文が表示されること。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 問題データ（DTO）は RPC で配るため、出題より後に接続したクライアントには届かない。
        /// サーバー（<see cref="LobbyState"/>）が接続完了時に <c>GameSession.ResyncClient</c> を
        /// 呼ぶ配線（#109）が無いと、次の問題が始まるまで問題文が空のままになる。
        /// </para>
        /// <para>
        /// <see cref="ClientJoiningAfterQuestionStarted_MovesFromLobbyToGameView"/> と違い、
        /// ホスト側に <see cref="LobbyState"/> もスポーンする（本番の <see cref="NetworkBootstrap"/> と
        /// 同じ構成）。再送の呼び出し元が名簿だからで、途中参加の可否判定もここで効くようになる。
        /// </para>
        /// </remarks>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator ClientJoiningAfterQuestionStarted_ReceivesCurrentQuestionText()
        {
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);

            var port = StartInProcessHost(spawnLobbyState: true);
            Assert.IsTrue(_hostSession.StartQuestion(0), "ホスト側で 1 問目の出題を開始できるはず。");

            // 出題（＝ ルーム設定の確定、CommitRoomSettingsForStart）で、このテストホストの
            // RoomSettingsSync が持つ既定値がロビーへ流し込まれ network.allowLateJoin が false に戻る。
            // 本番ではホスト設定画面で保存した値が RoomSettingsApplier 経由で適用されるので、
            // これはテストホスト固有の事情（PR #114 レビュー L-6）。
            // 司会が実行中に切り替える経路（docs/network.md §2.3）で改めて許可する。
            _hostLobby.SetAllowLateJoin(true);
            Assert.IsTrue(_hostLobby.AllowLateJoin.Value, "途中参加を許可した状態で合流させるはず。");

            var clientService = NetworkBootstrap.Instance.Service;
            NetworkStartResult startResult = default;
            yield return clientService.StartClientWhenReady(
                LoopbackAddress, port, "あとから合流", onCompleted: r => startResult = r);
            Assert.IsTrue(startResult.Success, startResult.Message);

            // JoinView が接続成功時に行う遷移と同じ（本テストは参加コード入力の経路は対象外）。
            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Lobby);

            yield return WaitUntil(
                () => panelRoot.Q<Button>("buzz-button") != null,
                DefaultTimeoutSeconds,
                () => "進行中のルームへ合流したクライアントが Game View へ移りませんでした"
                      + $"（サーバーのフェーズ: {_hostSession.Phase.Value}）。",
                panelRoot);

            Label questionTextLabel = null;
            yield return WaitForElement<Label>(
                panelRoot, "question-text-label", found => questionTextLabel = found);
            yield return WaitUntil(
                () => questionTextLabel.text == MidGameQuestionText,
                DefaultTimeoutSeconds,
                () => "途中参加したクライアントへ現在の問題文が再送されるはず"
                      + $"（表示中: '{questionTextLabel.text}'）。",
                panelRoot);

            // 表示だけでなく、配信キャッシュ（QuestionDistributor）にも DTO が入っていること。
            var clientSession = clientService.FindActiveGameSession();
            Assert.IsNotNull(clientSession, "クライアント側にも GameSession が同期されているはず。");
            Assert.IsNotNull(clientSession.Distributor, "GameSession には QuestionDistributor が載っているはず。");
            Assert.IsTrue(
                clientSession.Distributor.TryGetQuestion(clientSession.QuestionIndex.Value, out var resent),
                "再送された現在問の DTO が保持されているはず。");
            Assert.AreEqual(MidGameQuestionText, resent.Text, "再送されるのは現在出題中の問題。");
        }

        /// <summary>
        /// #117: 全問終了後（<see cref="QuizPhase.Finished"/>）のルームへ合流したクライアントが、
        /// ロビーに取り残されずそのまま Result View（#20）へ到達し、順位表に自分の行が出ること。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>LobbyView</c> の購読直後の 1 回だけの判定は <c>QuizPhases.IsQuestionInProgress</c>
        /// （<see cref="QuizPhase.Result"/> / <see cref="QuizPhase.Finished"/> を含まない）のままなので、
        /// ここでの遷移はサーバーが名指しで送る合流の合図
        /// （<c>GameSession.SessionStateRpc</c> → <c>TryConsumePendingResync</c>）だけが根拠になる。
        /// </para>
        /// <para>
        /// Game View を経由せず直接 Result View へ移ることが要点。<c>GameView.HandlePhaseChanged</c> は
        /// フェーズが <see cref="QuizPhase.Finished"/> へ<b>変わった</b>ときにしか Result View へ送らないため、
        /// すでに終わっているルームへ合流した場合は Game View で止まってしまう。
        /// </para>
        /// </remarks>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator ClientJoiningAfterSessionFinished_ReachesResultView()
        {
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);

            ViewRouter router = null;
            yield return JoinFinishedRoomAndWaitForResultView(panelRoot, found => router = found);

            // 順位表が ScoreBoard（NetworkList）と名簿から描けていること。ホスト + 合流した自分の 2 行。
            VisualElement rankList = null;
            yield return WaitForElement<VisualElement>(panelRoot, "result-rank-list", found => rankList = found);
            yield return WaitUntil(
                () => rankList.childCount >= 2,
                DefaultTimeoutSeconds,
                () => $"順位表にホストと自分の行が出るはず（行数: {rankList.childCount}）。",
                panelRoot);

            Assert.IsTrue(
                HasYouBadge(rankList),
                "合流したクライアント自身の行（「あなた」バッジ付き）が順位表にあるはず。");
        }

        /// <summary>
        /// #117: 結果表示中（<see cref="QuizPhase.Result"/>）のルームへ合流したクライアントが、
        /// ロビーに取り残されず Game View（結果表示中の画面）へ到達すること。
        /// </summary>
        /// <remarks>
        /// この経路も根拠は合流の合図（<c>SessionStateRpc</c>）で、<c>GameSession.Phase</c> の現在値では
        /// ない。現在値で判定すると「ロビーへ戻る」直後のクライアントが跳ね返る（PR #104 レビュー H-A）。
        /// </remarks>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator ClientJoiningDuringResultPhase_ReachesGameView()
        {
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);

            var port = StartInProcessHost(spawnLobbyState: true);
            yield return DriveHostToResultPhase();

            // 結果表示中は「ゲーム進行中」なので、途中参加の許可が要る（docs/network.md §2.3 の 5）。
            _hostLobby.SetAllowLateJoin(true);
            Assert.IsTrue(_hostLobby.AllowLateJoin.Value, "途中参加を許可した状態で合流させるはず。");

            var clientService = NetworkBootstrap.Instance.Service;
            NetworkStartResult startResult = default;
            yield return clientService.StartClientWhenReady(
                LoopbackAddress, port, "結果表示中に合流", onCompleted: r => startResult = r);
            Assert.IsTrue(startResult.Success, startResult.Message);

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Lobby);

            yield return WaitUntil(
                () => router.CurrentViewName == ViewNames.Game,
                DefaultTimeoutSeconds,
                () => "結果表示中に合流したクライアントが Game View へ移りませんでした"
                      + $"（表示中の View: '{router.CurrentViewName}' / サーバーのフェーズ: {_hostSession.Phase.Value}）。",
                panelRoot);

            Assert.AreEqual(
                QuizPhase.Result,
                _hostSession.Phase.Value,
                "合流の間もサーバーは結果表示のままであるはず（単問経路は自動進行しない）。");

            // 再送された現在問が Game View にも出ていること（#109 の再送と組み合わせて成立する）。
            Label questionTextLabel = null;
            yield return WaitForElement<Label>(
                panelRoot, "question-text-label", found => questionTextLabel = found);
            yield return WaitUntil(
                () => questionTextLabel.text == MidGameQuestionText,
                DefaultTimeoutSeconds,
                () => "結果表示中に合流したクライアントにも現在問が再送されるはず"
                      + $"（表示中: '{questionTextLabel.text}'）。",
                panelRoot);
        }

        /// <summary>
        /// PR #123 レビュー M-3: 全問終了後に合流したクライアントが Result View に居るとき、
        /// ホストが「ロビーへ戻る」（<see cref="GameSession.ReturnToLobby"/>）を実行したら
        /// ロビーへ戻り、<b>そのまま Game View / Result View へ跳ね返らない</b>こと。
        /// </summary>
        /// <remarks>
        /// PR #104 レビュー H-A（「ロビーへ戻る」直後に古い <see cref="QuizPhase.Result"/> を
        /// 観測して Lobby → Game へ戻ってしまう）の自動テスト。#117 で合流の合図による遷移を
        /// 足したため、合図が残っていると同じ跳ね返りが起こりうる。
        /// 合図は取り出した時点で消え、<c>ReturnToLobbyRpc</c> の受信時にも捨てられるので戻らない。
        /// </remarks>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator ReturnToLobbyAfterLateJoin_KeepsClientInLobby()
        {
            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);

            ViewRouter router = null;
            yield return JoinFinishedRoomAndWaitForResultView(panelRoot, found => router = found);

            // --- ホストが「ロビーへ戻る」（ResultView のボタンと同じ経路） ---
            Assert.IsTrue(_hostSession.ReturnToLobby(), "全問終了からロビーへ戻せるはず。");

            yield return WaitUntil(
                () => router.CurrentViewName == ViewNames.Lobby,
                DefaultTimeoutSeconds,
                () => $"クライアントがロビーへ戻りませんでした（表示中の View: '{router.CurrentViewName}'）。",
                panelRoot);

            // --- 跳ね返らないこと（数十フレーム見張る） ---
            for (var frame = 0; frame < LobbyStayFrameCount; frame++)
            {
                yield return null;
                Assert.AreEqual(
                    ViewNames.Lobby,
                    router.CurrentViewName,
                    $"ロビーへ戻った {frame + 1} フレーム後に画面が切り替わりました"
                    + $"（サーバーのフェーズ: {_hostSession.Phase.Value}）。");
            }
        }

        /// <summary>
        /// 全問終了（<see cref="QuizPhase.Finished"/>）まで進めたテストホストへクライアントを合流させ、
        /// Result View に到達するまで待つ（#117、PR #123 レビュー M-3 で共通化）。
        /// </summary>
        /// <param name="panelRoot">Main シーンの UI ルート（失敗時の診断ダンプに使う）。</param>
        /// <param name="onRouter">見つかった <see cref="ViewRouter"/> の受け取り先。</param>
        private IEnumerator JoinFinishedRoomAndWaitForResultView(
            VisualElement panelRoot, Action<ViewRouter> onRouter)
        {
            var port = StartInProcessHost(spawnLobbyState: true);
            yield return DriveHostToResultPhase();

            Assert.IsTrue(_hostSession.FinishSession(), "結果表示からセッションを終了できるはず。");
            yield return WaitUntil(
                () => _hostSession.Phase.Value == QuizPhase.Finished,
                DefaultTimeoutSeconds,
                () => $"ホストが全問終了になりませんでした（{_hostSession.Phase.Value}）。");

            // 全問終了は「ゲーム進行中」ではない（QuizPhases.IsGameInProgress、docs/network.md §2.3 の 5）ので、
            // network.allowLateJoin が false のままでも参加できる。
            Assert.IsFalse(
                _hostLobby.AllowLateJoin.Value,
                "出題時のルーム設定の確定で途中参加は不許可に戻っているはず（それでも Finished なら入れる）。");

            var clientService = NetworkBootstrap.Instance.Service;
            NetworkStartResult startResult = default;
            yield return clientService.StartClientWhenReady(
                LoopbackAddress, port, "終了後に合流", onCompleted: r => startResult = r);
            Assert.IsTrue(startResult.Success, startResult.Message);

            // JoinView が接続成功時に行う遷移と同じ（本テストは参加コード入力の経路は対象外）。
            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Lobby);

            yield return WaitUntil(
                () => router.CurrentViewName == ViewNames.Result,
                DefaultTimeoutSeconds,
                () => "全問終了後に合流したクライアントが Result View へ移りませんでした"
                      + $"（表示中の View: '{router.CurrentViewName}' / サーバーのフェーズ: {_hostSession.Phase.Value}）。",
                panelRoot);

            onRouter(router);
        }

        /// <summary>
        /// テストホストを 1 問目の結果表示（<see cref="QuizPhase.Result"/>）まで進める（#117）。
        /// </summary>
        /// <remarks>
        /// <c>ResultViewSceneTests</c> と同じく、ホスト自身が早押し → 正解を送って判定を進める。
        /// 単問経路（<c>Configure</c> + <c>StartQuestion</c>）は結果表示から自動進行しない
        /// （<c>GameSession.TickSession</c>）ので、クライアントが合流するまで <see cref="QuizPhase.Result"/> が続く。
        /// </remarks>
        private IEnumerator DriveHostToResultPhase()
        {
            Assert.IsTrue(_hostSession.StartQuestion(0), "ホスト側で 1 問目の出題を開始できるはず。");

            yield return WaitUntil(
                () => _hostSession.Phase.Value == QuizPhase.BuzzOpen,
                DefaultTimeoutSeconds,
                () => $"早押しの受付が開きませんでした（{_hostSession.Phase.Value}）。");

            Assert.IsTrue(_hostSession.RequestBuzz(), "ホスト自身の押下を送れるはず。");
            yield return WaitUntil(
                () => _hostSession.Phase.Value == QuizPhase.Answering,
                DefaultTimeoutSeconds,
                () => $"回答フェーズに進みませんでした（{_hostSession.Phase.Value}）。");

            Assert.IsTrue(_hostSession.RequestAnswer(MidGameAnswerText), "回答権を持つ本人の回答は受理されるはず。");
            yield return WaitUntil(
                () => _hostSession.Phase.Value == QuizPhase.Result,
                DefaultTimeoutSeconds,
                () => $"結果フェーズに進みませんでした（{_hostSession.Phase.Value}）。");
        }

        /// <summary>順位表に「あなた」バッジの付いた行（＝ 自分の行）があるか。</summary>
        private static bool HasYouBadge(VisualElement rankList)
        {
            foreach (var label in rankList.Query<Label>().ToList())
            {
                if (label.text == LocalPlayerBadgeText)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 同一プロセス内に生の <see cref="NetworkManager"/> でホストを立て、freeText 1 問を仕込む
        /// （<c>GameViewSceneTests.StartInProcessHost</c> と同じ作法）。
        /// </summary>
        /// <param name="spawnLobbyState">
        /// 本番の <see cref="NetworkBootstrap"/> と同じく <see cref="LobbyState"/> もスポーンして
        /// 接続承認・再同期（#109）の配線まで再現するか。false なら名簿なしのホストになる。
        /// </param>
        /// <returns>ホストが実際に待ち受けているポート。</returns>
        private ushort StartInProcessHost(bool spawnLobbyState = false)
        {
            _hostObject = new GameObject(nameof(LobbyGameStartSceneTests) + "-Host");
            _hostObject.SetActive(false);

            var transport = _hostObject.AddComponent<UnityTransport>();
            transport.MaxPayloadSize = NetworkConstants.MaxPayloadSizeBytes;

            _hostManager = _hostObject.AddComponent<NetworkManager>();
            _hostManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = null,
            };

            // Boot シーンの NetworkManager と同じプレハブ登録にそろえる（ForceSamePrefabs）。
            _hostManager.AddNetworkPrefab(NetworkTestPrefabs.LoadGameSession());
            _hostManager.AddNetworkPrefab(NetworkTestPrefabs.LoadLobbyState());

            _hostObject.SetActive(true);

            _hostService = new NetworkService(_hostManager);
            var result = _hostService.StartHost(startPort: 0);
            Assert.IsTrue(result.Success, result.Message);

            _hostSession = _hostService.ActiveGameSession;
            Assert.IsNotNull(_hostSession, "ホスト開始時に GameSession がスポーンされるはず。");
            // 早押しの制限時間（既定 10 秒）で勝手に結果表示へ進まないよう、上限いっぱいに伸ばす。
            // クライアントの接続・シーン遷移を待つ間も出題中のままにしておきたいため。
            _hostSession.Configure(
                new TestQuestionSource(TestQuestionSource.FreeText("q-mid-1", MidGameQuestionText, MidGameAnswerText)),
                limits: new QuizTimeLimits(
                    buzzTimeLimitSec: 60.0, answerTimeLimitSec: 60.0, collectWindowSec: 0.15));

            if (spawnLobbyState)
            {
                SpawnHostLobbyState();
            }

            return _hostService.ActivePort;
        }

        /// <summary>
        /// ホスト側に <see cref="LobbyState"/> をスポーンして、本番（<see cref="NetworkBootstrap"/>）と
        /// 同じ配線（接続承認の追加判定・席の引き継ぎ・現在問の再送）を有効にする（#109）。
        /// </summary>
        private void SpawnHostLobbyState()
        {
            var prefab = NetworkTestPrefabs.LoadLobbyState().GetComponent<NetworkObject>();
            var spawned = _hostManager.SpawnManager.InstantiateAndSpawn(prefab);
            Assert.IsNotNull(spawned, "ホスト側で LobbyState をスポーンできるはず。");

            _hostLobbyObject = spawned.gameObject;
            _hostLobbyObject.name = "LobbyState(TestHost)";

            _hostLobby = _hostLobbyObject.GetComponent<LobbyState>();
            Assert.IsNotNull(_hostLobby, "スポーンしたプレハブに LobbyState があるはず。");

            _hostLobby.AttachServer(_hostService.ApprovalHandler, TestHostPlayerName);
            _hostLobby.ConfigureRoom(HostRole.Player, LobbyRoster.DefaultMaxPlayers, allowLateJoin: true);
        }
    }
}
