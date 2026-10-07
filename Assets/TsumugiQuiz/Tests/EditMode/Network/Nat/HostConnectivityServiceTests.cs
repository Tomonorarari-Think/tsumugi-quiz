using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Network.Nat;
using TsumugiQuiz.Tests.Shared.Network.Nat;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.Network.Nat
{
    /// <summary>
    /// ポート開放・グローバル IP・LAN IP を束ねた <see cref="HostConnectivityService"/> と、
    /// #5 の HostSetup 画面へ渡す <see cref="HostAddressInfo"/> の内容（docs/network.md §2.1 の 4〜6）。
    /// </summary>
    public class HostConnectivityServiceTests
    {
        private const string DefaultUrl = "https://api.ipify.org";

        private static NatOptions ShortTimeoutOptions => NatOptions.Default.With(discoveryTimeoutMs: 1000);

        [UnityTest]
        public IEnumerator ResolveAsync_OnFullSuccess_ProducesInternetAndLanCodeInputs()
        {
            var device = new FakeNatDevice { AssignedPublicPort = 41234, ExternalIpAddress = "203.0.113.7" };
            var client = new FakeIpLookupClient().WithBody(DefaultUrl, "203.0.113.7");

            using (var service = new HostConnectivityService(
                ShortTimeoutOptions,
                new FakeNatDiscovery(device),
                client,
                () => "192.168.1.23"))
            {
                yield return AsyncTest.Await(service.ResolveAsync(7777), info =>
                {
                    Assert.AreEqual(PortMappingStatus.Success, info.PortMapping.Status);
                    Assert.AreEqual(41234, info.ExternalPort, "ルーターが割り当てた外部ポートを使う。");
                    Assert.AreEqual(7777, info.InternalPort);
                    Assert.AreEqual("203.0.113.7", info.PublicIpAddress);
                    Assert.AreEqual("192.168.1.23", info.LanIpAddress);
                    Assert.IsTrue(info.CanCreateInternetCode);
                    Assert.IsTrue(info.CanCreateLanCode);
                    Assert.IsFalse(info.RequiresManualPortForwarding);
                    Assert.IsFalse(info.IsCarrierGradeNat);
                    Assert.IsEmpty(info.Warnings);
                });
            }
        }

        [UnityTest]
        public IEnumerator ResolveAsync_WhenMappingFails_ProducesManualGuide()
        {
            var client = new FakeIpLookupClient().WithBody(DefaultUrl, "203.0.113.7");

            using (var service = new HostConnectivityService(
                ShortTimeoutOptions,
                new FakeNatDiscovery(null),
                client,
                () => "192.168.1.23"))
            {
                yield return AsyncTest.Await(service.ResolveAsync(7777), info =>
                {
                    Assert.IsTrue(info.RequiresManualPortForwarding);
                    Assert.AreEqual(7777, info.ExternalPort, "手動開放では外部ポート = 内部ポートを案内する。");

                    var guide = info.ManualGuide;
                    Assert.AreEqual("UDP", guide.Protocol);
                    Assert.AreEqual(7777, guide.ExternalPort);
                    Assert.AreEqual(7777, guide.InternalPort);
                    Assert.AreEqual("192.168.1.23", guide.LanIpAddress);

                    // グローバル IP は取れているので、手動開放後はインターネット用コードを作れる。
                    Assert.IsTrue(info.CanCreateInternetCode);
                    Assert.IsTrue(info.CanCreateLanCode);
                    Assert.AreEqual(1, info.Warnings.Count);
                });
            }
        }

        [UnityTest]
        public IEnumerator ResolveAsync_WhenCarrierGradeNat_BlocksInternetCodeAndWarns()
        {
            var device = new FakeNatDevice { ExternalIpAddress = "100.100.7.7" };
            var client = new FakeIpLookupClient().WithFailure(DefaultUrl, "到達できません");

            using (var service = new HostConnectivityService(
                ShortTimeoutOptions,
                new FakeNatDiscovery(device),
                client,
                () => "192.168.1.23"))
            {
                yield return AsyncTest.Await(service.ResolveAsync(7777), info =>
                {
                    Assert.IsTrue(info.IsCarrierGradeNat);
                    Assert.IsFalse(info.CanCreateInternetCode, "CGNAT ではインターネット用コードを作らない。");
                    Assert.IsTrue(info.CanCreateLanCode, "LAN 用コードは作れる。");
                    StringAssert.Contains("CGNAT", info.Warnings[0]);
                    StringAssert.Contains("Tailscale", info.Warnings[0]);
                });
            }
        }

        [UnityTest]
        public IEnumerator ResolveAsync_WhenNoLanIp_StillProducesGuideWithoutAddress()
        {
            var client = new FakeIpLookupClient().WithFailure(DefaultUrl, "到達できません");

            using (var service = new HostConnectivityService(
                ShortTimeoutOptions,
                new FakeNatDiscovery(null),
                client,
                () => string.Empty))
            {
                yield return AsyncTest.Await(service.ResolveAsync(7777), info =>
                {
                    Assert.IsFalse(info.CanCreateLanCode);
                    Assert.IsFalse(info.CanCreateInternetCode);
                    Assert.IsFalse(info.ManualGuide.HasLanIpAddress);
                    Assert.AreEqual(2, info.Warnings.Count, "ポート開放失敗とグローバル IP 未取得の 2 件。");
                });
            }
        }

        [UnityTest]
        public IEnumerator ResolveAsync_WhenLanIpProviderThrows_DoesNotFail()
        {
            var client = new FakeIpLookupClient().WithBody(DefaultUrl, "203.0.113.7");

            using (var service = new HostConnectivityService(
                ShortTimeoutOptions,
                new FakeNatDiscovery(new FakeNatDevice()),
                client,
                () => throw new System.InvalidOperationException("テスト用")))
            {
                yield return AsyncTest.Await(service.ResolveAsync(7777), info =>
                {
                    Assert.AreEqual(string.Empty, info.LanIpAddress);
                    Assert.IsTrue(info.PortMapping.Success);
                });
            }
        }

        [UnityTest]
        public IEnumerator ApplyManualPublicIpAddress_AcceptsTailscaleAddressWithoutCarrierGradeNatWarning()
        {
            // docs/network-nat.md §1.5 の注記: 手入力された IP には CGNAT 判定を適用しない。
            var client = new FakeIpLookupClient().WithFailure(DefaultUrl, "到達できません");

            using (var service = new HostConnectivityService(
                ShortTimeoutOptions,
                new FakeNatDiscovery(null),
                client,
                () => "192.168.1.23"))
            {
                yield return AsyncTest.Await(service.ResolveAsync(7777), info => Assert.IsFalse(info.CanCreateInternetCode));

                var updated = service.ApplyManualPublicIpAddress("100.101.102.103");

                Assert.AreEqual("100.101.102.103", updated.PublicIpAddress);
                Assert.IsFalse(updated.IsCarrierGradeNat, "手入力（Tailscale IP）には CGNAT 判定を適用しない。");
                Assert.IsTrue(updated.CanCreateInternetCode);
            }
        }

        [UnityTest]
        public IEnumerator ApplyManualPublicIpAddress_IgnoresUnreadableInput()
        {
            var client = new FakeIpLookupClient().WithFailure(DefaultUrl, "到達できません");

            using (var service = new HostConnectivityService(
                ShortTimeoutOptions,
                new FakeNatDiscovery(null),
                client,
                () => "192.168.1.23"))
            {
                yield return AsyncTest.Await(service.ResolveAsync(7777), _ => { });

                var updated = service.ApplyManualPublicIpAddress("これはIPではない");

                Assert.AreEqual(string.Empty, updated.ManualPublicIpAddress);
                Assert.IsFalse(updated.CanCreateInternetCode);
            }
        }

        [UnityTest]
        public IEnumerator ApplyManualPublicIpAddress_RejectsUnreachableRanges()
        {
            var client = new FakeIpLookupClient().WithFailure(DefaultUrl, "到達できません");

            using (var service = new HostConnectivityService(
                ShortTimeoutOptions,
                new FakeNatDiscovery(null),
                client,
                () => "192.168.1.23"))
            {
                yield return AsyncTest.Await(service.ResolveAsync(7777), _ => { });

                // 手入力でもプライベート・ループバック・予約帯は「インターネット用の IP」にならない。
                foreach (var rejected in new[] { "192.168.1.23", "10.1.2.3", "172.16.0.1", "127.0.0.1", "169.254.1.1", "0.0.0.0", "255.255.255.255", "224.0.0.1" })
                {
                    var updated = service.ApplyManualPublicIpAddress(rejected);
                    Assert.AreEqual(string.Empty, updated.ManualPublicIpAddress, rejected);
                    Assert.IsFalse(updated.CanCreateInternetCode, rejected);
                }

                // グローバルと CGNAT（Tailscale）は受け付ける。
                Assert.AreEqual("203.0.113.7", service.ApplyManualPublicIpAddress("203.0.113.7").ManualPublicIpAddress);
                Assert.AreEqual("100.64.0.1", service.ApplyManualPublicIpAddress("100.64.0.1").ManualPublicIpAddress);
            }
        }

        [UnityTest]
        public IEnumerator Current_BeforeResolve_IsSafeToRead()
        {
            var client = new FakeIpLookupClient();

            using (var service = new HostConnectivityService(
                ShortTimeoutOptions,
                new FakeNatDiscovery(null),
                client,
                () => string.Empty))
            {
                var current = service.Current;

                Assert.AreEqual(string.Empty, current.PublicIpAddress);
                Assert.AreEqual(string.Empty, current.LanIpAddress);
                Assert.AreEqual(0, current.InternalPort);
                Assert.IsFalse(current.CanCreateInternetCode);
                Assert.IsFalse(service.IsJoinCodeOutdated);
                Assert.AreEqual(2, current.Warnings.Count);

                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator MappingRenewal_WithNewExternalPort_RaisesJoinCodeInvalidated()
        {
            var device = new FakeNatDevice { AssignedPublicPort = 41234 };
            var client = new FakeIpLookupClient().WithBody(DefaultUrl, "203.0.113.7");

            using (var service = new HostConnectivityService(
                ShortTimeoutOptions,
                new FakeNatDiscovery(device),
                client,
                () => "192.168.1.23"))
            {
                var addressChanges = 0;
                var invalidations = new List<string>();
                service.AddressChanged += _ => addressChanges++;
                service.JoinCodeInvalidated += invalidations.Add;

                yield return AsyncTest.Await(service.ResolveAsync(7777), info => Assert.AreEqual(41234, info.ExternalPort));

                Assert.AreEqual(1, addressChanges, "解決完了で 1 回通知する。");
                Assert.IsEmpty(invalidations);
                Assert.IsFalse(service.IsJoinCodeOutdated);

                // 更新でルーターが別の外部ポートを割り当てた → 配布済みの参加コードは使えない。
                device.AssignedPublicPort = 51234;
                yield return AsyncTest.Await(service.PortMapping.RenewAsync(), result => Assert.IsTrue(result.Success, result.Message));

                Assert.AreEqual(51234, service.Current.ExternalPort, "Current が差し替わる。");
                Assert.AreEqual(2, addressChanges);
                Assert.AreEqual(1, invalidations.Count);
                StringAssert.Contains("41234", invalidations[0]);
                StringAssert.Contains("51234", invalidations[0]);
                Assert.IsTrue(service.IsJoinCodeOutdated);

                service.AcknowledgeJoinCodeChange();
                Assert.IsFalse(service.IsJoinCodeOutdated);
            }
        }

        [UnityTest]
        public IEnumerator MappingRenewal_WithSamePort_DoesNotInvalidateJoinCode()
        {
            var device = new FakeNatDevice();
            var client = new FakeIpLookupClient().WithBody(DefaultUrl, "203.0.113.7");

            using (var service = new HostConnectivityService(
                ShortTimeoutOptions,
                new FakeNatDiscovery(device),
                client,
                () => "192.168.1.23"))
            {
                var invalidations = 0;
                service.JoinCodeInvalidated += _ => invalidations++;

                yield return AsyncTest.Await(service.ResolveAsync(7777), info => Assert.AreEqual(7777, info.ExternalPort));
                yield return AsyncTest.Await(service.PortMapping.RenewAsync(), result => Assert.IsTrue(result.Success, result.Message));

                Assert.AreEqual(0, invalidations);
                Assert.IsFalse(service.IsJoinCodeOutdated);
                Assert.AreEqual(7777, service.Current.ExternalPort);
            }
        }

        [UnityTest]
        public IEnumerator MappingRenewal_WhenItFails_InvalidatesJoinCode()
        {
            var device = new FakeNatDevice { AssignedPublicPort = 41234 };
            var client = new FakeIpLookupClient().WithBody(DefaultUrl, "203.0.113.7");

            using (var service = new HostConnectivityService(
                ShortTimeoutOptions,
                new FakeNatDiscovery(device),
                client,
                () => "192.168.1.23"))
            {
                var invalidations = new List<string>();
                service.JoinCodeInvalidated += invalidations.Add;

                yield return AsyncTest.Await(service.ResolveAsync(7777), info => Assert.AreEqual(41234, info.ExternalPort));

                device.CreateException = new NatDeviceException(NatFailureKind.Refused, "拒否");
                yield return AsyncTest.Await(service.PortMapping.RenewAsync(), result => Assert.IsFalse(result.Success));

                // 開放が失敗した状態なので、外部ポートは内部ポートに戻り、手動開放案内へ落ちる。
                Assert.AreEqual(7777, service.Current.ExternalPort);
                Assert.IsTrue(service.Current.RequiresManualPortForwarding);
                Assert.AreEqual(1, invalidations.Count);
                Assert.IsTrue(service.IsJoinCodeOutdated);
            }
        }

        [UnityTest]
        public IEnumerator ReleaseAsync_DeletesMapping()
        {
            var device = new FakeNatDevice();
            var client = new FakeIpLookupClient().WithBody(DefaultUrl, "203.0.113.7");

            using (var service = new HostConnectivityService(
                ShortTimeoutOptions,
                new FakeNatDiscovery(device),
                client,
                () => "192.168.1.23"))
            {
                yield return AsyncTest.Await(service.ResolveAsync(7777), info => Assert.IsTrue(info.PortMapping.Success));

                yield return AsyncTest.Await(service.ReleaseAsync());

                Assert.AreEqual(1, device.DeletedMappings.Count);
                Assert.IsFalse(service.PortMapping.HasActiveMapping);
            }
        }
    }
}
