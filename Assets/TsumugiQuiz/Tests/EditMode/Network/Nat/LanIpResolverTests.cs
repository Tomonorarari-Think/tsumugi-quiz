using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network.Nat;
using UnityEngine;

namespace TsumugiQuiz.Tests.EditMode.Network.Nat
{
    /// <summary>
    /// LAN IPv4 の列挙と選択。実行環境のネットワーク構成に依存するため、
    /// 「返る値が必ずプライベート帯である」ことと候補の整合性を検証し、実測値はログに出す。
    /// 実 NIC を列挙するテストには <c>[Category("Network")]</c> を付け、
    /// <c>scripts/verify.ps1</c> の既定（<c>-testCategory "!Network"</c>）では除外する。
    /// </summary>
    public class LanIpResolverTests
    {
        [TestCase("192.168.1.23", true)]
        [TestCase("10.0.0.5", true)]
        [TestCase("172.16.0.1", true)]
        [TestCase("100.101.102.103", false)]   // CGNAT / Tailscale は LAN コードに使わない
        [TestCase("127.0.0.1", false)]
        [TestCase("169.254.1.1", false)]
        [TestCase("203.0.113.7", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsUsableLanAddress_AcceptsPrivateRangesOnly(string address, bool expected)
        {
            Assert.AreEqual(expected, LanIpResolver.IsUsableLanAddress(address), address ?? "(null)");
        }

        [Test]
        [Category("Network")]
        public void Resolve_ReturnsPrivateAddressOrEmpty()
        {
            var address = LanIpResolver.Resolve();

            Debug.Log($"[LanIpResolverTests] Resolve() = '{address}'");

            if (address.Length == 0)
            {
                Assert.Pass("この環境では LAN IP を判定できなかった（ネットワーク未接続などでも成立する）。");
            }

            Assert.AreEqual(IpAddressCategory.Private, IpRangeClassifier.Classify(address));
        }

        [Test]
        [Category("Network")]
        public void EnumerateCandidates_ReturnsOnlyPrivateAddressesWithoutDuplicates()
        {
            var candidates = LanIpResolver.EnumerateCandidates();

            Debug.Log($"[LanIpResolverTests] EnumerateCandidates() = [{string.Join(", ", candidates)}]");

            CollectionAssert.AllItemsAreUnique(candidates);
            foreach (var candidate in candidates)
            {
                Assert.AreEqual(IpAddressCategory.Private, IpRangeClassifier.Classify(candidate), candidate);
            }
        }

        [Test]
        [Category("Network")]
        public void TryResolveViaDefaultRoute_ReturnsPrivateAddressOrEmpty()
        {
            var address = LanIpResolver.TryResolveViaDefaultRoute();

            Debug.Log($"[LanIpResolverTests] TryResolveViaDefaultRoute() = '{address}'");

            if (address.Length > 0)
            {
                Assert.AreEqual(IpAddressCategory.Private, IpRangeClassifier.Classify(address));
            }
        }
    }
}
