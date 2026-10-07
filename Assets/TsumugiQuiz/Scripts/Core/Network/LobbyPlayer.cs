namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// ロビー名簿の 1 エントリ（不変値）。サーバー（ホスト）だけが作り替え、
    /// クライアントへは <c>TsumugiQuiz.Network.PlayerEntry</c> として同期される
    /// （docs/network.md §1.2 / §2.4）。
    ///
    /// 切断してもエントリは残し（<see cref="IsConnected"/> = false）、一定時間内に
    /// 「同じプレイヤー名 + 発行済みトークンの一致」で再接続したら同じエントリを復帰させる
    /// （docs/network.md §2.3 の 6、issue #69）。
    /// </summary>
    public readonly struct LobbyPlayer
    {
        /// <summary>切断していないエントリの <see cref="DisconnectedAtSec"/> に入れる値。</summary>
        public const double NotDisconnected = 0.0;

        /// <summary>席の安定 ID が未割り当てであることを表す値（#84）。</summary>
        public const int NoSeatId = 0;

        /// <summary>
        /// エントリを生成する。
        /// </summary>
        /// <param name="clientId">NGO のクライアント ID。切断中は最後に使われた ID を保持する。</param>
        /// <param name="name">正規化済みのプレイヤー名（<see cref="PlayerNameValidator"/> 通過後）。</param>
        /// <param name="isHost">ホスト自身のエントリか。</param>
        /// <param name="isModerator">司会専任（ホストが <see cref="HostRole.Moderator"/>）か。</param>
        /// <param name="isConnected">現在接続中か。</param>
        /// <param name="disconnectedAtSec">切断した時刻（サーバー時刻軸の秒）。接続中は 0。</param>
        /// <param name="tokenHash">
        /// このエントリに発行した再接続トークンのハッシュ（#69）。
        /// 既定（<see cref="SessionTokenHash.None"/>）のエントリへは誰も再接続できない。
        /// </param>
        /// <param name="seatId">
        /// 席の安定 ID（#84）。<see cref="LobbyRoster"/> が入室時に 1 から振り、
        /// 再接続（<see cref="AsReconnected"/>）でも変わらない。得点・ペナルティを
        /// 新しい <paramref name="clientId"/> へ移し替えるときの「同じ人である」根拠になる。
        /// </param>
        public LobbyPlayer(
            ulong clientId,
            string name,
            bool isHost,
            bool isModerator,
            bool isConnected,
            double disconnectedAtSec,
            SessionTokenHash tokenHash = default,
            int seatId = NoSeatId)
        {
            ClientId = clientId;
            Name = name ?? string.Empty;
            IsHost = isHost;
            IsModerator = isModerator;
            IsConnected = isConnected;
            DisconnectedAtSec = disconnectedAtSec;
            TokenHash = tokenHash;
            SeatId = seatId;
        }

        /// <summary>NGO のクライアント ID。</summary>
        public ulong ClientId { get; }

        /// <summary>正規化済みのプレイヤー名。</summary>
        public string Name { get; }

        /// <summary>ホスト自身のエントリか。</summary>
        public bool IsHost { get; }

        /// <summary>司会専任か（ホスト以外は常に false）。</summary>
        public bool IsModerator { get; }

        /// <summary>現在接続中か。false なら一覧ではグレー表示にする（docs/network.md §2.4）。</summary>
        public bool IsConnected { get; }

        /// <summary>切断した時刻（サーバー時刻軸の秒）。接続中は <see cref="NotDisconnected"/>。</summary>
        public double DisconnectedAtSec { get; }

        /// <summary>
        /// このエントリに発行した再接続トークンのハッシュ（#69）。**サーバーだけが持ち、
        /// クライアントへ同期しない**（<c>TsumugiQuiz.Network.PlayerEntry</c> には含めない）。
        /// </summary>
        public SessionTokenHash TokenHash { get; }

        /// <summary>
        /// 席の安定 ID（#84）。再接続で <see cref="ClientId"/> が変わっても不変で、
        /// 得点行・ペナルティを新しいクライアント ID へ移し替える根拠になる。
        /// <b>クライアントへは同期しない</b>（<c>TsumugiQuiz.Network.PlayerEntry</c> には含めない。
        /// 表示に使う識別子は従来どおり <see cref="ClientId"/>）。
        /// </summary>
        public int SeatId { get; }

        /// <summary>
        /// 定員（<c>room.maxPlayers</c>）に数えるエントリか。
        /// 司会専任のホストはプレイヤーではないので数えない（仮決め K18）。
        /// </summary>
        public bool OccupiesPlayerSlot => !(IsHost && IsModerator);

        /// <summary>切断済みにした新しいエントリを返す。</summary>
        public LobbyPlayer AsDisconnected(double atSec)
            => new LobbyPlayer(ClientId, Name, IsHost, IsModerator, false, atSec, TokenHash, SeatId);

        /// <summary>
        /// 新しいクライアント ID で復帰させた新しいエントリを返す。
        /// トークンのハッシュは引き継ぐ（トークンはホストのプロセス寿命の間ずっと有効。#69）。
        /// </summary>
        public LobbyPlayer AsReconnected(ulong clientId)
            => new LobbyPlayer(clientId, Name, IsHost, IsModerator, true, NotDisconnected, TokenHash, SeatId);

        /// <summary>司会専任フラグだけを差し替えた新しいエントリを返す。</summary>
        public LobbyPlayer WithModerator(bool isModerator)
            => new LobbyPlayer(ClientId, Name, IsHost, isModerator, IsConnected, DisconnectedAtSec, TokenHash, SeatId);

    }
}
