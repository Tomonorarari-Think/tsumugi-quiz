using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tests.EditMode.Core;
using TsumugiQuiz.Tests.Shared.Tts;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.UI.Settings
{
    /// <summary>
    /// <see cref="TtsAppSettingsReloader"/> の判定（issue #138）を検証する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本物の再初期化（<c>TtsService.RetryInitializeAsync</c>）は voicevox_core を読みに行くため、
    /// EditMode では <see cref="TtsAppSettingsReloader.RestartOverrideForTesting"/> で差し替えて
    /// 「呼ばれたか・何回呼ばれたか」だけを見る（docs/tts.md §11.1: EditMode は <c>External/</c> 非依存）。
    /// 保存 → 再初期化 → <c>TtsService.Settings</c> 更新まで実経路で通した検証は
    /// PlayMode の <c>TsumugiQuiz.Tests.PlayMode.UI.TtsAppSettingsLiveTests</c> が行う。
    /// </para>
    /// <para>
    /// 初期化完了の通知（<c>TtsService.StatusChanged</c>）は <c>TtsService.Update</c> から配られるが、
    /// EditMode では <c>MonoBehaviour.Update</c> が走らないため、
    /// <see cref="TtsAppSettingsReloader.DeliverStatusForTesting"/> でその配達を代行する。
    /// </para>
    /// </remarks>
    public sealed class TtsAppSettingsReloaderTests
    {
        private const int WaitMs = 20000;
        private const string SavedSpeakerName = "設定画面で保存した話者";
        private const string SecondSavedSpeakerName = "設定画面で 2 回目に保存した話者";
        private const string LastSavedSpeakerName = "設定画面で最後に保存した話者";

        private FakeConsentStorage _consentStorage;
        private readonly List<GameObject> _gameObjects = new();
        private readonly List<string> _cacheRoots = new();

        private string _originalDataRootEnvironmentValue;
        private string _dataRoot;

        /// <summary>初期化タスクを「初期化中」で止めておくための関門（<see cref="CreateInitializingService"/>）。</summary>
        private ManualResetEventSlim _engineGate;

        [SetUp]
        public void SetUp()
        {
            _consentStorage = new FakeConsentStorage();
            ConsentGate.SetStorageFactoryForTesting(() => _consentStorage);
            RecordConsent();

            // TtsSettingsProviderFactory.BuildOrNull() が保存先を解決できるようにする
            // （AppPathsTests / TtsSettingsProviderFactoryTests と同じ作法、#71）。
            _originalDataRootEnvironmentValue = Environment.GetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable);
            Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, null);
            AppPaths.Reset();
            _dataRoot = Path.Combine(
                Path.GetTempPath(), "TsumugiQuizTtsAppSettingsReloaderTests_" + Guid.NewGuid().ToString("N"));
            AppPaths.Configure(_dataRoot);
        }

        [TearDown]
        public void TearDown()
        {
            // 静的な予約・差し替えを次のテストへ持ち越さない。
            TtsAppSettingsReloader.ResetForTesting();
            ConsentGate.SetStorageFactoryForTesting(null);

            // GameObject を壊す前に関門を開ける。開けないまま OnDestroy → Shutdown へ入ると、
            // 初期化タスクの完了を最大 10 秒（ShutdownWaitMs）待つことになる。
            _engineGate?.Set();

            foreach (var go in _gameObjects)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            _gameObjects.Clear();

            _engineGate?.Dispose();
            _engineGate = null;

            AppPaths.Reset();
            Environment.SetEnvironmentVariable(
                AppPaths.DataRootEnvironmentVariable, _originalDataRootEnvironmentValue);

            _cacheRoots.Add(_dataRoot);
            foreach (var root in _cacheRoots)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
                catch (IOException)
                {
                    // 一時ディレクトリなので、消せなくてもテスト結果には影響しない。
                }
            }

            _cacheRoots.Clear();
        }

        [Test]
        public void サービスがnullなら何もしない()
        {
            Assert.That(
                TtsAppSettingsReloader.ReloadIfNeeded(null), Is.EqualTo(TtsReloadOutcome.NotNeeded));
            Assert.That(TtsAppSettingsReloader.NeedsReinitialize(null, TtsSettings.Default), Is.False);
        }

        /// <summary>
        /// まだ初期化していないなら再初期化しない。ここで走らせてしまうと、
        /// <b>設定を保存しただけで voicevox_core（ONNX Runtime）をプロセス全体へロード</b>してしまう
        /// （docs/tts.md §6.5、#25 H-5）。都度読みの provider があるので、次の初期化で自然に反映される。
        /// </summary>
        [Test]
        public void 未初期化なら再初期化しない()
        {
            var service = CreateNotInitializedService();
            Assert.That(
                service.Status.State, Is.EqualTo(TtsServiceState.NotInitialized), "前提: まだ初期化していない。");

            Assert.That(
                TtsAppSettingsReloader.NeedsReinitialize(service, TtsSettings.Create("別の話者")), Is.False,
                "未初期化のときは保存だけで初期化を始めないこと。");
        }

        [Test]
        public void 初期化済みで設定が同じなら再初期化しない()
        {
            var service = CreateReadyService(TtsSettings.Default);

            Assert.That(
                TtsAppSettingsReloader.NeedsReinitialize(service, TtsSettings.Default), Is.False,
                "同じ内容で作り直すとモデルの読み込みを無駄にやり直すことになる。");
        }

        [Test]
        public void 初期化済みで話者が変わったら再初期化する()
        {
            var service = CreateReadyService(TtsSettings.Default);

            Assert.That(
                TtsAppSettingsReloader.NeedsReinitialize(
                    service, TtsSettings.Default.WithSpeaker("別の話者", TtsSettings.DefaultStyleName)),
                Is.True,
                "話者・スタイルは合成エンジンの生成時に固定されるため、再初期化しないと反映できない。");
        }

        [Test]
        public void 初期化済みで配置の上書きが変わったら再初期化する()
        {
            var service = CreateReadyService(TtsSettings.Default);

            Assert.That(
                TtsAppSettingsReloader.NeedsReinitialize(
                    service, TtsSettings.Default.WithAssetPathOverride("C:/tmp/voicevox")),
                Is.True,
                "tts.assetPathOverride は配置の解決（VoicevoxPaths.Resolve）にしか効かないため再初期化が要る。");
        }

        [Test]
        public void 初期化済みでキャッシュ上限が変わったら再初期化する()
        {
            var service = CreateReadyService(TtsSettings.Default);

            Assert.That(
                TtsAppSettingsReloader.NeedsReinitialize(
                    service, TtsSettings.Default.WithCacheLimits(12345L, 42)),
                Is.True,
                "TtsCacheLimits も初期化時にしか読まれない。");
        }

        /// <summary>
        /// 同意を撤回した状態では再初期化しない（FR-74 / FR-75）。
        /// 撤回後に voicevox_core を読み込み直してはいけない。
        /// </summary>
        [Test]
        public void 同意を撤回していたら再初期化しない()
        {
            var service = CreateReadyService(TtsSettings.Default);
            RevokeConsent();

            Assert.That(
                TtsAppSettingsReloader.NeedsReinitialize(service, TtsSettings.Create("別の話者")), Is.False,
                "未同意・撤回後は再初期化しないこと。");
        }

        /// <summary>
        /// #138 レビュー H-1 の本体。初期化中（<see cref="TtsServiceState.Initializing"/>）に保存しても
        /// その場では <c>RetryInitializeAsync</c> を呼ばず（呼ぶと進行中の初期化タスクの完了を
        /// メインスレッドで最大 10 秒待つ）、完了の通知が届いてから<b>ちょうど 1 回だけ</b>やり直すこと。
        /// あわせて、やり直しに渡る provider が<b>保存済みの内容</b>を読むこと（#138 レビュー L-1）。
        /// </summary>
        [Test]
        public void 初期化中の保存は完了後に1回だけ再初期化する()
        {
            SaveAppSettings(SavedSpeakerName);

            var restartCount = 0;
            string restartedWithSpeakerName = null;
            TtsAppSettingsReloader.RestartOverrideForTesting = (_, provider) =>
            {
                restartCount++;
                restartedWithSpeakerName = provider.Load().SpeakerName;
            };

            var service = CreateInitializingService(TtsSettings.Default);

            var outcome = TtsAppSettingsReloader.ReloadIfNeeded(
                service, AppSettings.Create(ttsSpeakerName: SavedSpeakerName));

            Assert.That(
                outcome, Is.EqualTo(TtsReloadOutcome.Deferred),
                "初期化中は持ち越すこと（その場で再初期化するとメインスレッドが最大 10 秒止まる）。");
            Assert.That(restartCount, Is.Zero, "初期化中は RetryInitializeAsync を呼ばないこと。");
            Assert.That(
                service.Status.State, Is.EqualTo(TtsServiceState.Initializing),
                "持ち越しただけなので、進行中の初期化はそのまま続くこと。");

            ReleaseEngineGateAndWaitForReady(service);

            Assert.That(
                TtsAppSettingsReloader.DeliverStatusForTesting(service.Status), Is.True,
                "初期化完了の通知を受け取る予約が残っているはず。");
            Assert.That(restartCount, Is.EqualTo(1), "完了後に 1 回だけ再初期化すること。");
            Assert.That(
                restartedWithSpeakerName, Is.EqualTo(SavedSpeakerName),
                "やり直しに渡す provider が、保存した tts.speakerName を読むこと。");

            Assert.That(
                TtsAppSettingsReloader.DeliverStatusForTesting(service.Status), Is.False,
                "予約は届いた時点で自己解除されること（多重登録・多重実行をしない）。");
            Assert.That(restartCount, Is.EqualTo(1), "2 回目の通知では再初期化しないこと。");
        }

        /// <summary>
        /// 初期化中に何度保存しても、予約は 1 つだけ（＝完了後の再初期化も 1 回だけ）で、
        /// そのとき使われるのは<b>最後に保存した内容</b>であること（#138 レビュー L-1）。
        /// 途中の保存内容で初期化し直してしまうと、ユーザーが最後に決めた話者が反映されない。
        /// </summary>
        [Test]
        public void 初期化中に保存を繰り返しても最後の内容で1回だけ再初期化する()
        {
            var restartCount = 0;
            string restartedWithSpeakerName = null;
            TtsAppSettingsReloader.RestartOverrideForTesting = (_, provider) =>
            {
                restartCount++;
                restartedWithSpeakerName = provider.Load().SpeakerName;
            };

            SaveAppSettings(SavedSpeakerName);
            var service = CreateInitializingService(TtsSettings.Default);

            // 初期化が終わらないあいだに、設定画面で 3 回保存した状況。
            var savedSpeakerNames = new[] { SavedSpeakerName, SecondSavedSpeakerName, LastSavedSpeakerName };
            foreach (var speakerName in savedSpeakerNames)
            {
                SaveAppSettings(speakerName);

                Assert.That(
                    TtsAppSettingsReloader.ReloadIfNeeded(
                        service, AppSettings.Create(ttsSpeakerName: speakerName)),
                    Is.EqualTo(TtsReloadOutcome.Deferred));
            }

            ReleaseEngineGateAndWaitForReady(service);

            Assert.That(TtsAppSettingsReloader.DeliverStatusForTesting(service.Status), Is.True);
            Assert.That(restartCount, Is.EqualTo(1), "購読が積み上がっていないこと。");
            Assert.That(
                restartedWithSpeakerName, Is.EqualTo(LastSavedSpeakerName),
                "最後に保存した tts.speakerName で初期化し直すこと（都度読みの provider を渡しているため）。");
        }

        /// <summary>
        /// 初期化中に保存したあと、その初期化が失敗（<see cref="TtsServiceState.NotAvailable"/>）した場合は
        /// 再初期化しない（配置が直っていないので同じ失敗を繰り返すだけ）。予約は解除する。
        /// </summary>
        [Test]
        public void 初期化に失敗したら持ち越した再初期化は行わない()
        {
            SaveAppSettings(SavedSpeakerName);

            var restartCount = 0;
            TtsAppSettingsReloader.RestartOverrideForTesting = (_, __) => restartCount++;

            var service = CreateInitializingService(TtsSettings.Default);
            Assert.That(
                TtsAppSettingsReloader.ReloadIfNeeded(service, AppSettings.Create(ttsSpeakerName: SavedSpeakerName)),
                Is.EqualTo(TtsReloadOutcome.Deferred));

            LogAssert.Expect(
                LogType.Warning,
                new Regex("^\\[TtsAppSettingsReloader\\] 読み上げを初期化できなかったため"));

            Assert.That(
                TtsAppSettingsReloader.DeliverStatusForTesting(
                    TtsServiceStatus.NotAvailable("テスト用の失敗理由")),
                Is.True);

            Assert.That(restartCount, Is.Zero, "初期化に失敗したときは再初期化しないこと。");
            Assert.That(
                TtsAppSettingsReloader.DeliverStatusForTesting(TtsServiceStatus.Ready), Is.False,
                "失敗時も予約は解除すること。");
        }

        /// <summary>初期化が済んでいれば、持ち越さずその場で再初期化を始めること（対照）。</summary>
        [Test]
        public void 初期化済みならその場で再初期化する()
        {
            SaveAppSettings(SavedSpeakerName);

            var restartCount = 0;
            TtsAppSettingsReloader.RestartOverrideForTesting = (_, __) => restartCount++;

            var service = CreateReadyService(TtsSettings.Default);

            Assert.That(
                TtsAppSettingsReloader.ReloadIfNeeded(service, AppSettings.Create(ttsSpeakerName: SavedSpeakerName)),
                Is.EqualTo(TtsReloadOutcome.Started));
            Assert.That(restartCount, Is.EqualTo(1));
            Assert.That(
                TtsAppSettingsReloader.DeliverStatusForTesting(TtsServiceStatus.Ready), Is.False,
                "その場で実行したので予約は作らないこと。");
        }

        private static void SaveAppSettings(string speakerName)
        {
            var result = new AppSettingsStore().Save(AppSettings.Create(ttsSpeakerName: speakerName));
            Assert.That(result.Success, Is.True, string.Join(" / ", result.Warnings));
        }

        private void RecordConsent()
            => new ConsentStore(_consentStorage).RecordConsent(
                TermsCatalog.LoadRequiredTerms(), "0.1.0", DateTime.UtcNow);

        private void RevokeConsent() => new ConsentStore(_consentStorage).Revoke();

        /// <summary>
        /// <c>Awake</c>（<c>Instance</c> / <c>DontDestroyOnLoad</c>）を走らせないために、
        /// 非アクティブな GameObject へ載せた <see cref="TtsService"/>（<c>TtsConsentWiringTests</c> と同じ作法）。
        /// </summary>
        private TtsService CreateNotInitializedService()
        {
            var go = new GameObject(nameof(TtsAppSettingsReloaderTests) + "-Service");
            go.SetActive(false);
            _gameObjects.Add(go);
            return go.AddComponent<TtsService>();
        }

        /// <summary>フェイクエンジンで <see cref="TtsServiceState.Ready"/> まで進めた <see cref="TtsService"/>。</summary>
        private TtsService CreateReadyService(TtsSettings settings)
        {
            var cacheRoot = Path.Combine(
                Path.GetTempPath(), "TsumugiQuizTtsAppSettingsReloaderTests", Guid.NewGuid().ToString("N"));
            _cacheRoots.Add(cacheRoot);

            var service = CreateNotInitializedService();
            service.Initialize(
                new FixedTtsSettingsProvider(settings),
                cacheRoot,
                consentCheck: null,
                engineFactory: (location, engineSettings) => new FakeTtsSynthesisEngine());

            var initTask = service.EnsureInitializedAsync();
            Assert.That(SpinWaitUntil(() => initTask.IsCompleted), Is.True, "初期化待ちが完了すること。");
            Assert.That(service.Status.IsReady, Is.True, $"フェイクエンジンなら Ready になること（{service.Status}）。");
            return service;
        }

        /// <summary>
        /// 合成エンジンの生成を <see cref="_engineGate"/> で止め、<see cref="TtsServiceState.Initializing"/> の
        /// ままにした <see cref="TtsService"/>。<c>Initialize</c> は同期的に <c>Initializing</c> へ移すので、
        /// 待ちを入れずに確定した状態を作れる。
        /// </summary>
        private TtsService CreateInitializingService(TtsSettings settings)
        {
            var cacheRoot = Path.Combine(
                Path.GetTempPath(), "TsumugiQuizTtsAppSettingsReloaderTests", Guid.NewGuid().ToString("N"));
            _cacheRoots.Add(cacheRoot);

            _engineGate = new ManualResetEventSlim(false);

            var service = CreateNotInitializedService();
            service.Initialize(
                new FixedTtsSettingsProvider(settings),
                cacheRoot,
                consentCheck: null,
                engineFactory: (location, engineSettings) =>
                {
                    _engineGate.Wait();
                    return new FakeTtsSynthesisEngine();
                });

            Assert.That(
                service.Status.State, Is.EqualTo(TtsServiceState.Initializing),
                "Initialize は同期的に Initializing へ移すこと（前提）。");
            return service;
        }

        /// <summary>関門を開けて、初期化が <see cref="TtsServiceState.Ready"/> まで進むのを待つ。</summary>
        private void ReleaseEngineGateAndWaitForReady(TtsService service)
        {
            _engineGate.Set();

            Assert.That(
                SpinWaitUntil(() => service.Status.State == TtsServiceState.Ready), Is.True,
                $"フェイクエンジンなら Ready まで進むこと（{service.Status}）。");
        }

        private static bool SpinWaitUntil(Func<bool> condition)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < WaitMs)
            {
                if (condition())
                {
                    return true;
                }

                Thread.Sleep(1);
            }

            return condition();
        }
    }
}
