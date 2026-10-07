using System;
using System.Threading;
using System.Threading.Tasks;
using TsumugiQuiz.Core.Audio;
using UnityEngine;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// サーバーが指定した <c>playAtServerTime</c> に合わせて読み上げを再生する
    /// <see cref="IReadingPlayback"/> の実装（docs/tts.md §6、docs/architecture.md §4、#23）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AudioSource</c> 1 つで <c>PlayScheduled</c> する。時刻変換と予約の判断は純関数の
    /// <see cref="PlaybackScheduler"/> に切り出してあり、本クラスは Unity API との接続だけを行う。
    /// </para>
    /// <para>
    /// 司令塔（<c>TsumugiQuiz.Network.TtsSyncCoordinator</c>）とは同じ <c>GameObject</c> に載せ、
    /// <c>GetComponent&lt;IReadingPlayback&gt;()</c> 経由で結ぶ。
    /// asmdef の依存方向（docs/architecture.md §3）により <c>Network</c> と <c>Tts</c> は
    /// 互いに参照できないため、契約は <c>Core</c> 層の <see cref="IReadingPlayback"/> に置いている。
    /// </para>
    /// <para>
    /// <b>AudioClip の解放</b>: 1 問の再生が終わった時点、次の問題の合成を始める時点、
    /// および破棄時に <see cref="TtsResult.ReleaseClip"/> を呼ぶ（docs/tts.md §6.3）。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TtsSyncPlayer : MonoBehaviour, IReadingPlayback
    {
        /// <summary>問題インデックスを保持していないことを表す値。</summary>
        private const int NoQuestionIndex = -1;

        /// <summary>
        /// 途中から再生するときに残しておく最小の秒数。
        /// <c>AudioSource.time</c> に音声の長さちょうどを入れると再生が始まらないため、わずかに手前に寄せる。
        /// </summary>
        private const double MinRemainingSec = 0.001d;

        /// <summary>
        /// 再生完了とみなすまでに終了予定時刻へ足す余裕（秒）。
        /// <c>dspTime</c> はオーディオバッファ単位（数十 ms）で進み、
        /// <c>PlayScheduled</c> の実際の開始もその粒度でずれるため、予定時刻ちょうどで
        /// 完了扱いにすると末尾が切れる・完了イベントが早すぎることがある。
        /// </summary>
        private const double ReadingEndMarginSec = 0.05d;

        private AudioSource _audioSource;
        private TtsService _serviceOverride;
        private Func<bool> _consentCheck;
        private ITtsSettingsProvider _settingsProviderOverride;
        private Func<double> _dspClock;
        private TtsResult _result;
        private bool _hasSchedule;
        private bool _destroyed;
        private int _preparedQuestionIndex = NoQuestionIndex;
        private int _scheduledQuestionIndex = NoQuestionIndex;
        private int _pendingQuestionIndex = NoQuestionIndex;
        private double _pendingTargetDspTime;

        /// <summary>
        /// 読み上げの再生が実際に始まったとき（引数は問題インデックス）。立ち絵・UI（#24）が購読する。
        /// <c>PlayScheduled</c> で予約した場合は予約した瞬間に発火する（実際の音声再開始は
        /// <c>tts.leadTimeSec</c>（既定 0.3 秒）後）。docs/tts.md §8 で設計した
        /// <c>ITtsPlaybackObserver.OnSpeakingStarted</c> 相当だが、本実装では単純な
        /// <see cref="Action{T}"/> イベントとして提供する（#24 の判断、tts.md §8 参照）。
        /// </summary>
        public event Action<int> ReadingStarted;

        /// <summary>
        /// 読み上げの再生が終わったとき（引数は問題インデックス）。立ち絵・UI（#24）が購読する。
        /// 自然な再生完了に加えて、次の問題の準備等で <see cref="CancelReading"/> により
        /// 途中で打ち切られた場合にも発火する（レビュー M4。中断か正常終了かは引数からは区別しない）。
        /// </summary>
        public event Action<int> ReadingCompleted;

        /// <summary>直近の予約内容（診断・テスト用）。</summary>
        public PlaybackSchedule LastSchedule { get; private set; }

        /// <summary>直近に受け取った再生開始時刻（サーバー時刻軸の秒）。</summary>
        public double LastPlayAtServerTime { get; private set; }

        /// <summary>直近に受け取ったホストの音声長（秒）。</summary>
        public double LastHostDurationSec { get; private set; }

        /// <summary>直近に算出した <c>dspTime</c> 軸の再生開始時刻。</summary>
        public double LastTargetDspTime { get; private set; }

        /// <summary>いま予約（または再生）中か。</summary>
        public bool IsReadingScheduled => _hasSchedule;

        /// <summary>用意済みの音声の長さ（秒）。用意していなければ 0。</summary>
        public double PreparedDurationSec => _result?.DurationSec ?? 0d;

        /// <summary>用意済みの音声の問題インデックス。用意していなければ -1。</summary>
        public int PreparedQuestionIndex => _preparedQuestionIndex;

        /// <inheritdoc />
        public bool IsReadingPossible
        {
            get
            {
                var service = ResolveService();
                if (service == null || !service.ReadingEnabled || !HasUserConsented())
                {
                    return false;
                }

                // Ready（合成できる）と Initializing（初期化中。合成の待ち時間は Ready 待ちが吸収する）だけを
                // 「読み上げられる見込みあり」とする。NotInitialized は
                // 「まだ初期化を始めていない ＝ 同意・設定がそろっていない」状態なので false
                // （ここで true を返すと、初期化していないのにサーバーが全員を待ってしまう）。
                var state = service.Status.State;
                return state == TtsServiceState.Ready || state == TtsServiceState.Initializing;
            }
        }

        /// <summary>いまの <c>AudioSettings.dspTime</c>（テストでは差し替えられる）。</summary>
        private double DspTimeNow => _dspClock != null ? _dspClock() : AudioSettings.dspTime;

        /// <summary>
        /// 使う <see cref="TtsService"/> を明示指定する（テスト・Boot を経由しない構成用）。
        /// null を渡すと <see cref="TtsService.Instance"/> に戻る。
        /// </summary>
        /// <param name="service">読み上げの窓口。</param>
        public void SetService(TtsService service) => _serviceOverride = service;

        /// <summary>
        /// 利用規約の同意確認を差し込む（#37 / #127、requirements.md FR-74 / FR-75 / NFR-08）。
        /// null（既定）なら制限しない。判定自体が例外になった場合は安全側（未同意）に倒す。
        ///
        /// <c>Tts</c> 層から <c>UI</c> 層は参照できない（asmdef の依存方向は UI → Tts の一方向）ため、
        /// 接続は呼び出し側の責務。<b>呼び出し元は <c>TsumugiQuiz.UI.Views.Game.GameView</c></b>
        /// （<c>GameView.Tts.cs</c> の <c>WireTtsSyncPlayer</c>。セッション取得時に
        /// <c>TtsConsentCheckFactory.Build()</c> = <c>ConsentGate.HasUserConsented</c> を渡す、#127）。
        /// <see cref="SetSettingsProvider"/> と同じく <see cref="InitializeAsync"/> より前に呼ぶこと
        /// （<see cref="TtsService.EnsureInitializedAsync"/> は初回呼び出し時にしか
        /// <c>consentCheck</c> を読まないため）。
        ///
        /// 判定は<b>出題のたびに</b>評価される（<see cref="IsReadingPossible"/> →
        /// <see cref="PrepareAsync"/>）。同意を撤回した場合、進行中の再生はそのまま完了させ、
        /// <b>次の問題から</b>合成・再生しない（docs/tts.md §6.6）。
        /// </summary>
        /// <param name="consentCheck">同意確認。</param>
        public void SetConsentCheck(Func<bool> consentCheck) => _consentCheck = consentCheck;

        /// <summary>
        /// アプリ設定（<c>tts.speakerName</c> 等）の読み込み元を差し込む（issue #28）。
        /// null（既定）なら <see cref="TtsService"/> 既定の <see cref="DefaultTtsSettingsProvider"/> を使う。
        ///
        /// <see cref="TtsService.EnsureInitializedAsync"/> は初回呼び出し時にしか <c>settingsProvider</c> を
        /// 読まないため（voicevox_core の初期化は明示的に一度だけ行う設計、docs/tts.md §6.5）、本メソッドは
        /// <see cref="InitializeAsync"/> より前に呼ぶこと。<c>TsumugiQuiz.Tts</c> 層から <c>UI</c> 層の
        /// <c>AppSettingsStore</c> は参照できないので、<see cref="SetConsentCheck"/> と同様に呼び出し側が注入する。
        /// 呼び出し元は <c>TsumugiQuiz.UI.Views.Game.GameView</c>（<c>GameView.Tts.cs</c>、issue #28 M1。
        /// セッション取得時に配線する）で、渡すのは
        /// <c>TsumugiQuiz.UI.Views.Settings.TtsSettingsProviderFactory.BuildOrNull()</c> が返す
        /// <c>AppSettingsTtsSettingsProvider</c>（<c>Load()</c> のたびに <c>app-settings.json</c> を
        /// 読み直す都度読みの実装、issue #138）。
        /// </summary>
        /// <param name="settingsProvider">アプリ設定の読み込み元。</param>
        public void SetSettingsProvider(ITtsSettingsProvider settingsProvider) => _settingsProviderOverride = settingsProvider;

        /// <summary>dsp 時計を差し替える（EditMode テストで時間を進めるため）。null で既定に戻る。</summary>
        /// <param name="dspClock">dsp 時刻を返す関数。</param>
        public void SetDspClock(Func<double> dspClock) => _dspClock = dspClock;

        /// <inheritdoc />
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            var service = ResolveService();
            if (service == null)
            {
                Debug.LogWarning(
                    "[TtsSyncPlayer] TtsService が見つかりません（Boot シーンを経由していない）。読み上げなしで進行します。");
                return;
            }

            if (!HasUserConsented())
            {
                // 未同意なら初期化そのものを行わない（docs/tts.md §6.5。voicevox_core =
                // ONNX Runtime のプロセス全体へのロードを、同意していない状態で起こさないため）。
                // TtsService.ReadingEnabled（ローカルの ON/OFF）はここでは書き換えない。
                // 同意とは別の軸で、撤回のたびに潰すと同意し直しても読み上げが戻らないため（レビュー M-1）。
                //
                // このメソッドはロビーでの GameSession スポーン時（TtsSyncCoordinator.OnNetworkSpawn）に
                // 呼ばれ、GameView の配線（#127）より前になる。そのため _consentCheck が未設定でも
                // TtsService 側に登録済みの同意確認へフォールバックする（HasUserConsented、レビュー H-1）。
                Debug.LogWarning("[TtsSyncPlayer] 利用規約に同意していないため読み上げを行いません（FR-74）。");
                return;
            }

            await service.EnsureInitializedAsync(
                settingsProvider: _settingsProviderOverride, consentCheck: _consentCheck).ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested || _destroyed)
            {
                return;
            }

            Debug.Log($"[TtsSyncPlayer] 読み上げの初期化が完了しました（{service.Status}）。");
        }

        /// <inheritdoc />
        public async Task<double> PrepareAsync(
            int questionIndex, string readingText, float speed, CancellationToken cancellationToken)
        {
            // 前問の予約と AudioClip を必ず片付けてから次の合成に入る（docs/tts.md §6.3）。
            CancelReading();

            var service = ResolveService();
            if (service == null || !IsReadingPossible)
            {
                return 0d;
            }

            if (!service.Status.IsReady)
            {
                // 初期化が終わっていない（Initializing / 失敗）。ここで合成を待つと
                // 1 問目に辞書とモデルの読み込み時間がそのまま乗るため、この問題は読み上げなしで進める
                // （docs/tts.md §9。初期化が終われば次の問題から読み上げられる）。
                Debug.LogWarning(
                    $"[TtsSyncPlayer] 読み上げの準備ができていないため問題 {questionIndex} は読み上げません（{service.Status}）。");
                return 0d;
            }

            if (string.IsNullOrWhiteSpace(readingText))
            {
                Debug.LogWarning($"[TtsSyncPlayer] 問題 {questionIndex} の読み上げテキストが空のため読み上げません。");
                return 0d;
            }

            var result = await service.SynthesizeAsync(readingText, speed, cancellationToken).ConfigureAwait(true);
            if (result == null)
            {
                // 読み上げ無効・未同意・配置不足・合成失敗。読み上げなしで続行する（docs/tts.md §9）。
                return 0d;
            }

            if (_destroyed || cancellationToken.IsCancellationRequested)
            {
                result.ReleaseClip();
                return 0d;
            }

            _result = result;
            _preparedQuestionIndex = questionIndex;

            // Ready のタイムアウトで先に再生開始時刻が届いていた場合は、ここで追いつく
            // （docs/tts.md §6.2「合成が終わり次第そのタイミングで再生する」）。
            ApplyPendingSchedule();

            return result.DurationSec;
        }

        /// <inheritdoc />
        public void Schedule(
            int questionIndex, double playAtServerTime, double referenceServerTimeNow, double hostDurationSec)
        {
            double targetDspTime;
            try
            {
                targetDspTime = PlaybackScheduler.ToDspTime(playAtServerTime, referenceServerTimeNow, DspTimeNow);
            }
            catch (ArgumentException e)
            {
                Debug.LogWarning($"[TtsSyncPlayer] 再生開始時刻が不正なため読み上げません（{e.Message}）。");
                return;
            }

            LastPlayAtServerTime = playAtServerTime;
            LastHostDurationSec = hostDurationSec;
            LastTargetDspTime = targetDspTime;

            if (_result == null || _preparedQuestionIndex != questionIndex)
            {
                // まだ合成が終わっていない。dsp 軸へ変換済みの目標時刻を覚えておき、完了時に追いつく。
                _pendingQuestionIndex = questionIndex;
                _pendingTargetDspTime = targetDspTime;
                return;
            }

            StartPlayback(questionIndex, targetDspTime);
        }

        /// <inheritdoc />
        public void CancelReading()
        {
            // レビュー M4: 何かが進行中だった場合は、中断されたことを購読者（立ち絵・UI）へ伝える。
            // 何も進行していなかった場合（PrepareAsync の冒頭で毎回呼ばれる等）は発火しない。
            var wasActive = _hasSchedule
                || _preparedQuestionIndex != NoQuestionIndex
                || _pendingQuestionIndex != NoQuestionIndex;
            var abortedQuestionIndex = _hasSchedule ? _scheduledQuestionIndex
                : _preparedQuestionIndex != NoQuestionIndex ? _preparedQuestionIndex
                : _pendingQuestionIndex;

            _hasSchedule = false;
            _scheduledQuestionIndex = NoQuestionIndex;
            _pendingQuestionIndex = NoQuestionIndex;
            _pendingTargetDspTime = 0d;
            _preparedQuestionIndex = NoQuestionIndex;

            StopAudioSource();
            ReleaseResult();

            if (wasActive)
            {
                InvokeReadingCompleted(abortedQuestionIndex);
            }
        }

        private void Awake()
        {
            EnsureAudioSource();
        }

        private void Update()
        {
            if (!_hasSchedule)
            {
                return;
            }

            if (DspTimeNow < LastSchedule.DspEndTime + ReadingEndMarginSec)
            {
                return;
            }

            CompleteReading(_scheduledQuestionIndex);
        }

        private void OnDestroy()
        {
            _destroyed = true;
            _hasSchedule = false;
            StopAudioSource();
            ReleaseResult();
        }

        private void StartPlayback(int questionIndex, double targetDspTime)
        {
            if (!HasUserConsented())
            {
                // 合成が終わってから再生が始まるまでには Ready 待ち（最大 tts.readyTimeoutMs）と
                // tts.leadTimeSec の隙間がある。その間に同意が撤回されたら、合成済みでも鳴らさない
                // （#127 レビュー H-2、FR-75）。既に鳴っている音は止めない（docs/tts.md §6.6）。
                Debug.LogWarning(
                    $"[TtsSyncPlayer] 利用規約の同意が撤回されたため問題 {questionIndex} の読み上げを再生しません（FR-75）。");
                CompleteReading(questionIndex);
                return;
            }

            var durationSec = _result?.DurationSec ?? 0d;
            var schedule = PlaybackScheduler.Create(targetDspTime, DspTimeNow, durationSec);

            LastSchedule = schedule;
            _scheduledQuestionIndex = questionIndex;

            if (!schedule.ShouldPlay)
            {
                Debug.LogWarning(
                    $"[TtsSyncPlayer] 問題 {questionIndex} の読み上げ時間（{durationSec:F3}s）が"
                    + $"丸ごと過ぎていたため再生しません（{schedule}）。");
                CompleteReading(questionIndex);
                return;
            }

            var source = EnsureAudioSource();
            var clip = _result.Clip;
            if (source == null || clip == null)
            {
                Debug.LogWarning($"[TtsSyncPlayer] 問題 {questionIndex} の音声が失われていたため再生しません。");
                CompleteReading(questionIndex);
                return;
            }

            source.clip = clip;
            if (schedule.PlayImmediately)
            {
                // 間に合わなかった分だけ頭を飛ばす（docs/tts.md §6.1）。
                source.time = (float)Math.Min(schedule.StartOffsetSec, Math.Max(0d, durationSec - MinRemainingSec));
                source.Play();
            }
            else
            {
                source.PlayScheduled(schedule.DspStartTime);
            }

            // 実機検証で「読み上げが実際に鳴ったか」をログから追えるようにする（#144 再レビュー NH-1。
            // 2 回目のゲームで PlayAtRpc が捨てられ音が鳴らない不具合の確認に使った）。
            Debug.Log(
                $"[TtsSyncPlayer] 問題 {questionIndex} の読み上げを再生します（長さ {durationSec:F2} 秒、"
                + (schedule.PlayImmediately ? $"{schedule.StartOffsetSec:F2} 秒遅れのため頭を飛ばして即再生" : "予約再生")
                + "）。");

            _hasSchedule = true;
            InvokeReadingStarted(questionIndex);
        }

        private void ApplyPendingSchedule()
        {
            if (_pendingQuestionIndex == NoQuestionIndex || _pendingQuestionIndex != _preparedQuestionIndex)
            {
                return;
            }

            var questionIndex = _pendingQuestionIndex;
            var targetDspTime = _pendingTargetDspTime;
            _pendingQuestionIndex = NoQuestionIndex;
            _pendingTargetDspTime = 0d;

            StartPlayback(questionIndex, targetDspTime);
        }

        private void CompleteReading(int questionIndex)
        {
            _hasSchedule = false;
            _scheduledQuestionIndex = NoQuestionIndex;
            _preparedQuestionIndex = NoQuestionIndex;

            StopAudioSource();
            ReleaseResult();

            InvokeReadingCompleted(questionIndex);
        }

        private AudioSource EnsureAudioSource()
        {
            if (_destroyed)
            {
                return null;
            }

            if (_audioSource != null)
            {
                return _audioSource;
            }

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }

            _audioSource.playOnAwake = false;
            _audioSource.loop = false;

            // 読み上げは 2D で鳴らす（立ち絵の位置に依存させない）。
            _audioSource.spatialBlend = 0f;
            return _audioSource;
        }

        private void StopAudioSource()
        {
            if (_audioSource == null)
            {
                return;
            }

            _audioSource.Stop();
            _audioSource.clip = null;
        }

        private void ReleaseResult()
        {
            var result = _result;
            _result = null;
            result?.ReleaseClip();
        }

        /// <summary>
        /// <see cref="ReadingStarted"/> を安全に発火する（レビュー M5）。
        /// 購読者（<c>CharacterView</c> 等）の例外が読み上げ本体の進行を止めないよう、ここで捕捉してログに残す。
        /// </summary>
        private void InvokeReadingStarted(int questionIndex)
        {
            try
            {
                ReadingStarted?.Invoke(questionIndex);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary><see cref="ReadingCompleted"/> を安全に発火する（レビュー M5、<see cref="InvokeReadingStarted"/> と同じ理由）。</summary>
        private void InvokeReadingCompleted(int questionIndex)
        {
            try
            {
                ReadingCompleted?.Invoke(questionIndex);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>テスト専用: <see cref="ReadingStarted"/> を直接発火する（レビュー L5、リフレクション回避）。</summary>
        internal void RaiseReadingStartedForTesting(int questionIndex) => InvokeReadingStarted(questionIndex);

        /// <summary>テスト専用: <see cref="ReadingCompleted"/> を直接発火する（レビュー L5、リフレクション回避）。</summary>
        internal void RaiseReadingCompletedForTesting(int questionIndex) => InvokeReadingCompleted(questionIndex);

        private TtsService ResolveService() => _serviceOverride != null ? _serviceOverride : TtsService.Instance;

        /// <summary>
        /// 同意確認。<see cref="SetConsentCheck"/> で明示的に差し込まれていればそれを使い、
        /// 未設定なら <see cref="TtsService.IsConsentSatisfied"/>（UI 層がアプリ起動時に
        /// <see cref="TtsService.ConfigureDefaults"/> で登録したもの）へフォールバックする
        /// （#127 レビュー H-1。ロビーでの初期化は <c>GameView</c> の配線より前に走るため）。
        /// どちらも無ければ制限しない。判定が例外になった場合は未同意として扱う。
        /// </summary>
        private bool HasUserConsented()
        {
            var check = _consentCheck;
            if (check == null)
            {
                var service = ResolveService();
                return service == null || service.IsConsentSatisfied;
            }

            try
            {
                return check();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TtsSyncPlayer] 同意確認に失敗したため未同意として扱います（{e.GetType().Name}）。");
                return false;
            }
        }
    }
}
