using System;
using System.Collections.Generic;
using System.Threading;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Audio;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// 読み上げ（TTS）の同期再生を仕切る <see cref="NetworkBehaviour"/>
    /// （docs/tts.md §6、docs/network.md §7.3 / §8.6、#23）。
    /// <see cref="GameSession"/> と同じ <c>NetworkObject</c>
    /// （<c>Assets/TsumugiQuiz/Prefabs/GameSession.prefab</c>）に載せて使う。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 1 問の流れ:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     出題（<see cref="GameSession.QuestionShown"/>）を全ピアが受け取る。
    ///     サーバーは Ready 待ちを開始し、<b>受付開始 T0 を「Ready 待ちの上限 + 保留マージン」まで予約</b>して
    ///     読み上げ前に早押しが開いてしまうのを防ぐ（docs/network.md §8.6 のゲートの最大値）。
    ///   </description></item>
    ///   <item><description>
    ///     各クライアント（ホストも含む）は読み上げ音声を用意し、
    ///     <see cref="TtsReadyRpc"/> で長さを報告する。読み上げが使えないクライアントは即座に 0 秒で報告する。
    ///   </description></item>
    ///   <item><description>
    ///     全員 Ready、または <see cref="ReadyTimeoutSec"/> 経過で、サーバーが
    ///     <c>playAtServerTime = ServerTime.Time + </c><see cref="LeadTimeSec"/> を決めて
    ///     <see cref="PlayAtRpc"/> を配信する。読み上げ時間は<b>ホストの合成結果だけを正</b>とする。
    ///   </description></item>
    ///   <item><description>
    ///     サーバーは <see cref="GameSession.NotifyReadingStarted"/>（<c>buzz.allowDuringReading = true</c>）か
    ///     <see cref="GameSession.NotifyReadingCompleted"/>（false）で T0 を確定する。
    ///   </description></item>
    ///   <item><description>
    ///     各クライアントは <see cref="IReadingPlayback.Schedule"/> で
    ///     <c>AudioSource.PlayScheduled</c> の予約に変換する。
    ///   </description></item>
    /// </list>
    /// <para>
    /// 読み上げの実装（<c>TsumugiQuiz.Tts.TtsSyncPlayer</c>）は同じ <c>GameObject</c> から
    /// <see cref="IReadingPlayback"/>（<c>Core</c> 層）として取得する。
    /// asmdef の依存方向（docs/architecture.md §3）で <c>Network</c> は <c>Tts</c> を参照できないため。
    /// </para>
    /// <para>
    /// ルーム設定（<c>tts.enabled</c> / <c>tts.speed</c> / <c>tts.readyTimeoutMs</c> /
    /// <c>tts.leadTimeSec</c> / <c>buzz.allowDuringReading</c>、docs/room-settings.md）との接続は #26。
    /// 本 issue では既定値の定数で動かす。
    /// </para>
    /// <para>
    /// <b>利用規約の同意（#37 / #127、requirements.md FR-74 / FR-75 / NFR-08）</b>:
    /// 同意していない・撤回したクライアントは <see cref="IReadingPlayback.IsReadingPossible"/> が
    /// false になり、<b>配置不足で読み上げられないクライアントと同じ扱い</b>になる
    /// （合成せず、長さ 0 で即 Ready を返すのでホストの Ready 待ちを引き延ばさない。docs/tts.md §6.6）。
    /// ホスト自身が未同意なら <c>BeginReadyRound</c> が同期再生そのものを行わない。
    /// 判定（<c>ConsentGate.HasUserConsented</c>）を渡すのは <c>UI</c> 層の責務
    /// （<c>Network</c> / <c>Tts</c> 層から <c>UI</c> 層は参照できないため）。
    /// <b><see cref="OnNetworkSpawn"/> の <c>InitializeReadingAsync</c> は
    /// <c>GameView</c> の配線より前に走る</b>ので、そこで効くのはアプリ起動時に
    /// <c>TtsService.ConfigureDefaults</c> で登録された判定のほう（#127 レビュー H-1、docs/tts.md §6.5）。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GameSession))]
    public sealed partial class TtsSyncCoordinator : NetworkBehaviour
    {
        /// <summary>Ready を待つ上限（秒）。ルーム設定 <c>tts.readyTimeoutMs</c> の既定 3000ms。</summary>
        public const double DefaultReadyTimeoutSec = 3.0;

        /// <summary>Ready を待つ上限の下限（<c>tts.readyTimeoutMs</c> の範囲 0〜15000ms）。</summary>
        public const double MinReadyTimeoutSec = 0.0;

        /// <summary>Ready を待つ上限の上限。</summary>
        public const double MaxReadyTimeoutSec = 15.0;

        /// <summary>再生開始を現在時刻からどれだけ先に置くか（秒）。ルーム設定 <c>tts.leadTimeSec</c> の既定。</summary>
        public const double DefaultLeadTimeSec = 0.3;

        /// <summary><c>tts.leadTimeSec</c> の下限。</summary>
        public const double MinLeadTimeSec = 0.1;

        /// <summary><c>tts.leadTimeSec</c> の上限。</summary>
        public const double MaxLeadTimeSec = 2.0;

        /// <summary>
        /// Ready 待ちの期限に足す保留マージン（秒）。受付開始 T0 は
        /// 「<see cref="ReadyTimeoutSec"/> + max(<see cref="LeadTimeSec"/>, この値)」先に予約する。
        /// </summary>
        /// <remarks>
        /// <see cref="QuestionDistributor.AckHoldMarginSec"/> と同じ考え方。
        /// Ready のタイムアウト判定（本クラスの tick）が、受付開始（状態機械の tick）より
        /// <b>必ず先に走る</b>ようにするためのもので、同じ <c>NetworkObject</c> 上の
        /// コンポーネント順（= tick 購読順）に結果が依存しなくなる。
        /// <c>tts.leadTimeSec</c> を小さくしても（下限 0.1 秒）このマージンは縮まない。
        /// </remarks>
        public const double ReadyHoldMarginSec = 0.1;

        /// <summary>読み上げ速度（ルーム設定 <c>tts.speed</c> の既定）。接続は #26。</summary>
        public const float DefaultSpeed = 1.0f;

        /// <summary>
        /// 読み上げ速度の下限（<c>tts.speed</c> の範囲 0.5〜2.0）。
        /// <c>TsumugiQuiz.Tts.TtsSpeed</c> と同じ値だが、<c>Network</c> から <c>Tts</c> は参照できないため
        /// ここにも定数を置く（実際のクランプは合成側でも行われる）。
        /// </summary>
        public const float MinSpeed = 0.5f;

        /// <summary>読み上げ速度の上限。</summary>
        public const float MaxSpeed = 2.0f;

        /// <summary>読み上げ中でも早押しを受け付けるか（ルーム設定 <c>buzz.allowDuringReading</c> の既定）。</summary>
        public const bool DefaultAllowBuzzDuringReading = true;

        private readonly TtsReadyTracker _readyTracker = new TtsReadyTracker();

        /// <summary>
        /// 読み上げを行うか（ルーム設定 <c>tts.enabled</c>）。サーバーが書き、クライアントは読むだけ。
        /// クライアントもこの値を見るので、無効のときは合成そのものを始めない。
        /// </summary>
        private readonly NetworkVariable<bool> _readingEnabled = new NetworkVariable<bool>(true);

        private GameSession _session;
        private IReadingPlayback _playback;

        /// <summary>
        /// <see cref="_playback"/> が <see cref="MonoBehaviour"/> 実装のときの参照。
        /// Unity の <c>==</c> オーバーロードで「破棄済み」を判定するために、インターフェースとは別に持つ。
        /// </summary>
        private MonoBehaviour _playbackBehaviour;

        /// <summary>
        /// <see cref="SetPlayback"/> で明示指定されたか。true のときは
        /// <c>GetComponent</c> で上書きしない（null 指定＝「読み上げなし」を尊重する）。
        /// </summary>
        private bool _playbackExplicitlySet;

        private NetworkTickSystem _subscribedTickSystem;
        private CancellationTokenSource _lifetime;
        private CancellationTokenSource _prepare;
        private bool _subscribedClientDisconnect;
        private bool _subscribedQuestionShown;
        private bool _subscribedPhase;

        /// <summary>このピアが直近に提示された問題インデックス（古い <see cref="PlayAtRpc"/> を捨てるため）。</summary>
        private int _shownQuestionIndex = TtsReadyTracker.NoQuestionIndex;

        /// <summary>
        /// 途中参加・再接続の再同期で受け取った問題インデックス（#109、統括判断）。
        /// この問題の読み上げは<b>行わない</b>（合成を始めず Ready も返さない）ので、
        /// あとから <see cref="PlayAtRpc"/> が届いても警告ではなく情報ログにして捨てるために覚えておく。
        /// </summary>
        private int _resyncedQuestionIndex = TtsReadyTracker.NoQuestionIndex;

        private double _readyTimeoutSec = DefaultReadyTimeoutSec;
        private double _leadTimeSec = DefaultLeadTimeSec;
        private float _speed = DefaultSpeed;

        /// <summary>
        /// 読み上げを行うか（ルーム設定 <c>tts.enabled</c>）。書き込みはサーバーのみ
        /// （<see cref="SetReadingEnabled"/>）。false のあいだは Ready 待ちも
        /// <see cref="PlayAtRpc"/> の配信も行わず、受付は配信完了と同時に開く。
        /// クライアントもこの値を見て合成を始めない。
        /// ルーム設定 <c>tts.enabled</c> からの配線は <c>RoomSettingsApplier</c> が行う（#27）。
        /// 本プロパティは「実行時の読み上げ ON / OFF」を表し、<c>tts.enabled</c> はその確定値を決める入力。
        /// </summary>
        public NetworkVariable<bool> ReadingEnabled => _readingEnabled;

        /// <summary>
        /// 読み上げ中でも早押しを受け付けるか（ルーム設定 <c>buzz.allowDuringReading</c>、既定 true）。
        /// true なら T0 = 再生開始時刻、false なら T0 = 読み上げ完了時刻（docs/network.md §7.3）。
        /// サーバー側だけで使う値なので同期しない。
        /// ルーム設定 <c>buzz.allowDuringReading</c> から <c>RoomSettingsApplier</c> が設定する（#27）。
        /// </summary>
        public bool AllowBuzzDuringReading { get; set; } = DefaultAllowBuzzDuringReading;

        /// <summary>Ready を待つ上限（秒）。範囲外の値は <see cref="MinReadyTimeoutSec"/>〜<see cref="MaxReadyTimeoutSec"/> に丸める。</summary>
        public double ReadyTimeoutSec
        {
            get => _readyTimeoutSec;
            set => _readyTimeoutSec = Clamp(value, MinReadyTimeoutSec, MaxReadyTimeoutSec, DefaultReadyTimeoutSec);
        }

        /// <summary>再生開始までの先行時間（秒）。範囲外の値は <see cref="MinLeadTimeSec"/>〜<see cref="MaxLeadTimeSec"/> に丸める。</summary>
        public double LeadTimeSec
        {
            get => _leadTimeSec;
            set => _leadTimeSec = Clamp(value, MinLeadTimeSec, MaxLeadTimeSec, DefaultLeadTimeSec);
        }

        /// <summary>読み上げ速度（<c>tts.speed</c>、0.5〜2.0）。</summary>
        public float Speed
        {
            get => _speed;
            set => _speed = (float)Clamp(value, MinSpeed, MaxSpeed, DefaultSpeed);
        }

        /// <summary>この問題の再生開始時刻が決まったとき（問題インデックス・再生開始サーバー時刻・読み上げ時間）。</summary>
        public event Action<int, double, double> ReadingScheduled;

        /// <summary>
        /// Ready 待ちが終わったとき（サーバーのみ）。引数は（問題インデックス, タイムアウトしたか）。
        /// </summary>
        public event Action<int, bool> ReadyRoundCompleted;

        /// <summary>Ready 待ちの状態（テスト・診断用）。</summary>
        public TtsReadyTracker ReadyTracker => _readyTracker;

        /// <summary>直近に配信・受信した再生開始時刻（サーバー時刻軸の秒）。未配信なら 0。</summary>
        public double LastPlayAtServerTime { get; private set; }

        /// <summary>直近に配信・受信した読み上げ時間（秒）。</summary>
        public double LastDurationSec { get; private set; }

        /// <summary>直近に配信・受信した読み上げの問題インデックス。未配信なら -1。</summary>
        public int LastReadingQuestionIndex { get; private set; } = TtsReadyTracker.NoQuestionIndex;

        /// <summary>
        /// 読み上げの実装を明示指定する（テスト・UI 層からの配線用）。
        /// <b>null を渡すと「読み上げなし」</b>として扱い、以後 <c>GetComponent</c> で拾い直さない。
        /// </summary>
        /// <param name="playback">読み上げの実装。null で「読み上げなし」。</param>
        public void SetPlayback(IReadingPlayback playback)
        {
            _playback = playback;
            _playbackBehaviour = playback as MonoBehaviour;
            _playbackExplicitlySet = true;
        }

        /// <summary>
        /// 読み上げの ON / OFF を切り替える（サーバーのみ、ルーム設定 <c>tts.enabled</c>）。
        /// </summary>
        /// <param name="enabled">読み上げを行うか。</param>
        /// <returns>設定できたら true。</returns>
        public bool SetReadingEnabled(bool enabled)
        {
            if (IsSpawned && !IsServer)
            {
                Debug.LogWarning("[TtsSyncCoordinator] 読み上げの ON / OFF はサーバーでのみ変更できます。");
                return false;
            }

            _readingEnabled.Value = enabled;
            return true;
        }

        /// <inheritdoc />
        public override void OnNetworkSpawn()
        {
            _session = _session != null ? _session : GetComponent<GameSession>();
            AttachPlaybackComponent();

            if (_session == null)
            {
                Debug.LogWarning("[TtsSyncCoordinator] GameSession が同じ GameObject にないため同期再生を行いません。");
                return;
            }

            _lifetime = new CancellationTokenSource();
            _shownQuestionIndex = TtsReadyTracker.NoQuestionIndex;
            _resyncedQuestionIndex = TtsReadyTracker.NoQuestionIndex;

            _session.QuestionShown += HandleQuestionShown;
            _subscribedQuestionShown = true;

            // 「ロビーへ戻る」で進行がリセットされたら、再同期の記録も捨てる（PR #114 再レビュー LOW-5）。
            _session.Phase.OnValueChanged += HandlePhaseChanged;
            _subscribedPhase = true;

            if (IsServer)
            {
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
                _subscribedClientDisconnect = true;

                _subscribedTickSystem = NetworkManager.NetworkTickSystem;
                if (_subscribedTickSystem != null)
                {
                    _subscribedTickSystem.Tick += HandleServerTick;
                }
            }

            // 読み上げを使う画面の起動シーケンスから初期化を始める（docs/tts.md §6.5）。
            // voicevox_core の初期化は ONNX Runtime をプロセス全体にロードする副作用があるため、
            // 「読み上げが有効」かつ「同意済み」のときだけ呼ぶ（同意の確認は実装側が行う）。
            if (IsClient && _readingEnabled.Value)
            {
                InitializeReadingAsync();
            }
        }

        /// <inheritdoc />
        public override void OnNetworkDespawn()
        {
            if (_session != null && _subscribedQuestionShown)
            {
                _session.QuestionShown -= HandleQuestionShown;
            }

            _subscribedQuestionShown = false;

            if (_session != null && _subscribedPhase)
            {
                _session.Phase.OnValueChanged -= HandlePhaseChanged;
            }

            _subscribedPhase = false;

            UnsubscribeClientDisconnect();
            UnsubscribeTick();
            CancelPrepare();
            CancelLifetime();

            _readyTracker.Reset();
            ResolvePlayback()?.CancelReading();

            _shownQuestionIndex = TtsReadyTracker.NoQuestionIndex;
            _resyncedQuestionIndex = TtsReadyTracker.NoQuestionIndex;
            ResetLastReading();
            _rejectLogger = null; // #72: 次のスポーンで作り直す（RpcRateGuard と同じ遅延生成の方針）。
            _rpcRateGuard = null; // #52: 次のスポーンで新しい NetworkManager に対して作り直す。
        }

        /// <inheritdoc />
        public override void OnDestroy()
        {
            UnsubscribeClientDisconnect();
            UnsubscribeTick();
            CancelPrepare();
            CancelLifetime();
            base.OnDestroy();
        }

        private void Awake()
        {
            _session = GetComponent<GameSession>();
            AttachPlaybackComponent();
        }

        /// <summary>同じ <c>GameObject</c> の読み上げ実装を拾う（明示指定済みなら何もしない）。</summary>
        private void AttachPlaybackComponent()
        {
            if (_playbackExplicitlySet || _playback != null)
            {
                return;
            }

            _playback = GetComponent<IReadingPlayback>();
            _playbackBehaviour = _playback as MonoBehaviour;
        }

        /// <summary>
        /// 生きている読み上げ実装を返す。<see cref="MonoBehaviour"/> 実装が破棄済みなら null。
        /// </summary>
        /// <returns>読み上げ実装。無ければ null。</returns>
        private IReadingPlayback ResolvePlayback()
        {
            if (_playback == null)
            {
                return null;
            }

            if (ReferenceEquals(_playbackBehaviour, null))
            {
                // MonoBehaviour ではない実装（テストのフェイクなど）。Unity の破棄判定は不要。
                return _playback;
            }

            // Unity の == オーバーロード: 破棄済みのコンポーネントは null 扱いになる。
            if (_playbackBehaviour == null)
            {
                _playback = null;
                _playbackBehaviour = null;
                return null;
            }

            return _playback;
        }

        /// <summary>
        /// 進行フェーズが変わったときの処理（全ピア）。
        /// <see cref="QuizPhase.Lobby"/> へ戻った（<c>GameSession.ReturnToLobby</c> / 新しい進行の開始）なら、
        /// 「この問題は読み上げない」という再同期の記録を捨てる（PR #114 再レビュー LOW-5）。
        /// </summary>
        /// <remarks>
        /// 残したままだと、次の進行で<b>同じ問題インデックス</b>が通常の出題として来たときに、
        /// その <see cref="PlayAtRpc"/> を「合流した問題」と誤認して捨てうる
        /// （通常の提示を受ければ <see cref="HandleQuestionShown"/> がクリアするので実害は出にくいが、
        /// ロビーに戻った時点で捨てておくほうが状態として素直）。
        /// </remarks>
        /// <param name="previous">直前のフェーズ。</param>
        /// <param name="current">現在のフェーズ。</param>
        private void HandlePhaseChanged(QuizPhase previous, QuizPhase current)
        {
            if (current == QuizPhase.Lobby)
            {
                _resyncedQuestionIndex = TtsReadyTracker.NoQuestionIndex;
            }
        }

        /// <summary>
        /// 出題（提示）を受け取ったときの処理。サーバーは Ready 待ちを始め、
        /// クライアント（ホストを含む）は読み上げ音声の用意を始める。
        /// </summary>
        /// <remarks>
        /// <b>途中参加・再接続の再同期（<paramref name="source"/> が
        /// <see cref="QuestionShownSource.Resync"/>）では何もしない</b>（統括判断 #109、docs/tts.md §6.7）。
        /// 合流した時点で読み上げは既に始まっている（あるいは終わっている）ため、
        /// いまから合成しても同期して鳴らせず、Ready を返してもサーバーは待っていないので
        /// 棄却ログが増えるだけになる。合流したクライアントは<b>その問題だけ音声なし</b>で進み、
        /// 次の問題から通常どおり読み上げに参加する。
        /// </remarks>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="question">配信された問題データ（正解は含まない）。</param>
        /// <param name="source">発火の経路（通常の提示か、再同期か）。</param>
        private void HandleQuestionShown(int questionIndex, QuestionDto question, QuestionShownSource source)
        {
            if (!IsSpawned)
            {
                return;
            }

            if (source == QuestionShownSource.Resync)
            {
                // 現在問の読み上げには参加しない。_shownQuestionIndex は更新せず、
                // 「この問題は読み上げない」ことだけを覚える（PlayAtRpc の扱いに使う）。
                _resyncedQuestionIndex = questionIndex;
                return;
            }

            _shownQuestionIndex = questionIndex;
            _resyncedQuestionIndex = TtsReadyTracker.NoQuestionIndex;

            // この問題の PlayAtRpc はこの後に届く。前の問題（前のゲームを含む）の記録は捨てる（#144 再レビュー NH-1）。
            ResetLastReading();

            // サーバー側を先に済ませる。ホストでは自分自身の Ready がこの直後に届きうるため。
            if (IsServer)
            {
                BeginReadyRound(questionIndex);
            }

            if (!IsClient)
            {
                return;
            }

            if (!_readingEnabled.Value)
            {
                // tts.enabled = false。合成そのものを始めない（サーバーも Ready を待っていない）。
                return;
            }

            PrepareReadingAsync(questionIndex, ResolveReadingText(question));
        }

        /// <summary>
        /// Ready 待ちを始め、読み上げが終わるまで早押し受付が開かないようゲートを予約する（サーバーのみ）。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        private void BeginReadyRound(int questionIndex)
        {
            _readyTracker.Reset();

            if (!_readingEnabled.Value)
            {
                // tts.enabled = false。読み上げを飛ばし、配信完了と同時に受付を開く（docs/tts.md §9）。
                return;
            }

            var playback = ResolvePlayback();
            if (playback == null || !playback.IsReadingPossible)
            {
                // ホスト自身が読み上げられない構成（配置不足・未同意・Tts 未搭載）。
                // 全員を待たせても読み上げは鳴らないので、同期再生そのものを行わない。
                return;
            }

            var serverNow = NetworkManager.ServerTime.Time;
            _readyTracker.Begin(
                questionIndex, ResolveReadyClientIds(), NetworkManager.ServerClientId, serverNow, ReadyTimeoutSec);

            // Ready 待ちの上限 + 保留マージンまで受付を開かない（docs/network.md §8.6 のゲートの最大値）。
            // 全員 Ready が揃えば、この予約は実際の playAtServerTime まで引き下げられる。
            var holdSec = ReadyTimeoutSec + Math.Max(LeadTimeSec, ReadyHoldMarginSec);
            if (!_session.SetBuzzOpenTime(serverNow + holdSec))
            {
                Debug.LogWarning(
                    $"[TtsSyncCoordinator] 問題 {questionIndex} の読み上げ用に受付開始を保留できませんでした。"
                    + "読み上げ前に早押しが開く可能性があります。");
            }

            if (_readyTracker.IsComplete)
            {
                // 待つ相手が居ない（問題データが誰にも届いていない）。すぐ次へ進む。
                CompleteReadyRound(timedOut: false);
            }
        }

        /// <summary>
        /// Ready を待つ相手。<b>問題データの受信確認（Ack）を返したクライアント</b>に合わせる（#13 / §8.6）。
        /// 問題データが届いていないクライアントは出題（提示）を処理できず Ready も返せないため、
        /// 待ち対象に入れるとタイムアウトぶんだけ全員が待たされる。
        /// </summary>
        /// <returns>Ready を待つクライアント ID。</returns>
        private IEnumerable<ulong> ResolveReadyClientIds()
        {
            var acked = _session.Distributor?.AckedClientIds;
            if (acked != null && acked.Count > 0)
            {
                return acked;
            }

            // 配信器が無い構成（テスト等）では接続中の全クライアントを待つ。
            return NetworkManager.ConnectedClientsIds;
        }

        /// <summary>Ready 待ちの期限を監視する（サーバーのみ）。</summary>
        private void HandleServerTick()
        {
            if (!IsSpawned || !IsServer || !_readyTracker.IsAwaiting)
            {
                return;
            }

            if (!_readyTracker.HasTimedOut(NetworkManager.ServerTime.Time))
            {
                return;
            }

            CompleteReadyRound(timedOut: true);
        }

        /// <summary>
        /// Ready 待ちを終え、再生開始時刻を全員へ配って受付開始 T0 を確定する（サーバーのみ）。
        /// </summary>
        /// <param name="timedOut">Ready が揃わずタイムアウトしたか。</param>
        private void CompleteReadyRound(bool timedOut)
        {
            var questionIndex = _readyTracker.QuestionIndex;
            var durationSec = _readyTracker.AuthoritativeDurationSec;

            if (timedOut && _readyTracker.PendingCount > 0)
            {
                Debug.LogWarning(
                    $"[TtsSyncCoordinator] 問題 {questionIndex} の読み上げ準備が {ReadyTimeoutSec} 秒で揃いませんでした"
                    + $"（未報告: {string.Join(", ", _readyTracker.PendingClientIds)}）。読み上げを開始します。");
            }

            _readyTracker.Reset();

            var playAtServerTime = NetworkManager.ServerTime.Time + LeadTimeSec;
            PlayAtRpc(questionIndex, playAtServerTime, durationSec);

            // 受付開始 T0（docs/network.md §7.3 / §8.6）。
            var accepted = AllowBuzzDuringReading
                ? _session.NotifyReadingStarted(playAtServerTime)
                : _session.NotifyReadingCompleted(playAtServerTime + durationSec);

            if (!accepted)
            {
                Debug.LogWarning(
                    $"[TtsSyncCoordinator] 問題 {questionIndex} の受付開始時刻を反映できませんでした"
                    + "（既に受付が開いている可能性があります）。");
            }

            ReadyRoundCompleted?.Invoke(questionIndex, timedOut);
        }

        /// <summary>切断したクライアントの Ready は待たない（1 人のために全員を待たせない）。</summary>
        /// <param name="clientId">切断したクライアント ID。</param>
        private void HandleClientDisconnected(ulong clientId)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            _rejectLogger?.Forget(clientId); // #72: 棄却ログの間引き記録も切断時に捨てる。
            _rpcRateGuard?.Forget(clientId); // #52: レート制限の状態も切断時に捨てる（docs/network.md §9）。

            if (!_readyTracker.RemoveClient(clientId) || !_readyTracker.IsComplete)
            {
                return;
            }

            CompleteReadyRound(timedOut: false);
        }

        /// <summary>読み上げるテキスト。読みが空なら問題文を使う（docs/tts.md §7.1）。</summary>
        /// <param name="question">問題データ。</param>
        /// <returns>読み上げるテキスト。</returns>
        private static string ResolveReadingText(QuestionDto question)
        {
            if (question == null)
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(question.ReadingText) ? question.Text : question.ReadingText;
        }

        private static double Clamp(double value, double min, double max, double fallback)
        {
            if (double.IsNaN(value))
            {
                return fallback;
            }

            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private void UnsubscribeTick()
        {
            if (_subscribedTickSystem == null)
            {
                return;
            }

            _subscribedTickSystem.Tick -= HandleServerTick;
            _subscribedTickSystem = null;
        }

        private void UnsubscribeClientDisconnect()
        {
            if (!_subscribedClientDisconnect)
            {
                return;
            }

            if (NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            _subscribedClientDisconnect = false;
        }

        private void CancelLifetime()
        {
            var lifetime = _lifetime;
            _lifetime = null;
            if (lifetime == null)
            {
                return;
            }

            lifetime.Cancel();
            lifetime.Dispose();
        }

        /// <summary>進行中の合成を取り消す。<see cref="_prepare"/> は必ず null に戻す。</summary>
        private void CancelPrepare()
        {
            var prepare = _prepare;
            _prepare = null;
            if (prepare == null)
            {
                return;
            }

            prepare.Cancel();
            prepare.Dispose();
        }
    }
}
