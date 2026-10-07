namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// クライアントが接続時にホストへ送る承認ペイロード（docs/network.md §2.2 / §2.3）。
    /// 不変値オブジェクト。NGO の <c>NetworkConfig.ConnectionData</c> にバイト列として載る。
    /// </summary>
    public readonly struct ConnectionPayload
    {
        /// <summary>
        /// 承認ペイロードを生成する。
        /// </summary>
        /// <param name="protocolVersion">クライアントのプロトコルバージョン。</param>
        /// <param name="playerName">プレイヤー名（未検証で構わない。検証は <see cref="ConnectionApprovalEvaluator"/>）。</param>
        /// <param name="clientBuildHash">クライアントのビルド識別子。空文字可。</param>
        /// <param name="reconnectToken">
        /// 前回この部屋に入ったときにホストから受け取った再接続トークン（issue #69）。
        /// 初回接続・トークンを持っていない場合は <see cref="SessionToken.None"/>。
        /// </param>
        public ConnectionPayload(
            ushort protocolVersion,
            string playerName,
            string clientBuildHash,
            SessionToken reconnectToken = default)
        {
            ProtocolVersion = protocolVersion;
            PlayerName = playerName ?? string.Empty;
            ClientBuildHash = clientBuildHash ?? string.Empty;
            ReconnectToken = reconnectToken;
        }

        /// <summary>クライアントのプロトコルバージョン。</summary>
        public ushort ProtocolVersion { get; }

        /// <summary>プレイヤー名。</summary>
        public string PlayerName { get; }

        /// <summary>クライアントのビルド識別子（将来の互換性判定用。M1 では検証のみ）。</summary>
        public string ClientBuildHash { get; }

        /// <summary>
        /// 再接続トークン（issue #69）。ホストは「プレイヤー名の一致 + トークンの一致」で
        /// 本人の席・得点へ復帰させる。無ければ新規参加として扱う。
        /// </summary>
        public SessionToken ReconnectToken { get; }

        /// <summary>プレイヤー名だけを差し替えた新しいインスタンスを返す。</summary>
        public ConnectionPayload WithPlayerName(string playerName)
            => new ConnectionPayload(ProtocolVersion, playerName, ClientBuildHash, ReconnectToken);

        /// <summary>再接続トークンだけを差し替えた新しいインスタンスを返す。</summary>
        public ConnectionPayload WithReconnectToken(SessionToken reconnectToken)
            => new ConnectionPayload(ProtocolVersion, PlayerName, ClientBuildHash, reconnectToken);
    }
}
