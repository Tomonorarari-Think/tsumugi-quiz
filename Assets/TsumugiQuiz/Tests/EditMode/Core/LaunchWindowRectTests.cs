using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <c>-tq-window</c> の "x,y,w,h" 解析（<see cref="LaunchWindowRect.TryParse"/>）のテスト（issue #8）。
    /// </summary>
    public class LaunchWindowRectTests
    {
        [Test]
        public void TryParse_ValidValue_ReturnsRect()
        {
            Assert.IsTrue(LaunchWindowRect.TryParse("100,200,960,540", out var rect));
            Assert.AreEqual(100, rect.X);
            Assert.AreEqual(200, rect.Y);
            Assert.AreEqual(960, rect.Width);
            Assert.AreEqual(540, rect.Height);
        }

        [Test]
        public void TryParse_AllowsNegativeXY()
        {
            // マルチモニタ環境では左側のモニタが負の座標を持ち得る。
            Assert.IsTrue(LaunchWindowRect.TryParse("-100,-50,800,600", out var rect));
            Assert.AreEqual(-100, rect.X);
            Assert.AreEqual(-50, rect.Y);
        }

        [Test]
        public void TryParse_TrimsWhitespaceAroundEachElement()
        {
            Assert.IsTrue(LaunchWindowRect.TryParse(" 0 , 0 , 800 , 600 ", out var rect));
            Assert.AreEqual(0, rect.X);
            Assert.AreEqual(800, rect.Width);
        }

        [TestCase("100,200,960")]
        [TestCase("100,200,960,540,1")]
        [TestCase("")]
        [TestCase(null)]
        public void TryParse_WrongElementCount_ReturnsFalse(string raw)
        {
            Assert.IsFalse(LaunchWindowRect.TryParse(raw, out _));
        }

        [Test]
        public void TryParse_NonNumericElement_ReturnsFalse()
        {
            Assert.IsFalse(LaunchWindowRect.TryParse("a,0,800,600", out _));
        }

        [TestCase("0,0,0,600")]
        [TestCase("0,0,800,0")]
        [TestCase("0,0,-1,600")]
        public void TryParse_NonPositiveWidthOrHeight_ReturnsFalse(string raw)
        {
            Assert.IsFalse(LaunchWindowRect.TryParse(raw, out _));
        }

        [Test]
        public void TryParse_WidthOrHeightAtMaxDimension_ReturnsTrue()
        {
            // レビュー L-4: 上限ちょうどは許可する（境界値）。
            Assert.IsTrue(LaunchWindowRect.TryParse($"0,0,{LaunchWindowRect.MaxDimension},600", out _));
            Assert.IsTrue(LaunchWindowRect.TryParse($"0,0,800,{LaunchWindowRect.MaxDimension}", out _));
        }

        [Test]
        public void TryParse_WidthOrHeightExceedsMaxDimension_ReturnsFalse()
        {
            // レビュー L-4: 桁の打ち間違い等による極端な値を弾く。
            var tooLarge = LaunchWindowRect.MaxDimension + 1;
            Assert.IsFalse(LaunchWindowRect.TryParse($"0,0,{tooLarge},600", out _));
            Assert.IsFalse(LaunchWindowRect.TryParse($"0,0,800,{tooLarge}", out _));
        }
    }
}
