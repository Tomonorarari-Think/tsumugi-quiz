using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.Network
{
    /// <summary>
    /// <see cref="AppPathsBootstrap.TryConfigureDataRoot"/>（issue #8 レビュー M-6）のテスト。
    /// <c>-tq-data-root</c> に不正な値（空文字・相対パス等）が渡された場合、
    /// <see cref="AppPaths.Configure"/> の <see cref="System.ArgumentException"/> を捕まえて
    /// 既定のデータルートへフォールバックし、起動を止めないことを確認する。
    /// <see cref="AppPathsBootstrap.Awake"/> 自体は MonoBehaviour のライフサイクルに依存するため、
    /// ここでは internal な <see cref="AppPathsBootstrap.TryConfigureDataRoot"/> を直接呼ぶ。
    /// </summary>
    public class AppPathsBootstrapTests
    {
        private string _originalEnvironmentValue;

        [SetUp]
        public void SuppressDataRootEnvironmentVariable()
        {
            // レビュー L-2 と同じ理由: AppPaths.DataRoot は環境変数 TSUMUGI_DATA_ROOT を
            // ConfigureDefault より高い優先度で読む（#71）。scripts/verify.ps1 実行時など
            // 環境変数が設定された状態では、ConfigureDefault によるフォールバック確認が
            // 検証にならなくなるため、テストの間だけ退避して空にする。
            _originalEnvironmentValue = System.Environment.GetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable);
            System.Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, null);
        }

        [TearDown]
        public void ResetAppPaths()
        {
            // AppPaths は静的な状態を持つため、他のテストに影響を残さないよう必ずリセットする。
            AppPaths.Reset();
            System.Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, _originalEnvironmentValue);
        }

        [Test]
        public void TryConfigureDataRoot_ValidAbsolutePath_ReturnsTrueAndConfigures()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tq-apppathsbootstrap-test");

            Assert.IsTrue(AppPathsBootstrap.TryConfigureDataRoot(path));
            Assert.AreEqual(path, AppPaths.DataRoot);
        }

        [Test]
        public void TryConfigureDataRoot_EmptyValue_ReturnsFalseAndDoesNotThrow()
        {
            // 既定値をあらかじめ登録しておく（Awake が Configure より先に ConfigureDefault するのと同じ順序）。
            var fallback = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tq-apppathsbootstrap-fallback");
            AppPaths.ConfigureDefault(fallback);

            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex(
                @"^\[AppPathsBootstrap\] -tq-data-root が不正なため既定のデータルートを使います"));

            bool result = false;
            Assert.DoesNotThrow(() => result = AppPathsBootstrap.TryConfigureDataRoot(string.Empty));

            Assert.IsFalse(result, "空文字は不正な値として false を返すはず。");
            Assert.AreEqual(fallback, AppPaths.DataRoot, "不正な値のときは既定値のままであるはず。");
        }

        [Test]
        public void TryConfigureDataRoot_RelativePath_ReturnsFalseAndDoesNotThrow()
        {
            var fallback = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tq-apppathsbootstrap-fallback2");
            AppPaths.ConfigureDefault(fallback);

            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex(
                @"^\[AppPathsBootstrap\] -tq-data-root が不正なため既定のデータルートを使います"));

            bool result = false;
            Assert.DoesNotThrow(() => result = AppPathsBootstrap.TryConfigureDataRoot("relative\\path"));

            Assert.IsFalse(result, "相対パスは不正な値として false を返すはず。");
            Assert.AreEqual(fallback, AppPaths.DataRoot);
        }
    }
}
