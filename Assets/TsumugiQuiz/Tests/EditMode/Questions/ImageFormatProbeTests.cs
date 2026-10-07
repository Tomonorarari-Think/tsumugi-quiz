using System;
using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Images;
using TsumugiQuiz.Tests.Shared.Questions;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="ImageFormatProbe"/> の形式判定・解像度読み取り（docs/network.md §9、#16）。
    /// 受信したバイト列をデコードする前の入力検証にあたる。
    /// </summary>
    public class ImageFormatProbeTests
    {
        [Test]
        public void TryProbe_WithPng_ReadsFormatAndSize()
        {
            var png = TestImageFactory.CreatePng(24, 13);

            Assert.IsTrue(ImageFormatProbe.TryProbe(png, out var format, out var width, out var height));
            Assert.AreEqual(ImageFormat.Png, format);
            Assert.AreEqual(24, width);
            Assert.AreEqual(13, height);
        }

        [Test]
        public void TryProbe_WithJpg_ReadsFormatAndSize()
        {
            var jpg = TestImageFactory.CreateJpg(32, 16);

            Assert.IsTrue(ImageFormatProbe.TryProbe(jpg, out var format, out var width, out var height));
            Assert.AreEqual(ImageFormat.Jpeg, format);
            Assert.AreEqual(32, width);
            Assert.AreEqual(16, height);
        }

        [Test]
        public void TryProbe_WithUnknownBytes_IsUnknown()
        {
            var garbage = new byte[64];
            for (var i = 0; i < garbage.Length; i++)
            {
                garbage[i] = (byte)i;
            }

            Assert.IsFalse(ImageFormatProbe.TryProbe(garbage, out var format, out var width, out var height));
            Assert.AreEqual(ImageFormat.Unknown, format);
            Assert.AreEqual(0, width);
            Assert.AreEqual(0, height);
        }

        [Test]
        public void TryProbe_WithNullOrTruncatedPng_IsFalse()
        {
            Assert.IsFalse(ImageFormatProbe.TryProbe(null, out _, out _, out _));

            // 署名だけ（IHDR まで届いていない）は読み取れない。
            var signatureOnly = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Assert.IsFalse(ImageFormatProbe.TryProbe(signatureOnly, out var format, out _, out _));
            Assert.AreEqual(ImageFormat.Png, format, "署名だけでも形式は PNG と判定する。");
        }

        [Test]
        public void TryValidate_WithPng_Accepts()
        {
            var png = TestImageFactory.CreatePng(64, 64);

            Assert.IsTrue(ImageFormatProbe.TryValidate(png, out var reason));
            Assert.AreEqual(ImageFailureReason.None, reason);
        }

        [Test]
        public void TryValidate_WithEmptyOrUnknown_IsDecodeFailed()
        {
            Assert.IsFalse(ImageFormatProbe.TryValidate(null, out var reason));
            Assert.AreEqual(ImageFailureReason.DecodeFailed, reason);

            Assert.IsFalse(ImageFormatProbe.TryValidate(Array.Empty<byte>(), out reason));
            Assert.AreEqual(ImageFailureReason.DecodeFailed, reason);

            Assert.IsFalse(ImageFormatProbe.TryValidate(new byte[] { 1, 2, 3, 4, 5 }, out reason));
            Assert.AreEqual(
                ImageFailureReason.DecodeFailed, reason, "PNG/JPG 以外は（拡張子に関わらず）受け付けない。");
        }

        [Test]
        public void TryValidate_OverSizeLimit_IsSizeExceeded()
        {
            var tooLarge = new byte[QuestionLimits.MaxImageSizeBytes + 1];

            Assert.IsFalse(ImageFormatProbe.TryValidate(tooLarge, out var reason));
            Assert.AreEqual(ImageFailureReason.SizeExceeded, reason);
        }

        [Test]
        public void TryValidate_OverDimensionLimit_IsDecodeFailed()
        {
            // 実際に巨大な画像を作らず、IHDR が主張する解像度だけを上限超過にする。
            var header = TestImageFactory.CreatePngHeaderOnly(QuestionLimits.MaxImageDimension + 1, 16);

            Assert.IsTrue(
                ImageFormatProbe.TryProbe(header, out _, out var width, out _),
                "ヘッダから解像度は読めるはず。");
            Assert.AreEqual(QuestionLimits.MaxImageDimension + 1, width);

            Assert.IsFalse(ImageFormatProbe.TryValidate(header, out var reason));
            Assert.AreEqual(ImageFailureReason.DecodeFailed, reason);
        }

        [Test]
        public void TryValidate_WithZeroDimension_IsDecodeFailed()
        {
            var header = TestImageFactory.CreatePngHeaderOnly(0, 0);

            Assert.IsFalse(ImageFormatProbe.TryValidate(header, out var reason));
            Assert.AreEqual(ImageFailureReason.DecodeFailed, reason);
        }

        [Test]
        public void TryValidate_AtDimensionLimit_Accepts()
        {
            var header = TestImageFactory.CreatePngHeaderOnly(
                QuestionLimits.MaxImageDimension, QuestionLimits.MaxImageDimension);

            Assert.IsTrue(ImageFormatProbe.TryValidate(header, out var reason), "上限ちょうどは受け入れる。");
            Assert.AreEqual(ImageFailureReason.None, reason);
        }
    }
}
