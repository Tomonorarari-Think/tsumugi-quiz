namespace TsumugiQuiz.Core.Participants
{
    /// <summary>
    /// 参加者パネル（#194）に渡す名簿の 1 行。UI 層が <c>LobbyState</c> の名簿から作る
    /// （Core は Network の型を参照できないため、必要な値だけを写した入れ物）。
    /// </summary>
    public readonly struct ParticipantRosterEntry
    {
        /// <summary>クライアント ID。</summary>
        public ulong ClientId { get; }

        /// <summary>表示名（同名の連番 #85 を解決済みのもの）。</summary>
        public string DisplayName { get; }

        /// <summary>接続中か。</summary>
        public bool IsConnected { get; }

        /// <summary>司会専任のホストか（参加者ではないのでパネルに出さない）。</summary>
        public bool IsModeratorHost { get; }

        /// <summary>値を指定して生成する。</summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <param name="displayName">表示名。null は空文字として扱う。</param>
        /// <param name="isConnected">接続中か。</param>
        /// <param name="isModeratorHost">司会専任のホストか。</param>
        public ParticipantRosterEntry(ulong clientId, string displayName, bool isConnected, bool isModeratorHost)
        {
            ClientId = clientId;
            DisplayName = displayName ?? string.Empty;
            IsConnected = isConnected;
            IsModeratorHost = isModeratorHost;
        }
    }
}
