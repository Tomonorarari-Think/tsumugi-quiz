using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// ロビーの名簿（純 C#）。サーバー（ホスト）だけが保持・変更し、
    /// <c>TsumugiQuiz.Network.LobbyState</c> が <c>NetworkList&lt;PlayerEntry&gt;</c> へ写して同期する。
    /// Unity / NGO に依存しないので EditMode でそのままテストできる（docs/network.md §10.1）。
    ///
    /// 再接続の突き合わせは「プレイヤー名の一致 + ホストが発行した再接続トークンの一致」で行う
    /// （#69。名前だけで判定していた当初の方式では、満室・進行中の部屋でも
    /// 「落ちた人の名前を騙る」だけで席と得点を奪えてしまう）。名簿が持つのはトークンの
    /// SHA-256 ハッシュだけで、トークン本体はクライアントにしか残らない。
    ///
    /// 扱う責務は「定員」「途中参加の可否」「切断の保持と再接続の突き合わせ」だけで、
    /// スコアや進行フェーズは持たない（スコアは #18、フェーズは <c>QuizStateMachine</c> が持つ）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判定順序は docs/network.md §2.3 の 4〜6 に対して意図的に入れ替えてある。
    /// ドキュメントの列挙順は「人数上限 → フェーズ → 再接続」だが、その順で評価すると
    /// 「満室の部屋から落ちたプレイヤー」「ゲーム進行中に落ちたプレイヤー」が復帰できない。
    /// 再接続は本人の席へ戻る操作であって新規の席を要求しないので、
    /// <see cref="Evaluate"/> では再接続を最初に判定する（この差異は docs/network.md §2.3 に明記した）。
    /// </para>
    /// <para>
    /// 定員には「接続中」のエントリと「保持期間中の切断エントリ」の両方を数える
    /// （統括判断 #7 Q3）。docs/network.md §2.3 の 4 は <c>ConnectedClientsIds.Count</c> を
    /// 基準に書いてあるが、それだと満室の部屋で誰かが落ちた直後に別の人が入ってしまい、
    /// 落ちた人が復帰できない。切断エントリが席を押さえることで、保持期間
    /// （<see cref="DefaultDisconnectedRetentionSec"/>）の間は必ず復帰できる。
    /// 保持期間を過ぎたエントリは <see cref="RemoveExpired"/> で消え、そこで席が空く。
    /// </para>
    /// </remarks>
    public sealed class LobbyRoster
    {
        /// <summary>参加人数上限の既定値（docs/room-settings.md §1 の <c>room.maxPlayers</c>）。</summary>
        public const int DefaultMaxPlayers = 6;

        /// <summary>最初に割り当てる席の安定 ID（#84）。</summary>
        public const int FirstSeatId = 1;

        /// <summary>参加人数上限の下限（docs/room-settings.md §1）。</summary>
        public const int MinMaxPlayers = 2;

        /// <summary>参加人数上限の上限（docs/room-settings.md §1）。</summary>
        public const int MaxMaxPlayers = 12;

        /// <summary>
        /// 切断中のエントリを保持する時間（秒）。これを超えたら名簿から削除する。
        /// TODO(#26): ルーム設定として外に出す。本 issue では定数（60 秒）とする。
        /// </summary>
        public const double DefaultDisconnectedRetentionSec = 60.0;

        private readonly List<LobbyPlayer> _players = new List<LobbyPlayer>();

        /// <summary>
        /// 次に割り当てる席の安定 ID（#84）。1 から通し番号で振り、
        /// 再接続では既存のエントリの値を引き継ぐので増えない。
        /// </summary>
        private int _nextSeatId = FirstSeatId;

        /// <summary>
        /// 名簿を作る。
        /// </summary>
        /// <param name="maxPlayers">参加人数の上限。範囲外は <see cref="MinMaxPlayers"/>〜<see cref="MaxMaxPlayers"/> に丸める。</param>
        /// <param name="hostRole">ホストの役割。</param>
        public LobbyRoster(int maxPlayers = DefaultMaxPlayers, HostRole hostRole = HostRole.Player)
        {
            MaxPlayers = ClampMaxPlayers(maxPlayers);
            HostRole = hostRole;
        }

        /// <summary>参加人数の上限（丸め済み）。</summary>
        public int MaxPlayers { get; private set; }

        /// <summary>ホストの役割。</summary>
        public HostRole HostRole { get; private set; }

        /// <summary>名簿の内容（追加順）。読み取り専用。</summary>
        public IReadOnlyList<LobbyPlayer> Players => _players;

        /// <summary>
        /// 定員（<see cref="MaxPlayers"/>）に数えるエントリ数。
        /// 接続中のエントリと、保持期間中の切断エントリ（復帰用に席を押さえている）の両方を数える
        /// （統括判断 #7 Q3）。司会専任のホストは数えない（仮決め K18）。
        /// </summary>
        public int OccupiedSlotCount
        {
            get
            {
                var count = 0;
                foreach (var player in _players)
                {
                    if (player.OccupiesPlayerSlot)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>接続中のエントリ数（司会専任のホストも含む。画面表示用）。</summary>
        public int ConnectedCount
        {
            get
            {
                var count = 0;
                foreach (var player in _players)
                {
                    if (player.IsConnected)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>参加人数の上限を範囲内に丸める（docs/room-settings.md §5）。</summary>
        public static int ClampMaxPlayers(int maxPlayers)
        {
            if (maxPlayers < MinMaxPlayers)
            {
                return MinMaxPlayers;
            }

            return maxPlayers > MaxMaxPlayers ? MaxMaxPlayers : maxPlayers;
        }

        /// <summary>参加人数の上限を差し替える（丸めたうえで反映する）。</summary>
        public void SetMaxPlayers(int maxPlayers) => MaxPlayers = ClampMaxPlayers(maxPlayers);

        /// <summary>
        /// ホストの役割を差し替える。既にホストのエントリがあれば
        /// <see cref="LobbyPlayer.IsModerator"/> も揃える。
        /// </summary>
        public void SetHostRole(HostRole hostRole)
        {
            HostRole = hostRole;

            for (var i = 0; i < _players.Count; i++)
            {
                if (_players[i].IsHost)
                {
                    _players[i] = _players[i].WithModerator(hostRole == HostRole.Moderator);
                }
            }
        }

        /// <summary>
        /// ホスト自身のエントリを登録（既にあれば更新）する。定員・フェーズの判定は行わない
        /// （NGO はホスト自身の接続を拒否できないため）。
        /// </summary>
        /// <param name="clientId">ホストのクライアント ID（通常は 0）。</param>
        /// <param name="playerName">正規化済みのホストのプレイヤー名。</param>
        /// <returns>登録後のエントリ。</returns>
        /// <exception cref="ArgumentException"><paramref name="playerName"/> が空。</exception>
        public LobbyPlayer SetHost(ulong clientId, string playerName)
        {
            if (string.IsNullOrEmpty(playerName))
            {
                throw new ArgumentException("ホストのプレイヤー名が空です。", nameof(playerName));
            }

            var isModerator = HostRole == HostRole.Moderator;

            for (var i = 0; i < _players.Count; i++)
            {
                if (!_players[i].IsHost)
                {
                    continue;
                }

                // 既にホストのエントリがあるなら席の安定 ID は変えない（#84。ホストは切断しない想定だが、
                // 設定変更のたびに ID が変わると得点行との対応が崩れるため）。
                var updated = new LobbyPlayer(
                    clientId,
                    playerName,
                    true,
                    isModerator,
                    true,
                    LobbyPlayer.NotDisconnected,
                    _players[i].TokenHash,
                    _players[i].SeatId);
                _players[i] = updated;
                return updated;
            }

            var entry = new LobbyPlayer(
                clientId,
                playerName,
                true,
                isModerator,
                true,
                LobbyPlayer.NotDisconnected,
                default,
                AllocateSeatId());
            _players.Add(entry);
            return entry;
        }

        /// <summary>
        /// 入室を認めるかどうかを判定する（名簿は変更しない）。
        /// 接続承認コールバック（<c>ConnectionApprovalHandler</c>）から呼ぶ。
        /// </summary>
        /// <param name="playerName">正規化済みのプレイヤー名。</param>
        /// <param name="reconnectToken">
        /// クライアントが承認ペイロードに載せてきた再接続トークン（#69）。
        /// 切断中の同名エントリに発行済みのトークンと一致したときだけ復帰させる。
        /// <see cref="SessionToken.None"/>（未所持）なら常に新規参加として扱う。
        /// </param>
        /// <param name="reservations">
        /// 承認済みでまだ接続完了していないクライアントの予約一覧（<see cref="LobbyReservation"/>）。
        /// 承認は 1 件ずつ処理されるのに対し名簿への反映は接続完了時なので、その隙間に
        /// 「定員超過」「同名の二重参加」「同じ席への二重再接続」がすり抜けるのを防ぐ
        /// （レビュー H-1）。null / 空なら予約なしとして扱う。
        /// </param>
        /// <param name="isGameInProgress">ゲームが進行中（ロビー以外のフェーズ）か。</param>
        /// <param name="allowLateJoin">途中参加を許可しているか（<c>network.allowLateJoin</c>）。</param>
        /// <returns>判定結果。</returns>
        public LobbyAdmission Evaluate(
            string playerName,
            SessionToken reconnectToken,
            IReadOnlyList<LobbyReservation> reservations,
            bool isGameInProgress,
            bool allowLateJoin)
        {
            if (string.IsNullOrEmpty(playerName))
            {
                // 名前の検証は ConnectionApprovalEvaluator が先に済ませている。
                // ここへ空文字が来るのは想定外なので、理由もそのまま「名前が不正」とする（レビュー M-9）。
                return LobbyAdmission.InvalidPlayerName();
            }

            // 1. 承認済みで接続完了待ちのクライアントとの名前重複（レビュー H-1）。
            //    先に見るのは、同名の参加・再接続要求が続けて届いたときに 2 件とも承認しないため。
            if (IsNameReserved(reservations, playerName))
            {
                return LobbyAdmission.DuplicateName();
            }

            // 2. 再接続（切断中の同名エントリ + トークン一致）。定員・フェーズより先に見る（remarks 参照）。
            //    トークンが無い / 一致しない場合はここを通さず、以降を新規参加として評価する（#69）。
            //    名前だけで復帰できると、満室・進行中の部屋でも「落ちた人の名前を騙る」だけで
            //    席と得点を奪えてしまうため（K-N1 の弱点）。
            if (TryFindDisconnectedByNameAndToken(playerName, reconnectToken, out var disconnected))
            {
                // 同じ席（PreviousClientId）を狙う予約が既にあるなら二重再接続なので拒否する。
                // 1 の名前一致でほぼ捕まえられるが、名簿の名前と承認ペイロードの名前がずれた場合の保険。
                return IsSeatReserved(reservations, disconnected.ClientId)
                    ? LobbyAdmission.DuplicateName()
                    : LobbyAdmission.Reconnect(disconnected.ClientId);
            }

            // 2b. 引き継ぎ（接続中の同名エントリ + トークン一致、#163）。
            //     クライアントが強制終了した直後は、ホストが切断を検知する（UTP の DisconnectTimeoutMS、
            //     Boot.unity では 30 秒）まで旧エントリが「接続中」のまま残る。その間に本人が再起動して
            //     戻ってきても 3 の名前重複で拒否されないよう、トークンが一致すれば旧接続を切って席を引き継ぐ。
            //     トークンは本人しか持たないので、名前だけで席を奪える K-N1 の弱点は生じない。
            if (TryFindConnectedByNameAndToken(playerName, reconnectToken, out var stale))
            {
                return IsSeatReserved(reservations, stale.ClientId)
                    ? LobbyAdmission.DuplicateName()
                    : LobbyAdmission.Takeover(stale.ClientId);
            }

            // 3. 接続中のプレイヤーとの名前重複。
            //    トークン方式（#69）でも、一覧に同名が並ぶと誰が誰だか分からなくなるため
            //    同名の同時接続は引き続き拒否する（統括判断 #7 Q2）。
            if (IsNameTakenByConnectedPlayer(playerName))
            {
                return LobbyAdmission.DuplicateName();
            }

            // 4. フェーズ（途中参加）。
            if (isGameInProgress && !allowLateJoin)
            {
                return LobbyAdmission.GameInProgress();
            }

            // 5. 定員。切断中のエントリも保持期間の間は席を押さえている（統括判断 #7 Q3）。
            //    予約のうち数えるのは新規参加分だけ（再接続は既存の席へ戻るだけ。レビュー M-1 / M-2）。
            if (OccupiedSlotCount + CountNewSeatReservations(reservations) >= MaxPlayers)
            {
                // 空きが「切断者の復帰用に確保された席」だけなら、単なる満室と区別して伝える（レビュー M-3）。
                return HasRetainedDisconnectedSeat() ? LobbyAdmission.SeatReserved() : LobbyAdmission.RoomFull();
            }

            return LobbyAdmission.NewPlayer();
        }

        /// <summary>
        /// 判定結果を名簿に反映する（接続完了時に呼ぶ）。
        /// </summary>
        /// <param name="clientId">接続したクライアント ID。</param>
        /// <param name="playerName">正規化済みのプレイヤー名。</param>
        /// <param name="admission">
        /// 承認時の判定結果。再接続なら該当エントリを復帰させ、新規なら追加する。
        /// 拒否の判定結果を渡した場合は何もせず false を返す。
        /// </param>
        /// <param name="issuedTokenHash">
        /// 新規参加のときにこのクライアントへ発行したトークンのハッシュ（#69）。
        /// 再接続のときは無視し、エントリが持っているハッシュをそのまま使う
        /// （トークンはホストのプロセス寿命の間ずっと有効なので入れ替えない）。
        /// </param>
        /// <param name="player">反映後のエントリ。</param>
        /// <returns>名簿を更新したか。</returns>
        public bool TryApply(
            ulong clientId,
            string playerName,
            LobbyAdmission admission,
            SessionTokenHash issuedTokenHash,
            out LobbyPlayer player)
            => TryApply(clientId, playerName, admission, issuedTokenHash, out player, out _);

        /// <summary>
        /// 判定結果を名簿に反映し、再接続なら席のクライアント ID の付け替えを報告する（#84）。
        /// </summary>
        /// <param name="clientId">接続したクライアント ID。</param>
        /// <param name="playerName">正規化済みのプレイヤー名。</param>
        /// <param name="admission">承認時の判定結果。</param>
        /// <param name="issuedTokenHash">新規参加のときに発行したトークンのハッシュ（#69）。</param>
        /// <param name="player">反映後のエントリ。</param>
        /// <param name="transfer">
        /// 既存の席へ復帰した場合の付け替え（旧クライアント ID → 新クライアント ID）。
        /// 新規に席を作った場合、および保持期間切れで復帰先が消えていた場合は
        /// <see cref="SeatTransfer.None"/>（<c>HasValue == false</c>）。
        /// </param>
        /// <returns>名簿を更新したか。</returns>
        public bool TryApply(
            ulong clientId,
            string playerName,
            LobbyAdmission admission,
            SessionTokenHash issuedTokenHash,
            out LobbyPlayer player,
            out SeatTransfer transfer)
        {
            player = default;
            transfer = SeatTransfer.None;

            if (!admission.IsApproved || string.IsNullOrEmpty(playerName))
            {
                return false;
            }

            if (admission.Kind == LobbyAdmissionKind.Reconnect)
            {
                for (var i = 0; i < _players.Count; i++)
                {
                    if (_players[i].ClientId != admission.PreviousClientId)
                    {
                        continue;
                    }

                    // 接続中のエントリへ戻れるのは引き継ぎ（#163）のときだけ。
                    // 通常の再接続の判定から接続完了までの間に旧エントリが接続中へ戻ることは無いはずだが、
                    // 念のため引き継ぎでない限り接続中の席は奪わない。
                    if (_players[i].IsConnected && !admission.ReplacesConnectedClient)
                    {
                        continue;
                    }

                    var previousClientId = _players[i].ClientId;
                    player = _players[i].AsReconnected(clientId);
                    _players[i] = player;

                    // 席（＝得点行・ペナルティ）を新しいクライアント ID へ引き継ぐよう上位層へ伝える（#84）。
                    transfer = new SeatTransfer(player.SeatId, previousClientId, clientId);
                    return true;
                }

                // 復帰対象が保持期間切れで消えていた場合は新規として扱う（得点も引き継がない）。
            }

            // 同じクライアント ID の「接続中」エントリが残っていたら（切断通知の取りこぼし等）上書きする。
            // 切断中のエントリは復帰用に保持しているので、別人の新規参加で潰さない（レビュー M-4）。
            for (var i = 0; i < _players.Count; i++)
            {
                if (_players[i].ClientId != clientId || _players[i].IsHost || !_players[i].IsConnected)
                {
                    continue;
                }

                // 席そのものは作り直す（別人が同じクライアント ID を使っている状態なので、
                // 前の住人の得点を引き継がせない）。
                player = new LobbyPlayer(
                    clientId,
                    playerName,
                    false,
                    false,
                    true,
                    LobbyPlayer.NotDisconnected,
                    issuedTokenHash,
                    AllocateSeatId());
                _players[i] = player;
                return true;
            }

            player = new LobbyPlayer(
                clientId,
                playerName,
                false,
                false,
                true,
                LobbyPlayer.NotDisconnected,
                issuedTokenHash,
                AllocateSeatId());
            _players.Add(player);
            return true;
        }

        /// <summary>
        /// 切断を反映する。エントリは削除せず <see cref="LobbyPlayer.IsConnected"/> を false にする
        /// （docs/network.md §2.4）。
        /// </summary>
        /// <param name="clientId">切断したクライアント ID。</param>
        /// <param name="nowSec">現在のサーバー時刻（秒）。</param>
        /// <param name="player">更新後のエントリ。</param>
        /// <returns>該当エントリがあり、接続中から切断中へ変化したか。</returns>
        public bool TryMarkDisconnected(ulong clientId, double nowSec, out LobbyPlayer player)
        {
            player = default;

            for (var i = 0; i < _players.Count; i++)
            {
                if (_players[i].ClientId != clientId || !_players[i].IsConnected)
                {
                    continue;
                }

                player = _players[i].AsDisconnected(nowSec);
                _players[i] = player;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 保持期間を過ぎた切断中のエントリを削除する。
        /// </summary>
        /// <param name="nowSec">現在のサーバー時刻（秒）。</param>
        /// <param name="retentionSec">保持する時間（秒）。0 以下なら切断と同時に削除できる。</param>
        /// <returns>削除したエントリ（無ければ空）。</returns>
        public IReadOnlyList<LobbyPlayer> RemoveExpired(double nowSec, double retentionSec)
        {
            List<LobbyPlayer> removed = null;

            for (var i = _players.Count - 1; i >= 0; i--)
            {
                var player = _players[i];

                // ホストのエントリは消さない（ホストが落ちればセッション自体が終わる）。
                if (player.IsConnected || player.IsHost)
                {
                    continue;
                }

                if (nowSec - player.DisconnectedAtSec < retentionSec)
                {
                    continue;
                }

                removed ??= new List<LobbyPlayer>();
                removed.Add(player);
                _players.RemoveAt(i);
            }

            return removed ?? (IReadOnlyList<LobbyPlayer>)Array.Empty<LobbyPlayer>();
        }

        /// <summary>クライアント ID でエントリを引く。</summary>
        public bool TryGet(ulong clientId, out LobbyPlayer player)
        {
            foreach (var candidate in _players)
            {
                if (candidate.ClientId == clientId)
                {
                    player = candidate;
                    return true;
                }
            }

            player = default;
            return false;
        }

        /// <summary>エントリを削除する。</summary>
        public bool Remove(ulong clientId)
        {
            for (var i = 0; i < _players.Count; i++)
            {
                if (_players[i].ClientId != clientId)
                {
                    continue;
                }

                _players.RemoveAt(i);
                return true;
            }

            return false;
        }

        /// <summary>名簿を空にする（ホストを停止したとき）。席の安定 ID も 1 から振り直す。</summary>
        public void Clear()
        {
            _players.Clear();
            _nextSeatId = FirstSeatId;
        }

        /// <summary>
        /// クライアント ID から席の安定 ID を引く（#84）。
        /// </summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <param name="seatId">席の安定 ID。見つからなければ <see cref="LobbyPlayer.NoSeatId"/>。</param>
        /// <returns>見つかったか。</returns>
        public bool TryGetSeatId(ulong clientId, out int seatId)
        {
            if (TryGet(clientId, out var player))
            {
                seatId = player.SeatId;
                return seatId != LobbyPlayer.NoSeatId;
            }

            seatId = LobbyPlayer.NoSeatId;
            return false;
        }

        /// <summary>新しい席の安定 ID を 1 つ払い出す（#84）。</summary>
        private int AllocateSeatId() => _nextSeatId++;

        /// <summary>
        /// 指定した名前で「接続中」のエントリ数を数える（完全一致・<see cref="StringComparison.Ordinal"/>、#85）。
        /// 通常は 0 か 1 だが、再接続経路では 2 以上になり得る（docs/network.md §2.3 の補足。
        /// 「A が切断 → 別人 B が同じ名前で新規参加 → A がトークンで復帰」の順。本人の席の保護を
        /// 名前の一意性より優先しているため）。ホストが気づけるよう警告を出す用途で使う。
        /// </summary>
        /// <param name="playerName">数える名前。null / 空なら 0。</param>
        public int CountConnectedWithName(string playerName)
        {
            if (string.IsNullOrEmpty(playerName))
            {
                return 0;
            }

            var count = 0;
            foreach (var candidate in _players)
            {
                if (candidate.IsConnected && string.Equals(candidate.Name, playerName, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 「プレイヤー名が一致し、かつ発行済みトークンとも一致する」切断中エントリを探す（#69）。
        /// トークンを持っていないエントリ（<see cref="SessionTokenHash.IsEmpty"/>）へは復帰できない。
        /// </summary>
        private bool TryFindDisconnectedByNameAndToken(
            string playerName, SessionToken reconnectToken, out LobbyPlayer player)
        {
            player = default;

            if (!reconnectToken.HasValue)
            {
                return false;
            }

            foreach (var candidate in _players)
            {
                if (candidate.IsConnected || !string.Equals(candidate.Name, playerName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!candidate.TokenHash.Matches(reconnectToken))
                {
                    continue;
                }

                player = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 「プレイヤー名が一致し、かつ発行済みトークンとも一致する」接続中エントリを探す（#163）。
        /// ホスト自身・トークンを持っていないエントリは対象外。
        /// </summary>
        private bool TryFindConnectedByNameAndToken(
            string playerName, SessionToken reconnectToken, out LobbyPlayer player)
        {
            player = default;

            if (!reconnectToken.HasValue)
            {
                return false;
            }

            foreach (var candidate in _players)
            {
                if (!candidate.IsConnected
                    || candidate.IsHost
                    || !string.Equals(candidate.Name, playerName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!candidate.TokenHash.Matches(reconnectToken))
                {
                    continue;
                }

                player = candidate;
                return true;
            }

            return false;
        }

        /// <summary>保持期間中の切断エントリ（＝復帰用に席を押さえているもの）があるか。</summary>
        private bool HasRetainedDisconnectedSeat()
        {
            foreach (var candidate in _players)
            {
                if (!candidate.IsConnected && candidate.OccupiesPlayerSlot)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>予約のうち、定員に対して新しい席を要求するもの（新規参加）の数。</summary>
        private static int CountNewSeatReservations(IReadOnlyList<LobbyReservation> reservations)
        {
            if (reservations == null)
            {
                return 0;
            }

            var count = 0;
            for (var i = 0; i < reservations.Count; i++)
            {
                if (reservations[i].OccupiesNewSeat)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>その名前で既に承認済みの予約があるか。</summary>
        private static bool IsNameReserved(IReadOnlyList<LobbyReservation> reservations, string playerName)
        {
            if (reservations == null)
            {
                return false;
            }

            for (var i = 0; i < reservations.Count; i++)
            {
                if (string.Equals(reservations[i].PlayerName, playerName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>その席（復帰先エントリ）を狙う再接続の予約が既にあるか。</summary>
        private static bool IsSeatReserved(IReadOnlyList<LobbyReservation> reservations, ulong previousClientId)
        {
            if (reservations == null)
            {
                return false;
            }

            for (var i = 0; i < reservations.Count; i++)
            {
                if (reservations[i].Kind == LobbyAdmissionKind.Reconnect
                    && reservations[i].PreviousClientId == previousClientId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 接続中のプレイヤーに同じ名前が居るか（docs/network.md §2.3 の判定 3）。
        /// 数え方は <see cref="CountConnectedWithName"/> に一本化してある（レビュー L-4）。
        /// </summary>
        private bool IsNameTakenByConnectedPlayer(string playerName)
            => CountConnectedWithName(playerName) > 0;
    }
}
