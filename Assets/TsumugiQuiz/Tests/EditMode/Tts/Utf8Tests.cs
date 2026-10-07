using System;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using TsumugiQuiz.Tts.Native;

namespace TsumugiQuiz.Tests.EditMode.Tts
{
    /// <summary>UTF-8 マーシャリングヘルパ（docs/tts-native-api.md §2.1）の検証。</summary>
    public sealed class Utf8Tests
    {
        [TestCase("こんにちは")]
        [TestCase("春日部つむぎ")]
        [TestCase("ASCII only")]
        [TestCase("")]
        [TestCase("絵文字🎤と半角ｶﾅ")]
        public void ToNullTerminated_FromPtr_ラウンドトリップする(string original)
        {
            var bytes = Utf8.ToNullTerminated(original);

            Assert.That(bytes[bytes.Length - 1], Is.EqualTo(0), "ヌル終端されていること");
            Assert.That(bytes.Length, Is.EqualTo(Encoding.UTF8.GetByteCount(original) + 1));

            var ptr = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                Assert.That(Utf8.FromPtr(ptr), Is.EqualTo(original));
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        [Test]
        public void ToNullTerminated_UTF16ではなくUTF8のバイト列になる()
        {
            var bytes = Utf8.ToNullTerminated("あ");

            // U+3042 は UTF-8 で E3 81 82（3 バイト）
            Assert.That(bytes, Is.EqualTo(new byte[] { 0xE3, 0x81, 0x82, 0x00 }));
        }

        [Test]
        public void ToNullTerminated_nullは例外()
        {
            Assert.Throws<ArgumentNullException>(() => Utf8.ToNullTerminated(null));
        }

        [TestCase("あ\0い")]
        [TestCase("\0")]
        [TestCase("末尾に\0")]
        public void ToNullTerminated_埋め込みNULは例外(string invalid)
        {
            // ネイティブ側で文字列が途中で切れるため、境界で弾く。
            Assert.Throws<ArgumentException>(() => Utf8.ToNullTerminated(invalid));
        }

        [Test]
        public void FromPtr_NULLポインタは空文字列()
        {
            Assert.That(Utf8.FromPtr(IntPtr.Zero), Is.EqualTo(string.Empty));
        }
    }
}
