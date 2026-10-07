using System;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Network.Nat;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI.Views.Settings;

namespace TsumugiQuiz.Tests.EditMode.UI.Settings
{
    /// <summary>
    /// <see cref="AppSettingsAdapters"/>（issue #28）の <c>AppSettings → TtsSettings</c> /
    /// <c>AppSettings → NatOptions</c> 変換を検証する。
    /// </summary>
    public class AppSettingsAdaptersTests
    {
        [Test]
        public void ToTtsSettings_MapsAllTtsFields()
        {
            var appSettings = AppSettings.Create(
                ttsSpeakerName: "テスト話者",
                ttsStyleName: "テストスタイル",
                ttsCacheMaxBytes: 12345L,
                ttsCacheMaxEntries: 42,
                ttsAssetPathOverride: "C:/tmp/voicevox");

            var ttsSettings = AppSettingsAdapters.ToTtsSettings(appSettings);

            Assert.AreEqual("テスト話者", ttsSettings.SpeakerName);
            Assert.AreEqual("テストスタイル", ttsSettings.StyleName);
            Assert.AreEqual(12345L, ttsSettings.CacheMaxBytes);
            Assert.AreEqual(42, ttsSettings.CacheMaxEntries);
            Assert.AreEqual("C:/tmp/voicevox", ttsSettings.AssetPathOverride);
        }

        [Test]
        public void ToTtsSettings_NullArgument_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => AppSettingsAdapters.ToTtsSettings(null));
        }

        [Test]
        public void ToNatOptions_MapsAllNatFields()
        {
            var urls = new List<string> { "https://example.com/ip" };
            var appSettings = AppSettings.Create(
                upnpEnabled: false,
                upnpDiscoveryTimeoutMs: 9000,
                upnpMappingLifetimeSec: 7200,
                upnpRenewIntervalMs: 20000,
                ipLookupUrls: urls);

            var natOptions = AppSettingsAdapters.ToNatOptions(appSettings);

            Assert.IsFalse(natOptions.Enabled);
            Assert.AreEqual(9000, natOptions.DiscoveryTimeoutMs);
            Assert.AreEqual(7200, natOptions.MappingLifetimeSec);
            Assert.AreEqual(20000, natOptions.RenewIntervalMs);
            CollectionAssert.AreEqual(urls, natOptions.IpLookupUrls);
        }

        [Test]
        public void ToNatOptions_NullArgument_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => AppSettingsAdapters.ToNatOptions(null));
        }

        /// <summary>PR #92 レビュー M6: 既定の AppSettings から変換した結果が TtsSettings.Default と一致すること。</summary>
        [Test]
        public void ToTtsSettings_DefaultAppSettings_MatchesTtsSettingsDefault()
        {
            var ttsSettings = AppSettingsAdapters.ToTtsSettings(AppSettings.Default);

            Assert.AreEqual(TtsSettings.Default.SpeakerName, ttsSettings.SpeakerName);
            Assert.AreEqual(TtsSettings.Default.StyleName, ttsSettings.StyleName);
            Assert.AreEqual(TtsSettings.Default.CacheMaxBytes, ttsSettings.CacheMaxBytes);
            Assert.AreEqual(TtsSettings.Default.CacheMaxEntries, ttsSettings.CacheMaxEntries);
            Assert.AreEqual(TtsSettings.Default.AssetPathOverride, ttsSettings.AssetPathOverride);
        }

        /// <summary>PR #92 レビュー M6: 既定の AppSettings から変換した結果が NatOptions.Default と一致すること。</summary>
        [Test]
        public void ToNatOptions_DefaultAppSettings_MatchesNatOptionsDefault()
        {
            var natOptions = AppSettingsAdapters.ToNatOptions(AppSettings.Default);

            Assert.AreEqual(NatOptions.Default.Enabled, natOptions.Enabled);
            Assert.AreEqual(NatOptions.Default.DiscoveryTimeoutMs, natOptions.DiscoveryTimeoutMs);
            Assert.AreEqual(NatOptions.Default.MappingLifetimeSec, natOptions.MappingLifetimeSec);
            Assert.AreEqual(NatOptions.Default.RenewIntervalMs, natOptions.RenewIntervalMs);
            CollectionAssert.AreEqual(NatOptions.Default.IpLookupUrls, natOptions.IpLookupUrls);
        }
    }
}
