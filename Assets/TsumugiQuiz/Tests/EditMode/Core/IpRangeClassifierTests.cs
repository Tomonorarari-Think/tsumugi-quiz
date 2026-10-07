using NUnit.Framework;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// IPv4 アドレスの厳密な解析と到達可能性の分類（docs/network-nat.md §1.6 の表）。
    /// 各範囲の **境界値**（先頭・末尾とその 1 つ外側）を網羅する。
    /// </summary>
    public class IpRangeClassifierTests
    {
        // ---- 書式の検証 ----

        [TestCase("0.0.0.0", 0x00000000u)]
        [TestCase("1.2.3.4", 0x01020304u)]
        [TestCase("192.168.0.1", 0xC0A80001u)]
        [TestCase("255.255.255.255", 0xFFFFFFFFu)]
        [TestCase("  203.0.113.7  ", 0xCB007107u)]
        public void TryParseIpv4_AcceptsCanonicalForm(string text, uint expected)
        {
            Assert.IsTrue(IpRangeClassifier.TryParseIpv4(text, out var address), text);
            Assert.AreEqual(expected, address);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("1.2.3")]
        [TestCase("1.2.3.4.5")]
        [TestCase("1.2.3.")]
        [TestCase(".1.2.3")]
        [TestCase("1.2..4")]
        [TestCase("256.0.0.1")]
        [TestCase("1.2.3.256")]
        [TestCase("1.2.3.1000")]
        [TestCase("01.2.3.4")]        // 先頭 0 は 8 進数と誤読される余地があるため拒否
        [TestCase("1.2.3.04")]
        [TestCase("1.2.3.-4")]
        [TestCase("1.2.3.+4")]
        [TestCase("0x7f.0.0.1")]
        [TestCase("2001:db8::1")]     // IPv6 は対象外
        [TestCase("192.168.0.1:7777")]
        [TestCase("localhost")]
        public void TryParseIpv4_RejectsMalformedText(string text)
        {
            Assert.IsFalse(IpRangeClassifier.TryParseIpv4(text, out _), text);
            Assert.AreEqual(IpAddressCategory.Invalid, IpRangeClassifier.Classify(text));
        }

        // ---- CGNAT: 100.64.0.0/10 = 100.64.0.0 〜 100.127.255.255 ----

        [TestCase("100.64.0.0")]
        [TestCase("100.100.50.1")]
        [TestCase("100.127.255.255")]
        public void Classify_DetectsCarrierGradeNatRange(string text)
        {
            Assert.AreEqual(IpAddressCategory.CarrierGradeNat, IpRangeClassifier.Classify(text), text);
            Assert.IsTrue(IpRangeClassifier.IsCarrierGradeNat(text));
            Assert.IsFalse(IpRangeClassifier.IsGloballyRoutable(text));
        }

        [TestCase("100.63.255.255")]
        [TestCase("100.128.0.0")]
        public void Classify_TreatsAddressesOutsideCarrierGradeNatRangeAsPublic(string text)
        {
            Assert.AreEqual(IpAddressCategory.Public, IpRangeClassifier.Classify(text), text);
            Assert.IsFalse(IpRangeClassifier.IsCarrierGradeNat(text));
        }

        // ---- プライベート: 10/8, 172.16/12, 192.168/16 ----

        [TestCase("10.0.0.0")]
        [TestCase("10.255.255.255")]
        [TestCase("172.16.0.0")]
        [TestCase("172.31.255.255")]
        [TestCase("192.168.0.0")]
        [TestCase("192.168.255.255")]
        public void Classify_DetectsPrivateRanges(string text)
        {
            Assert.AreEqual(IpAddressCategory.Private, IpRangeClassifier.Classify(text), text);
            Assert.IsTrue(IpRangeClassifier.IsPrivate(text));
            Assert.IsFalse(IpRangeClassifier.IsGloballyRoutable(text));
        }

        [TestCase("9.255.255.255")]
        [TestCase("11.0.0.0")]
        [TestCase("172.15.255.255")]
        [TestCase("172.32.0.0")]
        [TestCase("192.167.255.255")]
        [TestCase("192.169.0.0")]
        public void Classify_TreatsAddressesOutsidePrivateRangesAsPublic(string text)
        {
            Assert.AreEqual(IpAddressCategory.Public, IpRangeClassifier.Classify(text), text);
            Assert.IsFalse(IpRangeClassifier.IsPrivate(text));
        }

        // ---- ループバック・リンクローカル・予約・マルチキャスト ----

        [TestCase("127.0.0.0", IpAddressCategory.Loopback)]
        [TestCase("127.0.0.1", IpAddressCategory.Loopback)]
        [TestCase("127.255.255.255", IpAddressCategory.Loopback)]
        [TestCase("126.255.255.255", IpAddressCategory.Public)]
        [TestCase("128.0.0.0", IpAddressCategory.Public)]
        [TestCase("169.254.0.0", IpAddressCategory.LinkLocal)]
        [TestCase("169.254.255.255", IpAddressCategory.LinkLocal)]
        [TestCase("169.253.255.255", IpAddressCategory.Public)]
        [TestCase("169.255.0.0", IpAddressCategory.Public)]
        [TestCase("0.0.0.0", IpAddressCategory.Unspecified)]
        [TestCase("0.0.0.1", IpAddressCategory.Reserved)]
        [TestCase("0.255.255.255", IpAddressCategory.Reserved)]
        [TestCase("1.0.0.0", IpAddressCategory.Public)]
        [TestCase("224.0.0.0", IpAddressCategory.Multicast)]
        [TestCase("239.255.255.255", IpAddressCategory.Multicast)]
        [TestCase("223.255.255.255", IpAddressCategory.Public)]
        [TestCase("240.0.0.0", IpAddressCategory.Reserved)]
        [TestCase("255.255.255.254", IpAddressCategory.Reserved)]
        [TestCase("255.255.255.255", IpAddressCategory.Broadcast)]
        public void Classify_MatchesDocumentedTable(string text, IpAddressCategory expected)
        {
            Assert.AreEqual(expected, IpRangeClassifier.Classify(text), text);
        }

        // ---- 到達可能性 ----

        [TestCase("203.0.113.7")]
        [TestCase("8.8.8.8")]
        [TestCase("1.1.1.1")]
        public void IsGloballyRoutable_AcceptsPublicAddresses(string text)
        {
            Assert.IsTrue(IpRangeClassifier.IsGloballyRoutable(text), text);
        }

        [TestCase("192.168.1.23")]
        [TestCase("100.100.0.1")]
        [TestCase("127.0.0.1")]
        [TestCase("169.254.1.1")]
        [TestCase("0.0.0.0")]
        [TestCase("invalid")]
        public void IsGloballyRoutable_RejectsUnreachableAddresses(string text)
        {
            Assert.IsFalse(IpRangeClassifier.IsGloballyRoutable(text), text);
        }

        [Test]
        public void Describe_ReturnsNonEmptyTextForEveryCategory()
        {
            foreach (IpAddressCategory category in System.Enum.GetValues(typeof(IpAddressCategory)))
            {
                Assert.IsNotEmpty(IpRangeClassifier.Describe(category), category.ToString());
            }
        }
    }
}
