using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TsumugiQuiz.Core.Audio;
using UnityEngine;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// 読み上げの窓口（docs/architecture.md §2・§5、docs/tts.md §4・§6・§7）。
    /// Boot シーンに常駐させ（<c>DontDestroyOnLoad</c>）、<see cref="Instance"/> 経由でどの View からも使う
    /// （#2 <c>NetworkBootstrap</c> / #35 <c>SePlayer</c> と同じ作法）。
    ///
    /// 役割:
    /// <list type="bullet">
    ///   <item><description><see cref="EnsureInitializedAsync"/> で voicevox_core の初期化とスタイル解決を
    ///     <b>バックグラウンドスレッド</b>で行い、結果を <see cref="Status"/> で公開する</description></item>
    ///   <item><description><see cref="SynthesizeAsync"/> でキャッシュ照会 → 合成 → <c>AudioClip</c> 化</description></item>
    ///   <item><description><see cref="PrefetchAsync"/> で事前合成（docs/tts.md §7.4）</description></item>
    /// </list>
    ///
    /// 失敗時の原則（docs/tts.md §9）: <b>読み上げが失敗してもクイズは必ず続行できる</b>。
    /// 例外はすべてこの境界で捕捉し、<see cref="SynthesizeAsync"/> は <c>null</c> を返す。
    /// 呼び出し側は null を「読み上げなし」として扱えばよい。
    ///
    /// <b>初期化は Awake では行わない</b>（<c>_initializeOnAwake</c> の既定は false）。
    /// voicevox_core の初期化は ONNX Runtime をプロセス全体にロードする副作用があり、
    /// Boot シーンを読み込むだけの他のテスト・ツールにまで影響するため、
    /// <b>読み上げを使う画面の起動シーケンス（#23 の同期再生）から
    /// <see cref="EnsureInitializedAsync"/> を明示的に呼ぶ</b>（docs/tts.md §6.5）。
    /// 呼び忘れても <see cref="SynthesizeAsync"/> / <see cref="PrefetchAsync"/> が内部で同じ入口を通るので
    /// 読み上げ自体は成立するが、初回の待ち時間が伸びる。
    ///
    /// <b>同意ゲート（#37 / #127、requirements.md FR-74 / FR-75 / NFR-08）</b>:
    /// 利用規約に同意していない状態で読み上げを実行してはいけない。
    /// 判定は <c>TsumugiQuiz.UI.ConsentGate.HasUserConsented()</c> が持つが、
    /// asmdef の依存方向が <c>UI → Tts</c> の一方向で <b>Tts から UI は参照できない</b>ため、
    /// <b>確認は呼び出し側（UI 層）の責務</b>とする。UI 層は
    /// <list type="bullet">
    ///   <item><description><b>アプリ起動時に <see cref="ConfigureDefaults"/> で同意確認を登録する</b>
    ///     （#127 レビュー H-1。初期化を始めるのがロビーでスポーンした
    ///     <c>TtsSyncCoordinator</c> になる経路があり、そこは UI 層を参照できないため）</description></item>
    ///   <item><description>同意済みのときだけ <see cref="EnsureInitializedAsync"/> を呼ぶ</description></item>
    ///   <item><description><see cref="Initialize"/> / <see cref="RetryInitializeAsync"/> の
    ///     <c>consentCheck</c> にも同じ判定を渡す（合成のたびに確認され、false なら合成しない）</description></item>
    /// </list>
    /// <see cref="ReadingEnabled"/> は同意とは別の軸で、<b>撤回時にも false にしない</b>
    /// （同意し直しても読み上げが戻らなくなるため。#127 レビュー M-1）。
    ///
    /// スレッド: <see cref="SynthesizeAsync"/> は<b>メインスレッドから呼ぶこと</b>
    /// （<c>AudioClip.Create</c> がメインスレッド専用のため）。合成・パース・キャッシュ I/O は
    /// ワーカースレッドで行い、<c>AudioClip</c> 化だけメインスレッドに戻る。
    /// メインスレッドへ戻れるのは、呼び出し時に Unity の <c>SynchronizationContext</c> が
    /// 捕捉されているからである（Unity のメインスレッドから呼べば既定の <c>ConfigureAwait(true)</c> で戻る）。
    /// ワーカースレッドから呼ぶと戻り先が無いので、<see cref="EnsureMainThread"/> で弾いている。
    ///
    /// <b>ファイル構成（#140 で partial 分割、docs/tts.md §6.0）</b>: 本ファイルはフィールド・ライフサイクル
    /// （<see cref="Awake"/> / <see cref="OnDestroy"/> / <see cref="OnApplicationQuit"/> / <see cref="Update"/>）と
    /// 公開プロパティのみを持つ。初期化は <c>TtsService.Initialization.cs</c>、同意判定は
    /// <c>TtsService.Consent.cs</c>、合成は <c>TtsService.Synthesis.cs</c>、キャッシュ関連は
    /// <c>TtsService.Cache.cs</c>、状態通知は <c>TtsService.Status.cs</c> に分けてある。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class TtsService : MonoBehaviour
    {
        /// <summary>キャッシュディレクトリ名（<see cref="TsumugiQuiz.Core.AppPaths.DataRoot"/> 直下、docs/tts.md §7.2）。</summary>
        public const string CacheDirectoryName = "TtsCache";

        /// <summary>終了時に初期化・合成の完了を待つ上限（ミリ秒）。</summary>
        private const int ShutdownWaitMs = 10000;

        /// <summary>
        /// 合成失敗ログの行頭タグ。<c>scripts/common.ps1</c> の <c>$ignorePatterns</c> と
        /// EditMode テストの <c>LogAssert.Expect</c> がこの文字列に依存している。変更するときは両方直すこと。
        /// </summary>
        internal const string SynthesisFailureLogTag = "[TtsService] 合成に失敗しました";

        // 別スレッドの合成パイプラインから読むので volatile。
        [SerializeField]
        [Tooltip("このプロセスで読み上げを行うか（ローカルの ON/OFF）。本番で書き込む箇所は現時点で無い。" +
                 "ルーム設定 tts.enabled は TtsSyncCoordinator 側で同期する。" +
                 "同意ゲート（#37 / #127）はここではなく consentCheck が担当する。")]
        private volatile bool _readingEnabled = true;

        [SerializeField]
        [Tooltip("Awake で voicevox_core の初期化を始めるか。既定は false（Boot でも false）。" +
                 "起動シーケンス（#23）が EnsureInitializedAsync を呼ぶ。")]
        private bool _initializeOnAwake;

        private readonly object _gate = new object();

        /// <summary>
        /// 同じ読みの合成が二重に走らないようにするための、進行中タスクの一覧（キャッシュキー単位）。
        ///
        /// <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,Func{TKey,TValue})"/> の生成関数は
        /// 競合時に複数回呼ばれうる（辞書に入るのは 1 つだけ）ので、<see cref="Lazy{T}"/> を挟んで
        /// <b>合成そのものが 1 回しか始まらない</b>ようにしている。
        /// </summary>
        private readonly ConcurrentDictionary<string, Lazy<Task<WavData>>> _inFlight =
            new ConcurrentDictionary<string, Lazy<Task<WavData>>>(StringComparer.Ordinal);

        private ITtsSettingsProvider _settingsProvider;
        private TtsSynthesisEngineFactory _engineFactory;
        private Func<bool> _consentCheck;
        private CancellationTokenSource _lifetime;
        private Task<ITtsSynthesisEngine> _initTask;
        private volatile ITtsSynthesisEngine _engine;
        private volatile bool _cacheClearedForOutOfMemory;
        private int _mainThreadId;
        private bool _initialized;
        private bool _shutdown;

        // TtsServiceStatus は構造体でフィールドに volatile を付けられないため、
        // 状態と理由に分けて保持する（ワーカースレッドが書き、メインスレッドが読む）。
        private volatile TtsServiceState _state = TtsServiceState.NotInitialized;
        private volatile string _stateReason;

        /// <summary>
        /// <see cref="_state"/> が <see cref="TtsServiceState.NotAvailable"/> のときの理由（#25）。
        /// <see cref="TtsUnavailableReason"/> は int 基底の enum なので volatile を付けられる。
        /// それ以外の状態のときは意味を持たない（<see cref="UnavailableReason"/> 側で弾く）。
        /// </summary>
        private volatile TtsUnavailableReason _stateUnavailableReason = TtsUnavailableReason.InitializationFailed;

        /// <summary>
        /// <see cref="_stateUnavailableReason"/> の補足情報（#25 H-3）。
        /// 現状は <see cref="TtsUnavailableReason.OnnxRuntimeVersionMismatch"/> の対応バージョン範囲のみ。
        /// </summary>
        private volatile string _stateDetail;

        private TtsServiceState _lastNotifiedState = TtsServiceState.NotInitialized;
        private string _lastNotifiedReason;
        private TtsUnavailableReason _lastNotifiedUnavailableReason = TtsUnavailableReason.InitializationFailed;
        private string _lastNotifiedDetail;

        /// <summary>Boot シーンに常駐する唯一のインスタンス。Boot を通っていなければ null。</summary>
        public static TtsService Instance { get; private set; }

        /// <summary>
        /// <see cref="Status"/> が変わるたびに呼ばれる（#25、<c>TsumugiQuiz.UI.TtsStatusPanel</c> が購読する）。
        /// <b>メインスレッドから呼ばれる</b>（<see cref="Update"/> でポーリングして検知しているため）。
        /// 状態はワーカースレッドで書き換わるので、最大 1 フレーム分の遅延がある。
        /// </summary>
        public event Action<TtsServiceStatus> StatusChanged;

        /// <summary>いま読み上げを頼めるか（Ready / Initializing / NotAvailable(理由)）。</summary>
        public TtsServiceStatus Status
        {
            get
            {
                switch (_state)
                {
                    case TtsServiceState.Ready:
                        return TtsServiceStatus.Ready;
                    case TtsServiceState.Initializing:
                        return TtsServiceStatus.Initializing;
                    case TtsServiceState.NotAvailable:
                        return TtsServiceStatus.NotAvailable(_stateReason);
                    default:
                        return TtsServiceStatus.NotInitialized;
                }
            }
        }

        /// <summary>
        /// 利用できない理由（#25）。<see cref="Status"/> の状態が <see cref="TtsServiceState.NotAvailable"/> の
        /// ときだけ値を持つ。<see cref="TtsSetupException"/> が理由を分類できなかった場合や、
        /// 想定外の例外で失敗した場合は <see cref="TtsUnavailableReason.InitializationFailed"/> にフォールバックする。
        /// 未同意（<see cref="TtsUnavailableReason.ConsentNotGiven"/>）・ユーザー操作による無効化
        /// （<see cref="TtsUnavailableReason.UserSuppressed"/>）は Tts 層からは分からないため、
        /// UI 層（<c>TsumugiQuiz.UI.TtsStatusPanel</c>）がこの値に合流させる。
        /// </summary>
        public TtsUnavailableReason? UnavailableReason
            => _state == TtsServiceState.NotAvailable ? _stateUnavailableReason : (TtsUnavailableReason?)null;

        /// <summary>
        /// <see cref="UnavailableReason"/> の補足情報（#25 H-3）。現状は
        /// <see cref="TtsUnavailableReason.OnnxRuntimeVersionMismatch"/> のときだけ
        /// 「対応バージョンは 1.{min} 以上 1.{max} 以下です。」を返す。それ以外は null。
        /// </summary>
        public string UnavailableDetail
            => _state == TtsServiceState.NotAvailable ? _stateDetail : null;

        /// <summary>
        /// このプロセスで読み上げを行うか（ローカルの ON/OFF）。false のあいだ
        /// <see cref="SynthesizeAsync"/> は常に null を返す。
        ///
        /// <b>本番のコードで書き込んでいる箇所は現時点で無い</b>（#127 レビュー M-1）。
        /// ルーム設定の <c>tts.enabled</c> は <c>TsumugiQuiz.Network.TtsSyncCoordinator.SetReadingEnabled</c>
        /// （<c>NetworkVariable</c> 同期。書き込むのは <c>RoomSettingsApplier</c> だけ、docs/network.md §12.6）が
        /// 担当しており、ここへは反映していない。将来ローカル設定として接続する場合も、
        /// <b>同意ゲートの都合でここを false にしてはいけない</b>（同意し直しても読み上げが戻らなくなる）。
        ///
        /// <b>同意ゲート（#37 / #127、FR-74 / FR-75）は別の軸</b>: 未同意・撤回の反映は
        /// <see cref="ConfigureDefaults"/> / <see cref="Initialize"/> の <c>consentCheck</c>
        /// （<c>UI</c> 層が <c>TtsConsentCheckFactory.Build()</c> で渡す）が担当する。
        /// </summary>
        public bool ReadingEnabled
        {
            get => _readingEnabled;
            set => _readingEnabled = value;
        }

        /// <summary>
        /// 同意ゲート（<see cref="ConfigureDefaults"/> / <see cref="Initialize"/> の <c>consentCheck</c>）が
        /// いま通るか。判定関数が未設定なら true（制限なし）、判定が例外になった場合は false（安全側）。
        /// ログは出さない（出題ごと・フレームごとに問い合わせても騒がしくならないようにするため）。
        ///
        /// <c>TsumugiQuiz.Tts.TtsSyncPlayer</c> は、自分に明示的な <c>consentCheck</c> が
        /// 差し込まれていないとき（<c>GameView</c> の配線より前 ＝ ロビーでの初期化時）に
        /// この値を見る（#127 レビュー H-1）。
        /// </summary>
        public bool IsConsentSatisfied => EvaluateConsent();

        /// <summary>適用中のアプリ設定。<see cref="Initialize"/> 前は null。</summary>
        public TtsSettings Settings { get; private set; }

        /// <summary>解決された配置。<see cref="Initialize"/> 前は null。</summary>
        public VoicevoxLocation Location { get; private set; }

        /// <summary>ディスクキャッシュ。<see cref="Initialize"/> 前は null。</summary>
        public TtsCache Cache { get; private set; }

        /// <summary>解決されたスタイル。<see cref="TtsServiceState.Ready"/> でなければ null。</summary>
        public VoicevoxStyleResolution? ResolvedStyle => _engine?.Style;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Boot シーンが 2 度読み込まれた場合の保険。後から生成されたほうを破棄する。
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            // 既定では初期化しない（ONNX Runtime のプロセス全体へのロードを Boot の読み込みだけで起こさないため）。
            if (_initializeOnAwake) Initialize();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Shutdown();
        }

        private void OnApplicationQuit() => Shutdown();

        /// <summary>
        /// <see cref="StatusChanged"/> をメインスレッドから安全に発火するためのポーリング（#25）。
        /// <see cref="_state"/> / <see cref="_stateReason"/> / <see cref="_stateUnavailableReason"/> /
        /// <see cref="_stateDetail"/> はワーカースレッドから書き換わるので、イベントをその場で呼ぶ代わりに
        /// 毎フレーム比較し、いずれかに変化があれば通知する（UI Toolkit の要素更新はメインスレッド専用のため）。
        /// </summary>
        private void Update()
        {
            var state = _state;
            var reason = _stateReason;
            var unavailableReason = _stateUnavailableReason;
            var detail = _stateDetail;
            if (state == _lastNotifiedState && reason == _lastNotifiedReason
                && unavailableReason == _lastNotifiedUnavailableReason && detail == _lastNotifiedDetail)
            {
                return;
            }

            _lastNotifiedState = state;
            _lastNotifiedReason = reason;
            _lastNotifiedUnavailableReason = unavailableReason;
            _lastNotifiedDetail = detail;
            NotifyStatusChanged();
        }
    }
}
