using System;
using NUnit.Framework;
using TsumugiQuiz.Tts;

namespace TsumugiQuiz.Tests.EditMode.Tts
{
    /// <summary>
    /// キャッシュキーの連結規則（docs/tts.md §7.1、仮決め K16）の検証。
    /// </summary>
    public sealed class TtsCacheKeyTests
    {
        private const string Speaker = "春日部つむぎ";
        private const string Style = "ノーマル";
        private const string Core = "0.17.0";
        private const string Models = "0.16.4";

        private static string Key(
            string readingText, string styleName = Style, string speakerName = Speaker,
            float speed = 1.0f, string coreVersion = Core, string modelsVersion = Models)
            => TtsCacheKey.Compute(readingText, styleName, speakerName, speed, coreVersion, modelsVersion);

        [Test]
        public void 同じ入力なら同じキーになる()
        {
            Assert.That(Key("こんにちは"), Is.EqualTo(Key("こんにちは")));
        }

        [Test]
        public void キーは32文字の小文字hexになる()
        {
            var key = Key("こんにちは");

            Assert.That(key.Length, Is.EqualTo(TtsCacheKey.HexLength));
            Assert.That(key, Does.Match("^[0-9a-f]{32}$"), "小文字 hex であること");
            Assert.That(TtsCacheKey.IsValid(key), Is.True);
        }

        /// <summary>
        /// 区切りなしで連結すると ("あい","う") と ("あ","いう") が同じキーになる（連結の曖昧性）。
        /// NUL 区切りを入れているので別キーになること。
        /// </summary>
        [Test]
        public void 区切り文字があるので連結の曖昧性が生じない()
        {
            var a = TtsCacheKey.Compute("あい", "う", Speaker, 1.0f, Core, Models);
            var b = TtsCacheKey.Compute("あ", "いう", Speaker, 1.0f, Core, Models);

            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void 話者名とスタイル名を入れ替えると別キーになる()
        {
            var a = TtsCacheKey.Compute("テスト", "ノーマル", "春日部つむぎ", 1.0f, Core, Models);
            var b = TtsCacheKey.Compute("テスト", "春日部つむぎ", "ノーマル", 1.0f, Core, Models);

            Assert.That(a, Is.Not.EqualTo(b), "連結順が固定されていること");
        }

        [Test]
        public void 読みが違えば別キーになる()
        {
            Assert.That(Key("こんにちは"), Is.Not.EqualTo(Key("こんばんは")));
        }

        [Test]
        public void 大文字小文字が違えば別キーになる()
        {
            Assert.That(Key("ABC"), Is.Not.EqualTo(Key("abc")));
            Assert.That(
                TtsCacheKey.Compute("test", Style, "Tsumugi", 1.0f, Core, Models),
                Is.Not.EqualTo(TtsCacheKey.Compute("test", Style, "tsumugi", 1.0f, Core, Models)));
        }

        [Test]
        public void 速度が違えば別キーになる()
        {
            Assert.That(Key("こんにちは", speed: 1.0f), Is.Not.EqualTo(Key("こんにちは", speed: 1.5f)));
        }

        /// <summary>速度は F2 で丸めるので、表示上同じ値なら同じキーになる。</summary>
        [Test]
        public void 速度は小数第2位までで同一視される()
        {
            Assert.That(Key("こんにちは", speed: 1.0f), Is.EqualTo(Key("こんにちは", speed: 1.004f)));
            Assert.That(Key("こんにちは", speed: 1.0f), Is.Not.EqualTo(Key("こんにちは", speed: 1.01f)));
        }

        [Test]
        public void coreVersionとmodelsVersionが違えば別キーになる()
        {
            Assert.That(Key("こんにちは", coreVersion: "0.17.0"), Is.Not.EqualTo(Key("こんにちは", coreVersion: "0.18.0")));
            Assert.That(Key("こんにちは", modelsVersion: "0.16.4"), Is.Not.EqualTo(Key("こんにちは", modelsVersion: "0.17.0")));
        }

        [Test]
        public void 空の読みでもキーを作れる()
        {
            Assert.That(TtsCacheKey.IsValid(Key(string.Empty)), Is.True);
            Assert.That(Key(string.Empty), Is.Not.EqualTo(Key(" ")));
        }

        [Test]
        public void 読みがnullなら例外になる()
        {
            Assert.Throws<ArgumentNullException>(() => Key(null));
        }

        [TestCase("")]
        [TestCase(null)]
        public void 話者名が空なら例外になる(string speakerName)
        {
            Assert.Throws<ArgumentException>(
                () => TtsCacheKey.Compute("こんにちは", Style, speakerName, 1.0f, Core, Models));
        }

        [TestCase("")]
        [TestCase(null)]
        public void スタイル名が空なら例外になる(string styleName)
        {
            Assert.Throws<ArgumentException>(
                () => TtsCacheKey.Compute("こんにちは", styleName, Speaker, 1.0f, Core, Models));
        }

        [Test]
        public void バージョンがnullでも例外にならない()
        {
            Assert.That(TtsCacheKey.IsValid(Key("こんにちは", coreVersion: null, modelsVersion: null)), Is.True);
        }

        [TestCase("00112233445566778899aabbccddeeff", true)]
        [TestCase("00112233445566778899AABBCCDDEEFF", false, Description = "大文字 hex は不可")]
        [TestCase("00112233445566778899aabbccddeef", false, Description = "31 文字")]
        [TestCase("00112233445566778899aabbccddeeffa", false, Description = "33 文字")]
        [TestCase("00112233445566778899aabbccddeeg0", false, Description = "hex 以外")]
        [TestCase(null, false)]
        public void キーの形式検証(string key, bool expected)
        {
            Assert.That(TtsCacheKey.IsValid(key), Is.EqualTo(expected));
        }

        [Test]
        public void サブディレクトリ名はキーの先頭2文字になる()
        {
            var key = Key("こんにちは");

            Assert.That(TtsCacheKey.GetShard(key), Is.EqualTo(key.Substring(0, 2)));
            Assert.Throws<ArgumentException>(() => TtsCacheKey.GetShard("壊れたキー"));
        }
    }
}
