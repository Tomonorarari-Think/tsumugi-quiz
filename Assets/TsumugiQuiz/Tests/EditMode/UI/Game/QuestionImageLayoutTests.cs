using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.UI.Views.Game;
using UnityEngine;

namespace TsumugiQuiz.Tests.EditMode.UI.Game
{
    /// <summary>
    /// <see cref="QuestionImageLayout"/> のテスト（issue #185 / #193。問題画像エリアの定数と寸法の判定）。
    /// 実際の大きさは USS で決まるため、PlayMode の GameViewQuestionImageSceneTests / GameViewVerticalFitSceneTests が
    /// 実寸を検証する。
    /// </summary>
    public class QuestionImageLayoutTests
    {
        [TestCase(1, 1)]
        [TestCase(800, 600)]
        [TestCase(1, 4096)]
        public void IsDisplayableSize_PositiveSize_ReturnsTrue(int width, int height)
        {
            Assert.IsTrue(QuestionImageLayout.IsDisplayableSize(width, height));
        }

        [TestCase(0, 10)]
        [TestCase(10, 0)]
        [TestCase(-5, 10)]
        [TestCase(10, -5)]
        public void IsDisplayableSize_NonPositiveSize_ReturnsFalse(int width, int height)
        {
            Assert.IsFalse(QuestionImageLayout.IsDisplayableSize(width, height));
        }

        [Test]
        public void DisplayHeightBounds_AreOrdered()
        {
            Assert.Greater(QuestionImageLayout.MinDisplayHeight, 0f);
            Assert.Greater(QuestionImageLayout.MaxDisplayHeight, QuestionImageLayout.MinDisplayHeight);
        }

        [Test]
        public void AreaHeights_IncludeVerticalPadding()
        {
            Assert.AreEqual(
                QuestionImageLayout.MinDisplayHeight + QuestionImageLayout.AreaPadding * 2f, QuestionImageLayout.MinAreaHeight);
            Assert.AreEqual(
                QuestionImageLayout.MaxDisplayHeight + QuestionImageLayout.AreaPadding * 2f, QuestionImageLayout.MaxAreaHeight);
        }

        /// <summary>
        /// PR #198 レビュー L-2: 画像エリアの大きさは USS だけで決まるので、USS（theme-views-game.uss の
        /// <c>.game-question-image-area</c>）の min-height / max-height / padding と <see cref="QuestionImageLayout"/> の
        /// 定数（PlayMode テストが実寸の比較に使う）が食い違っていないことを、USS を読んで確かめる。
        /// </summary>
        [Test]
        public void UssImageArea_MatchesLayoutConstants()
        {
            var block = ReadRuleBlock(GameUssRelativePath, ".game-question-image-area");

            Assert.AreEqual(QuestionImageLayout.MinAreaHeight, ReadPx(block, "min-height"), "USS の min-height と MinAreaHeight が一致しない。");
            Assert.AreEqual(QuestionImageLayout.MaxAreaHeight, ReadPx(block, "max-height"), "USS の max-height と MaxAreaHeight が一致しない。");
            Assert.AreEqual(QuestionImageLayout.AreaPadding, ReadPx(block, "padding"), "USS の padding と AreaPadding が一致しない。");
        }

        /// <summary>画像そのもの（<c>.game-question-image</c>）はエリアの padding の内側いっぱいに絶対配置する。</summary>
        [Test]
        public void UssImage_IsInsetByAreaPadding()
        {
            var block = ReadRuleBlock(GameUssRelativePath, ".game-question-image");

            Assert.AreEqual("absolute", ReadValue(block, "position"));
            foreach (var side in new[] { "left", "top", "right", "bottom" })
            {
                Assert.AreEqual(QuestionImageLayout.AreaPadding, ReadPx(block, side), $"{side} が AreaPadding と一致しない。");
            }
        }

        private const string GameUssRelativePath = "TsumugiQuiz/UI/Styles/theme-views-game.uss";
        private const string ThemeUssRelativePath = "TsumugiQuiz/UI/Styles/theme.uss";

        /// <summary>コメントを除いた USS から、指定したセレクタ単独の規則の本体を取り出す。</summary>
        private static string ReadRuleBlock(string relativePath, string selector)
        {
            var uss = StripComments(File.ReadAllText(Path.Combine(Application.dataPath, relativePath)));
            var match = Regex.Match(uss, @"(^|\})\s*" + Regex.Escape(selector) + @"\s*\{(?<body>[^}]*)\}");
            Assert.IsTrue(match.Success, $"{relativePath} に {selector} の規則が見つかりません。");
            return match.Groups["body"].Value;
        }

        private static string ReadValue(string block, string property)
        {
            var match = Regex.Match(block, @"(^|;|\s)" + Regex.Escape(property) + @"\s*:\s*(?<value>[^;]+);");
            Assert.IsTrue(match.Success, $"{property} が指定されていません。");
            return match.Groups["value"].Value.Trim();
        }

        /// <summary>px 値を読む。<c>var(--name)</c> なら theme.uss の :root から解決する。</summary>
        private static float ReadPx(string block, string property)
        {
            var value = ReadValue(block, property);
            var variable = Regex.Match(value, @"^var\((?<name>--[a-z0-9-]+)\)$");
            if (variable.Success)
            {
                value = ReadRootVariable(variable.Groups["name"].Value);
            }

            var px = Regex.Match(value, @"^(?<number>[0-9.]+)px$");
            Assert.IsTrue(px.Success, $"{property}: px の単一値ではありません（{value}）。");
            return float.Parse(px.Groups["number"].Value, CultureInfo.InvariantCulture);
        }

        private static string ReadRootVariable(string name)
        {
            var uss = StripComments(File.ReadAllText(Path.Combine(Application.dataPath, ThemeUssRelativePath)));
            var match = Regex.Match(uss, Regex.Escape(name) + @"\s*:\s*(?<value>[^;]+);");
            Assert.IsTrue(match.Success, $"theme.uss に {name} が見つかりません。");
            return match.Groups["value"].Value.Trim();
        }

        private static string StripComments(string uss) =>
            Regex.Replace(uss, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
    }
}
