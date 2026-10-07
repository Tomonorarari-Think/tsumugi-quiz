using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TsumugiQuiz.Core.Network;
using UnityEngine;

namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// このPCの LAN IPv4 アドレスの列挙と選択。
    /// LAN 用参加コード（docs/network.md §2.1 の 6）と、手動ポート開放案内の「宛先 IP」
    /// （docs/network-nat.md §1.5）に使う。
    ///
    /// 選択方針:
    /// <list type="number">
    ///   <item>
    ///     既定ルートに紐づくアドレスを優先する。UDP ソケットを外部アドレスへ <c>Connect</c> して
    ///     <c>LocalEndPoint</c> を読む（UDP の Connect はパケットを送らないため通信は発生しない）。
    ///     複数 NIC（有線 + 無線 + 仮想）がある PC でも、実際に外へ出る経路の IP が得られる。
    ///   </item>
    ///   <item>
    ///     取れない場合は <see cref="NetworkInterface"/> を列挙し、稼働中・非ループバック・
    ///     ゲートウェイあり・プライベート帯のアドレスを優先順に並べて先頭を採る。
    ///   </item>
    /// </list>
    /// </summary>
    public static class LanIpResolver
    {
        /// <summary>
        /// 既定ルートの判定に使う宛先。ここへは接続もパケット送信も行わず、
        /// OS のルーティングテーブル参照のためだけに使う（Google Public DNS のアドレス）。
        /// </summary>
        private const string RouteProbeAddress = "8.8.8.8";

        /// <summary>ルート判定用のダミーポート。</summary>
        private const int RouteProbePort = 65530;

        /// <summary>
        /// 最も適切な LAN IPv4 を 1 つ返す。判定できなければ空文字。
        /// </summary>
        public static string Resolve()
        {
            var routed = TryResolveViaDefaultRoute();
            if (routed.Length > 0)
            {
                return routed;
            }

            var candidates = EnumerateCandidates();
            return candidates.Count > 0 ? candidates[0] : string.Empty;
        }

        /// <summary>
        /// 既定ルートに紐づく LAN IPv4 を返す。取得できなければ空文字。
        /// </summary>
        public static string TryResolveViaDefaultRoute()
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect(new IPEndPoint(IPAddress.Parse(RouteProbeAddress), RouteProbePort));
                    if (socket.LocalEndPoint is IPEndPoint localEndPoint
                        && localEndPoint.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var address = localEndPoint.Address.ToString();
                        return IsUsableLanAddress(address) ? address : string.Empty;
                    }
                }
            }
            catch (SocketException exception)
            {
                // ネットワーク未接続などでルートが引けない場合。列挙にフォールバックする。
                Debug.LogWarning($"[LanIpResolver] 既定ルートから LAN IP を判定できませんでした: {exception.SocketErrorCode}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LanIpResolver] 既定ルートから LAN IP を判定できませんでした: {exception.Message}");
            }

            return string.Empty;
        }

        /// <summary>
        /// 候補となる LAN IPv4 を優先度順に列挙する。
        /// 手動ポート開放案内で「どの IP か分からない」場合に一覧を出せるようにするため公開する。
        /// </summary>
        /// <returns>優先度の高い順に並べたアドレス。1 件も無ければ空のリスト。</returns>
        public static IReadOnlyList<string> EnumerateCandidates()
        {
            var scored = new List<KeyValuePair<int, string>>();

            NetworkInterface[] interfaces;
            try
            {
                interfaces = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (NetworkInformationException exception)
            {
                Debug.LogWarning($"[LanIpResolver] ネットワークインターフェースを列挙できませんでした: {exception.Message}");
                return Array.Empty<string>();
            }

            foreach (var networkInterface in interfaces)
            {
                if (!IsUsableInterface(networkInterface))
                {
                    continue;
                }

                IPInterfaceProperties properties;
                try
                {
                    properties = networkInterface.GetIPProperties();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[LanIpResolver] インターフェース {networkInterface.Name} の情報を取得できませんでした: {exception.Message}");
                    continue;
                }

                var hasGateway = HasIpv4Gateway(properties);

                foreach (var unicast in properties.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    var address = unicast.Address.ToString();
                    if (!IsUsableLanAddress(address))
                    {
                        continue;
                    }

                    scored.Add(new KeyValuePair<int, string>(Score(networkInterface, hasGateway), address));
                }
            }

            scored.Sort((left, right) =>
            {
                var byScore = right.Key.CompareTo(left.Key);
                return byScore != 0 ? byScore : string.CompareOrdinal(left.Value, right.Value);
            });

            var results = new List<string>(scored.Count);
            foreach (var entry in scored)
            {
                if (!results.Contains(entry.Value))
                {
                    results.Add(entry.Value);
                }
            }

            return results;
        }

        /// <summary>
        /// LAN 用の参加コードや手動ポート開放案内に載せられるアドレスか。
        /// プライベート帯（RFC 1918）だけを受け付ける（ループバック・リンクローカル・CGNAT は除く）。
        /// </summary>
        /// <param name="address">IPv4 のドット 10 進表記。</param>
        public static bool IsUsableLanAddress(string address)
            => IpRangeClassifier.Classify(address) == IpAddressCategory.Private;

        private static bool IsUsableInterface(NetworkInterface networkInterface)
            => networkInterface != null
               && networkInterface.OperationalStatus == OperationalStatus.Up
               && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback
               && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel;

        private static bool HasIpv4Gateway(IPInterfaceProperties properties)
        {
            foreach (var gateway in properties.GatewayAddresses)
            {
                if (gateway?.Address != null
                    && gateway.Address.AddressFamily == AddressFamily.InterNetwork
                    && !gateway.Address.Equals(IPAddress.Any))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 優先度。ゲートウェイを持つインターフェースを最優先し、次に有線・無線を優先する
        /// （Hyper-V / WSL / VPN の仮想アダプタを避けるため）。
        /// </summary>
        private static int Score(NetworkInterface networkInterface, bool hasGateway)
        {
            var score = hasGateway ? 100 : 0;

            switch (networkInterface.NetworkInterfaceType)
            {
                case NetworkInterfaceType.Ethernet:
                case NetworkInterfaceType.GigabitEthernet:
                case NetworkInterfaceType.FastEthernetT:
                case NetworkInterfaceType.FastEthernetFx:
                    score += 20;
                    break;
                case NetworkInterfaceType.Wireless80211:
                    score += 10;
                    break;
                default:
                    break;
            }

            return score;
        }
    }
}
