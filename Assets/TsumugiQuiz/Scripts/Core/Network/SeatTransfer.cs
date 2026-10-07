namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 再接続で「席（名簿エントリ）」のクライアント ID が付け替わったことを表す不変値（#84）。
    /// <see cref="LobbyRoster.TryApply(ulong, string, LobbyAdmission, SessionTokenHash, out LobbyPlayer, out SeatTransfer)"/>
    /// が返し、<c>TsumugiQuiz.Network.LobbyState</c> が同じ層の <c>GameSession</c> へ通知する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 得点（<c>ScoreBoard</c>）・ペナルティ（<c>PenaltyTracker</c>）・回答済み・早押しロック保持者は
    /// いずれも NGO の <c>clientId</c> をキーにしている。NGO はクライアント ID を
    /// 使い回さない（NGO 2.13.2 <c>Runtime/Connection/NetworkConnectionManager.cs</c> L350 /
    /// L531 の <c>m_NextClientId++</c>）ので、再接続すると同じ人でも別のキーになる。
    /// そこで「席の安定 ID（<see cref="LobbyPlayer.SeatId"/>、再接続で不変）」を軸に、
    /// 旧 ID から新 ID へ状態を移し替える（docs/network.md §2.4 の引き継ぎ表）。
    /// </para>
    /// <para>
    /// 新規参加（席を新しく作った場合）は <see cref="None"/> を返す。
    /// 「同名・トークン無しの別人」は新規参加として評価されるため、ここには絶対に現れない（#69）。
    /// </para>
    /// </remarks>
    public readonly struct SeatTransfer
    {
        /// <summary>付け替えが発生しなかったことを表す値。</summary>
        public static readonly SeatTransfer None = default;

        /// <summary>
        /// 付け替えを生成する。
        /// </summary>
        /// <param name="seatId">席の安定 ID（<see cref="LobbyPlayer.SeatId"/>）。</param>
        /// <param name="previousClientId">切断時に使っていた古いクライアント ID。</param>
        /// <param name="clientId">復帰して新しく割り当てられたクライアント ID。</param>
        public SeatTransfer(int seatId, ulong previousClientId, ulong clientId)
        {
            SeatId = seatId;
            PreviousClientId = previousClientId;
            ClientId = clientId;
        }

        /// <summary>席の安定 ID。<see cref="LobbyPlayer.NoSeatId"/> なら無効。</summary>
        public int SeatId { get; }

        /// <summary>切断時に使っていた古いクライアント ID。</summary>
        public ulong PreviousClientId { get; }

        /// <summary>復帰して新しく割り当てられたクライアント ID。</summary>
        public ulong ClientId { get; }

        /// <summary>
        /// 実際に付け替えが必要か（席が有効で、かつ旧 ID と新 ID が異なる）。
        /// </summary>
        public bool HasValue => SeatId != LobbyPlayer.NoSeatId && PreviousClientId != ClientId;

        /// <inheritdoc />
        public override string ToString() =>
            HasValue ? $"seat={SeatId} {PreviousClientId} -> {ClientId}" : "（付け替えなし）";
    }
}
