using NUnit.Framework;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.Tests.EditMode.Room
{
    /// <summary>
    /// <see cref="AppSettings"/>（#26）のテスト。既定値は docs/room-settings.md §0/§2。
    /// </summary>
    public class AppSettingsTests
    {
        [Test]
        public void Default_MatchesDocumentedValues()
        {
            var settings = AppSettings.Default;

            Assert.AreEqual("", settings.PlayerName);
            Assert.AreEqual(7777, settings.NetworkPort);
            CollectionAssert.AreEqual(new[] { "https://api.ipify.org" }, settings.IpLookupUrls);
            Assert.IsTrue(settings.UpnpEnabled);
            Assert.AreEqual(5000, settings.UpnpDiscoveryTimeoutMs);
            Assert.AreEqual(3600, settings.UpnpMappingLifetimeSec);
            Assert.AreEqual(1800000, settings.UpnpRenewIntervalMs);
            Assert.AreEqual(1, settings.QuestionPrefetchCount);
            Assert.AreEqual("春日部つむぎ", settings.TtsSpeakerName);
            Assert.AreEqual("ノーマル", settings.TtsStyleName);
            Assert.AreEqual(209715200L, settings.TtsCacheMaxBytes);
            Assert.AreEqual(5000, settings.TtsCacheMaxEntries);
            Assert.AreEqual("", settings.TtsAssetPathOverride);
            Assert.IsTrue(settings.CharacterEnabled, "character.enabled の既定は true。");
        }

        [Test]
        public void Create_OutOfRangePort_Clamps()
        {
            var tooLow = AppSettings.Create(networkPort: 1);
            var tooHigh = AppSettings.Create(networkPort: 999999);

            Assert.AreEqual(AppSettings.MinNetworkPort, tooLow.NetworkPort);
            Assert.AreEqual(AppSettings.MaxNetworkPort, tooHigh.NetworkPort);
        }

        [Test]
        public void Create_NonHttpsIpLookupUrl_IsFilteredOut()
        {
            var settings = AppSettings.Create(ipLookupUrls: new[] { "http://insecure.example", "https://good.example" });

            CollectionAssert.AreEqual(new[] { "https://good.example" }, settings.IpLookupUrls);
        }

        [Test]
        public void Create_AllUrlsInvalid_FallsBackToDefault()
        {
            var settings = AppSettings.Create(ipLookupUrls: new[] { "not a url", "ftp://also-bad" });

            CollectionAssert.AreEqual(AppSettings.DefaultIpLookupUrls, settings.IpLookupUrls);
        }

        [Test]
        public void Create_RenewIntervalLongerThanHalfLifetime_ClampsDown()
        {
            var settings = AppSettings.Create(upnpMappingLifetimeSec: 100, upnpRenewIntervalMs: 1000000);

            // lifetime 100秒 → 半分は50秒=50000ms。ただし下限 MinUpnpRenewIntervalMs=10000 は下回らない。
            Assert.AreEqual(50000, settings.UpnpRenewIntervalMs);
        }

        [Test]
        public void Create_UnlimitedLifetime_DoesNotCapRenewInterval()
        {
            var settings = AppSettings.Create(upnpMappingLifetimeSec: 0, upnpRenewIntervalMs: 5_000_000);

            Assert.AreEqual(5_000_000, settings.UpnpRenewIntervalMs);
        }

        [Test]
        public void Create_NegativeCacheValues_FallBackToDefaults()
        {
            var settings = AppSettings.Create(ttsCacheMaxBytes: -1, ttsCacheMaxEntries: -1);

            Assert.AreEqual(AppSettings.DefaultTtsCacheMaxBytes, settings.TtsCacheMaxBytes);
            Assert.AreEqual(AppSettings.DefaultTtsCacheMaxEntries, settings.TtsCacheMaxEntries);
        }

        [Test]
        public void Create_InvalidPlayerName_FallsBackToDefault()
        {
            var tooLong = AppSettings.Create(playerName: "12345678901234567"); // 17文字（上限16超え）

            Assert.AreEqual(AppSettings.DefaultPlayerName, tooLong.PlayerName);
        }

        [Test]
        public void Create_ValidPlayerName_IsNormalized()
        {
            var settings = AppSettings.Create(playerName: "  つむぎ  ");

            Assert.AreEqual("つむぎ", settings.PlayerName, "前後の空白は正規化される。");
        }

        [Test]
        public void WithPlayerName_ReturnsNewInstance()
        {
            var original = AppSettings.Default;

            var changed = original.WithPlayerName("つむぎ");

            Assert.AreEqual("", original.PlayerName);
            Assert.AreEqual("つむぎ", changed.PlayerName);
        }

        [Test]
        public void WithCharacterEnabled_ReturnsNewInstance()
        {
            var original = AppSettings.Default;

            var changed = original.WithCharacterEnabled(false);

            Assert.IsTrue(original.CharacterEnabled);
            Assert.IsFalse(changed.CharacterEnabled);
        }
    }
}
