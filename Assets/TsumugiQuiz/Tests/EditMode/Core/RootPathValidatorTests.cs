using System;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="RootPathValidator"/>（PR #118 レビュー M-1）の検証。
    /// <see cref="AppPaths"/> / <see cref="DocumentsPaths"/> が受け取るルートパスの
    /// 正規化（<c>..</c> の解決・末尾セパレータの除去）と、<c>Assets/</c> 配下の拒否を固定する。
    /// </summary>
    public class RootPathValidatorTests
    {
        private const string Label = "テスト対象の root";
        private const string ParameterName = "root";

        private static string Validate(string root, string forbiddenPrefix = null)
            => RootPathValidator.Validate(root, Label, ParameterName, forbiddenPrefix);

        [Test]
        public void Validate_PathWithDotDot_ReturnsResolvedPath()
        {
            var root = Path.Combine(Path.GetTempPath(), "tq-validator", "sub", "..", "used");

            var result = Validate(root);

            Assert.AreEqual(Path.Combine(Path.GetTempPath(), "tq-validator", "used"), result);
            StringAssert.DoesNotContain("..", result);
        }

        [Test]
        public void Validate_PathWithForwardSlashes_ReturnsWindowsSeparators()
        {
            // Application.persistentDataPath は Windows でも "/" 区切りで返る。
            var root = "C:/Users/example/AppData/LocalLow/Company/Product";

            Assert.AreEqual(@"C:\Users\example\AppData\LocalLow\Company\Product", Validate(root));
        }

        [Test]
        public void Validate_TrailingSeparator_IsTrimmed()
        {
            var expected = Path.Combine(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), "tq-validator-trail");

            Assert.AreEqual(expected, Validate(expected + Path.DirectorySeparatorChar));
        }

        [Test]
        public void Validate_DriveRoot_KeepsTrailingSeparator()
        {
            // "C:" は「C ドライブのカレントディレクトリ」を意味してしまうため、ここだけは末尾を残す。
            Assert.AreEqual(@"C:\", Validate(@"C:\"));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("relative\\path")]
        [TestCase("relative/path")]
        public void Validate_InvalidRoot_Throws(string invalidRoot)
        {
            Assert.Throws<ArgumentException>(() => Validate(invalidRoot));
        }

        [Test]
        public void Validate_Null_Throws()
        {
            Assert.Throws<ArgumentException>(() => Validate(null));
        }

        [Test]
        public void Validate_UnderForbiddenPrefix_Throws()
        {
            var forbidden = Path.Combine(Path.GetTempPath(), "tq-project", "Assets");
            var root = Path.Combine(forbidden, "TsumugiQuiz", "Runtime");

            var ex = Assert.Throws<ArgumentException>(() => Validate(root, forbidden));
            StringAssert.Contains("配下は指定できません", ex.Message);
        }

        [Test]
        public void Validate_ForbiddenPrefixItself_Throws()
        {
            var forbidden = Path.Combine(Path.GetTempPath(), "tq-project", "Assets");

            Assert.Throws<ArgumentException>(() => Validate(forbidden, forbidden));
        }

        [Test]
        public void Validate_DotDotEscapingIntoForbiddenPrefix_Throws()
        {
            // 正規化前は Assets 配下に見えないが、解決すると Assets 配下になるケース。
            var projectRoot = Path.Combine(Path.GetTempPath(), "tq-project");
            var forbidden = Path.Combine(projectRoot, "Assets");
            var root = Path.Combine(projectRoot, "Builds", "..", "Assets", "Data");

            Assert.Throws<ArgumentException>(() => Validate(root, forbidden));
        }

        [Test]
        public void Validate_SiblingOfForbiddenPrefix_IsAllowed()
        {
            // "Assets" と "AssetsBackup" を取り違えないこと（前方一致だけで判定しない）。
            var projectRoot = Path.Combine(Path.GetTempPath(), "tq-project");
            var forbidden = Path.Combine(projectRoot, "Assets");
            var root = Path.Combine(projectRoot, "AssetsBackup");

            Assert.AreEqual(root, Validate(root, forbidden));
        }

        [Test]
        public void Validate_ForbiddenPrefixWithTrailingSeparator_StillRejectsChild()
        {
            var forbidden = Path.Combine(Path.GetTempPath(), "tq-project", "Assets") + Path.DirectorySeparatorChar;
            var root = Path.Combine(Path.GetTempPath(), "tq-project", "Assets", "Data");

            Assert.Throws<ArgumentException>(() => Validate(root, forbidden));
        }

        [Test]
        public void IsSameOrUnder_ComparesNormalizedPaths()
        {
            var parent = Path.Combine(Path.GetTempPath(), "tq-parent");

            Assert.IsTrue(RootPathValidator.IsSameOrUnder(parent, parent));
            Assert.IsTrue(RootPathValidator.IsSameOrUnder(Path.Combine(parent, "child"), parent));
            Assert.IsFalse(RootPathValidator.IsSameOrUnder(parent + "2", parent));
            Assert.IsFalse(RootPathValidator.IsSameOrUnder(null, parent));
            Assert.IsFalse(RootPathValidator.IsSameOrUnder(parent, null));
        }

        [Test]
        public void IsSameOrUnder_DriveRootParent_ChildPathIsUnder()
        {
            // issue #122-2: 親がドライブ直下（"C:\"）のとき、TrimTrailingSeparators が
            // 末尾のセパレータを落とさないため "C:\" + セパレータ = "C:\\" になり、
            // "C:\Users\..." のような実在のパスと前方一致しない不具合があった。
            Assert.IsTrue(RootPathValidator.IsSameOrUnder(@"C:\Users\example", @"C:\"));
            Assert.IsTrue(RootPathValidator.IsSameOrUnder(@"C:\", @"C:\"));
        }

        [Test]
        public void IsSameOrUnder_DriveRootParent_DifferentDriveIsNotUnder()
        {
            Assert.IsFalse(RootPathValidator.IsSameOrUnder(@"D:\Users\example", @"C:\"));
        }

        [Test]
        public void IsSameOrUnder_UncPath_ChildPathIsUnder()
        {
            // issue #122 レビュー L-2: UNC パス（"\\server\share" のような、ドライブ文字を持たないルート）
            // でも既存の前方一致ロジックが成立することの回帰テスト。
            Assert.IsTrue(RootPathValidator.IsSameOrUnder(@"\\server\share\dir", @"\\server\share"));
        }
    }
}
