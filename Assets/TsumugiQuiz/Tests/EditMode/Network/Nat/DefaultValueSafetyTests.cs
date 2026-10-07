using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network.Nat;

namespace TsumugiQuiz.Tests.EditMode.Network.Nat
{
    /// <summary>
    /// 初期化していない構造体（<c>default(T)</c>）でも全プロパティが安全に読めることの確認。
    /// 構造体はコンストラクタを通さずに生成できるため、文字列フィールドが null のまま
    /// プロパティに露出すると画面側で NullReferenceException になる。
    /// #5 の HostSetup 画面は解決前に <see cref="HostAddressInfo"/> を読みうるので、
    /// ここを崩さないようテストで固定する。
    /// </summary>
    public class DefaultValueSafetyTests
    {
        [Test]
        public void DefaultHostAddressInfo_ExposesEmptyStringsAndWarnings()
        {
            var info = default(HostAddressInfo);

            Assert.AreEqual(string.Empty, info.LanIpAddress);
            Assert.AreEqual(string.Empty, info.ManualPublicIpAddress);
            Assert.AreEqual(string.Empty, info.PublicIpAddress);
            Assert.AreEqual(0, info.InternalPort);
            Assert.AreEqual(0, info.ExternalPort);
            Assert.IsFalse(info.CanCreateInternetCode);
            Assert.IsFalse(info.CanCreateLanCode);
            Assert.IsFalse(info.IsCarrierGradeNat);
            Assert.IsFalse(info.IsDoubleNatSuspected);
            Assert.IsTrue(info.RequiresManualPortForwarding);

            // Warnings は例外を出さずに読めること（中身は「未開放」「IP 未取得」の 2 件）。
            var warnings = info.Warnings;
            Assert.IsNotNull(warnings);
            Assert.AreEqual(2, warnings.Count);
            foreach (var warning in warnings)
            {
                Assert.IsNotEmpty(warning);
            }

            // 内部ポートが 0 なので案内は既定値（例外にならないこと）。
            var guide = info.ManualGuide;
            Assert.AreEqual("UDP", guide.Protocol);
            Assert.AreEqual(string.Empty, guide.LanIpAddress);
            Assert.IsFalse(guide.HasLanIpAddress);
        }

        [Test]
        public void NotResolved_EqualsDefaultBehaviour()
        {
            var explicitValue = HostAddressInfo.NotResolved;
            var defaultValue = default(HostAddressInfo);

            Assert.AreEqual(defaultValue.Warnings.Count, explicitValue.Warnings.Count);
            Assert.AreEqual(defaultValue.PublicIpAddress, explicitValue.PublicIpAddress);
            Assert.AreEqual(defaultValue.LanIpAddress, explicitValue.LanIpAddress);
            Assert.AreEqual(defaultValue.PortMapping.Status, explicitValue.PortMapping.Status);
            Assert.AreEqual(defaultValue.PublicIp.Source, explicitValue.PublicIp.Source);
        }

        [Test]
        public void DefaultPortMappingResult_ExposesEmptyStrings()
        {
            var result = default(PortMappingResult);

            Assert.AreEqual(PortMappingStatus.NotAttempted, result.Status);
            Assert.AreEqual(string.Empty, result.Message);
            Assert.AreEqual(string.Empty, result.DeviceExternalIpAddress);
            Assert.AreEqual(string.Empty, result.DeviceProtocolName);
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void DefaultPublicIpResult_ExposesEmptyStrings()
        {
            var result = default(PublicIpResult);

            Assert.AreEqual(string.Empty, result.Address);
            Assert.AreEqual(string.Empty, result.NatDeviceAddress);
            Assert.AreEqual(string.Empty, result.LookupServiceAddress);
            Assert.AreEqual(string.Empty, result.Message);
            Assert.AreEqual(PublicIpSource.None, result.Source);
            Assert.IsFalse(result.Found);
            Assert.IsFalse(result.IsCarrierGradeNat);
            Assert.IsFalse(result.IsGloballyRoutable);
            Assert.IsFalse(result.AddressesDisagree);
            Assert.AreEqual(IpAddressCategory.Invalid, result.Category);
        }

        [Test]
        public void DefaultNatPortMapping_ExposesEmptyDescription()
        {
            var mapping = default(NatPortMapping);

            Assert.AreEqual(string.Empty, mapping.Description);
            Assert.IsFalse(mapping.HasValidPublicPort);
            Assert.IsNotEmpty(mapping.ToString());
            Assert.AreEqual(mapping.GetHashCode(), default(NatPortMapping).GetHashCode());
        }

        [Test]
        public void DefaultManualPortMappingGuide_ExposesUdpAndEmptyLanIp()
        {
            var guide = default(ManualPortMappingGuide);

            Assert.AreEqual("UDP", guide.Protocol);
            Assert.AreEqual(string.Empty, guide.LanIpAddress);
            Assert.IsFalse(guide.HasLanIpAddress);
            Assert.AreEqual(4, guide.ToDisplayRows().Count);
            Assert.IsNotEmpty(guide.ToString());
        }
    }
}
