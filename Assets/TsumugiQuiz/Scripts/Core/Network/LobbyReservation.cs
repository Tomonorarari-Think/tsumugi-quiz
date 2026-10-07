namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 承認済みでまだ接続が完了していないクライアントの「席の予約」（不変値、#7 レビュー H-1）。
    ///
    /// 接続承認は 1 件ずつ処理されるのに対し、名簿（<see cref="LobbyRoster"/>）への反映は
    /// 接続完了時なので、その間は名簿に現れない。予約として <see cref="LobbyRoster.Evaluate"/> に
    /// 渡すことで、この隙間に「定員超過」「同名の二重参加」「同じ席への二重再接続」が
    /// すり抜けるのを防ぐ。
    /// </summary>
    public readonly struct LobbyReservation
    {
        /// <summary>
        /// 予約を作る。
        /// </summary>
        /// <param name="playerName">承認したクライアントの正規化済みプレイヤー名。</param>
        /// <param name="kind">
        /// 承認時の判定種別。<see cref="LobbyAdmissionKind.NewPlayer"/> か
        /// <see cref="LobbyAdmissionKind.Reconnect"/> のいずれか。
        /// </param>
        /// <param name="previousClientId">
        /// 再接続の場合、復帰先エントリが最後に使っていたクライアント ID。新規なら 0。
        /// </param>
        public LobbyReservation(string playerName, LobbyAdmissionKind kind, ulong previousClientId)
        {
            PlayerName = playerName ?? string.Empty;
            Kind = kind;
            PreviousClientId = previousClientId;
        }

        /// <summary>承認したクライアントの正規化済みプレイヤー名。</summary>
        public string PlayerName { get; }

        /// <summary>承認時の判定種別。</summary>
        public LobbyAdmissionKind Kind { get; }

        /// <summary>再接続の場合の復帰先クライアント ID。新規なら 0。</summary>
        public ulong PreviousClientId { get; }

        /// <summary>
        /// 定員に対して**新しい席**を要求する予約か。
        /// 再接続は既存エントリの席へ戻るだけなので数えない（二重計上を防ぐ。レビュー M-1 / M-2）。
        /// </summary>
        public bool OccupiesNewSeat => Kind == LobbyAdmissionKind.NewPlayer;

        /// <summary>新規参加の予約を作る。</summary>
        public static LobbyReservation NewPlayer(string playerName)
            => new LobbyReservation(playerName, LobbyAdmissionKind.NewPlayer, 0UL);

        /// <summary>再接続の予約を作る。</summary>
        public static LobbyReservation Reconnect(string playerName, ulong previousClientId)
            => new LobbyReservation(playerName, LobbyAdmissionKind.Reconnect, previousClientId);

        /// <summary>承認結果から予約を作る。</summary>
        public static LobbyReservation FromAdmission(string playerName, LobbyAdmission admission)
            => new LobbyReservation(playerName, admission.Kind, admission.PreviousClientId);
    }
}
