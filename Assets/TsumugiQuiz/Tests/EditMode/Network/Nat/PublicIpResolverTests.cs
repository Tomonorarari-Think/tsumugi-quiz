using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network.Nat;
using TsumugiQuiz.Tests.Shared.Network.Nat;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.Network.Nat
{
    /// <summary>
    /// グローバル IP の取得と応答の検証（docs/network-nat.md §2）。
    /// HTTP アクセスは <see cref="IIpLookupClient"/> を差し替えて実ネットワークに出ない。
    /// </summary>
    public class PublicIpResolverTests
    {
        private const string DefaultUrl = "https://api.ipify.org";

        // ---- 応答本文の検証（docs/network-nat.md §2「外部データを信用しない」）----

        [TestCase("203.0.113.7", "203.0.113.7")]
        [TestCase(" 203.0.113.7 ", "203.0.113.7")]
        [TestCase("203.0.113.7\n", "203.0.113.7")]
        [TestCase("100.64.0.1", "100.64.0.1")]  // CGNAT は「取得できた」として採用し、別途警告する
        public void TryValidateLookupBody_AcceptsGlobalIpv4(string body, string expected)
        {
            Assert.IsTrue(PublicIpResolver.TryValidateLookupBody(body, out var address), body);
            Assert.AreEqual(expected, address);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("    ")]
        [TestCase("192.168.1.23")]      // プライベートは採用しない
        [TestCase("10.0.0.1")]
        [TestCase("127.0.0.1")]
        [TestCase("169.254.1.1")]
        [TestCase("0.0.0.0")]
        [TestCase("255.255.255.255")]
        [TestCase("224.0.0.1")]
        [TestCase("2001:db8::1")]       // IPv6 は対象外
        [TestCase("not-an-ip")]
        [TestCase("<html><body>203.0.113.7</body></html>")]
        [TestCase("203.0.113.7 203.0.113.8")]
        public void TryValidateLookupBody_RejectsUnusableBody(string body)
        {
            Assert.IsFalse(PublicIpResolver.TryValidateLookupBody(body, out var address), body ?? "(null)");
            Assert.AreEqual(string.Empty, address);
        }

        [Test]
        public void TryValidateLookupBody_RejectsBodyLongerThanLimit()
        {
            var body = "203.0.113.7".PadRight(NatOptions.IpLookupMaxResponseBytes + 1, ' ');

            Assert.IsFalse(PublicIpResolver.TryValidateLookupBody(body, out _));
        }

        // ---- 3 段階のフォールバック ----

        [UnityTest]
        public IEnumerator ResolveAsync_UsesLookupServiceResult()
        {
            var client = new FakeIpLookupClient().WithBody(DefaultUrl, "203.0.113.7");
            var resolver = new PublicIpResolver(client, NatOptions.Default);

            yield return AsyncTest.Await(resolver.ResolveAsync(null), result =>
            {
                Assert.IsTrue(result.Found);
                Assert.AreEqual("203.0.113.7", result.Address);
                Assert.AreEqual(PublicIpSource.IpLookupService, result.Source);
                Assert.IsTrue(result.IsGloballyRoutable);
                Assert.IsFalse(result.IsCarrierGradeNat);
                Assert.IsFalse(result.AddressesDisagree);
            });

            CollectionAssert.AreEqual(new[] { DefaultUrl }, client.RequestedUrls);
        }

        [UnityTest]
        public IEnumerator ResolveAsync_TriesUrlsInOrderUntilOneSucceeds()
        {
            var options = NatOptions.Default.With(ipLookupUrls: new[]
            {
                "https://first.example",
                "https://second.example",
                "https://third.example",
            });

            var client = new FakeIpLookupClient()
                .WithFailure("https://first.example", "接続できません")
                .WithBody("https://second.example", "no-an-ip")
                .WithBody("https://third.example", "198.51.100.9");

            yield return AsyncTest.Await(new PublicIpResolver(client, options).ResolveAsync(null), result =>
            {
                Assert.AreEqual("198.51.100.9", result.Address);
                Assert.AreEqual(PublicIpSource.IpLookupService, result.Source);
            });

            CollectionAssert.AreEqual(
                new[] { "https://first.example", "https://second.example", "https://third.example" },
                client.RequestedUrls);
        }

        [UnityTest]
        public IEnumerator ResolveAsync_WhenClientThrows_ContinuesToNextUrl()
        {
            var options = NatOptions.Default.With(ipLookupUrls: new[] { "https://broken.example", "https://ok.example" });
            var client = new FakeIpLookupClient()
                .WithException("https://broken.example")
                .WithBody("https://ok.example", "198.51.100.9");

            yield return AsyncTest.Await(new PublicIpResolver(client, options).ResolveAsync(null), result =>
                Assert.AreEqual("198.51.100.9", result.Address));
        }

        [UnityTest]
        public IEnumerator ResolveAsync_FallsBackToNatDeviceAddressWhenLookupFails()
        {
            var client = new FakeIpLookupClient().WithFailure(DefaultUrl, "タイムアウト");

            yield return AsyncTest.Await(new PublicIpResolver(client, NatOptions.Default).ResolveAsync("203.0.113.7"), result =>
            {
                Assert.IsTrue(result.Found);
                Assert.AreEqual("203.0.113.7", result.Address);
                Assert.AreEqual(PublicIpSource.NatDevice, result.Source);
                Assert.IsFalse(result.AddressesDisagree, "確認サービスの結果が無いので比較しない。");
            });
        }

        [UnityTest]
        public IEnumerator ResolveAsync_WhenBothFail_ReturnsNotFoundForManualInput()
        {
            var client = new FakeIpLookupClient().WithFailure(DefaultUrl, "タイムアウト");

            yield return AsyncTest.Await(new PublicIpResolver(client, NatOptions.Default).ResolveAsync("192.168.1.1"), result =>
            {
                Assert.IsFalse(result.Found);
                Assert.AreEqual(PublicIpSource.None, result.Source);
                Assert.AreEqual(string.Empty, result.Address);
                Assert.AreEqual("192.168.1.1", result.NatDeviceAddress, "判定材料として値は残す。");
                Assert.IsNotEmpty(result.Message);
            });
        }

        [UnityTest]
        public IEnumerator ResolveAsync_PrefersLookupServiceWhenAddressesDisagree()
        {
            // 二重 NAT（docs/network-nat.md §1.6）。ipify 側が外から見える真のアドレス。
            var client = new FakeIpLookupClient().WithBody(DefaultUrl, "198.51.100.9");

            yield return AsyncTest.Await(new PublicIpResolver(client, NatOptions.Default).ResolveAsync("203.0.113.7"), result =>
            {
                Assert.AreEqual("198.51.100.9", result.Address);
                Assert.AreEqual(PublicIpSource.IpLookupService, result.Source);
                Assert.IsTrue(result.AddressesDisagree);
                Assert.AreEqual("203.0.113.7", result.NatDeviceAddress);
                Assert.AreEqual("198.51.100.9", result.LookupServiceAddress);
                StringAssert.Contains("一致しません", result.Message);
            });
        }

        [UnityTest]
        public IEnumerator ResolveAsync_DetectsCarrierGradeNatFromNatDeviceEvenWhenLookupSucceeds()
        {
            // ルーターの WAN 側が CGNAT。ipify はキャリアのグローバル IP を返すが外からは到達できない。
            var client = new FakeIpLookupClient().WithBody(DefaultUrl, "198.51.100.9");

            yield return AsyncTest.Await(new PublicIpResolver(client, NatOptions.Default).ResolveAsync("100.100.1.2"), result =>
            {
                Assert.IsTrue(result.Found);
                Assert.IsTrue(result.IsCarrierGradeNat, "ルーターの WAN 側が CGNAT なら到達できない。");
                Assert.IsTrue(result.AddressesDisagree);
            });
        }

        [UnityTest]
        public IEnumerator ResolveAsync_DetectsCarrierGradeNatFromLookupService()
        {
            var client = new FakeIpLookupClient().WithBody(DefaultUrl, "100.127.255.255");

            yield return AsyncTest.Await(new PublicIpResolver(client, NatOptions.Default).ResolveAsync(null), result =>
            {
                Assert.IsTrue(result.Found);
                Assert.AreEqual(IpAddressCategory.CarrierGradeNat, result.Category);
                Assert.IsTrue(result.IsCarrierGradeNat);
                Assert.IsFalse(result.IsGloballyRoutable);
            });
        }

        [UnityTest]
        public IEnumerator ResolveAsync_AgreementDoesNotWarn()
        {
            var client = new FakeIpLookupClient().WithBody(DefaultUrl, "203.0.113.7");

            yield return AsyncTest.Await(new PublicIpResolver(client, NatOptions.Default).ResolveAsync("203.0.113.7"), result =>
            {
                Assert.IsFalse(result.AddressesDisagree);
                StringAssert.DoesNotContain("一致しません", result.Message);
            });
        }
    }
}
