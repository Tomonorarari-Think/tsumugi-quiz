namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// NGO（Netcode for GameObjects 2.13.2）と、#208 より前のこのアプリが切断理由に入れる英語の文字列（issue #208）。
    /// <see cref="DisconnectReasonLocalizer"/> がこれらを日本語の文言に対応づける。
    /// NGO を更新したら、ここの文字列が変わっていないかを PlayMode の <c>NetworkServiceDisconnectReasonTests</c> と
    /// EditMode の <c>NetworkDisconnectReasonTests</c> で確かめる（docs/network.md §2.4「切断理由の対応づけ」）。
    /// </summary>
    public static class NgoDisconnectReasons
    {
        /// <summary>
        /// ホストが停止したとき、各クライアントへ送る理由。NGO 2.13.2 <c>Runtime/Core/NetworkManager.cs</c> の
        /// <c>ProcessServerShutdown</c>（<c>$"Disconnected due to {hostServer} shutting down."</c>、hostServer = "host"）。
        /// </summary>
        public const string HostShuttingDown = "Disconnected due to host shutting down.";

        /// <summary>
        /// 専用サーバーが停止したときの理由（同じ箇所で hostServer = "server"）。このアプリは Host モードだけを使うが、
        /// 同じ意味なので対応づけておく。
        /// </summary>
        public const string ServerShuttingDown = "Disconnected due to server shutting down.";

        /// <summary>
        /// #208 より前の <c>RpcRateGuard</c> が送っていた理由。今は <see cref="DisconnectReasonMessages.RateLimitExceeded"/> を送る。
        /// </summary>
        public const string LegacyRateLimitExceeded = "rate limit exceeded";

        /// <summary>
        /// サーバーからの理由が無い切断で、NGO がクライアント側で組み立てる診断文字列の先頭。NGO 2.13.2
        /// <c>Runtime/Connection/NetworkConnectionManager.cs</c> の <c>GenerateDisconnectInformation</c>:
        /// <c>[Disconnect Event][Client-{id}][TransportClientId-{id}][{DisconnectEvent}] {理由} {Transport の文言}</c>。
        /// </summary>
        public const string DisconnectEventHeader = "[Disconnect Event]";

        /// <summary>
        /// Transport のタイムアウト（<c>NetworkTransport.DisconnectEvents.ProtocolTimeout</c> の名前）。
        /// </summary>
        public const string ProtocolTimeoutEvent = "ProtocolTimeout";

        /// <summary>
        /// 接続の試行回数の上限に達した（<c>NetworkTransport.DisconnectEvents.MaxConnectionAttempts</c> の名前）。
        /// </summary>
        public const string MaxConnectionAttemptsEvent = "MaxConnectionAttempts";

        /// <summary>
        /// 自分の Transport を停止した（<c>NetworkTransport.DisconnectEvents.TransportShutdown</c> の名前）。
        /// 自分で <c>NetworkService.Stop()</c> したときのほか、NGO がクライアント側の承認待ちのタイムアウトで
        /// 自ら停止したときにも入る。
        /// </summary>
        public const string TransportShutdownEvent = "TransportShutdown";
    }
}
