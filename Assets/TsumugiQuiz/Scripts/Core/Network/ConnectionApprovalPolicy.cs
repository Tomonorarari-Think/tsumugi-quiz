namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 接続承認の判定条件（不変）。ホスト側が接続開始時に確定させ、判定のたびに渡す。
    /// 「プロトコルバージョン一致」「ペイロード検証」「ビルド一致（#204）」「人数上限」を扱う。
    /// フェーズ判定・再接続判定は名簿側（<c>LobbyState</c>、#7）が行う（docs/network.md §2.3 の 4〜6）。
    /// </summary>
    public readonly struct ConnectionApprovalPolicy
    {
        private readonly string _expectedClientBuildHash;

        /// <summary>
        /// 判定条件を生成する。
        /// </summary>
        /// <param name="expectedProtocolVersion">ホストのプロトコルバージョン。</param>
        /// <param name="maxPlayers">
        /// 参加人数の上限。null なら人数制限なし（ルーム設定が未実装の M1 既定）。
        /// 司会専用モードではホストを数に含めない運用にする（仮決め K18）ため、
        /// 数え方の責務は呼び出し側（<c>connectedPlayerCount</c> の作り方）に置く。
        /// </param>
        /// <param name="expectedClientBuildHash">
        /// ホストのビルドの識別子（#204）。クライアントの <c>clientBuildHash</c> と完全一致（大文字小文字も区別）しなければ拒否する。
        /// null は空文字として扱う。
        /// </param>
        public ConnectionApprovalPolicy(ushort expectedProtocolVersion, int? maxPlayers, string expectedClientBuildHash = "")
        {
            ExpectedProtocolVersion = expectedProtocolVersion;
            MaxPlayers = maxPlayers;
            _expectedClientBuildHash = expectedClientBuildHash ?? string.Empty;
        }

        /// <summary>ホストのプロトコルバージョン。</summary>
        public ushort ExpectedProtocolVersion { get; }

        /// <summary>参加人数の上限。null なら制限なし。</summary>
        public int? MaxPlayers { get; }

        /// <summary>
        /// ホストのビルドの識別子（#204）。クライアントの <c>clientBuildHash</c> と完全一致しなければ拒否する。
        /// Editor では空になる（<c>TsumugiQuiz.Network.LocalBuildIdentity</c>）。null にはならない。
        /// </summary>
        public string ExpectedClientBuildHash => _expectedClientBuildHash ?? string.Empty;

        /// <summary>現行プロトコルバージョン・人数制限なし・ビルドの識別子が空の既定条件。</summary>
        public static ConnectionApprovalPolicy Default
            => new ConnectionApprovalPolicy(ProtocolConstants.Version, null, string.Empty);

        /// <summary>人数上限だけを差し替えた新しい条件を返す。</summary>
        public ConnectionApprovalPolicy WithMaxPlayers(int? maxPlayers)
            => new ConnectionApprovalPolicy(ExpectedProtocolVersion, maxPlayers, ExpectedClientBuildHash);

        /// <summary>ビルドの識別子だけを差し替えた新しい条件を返す（#204）。</summary>
        public ConnectionApprovalPolicy WithExpectedClientBuildHash(string expectedClientBuildHash)
            => new ConnectionApprovalPolicy(ExpectedProtocolVersion, MaxPlayers, expectedClientBuildHash);
    }
}
