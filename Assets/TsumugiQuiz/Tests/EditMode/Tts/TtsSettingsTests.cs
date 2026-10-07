using NUnit.Framework;
using TsumugiQuiz.Tts;

namespace TsumugiQuiz.Tests.EditMode.Tts
{
    /// <summary>アプリ設定の既定値とクランプ（docs/room-settings.md §2）の検証。</summary>
    public sealed class TtsSettingsTests
    {
        [Test]
        public void 既定値はdocsのとおり()
        {
            var settings = TtsSettings.Default;

            Assert.That(settings.SpeakerName, Is.EqualTo("春日部つむぎ"));
            Assert.That(settings.StyleName, Is.EqualTo("ノーマル"));
            Assert.That(settings.CacheMaxBytes, Is.EqualTo(209715200L), "200MB");
            Assert.That(settings.CacheMaxEntries, Is.EqualTo(5000));
            Assert.That(settings.AssetPathOverride, Is.Empty);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void 話者名とスタイル名が空なら既定値に丸める(string value)
        {
            var settings = TtsSettings.Create(speakerName: value, styleName: value);

            Assert.That(settings.SpeakerName, Is.EqualTo(TtsSettings.DefaultSpeakerName));
            Assert.That(settings.StyleName, Is.EqualTo(TtsSettings.DefaultStyleName));
        }

        [Test]
        public void 負の上限は既定値に丸める()
        {
            var settings = TtsSettings.Create(cacheMaxBytes: -1, cacheMaxEntries: -1);

            Assert.That(settings.CacheMaxBytes, Is.EqualTo(TtsSettings.DefaultCacheMaxBytes));
            Assert.That(settings.CacheMaxEntries, Is.EqualTo(TtsSettings.DefaultCacheMaxEntries));
        }

        [Test]
        public void 上限0は許容する()
        {
            var settings = TtsSettings.Create(cacheMaxBytes: 0, cacheMaxEntries: 0);

            Assert.That(settings.CacheMaxBytes, Is.EqualTo(0));
            Assert.That(settings.CacheMaxEntries, Is.EqualTo(0));
        }

        [Test]
        public void Withメソッドは新しいインスタンスを返し元を変えない()
        {
            var original = TtsSettings.Default;

            var changed = original.WithSpeaker("ずんだもん", "あまあま")
                                  .WithCacheLimits(100, 10)
                                  .WithAssetPathOverride(@"E:\voicevox_core");

            Assert.That(original.SpeakerName, Is.EqualTo(TtsSettings.DefaultSpeakerName), "元は不変であること");
            Assert.That(original.CacheMaxBytes, Is.EqualTo(TtsSettings.DefaultCacheMaxBytes));
            Assert.That(changed.SpeakerName, Is.EqualTo("ずんだもん"));
            Assert.That(changed.StyleName, Is.EqualTo("あまあま"));
            Assert.That(changed.CacheMaxBytes, Is.EqualTo(100));
            Assert.That(changed.CacheMaxEntries, Is.EqualTo(10));
            Assert.That(changed.AssetPathOverride, Is.EqualTo(@"E:\voicevox_core"));
        }

        [Test]
        public void 既定のプロバイダは既定値を返す()
        {
            Assert.That(new DefaultTtsSettingsProvider().Load(), Is.SameAs(TtsSettings.Default));
        }

        [Test]
        public void 固定プロバイダは与えた設定を返す()
        {
            var settings = TtsSettings.Create(cacheMaxEntries: 3);

            Assert.That(new FixedTtsSettingsProvider(settings).Load(), Is.SameAs(settings));
        }

        [Test]
        public void キャッシュ上限は設定から作れる()
        {
            var limits = TtsCacheLimits.FromSettings(TtsSettings.Create(cacheMaxBytes: 123, cacheMaxEntries: 4));

            Assert.That(limits.MaxBytes, Is.EqualTo(123));
            Assert.That(limits.MaxEntries, Is.EqualTo(4));
            Assert.That(TtsCacheLimits.Default.MaxBytes, Is.EqualTo(TtsSettings.DefaultCacheMaxBytes));
        }

        [TestCase(1.0f, 1.0f)]
        [TestCase(0.1f, 0.5f)]
        [TestCase(3.0f, 2.0f)]
        [TestCase(float.NaN, 1.0f)]
        public void 速度は範囲内に丸める(float input, float expected)
        {
            Assert.That(TtsSpeed.Clamp(input), Is.EqualTo(expected));
        }

        [Test]
        public void 速度が1付近なら一括合成の経路を使う()
        {
            Assert.That(TtsSpeed.IsDefault(1.0f), Is.True);
            Assert.That(TtsSpeed.IsDefault(1.0005f), Is.True);
            Assert.That(TtsSpeed.IsDefault(1.05f), Is.False);
        }
    }
}
