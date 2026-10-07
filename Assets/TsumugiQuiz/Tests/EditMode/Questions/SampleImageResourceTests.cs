using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Images;
using TsumugiQuiz.UI.Views.Game;
using UnityEngine;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// issue #220: 同梱のサンプル画像（Resources/Questions/sample-image.bytes）が、
    /// 1x1 のプレースホルダーではなく、問題画像として見える大きさの PNG であることの回帰テスト。
    /// 画像は scripts/gen-sample-image.py で生成する。
    /// </summary>
    public class SampleImageResourceTests
    {
        /// <summary>くっきり見せるための最小の辺の長さ（表示エリアの最大の高さ 480px 以上）。</summary>
        private const int MinSampleImageSide = 480;

        [Test]
        public void SampleImage_IsPngAndWithinDistributionLimits()
        {
            var asset = Resources.Load<TextAsset>("Questions/sample-image");
            Assert.IsNotNull(asset, "Resources/Questions/sample-image.bytes が見つかりません");

            Assert.IsTrue(ImageFormatProbe.TryValidate(asset.bytes, out var reason), $"配信の検証を通りません: {reason}");
            Assert.IsTrue(ImageFormatProbe.TryProbe(asset.bytes, out var format, out _, out _));
            Assert.AreEqual(ImageFormat.Png, format);
        }

        [Test]
        public void SampleImage_IsDisplayableAndNotAPlaceholderPixel()
        {
            var asset = Resources.Load<TextAsset>("Questions/sample-image");
            Assert.IsNotNull(asset, "Resources/Questions/sample-image.bytes が見つかりません");

            var texture = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(texture.LoadImage(asset.bytes), "サンプル画像をデコードできません");
                Assert.IsTrue(QuestionImageLayout.IsDisplayableSize(texture.width, texture.height));
                Assert.GreaterOrEqual(texture.height, MinSampleImageSide,
                    $"サンプル画像が小さすぎます（{texture.width}x{texture.height}）。1x1 のプレースホルダーに戻っていませんか");
                Assert.GreaterOrEqual(texture.width, MinSampleImageSide);

                var pixels = texture.GetPixels32();
                var first = pixels[0];
                var isUniform = true;
                for (var i = 1; i < pixels.Length && isUniform; i++)
                {
                    isUniform = pixels[i].r == first.r && pixels[i].g == first.g
                        && pixels[i].b == first.b && pixels[i].a == first.a;
                }

                Assert.IsFalse(isUniform, "サンプル画像の画素がすべて同じ色です（単色の塗りつぶしになっていませんか）");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
