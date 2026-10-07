using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Questions;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="QuestionFolderLauncher"/> の検証。実際に explorer を起動すると実機依存になり
    /// EditMode テストとして不適切なため、processStarter を差し替えて「どのパスで何を起動しようと
    /// したか」だけを検証する（実際にエクスプローラーが開くことの確認は issue #29 の受け入れ条件通り
    /// 実機確認で行う）。
    /// レビュー M5/M6: フォルダの作成は行わない（<see cref="TsumugiQuiz.Questions.QuestionLibrary"/> の
    /// 責務）ため、フォルダが存在しない場合は何もせず false を返すことを検証する。
    /// </summary>
    public class QuestionFolderLauncherTests
    {
        [Test]
        public void OpenInExplorer_FolderExists_InvokesProcessStarterWithFullPath()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TsumugiQuizTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string capturedPath = null;
                var result = QuestionFolderLauncher.OpenInExplorer(tempDir, path => capturedPath = path);

                Assert.IsTrue(result);
                Assert.AreEqual(Path.GetFullPath(tempDir), capturedPath);
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Test]
        public void OpenInExplorer_RelativeLikePath_IsNormalizedWithGetFullPath()
        {
            // M5: 入口で Path.GetFullPath により正規化する。相対パス的な入力（"." を含む）でも、
            // 起動処理へは正規化済みの絶対パスが渡ること。
            var tempDir = Path.Combine(Path.GetTempPath(), "TsumugiQuizTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var pathWithRedundantSegment = Path.Combine(tempDir, ".", ".");
                string capturedPath = null;

                var result = QuestionFolderLauncher.OpenInExplorer(pathWithRedundantSegment, path => capturedPath = path);

                Assert.IsTrue(result);
                Assert.AreEqual(Path.GetFullPath(tempDir), capturedPath);
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Test]
        public void OpenInExplorer_FolderMissing_ReturnsFalseWithoutCreatingOrInvokingProcessStarter()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TsumugiQuizTests_" + Guid.NewGuid().ToString("N"));
            Assert.IsFalse(Directory.Exists(tempDir));

            var starterInvoked = false;
            var result = QuestionFolderLauncher.OpenInExplorer(tempDir, _ => starterInvoked = true);

            Assert.IsFalse(result, "フォルダが存在しない場合は false を返すこと");
            Assert.IsFalse(starterInvoked, "起動処理は呼ばれないこと");
            Assert.IsFalse(Directory.Exists(tempDir), "フォルダの作成は行わないこと（QuestionLibrary の責務）");
        }

        [Test]
        public void OpenInExplorer_PathPointsToFile_ReturnsFalse()
        {
            // ディレクトリではなくファイルを指すパスは開けないため false を返す。
            var tempFile = Path.Combine(Path.GetTempPath(), "TsumugiQuizTests_" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(tempFile, "x");

            try
            {
                var starterInvoked = false;
                var result = QuestionFolderLauncher.OpenInExplorer(tempFile, _ => starterInvoked = true);

                Assert.IsFalse(result);
                Assert.IsFalse(starterInvoked);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Test]
        public void OpenInExplorer_ProcessStarterThrows_ReturnsFalseAndDoesNotThrow()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TsumugiQuizTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // 失敗時は Debug.LogError でも記録するため、テストフレームワークが
                // 「未処理のログ」として失敗扱いにしないよう期待しておく。
                LogAssert.Expect(LogType.Error, new Regex(@"\[QuestionFolderLauncher\].*boom"));

                bool result = true;
                Assert.DoesNotThrow(() =>
                {
                    result = QuestionFolderLauncher.OpenInExplorer(tempDir, _ => throw new InvalidOperationException("boom"));
                });

                Assert.IsFalse(result);
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Test]
        public void OpenInExplorer_NullOrEmptyPath_Throws()
        {
            Assert.Throws<ArgumentException>(() => QuestionFolderLauncher.OpenInExplorer(null, _ => { }));
            Assert.Throws<ArgumentException>(() => QuestionFolderLauncher.OpenInExplorer(string.Empty, _ => { }));
        }
    }
}
