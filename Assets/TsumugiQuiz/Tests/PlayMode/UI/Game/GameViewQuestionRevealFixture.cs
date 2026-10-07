using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Reveal;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.Tests.PlayMode.Tts;
using TsumugiQuiz.Tests.Shared.Room;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.Game;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI.Game
{
    /// <summary>
    /// 問題文の文字送り表示（issue #144）の PlayMode テストの共通部分。
    /// <see cref="GameViewSceneTests"/> と同じく、1 プロセス内に立てた生の <see cref="NetworkManager"/> をホストにし、
    /// Boot → Main を読み込んだ側をクライアントとして Game View を駆動する。
    /// </summary>
    public abstract class GameViewQuestionRevealFixture
    {
        protected const string LoopbackAddress = "127.0.0.1";

        /// <summary>28 文字。遅い固定速度でも、途中の状態を十分な時間観察できる長さにする。</summary>
        protected const string LongQuestionText = "つむぎちゃんが好きな食べ物として知られているものはなに？";

        protected const string CorrectAnswer = "ぱん";

        private ConsentFileScope _consentScope;
        private RoomSettingsDraftScope _roomSettingsDraftScope;

        private GameObject _hostObject;
        private NetworkManager _hostManager;
        private NetworkService _hostService;
        private GameSession _hostSession;

        /// <summary>ホスト側のセッション（<see cref="StartInProcessHost"/> で作る）。</summary>
        protected GameSession HostSession => _hostSession;

        protected static int LongQuestionLength => RevealText.GetTextElementStarts(LongQuestionText).Length;

        [SetUp]
        public void SeedConsentedState()
        {
            _consentScope = ConsentFileScope.Backup();
            _roomSettingsDraftScope = RoomSettingsDraftScope.Redirect();

            var store = ConsentGate.CreateDefaultStore();
            store.RecordConsent(TermsCatalog.LoadRequiredTerms(), Application.version, DateTime.UtcNow);
        }

        [TearDown]
        public void RestoreConsentFile()
        {
            _consentScope?.Restore();
            _roomSettingsDraftScope?.Restore();
            _roomSettingsDraftScope = null;
        }

        [UnityTearDown]
        public IEnumerator TearDownSceneAndNetwork()
        {
            // GameViewSceneTests.TearDownSceneAndNetwork と同じ手順（停止完了を待ってから破棄する）。
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

            if (_hostObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostObject);
                _hostObject = null;
            }

            _hostManager = null;

            yield return UnloadMainSceneRoutine();
            TearDownMainSceneAndBootstrapSingletons();
        }

        protected const double HostReadingDurationSec = 1.0;

        protected sealed class RevealContext
        {
            public VisualElement PanelRoot;
            public Label QuestionTextLabel;
            public GameSession ClientSession;
            public GameView GameView;
        }

        protected static IQuestionSource FreeTextSource() =>
            new TestQuestionSource(TestQuestionSource.FreeText("q-reveal-1", LongQuestionText, CorrectAnswer));

        /// <summary>
        /// ホストを立ててルーム設定（読み上げの有無・文字送り速度）を配り、クライアントを接続して Game View を出す。
        /// <paramref name="showGameView"/> が false なら Game View はまだ出さない（出題の取りこぼしを作るため）。
        /// </summary>
        protected IEnumerator SetUpClientGameView(
            RevealContext context, IQuestionSource source, int msPerChar, bool ttsEnabled = false, bool showGameView = true)
        {
            yield return LoadBootThenMainSceneAndGetRoot(r => context.PanelRoot = r);

            var port = StartInProcessHost(source);
            var settings = RoomSettings.Default
                .WithTts(ttsEnabled, RoomSettings.DefaultTtsSpeed, RoomSettings.DefaultTtsReadyTimeoutMs, RoomSettings.DefaultTtsLeadTimeSec)
                .WithQuestionRevealMsPerChar(msPerChar);
            Assert.IsTrue(_hostSession.SettingsSync.TrySetSettings(settings), "ロック前のホストはルーム設定を変えられるはず。");

            var clientService = NetworkBootstrap.Instance.Service;
            NetworkStartResult startResult = default;
            yield return clientService.StartClientWhenReady(
                LoopbackAddress, port, "つむぎ", onCompleted: r => startResult = r);
            Assert.IsTrue(startResult.Success, startResult.Message);

            yield return WaitUntil(
                () => (context.ClientSession = clientService.FindActiveGameSession()) != null
                      && context.ClientSession.SettingsSync != null
                      && context.ClientSession.SettingsSync.Current.QuestionRevealMsPerChar == msPerChar
                      && context.ClientSession.GetComponent<TtsSyncCoordinator>().ReadingEnabled.Value == ttsEnabled,
                DefaultTimeoutSeconds,
                "クライアントへセッションとルーム設定が届きませんでした。");

            if (showGameView)
            {
                yield return ShowGameView(context);
            }
        }

        /// <summary>Game View を出し、要素と GameView のインスタンスを <paramref name="context"/> に積む。</summary>
        protected static IEnumerator ShowGameView(RevealContext context)
        {
            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Game);

            yield return WaitForElement<Label>(
                context.PanelRoot, "question-text-label", found => context.QuestionTextLabel = found);
            context.GameView = router.CurrentController as GameView;
            Assert.IsNotNull(context.GameView, "表示中のコントローラは GameView のはず。");
        }

        protected ushort StartInProcessHost(IQuestionSource questionSource)
        {
            _hostObject = new GameObject(nameof(GameViewQuestionRevealTests) + "-Host");
            _hostObject.SetActive(false);

            var transport = _hostObject.AddComponent<UnityTransport>();
            transport.MaxPayloadSize = NetworkConstants.MaxPayloadSizeBytes;

            _hostManager = _hostObject.AddComponent<NetworkManager>();
            _hostManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = null,
            };

            // Boot シーンの NetworkManager と同じプレハブ登録にそろえる（GameViewSceneTests と同じ理由）。
            _hostManager.AddNetworkPrefab(NetworkTestPrefabs.LoadGameSession());
            _hostManager.AddNetworkPrefab(NetworkTestPrefabs.LoadLobbyState());

            _hostObject.SetActive(true);

            _hostService = new NetworkService(_hostManager);
            var result = _hostService.StartHost(startPort: 0);
            Assert.IsTrue(result.Success, result.Message);

            _hostSession = _hostService.ActiveGameSession;
            Assert.IsNotNull(_hostSession, "ホスト開始時に GameSession がスポーンされるはず。");
            _hostSession.Configure(questionSource);

            return _hostService.ActivePort;
        }

        protected static IEnumerator WaitRealtime(double seconds)
        {
            var until = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < until)
            {
                yield return null;
            }
        }

        protected static bool IsNetworkBusy(NetworkService service)
            => service != null && (service.IsListening || service.IsClient || service.IsShutdownInProgress);
    }
}
