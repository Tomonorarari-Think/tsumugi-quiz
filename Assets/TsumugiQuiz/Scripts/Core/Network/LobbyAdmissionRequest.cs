namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 名簿に依存する入室判定（docs/network.md §2.3 の 4〜6）へ渡す入力（不変値、#69）。
    ///
    /// <c>ConnectionApprovalHandler.AdmissionEvaluator</c> の引数。
    /// ペイロードの形式・プレイヤー名の検証（1〜3）を通過したものだけがここへ来る。
    /// </summary>
    /// <remarks>
    /// 引数を構造体にまとめてあるのは、再接続トークン（#69）のように
    /// 判定に必要な材料が増えてもフックの形（<c>Func</c> の型引数）を変えずに済ませるため。
    /// </remarks>
    public readonly struct LobbyAdmissionRequest
    {
        /// <summary>
        /// 入力を作る。
        /// </summary>
        /// <param name="clientId">承認対象のクライアント ID。</param>
        /// <param name="playerName">検証・正規化済みのプレイヤー名（形式が正しいことだけが保証される）。</param>
        /// <param name="reconnectToken">クライアントが提示した再接続トークン（無ければ <see cref="SessionToken.None"/>）。</param>
        public LobbyAdmissionRequest(ulong clientId, string playerName, SessionToken reconnectToken)
        {
            ClientId = clientId;
            PlayerName = playerName ?? string.Empty;
            ReconnectToken = reconnectToken;
        }

        /// <summary>承認対象のクライアント ID。</summary>
        public ulong ClientId { get; }

        /// <summary>検証・正規化済みのプレイヤー名。</summary>
        public string PlayerName { get; }

        /// <summary>クライアントが提示した再接続トークン。</summary>
        public SessionToken ReconnectToken { get; }

        /// <summary>検証済みのペイロードから入力を作る。</summary>
        public static LobbyAdmissionRequest FromPayload(ulong clientId, ConnectionPayload payload)
            => new LobbyAdmissionRequest(clientId, payload.PlayerName, payload.ReconnectToken);
    }
}
