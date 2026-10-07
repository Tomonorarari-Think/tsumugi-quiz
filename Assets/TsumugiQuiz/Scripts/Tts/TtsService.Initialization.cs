using System;
using System.Threading;
using System.Threading.Tasks;
using TsumugiQuiz.Core;
using UnityEngine;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// <see cref="TtsService"/> のうち、アプリ設定・同意確認の登録（<see cref="ConfigureDefaults"/> /
    /// <see cref="ApplyDefaults"/>）、初期化本体（<see cref="Initialize"/> /
    /// <see cref="EnsureInitializedAsync"/> / <see cref="RetryInitializeAsync"/>）、
    /// 合成エンジンの生成・破棄（<see cref="CreateEngine"/> / <see cref="GetEngineAsync"/> /
    /// <see cref="DisposeEngine"/>）と終了処理（<see cref="Shutdown"/>）をまとめた部分（#140）。
    /// </summary>
    public sealed partial class TtsService
    {
        /// <summary>
        /// テスト専用: <see cref="Initialize"/> / <see cref="EnsureInitializedAsync"/> /
        /// <see cref="RetryInitializeAsync"/> の <c>engineFactory</c> 引数を省略した呼び出しで使う既定値（#156）。
        /// null（既定）なら voicevox_core を使う本番実装のまま。
        ///
        /// <c>engineFactory</c> を明示的に受け取らない production の呼び出し元
        /// （<c>TsumugiQuiz.UI.TtsStatusPanel.OnRetryClicked</c> の「再試行」、
        /// <c>TsumugiQuiz.UI.Views.Settings.TtsAppSettingsReloader</c> の本番の再初期化）は、
        /// 意図的に本番実装（voicevox_core）を使う設計だが、EditMode テストがこれらの UI 経由の
        /// 経路を（フェイクエンジンで作った <see cref="TtsService"/> に対して）実行してしまうと、
        /// <c>External/</c> の実 DLL が配置された環境でだけ voicevox_core の読み込みが走ってしまい、
        /// docs/tts.md §11.1（EditMode は <c>External/</c> 非依存）に反する。
        /// このプロパティをテストの SetUp でフェイクエンジンに差し替え、TearDown で
        /// <see cref="ResetForTesting"/>（または EditMode アセンブリ全体のガード値）に戻すことで、
        /// そうした経路も含めて EditMode 実行中は常にフェイクエンジンだけが使われるようにする。
        /// null に戻すと EditMode アセンブリ全体のガード（<c>TtsEngineFactoryGuardSetUp</c>、#156）が
        /// 以後のテストで効かなくなる点に注意（個々のテストの TearDown では、テスト開始前の値へ
        /// 戻すのが安全。多くの場合はガードのフェイル値）。
        /// </summary>
        internal static TtsSynthesisEngineFactory DefaultEngineFactoryOverrideForTesting { get; set; }

        /// <summary>
        /// テスト専用: <see cref="DefaultEngineFactoryOverrideForTesting"/> を既定（null＝本番実装を使う）へ
        /// 戻す（#156）。EditMode アセンブリ全体のガード（<c>TtsEngineFactoryGuardSetUp</c>）が
        /// <c>OneTimeTearDown</c> で呼ぶ想定。
        /// </summary>
        internal static void ResetForTesting() => DefaultEngineFactoryOverrideForTesting = null;

        /// <summary>
        /// <b>初期化を始めずに</b>、アプリ設定の読み込み元と同意確認だけを登録する（#127 レビュー H-1）。
        ///
        /// <c>UI</c> 層がアプリ起動時（<c>DefaultViewControllerRegistrations.ConfigureTtsConsentGate</c>、
        /// Terms/Title の振り分けと同じ場所）に一度だけ呼び、<b>「誰が最初に初期化しても
        /// 同意ゲートとアプリ設定が効いている」状態を先に作る</b>ために使う。
        /// ロビーでスポーンする <c>TtsSyncCoordinator</c> が最初の初期化者になる経路があり、
        /// そこは <c>UI</c> 層を参照できないため、この登録が無いと同意ゲートが素通りしてしまう。
        ///
        /// <b>ここで voicevox_core のロードは始めない</b>（ONNX Runtime をプロセス全体へ読み込む副作用を
        /// 読み上げを使わない画面で起こさないため。docs/tts.md §6.5、#25 H-5 と同じ方針）。
        /// 初期化は従来どおり <see cref="EnsureInitializedAsync"/> / <see cref="SynthesizeAsync"/> から始まる。
        ///
        /// <c>Application.*</c> を読みうるためメインスレッドから呼ぶこと。
        /// </summary>
        /// <param name="settingsProvider">
        /// アプリ設定の読み込み元。<b>初期化前にだけ</b>効く（初期化後は <see cref="RetryInitializeAsync"/> で渡し直す）。
        /// null なら変更しない。
        /// <b>#138</b>: UI 層が渡すのは「起動時スナップショット」ではなく<b>都度読み</b>の provider
        /// （<c>TsumugiQuiz.UI.Views.Settings.AppSettingsTtsSettingsProvider</c>）なので、
        /// ここで登録しておけば、あとで誰が最初の初期化者になっても<b>その時点で保存されている</b>
        /// <c>tts.*</c> が読まれる。
        /// </param>
        /// <param name="consentCheck">
        /// 同意確認（<see cref="Initialize"/> と同じ）。<b>初期化後でも差し替わる</b>（合成のたびに評価されるため）。
        /// null なら変更しない。
        /// </param>
        public void ConfigureDefaults(ITtsSettingsProvider settingsProvider = null, Func<bool> consentCheck = null)
        {
            lock (_gate)
            {
                ApplyDefaults(settingsProvider, consentCheck);
            }
        }

        /// <summary>
        /// <see cref="ConfigureDefaults"/> / <see cref="Initialize"/> 共通の取り込み処理。
        /// <b><see cref="_gate"/> を保持した状態で呼ぶこと。</b>
        /// </summary>
        private void ApplyDefaults(ITtsSettingsProvider settingsProvider, Func<bool> consentCheck)
        {
            if (consentCheck != null)
            {
                // 同意確認は合成のたびに評価されるので、初期化済みでも差し替える（#127 レビュー M-5）。
                _consentCheck = consentCheck;
            }

            if (_initialized)
            {
                // Settings は初期化時に一度だけ読む。ここで provider だけ差し替えても実際の設定は変わらず、
                // 「渡したのに効かない」誤解を生むので取り込まない（RetryInitializeAsync で渡し直す）。
                // #138: 初期化後にアプリ設定が変わった場合は、UI 層の
                // TsumugiQuiz.UI.Views.Settings.TtsAppSettingsReloader が保存時に
                // RetryInitializeAsync を呼び直す（話者・配置は合成エンジンの生成時に固定されるため、
                // provider を差し替えるだけでは反映できない）。
                return;
            }

            if (settingsProvider != null)
            {
                _settingsProvider = settingsProvider;
            }

            _settingsProvider ??= new DefaultTtsSettingsProvider();
        }

        /// <summary>
        /// 初期化を開始する（完了は待たない）。2 回目以降の呼び出しは何もしない。
        /// 完了を待ちたい場合は <see cref="EnsureInitializedAsync"/> を使う。
        ///
        /// <c>Application.*</c> を読むためメインスレッドから呼ぶこと。
        /// 重い初期化（辞書とモデルの読み込み）はバックグラウンドスレッドに逃がす。
        /// </summary>
        /// <param name="settingsProvider">アプリ設定の読み込み元。null なら既定値（#26 / #28 が差し込む）</param>
        /// <param name="cacheRootOverride">キャッシュディレクトリの明示指定（テスト用）</param>
        /// <param name="consentCheck">
        /// 利用規約への同意確認（#37 / #127、FR-74 / FR-75）。<b>合成のたびに呼ばれ、false なら合成しない</b>
        /// （<see cref="SynthesizeAsync"/> は null を返す）。
        /// Tts 層から UI 層の <c>ConsentGate</c> は参照できないので、
        /// UI 層が <c>TtsConsentCheckFactory.Build()</c>（= <c>ConsentGate.HasUserConsented</c>）を渡す
        /// （ゲーム中は <c>GameView</c> → <c>TtsSyncPlayer</c> 経由、読み上げプレビューは
        /// <c>QuestionEditorView</c>、再試行は <c>TtsStatusPanel</c>、#127）。
        /// null（既定）なら制限しない。
        /// </param>
        /// <param name="engineFactory">
        /// 合成エンジンの生成関数。null なら <see cref="DefaultEngineFactoryOverrideForTesting"/>、
        /// それも null なら voicevox_core を使う本番実装。
        /// EditMode テストでフェイクに差し替えるための差し替え口（<see cref="ITtsSynthesisEngine"/>）。
        /// </param>
        public void Initialize(
            ITtsSettingsProvider settingsProvider = null,
            string cacheRootOverride = null,
            Func<bool> consentCheck = null,
            TtsSynthesisEngineFactory engineFactory = null)
        {
            lock (_gate)
            {
                // #127 レビュー M-5: 2 回目以降の呼び出しでも、非 null で渡された同意確認は必ず取り込む。
                // 初期化を始めるのが「同意を知らない呼び出し元」（ロビーでスポーンした TtsSyncPlayer 等）に
                // なる場合があり、そこで _consentCheck が null に固定されると以後どこからも直せないため。
                // アプリ設定（settingsProvider）は初期化時にしか読まれない（Settings = LoadSettings()）ので、
                // 初期化済みの場合は取り込まず、RetryInitializeAsync で渡し直してもらう。
                ApplyDefaults(settingsProvider, consentCheck);

                if (_initialized) return;
                _initialized = true;

                _mainThreadId = Thread.CurrentThread.ManagedThreadId;
                // #156: engineFactory を省略した呼び出し（TtsStatusPanel.OnRetryClicked の「再試行」など、
                // 本番の実装をそのまま使うことを意図した呼び出し）は、EditMode テストでは
                // DefaultEngineFactoryOverrideForTesting（フェイクに差し替え済みなら非 null）を優先する。
                // これが無ければ voicevox_core を読みに行く本番実装にフォールバックする。
                _engineFactory = engineFactory
                    ?? DefaultEngineFactoryOverrideForTesting
                    ?? ((location, settings) => TtsSynthesisEngine.Create(location, settings));
                Settings = LoadSettings();

                // 既定のキャッシュルートは AppPaths（#71）経由で解決する。テスト実行時は
                // worktree ごとに分離できる（詳しくは TsumugiQuiz.Core.AppPaths のコメントを参照）。
                var cacheRoot = string.IsNullOrWhiteSpace(cacheRootOverride)
                    ? AppPaths.Combine(CacheDirectoryName)
                    : cacheRootOverride;

                Cache = new TtsCache(
                    cacheRoot, TtsCacheLimits.FromSettings(Settings), logWarning: Debug.LogWarning);

                Location = VoicevoxPaths.Resolve(new VoicevoxPathOverrides(Settings.AssetPathOverride));
                Debug.Log($"[TtsService] {Settings.Describe()} / {Location.Describe()} / cache={cacheRoot}");

                _lifetime = new CancellationTokenSource();
                _state = TtsServiceState.Initializing;
                _stateReason = null;

                var location = Location;
                var settings = Settings;
                _initTask = Task.Run(() => CreateEngine(location, settings), _lifetime.Token);
            }
        }

        /// <summary>
        /// 初期化を始め、完了するまで待つ。<b>読み上げを使う画面の起動シーケンスから呼ぶ</b>
        /// （#23 の同期再生。docs/tts.md §6.5）。
        ///
        /// <b>同意ゲート（#37 / #127、FR-74 / FR-75）</b>: 呼び出し側（UI 層）は
        /// <c>ConsentGate.HasUserConsented()</c> が true のときだけ本メソッドを呼ぶこと
        /// （<c>TtsSyncPlayer.InitializeAsync</c> は未同意なら呼ばない）。
        /// Tts 層から UI 層は参照できない（asmdef の依存方向は UI → Tts の一方向）ため、
        /// 判定は呼び出し側の責務になる。加えて<b>撤回（FR-75）を後から拾うため、
        /// <paramref name="consentCheck"/> にも必ず <c>TtsConsentCheckFactory.Build()</c>
        /// （= <c>ConsentGate.HasUserConsented</c>）を渡す</b>（#127）。
        ///
        /// 多重に呼んでも初期化は 1 回だけで、<b>同じ <see cref="Task"/> を返す</b>。
        /// 初期化に失敗しても例外は投げない（<see cref="Status"/> が
        /// <see cref="TtsServiceState.NotAvailable"/> になる）ので、
        /// 呼び出し側は待ったあとに <see cref="Status"/> を見て読み上げの可否を判断する。
        ///
        /// <c>Application.*</c> を読むためメインスレッドから呼ぶこと。
        /// </summary>
        /// <param name="settingsProvider">アプリ設定の読み込み元。null なら既定値（#26 / #28 が差し込む）</param>
        /// <param name="cacheRootOverride">キャッシュディレクトリの明示指定（テスト用）</param>
        /// <param name="consentCheck">同意確認（<see cref="Initialize"/> と同じ）。</param>
        /// <param name="engineFactory">合成エンジンの生成関数（<see cref="Initialize"/> と同じ）。</param>
        public Task EnsureInitializedAsync(
            ITtsSettingsProvider settingsProvider = null,
            string cacheRootOverride = null,
            Func<bool> consentCheck = null,
            TtsSynthesisEngineFactory engineFactory = null)
        {
            Initialize(settingsProvider, cacheRootOverride, consentCheck, engineFactory);

            lock (_gate)
            {
                // 終了処理のあとは _initTask が null になる。待つものが無いので完了済みを返す。
                return (Task)_initTask ?? Task.CompletedTask;
            }
        }

        /// <summary>
        /// 状態を一度リセットしてから <see cref="Initialize"/> をやり直す（#25 C-1）。
        /// <see cref="TtsStatusPanel"/> の「再試行」から呼ばれる想定。
        ///
        /// <see cref="Initialize"/> は 1 回しか実行されない（<c>_initialized</c> フラグで多重実行を防ぐ）ため、
        /// 配置ファイルを後から置いた場合の再チェックには使えない。本メソッドは
        /// <list type="bullet">
        ///   <item><description>直前のエンジン（あれば）を破棄する</description></item>
        ///   <item><description>進行中の合成の相乗り先（<see cref="_inFlight"/>）をクリアする</description></item>
        ///   <item><description>状態を <see cref="TtsServiceState.NotInitialized"/> に戻す</description></item>
        ///   <item><description><c>_initialized</c> を false に戻してから <see cref="Initialize"/> を呼び直す</description></item>
        /// </list>
        /// を行ってから、通常の初期化（<see cref="Initialize"/>）をやり直す。パラメータの意味は
        /// <see cref="Initialize"/> と同じで、省略時は既定値（設定ファイル・本番のエンジン）を使う。
        ///
        /// <c>Application.*</c> を読むためメインスレッドから呼ぶこと。
        /// </summary>
        public Task RetryInitializeAsync(
            ITtsSettingsProvider settingsProvider = null,
            string cacheRootOverride = null,
            Func<bool> consentCheck = null,
            TtsSynthesisEngineFactory engineFactory = null)
        {
            Task<ITtsSynthesisEngine> previousInitTask;
            CancellationTokenSource previousLifetime;

            lock (_gate)
            {
                previousInitTask = _initTask;
                previousLifetime = _lifetime;

                _initialized = false;
                _initTask = null;
                _lifetime = null;
                _engine = null;
                _cacheClearedForOutOfMemory = false;

                SetState(TtsServiceState.NotInitialized, null);
            }

            try
            {
                previousLifetime?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            // 実行中の合成が終わるまでブロックしうる（DisposeEngine の仕様どおり）。
            DisposeEngine(previousInitTask);
            previousLifetime?.Dispose();

            _inFlight.Clear();

            return EnsureInitializedAsync(settingsProvider, cacheRootOverride, consentCheck, engineFactory);
        }

        /// <summary>ワーカースレッドで走る初期化本体。</summary>
        private ITtsSynthesisEngine CreateEngine(VoicevoxLocation location, TtsSettings settings)
        {
            try
            {
                var engine = _engineFactory(location, settings);
                _engine = engine;
                SetState(TtsServiceState.Ready, null);
                Debug.Log($"[TtsService] 初期化しました {engine.Describe()}");
                return engine;
            }
            catch (TtsSetupException e)
            {
                // ユーザーの操作（setup-external.ps1 の実行・再インストール）で直せる内容なので UI に出してよい。
                var detail = e.OnnxMinMinor.HasValue && e.OnnxMaxMinor.HasValue
                    ? $"対応バージョンは 1.{e.OnnxMinMinor} 以上 1.{e.OnnxMaxMinor} 以下です。"
                    : null;
                SetState(
                    TtsServiceState.NotAvailable, e.Message, e.Reason ?? TtsUnavailableReason.InitializationFailed,
                    detail);
                Debug.LogWarning($"[TtsService] 読み上げを無効にして続行します: {e.Message}");
                return null;
            }
            catch (Exception e)
            {
                // VoicevoxException.Message は ResultCode を含むログ向け文言なので UI には出さない（docs/tts.md §9）。
                SetState(
                    TtsServiceState.NotAvailable, "読み上げを初期化できませんでした（詳細はログを参照）。",
                    TtsUnavailableReason.InitializationFailed);
                Debug.LogError("[TtsService] 読み上げの初期化に失敗しました。読み上げなしで続行します。");
                Debug.LogException(e);
                return null;
            }
        }

        /// <summary>初期化の完了を待ってエンジンを返す。使えない場合は null。</summary>
        private async Task<ITtsSynthesisEngine> GetEngineAsync(CancellationToken cancellationToken)
        {
            // 起動シーケンス（#23）が EnsureInitializedAsync を呼び忘れていても読み上げは成立させる。
            // ただし初回の待ち時間が伸びるので、早めに呼ぶのが望ましい。
            Initialize();

            Task<ITtsSynthesisEngine> initTask;
            lock (_gate)
            {
                initTask = _initTask;
            }

            if (initTask == null) return null;

            try
            {
                return await initTask.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                return null;
            }
        }

        private TtsSettings LoadSettings()
        {
            try
            {
                return _settingsProvider.Load() ?? TtsSettings.Default;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TtsService] アプリ設定を読めませんでした。既定値を使います: {e.Message}");
                return TtsSettings.Default;
            }
        }

        /// <summary>
        /// 取り消して破棄する。<b>ネイティブの破棄はドメインが生きているうちに終わらせたい</b>ので、
        /// 初期化中なら完了を待ってから破棄する（<see cref="ShutdownWaitMs"/> が上限）。
        /// </summary>
        private void Shutdown()
        {
            Task<ITtsSynthesisEngine> initTask;
            CancellationTokenSource lifetime;

            lock (_gate)
            {
                if (_shutdown) return;
                _shutdown = true;

                initTask = _initTask;
                lifetime = _lifetime;
                _initTask = null;
                _lifetime = null;
                _engine = null;
            }

            try
            {
                lifetime?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            DisposeEngine(initTask);

            try
            {
                Cache?.Flush();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TtsService] キャッシュの書き出しに失敗しました: {e.Message}");
            }

            lifetime?.Dispose();
            SetState(TtsServiceState.NotInitialized, null);
        }

        private static void DisposeEngine(Task<ITtsSynthesisEngine> initTask)
        {
            if (initTask == null) return;

            if (initTask.IsCompleted)
            {
                if (initTask.Status == TaskStatus.RanToCompletion) initTask.Result?.Dispose();
                return;
            }

            try
            {
                if (initTask.Wait(ShutdownWaitMs) && initTask.Status == TaskStatus.RanToCompletion)
                {
                    initTask.Result?.Dispose();
                    return;
                }
            }
            catch (AggregateException)
            {
                // 初期化が失敗していた場合。CreateEngine 側でログに残してある。
                return;
            }

            // 待ちきれなかった場合だけ後追いで破棄する（ファイナライザは無いので取りこぼさない）。
            Debug.LogWarning("[TtsService] 初期化の完了を待てませんでした。完了後に破棄します。");
            initTask.ContinueWith(
                t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion) t.Result?.Dispose();
                },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
