using System;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Editor;

namespace TsumugiQuiz.Tests.EditMode.Build
{
    /// <summary>
    /// <see cref="BuildCommand.ResolveOutputDirPath"/>（issue #142 項目 4、レビュー M-1/L-7）の
    /// 相対パス解決基準・不正な値の拒否を検証する。-buildOutputDir に相対パスを指定した場合、
    /// Unity バッチモードの起動時カレントディレクトリではなく呼び出し側が渡したプロジェクトルート
    /// 基準で解決されることを確認する。
    /// </summary>
    public sealed class BuildCommandTests
    {
        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tq-build-command-test-root"));

        [Test]
        public void ResolveOutputDirPath_相対パスはprojectRoot基準で解決される()
        {
            var projectRoot = ProjectRoot;

            var resolved = BuildCommand.ResolveOutputDirPath(@"Builds\verify\Windows", projectRoot);

            var expected = Path.GetFullPath(Path.Combine(projectRoot, "Builds", "verify", "Windows"));
            Assert.AreEqual(expected, resolved);
        }

        [Test]
        public void ResolveOutputDirPath_絶対パスはprojectRootを無視してそのまま使われる()
        {
            var projectRoot = ProjectRoot;
            var absolutePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tq-build-command-test-absolute"));

            var resolved = BuildCommand.ResolveOutputDirPath(absolutePath, projectRoot);

            Assert.AreEqual(absolutePath, resolved);
        }

        // レビュー L-7: ".." を含む相対パスでも projectRoot 基準で正しく解決されること
        // （projectRoot の兄弟ディレクトリへ抜けるケース）。
        [Test]
        public void ResolveOutputDirPath_ドットドットを含む相対パスはprojectRoot基準で解決される()
        {
            var projectRoot = ProjectRoot;

            var resolved = BuildCommand.ResolveOutputDirPath(@"..\sibling\Builds\Windows", projectRoot);

            var expected = Path.GetFullPath(Path.Combine(projectRoot, "..", "sibling", "Builds", "Windows"));
            Assert.AreEqual(expected, resolved);
        }

        // レビュー L-7: 末尾に区切り文字が付いていても解決結果が変わらないこと。
        [Test]
        public void ResolveOutputDirPath_末尾区切りがあっても同じ結果になる()
        {
            var projectRoot = ProjectRoot;

            var withTrailingSeparator = BuildCommand.ResolveOutputDirPath(@"Builds\Windows\", projectRoot);
            var withoutTrailingSeparator = BuildCommand.ResolveOutputDirPath(@"Builds\Windows", projectRoot);

            Assert.AreEqual(withoutTrailingSeparator, withTrailingSeparator);
        }

        // レビュー M-1: rawValue が空/空白の場合、projectRoot へ無言で解決されず例外になること。
        [TestCase("")]
        [TestCase("   ")]
        public void ResolveOutputDirPath_空文字または空白は例外(string rawValue)
        {
            var projectRoot = ProjectRoot;

            Assert.Throws<ArgumentException>(() => BuildCommand.ResolveOutputDirPath(rawValue, projectRoot));
        }

        // レビュー M-1: 解決結果がプロジェクトルート自体と一致する場合（例: "."）も拒否すること。
        [Test]
        public void ResolveOutputDirPath_解決結果がprojectRoot自体だと例外()
        {
            var projectRoot = ProjectRoot;

            Assert.Throws<ArgumentException>(() => BuildCommand.ResolveOutputDirPath(".", projectRoot));
        }
    }
}
