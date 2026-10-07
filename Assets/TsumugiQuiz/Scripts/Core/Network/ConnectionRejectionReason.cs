namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 接続承認を拒否した理由。ログと単体テストのために内部で使う識別子であり、
    /// クライアントへ送る文言は <see cref="ConnectionRejectionMessages"/> が生成する定型文に限る
    /// （例外メッセージやスタックトレースを返さない: docs/network.md §9）。
    /// </summary>
    public enum ConnectionRejectionReason
    {
        /// <summary>拒否していない（承認）。</summary>
        None = 0,

        /// <summary>ペイロードが空、または null。</summary>
        PayloadMissing,

        /// <summary>ペイロードが上限バイト数を超えている。</summary>
        PayloadTooLarge,

        /// <summary>ペイロードの形式が壊れている（長さ不整合・不正な UTF-8 など）。</summary>
        PayloadMalformed,

        /// <summary>プロトコルバージョンがホストと一致しない。</summary>
        ProtocolVersionMismatch,

        /// <summary>プレイヤー名が規則を満たさない。</summary>
        InvalidPlayerName,

        /// <summary>クライアントビルドハッシュが規則を満たさない。</summary>
        InvalidClientBuildHash,

        /// <summary>
        /// 再接続トークンの形式が不正（長さが 128bit でない、16 進表記でない、など。#69）。
        /// 「トークンが一致しない」場合はこの理由ではなく、新規参加として扱ったうえで
        /// 定員・フェーズの判定結果（<see cref="RoomFull"/> 等）を返す。
        /// </summary>
        InvalidReconnectToken,

        /// <summary>参加人数が上限に達している。</summary>
        RoomFull,

        /// <summary>
        /// ゲームが進行中で、途中参加（<c>network.allowLateJoin</c>）が許可されていない（#7）。
        /// </summary>
        GameInProgress,

        /// <summary>
        /// 接続中のプレイヤーとプレイヤー名が重複している（#7、統括判断 Q2）。
        /// 再接続をプレイヤー名の一致で判定する方式（仮決め K-N1）のため、
        /// 同名の同時接続は名簿と再接続の対応を一意に決められなくなるので拒否する。
        /// </summary>
        DuplicatePlayerName,

        /// <summary>
        /// 定員は埋まっていないが、空きが「切断したプレイヤーの復帰用に確保された席」だけだった（#7 Q3）。
        /// 単なる満室（<see cref="RoomFull"/>）と区別して、少し待てば入れる可能性を伝える。
        /// </summary>
        SeatReserved,

        /// <summary>
        /// ロビーの名簿がまだ準備できていない（<c>LobbyState</c> のスポーン前など、#7）。
        /// 判定材料が無い状態で素通しすると定員・フェーズを無視した参加を許してしまうため、
        /// fail-closed で拒否する。クライアントは少し待ってから再試行すればよい。
        /// </summary>
        LobbyNotReady,

        /// <summary>
        /// クライアントのビルドがホストと一致しない（承認ペイロードの <c>clientBuildHash</c> が違う、#204）。
        /// 文言はプロトコルバージョンの不一致と同じ書式（<see cref="ConnectionRejectionMessages.ProtocolVersionMismatchFormat"/>）で、
        /// 数字はビルドの識別子から作った番号（<see cref="BuildDisplayNumber"/>）。
        /// 拒否ログの間引きのキーにこの値を使うので、既存の値を変えないよう末尾に足している。
        /// </summary>
        ClientBuildMismatch,
    }
}
