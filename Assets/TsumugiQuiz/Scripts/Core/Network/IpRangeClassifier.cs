using System;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// IPv4 アドレス文字列の厳密な解析と、到達可能性の分類（docs/network-nat.md §1.6）。
    /// UPnP / IP 確認サービスから受け取った外部入力を検証するために使う純 C# 実装で、
    /// Unity API にも <c>System.Net.IPAddress</c> の実装差にも依存しない。
    ///
    /// <c>IPAddress.Parse</c> は実行環境によって <c>"10.1"</c> のような省略記法や 8 進数解釈
    /// （先頭 0）を受け付けることがあり、範囲判定の前段としては危険なため、ここでは
    /// ドット区切り 4 組・各組 1〜3 桁・先頭 0 禁止の正準形だけを受け付ける。
    /// </summary>
    public static class IpRangeClassifier
    {
        /// <summary>IPv4 アドレスの区切り文字数（ドットは 3 個）。</summary>
        private const int OctetCount = 4;

        /// <summary>
        /// IPv4 のドット 10 進表記を厳密に解析する。
        /// 前後の空白は取り除くが、それ以外の省略記法・先頭 0・16 進表記は受け付けない。
        /// </summary>
        /// <param name="text">解析する文字列。</param>
        /// <param name="address">解析結果（ネットワークバイトオーダーを big-endian の <see cref="uint"/> として表現）。</param>
        /// <returns>解析できたら true。</returns>
        public static bool TryParseIpv4(string text, out uint address)
        {
            address = 0;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            uint value = 0;
            var octetIndex = 0;
            var digitCount = 0;
            var octet = 0;
            var hasLeadingZero = false;

            for (var i = 0; i < trimmed.Length; i++)
            {
                var c = trimmed[i];

                if (c == '.')
                {
                    if (digitCount == 0 || hasLeadingZero || octetIndex >= OctetCount - 1)
                    {
                        return false;
                    }

                    value = (value << 8) | (uint)octet;
                    octetIndex++;
                    digitCount = 0;
                    octet = 0;
                    continue;
                }

                if (c < '0' || c > '9')
                {
                    return false;
                }

                // 先頭 0（"01" など）は 8 進数と誤読される余地があるため受け付けない。
                if (digitCount == 1 && octet == 0)
                {
                    hasLeadingZero = true;
                }

                digitCount++;
                if (digitCount > 3)
                {
                    return false;
                }

                octet = (octet * 10) + (c - '0');
                if (octet > 255)
                {
                    return false;
                }
            }

            if (octetIndex != OctetCount - 1 || digitCount == 0 || hasLeadingZero)
            {
                return false;
            }

            address = (value << 8) | (uint)octet;
            return true;
        }

        /// <summary>
        /// 文字列を分類する。解析できない場合は <see cref="IpAddressCategory.Invalid"/>。
        /// </summary>
        /// <param name="text">IPv4 のドット 10 進表記。</param>
        public static IpAddressCategory Classify(string text)
            => TryParseIpv4(text, out var address) ? Classify(address) : IpAddressCategory.Invalid;

        /// <summary>
        /// 数値表現のアドレスを分類する（docs/network-nat.md §1.6 の表）。
        /// </summary>
        /// <param name="address">big-endian の <see cref="uint"/> として表した IPv4 アドレス。</param>
        public static IpAddressCategory Classify(uint address)
        {
            if (address == 0u)
            {
                return IpAddressCategory.Unspecified;
            }

            if (address == 0xFFFFFFFFu)
            {
                return IpAddressCategory.Broadcast;
            }

            // 0.0.0.0/8（"this network"）
            if (InRange(address, 0x00000000u, 8))
            {
                return IpAddressCategory.Reserved;
            }

            if (InRange(address, 0x0A000000u, 8)        // 10.0.0.0/8
                || InRange(address, 0xAC100000u, 12)    // 172.16.0.0/12
                || InRange(address, 0xC0A80000u, 16))   // 192.168.0.0/16
            {
                return IpAddressCategory.Private;
            }

            // 100.64.0.0/10（RFC 6598 CGNAT。Tailscale の 100.x も同帯）
            if (InRange(address, 0x64400000u, 10))
            {
                return IpAddressCategory.CarrierGradeNat;
            }

            if (InRange(address, 0x7F000000u, 8))       // 127.0.0.0/8
            {
                return IpAddressCategory.Loopback;
            }

            if (InRange(address, 0xA9FE0000u, 16))      // 169.254.0.0/16
            {
                return IpAddressCategory.LinkLocal;
            }

            if (InRange(address, 0xE0000000u, 4))       // 224.0.0.0/4
            {
                return IpAddressCategory.Multicast;
            }

            if (InRange(address, 0xF0000000u, 4))       // 240.0.0.0/4
            {
                return IpAddressCategory.Reserved;
            }

            return IpAddressCategory.Public;
        }

        /// <summary>
        /// インターネット越しの直接接続に使えるアドレスか
        /// （<see cref="IpAddressCategory.Public"/> だけが true）。
        /// </summary>
        /// <param name="text">IPv4 のドット 10 進表記。</param>
        public static bool IsGloballyRoutable(string text) => Classify(text) == IpAddressCategory.Public;

        /// <summary>
        /// CGNAT 帯（<c>100.64.0.0/10</c>）かどうか。
        /// 手入力されたアドレス（Tailscale IP）には適用しないこと（docs/network-nat.md §1.5 の注記）。
        /// </summary>
        /// <param name="text">IPv4 のドット 10 進表記。</param>
        public static bool IsCarrierGradeNat(string text) => Classify(text) == IpAddressCategory.CarrierGradeNat;

        /// <summary>
        /// RFC 1918 のプライベート帯かどうか。LAN 用コードの生成可否判定に使う。
        /// </summary>
        /// <param name="text">IPv4 のドット 10 進表記。</param>
        public static bool IsPrivate(string text) => Classify(text) == IpAddressCategory.Private;

        /// <summary>
        /// 分類に対応する日本語の説明。UI の警告文に使う。
        /// </summary>
        /// <param name="category">分類。</param>
        public static string Describe(IpAddressCategory category)
        {
            switch (category)
            {
                case IpAddressCategory.Invalid:
                    return "IPv4 アドレスとして読み取れません";
                case IpAddressCategory.Unspecified:
                    return "未設定のアドレス（0.0.0.0）です";
                case IpAddressCategory.Loopback:
                    return "ループバックアドレスです";
                case IpAddressCategory.Private:
                    return "プライベートアドレスです（インターネットからは到達できません）";
                case IpAddressCategory.CarrierGradeNat:
                    return "CGNAT（大規模 NAT）のアドレスです（インターネットからは到達できません）";
                case IpAddressCategory.LinkLocal:
                    return "リンクローカルアドレスです（ネットワーク設定を取得できていません）";
                case IpAddressCategory.Multicast:
                    return "マルチキャストアドレスです";
                case IpAddressCategory.Broadcast:
                    return "ブロードキャストアドレスです";
                case IpAddressCategory.Reserved:
                    return "予約されたアドレスです";
                case IpAddressCategory.Public:
                    return "グローバルアドレスです";
                default:
                    throw new ArgumentOutOfRangeException(nameof(category), category, "未知の分類です。");
            }
        }

        /// <summary>指定したアドレスが <paramref name="networkAddress"/>/<paramref name="prefixLength"/> に含まれるか。</summary>
        private static bool InRange(uint address, uint networkAddress, int prefixLength)
        {
            var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
            return (address & mask) == networkAddress;
        }
    }
}
