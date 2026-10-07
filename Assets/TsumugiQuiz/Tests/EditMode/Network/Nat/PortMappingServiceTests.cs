using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Network.Nat;
using TsumugiQuiz.Tests.Shared.Network.Nat;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.Network.Nat
{
    /// <summary>
    /// 自動ポート開放の状態遷移（docs/network-nat.md §1.3 / §1.4）。
    /// <see cref="INatDiscovery"/> / <see cref="INatDevice"/> を差し替えて、実ネットワークなしで検証する。
    /// </summary>
    public class PortMappingServiceTests
    {
        /// <summary>探索タイムアウトの下限（1000ms）を使い、タイムアウト系のテストを短く済ませる。</summary>
        private static NatOptions ShortTimeoutOptions => NatOptions.Default.With(discoveryTimeoutMs: 1000);

        [UnityTest]
        public IEnumerator MapAsync_WhenDisabled_ReturnsDisabledWithoutDiscovery()
        {
            var discovery = new FakeNatDiscovery(new FakeNatDevice());
            using (var service = new PortMappingService(discovery, NatOptions.Default.With(enabled: false)))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result =>
                {
                    Assert.AreEqual(PortMappingStatus.Disabled, result.Status);
                    Assert.IsFalse(result.Success);
                    Assert.AreEqual(0, result.ExternalPort);
                });

                Assert.AreEqual(0, discovery.CallCount, "無効なら探索しない。");
                Assert.IsFalse(service.HasActiveMapping);
            }
        }

        [UnityTest]
        public IEnumerator MapAsync_WhenDeviceRespondsImmediatelyMissing_ReturnsDeviceNotFound()
        {
            var discovery = new FakeNatDiscovery(null);
            using (var service = new PortMappingService(discovery, ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result =>
                {
                    Assert.AreEqual(PortMappingStatus.DeviceNotFound, result.Status);
                    Assert.AreEqual(7777, result.InternalPort);
                    Assert.IsNotEmpty(result.Message);
                });

                Assert.AreEqual(1, discovery.CallCount);
                Assert.AreEqual(1000, discovery.LastTimeoutMs, "upnp.discoveryTimeoutMs をそのまま渡す。");
                Assert.IsFalse(service.HasActiveMapping);
            }
        }

        [UnityTest]
        public IEnumerator MapAsync_WhenDiscoveryConsumesTimeout_ReturnsTimeout()
        {
            var discovery = new FakeNatDiscovery(null, consumeTimeout: true);
            using (var service = new PortMappingService(discovery, ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result =>
                {
                    Assert.AreEqual(PortMappingStatus.Timeout, result.Status);
                    StringAssert.Contains("1000ms", result.Message);
                });
            }
        }

        [UnityTest]
        public IEnumerator MapAsync_OnSuccess_UsesPortReturnedByRouter()
        {
            // ルーターが要求とは違う外部ポートを割り当てるケース（docs/network-nat.md §1.3）。
            var device = new FakeNatDevice { AssignedPublicPort = 41234, ExternalIpAddress = "203.0.113.7" };
            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result =>
                {
                    Assert.AreEqual(PortMappingStatus.Success, result.Status);
                    Assert.IsTrue(result.Success);
                    Assert.AreEqual(7777, result.InternalPort);
                    Assert.AreEqual(41234, result.ExternalPort, "要求値ではなく返り値の PublicPort を使う。");
                    Assert.AreEqual("203.0.113.7", result.DeviceExternalIpAddress);
                    Assert.AreEqual("UPnP", result.DeviceProtocolName);
                });

                Assert.IsTrue(service.HasActiveMapping);
                Assert.AreEqual(1, device.CreatedMappings.Count);
                Assert.AreEqual(7777, device.CreatedMappings[0].PrivatePort);
                Assert.AreEqual(7777, device.CreatedMappings[0].PublicPort);
                Assert.AreEqual(3600, device.CreatedMappings[0].LifetimeSeconds, "lifetime は 3600 秒。");
                Assert.AreEqual("TsumugiQuiz", device.CreatedMappings[0].Description);
            }
        }

        [UnityTest]
        public IEnumerator MapAsync_RemovesStaleMappingsWithOwnDescription()
        {
            var device = new FakeNatDevice();
            device.ExistingMappings.Add(new NatPortMapping(7777, 7777, 3600, "TsumugiQuiz"));
            device.ExistingMappings.Add(new NatPortMapping(8080, 8080, 3600, "OtherApp"));
            device.ExistingMappings.Add(new NatPortMapping(7000, 7000, 0, "TsumugiQuiz"));

            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result => Assert.IsTrue(result.Success, result.Message));

                Assert.AreEqual(1, device.GetAllMappingsCallCount);
                Assert.AreEqual(2, device.DeletedMappings.Count, "自分の説明が付いたマッピングだけ削除する。");
                CollectionAssert.AreEqual(
                    new[] { 7777, 7000 },
                    new[] { device.DeletedMappings[0].PrivatePort, device.DeletedMappings[1].PrivatePort });

                // 他アプリのマッピングは残り、自分の新しいマッピングが登録されている。
                CollectionAssert.AreEquivalent(
                    new[] { "OtherApp", "TsumugiQuiz" },
                    device.ExistingMappings.ConvertAll(entry => entry.Description));
            }
        }

        [UnityTest]
        public IEnumerator RenewAsync_DoesNotRemoveItsOwnActiveMapping()
        {
            // 更新のたびに掛除（古いマッピングの削除）を走らせると、今使っているマッピングを消してしまう（M-1）。
            var device = new FakeNatDevice();
            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result => Assert.IsTrue(result.Success, result.Message));

                var deletesAfterMap = device.DeletedMappings.Count;
                var listCallsAfterMap = device.GetAllMappingsCallCount;

                yield return AsyncTest.Await(service.RenewAsync(), result => Assert.IsTrue(result.Success, result.Message));

                Assert.AreEqual(deletesAfterMap, device.DeletedMappings.Count, "更新では削除しない（上書き作成のみ）。");
                Assert.AreEqual(listCallsAfterMap, device.GetAllMappingsCallCount, "更新では掛除（一覧取得）をしない。");
                Assert.AreEqual(1, device.ExistingMappings.Count, "ルーター側にマッピングが残り続ける。");
                Assert.AreEqual(7777, device.ExistingMappings[0].PublicPort);
                Assert.IsTrue(service.HasActiveMapping);
            }
        }

        [UnityTest]
        public IEnumerator MapAsync_WhenRouterReturnsDifferentPrivatePort_UsesRequestedPort()
        {
            // ルーターの言い値ではなく、このPCが実際に待ち受けているポートを使う（H-3）。
            var device = new FakeNatDevice { AssignedPrivatePort = 1234, AssignedPublicPort = 41234 };
            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result =>
                {
                    Assert.IsTrue(result.Success, result.Message);
                    Assert.AreEqual(7777, result.InternalPort);
                    Assert.AreEqual(41234, result.ExternalPort);
                });

                yield return AsyncTest.Await(service.RenewAsync(), result => Assert.IsTrue(result.Success, result.Message));

                // 更新も要求値で行う（ルーターが返した 1234 ではない）。
                Assert.AreEqual(2, device.CreatedMappings.Count);
                Assert.AreEqual(7777, device.CreatedMappings[1].PrivatePort);

                yield return AsyncTest.Await(service.ReleaseAsync());

                Assert.AreEqual(1, device.DeletedMappings.Count);
                Assert.AreEqual(7777, device.DeletedMappings[0].PrivatePort, "削除も要求値の内部ポートで行う。");
            }
        }

        [UnityTest]
        public IEnumerator MappingChanged_IsRaisedForSuccessAndFailure()
        {
            var device = new FakeNatDevice { AssignedPublicPort = 41234 };
            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                var received = new List<PortMappingResult>();
                service.MappingChanged += received.Add;

                yield return AsyncTest.Await(service.MapAsync(7777), result => Assert.IsTrue(result.Success, result.Message));

                Assert.AreEqual(1, received.Count, "作成で 1 回発火する。");
                Assert.AreEqual(41234, received[0].ExternalPort);

                // 更新でルーターが別の外部ポートを割り当てるケース。
                device.AssignedPublicPort = 51234;
                yield return AsyncTest.Await(service.RenewAsync(), result => Assert.IsTrue(result.Success, result.Message));

                Assert.AreEqual(2, received.Count, "更新でも発火する。");
                Assert.AreEqual(51234, received[1].ExternalPort);

                // 失敗しても発火する（画面に理由を出せるようにするため）。
                device.CreateException = new NatDeviceException(NatFailureKind.Refused, "拒否");
                yield return AsyncTest.Await(service.RenewAsync(), result => Assert.IsFalse(result.Success));

                Assert.AreEqual(3, received.Count);
                Assert.AreEqual(PortMappingStatus.Refused, received[2].Status);
            }
        }

        [UnityTest]
        public IEnumerator MappingChanged_SubscriberExceptionDoesNotBreakMapping()
        {
            var device = new FakeNatDevice();
            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                service.MappingChanged += _ => throw new System.InvalidOperationException("テスト用");

                yield return AsyncTest.Await(service.MapAsync(7777), result => Assert.IsTrue(result.Success, result.Message));

                Assert.IsTrue(service.HasActiveMapping);
            }
        }

        [UnityTest]
        public IEnumerator MapAsync_WhenDeviceThrowsUnexpectedException_ReturnsFailed()
        {
            // NatDeviceException 以外の想定外の例外でもホスト開始を止めない（M-9）。
            var device = new ThrowingNatDevice(new System.InvalidOperationException("想定外"));
            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result =>
                {
                    Assert.AreEqual(PortMappingStatus.Failed, result.Status);
                    Assert.IsNotEmpty(result.Message);
                });

                Assert.IsFalse(service.HasActiveMapping);
            }
        }

        [UnityTest]
        public IEnumerator MapAsync_WhenListingFails_StillCreatesMapping()
        {
            var device = new FakeNatDevice
            {
                GetAllException = new NatDeviceException(NatFailureKind.Unsupported, "一覧に未対応"),
            };

            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result => Assert.IsTrue(result.Success, result.Message));

                Assert.AreEqual(1, device.CreatedMappings.Count);
            }
        }

        [UnityTest]
        public IEnumerator MapAsync_WhenRouterRefuses_ReturnsRefused()
        {
            var device = new FakeNatDevice
            {
                CreateException = new NatDeviceException(NatFailureKind.Refused, "拒否されました"),
            };

            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result =>
                {
                    Assert.AreEqual(PortMappingStatus.Refused, result.Status);
                    StringAssert.Contains("UPnP", result.Message);
                });

                Assert.IsFalse(service.HasActiveMapping);
            }
        }

        [UnityTest]
        public IEnumerator MapAsync_WhenPortConflicts_ReturnsRefused()
        {
            var device = new FakeNatDevice
            {
                CreateException = new NatDeviceException(NatFailureKind.Conflict, "競合"),
            };

            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result =>
                    Assert.AreEqual(PortMappingStatus.Refused, result.Status));
            }
        }

        [UnityTest]
        public IEnumerator MapAsync_WhenRouterReturnsInvalidPort_ReturnsFailed()
        {
            var device = new FakeNatDevice { AssignedPublicPort = 0 };

            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result =>
                {
                    Assert.AreEqual(PortMappingStatus.Failed, result.Status);
                    Assert.AreEqual(0, result.ExternalPort);
                });

                Assert.IsFalse(service.HasActiveMapping);
            }
        }

        [UnityTest]
        public IEnumerator MapAsync_WhenExternalIpFails_StillSucceedsWithoutAddress()
        {
            var device = new FakeNatDevice
            {
                ExternalIpException = new NatDeviceException(NatFailureKind.Network, "取得できません"),
            };

            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result =>
                {
                    Assert.IsTrue(result.Success, result.Message);
                    Assert.AreEqual(string.Empty, result.DeviceExternalIpAddress);
                });
            }
        }

        [UnityTest]
        public IEnumerator ReleaseAsync_DeletesMappingAndClearsState()
        {
            var device = new FakeNatDevice { AssignedPublicPort = 41234 };
            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result => Assert.IsTrue(result.Success, result.Message));

                yield return AsyncTest.Await(service.ReleaseAsync());

                Assert.IsFalse(service.HasActiveMapping);
                Assert.AreEqual(1, device.DeletedMappings.Count);
                Assert.AreEqual(41234, device.DeletedMappings[0].PublicPort, "作成結果のマッピングを削除する。");
            }
        }

        [UnityTest]
        public IEnumerator ReleaseAsync_WhenNothingMapped_DoesNothing()
        {
            var device = new FakeNatDevice();
            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.ReleaseAsync());

                Assert.IsFalse(service.HasActiveMapping);
                Assert.IsEmpty(device.DeletedMappings);
            }
        }

        [UnityTest]
        public IEnumerator ReleaseAsync_WhenDeleteFails_DoesNotThrow()
        {
            var device = new FakeNatDevice
            {
                DeleteException = new NatDeviceException(NatFailureKind.Network, "削除できません"),
            };

            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result => Assert.IsTrue(result.Success, result.Message));
                yield return AsyncTest.Await(service.ReleaseAsync());

                Assert.IsFalse(service.HasActiveMapping);
            }
        }

        [UnityTest]
        public IEnumerator MapAsync_CalledTwice_ReleasesPreviousMappingFirst()
        {
            var device = new FakeNatDevice();
            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result => Assert.IsTrue(result.Success, result.Message));
                device.DeletedMappings.Clear();

                yield return AsyncTest.Await(service.MapAsync(7778), result =>
                {
                    Assert.IsTrue(result.Success, result.Message);
                    Assert.AreEqual(7778, result.ExternalPort);
                });

                Assert.AreEqual(1, device.DeletedMappings.Count, "前のマッピングを削除してから作り直す。");
                Assert.AreEqual(7777, device.DeletedMappings[0].PrivatePort);
            }
        }

        [UnityTest]
        public IEnumerator RenewAsync_RecreatesMapping()
        {
            var device = new FakeNatDevice();
            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.MapAsync(7777), result => Assert.IsTrue(result.Success, result.Message));

                yield return AsyncTest.Await(service.RenewAsync(), result =>
                {
                    Assert.IsTrue(result.Success, result.Message);
                    Assert.AreEqual(7777, result.ExternalPort);
                });

                Assert.AreEqual(2, device.CreatedMappings.Count, "更新は作り直しで行う。");
                Assert.IsTrue(service.HasActiveMapping);
            }
        }

        [UnityTest]
        public IEnumerator RenewAsync_WithoutMapping_KeepsLastResult()
        {
            var device = new FakeNatDevice();
            using (var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions))
            {
                yield return AsyncTest.Await(service.RenewAsync(), result =>
                    Assert.AreEqual(PortMappingStatus.NotAttempted, result.Status));

                Assert.IsEmpty(device.CreatedMappings);
            }
        }

        [UnityTest]
        public IEnumerator Dispose_DeletesMapping()
        {
            var device = new FakeNatDevice();
            var service = new PortMappingService(new FakeNatDiscovery(device), ShortTimeoutOptions);

            yield return AsyncTest.Await(service.MapAsync(7777), result => Assert.IsTrue(result.Success, result.Message));

            service.Dispose();

            Assert.AreEqual(1, device.DeletedMappings.Count);
            Assert.IsFalse(service.HasActiveMapping);
        }

        [Test]
        public void LastResult_BeforeMapping_IsNotAttempted()
        {
            using (var service = new PortMappingService(new FakeNatDiscovery(null), ShortTimeoutOptions))
            {
                Assert.AreEqual(PortMappingStatus.NotAttempted, service.LastResult.Status);
                Assert.AreEqual(string.Empty, service.DeviceProtocolName);
            }
        }

        [UnityTest]
        public IEnumerator MapAsync_WithZeroPort_FaultsWithArgumentException()
        {
            using (var service = new PortMappingService(new FakeNatDiscovery(null), ShortTimeoutOptions))
            {
                // async メソッドの引数検証は例外を同期的に投げず、失敗したタスクとして返る。
                var task = service.MapAsync(0);

                while (!task.IsCompleted)
                {
                    yield return null;
                }

                Assert.IsTrue(task.IsFaulted);
                Assert.IsInstanceOf<System.ArgumentOutOfRangeException>(task.Exception?.InnerException);
            }
        }
    }
}
