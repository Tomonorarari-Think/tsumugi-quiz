namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// ロビー名簿に基づく入室判定の種別（docs/network.md §2.3 の 4〜6）。
    /// </summary>
    public enum LobbyAdmissionKind
    {
        /// <summary>新規プレイヤーとして受け入れる。</summary>
        NewPlayer = 0,

        /// <summary>切断中の同名エントリを復帰させる（再接続）。</summary>
        Reconnect,

        /// <summary>定員に達しているため拒否。</summary>
        RejectRoomFull,

        /// <summary>ゲーム進行中で途中参加が許可されていないため拒否。</summary>
        RejectGameInProgress,

        /// <summary>接続中のプレイヤー（または承認済みで接続完了待ちのクライアント）と名前が重複しているため拒否。</summary>
        RejectDuplicateName,

        /// <summary>空きが切断者の復帰用に確保された席だけなので拒否（#7 Q3）。</summary>
        RejectSeatReserved,

        /// <summary>プレイヤー名が空だったため拒否（本来は手前の検証で弾かれる）。</summary>
        RejectInvalidPlayerName,

        /// <summary>ロビーの名簿がまだ準備できていないため拒否（fail-closed）。</summary>
        RejectLobbyNotReady,
    }
}
