using NUnit.Framework;

namespace TsumugiQuiz.Tests.EditMode
{
    /// <summary>
    /// プロジェクトのセットアップ（パッケージ解決・asmdef 構成・テスト実行環境）が
    /// 壊れていないことを確認するための最小スモークテスト。
    /// </summary>
    public class SmokeTests
    {
        [Test]
        public void Setup_IsHealthy()
        {
            Assert.Pass("EditMode test infrastructure is working.");
        }
    }
}
