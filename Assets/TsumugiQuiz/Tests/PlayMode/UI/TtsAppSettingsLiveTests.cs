using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.Tests.Shared.Room;
using TsumugiQuiz.Tests.Shared.Tts;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.Settings;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// 設定画面で保存したアプリ設定（<c>tts.*</c>）が、<b>アプリを再起動せずに</b>
    /// <see cref="TtsService"/> へ届くことを、Boot → Main の実経路で固定する（issue #138）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// #127（PR #133）で <c>ViewRouter.Awake</c> → <c>DefaultViewControllerRegistrations.ConfigureTtsConsentGate</c>
    /// が <see cref="TtsService.ConfigureDefaults"/> を呼ぶようになり、「ロビーの先行初期化
    /// （<c>TtsSyncCoordinator.OnNetworkSpawn</c> → <c>TtsSyncPlayer.InitializeAsync</c>）が最初の初期化者になると
    /// <c>DefaultTtsSettingsProvider</c> に固定される」症状は解消した。ただし登録される provider が
    /// <b>起動時スナップショット</b>（<c>FixedTtsSettingsProvider</c>）だと、
    /// 「設定画面で <c>tts.speakerName</c> / <c>tts.assetPathOverride</c> を変更 → 再起動せずホスト」で
    /// 起動時の値が使われてしまう（#138 の R-1 分析）。
    /// </para>
    /// <para>
    /// ここで固定するのは次の 2 点。
    /// <list type="number">
    ///   <item><description>
    ///     <b>初期化前に保存した値</b>が、最初の初期化者（ロビーのスポーン）に届くこと
    ///     （= 起動時スナップショットではなく、初期化時点の保存内容を読むこと）
    ///   </description></item>
    ///   <item><description>
    ///     <b>初期化後に設定画面で保存した値</b>が、次の読み上げに効くこと
    ///     （<c>speakerName</c> / <c>assetPathOverride</c> は合成エンジンの生成時にしか読まれないため、
    ///     保存時に再初期化が必要。docs/tts.md §6.5）
    ///   </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 判定には <see cref="TtsService.Settings"/>（<c>Initialize</c> が <c>ITtsSettingsProvider</c> から読んだ実体）を使う。
    /// <c>External/</c>（voicevox_core の DLL・辞書・モデル）が未配置の環境では初期化そのものは
    /// <c>NotAvailable</c> で終わるが、<see cref="TtsService.Settings"/> はその手前で確定するため、
    /// 「どの設定で初期化しようとしたか」はどの環境でも同じように観測できる。
    /// </para>
    /// </remarks>
    public sealed class TtsAppSettingsLiveTests
    {
        /// <summary>アプリ起動より前に保存しておく値（起動時スナップショットならこちらが使われる）。</summary>
        private const string StartupSpeakerName = "起動時スナップショット話者";

        /// <summary>アプリ起動後（＝設定画面での保存に相当）に保存する値。こちらが使われるのが正しい。</summary>
        private const string SavedSpeakerName = "設定画面で保存した話者";

        private const int StartupCacheMaxEntries = 111;
        private const int SavedCacheMaxEntries = 222;
        private const string StartupAssetPathOverride = "C:/tsumugi-quiz-tests/startup-voicevox";
        private const string SavedAssetPathOverride = "C:/tsumugi-quiz-tests/saved-voicevox";

        /// <summary>
        /// 「保存」クリックの同期処理に許す上限（ミリ秒、#138 レビュー H-1 / L-2）。
        /// </summary>
        /// <remarks>
        /// 実測は数ミリ秒（ファイルの読み書きと設定の比較だけ）。ここで守りたいのは
        /// 「<c>RetryInitializeAsync</c> が進行中の初期化タスクの完了を待って
        /// <b>最大 10 秒（<c>ShutdownWaitMs</c>）ブロックしていない</b>」ことだけなので、
        /// 実測値ぎりぎりではなく 1 秒に置く。バッチ実行のディスク待ち・GC で数十〜数百ミリ秒
        /// ぶれてもフレークにならず、かつ 10 秒ブロックの回帰は確実に捕まえられる幅である。
        /// </remarks>
        private const double SaveMustCompleteWithinMs = 1000d;

        private ConsentFileScope _consentScope;
        private AppSettingsFileScope _appSettingsScope;
        private RoomSettingsDraftScope _roomSettingsDraftScope;

        private GameObject _hostObject;
        private NetworkService _hostService;

        /// <summary>合成エンジンの生成を「初期化中」で止めておくための関門（使うテストだけが作る）。</summary>
        private ManualResetEventSlim _engineGate;

        [SetUp]
        public void BackupUserFilesAndConsent()
        {
            DestroyLeftoverTtsService();

            _consentScope = ConsentFileScope.Backup();
            _appSettingsScope = AppSettingsFileScope.Backup();
            _roomSettingsDraftScope = RoomSettingsDraftScope.Redirect();

            // #28 Phase 2: ルーム設定タブが実ネットワークの RoomSettingsSync を掴まないようにする
            // （SettingsSceneTests と同じ作法）。本クラスが見るのはアプリ設定タブだけ。
            SettingsView.RoomSettingsSyncLocator = () => null;

            ConsentGate.CreateDefaultStore()
                .RecordConsent(TermsCatalog.LoadRequiredTerms(), Application.version, DateTime.UtcNow);
        }

        [UnityTearDown]
        public IEnumerator TearDownSceneAndNetwork()
        {
            // 予約（StatusChanged の購読）と差し替えを次のテストへ持ち越さない。
            TtsAppSettingsReloader.ResetForTesting();

            // シーンや TtsService を壊す前に関門を開ける。開けないまま OnDestroy → Shutdown へ入ると、
            // 初期化タスクの完了を最大 10 秒（ShutdownWaitMs）待つことになる。
            _engineGate?.Set();

            // NGO の Shutdown() はフレーム終端で実処理が走るため、停止完了を待ってから破棄する
            // （TtsConsentGateBootstrapTests.TearDownSceneAndNetwork と同じ理由・同じ手順）。
            var bootService = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.Service : null;
            bootService?.Stop();
            _hostService?.Stop();

            var deadline = Time.realtimeSinceStartupAsDouble + DefaultTimeoutSeconds;
            while ((IsNetworkBusy(bootService) || IsNetworkBusy(_hostService))
                   && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }

            yield return null;

            _hostService?.Dispose();
            _hostService = null;

            if (_hostObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostObject);
                _hostObject = null;
            }

            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();

            TearDownMainSceneAndBootstrapSingletons();

            SettingsView.RoomSettingsSyncLocator = null;

            _engineGate?.Dispose();
            _engineGate = null;

            _roomSettingsDraftScope?.Restore();
            _roomSettingsDraftScope = null;
            _appSettingsScope?.Restore();
            _appSettingsScope = null;
            _consentScope?.Restore();
            _consentScope = null;
        }

        /// <summary>
        /// アプリ起動後（＝ <c>ConfigureTtsConsentGate</c> の登録後）に保存した <c>tts.*</c> が、
        /// ロビーの先行初期化にそのまま反映されること（#138 の本題）。
        /// 起動時スナップショットを登録していると <see cref="StartupSpeakerName"/> のまま初期化され、失敗する。
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator 起動後に保存したtts設定がロビーの先行初期化に反映される()
        {
            SaveAppSettings(StartupSpeakerName, StartupCacheMaxEntries, StartupAssetPathOverride);

            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);
            Assert.IsNotNull(panelRoot, "Main シーンの UI ルートが取れるはず。");

            var service = TtsService.Instance;
            Assert.IsNotNull(service, "Boot シーンに TtsService が常駐しているはず。");
            Assert.AreEqual(
                TtsServiceState.NotInitialized, service.Status.State,
                "起動しただけでは初期化を始めない（#25 H-5、docs/tts.md §6.5）。");

            // 設定画面で保存した状態を作る（アプリは再起動しない）。
            SaveAppSettings(SavedSpeakerName, SavedCacheMaxEntries, SavedAssetPathOverride);

            // ロビー相当: GameSession をスポーンさせ、TtsSyncCoordinator.OnNetworkSpawn を通す。
            StartInProcessHost();

            yield return WaitUntilSettingsApplied(service, SavedSpeakerName);

            Assert.IsNotNull(service.Settings, "ロビーのスポーンで読み上げの初期化が始まるはず（docs/tts.md §6.5）。");
            Assert.AreEqual(
                SavedSpeakerName, service.Settings.SpeakerName,
                "設定画面で保存した tts.speakerName が使われるはず（起動時スナップショットではない、#138）。");
            Assert.AreEqual(
                SavedCacheMaxEntries, service.Settings.CacheMaxEntries,
                "tts.cacheMaxEntries も保存後の値が使われるはず。");
            Assert.AreEqual(
                SavedAssetPathOverride, service.Settings.AssetPathOverride,
                "tts.assetPathOverride も保存後の値が使われるはず（配置の解決に効く）。");
        }

        /// <summary>
        /// 初期化が済んだあとに設定画面で <c>tts.speakerName</c> を変えて保存すると、
        /// 再起動せずに新しい設定で初期化し直されること（#138）。
        /// <c>speakerName</c> / <c>assetPathOverride</c> は合成エンジンの生成時にしか読まれないため、
        /// provider を都度読みにしただけでは足りず、保存時の再初期化が要る（docs/tts.md §6.5）。
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator 初期化後に設定画面で保存したtts設定が再初期化で反映される()
        {
            SaveAppSettings(StartupSpeakerName, StartupCacheMaxEntries, StartupAssetPathOverride);

            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);
            Assert.IsNotNull(panelRoot, "Main シーンの UI ルートが取れるはず。");

            var service = TtsService.Instance;
            Assert.IsNotNull(service, "Boot シーンに TtsService が常駐しているはず。");

            // 先に初期化させる（ロビーのスポーン相当）。
            StartInProcessHost();
            yield return WaitUntilSettingsApplied(service, StartupSpeakerName);
            Assert.AreEqual(
                StartupSpeakerName, service.Settings.SpeakerName,
                "初期化時点では保存済みの tts.speakerName が使われるはず。");

            // 設定画面 → アプリ設定タブ → 話者名を書き換えて保存。
            Button settingsButton = null;
            yield return WaitForElement<Button>(panelRoot, "settings-button", found => settingsButton = found);
            yield return SimulateClickRoutine(settingsButton);

            Button appTabButton = null;
            yield return WaitForElement<Button>(panelRoot, "settings-tab-app-button", found => appTabButton = found);
            yield return SimulateClickRoutine(appTabButton);

            TextField speakerField = null;
            yield return WaitForElement<TextField>(
                panelRoot, "app-tts-speaker-name-field", found => speakerField = found);
            speakerField.value = SavedSpeakerName;

            Button saveButton = null;
            yield return WaitForElement<Button>(panelRoot, "app-settings-save-button", found => saveButton = found);
            yield return SimulateClickRoutine(saveButton);

            yield return WaitUntilSettingsApplied(service, SavedSpeakerName);

            Assert.AreEqual(
                SavedSpeakerName, service.Settings.SpeakerName,
                "設定画面で保存した tts.speakerName が、再起動せずに次の読み上げから効くはず（#138）。");
        }

        /// <summary>
        /// #138 レビュー H-1。<b>初期化中</b>に設定画面で保存しても、保存の処理（＝そのフレーム）が
        /// <see cref="SaveMustCompleteWithinMs"/> ミリ秒を超えて止まらないこと。そのうえで、
        /// 初期化が終わったら新しい話者で <see cref="TtsService.Settings"/> が確定すること。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>TtsService.RetryInitializeAsync</c> は同期部分で直前のエンジンを破棄する（<c>DisposeEngine</c>）が、
        /// 初期化タスクが走っている最中はその完了を<b>最大 10 秒（<c>ShutdownWaitMs</c>）メインスレッドで待つ</b>。
        /// 設定画面はロビーからも開ける（<c>LobbyView</c> の「設定」）ため、ロビーでの先行初期化
        /// （<c>TtsSyncCoordinator.OnNetworkSpawn</c>）の最中に保存すると画面が固まってしまう。
        /// </para>
        /// <para>
        /// 「初期化中」を取りこぼしなく作るため、ここだけはホストのスポーンに頼らず
        /// <c>TtsService.Initialize</c> をフェイクの合成エンジンで直接呼び、
        /// エンジン生成を <see cref="_engineGate"/> で止めておく（<c>External/</c> の有無に左右されない）。
        /// 完了後の再初期化は本番どおり実物の <c>RetryInitializeAsync</c>（＝実 DLL があればそれを読む）。
        /// </para>
        /// </remarks>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator 初期化中に保存してもメインスレッドを止めず完了後に反映される()
        {
            SaveAppSettings(StartupSpeakerName, StartupCacheMaxEntries, StartupAssetPathOverride);

            VisualElement panelRoot = null;
            yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);
            Assert.IsNotNull(panelRoot, "Main シーンの UI ルートが取れるはず。");

            var service = TtsService.Instance;
            Assert.IsNotNull(service, "Boot シーンに TtsService が常駐しているはず。");

            // 「初期化中」で止める。settingsProvider は渡さないので、アプリ起動時に
            // ConfigureTtsConsentGate が登録した都度読み provider（= 保存済みの起動時設定）が使われる。
            _engineGate = new ManualResetEventSlim(false);
            service.Initialize(
                engineFactory: (location, settings) =>
                {
                    _engineGate.Wait();
                    return new FakeTtsSynthesisEngine();
                });

            Assert.AreEqual(
                TtsServiceState.Initializing, service.Status.State, "初期化中の状態を作れているはず。");
            Assert.AreEqual(
                StartupSpeakerName, service.Settings.SpeakerName, "初期化時点では起動時の保存内容が使われる。");

            // 設定画面 → アプリ設定タブ → 話者名を書き換える。
            Button settingsButton = null;
            yield return WaitForElement<Button>(panelRoot, "settings-button", found => settingsButton = found);
            yield return SimulateClickRoutine(settingsButton);

            Button appTabButton = null;
            yield return WaitForElement<Button>(panelRoot, "settings-tab-app-button", found => appTabButton = found);
            yield return SimulateClickRoutine(appTabButton);

            TextField speakerField = null;
            yield return WaitForElement<TextField>(
                panelRoot, "app-tts-speaker-name-field", found => speakerField = found);
            speakerField.value = SavedSpeakerName;

            Button saveButton = null;
            yield return WaitForElement<Button>(panelRoot, "app-settings-save-button", found => saveButton = found);

            // クリック送出そのものだけを計る（Panel アタッチ待ちのフレームを含めない）。
            yield return WaitForPanelAttachment(saveButton);

            var stopwatch = Stopwatch.StartNew();
            using (var clickEvent = NavigationSubmitEvent.GetPooled())
            {
                clickEvent.target = saveButton;
                saveButton.SendEvent(clickEvent);
            }

            stopwatch.Stop();

            Assert.Less(
                stopwatch.Elapsed.TotalMilliseconds, SaveMustCompleteWithinMs,
                "初期化中の保存でメインスレッドを止めてはいけない（#138 H-1: RetryInitializeAsync は " +
                $"進行中の初期化タスクの完了を最大 10 秒待つ）。実測 {stopwatch.Elapsed.TotalMilliseconds:0.#} ms。");

            Assert.AreEqual(
                TtsServiceState.Initializing, service.Status.State,
                "初期化中は再初期化を始めず、進行中の初期化をそのまま続けること。");
            Assert.AreEqual(
                StartupSpeakerName, service.Settings.SpeakerName, "この時点ではまだ反映されていないこと。");

            Label statusLabel = null;
            yield return WaitForElement<Label>(
                panelRoot, "app-settings-status-label", found => statusLabel = found);
            Assert.AreEqual(
                "アプリ設定を保存しました。読み上げは初期化完了後に反映します。", statusLabel.text,
                "持ち越したことがユーザーに伝わること（#138 レビュー M-2）。");

            // 初期化を完了させると、次の Update で StatusChanged が届き、そこで再初期化が走る。
            _engineGate.Set();

            yield return WaitUntilSettingsApplied(service, SavedSpeakerName);

            Assert.AreEqual(
                SavedSpeakerName, service.Settings.SpeakerName,
                "初期化完了後に、保存しておいた tts.speakerName で初期化し直されるはず（#138 H-1）。");
        }

        /// <summary>
        /// 他のテストクラスが残した <see cref="TtsService"/>（<c>DontDestroyOnLoad</c> の常駐物）を片付ける。
        /// </summary>
        /// <remarks>
        /// <c>TtsService.Awake</c> は <c>Instance</c> が既に居ると<b>後から生成されたほう</b>
        /// （＝ Boot シーンが載せる新しいインスタンス）を破棄する。そのため残骸があると、
        /// Boot を読み込んでも<b>初期化済みの古いインスタンス</b>を掴んでしまい、
        /// 「まだ初期化していない状態から始める」という本クラスの前提が崩れる
        /// （全 PlayMode 通し実行では <c>TsumugiQuiz.Tests.PlayMode.Tts.*</c> が先に走る）。
        /// 破棄の手順は <see cref="MainSceneTestHelpers.TearDownMainSceneAndBootstrapSingletons"/> と同じ。
        ///
        /// <b>注意（#138 レビュー L-3）</b>: <c>OnDestroy</c> → <c>TtsService.Shutdown</c> は、初期化が
        /// 進行中だとその完了を<b>最大 10 秒（<c>ShutdownWaitMs</c>）待つ</b>。残骸の初期化が
        /// 走っている最中にここへ来ると、その分だけテストの開始が遅れる（機能的には正しい待ち）。
        /// </remarks>
        private static void DestroyLeftoverTtsService()
        {
            var leftover = TtsService.Instance;
            if (leftover != null)
            {
                UnityEngine.Object.DestroyImmediate(leftover.gameObject);
            }
        }

        /// <summary>アプリ設定（<c>tts.*</c> だけを指定）を実ファイルへ保存する。</summary>
        private static void SaveAppSettings(string speakerName, int cacheMaxEntries, string assetPathOverride)
        {
            var result = new AppSettingsStore().Save(
                AppSettings.Create(
                    ttsSpeakerName: speakerName,
                    ttsCacheMaxEntries: cacheMaxEntries,
                    ttsAssetPathOverride: assetPathOverride));
            Assert.IsTrue(result.Success, string.Join(" / ", result.Warnings));
        }

        /// <summary>
        /// <see cref="TtsService.Settings"/> が <paramref name="expectedSpeakerName"/> になるまで待つ
        /// （タイムアウトしても Fail せず、値の検証は呼び出し側の <c>Assert</c> に任せて差分を見せる）。
        /// </summary>
        private static IEnumerator WaitUntilSettingsApplied(TtsService service, string expectedSpeakerName)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + DefaultTimeoutSeconds;
            while ((service.Settings == null || service.Settings.SpeakerName != expectedSpeakerName)
                   && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }
        }

        /// <summary>
        /// 1 プロセス内にもう 1 つ <see cref="NetworkManager"/> を立ててホストを開始し、
        /// 実プレハブの <c>GameSession</c>（＝ <c>TtsSyncCoordinator</c> / <c>TtsSyncPlayer</c> を含む）を
        /// スポーンさせる（<c>TtsConsentGateBootstrapTests.StartInProcessHost</c> と同じ手順）。
        /// </summary>
        private void StartInProcessHost()
        {
            _hostObject = new GameObject(nameof(TtsAppSettingsLiveTests) + "-Host");
            _hostObject.SetActive(false);

            var transport = _hostObject.AddComponent<UnityTransport>();
            transport.MaxPayloadSize = NetworkConstants.MaxPayloadSizeBytes;

            var manager = _hostObject.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = null,
            };

            manager.AddNetworkPrefab(NetworkTestPrefabs.LoadGameSession());
            manager.AddNetworkPrefab(NetworkTestPrefabs.LoadLobbyState());

            _hostObject.SetActive(true);

            _hostService = new NetworkService(manager);
            var result = _hostService.StartHost(startPort: 0);
            Assert.IsTrue(result.Success, result.Message);
            Assert.IsNotNull(_hostService.ActiveGameSession, "ホスト開始で GameSession がスポーンされるはず。");
        }

        private static bool IsNetworkBusy(NetworkService service)
            => service != null && (service.IsListening || service.IsClient || service.IsShutdownInProgress);
    }
}
