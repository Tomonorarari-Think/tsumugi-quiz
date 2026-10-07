namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// 自動ポート開放の結果（不変）。失敗しても例外にせず、この値で理由を返す。
    /// </summary>
    public readonly struct PortMappingResult
    {
        // default(PortMappingResult)（初期化されていない値）でも null 参照にならないよう、
        // 文字列は必ずプロパティ側で null を空文字に畳む（H-2）。
        private readonly string _deviceExternalIpAddress;
        private readonly string _deviceProtocolName;
        private readonly string _message;

        private PortMappingResult(
            PortMappingStatus status,
            ushort internalPort,
            ushort externalPort,
            string deviceExternalIpAddress,
            string deviceProtocolName,
            string message)
        {
            Status = status;
            InternalPort = internalPort;
            ExternalPort = externalPort;
            _deviceExternalIpAddress = deviceExternalIpAddress;
            _deviceProtocolName = deviceProtocolName;
            _message = message;
        }

        /// <summary>結果種別。</summary>
        public PortMappingStatus Status { get; }

        /// <summary>要求した内部ポート。</summary>
        public ushort InternalPort { get; }

        /// <summary>
        /// 実際に開いた外部ポート（失敗時は 0）。
        /// ルーターが別のポートを割り当てることがあるため、参加コードにはこの値を使う。
        /// </summary>
        public ushort ExternalPort { get; }

        /// <summary>NAT デバイスが答えた外部 IP（取得できなければ空文字）。</summary>
        public string DeviceExternalIpAddress => _deviceExternalIpAddress ?? string.Empty;

        /// <summary>応答したデバイスのプロトコル名（"UPnP" / "NAT-PMP"、失敗時は空文字）。</summary>
        public string DeviceProtocolName => _deviceProtocolName ?? string.Empty;

        /// <summary>UI に出せる日本語の説明（成功時も理由を残す）。</summary>
        public string Message => _message ?? string.Empty;

        /// <summary>成功したか。</summary>
        public bool Success => Status == PortMappingStatus.Success;

        /// <summary>まだ試していない状態。</summary>
        public static PortMappingResult NotAttempted { get; } = new PortMappingResult(
            PortMappingStatus.NotAttempted, 0, 0, string.Empty, string.Empty, string.Empty);

        /// <summary>成功結果を作る。</summary>
        /// <param name="internalPort">内部ポート。</param>
        /// <param name="externalPort">ルーターが実際に割り当てた外部ポート。</param>
        /// <param name="deviceExternalIpAddress">デバイスが答えた外部 IP。</param>
        /// <param name="deviceProtocolName">デバイスのプロトコル名。</param>
        public static PortMappingResult Ok(
            ushort internalPort,
            ushort externalPort,
            string deviceExternalIpAddress,
            string deviceProtocolName)
            => new PortMappingResult(
                PortMappingStatus.Success,
                internalPort,
                externalPort,
                deviceExternalIpAddress,
                deviceProtocolName,
                $"{deviceProtocolName} でポート {externalPort}/UDP を開きました。");

        /// <summary>失敗結果を作る。</summary>
        /// <param name="status">失敗種別。</param>
        /// <param name="internalPort">要求した内部ポート。</param>
        /// <param name="message">UI に出す理由。</param>
        /// <param name="deviceProtocolName">応答したデバイスのプロトコル名（不明なら空）。</param>
        public static PortMappingResult Fail(
            PortMappingStatus status,
            ushort internalPort,
            string message,
            string deviceProtocolName = null)
            => new PortMappingResult(status, internalPort, 0, string.Empty, deviceProtocolName, message);
    }
}
