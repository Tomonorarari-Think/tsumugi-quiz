using NUnit.Framework;
using TsumugiQuiz.Network.Nat;

namespace TsumugiQuiz.Tests.EditMode.Network.Nat
{
    /// <summary>
    /// UPnP / グローバル IP 取得の設定（docs/room-settings.md §2 の <c>upnp.*</c> / <c>network.ipLookupUrls</c>）。
    /// </summary>
    public class NatOptionsTests
    {
        [Test]
        public void Default_MatchesDocumentedValues()
        {
            var options = NatOptions.Default;

            Assert.IsTrue(options.Enabled);
            Assert.AreEqual(5000, options.DiscoveryTimeoutMs);
            Assert.AreEqual(3600, options.MappingLifetimeSec);
            Assert.AreEqual(1800000, options.RenewIntervalMs);
            CollectionAssert.AreEqual(new[] { "https://api.ipify.org" }, options.IpLookupUrls);
            Assert.AreEqual("TsumugiQuiz", NatOptions.MappingDescription);
            Assert.AreEqual(5000, NatOptions.IpLookupTimeoutMs);
            Assert.AreEqual(64, NatOptions.IpLookupMaxResponseBytes);
        }

        [Test]
        public void With_DoesNotMutateOriginal()
        {
            var original = NatOptions.Default;
            var changed = original.With(enabled: false, discoveryTimeoutMs: 12000);

            Assert.IsTrue(original.Enabled, "元のインスタンスは変更しない。");
            Assert.AreEqual(5000, original.DiscoveryTimeoutMs);
            Assert.IsFalse(changed.Enabled);
            Assert.AreEqual(12000, changed.DiscoveryTimeoutMs);
            Assert.AreEqual(original.MappingLifetimeSec, changed.MappingLifetimeSec);
        }

        [TestCase(0, 1000)]
        [TestCase(999, 1000)]
        [TestCase(1000, 1000)]
        [TestCase(30000, 30000)]
        [TestCase(999999, 30000)]
        public void With_ClampsDiscoveryTimeoutToDocumentedRange(int input, int expected)
        {
            Assert.AreEqual(expected, NatOptions.Default.With(discoveryTimeoutMs: input).DiscoveryTimeoutMs);
        }

        [TestCase(-1, 0)]
        [TestCase(0, 0)]
        [TestCase(86400, 86400)]
        [TestCase(100000, 86400)]
        public void With_ClampsMappingLifetimeToDocumentedRange(int input, int expected)
        {
            Assert.AreEqual(expected, NatOptions.Default.With(mappingLifetimeSec: input).MappingLifetimeSec);
        }

        [TestCase(0, 10000)]
        [TestCase(60000, 60000)]
        public void With_KeepsRenewIntervalAboveLowerBound(int input, int expected)
        {
            Assert.AreEqual(expected, NatOptions.Default.With(renewIntervalMs: input).RenewIntervalMs);
        }

        [Test]
        public void With_DefaultRenewIntervalIsExactlyHalfOfDefaultLifetime()
        {
            // 既定値（lifetime 3600 秒 / 更新 30 分）はちょうど上限に一致する。
            Assert.AreEqual(
                NatOptions.DefaultMappingLifetimeSec * 1000 / 2,
                NatOptions.Default.RenewIntervalMs);
        }

        [TestCase(3600, 1800000, 1800000)]     // 既定。上限ちょうど
        [TestCase(3600, 3600000, 1800000)]     // lifetime を超える指定は半分に丸める
        [TestCase(600, 1800000, 300000)]       // 短い lifetime に合わせて縮む
        [TestCase(0, 1800000, 1800000)]        // lifetime 0（無期限）は丸めない
        [TestCase(10, 1800000, 10000)]         // 極端に短くても下限（10 秒）は割らない
        public void ClampRenewInterval_KeepsIntervalWithinHalfOfLifetime(int lifetimeSec, int renewIntervalMs, int expected)
        {
            Assert.AreEqual(expected, NatOptions.ClampRenewInterval(renewIntervalMs, lifetimeSec));
        }

        [Test]
        public void With_ClampsRenewIntervalAgainstTheNewLifetime()
        {
            // lifetime と更新間隔を同時に渡した場合も、新しい lifetime を基準に丸める。
            var options = NatOptions.Default.With(mappingLifetimeSec: 600, renewIntervalMs: 1800000);

            Assert.AreEqual(600, options.MappingLifetimeSec);
            Assert.AreEqual(300000, options.RenewIntervalMs);
        }

        [Test]
        public void DefaultIpLookupUrls_IsReadOnly()
        {
            Assert.Throws<System.NotSupportedException>(
                () => ((System.Collections.Generic.IList<string>)NatOptions.DefaultIpLookupUrls).Add("https://evil.example"));
        }

        [Test]
        public void With_DropsNonHttpsLookupUrls()
        {
            var options = NatOptions.Default.With(ipLookupUrls: new[]
            {
                "http://api.ipify.org",       // 平文 HTTP は落とす
                "not a url",
                " https://ifconfig.me/ip ",   // 前後の空白は取る
                null,
            });

            CollectionAssert.AreEqual(new[] { "https://ifconfig.me/ip" }, options.IpLookupUrls);
        }

        [Test]
        public void With_FallsBackToDefaultWhenNoUrlSurvives()
        {
            var options = NatOptions.Default.With(ipLookupUrls: new[] { "http://example.com", "" });

            CollectionAssert.AreEqual(NatOptions.DefaultIpLookupUrls, options.IpLookupUrls);
        }

        [Test]
        public void With_NullUrlsKeepsCurrentValue()
        {
            var options = NatOptions.Default.With(ipLookupUrls: new[] { "https://a.example" }).With(ipLookupUrls: null);

            CollectionAssert.AreEqual(new[] { "https://a.example" }, options.IpLookupUrls);
        }

        [TestCase("https://api.ipify.org", true)]
        [TestCase("http://api.ipify.org", false)]
        [TestCase("ftp://api.ipify.org", false)]
        [TestCase("api.ipify.org", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsAllowedLookupUrl_AcceptsHttpsAbsoluteUrlsOnly(string url, bool expected)
        {
            Assert.AreEqual(expected, NatOptions.IsAllowedLookupUrl(url), url ?? "(null)");
        }
    }
}
