using System;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.UI;

namespace TsumugiQuiz.Tests.EditMode.UI
{
    /// <summary>
    /// <see cref="LaunchOptionsRunner"/> の純ロジック部分（初期 View の決定・自動ホスト/自動参加の
    /// 1 回限りの消費・CLI 引数取得）のテスト（issue #8）。
    /// <see cref="LaunchOptionsRunner.ApplyWindowPlacement"/> は Unity のウィンドウ API を呼ぶため、
    /// ここでは検証しない（<see cref="LaunchWindowRect"/> の解析自体は別テストで検証済み）。
    /// </summary>
    public class LaunchOptionsRunnerTests
    {
        [TearDown]
        public void ResetLaunchOptions()
        {
            // 他のテスト（実引数を使うもの）に影響を残さないよう、必ずリセットする。
            LaunchOptionsRunner.ResetForTesting();
        }

        [Test]
        public void DetermineInitialView_NotConsented_AlwaysReturnsTerms()
        {
            LaunchOptionsRunner.SetOptionsForTesting(CommandLineOptions.Parse(new[] { "-tq-host" }));

            Assert.AreEqual(ViewNames.Terms, LaunchOptionsRunner.DetermineInitialView(hasConsented: false));
        }

        [Test]
        public void DetermineInitialView_ConsentedNoOptions_ReturnsTitle()
        {
            LaunchOptionsRunner.SetOptionsForTesting(CommandLineOptions.Empty);

            Assert.AreEqual(ViewNames.Title, LaunchOptionsRunner.DetermineInitialView(hasConsented: true));
        }

        [Test]
        public void DetermineInitialView_ConsentedWithHostFlag_ReturnsHostSetup()
        {
            LaunchOptionsRunner.SetOptionsForTesting(CommandLineOptions.Parse(new[] { "-tq-host" }));

            Assert.AreEqual(ViewNames.HostSetup, LaunchOptionsRunner.DetermineInitialView(hasConsented: true));
        }

        [Test]
        public void DetermineInitialView_ConsentedWithJoinCode_ReturnsJoin()
        {
            LaunchOptionsRunner.SetOptionsForTesting(CommandLineOptions.Parse(new[] { "-tq-join", "60N0-0HE7-K12R" }));

            Assert.AreEqual(ViewNames.Join, LaunchOptionsRunner.DetermineInitialView(hasConsented: true));
        }

        [Test]
        public void DetermineInitialView_NotConsented_WithJoinCode_AlsoReturnsTerms()
        {
            // レビュー M-7: -tq-join でも同意ゲート（#37）は迂回されないことを個別に確認する
            // （-tq-host 側は DetermineInitialView_NotConsented_AlwaysReturnsTerms で確認済み）。
            LaunchOptionsRunner.SetOptionsForTesting(CommandLineOptions.Parse(new[] { "-tq-join", "60N0-0HE7-K12R" }));

            Assert.AreEqual(ViewNames.Terms, LaunchOptionsRunner.DetermineInitialView(hasConsented: false));
        }

        [Test]
        public void DetermineInitialView_HostFlagTakesPriorityOverJoin()
        {
            LaunchOptionsRunner.SetOptionsForTesting(
                CommandLineOptions.Parse(new[] { "-tq-host", "-tq-join", "60N0-0HE7-K12R" }));

            Assert.AreEqual(ViewNames.HostSetup, LaunchOptionsRunner.DetermineInitialView(hasConsented: true));
        }

        [Test]
        public void TryConsumeAutoHost_ReturnsTrueOnceThenFalse()
        {
            LaunchOptionsRunner.SetOptionsForTesting(CommandLineOptions.Parse(new[] { "-tq-host" }));

            Assert.IsTrue(LaunchOptionsRunner.TryConsumeAutoHost());
            Assert.IsFalse(LaunchOptionsRunner.TryConsumeAutoHost());
        }

        [Test]
        public void TryConsumeAutoHost_WithoutFlag_ReturnsFalse()
        {
            LaunchOptionsRunner.SetOptionsForTesting(CommandLineOptions.Empty);

            Assert.IsFalse(LaunchOptionsRunner.TryConsumeAutoHost());
        }

