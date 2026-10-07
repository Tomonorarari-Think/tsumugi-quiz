namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// ロビー名簿に基づく入室判定の結果（不変）。
    /// <see cref="ConnectionApprovalEvaluator"/> がペイロードの妥当性を確認したあと、
    /// <see cref="LobbyRoster.Evaluate"/> が名簿の状態（定員・フェーズ・再接続）を見て決める。
    /// </summary>
    public readonly struct LobbyAdmission
    {
        private LobbyAdmission(
            LobbyAdmissionKind kind,
            ulong previousClientId,
            ConnectionRejectionReason rejectionReason,
            string message,
            bool replacesConnectedClient = false)
        {
            Kind = kind;
            PreviousClientId = previousClientId;
            RejectionReason = rejectionReason;
            Message = message ?? string.Empty;
            ReplacesConnectedClient = replacesConnectedClient;
        }

        /// <summary>判定の種別。</summary>
        public LobbyAdmissionKind Kind { get; }

        /// <summary>
        /// 再接続のとき、復帰させるエントリが最後に使っていたクライアント ID。
        /// それ以外では 0。
        /// </summary>
        public ulong PreviousClientId { get; }

        /// <summary>
        /// 再接続のうち、復帰先のエントリが名簿上まだ「接続中」だった（＝ホストが旧接続の切断をまだ検知していない）
        /// ものか（#163）。true のとき、ホストは旧クライアント ID の接続を切ってから席を引き継がせる。
        /// </summary>
        public bool ReplacesConnectedClient { get; }

        /// <summary>
        /// 拒否のときの理由コード。承認時は <see cref="ConnectionRejectionReason.None"/>。
        /// </summary>
        public ConnectionRejectionReason RejectionReason { get; }

        /// <summary>クライアントに返す定型文。承認時は空文字。</summary>
        public string Message { get; }

        /// <summary>入室を認めたか（新規・再接続のいずれか）。</summary>
        public bool IsApproved
            => Kind == LobbyAdmissionKind.NewPlayer || Kind == LobbyAdmissionKind.Reconnect;

        /// <summary>新規プレイヤーとして受け入れる判定。</summary>
        public static LobbyAdmission NewPlayer()
            => new LobbyAdmission(LobbyAdmissionKind.NewPlayer, 0UL, ConnectionRejectionReason.None, string.Empty);

        /// <summary>切断中の同名エントリを復帰させる判定。</summary>
        /// <param name="previousClientId">復帰対象のエントリが最後に使っていたクライアント ID。</param>
        public static LobbyAdmission Reconnect(ulong previousClientId)
            => new LobbyAdmission(LobbyAdmissionKind.Reconnect, previousClientId, ConnectionRejectionReason.None, string.Empty);

        /// <summary>
        /// 名簿上まだ接続中の同名エントリを、再接続トークンの一致を根拠に引き継ぐ判定（#163）。
        /// 種別は <see cref="LobbyAdmissionKind.Reconnect"/> と同じで、<see cref="ReplacesConnectedClient"/> が true。
        /// クライアントが強制終了した直後は、ホストが切断を検知する（UTP の DisconnectTimeoutMS）まで
        /// 旧エントリが接続中のまま残るため、その間の再接続を拒否せずに受け入れるためのもの。
        /// </summary>
        /// <param name="previousClientId">引き継ぐエントリが現在使っているクライアント ID（旧接続）。</param>
        public static LobbyAdmission Takeover(ulong previousClientId)
            => new LobbyAdmission(
                LobbyAdmissionKind.Reconnect, previousClientId, ConnectionRejectionReason.None, string.Empty, true);

        /// <summary>定員超過による拒否。</summary>
        public static LobbyAdmission RoomFull()
            => new LobbyAdmission(
                LobbyAdmissionKind.RejectRoomFull,
                0UL,
                ConnectionRejectionReason.RoomFull,
                ConnectionRejectionMessages.RoomFull);

        /// <summary>ゲーム進行中（途中参加不許可）による拒否。</summary>
        public static LobbyAdmission GameInProgress()
            => new LobbyAdmission(
                LobbyAdmissionKind.RejectGameInProgress,
                0UL,
                ConnectionRejectionReason.GameInProgress,
                ConnectionRejectionMessages.GameInProgress);

        /// <summary>
        /// 名前重複による拒否。接続中のプレイヤーと同名の場合と、
        /// 承認済みで接続完了待ちのクライアントと同名の場合の両方に使う。
        /// </summary>
        public static LobbyAdmission DuplicateName()
            => new LobbyAdmission(
                LobbyAdmissionKind.RejectDuplicateName,
                0UL,
                ConnectionRejectionReason.DuplicatePlayerName,
                ConnectionRejectionMessages.DuplicatePlayerName);

        /// <summary>空きが切断者の復帰用に確保された席だけだったことによる拒否（#7 Q3）。</summary>
        public static LobbyAdmission SeatReserved()
            => new LobbyAdmission(
                LobbyAdmissionKind.RejectSeatReserved,
                0UL,
                ConnectionRejectionReason.SeatReserved,
                ConnectionRejectionMessages.SeatReserved);

        /// <summary>プレイヤー名が空だったことによる拒否。</summary>
        public static LobbyAdmission InvalidPlayerName()
            => new LobbyAdmission(
                LobbyAdmissionKind.RejectInvalidPlayerName,
                0UL,
                ConnectionRejectionReason.InvalidPlayerName,
                ConnectionRejectionMessages.InvalidPlayerName);

        /// <summary>名簿がまだ準備できていないことによる拒否（fail-closed）。</summary>
        public static LobbyAdmission LobbyNotReady()
            => new LobbyAdmission(
                LobbyAdmissionKind.RejectLobbyNotReady,
                0UL,
                ConnectionRejectionReason.LobbyNotReady,
                ConnectionRejectionMessages.LobbyNotReady);
    }
}
