using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="LobbyState"/> のサーバー（ホスト）側の処理。
    /// 接続承認（docs/network.md §2.3 の 4〜6）と切断の扱い（§2.4）を
    /// <see cref="LobbyRoster"/> に委譲し、結果を <c>NetworkList</c> に書き出す。
    /// </summary>
    public sealed partial class LobbyState
    {
        /// <summary>ホストのプレイヤー名が未設定だった場合の表示名。</summary>
        public const string FallbackHostName = "ホスト";

        /// <summary>
        /// 承認済みでまだ接続完了していないクライアントを予約として保持する上限時間（秒）。
        /// NGO の <c>NetworkConfig.ClientConnectionBufferTimeout</c>（既定 10 秒）に合わせてあり、
        /// 承認後に握手が失敗したクライアントが席を占め続けないようにする。
        /// </summary>
        private const double PendingApprovalTimeoutSec = 10.0;

        /// <summary>承認からの接続完了待ち（サーバーのみ）。キーはクライアント ID。</summary>
        private readonly Dictionary<ulong, PendingApproval> _pendingApprovals =
            new Dictionary<ulong, PendingApproval>();

        /// <summary><see cref="LobbyRoster.Evaluate"/> へ渡す予約一覧の作業用バッファ（毎回確保しない）。</summary>
        private readonly List<LobbyReservation> _reservationBuffer = new List<LobbyReservation>();

        /// <summary>名簿の権威（サーバーのみ。クライアントでは null）。</summary>
        private LobbyRoster _roster;

        private ConnectionApprovalHandler _approvalHandler;
        private string _hostPlayerName = FallbackHostName;
        private double _nextSweepServerTime;

        /// <summary>
        /// ゲームが進行中（ロビー以外のフェーズ）かを返す関数。
        /// 途中参加の判定（docs/network.md §2.3 の 5）に使う。
        /// null（既定）なら、同じ <see cref="NetworkManager"/> でスポーンされている
        /// <see cref="GameSession"/>（#13）のフェーズを見る（<see cref="IsGameInProgress"/>）。
        /// テストから進行中の状況を作るときだけ差し替える。
        /// </summary>
        public Func<bool> GameInProgressProvider { get; set; }

        /// <summary>名簿の権威（サーバーのみ。診断・テスト用。クライアントでは null）。</summary>
        public LobbyRoster ServerRoster => _roster;

        /// <summary>
        /// サーバー側の配線を行う。<see cref="NetworkBootstrap"/> がスポーン直後に呼ぶ。
        /// </summary>
        /// <param name="approvalHandler">
        /// 接続承認ハンドラ。名簿に基づく追加判定（定員・フェーズ・再接続）を差し込む。
        /// </param>
        /// <param name="hostPlayerName">ホスト自身のプレイヤー名（空なら <see cref="FallbackHostName"/>）。</param>
        public void AttachServer(ConnectionApprovalHandler approvalHandler, string hostPlayerName)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[LobbyState] AttachServer はサーバー（ホスト）でのみ呼べます。");
                return;
            }

            _hostPlayerName = string.IsNullOrEmpty(hostPlayerName) ? FallbackHostName : hostPlayerName;

            if (_approvalHandler != null)
            {
                _approvalHandler.AdmissionEvaluator = null;
            }

            _approvalHandler = approvalHandler;
            if (_approvalHandler != null)
            {
                _approvalHandler.AdmissionEvaluator = EvaluateAdmission;

                // レビュー H-3: ここから先、定員の権威は名簿（LobbyRoster）に一本化する。
                // ホスト開始から本メソッドまでの間は LobbyState がまだ無いので、
                // NetworkService.StartHost に渡した ConnectionApprovalPolicy.MaxPlayers
                // （= LobbyRoster.DefaultMaxPlayers）が粗い保険として効いている。
                // 名簿が立った時点で明示的に権威を譲り、2 か所の上限が食い違わないようにする。
                _approvalHandler.SetPolicy(_approvalHandler.Policy.WithMaxPlayers(null));
            }

            // ホスト自身のエントリを確定させる。ホストの OnClientConnected が本メソッドより
            // 先に走る場合（スポーンをホスト開始後に行うテスト等）もあるため、ここで同期まで済ませる。
            UpsertHostEntry();
            SyncRosterToNetworkList();
        }

        /// <summary>
        /// ルーム設定をロビーへ反映する（ホストのみ）。
        /// #27 以降は <c>RoomSettingsApplier</c>（<c>RoomSettingsSync</c> の変更通知）から呼ばれる。
        /// </summary>
        /// <param name="hostRole">ホストの役割（<c>host.role</c>）。</param>
        /// <param name="maxPlayers">参加人数の上限（<c>room.maxPlayers</c>）。範囲外は丸める。</param>
        /// <param name="allowLateJoin">途中参加を許可するか（<c>network.allowLateJoin</c>）。</param>
        public void ConfigureRoom(HostRole hostRole, int maxPlayers, bool allowLateJoin)
        {
            if (!IsServer || _roster == null)
            {
                // レビュー LOW: AttachServer と同じ文言にすると、どちらで落ちたのか分からなくなる。
                Debug.LogWarning(
                    "[LobbyState] ConfigureRoom はサーバー（ホスト）で、かつ名簿の初期化後にのみ呼べます。");
                return;
            }

            _roster.SetHostRole(hostRole);
            _roster.SetMaxPlayers(maxPlayers);

            _hostRole.Value = hostRole;
            _maxPlayers.Value = _roster.MaxPlayers;
            _allowLateJoin.Value = allowLateJoin;

            UpsertHostEntry();
            SyncRosterToNetworkList();
        }

        /// <summary>途中参加の可否だけを切り替える（ホストのみ。進行中でも変更できる）。</summary>
        public void SetAllowLateJoin(bool allowLateJoin)
        {
            if (!IsServer)
            {
                return;
            }

            _allowLateJoin.Value = allowLateJoin;
        }

        private void InitializeServer()
        {
            _roster = new LobbyRoster(_maxPlayers.Value, _hostRole.Value);

            NetworkManager.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

            _nextSweepServerTime = NetworkManager.ServerTime.Time + SweepIntervalSec;
        }

        private void ShutdownServer()
        {
            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            if (_approvalHandler != null)
            {
                _approvalHandler.AdmissionEvaluator = null;
                _approvalHandler = null;
            }

            _pendingApprovals.Clear();
            _roster?.Clear();
            _roster = null;
            DisposeTokenGenerator();
        }

        /// <summary>
        /// 保持期間を過ぎた切断エントリと、握手が完了しなかった承認予約を掃除する（サーバーのみ）。
        /// </summary>
        private void Update()
        {
            if (!IsSpawned || !IsServer || _roster == null)
            {
                return;
            }

            var now = NetworkManager.ServerTime.Time;
            if (now < _nextSweepServerTime)
            {
                return;
            }

            _nextSweepServerTime = now + SweepIntervalSec;

            PurgeExpiredPendingApprovals(now);

            var removed = _roster.RemoveExpired(now, DisconnectedRetentionSec);
            if (removed.Count > 0)
            {
                Debug.Log($"[LobbyState] 切断から {DisconnectedRetentionSec} 秒が経過したエントリを {removed.Count} 件削除しました。");
                SyncRosterToNetworkList();

                // 席が無くなった＝もう復帰しないので、進行中のペナルティも捨てる（#84）。
                ForgetRemovedSeats(removed);
            }
        }

        /// <summary>
        /// 接続承認の追加判定（<see cref="ConnectionApprovalHandler.AdmissionEvaluator"/> の実装）。
        /// 呼び出し元はペイロードの形式・プレイヤー名の検証を済ませている。
        /// </summary>
        /// <param name="request">
        /// クライアント ID・正規化済みのプレイヤー名・再接続トークン（信用できるのは「形式が正しい」ことだけ）。
        /// </param>
        private LobbyAdmission EvaluateAdmission(LobbyAdmissionRequest request)
        {
            var clientId = request.ClientId;
            if (_roster == null)
            {
                // レビュー H-3: 判定材料が無い状態で素通しすると、定員・フェーズを無視した参加を
                // 許してしまう。fail-closed で拒否し、クライアントには再試行を促す文言を返す。
                Debug.LogWarning(
                    $"[LobbyState] 名簿が未初期化のため接続を拒否しました clientId={clientId}");
                return LobbyAdmission.LobbyNotReady();
            }

            var now = NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0;
            PurgeExpiredPendingApprovals(now);

            var isGameInProgress = IsGameInProgress();
            var admission = _roster.Evaluate(
                request.PlayerName,
                request.ReconnectToken,
                BuildReservations(),
                isGameInProgress,
                _allowLateJoin.Value);

            if (admission.IsApproved)
            {
                // 承認のたびに再接続トークンを発行しておく（#69）。実際に採用されるのは
                // 名簿に新しいエントリを作った場合だけで、再接続では既存のトークンを使い続ける
                // （どちらになったかは接続完了時に名簿の結果を見て判断する）。
                _pendingApprovals[clientId] =
                    new PendingApproval(request.PlayerName, admission, now, IssueSessionToken());
            }

            return admission;
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (_roster == null)
            {
                return;
            }

            if (clientId == NetworkManager.ServerClientId)
            {
                UpsertHostEntry();
                SyncRosterToNetworkList();
                return;
            }

            if (!_pendingApprovals.TryGetValue(clientId, out var pending))
            {
                // 承認を通っていないクライアントが接続完了することは通常ありえない。
                // 名簿に載せずログだけ残す（推測で席を作らない）。
                Debug.LogWarning($"[LobbyState] 承認記録のないクライアントが接続しました clientId={clientId}");
                return;
            }

            _pendingApprovals.Remove(clientId);

            if (!_roster.TryApply(
                    clientId,
                    pending.PlayerName,
                    pending.Admission,
                    SessionTokenHash.Of(pending.IssuedToken),
                    out var applied,
                    out var seatTransfer))
            {
                Debug.LogWarning($"[LobbyState] 名簿への反映に失敗しました clientId={clientId}");
                return;
            }

            // #85: 再接続経路でだけ同名の接続中エントリが並び得る（docs/network.md §2.3 の補足）。
            // 名簿の扱い（本人の席への復帰を名前の一意性より優先する）は変えず、
            // ホストが気づけるようログに残す。画面上の区別は表示側（LobbyView）が連番で行う。
            WarnIfDuplicateConnectedName(applied.Name);

            // 名簿が実際に採用したトークンだけを配る（#69。LobbyState.Token.cs）。
            // 再接続で既存のエントリへ戻った場合は採用されないので送らない
            // （送ってしまうとクライアント側の有効なトークンを無効な値で上書きしてしまう）。
            if (applied.TokenHash.Matches(pending.IssuedToken))
            {
                SendSessionTokenTo(clientId, pending.IssuedToken);
            }

            SyncRosterToNetworkList();

            // #84: 同じ席へ復帰したなら、得点・ペナルティなど clientId をキーに持つ進行中の状態も
            // 新しいクライアント ID へ移し替える（名簿の反映が終わってから通知する）。
            NotifySeatTransferred(seatTransfer);

            // #163: 名簿上まだ接続中だった旧接続から席を引き継いだ場合は、旧接続をこちらから切る
            // （強制終了したクライアントは切断通知を送らないので、放っておくと UTP の DisconnectTimeoutMS まで残る）。
            // 得点などの移し替え（NotifySeatTransferred）の**後**に切る（PR #171 レビュー M-1）。
            // 切断コールバックが同期で走っても、その時点で状態はすでに新しいクライアント ID へ移っている。
            // 旧クライアント ID の切断コールバック（HandleClientDisconnected）は、席が移っているので
            // 名簿に影響しない（TryMarkDisconnected が該当なしで false を返す）。
            DisconnectReplacedClient(pending.Admission, seatTransfer);

            // #109: 進行中のゲームへ合流した（途中参加・再接続）なら、現在の問題とフェーズを送り直す。
            // 席の引き継ぎ（TransferSeat）より後に呼ぶ（送る内容が新しいクライアント ID の状態になるよう）。
            ResyncIfSessionInProgress(clientId);
        }

        /// <summary>
        /// 進行中のゲームへ合流したクライアントへ、現在の問題データ・フェーズを送り直す
        /// （サーバーのみ、#109、docs/network.md §2.4）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>NetworkVariable</c> / <c>NetworkList</c>（フェーズ・問題インデックス・T0・得点表）は
        /// スポーン時に同期されるが、問題データ（DTO）と提示の合図は RPC なので後から参加した
        /// クライアントには届かない。その 2 つだけを <see cref="GameSession.ResyncClient"/> で補う。
        /// </para>
        /// <para>
        /// 呼び出し元をロビーに置くのは #19 からの取り決め（統括判断 2026-09-13）。
        /// 接続の可否（<c>network.allowLateJoin</c>・再接続トークン）は名簿が判断済みで、
        /// ここへ来る時点で「参加を認めたクライアント」に絞られている。
        /// </para>
        /// <para>
        /// NGO のサーバー側 <c>OnClientConnectedCallback</c> は、シーン管理が有効な構成
        /// （<c>NetworkConfig.EnableSceneManagement</c>、本プロジェクトは Boot.unity で有効）では
        /// クライアントの同期完了後に発火するため、この時点で相手側には <see cref="GameSession"/> が
        /// スポーン済みで、RPC が宛先不明にならない。
        /// </para>
        /// <para>
        /// 再同期はサーバー → クライアントの送信なので、RPC レート制限（#52、<c>RpcRateGuard</c>）の
        /// 対象外（あれはクライアント → サーバーの受信側にだけ入れてある）。
        /// </para>
        /// </remarks>
        /// <param name="clientId">合流したクライアント ID（ホスト自身は呼び出し元で除外済み）。</param>
        private void ResyncIfSessionInProgress(ulong clientId)
        {
            var session = FindGameSession();
            if (session == null)
            {
                return;
            }

            if (!QuizPhases.NeedsResync(session.ServerPhase))
            {
                // まだ 1 問も出していない（ロビー）。スポーン時の同期だけで足りる。
                return;
            }

            // 失敗理由（サーバーでない・接続していない・問題を送り直せない）は
            // GameSession.ResyncClient / QuestionDistributor 側が 1 か所でログに残す。
            // ここで重ねて警告すると、同じ事象が 2 行になって原因が追いにくくなる
            // （PR #114 レビュー L-3。握りつぶしではなく「ログを片側へ寄せる」判断）。
            session.ResyncClient(clientId);
        }

        /// <summary>
        /// 席のクライアント ID が付け替わったことを、同じ層の <see cref="GameSession"/> と
        /// 購読者へ伝える（サーバーのみ、#84）。
        /// </summary>
        /// <remarks>
        /// <see cref="GameSession"/> は購読ではなく都度探索して呼ぶ。ホスト開始・停止や
        /// 「もう一度」でスポーン / デスポーンされるため、購読だと古いインスタンスを掴んだり
        /// 購読漏れを起こしたりしやすい（<see cref="IsGameInProgress"/> と同じ方針）。
        /// </remarks>
        /// <param name="transfer">名簿が報告した付け替え。<c>HasValue == false</c> なら何もしない。</param>
        private void NotifySeatTransferred(in SeatTransfer transfer)
        {
            if (!transfer.HasValue)
            {
                if (transfer.PreviousClientId != transfer.ClientId
                    && transfer.SeatId == LobbyPlayer.NoSeatId)
                {
                    // 席へ復帰したのに席 ID が未採番。名簿の採番漏れ（LobbyRoster の不具合）でしか
                    // 起こらないが、黙って通すと得点・ペナルティの引き継ぎが無言で消えるので残す。
                    Debug.LogWarning(
                        "[LobbyState] 席の安定 ID が未採番のため、得点・ペナルティを引き継げませんでした"
                        + $"（{transfer.PreviousClientId} -> {transfer.ClientId}）。");
                }

                return;
            }

            FindGameSession()?.TransferSeat(transfer);
            SeatTransferred?.Invoke(transfer);
        }

        /// <summary>
        /// 名簿から消えた席のぶんだけ、進行中の状態（ペナルティ・誤答済み）を捨てる（サーバーのみ、#84）。
        /// </summary>
        /// <param name="removed">名簿から削除されたエントリ。</param>
        private void ForgetRemovedSeats(IReadOnlyList<LobbyPlayer> removed)
        {
            if (removed == null || removed.Count == 0)
            {
                return;
            }

            var session = FindGameSession();
            if (session == null)
            {
                return;
            }

            for (var i = 0; i < removed.Count; i++)
            {
                session.ForgetSeat(removed[i].ClientId);
            }
        }

        /// <summary>
        /// 引き継ぎ（<see cref="LobbyAdmission.ReplacesConnectedClient"/>、#163）で席を明け渡した旧接続を切断する。
        /// </summary>
        /// <param name="admission">接続完了したクライアントの承認時の判定。</param>
        /// <param name="seatTransfer">名簿への反映結果（旧クライアント ID → 新クライアント ID）。</param>
        private void DisconnectReplacedClient(LobbyAdmission admission, SeatTransfer seatTransfer)
        {
            if (!admission.ReplacesConnectedClient || !seatTransfer.HasValue || NetworkManager == null)
            {
                return;
            }

            var staleClientId = seatTransfer.PreviousClientId;
            if (staleClientId == NetworkManager.ServerClientId)
            {
                // ホスト自身の接続は名簿の判定（TryFindConnectedByNameAndToken）で対象外にしているが、念のため。
                return;
            }

            Debug.Log(
                $"[LobbyState] 再接続トークンが一致したため、切断を検知する前の旧接続から席を引き継ぎました "
                + $"seat={seatTransfer.SeatId} 旧clientId={staleClientId} 新clientId={seatTransfer.ClientId}");

            if (NetworkManager.ConnectedClients.ContainsKey(staleClientId))
            {
                // 旧接続が実は生きていた場合（同じ PC で同じ名前・同じデータルートを 2 つ起動した等）でも、
                // 本人に理由が分かるよう定型文を渡す（PR #171 レビュー L-1）。
                NetworkManager.DisconnectClient(staleClientId, SeatTakenOverDisconnectReason);
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            _pendingApprovals.Remove(clientId);

            if (_roster == null)
            {
                return;
            }

            var now = NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0;
            if (!_roster.TryMarkDisconnected(clientId, now, out _))
            {
                return;
            }

            SyncRosterToNetworkList();
        }

        /// <summary>
        /// ゲームが進行中か（docs/network.md §2.3 の 5）。
        /// <see cref="GameInProgressProvider"/> が差し込まれていればそれを使い、無ければ
        /// 同じ <see cref="NetworkManager"/> でスポーン済みの <see cref="GameSession"/> のフェーズを見る。
        /// <see cref="QuizPhase.Lobby"/>（未開始）と <see cref="QuizPhase.Finished"/>（終了済み）は
        /// 「進行中ではない」として扱い、その間の全フェーズ（Result を含む）を進行中とする
        /// （判定そのものは <see cref="QuizPhases.IsGameInProgress"/>、#109）。
        /// </summary>
        private bool IsGameInProgress()
        {
            if (GameInProgressProvider != null)
            {
                return GameInProgressProvider();
            }

            var session = FindGameSession();
            return session != null && QuizPhases.IsGameInProgress(session.ServerPhase);
        }

        private GameSession FindGameSession()
        {
            if (NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return null;
            }

            foreach (var spawned in NetworkManager.SpawnManager.SpawnedObjectsList)
            {
                if (spawned == null || spawned.NetworkManager != NetworkManager)
                {
                    continue;
                }

                var candidate = spawned.GetComponent<GameSession>();
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// 承認済みで接続完了待ちのクライアントを <see cref="LobbyReservation"/> の一覧にする
        /// （レビュー H-1）。戻り値は次の呼び出しで再利用するので保持しないこと。
        /// </summary>
        private IReadOnlyList<LobbyReservation> BuildReservations()
        {
            _reservationBuffer.Clear();

            foreach (var pending in _pendingApprovals.Values)
            {
                _reservationBuffer.Add(LobbyReservation.FromAdmission(pending.PlayerName, pending.Admission));
            }

            return _reservationBuffer;
        }

        /// <summary>
        /// 切断中のエントリをすべて削除して席を解放する（ホストのみ。レビュー H-2 の緩和策）。
        /// 「同名の別人が席を奪える」弱点（仮決め K-N1）に対し、ホストが怪しいエントリを
        /// 保持期間の満了を待たずに手動で片付けられるようにする。
        /// </summary>
        /// <returns>削除した件数。</returns>
        public int RemoveDisconnectedEntries()
        {
            if (!IsServer || _roster == null)
            {
                Debug.LogWarning("[LobbyState] RemoveDisconnectedEntries はサーバー（ホスト）でのみ呼べます。");
                return 0;
            }

            // 保持期間 0 で掃除すると「切断中のエントリすべて」が対象になる（ホストは除外される）。
            var removed = _roster.RemoveExpired(NetworkManager.ServerTime.Time, retentionSec: 0.0);
            if (removed.Count > 0)
            {
                SyncRosterToNetworkList();

                // 席が無くなった＝もう復帰しないので、進行中のペナルティも捨てる（#84）。
                ForgetRemovedSeats(removed);
            }

            return removed.Count;
        }

        /// <summary>
        /// 同名の接続中エントリが 2 件以上になったらホストのログへ警告を残す（#85）。
        /// 「A が切断 → 別人 B が同じ名前で新規参加 → A がトークンで復帰」という再接続経路でだけ起こる。
        /// </summary>
        /// <param name="playerName">いま名簿へ反映した（正規化済みの）プレイヤー名。</param>
        private void WarnIfDuplicateConnectedName(string playerName)
        {
            if (_roster == null)
            {
                return;
            }

            var connectedCount = _roster.CountConnectedWithName(playerName);
            if (connectedCount < 2)
            {
                return;
            }

            Debug.LogWarning($"[LobbyState] {DuplicateNameNotice.BuildLogLine(playerName, connectedCount)}");
        }

        private void UpsertHostEntry()
        {
            if (_roster == null)
            {
                return;
            }

            _roster.SetHost(NetworkManager.ServerClientId, _hostPlayerName);
        }

        private void PurgeExpiredPendingApprovals(double nowSec)
        {
            if (_pendingApprovals.Count == 0)
            {
                return;
            }

            List<ulong> expired = null;
            foreach (var pair in _pendingApprovals)
            {
                if (nowSec - pair.Value.ApprovedAtSec < PendingApprovalTimeoutSec)
                {
                    continue;
                }

                expired ??= new List<ulong>();
                expired.Add(pair.Key);
            }

            if (expired == null)
            {
                return;
            }

            foreach (var clientId in expired)
            {
                _pendingApprovals.Remove(clientId);
            }
        }

        /// <summary>
        /// 名簿（純 C#）の内容を <c>NetworkList</c> へ写す。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 毎回 <c>Clear()</c> してから全件 <c>Add()</c> し直す。差分だけを書くより通信量は増えるが
        /// （最大 12 件・1KB 未満なので実用上は無視できる）、こうすると送られる差分列が必ず
        /// 「Clear → Add…」で始まるため、**適用結果が受信側の元の内容に依存しない（冪等になる）**。
        /// </para>
        /// <para>
        /// これは NGO の仕様上の落とし穴を避けるためである。接続してくるクライアントには
        /// 「スポーン時の全状態（<c>WriteField</c>）」と「その tick に溜まった差分（<c>WriteDelta</c>）」の
        /// 両方が届きうる。差分が「Add」だけだと、全状態に含まれていた要素がもう一度追加されて
        /// 名簿が重複する（実測: クライアント側だけ件数が増える）。Clear から始めればこれが起きない。
        /// </para>
        /// <para>
        /// 副作用として、1 回の変更で <c>OnListChanged</c> が要素数 + 1 回発火し、
        /// その途中では名簿が空に見える。参加・退出の検出は UI 層が
        /// 「1 フレームにまとめた後の差分」で行う（<c>LobbyView.Roster.cs</c>）。
        /// </para>
        /// </remarks>
        private void SyncRosterToNetworkList()
        {
            if (!IsSpawned || _roster == null)
            {
                return;
            }

            _players.Clear();

            foreach (var player in _roster.Players)
            {
                // レビュー H-2（緩和策）: 切断中のエントリの名前は同期しない。
                // 名前が分かると「同名で再接続」による席の乗っ取り（仮決め K-N1 の弱点）を
                // 狙いやすくなるため、クライアントへは伏せ字だけを配る。
                // ホストの画面は GetPlayersSnapshot() がサーバー側の名簿（実名）から作る。
                _players.Add(player.IsConnected
                    ? PlayerEntry.FromLobbyPlayer(player)
                    : PlayerEntry.FromLobbyPlayer(player, PlayerEntry.DisconnectedNameMask));
            }
        }

        /// <summary>承認済みでまだ接続完了していないクライアントの記録（サーバーのみ）。</summary>
        private readonly struct PendingApproval
        {
            public PendingApproval(
                string playerName,
                LobbyAdmission admission,
                double approvedAtSec,
                SessionToken issuedToken)
            {
                PlayerName = playerName;
                Admission = admission;
                ApprovedAtSec = approvedAtSec;
                IssuedToken = issuedToken;
            }

            /// <summary>正規化済みのプレイヤー名。</summary>
            public string PlayerName { get; }

            /// <summary>承認時の判定結果（新規か再接続か）。</summary>
            public LobbyAdmission Admission { get; }

            /// <summary>承認した時刻（サーバー時刻軸の秒）。</summary>
            public double ApprovedAtSec { get; }

            /// <summary>
            /// 承認時に発行した再接続トークン（#69）。名簿に新しいエントリを作った場合だけ採用され、
            /// 再接続で既存のエントリへ戻った場合は使われない（クライアントへも送らない）。
            /// </summary>
            public SessionToken IssuedToken { get; }
        }
    }
}
