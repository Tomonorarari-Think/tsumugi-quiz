namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// ホストの役割（docs/room-settings.md §1 の <c>host.role</c>）。
    /// <see cref="Moderator"/>（司会専任 / 司会専用モード、requirements.md FR-80）のときは
    /// ホストをプレイヤーとして扱わず、参加人数（<c>room.maxPlayers</c>）にも数えない（仮決め K18）。
    /// </summary>
    public enum HostRole
    {
        /// <summary>ホストもプレイヤーとして参加する（既定）。</summary>
        Player = 0,

        /// <summary>ホストは司会専任。プレイヤーとしては参加しない。</summary>
        Moderator = 1,
    }
}
