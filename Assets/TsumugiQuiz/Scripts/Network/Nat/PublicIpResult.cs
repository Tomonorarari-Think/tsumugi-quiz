using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// グローバル IP の取得結果（不変、docs/network-nat.md §2）。
    /// 取得できなかった場合も例外にせず、<see cref="Found"/> が false の結果を返す（段 3 の手入力へ）。
    /// </summary>
    public readonly struct PublicIpResult
    {
        // default(PublicIpResult) でも null 参照にならないよう、文字列はプロパティ側で畳む（H-2）。
        private readonly string _address;
        private readonly string _natDeviceAddress;
        private readonly string _lookupServiceAddress;
        private readonly string _message;

        private PublicIpResult(
            string address,
            PublicIpSource source,
            string natDeviceAddress,
            string lookupServiceAddress,
            string message)
        {
            _address = address;
            Source = source;
            _natDeviceAddress = natDeviceAddress;
            _lookupServiceAddress = lookupServiceAddress;
            _message = message;
        }

        /// <summary>採用したアドレス（取得できなければ空文字）。</summary>
        public string Address => _address ?? string.Empty;

        /// <summary>採用元。</summary>
        public PublicIpSource Source { get; }

        /// <summary>NAT デバイスが答えたアドレス（無ければ空文字）。二重 NAT 判定の材料。</summary>
        public string NatDeviceAddress => _natDeviceAddress ?? string.Empty;

        /// <summary>IP 確認サービスが答えたアドレス（無ければ空文字）。</summary>
        public string LookupServiceAddress => _lookupServiceAddress ?? string.Empty;

        /// <summary>UI / ログ用の説明。</summary>
        public string Message => _message ?? string.Empty;

        /// <summary>アドレスを取得できたか。</summary>
        public bool Found => Source != PublicIpSource.None && Address.Length > 0;

        /// <summary>採用したアドレスの分類。</summary>
        public IpAddressCategory Category => IpRangeClassifier.Classify(Address);

        /// <summary>インターネット越しの直接接続に使えるアドレスか。</summary>
        public bool IsGloballyRoutable => Category == IpAddressCategory.Public;

        /// <summary>
        /// CGNAT 配下か（docs/network-nat.md §1.6）。
        /// 採用したアドレスと NAT デバイスが答えたアドレスのどちらかが CGNAT 帯なら true。
        /// ルーターの WAN 側が 100.64.0.0/10 のときは、確認サービスが真のグローバル IP を返しても
        /// 外部からは到達できないため、両方を見る必要がある。
        /// </summary>
        public bool IsCarrierGradeNat
            => Category == IpAddressCategory.CarrierGradeNat
               || IpRangeClassifier.Classify(NatDeviceAddress) == IpAddressCategory.CarrierGradeNat;

        /// <summary>
        /// NAT デバイスと確認サービスの答えが食い違っているか（二重 NAT の疑い）。
        /// 両方を取得できた場合にのみ判定する。
        /// </summary>
        public bool AddressesDisagree
            => NatDeviceAddress.Length > 0
               && LookupServiceAddress.Length > 0
               && NatDeviceAddress != LookupServiceAddress;

        /// <summary>
        /// まだ取得を試みていない状態。<c>default(PublicIpResult)</c> と等価だが、意図を明示するために使う。
        /// </summary>
        public static PublicIpResult NotResolved { get; } = new PublicIpResult(
            string.Empty, PublicIpSource.None, string.Empty, string.Empty, string.Empty);

        /// <summary>取得できなかった結果を作る。</summary>
        /// <param name="natDeviceAddress">NAT デバイスが答えた値（検証に落ちた値も残す）。</param>
        /// <param name="message">理由。</param>
        public static PublicIpResult NotFound(string natDeviceAddress, string message)
            => new PublicIpResult(string.Empty, PublicIpSource.None, natDeviceAddress, string.Empty, message);

        /// <summary>取得できた結果を作る。</summary>
        /// <param name="address">採用したアドレス。</param>
        /// <param name="source">採用元。</param>
        /// <param name="natDeviceAddress">NAT デバイスが答えたアドレス。</param>
        /// <param name="lookupServiceAddress">確認サービスが答えたアドレス。</param>
        /// <param name="message">説明。</param>
        public static PublicIpResult Resolved(
            string address,
            PublicIpSource source,
            string natDeviceAddress,
            string lookupServiceAddress,
            string message)
            => new PublicIpResult(address, source, natDeviceAddress, lookupServiceAddress, message);
    }
}
