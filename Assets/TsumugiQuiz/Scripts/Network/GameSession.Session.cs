using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Room;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="GameSession"/> のうち、複数問のセッション進行をまとめた部分（#19、
    /// docs/network.md §6.6 の <c>Result --&gt; Idle</c> / <c>Result --&gt; [*]</c>）。
    /// 出題列の確定（フィルタ・シャッフル・出題数）・次問への進行・全問終了の通知を扱う。
    /// 途中参加クライアントへの再同期は GameSession.Resync.cs にある。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 1 問の進行は従来どおり <see cref="QuizStateMachine"/> と <see cref="StartQuestion"/> が担う。
    /// 本ファイルは「何問目を出すか」と「結果表示のあとどうするか」だけを足している。
    /// </para>
    /// <para>
    /// 出題列は <see cref="QuestionSelector"/> が確定させ、サーバー内にのみ置く（docs/network.md §1.2）。
    /// <see cref="QuestionIndex"/> は<b>出題列の中の位置</b>で、元の問題セット上の並びではない。
    /// </para>
    /// </remarks>
    public sealed partial class GameSession
    {
        /// <summary>
        /// 最終結果として配る得点の最大件数（<c>room.maxPlayers</c> の上限 12 ＋ 司会 1 に余裕を見た値）。
        /// 受信側はこの件数を超える配列を不正として捨てる（docs/network.md §9）。
        /// </summary>
        public const int MaxFinalScoreCount = 32;

        /// <summary>出題列の長さ（サーバー書き込み・クライアント読み取り専用）。未開始は 0。</summary>
        private readonly NetworkVariable<int> _totalQuestions = new NetworkVariable<int>(0);

        /// <summary>出題を開始できなかった問題インデックス（同じ問題で何度もログを出さないため）。</summary>
        private readonly HashSet<int> _failedQuestionIndices = new HashSet<int>();

        /// <summary>確定した進行設定。<see cref="StartSession"/> を呼ぶまでは null（単問モード）。</summary>
        private SessionSettings _sessionSettings;

        /// <summary><c>questions.setIds</c> を適用するための問題セット（任意、<see cref="SetQuestionSets"/>）。</summary>
        private IReadOnlyList<QuestionSet> _questionSets;

        /// <summary>選択前の候補プール（<see cref="Configure"/> で渡された供給元）。</summary>
        private IQuestionSource _poolSource;

        /// <summary>直近の <see cref="StartSession"/> で作った出題列の供給元。</summary>
        private IQuestionSource _orderedSource;

        /// <summary>全問終了をまだ通知していないか（同じセッションで 2 回配らないため）。</summary>
        private bool _sessionFinishedNotified;

        /// <summary>
        /// 自動進行を打ち切ったか。次問の出題もセッション終了もできなかった場合に立て、
        /// 毎 tick 同じ失敗を繰り返さないようにする（復帰には <see cref="StartSession"/> が必要）。
        /// </summary>
        private bool _autoAdvanceStopped;

        /// <summary>
        /// 出題列の長さ（<c>questions.count</c> 適用後）。セッション未開始なら 0。
        /// クライアントは「あと何問か」の表示に使う。
        /// </summary>
        public NetworkVariable<int> TotalQuestions => _totalQuestions;

        /// <summary>確定した進行設定（サーバーのみ）。<see cref="StartSession"/> 前は null。</summary>
        public SessionSettings ActiveSessionSettings => _sessionSettings;

        /// <summary>
        /// ゲーム進行中の途中参加を許可するか（<c>network.allowLateJoin</c>）。
        /// 接続の可否判断はロビー（#7）が行い、本クラスは <see cref="ResyncClient"/> の口だけを提供する。
        /// </summary>
        public bool AllowLateJoin => _sessionSettings?.AllowLateJoin ?? SessionSettings.DefaultAllowLateJoin;

        /// <summary>全問終了したとき（最終得点表）。全ピアで発火する。</summary>
        public event Action<IReadOnlyList<ScoreEntry>> SessionFinished;

        /// <summary>
        /// 結果表示から全員がロビーへ戻ったとき（<see cref="ReturnToLobby"/>）。全ピアで発火する
        /// （Result View の「ロビーへ戻る」、#20、docs/architecture.md §2 の Result → Lobby）。
        /// </summary>
        public event Action ReturnedToLobby;

        /// <summary>
        /// <c>questions.setIds</c> を適用するための問題セットを設定する（サーバーのみ、任意）。
        /// 設定しない場合、出題列は <see cref="Configure"/> で渡した供給元を候補プールとして作る
        /// （その場合 <c>setIds</c> は適用できない）。
        /// </summary>
        /// <param name="sets">読み込み済みの問題セット。null なら指定を解除する。</param>
        public void SetQuestionSets(IReadOnlyList<QuestionSet> sets)
        {
            if (IsSpawned && !IsServer)
            {
                throw new InvalidOperationException("GameSession.SetQuestionSets はサーバーでのみ呼べます。");
            }

            _questionSets = sets;
        }

        /// <summary>
        /// 出題列を確定してセッションを開始する（サーバーのみ）。
        /// フィルタ・シャッフル・出題数を適用し、1 問目の出題まで進める。
        /// </summary>
        /// <remarks>
        /// 進行中（Lobby / Finished 以外）は受け付けない。1 問目の出題に失敗した場合は
        /// 何も始めなかった状態（Lobby・出題列なし）へ戻す。
        /// </remarks>
        /// <param name="settings">
        /// 進行設定。null のときは
        /// 「<see cref="RoomSettingsSync.Current"/>（ロビーで確定したルーム設定） → 前回の進行設定 →
        /// <see cref="SessionSettings.Default"/>」の順に解決する
        /// （<see cref="RoomSettingsSync"/> が無い構成では前回の進行設定 → 既定値、#27、docs/network.md §12.2）。
        /// 制限時間・得点設定はここで確定し、<see cref="Configure"/> で渡した値を上書きする。
        /// </param>
        /// <param name="shuffleSeed">
        /// 出題順シャッフルのシード。null なら乱数源から作る（テストでは固定値を渡して再現できる）。
        /// </param>
        /// <returns>セッションを開始できたら true。</returns>
        public bool StartSession(SessionSettings settings = null, int? shuffleSeed = null)
        {
            if (!IsSpawned || !IsServer)
            {
                Debug.LogWarning("[GameSession] セッションの開始はサーバーでのみ行えます。");
                return false;
            }

            if (_questionSource == null)
            {
                Debug.LogWarning("[GameSession] Configure が済んでいないためセッションを開始できません。");
                return false;
            }

            // 進行中のセッションを壊さない（状態機械を作り直す前に判定する）。
            if (_machine != null && _machine.Phase != QuizPhase.Lobby && _machine.Phase != QuizPhase.Finished)
            {
                Debug.LogWarning(
                    $"[GameSession] 進行中はセッションを開始できません（フェーズ: {_machine.Phase}）。");
                return false;
            }

            // 直前のセッションで差し替えた出題列ではなく、Configure で渡された候補プールを使う。
            if (!ReferenceEquals(_questionSource, _orderedSource))
            {
                _poolSource = _questionSource;
            }

            // 明示指定 > ロビーで確定したルーム設定（#27）> 前回の設定 > 既定値 の順で使う。
            // ルーム設定を前回の設定より優先するのは、ロビーで変更した値が 2 回目以降の開始で
            // 無視されないようにするため（RoomSettingsSync が無い構成では従来どおり前回の設定を使う）。
            var sync = SettingsSync;
            var effective = settings
                ?? (sync != null ? sync.Current.ToSessionSettings() : null)
                ?? _sessionSettings
                ?? SessionSettings.Default;
            var selection = BuildSelection(effective.Questions, shuffleSeed);
            if (selection.IsEmpty)
            {
                Debug.LogWarning(
                    "[GameSession] 問題選択の条件に合う問題が 1 件もないためセッションを開始できません"
                    + $"（候補プール {selection.PoolCount} 件）。");
                return false;
            }

            // 前のセッションの配信（Ack 待ち・送信予定）を残さない。
            _distributor?.AbortPendingDistribution();

            _sessionSettings = effective;
            _limits = effective.TimeLimits;
            _scoringSettings = effective.Scoring;
            _progressSettingsChangedSinceCommit = true;
            _rules = _scoringSettings.ToQuizRules();

            _machine = CreateQuizStateMachine();
            ClearScores();
            ResetSessionProgress();

            var ordered = new RepositoryQuestionSource(selection.Questions);
            _orderedSource = ordered;
            _questionSource = ordered;
            _distributor?.SetQuestionSource(ordered);
            _distributor?.ResetCache();

            if (!_machine.StartSession(selection.Count, NetworkManager.ServerTime.Time, out var reason))
            {
                Debug.LogWarning($"[GameSession] セッションを開始できません（理由: {reason}、フェーズ: {_machine.Phase}）。");
                RollbackSession();
                return false;
            }

            if (!StartQuestion(0))
            {
                // 1 問目が出せないセッションは始めない（中途半端な出題列を同期しない）。
                Debug.LogError("[GameSession] 1 問目を出題できなかったためセッションを開始しませんでした。");
                RollbackSession();
                return false;
            }

            _totalQuestions.Value = selection.Count;
            PublishState();
            return true;
        }

        /// <summary>
        /// 結果表示を終えて次の問題へ進む（サーバーのみ、Result フェーズでのみ受理）。
        /// 出題できない問題は読み飛ばし、残りが全部出題できなければセッションを終了する。
        /// </summary>
        /// <remarks>
        /// 司会専用モードの「次へ」（#20）と、<c>result.autoAdvanceSec</c> による自動進行の
        /// 共通の入口。自動進行が 0（手動）の場合はホストがこれを呼ぶまで Result に留まる。
        /// 出題に失敗した問題インデックスは記録し、同じ問題で何度もエラーログを出さない。
        /// </remarks>
        /// <returns>次問の出題またはセッション終了に進めたら true。</returns>
        public bool NextQuestion()
        {
            if (!IsSpawned || !IsServer || _machine == null)
            {
                Debug.LogWarning("[GameSession] 次の問題へ進めるのはサーバーだけです。");
                return false;
            }

            if (_machine.IsPaused)
            {
                // 司会が一時停止中は「次へ」を受け付けない（#20）。
                Debug.LogWarning("[GameSession] 一時停止中は次の問題へ進めません。");
                return false;
            }

            if (_machine.Phase != QuizPhase.Result)
            {
                Debug.LogWarning($"[GameSession] 結果表示中ではないため次へ進めません（フェーズ: {_machine.Phase}）。");
                return false;
            }

            // 出題できない問題（配信データが壊れている等）は飛ばして次へ進む。
            // ここで諦めると Result のまま止まり、自動進行が毎 tick 同じ失敗を繰り返してしまう。
            for (var next = _machine.NextQuestionIndex; next >= 0 && next < _machine.TotalQuestions; next++)
            {
                if (StartQuestion(next))
                {
                    return true;
                }

                if (_failedQuestionIndices.Add(next))
                {
                    Debug.LogError($"[GameSession] 問題 {next} を出題できなかったため読み飛ばします。");
                }
            }

            if (!FinishSession())
            {
                return false;
            }

            PublishSessionFinished();
            return true;
        }

        /// <summary>
        /// 結果表示から全員をロビーへ戻す（サーバーのみ、Result / Finished 中に受理、#20）。
        /// </summary>
        /// <remarks>
        /// ロビーへ戻ると、ホストはふたたび設定を変更できる必要があるため、
        /// ルーム設定のロックを解除し（<see cref="RoomSettingsSync.Unlock"/>、#27）、
        /// 進行設定（<see cref="ActiveSessionSettings"/>）も捨てる。
        /// 次に <see cref="StartSession"/> を引数なしで呼ぶと
        /// <see cref="RoomSettingsSync.Current"/>（＝ 直前のゲームで確定した値。ロビーで変更していれば
        /// その新しい値）が使われるので、設定を変えなければ従来どおり「もう一度（同じ設定で）」になる。
        /// 結果画面の「もう一度」ボタン（<c>ResultView.OnRestartClicked</c>）はロビーへ戻らずに
        /// <see cref="StartSession"/> を呼ぶ経路なので、こちらの影響を受けない。
        /// </remarks>
        /// <returns>ロビーへ戻せたら true。</returns>
        public bool ReturnToLobby()
        {
            if (!IsSpawned || !IsServer)
            {
                Debug.LogWarning("[GameSession] ロビーへ戻る操作はサーバーでのみ行えます。");
                return false;
            }

            if (_machine == null || (_machine.Phase != QuizPhase.Result && _machine.Phase != QuizPhase.Finished))
            {
                Debug.LogWarning(
                    $"[GameSession] 結果表示中でなければロビーへ戻せません（フェーズ: {_machine?.Phase}）。");
                return false;
            }

            _distributor?.AbortPendingDistribution();
            _machine = CreateQuizStateMachine();
            ClearScores();
            ResetSessionState();

            // #27: ロビーではホストがふたたび設定を変更できる（docs/room-settings.md §4）。
            UnlockRoomSettingsForLobby();

            PublishState();

            ReturnToLobbyRpc();
            return true;
        }

        /// <summary>
        /// 結果表示から全員をロビーへ戻す合図（サーバー → 全員、#20）。
        /// </summary>
        /// <remarks>
        /// #117: 未消費の再同期の状態（<see cref="TryConsumePendingResync"/>）はここで捨てる。
        /// 合流したクライアントが結果画面・Game View へ移る前にロビーへ戻された場合、
        /// 残したままにすると次にロビーへ入り直したときに古い状態で遷移してしまう。
        /// 同じ <c>NetworkObject</c> の信頼性のある RPC は送信順に届くので、
        /// 先に送られた <c>SessionStateRpc</c> はこの時点で必ず処理済み。
        /// </remarks>
        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void ReturnToLobbyRpc()
        {
            ClearPendingResync();
            ReturnedToLobby?.Invoke();
        }

        /// <summary>
        /// 結果表示からの自動進行と全問終了の通知（サーバーのみ、ネットワーク tick ごと）。
        /// </summary>
        /// <param name="serverNow">現在のサーバー時刻（秒）。</param>
        private void TickSession(double serverNow)
        {
            if (_machine == null || !IsSpawned || !IsServer || _machine.IsPaused)
            {
                // 一時停止中（#20）は自動進行のタイマーも進めない。
                return;
            }

            if (_machine.Phase == QuizPhase.Finished)
            {
                // ホストが FinishSession を直接呼んだ場合も、ここで一度だけ最終得点を配る。
                PublishSessionFinished();
                return;
            }

            if (_sessionSettings == null || _machine.TotalQuestions <= QuizStateMachine.NoSession)
            {
                // 単問モード（#12 / #18 の経路）は自動で進めない。
                return;
            }

            if (_autoAdvanceStopped || _machine.Phase != QuizPhase.Result
                || !_sessionSettings.AutoAdvancesAfterResult)
            {
                return;
            }

            if (serverNow - _machine.PhaseStartServerTime < _sessionSettings.ResultAutoAdvanceSec)
            {
                return;
            }

            if (NextQuestion())
            {
                return;
            }

            // 次問もセッション終了もできなかった。毎 tick の再試行は害しかないので打ち切る。
            _autoAdvanceStopped = true;
            Debug.LogError(
                "[GameSession] 結果表示から先へ進めなかったため自動進行を打ち切りました"
                + "（司会の「次へ」または新しいセッションの開始が必要です）。");
        }

        /// <summary>全問終了時の最終得点を 1 度だけ全員へ配る（サーバーのみ）。</summary>
        private void PublishSessionFinished()
        {
            if (_sessionFinishedNotified || _machine == null || _machine.Phase != QuizPhase.Finished)
            {
                return;
            }

            _sessionFinishedNotified = true;

            var board = _machine.FinalScores ?? _machine.Scores;
            var clientIds = board.ClientIds;
            var count = Math.Min(clientIds.Count, MaxFinalScoreCount);
            if (count < clientIds.Count)
            {
                Debug.LogWarning(
                    $"[GameSession] 最終得点が {clientIds.Count} 件あるため先頭 {count} 件だけを配信します。");
            }

            var ids = new ulong[count];
            var scores = new int[count];
            for (var i = 0; i < count; i++)
            {
                ids[i] = clientIds[i];
                scores[i] = board.GetScore(clientIds[i]);
            }

            SessionFinishedRpc(ids, scores);
        }

        /// <summary>
        /// セッションの進行状態（終了通知の済み・自動進行の打ち切り・出題失敗の記録）を初期化する。
        /// </summary>
        private void ResetSessionProgress()
        {
            _sessionFinishedNotified = false;
            _autoAdvanceStopped = false;
            _failedQuestionIndices.Clear();

            // #27: 同期できない制限時間の警告は 1 セッションにつき 1 回だけにする。
            _warnedLimitsNotSynced = false;
        }

        /// <summary>
        /// セッションを「開始していない」状態に戻す（<see cref="Configure"/>・デスポーン・開始失敗時）。
        /// 出題列の長さはサーバーだけが書けるので、クライアント側では触らない。
        /// </summary>
        private void ResetSessionState()
        {
            ResetSessionProgress();

            // #27: 前回の進行設定を持ち越さない（次の開始ではロビーのルーム設定を使う）。
            _sessionSettings = null;

            if (NetworkManager != null && !IsServer)
            {
                return;
            }

            if (_totalQuestions.Value != 0)
            {
                _totalQuestions.Value = 0;
            }
        }

        /// <summary>1 問目を出題できなかったときに、セッション開始前の状態へ戻す（サーバーのみ）。</summary>
        private void RollbackSession()
        {
            _distributor?.AbortPendingDistribution();
            _machine = CreateQuizStateMachine();
            ClearScores();
            ResetSessionState();
            PublishState();
        }

        /// <summary>候補プールから出題列を確定させる。</summary>
        private QuestionSelection BuildSelection(QuestionSelectionSettings questions, int? shuffleSeed)
        {
            var seed = shuffleSeed ?? NextShuffleSeed();

            QuestionSelection selection;
            if (_questionSets != null && _questionSets.Count > 0)
            {
                selection = QuestionSelector.SelectFromSets(_questionSets, questions, seed);
            }
            else
            {
                if (questions.SetIds.Count > 0)
                {
                    Debug.LogWarning(
                        "[GameSession] questions.setIds が指定されていますが、問題セットが渡されていないため適用できません"
                        + "（SetQuestionSets を呼ぶか、供給元の時点で絞り込んでください）。");
                }

                selection = QuestionSelector.Select(CollectPoolQuestions(), questions, seed);
            }

            if (selection.UnknownSetIds.Count > 0)
            {
                // 存在しない setId は無視して警告する（docs/room-settings.md §5）。
                Debug.LogWarning(
                    "[GameSession] 読み込み済みのどのセットにも一致しない setId を無視しました: "
                    + string.Join(", ", selection.UnknownSetIds));
            }

            return selection;
        }

        /// <summary>指定クライアントが現在接続しているか（最大 12 人なので線形探索で足りる）。</summary>
        private bool IsClientConnected(ulong clientId)
        {
            var connected = NetworkManager.ConnectedClientsIds;
            for (var i = 0; i < connected.Count; i++)
            {
                if (connected[i] == clientId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>候補プール（<see cref="Configure"/> で渡された供給元）の中身を取り出す。</summary>
        private List<Question> CollectPoolQuestions()
        {
            var pool = new List<Question>();
            var source = _poolSource;
            if (source == null)
            {
                return pool;
            }

            for (var i = 0; i < source.Count; i++)
            {
                if (source.TryGetQuestion(i, out var question) && question != null)
                {
                    pool.Add(question);
                }
            }

            return pool;
        }

        /// <summary>シャッフル用のシードを作る（乱数源はテストから差し替えられる）。</summary>
        private int NextShuffleSeed() =>
            _random != null ? _random.NextInt(int.MaxValue) : Environment.TickCount;

        /// <summary>
        /// 全問終了と最終得点（サーバー → 全員）。累計は得点表（<c>NetworkList</c>）でも同期しているが、
        /// 「このセッションの最終結果が確定した」ことを 1 度だけ伝えるために配る。
        /// </summary>
        /// <param name="clientIds">得点表に載っているクライアント ID（昇順）。</param>
        /// <param name="scores"><paramref name="clientIds"/> と同じ並びの累計得点。</param>
        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void SessionFinishedRpc(ulong[] clientIds, int[] scores)
        {
            // サーバー発でも受信データは検証する（docs/network.md §9）。
            if (clientIds == null || scores == null || clientIds.Length != scores.Length)
            {
                Debug.LogWarning("[GameSession] 最終得点の配信が壊れていたため破棄しました。");
                return;
            }

            if (clientIds.Length > MaxFinalScoreCount)
            {
                Debug.LogWarning($"[GameSession] 最終得点の件数が上限を超えていたため破棄しました（{clientIds.Length} 件）。");
                return;
            }

            var entries = new List<ScoreEntry>(clientIds.Length);
            for (var i = 0; i < clientIds.Length; i++)
            {
                entries.Add(new ScoreEntry(clientIds[i], scores[i]));
            }

            SessionFinished?.Invoke(entries);
        }
    }
}