        [Test]
        public void TryConsumeAutoJoin_ReturnsCodeOnceThenFalse()
        {
            LaunchOptionsRunner.SetOptionsForTesting(CommandLineOptions.Parse(new[] { "-tq-join", "60N0-0HE7-K12R" }));

            Assert.IsTrue(LaunchOptionsRunner.TryConsumeAutoJoin(out var code));
            Assert.AreEqual("60N0-0HE7-K12R", code);

            Assert.IsFalse(LaunchOptionsRunner.TryConsumeAutoJoin(out var second));
            Assert.IsNull(second);
        }

        [Test]
        public void TryGetPlayerName_ReadsTqName()
        {
            LaunchOptionsRunner.SetOptionsForTesting(CommandLineOptions.Parse(new[] { "-tq-name", "テスト太郎" }));

            Assert.IsTrue(LaunchOptionsRunner.TryGetPlayerName(out var name));
            Assert.AreEqual("テスト太郎", name);
        }

        [Test]
        public void TryGetPort_ZeroIsNotTreatedAsUnset()
        {
            LaunchOptionsRunner.SetOptionsForTesting(CommandLineOptions.Parse(new[] { "-tq-port", "0" }));

            Assert.IsTrue(LaunchOptionsRunner.TryGetPort(out var port));
            Assert.AreEqual(0, port);
        }

        // WriteJoinCodeFile は issue #71 統合後、書き出し先を独自パースの -tq-data-root ではなく
        // AppPaths.DataRoot（LaunchArguments.DataRoot に統一された AppPathsBootstrap が解決する）から
        // 得るようになった。テストでは AppPaths.Configure で明示的に一時フォルダへ差し替える。

        [Test]
        public void WriteJoinCodeFile_WritesAtomicallyUnderAppPathsDataRoot()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "tq-launchoptions-test-" + Guid.NewGuid().ToString("N"));
            AppPaths.Configure(tempDir);
            try
            {
                LaunchOptionsRunner.WriteJoinCodeFile("60N0-0HE7-K12R");

                var expectedPath = Path.Combine(tempDir, "join-code.txt");
                Assert.IsTrue(File.Exists(expectedPath), $"join-code.txt が書き出されていません: {expectedPath}");
                Assert.AreEqual("60N0-0HE7-K12R", File.ReadAllText(expectedPath));

                // H-2: 一時ファイル + File.Move によるアトミック書き込みのため、.tmp が残らないこと。
                Assert.IsFalse(File.Exists(expectedPath + ".tmp"));
            }
            finally
            {
                AppPaths.Reset();
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Test]
        public void WriteJoinCodeFile_EmptyCode_DoesNotCreateFile()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "tq-launchoptions-test-" + Guid.NewGuid().ToString("N"));
            AppPaths.Configure(tempDir);
            try
            {
                LaunchOptionsRunner.WriteJoinCodeFile(string.Empty);

                Assert.IsFalse(Directory.Exists(tempDir), "空の参加コードではデータルートすら作られないはず。");
            }
            finally
            {
                AppPaths.Reset();
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Test]
        public void WriteJoinCodeFile_DataRootUnconfigured_DoesNotThrow()
        {
            // レビュー L-2: AppPaths.DataRoot は環境変数 TSUMUGI_DATA_ROOT もフォールバック先として
            // 読むため（#71）、verify.ps1 実行時など環境変数が設定された状態では AppPaths.Reset() だけでは
            // 「本当に未設定」の状態を再現できない。テストの間だけ退避して空にし、必ず元に戻す。
            var originalEnvironmentValue = System.Environment.GetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable);
            System.Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, null);
            try
            {
                // AppPaths が未設定（Configure/ConfigureDefault のどちらも無い）状態を明示的に作る。
                AppPaths.Reset();

                // AppPaths.DataRoot が例外を投げても、LaunchOptionsRunner 側で捕まえて警告ログに留める
                // （join-code.txt の書き出し失敗で起動やホスト開始そのものを止めないため）。
                Assert.DoesNotThrow(() => LaunchOptionsRunner.WriteJoinCodeFile("60N0-0HE7-K12R"));
            }
            finally
            {
                System.Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, originalEnvironmentValue);
            }
        }
    }
}
