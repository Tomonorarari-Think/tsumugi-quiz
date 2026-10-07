using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;
using TsumugiQuiz.Questions;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// サーバー権限のゲーム進行を担う <see cref="NetworkBehaviour"/>（docs/network.md §1.2 / §6、
    /// docs/architecture.md §4）。進行ロジックそのものは <see cref="QuizStateMachine"/>
    /// （<c>TsumugiQuiz.Core</c>、純 C#）に置き、本クラスは
    /// 「NGO の時刻・<c>NetworkVariable</c>・RPC」と状態機械をつなぐ薄いアダプタに徹する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 状態（フェーズ・問題インデックス・フェーズ開始時刻・T0・ロック保持者）は
    /// <c>NetworkVariable</c> で同期する。書き込み権限はサーバーのみ（NGO 既定）で、
    /// クライアントは読み取り専用。残り時間はクライアントが
    /// 「<see cref="PhaseStartServerTime"/>（または <see cref="BuzzOpenServerTime"/>）＋制限時間」から計算する
    /// （制限時間はクライアントでは確定したルーム設定から読む。GameSession.Deadline.cs、#154）。
    /// </para>
    /// <para>
    /// 正解データはサーバー内（<see cref="QuizStateMachine"/>）にのみ置き、
    /// Result フェーズの <see cref="QuestionResultRpc"/> で初めてクライアントへ送る（仮決め K14）。
    /// </para>
    /// <para>
    /// 本 issue（#12）の範囲は「1 問・freeText のみ」。
    /// 問題データの配信は同じ <c>NetworkObject</c> に載せた <see cref="QuestionDistributor"/> が担い
    /// （#13、docs/network.md §8）、本クラスは提示（<see cref="QuestionShownRpc"/>）と
    /// 受信確認の完了待ちだけを扱う。TTS 連携（#23）は
    /// <see cref="SetBuzzOpenTime"/>（および <see cref="NotifyReadingStarted"/> /
    /// <see cref="NotifyReadingCompleted"/>）で受付開始 T0 を差し込める。
    /// 誤答時の受付再開放と得点・ペナルティ設定は #18 で実装した
    /// （<see cref="Configure"/> の <c>scoring</c> 引数。フル <c>RoomSettings</c> との接続は #26）。
    /// 複数問の進行（出題列の確定・次問への自動進行・全問終了・途中参加の再同期）は #19 で実装し、
    /// GameSession.Session.cs にまとめてある。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(QuestionDistributor))]
    public sealed partial class GameSession : NetworkBehaviour
    {
        /// <summary>ロック保持者が居ないことを表すクライアント ID。</summary>
        public const ulong NoClientId = QuizStateMachine.NoClientId;

        /// <summary>受理する回答の最大文字数（docs/network.md §9）。</summary>
        public const int MaxAnswerLength = QuizStateMachine.MaxAnswerLength;

        private readonly NetworkVariable<QuizPhase> _phase = new NetworkVariable<QuizPhase>(QuizPhase.Lobby);
        private readonly NetworkVariable<int> _questionIndex = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<double> _phaseStartServerTime = new NetworkVariable<double>(0.0);
        private readonly NetworkVariable<double> _buzzOpenServerTime = new NetworkVariable<double>(0.0);
        private readonly NetworkVariable<ulong> _lockedClientId = new NetworkVariable<ulong>(NoClientId);

        /// <summary>
        /// 司会が一時停止中か（サーバー書き込み・クライアント読み取り専用、#20）。
        /// 全クライアントの「一時停止中」表示に使う。
        /// </summary>
        private readonly NetworkVariable<bool> _isPaused = new NetworkVariable<bool>(false);

        private QuizStateMachine _machine;
        private QuestionDistributor _distributor;
        private IQuestionSource _questionSource;
        private IRandom _random;
        private QuizTimeLimits _limits = QuizTimeLimits.Default;
        private QuizRules _rules = QuizRules.Default;
        private NetworkTickSystem _subscribedTickSystem;

        /// <summary>乱数源を自前で生成したか（自前のものだけを破棄する）。</summary>
        private bool _ownsRandom;

        /// <summary>
        /// 外部（TTS 連携 #23 の <see cref="SetBuzzOpenTime"/>）から要求された早押し受付開始 T0。
        /// 未要求なら null。実際の T0 は「配信の受信確認が終わった時刻」とこの値の
        /// **大きい方**（ゲートの最大値、docs/network.md §8.6）にする。
        /// </summary>
        private double? _requestedBuzzOpenTime;

        /// <summary>
        /// この受付で自分（このピア）が既に押下を送ったか。連打で同じ押下を送り続けないためのローカル錠で、
        /// 正しさの判断はサーバー（<see cref="BuzzArbiter"/>）が行う（docs/network.md §6.2）。
        /// </summary>
        private bool _hasBuzzedInCurrentPhase;

        /// <summary>現在のフェーズ（サーバー書き込み・クライアント読み取り専用）。</summary>
        public NetworkVariable<QuizPhase> Phase => _phase;

        /// <summary>現在の問題インデックス。未出題なら -1。</summary>
        public NetworkVariable<int> QuestionIndex => _questionIndex;

        /// <summary>現在のフェーズに入ったサーバー時刻（秒）。</summary>
        public NetworkVariable<double> PhaseStartServerTime => _phaseStartServerTime;

        /// <summary>早押し受付開始時刻 T0（サーバー時刻軸の秒）。受付前は 0（docs/network.md §6.3）。</summary>
        public NetworkVariable<double> BuzzOpenServerTime => _buzzOpenServerTime;

        /// <summary>早押しロック保持者。ロック中でなければ <see cref="NoClientId"/>。</summary>
        public NetworkVariable<ulong> LockedClientId => _lockedClientId;

        /// <summary>司会が一時停止中か（#20）。全クライアントが読める。</summary>
        public NetworkVariable<bool> IsPaused => _isPaused;

        /// <summary>
        /// 問題が提示されたとき（問題インデックス・配信された DTO・発火の経路）。
        /// DTO には正解が含まれない（docs/question-data.md §7）。
        /// </summary>
        /// <remarks>
        /// 第 3 引数（<see cref="QuestionShownSource"/>）は #109 で追加した。
        /// 途中参加・再接続の再同期（<see cref="ResyncClient"/>）でも発火するが、
        /// そのときは「表示を現在問へ合わせる」以上のこと（読み上げの合成・ゲーム開始ジングル）を
        /// してはいけない購読者があるため、経路を区別して渡す。
        /// </remarks>
        public event Action<int, QuestionDto, QuestionShownSource> QuestionShown;

        /// <summary>早押しの勝者が確定したとき（勝者・確定したサーバー時刻・同着抽選だったか）。</summary>
        public event Action<ulong, double, bool> BuzzLocked;

        /// <summary>1 問の結果が出たとき（判定・回答者・正解・回答者の累計得点・その問題での増減）。</summary>
        public event Action<QuizJudgement, ulong, string, int, int> QuestionResolved;

        /// <summary>
        /// 進行ロジックの状態（サーバーのみ）。クライアントでは <see cref="QuizPhase.Lobby"/> を返す。
        /// </summary>
        public QuizPhase ServerPhase => _machine?.Phase ?? QuizPhase.Lobby;

        /// <summary>サーバー側のフェーズ履歴（診断・テスト用）。クライアントでは空。</summary>
        public IReadOnlyList<QuizPhase> ServerPhaseHistory => _machine?.PhaseHistory ?? Array.Empty<QuizPhase>();

        /// <summary>サーバー側の直近の判定結果。クライアントでは <see cref="QuizJudgement.None"/>。</summary>
        public QuizJudgement ServerJudgement => _machine?.LastJudgement ?? QuizJudgement.None;

        /// <summary>
        /// 同じ <c>NetworkObject</c> に載っている問題配信器（#13）。
        /// <see cref="RequireComponent"/> で必ず存在するが、実行時に取り外された場合は null になりうる。
        /// </summary>
        public QuestionDistributor Distributor => _distributor;

        /// <summary>
        /// サーバー側の状態機械が持つ得点を取得する（クライアントでは常に 0）。
        /// クライアント表示用の同期値は <see cref="GetScore"/>（<c>NetworkList</c> 経由）。
        /// </summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <returns>得点。</returns>
        public int GetServerScore(ulong clientId) => _machine?.GetScore(clientId) ?? 0;

        private void Awake()
        {
            _distributor = GetComponent<QuestionDistributor>();
        }

        /// <inheritdoc />
        public override void OnNetworkSpawn()
        {
            // 受付が開き直すたびにローカルの押下錠を外す（ホストもクライアントとして押すため全ピアで購読する）。
            _phase.OnValueChanged += HandlePhaseChanged;
            _scores.OnListChanged += HandleScoreListChanged;
            _questionProgress.OnValueChanged += HandleQuestionProgressChanged;
            _hasBuzzedInCurrentPhase = _phase.Value != QuizPhase.BuzzOpen;

            if (!IsServer)
            {
                // クライアントは進行ロジックを持たない（Configure 済みのインスタンスが
                // クライアントとして再スポーンされた場合も、古い状態を見せない）。
                _machine = null;
                return;
            }

            _machine ??= CreateQuizStateMachine();
            _random ??= CreateDefaultRandom();

            // 司会専用モードの判定に使う LobbyState をこの時点で解決してメモ化する（L-D）。
            TryResolveLobbyState();

            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

            if (_distributor != null)
            {
                _distributor.DistributionCompleted += HandleDistributionCompleted;
            }

            _subscribedTickSystem = NetworkManager.NetworkTickSystem;
            if (_subscribedTickSystem != null)
            {
                // ネットワーク tick ごとに 1 遷移まで進める。フレームレートに依存させないことと、
                // 各フェーズが 1 tick 以上続いて NetworkVariable の同期でクライアントに届くことを狙う。
                _subscribedTickSystem.Tick += HandleServerTick;
            }

            PublishState();
        }

        /// <inheritdoc />
        public override void OnNetworkDespawn()
        {
            _phase.OnValueChanged -= HandlePhaseChanged;
            _scores.OnListChanged -= HandleScoreListChanged;
            _questionProgress.OnValueChanged -= HandleQuestionProgressChanged;

            // 前回の進行の得点を持ち越さない（サーバーのみ書ける。クライアントは再スポーン時に全量が届く）。
            ClearScores();
            ClearQuestionProgress(); // #194

            // 出題列の長さ・全問終了の通知済み・出題失敗の記録も持ち越さない（#19）。
            ResetSessionState();

            // 未消費の再同期の状態（#117）も持ち越さない。再スポーン後は改めて送られてくる。
            ClearPendingResync();

            if (NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            if (_distributor != null)
            {
                _distributor.DistributionCompleted -= HandleDistributionCompleted;
            }

            UnsubscribeTick();
            _rejectLogger = null; // #72: 次のスポーンで作り直す（RpcRateGuard と同じ遅延生成の方針）。
            _rpcRateGuard = null; // #52: 次のスポーンで新しい NetworkManager に対して作り直す。
            _lobbyState = null; // L-D: 次のスポーンで改めて解決する（古い LobbyState への参照を残さない）。

#if UNITY_INCLUDE_TESTS
            // #200（PR #203 レビュー L-4）: テスト用の判定差し替えを次のスポーンへ持ち越さない。
            EligibleBuzzersOverrideForTests = null;
#endif

            // 同じインスタンスが再スポーンされたとき、前回の進行（フェーズ・得点）を持ち越さない。
            // 出題を再開するには Configure → StartQuestion をやり直す（問題の供給元と制限時間は保持する）。
            _machine = null;
        }

        /// <inheritdoc />
        public override void OnDestroy()
        {
            UnsubscribeTick();
            DisposeOwnedRandom();
            base.OnDestroy();
        }

        private void HandleServerTick()
        {
            if (_machine == null || !IsSpawned || !IsServer)
            {
                return;
            }

            QuizEvent quizEvent;
            try
            {
                quizEvent = _machine.Tick(NetworkManager.ServerTime.Time, _random);
            }
            catch (Exception ex)
            {
                // 進行ロジックの不具合で毎 tick 同じ例外を投げ続けないよう、記録して購読を切る。
                // 復帰には Configure → StartQuestion のやり直し（＝新しい進行の開始）が必要。
                Debug.LogException(ex, this);
                UnsubscribeTick();
                return;
            }

            if (quizEvent == QuizEvent.None)
            {
                // 遷移が無い tick でだけ、結果表示からの自動進行と全問終了の通知を見る（#19）。
                // 結果に入った直後の tick（Judged / BuzzTimedOut）より必ず後になるので、
                // 1 問の結果を配る前に次の問題へ進んでしまうことはない。
                TickSession(NetworkManager.ServerTime.Time);
                return;
            }

            PublishState();

            switch (quizEvent)
            {
                case QuizEvent.BuzzResolved:
                    var resolution = _machine.LastResolution;
                    BuzzResultRpc(
                        _machine.LockedClientId,
                        _machine.PhaseStartServerTime,
                        resolution.HasValue && resolution.Value.WasTie);
                    break;

                case QuizEvent.BuzzReopened:
                    // 誤答後の受付再開放（#18、docs/network.md §6.6）。T0 は据え置きのまま。
                    NotifyBuzzReopened();
                    break;

                case QuizEvent.BuzzClosedNoEligibleBuzzers:
                    // 押せる参加者が居なくなったので時間切れを待たずに締めた（#200）。結果の配り方は時間切れと同じ。
                    Debug.Log(
                        $"[GameSession] 押せる参加者が居ないため、時間切れを待たずに早押し受付を締めました（問題 {_machine.QuestionIndex}）。");
                    goto case QuizEvent.BuzzTimedOut;

                case QuizEvent.Judged:
                case QuizEvent.BuzzTimedOut:
                    NotifyScoreChanged();
                    QuestionResultRpc(
                        _machine.LastJudgement,
                        _machine.LockedClientId,
                        ToFixedAnswer(_machine.CorrectAnswer),
                        _machine.GetScore(_machine.LockedClientId),
                        _machine.LastScoreDelta);
                    break;

                case QuizEvent.ChoiceJudged:
                    // 選択式の一斉判定（仮決め: #17）。単独の回答者を前提とする QuestionResultRpc とは
                    // 別に、選択した全クライアント分の正誤・得点をまとめて配る。
                    NotifyChoiceResults();
                    break;
            }
        }

        /// <summary>状態機械の現在値を <c>NetworkVariable</c> へ反映する（サーバーのみ）。</summary>
        private void PublishState()
        {
            if (_machine == null || !IsSpawned || !IsServer)
            {
                return;
            }

            // NetworkVariable の setter は同値なら書き込まないため、毎 tick 呼んでも差分は出ない。
            _phase.Value = _machine.Phase;
            _questionIndex.Value = _machine.QuestionIndex;
            _phaseStartServerTime.Value = _machine.PhaseStartServerTime;
            _buzzOpenServerTime.Value = _machine.BuzzOpenServerTime;
            _lockedClientId.Value = _machine.LockedClientId;
            _isPaused.Value = _machine.IsPaused;

            PublishScores();
            PublishQuestionProgress(); // #194: 参加者パネルの押下順・回答権・回答済み
        }

        /// <summary>
        /// 切断したクライアントの接続に紐づく記録を捨てる（辞書を無制限に伸ばさないため）。
        /// </summary>
        /// <remarks>
        /// #84: 進行の状態（得点・お手つきペナルティ・誤答済み）はここでは捨てない。
        /// 席（名簿エントリ）は保持期間の間そのまま残り、本人がトークンで復帰したら
        /// <see cref="TransferSeat"/> が新しいクライアント ID へ移し替えるため。
        /// ここで捨てると「切断すればお手つきの罰から逃れられる」抜け道になる。
        /// 席そのものが消えたとき（保持期間切れ・ホストの手動削除）は
        /// <see cref="LobbyState"/> が <see cref="ForgetSeat"/> を呼んで捨てる。
        /// </remarks>
        private void HandleClientDisconnected(ulong clientId)
        {
            _rejectLogger?.Forget(clientId); // #72: 棄却ログの間引き記録は切断時に捨てる。
            _rpcRateGuard?.Forget(clientId); // #52: レート制限の状態も切断時に捨てる（docs/network.md §9）。
        }

#if UNITY_INCLUDE_TESTS
        /// <summary>
        /// テスト専用: 通常配信の提示（<see cref="QuestionShownRpc"/>）で
        /// <see cref="QuestionShown"/> を発火させない（#109 レビュー M-2 / M-5）。
        /// 「提示の合図を取りこぼしたクライアント」を決定的に再現するための seam で、
        /// 再同期（<see cref="ResyncClient"/>）由来の発火は抑止しない。
        /// </summary>
        internal bool SuppressDistributedQuestionShownForTests { get; set; }
#endif

        /// <summary>
        /// <see cref="QuestionShown"/> を発火する唯一の入口（#109）。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="question">配信された問題データ（正解は含まない）。</param>
        /// <param name="source">発火の経路（通常の提示か、再同期か）。</param>
        private void RaiseQuestionShown(int questionIndex, QuestionDto question, QuestionShownSource source)
        {
#if UNITY_INCLUDE_TESTS
            if (source == QuestionShownSource.Distribution && SuppressDistributedQuestionShownForTests)
            {
                return;
            }
#endif

            QuestionShown?.Invoke(questionIndex, question, source);
        }

        private void HandlePhaseChanged(QuizPhase previous, QuizPhase next)
        {
            if (next == QuizPhase.BuzzOpen)
            {
                _hasBuzzedInCurrentPhase = false;
            }
        }

        private IRandom CreateDefaultRandom()
        {
            _ownsRandom = true;
            return new CryptoRandom();
        }

        /// <summary>自前で作った乱数源（<see cref="CryptoRandom"/>）を破棄する。</summary>
        private void DisposeOwnedRandom()
        {
            if (!_ownsRandom)
            {
                return;
            }

            (_random as IDisposable)?.Dispose();
            _random = null;
            _ownsRandom = false;
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
    }
}
