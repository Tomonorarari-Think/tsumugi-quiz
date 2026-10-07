using System;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 手動ポート開放案内のデータ生成（docs/network-nat.md §1.5）。
    /// UI は #5 で作るため、本テストは「必要な 4 点が生成できること」だけを確認する。
    /// </summary>
    public class ManualPortMappingGuideTests
    {
        [Test]
        public void Create_ProducesFourRequiredItems()
        {
            var guide = ManualPortMappingGuide.Create(7777, 7777, "192.168.1.23");

            Assert.AreEqual("UDP", guide.Protocol);
            Assert.AreEqual(7777, guide.ExternalPort);
            Assert.AreEqual(7777, guide.InternalPort);
            Assert.AreEqual("192.168.1.23", guide.LanIpAddress);
            Assert.IsTrue(guide.HasLanIpAddress);

            var rows = guide.ToDisplayRows();
            Assert.AreEqual(4, rows.Count, "プロトコル / 外部ポート / 内部ポート / 宛先 IP の 4 点。");
            CollectionAssert.AreEqual(
                new[] { "プロトコル", "外部ポート", "内部ポート", "宛先 IP" },
                new[] { rows[0].Key, rows[1].Key, rows[2].Key, rows[3].Key });
        }

        [Test]
        public void Create_KeepsDifferentExternalPort()
        {
            // ルーターが別の外部ポートを割り当てた後に再試行する場合、外部と内部が食い違う。
            var guide = ManualPortMappingGuide.Create(41234, 7777, "10.0.0.5");

            Assert.AreEqual(41234, guide.ExternalPort);
            Assert.AreEqual(7777, guide.InternalPort);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not-an-ip")]
        [TestCase("192.168.1")]
        public void Create_TreatsUnreadableLanIpAsUnknown(string lanIp)
        {
            var guide = ManualPortMappingGuide.Create(7777, 7777, lanIp);

            Assert.IsFalse(guide.HasLanIpAddress);
            Assert.AreEqual(string.Empty, guide.LanIpAddress);
            StringAssert.Contains("LAN IP", guide.ToDisplayRows()[3].Value);
        }

        [Test]
        public void Create_TrimsLanIp()
        {
            var guide = ManualPortMappingGuide.Create(7777, 7777, "  192.168.11.2  ");

            Assert.AreEqual("192.168.11.2", guide.LanIpAddress);
        }

        [TestCase(0, 7777)]
        [TestCase(7777, 0)]
        public void Create_RejectsZeroPort(int externalPort, int internalPort)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ManualPortMappingGuide.Create((ushort)externalPort, (ushort)internalPort, "192.168.1.23"));
        }

        [Test]
        public void ToString_ListsEveryRow()
        {
            var text = ManualPortMappingGuide.Create(7777, 7777, "192.168.1.23").ToString();

            StringAssert.Contains("UDP", text);
            StringAssert.Contains("7777", text);
            StringAssert.Contains("192.168.1.23", text);
            Assert.AreEqual(4, text.Split('\n').Length);
        }

        [Test]
        public void Equals_ComparesByValue()
        {
            var left = ManualPortMappingGuide.Create(7777, 7777, "192.168.1.23");
            var right = ManualPortMappingGuide.Create(7777, 7777, "192.168.1.23");
            var other = ManualPortMappingGuide.Create(7778, 7777, "192.168.1.23");

            Assert.AreEqual(left, right);
            Assert.AreEqual(left.GetHashCode(), right.GetHashCode());
            Assert.AreNotEqual(left, other);
        }
    }
}
