using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tests.Shared.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.Network
{
    /// <summary>
    /// <see cref="AppPathsBootstrap.TryConfigureDocumentsRoot"/>（#112）のテスト。
    /// <c>-tq-documents-root</c> に不正な値（空文字・相対パス等）が渡された場合でも
    /// 起動を止めず、既定の Documents ルートへフォールバックすることを確認する
    /// （<c>AppPathsBootstrapTests</c> の <c>-tq-data-root</c> 版と同じ方針）。
    /// </summary>
    public class AppPathsBootstrapDocumentsRootTests
    {
        private static readonly Regex FallbackLogPattern = new Regex(
            @"^\[AppPathsBootstrap\] -tq-documents-root が不正なため既定の Documents ルートを使います");

        private string _originalEnvironmentValue;

        [SetUp]
        public void SuppressDocumentsRootEnvironmentVariable()
        {
            // DocumentsPaths.Root は環境変数を ConfigureDefault より高い優先度で読むため、
            // フォールバックの確認が検証にならないよう、テストの間だけ退避して空にする
            // （AppPathsBootstrapTests の TSUMUGI_DATA_ROOT と同じ理由）。
            _originalEnvironmentValue = Environment.GetEnvironmentVariable(DocumentsPaths.RootEnvironmentVariable);
            Environment.SetEnvironmentVariable(DocumentsPaths.RootEnvironmentVariable, null);
            DocumentsPaths.Reset();
        }

        [TearDown]
        public void ResetDocumentsPaths()
        {
            DocumentsPaths.Reset();
            Environment.SetEnvironmentVariable(DocumentsPaths.RootEnvironmentVariable, _originalEnvironmentValue);

            // アセンブリ単位の隔離（EditModeTestAssemblySetUp）を戻す（DocumentsPathsTests と同じ理由）。
            DocumentsRootScope.ReapplyCurrent();
        }

        private static string MakeRoot(string suffix)
            => Path.Combine(Path.GetTempPath(), "tq-apppathsbootstrap-documents-" + suffix);

        [Test]
        public void TryConfigureDocumentsRoot_ValidAbsolutePath_ReturnsTrueAndConfigures()
        {
            var path = MakeRoot("valid");

            Assert.IsTrue(AppPathsBootstrap.TryConfigureDocumentsRoot(path));
            Assert.AreEqual(path, DocumentsPaths.Root);
        }

        [Test]
        public void TryConfigureDocumentsRoot_EmptyValue_ReturnsFalseAndKeepsDefault()
        {
            var fallback = MakeRoot("fallback");
            DocumentsPaths.ConfigureDefault(fallback);

            LogAssert.Expect(LogType.Error, FallbackLogPattern);

            bool result = false;
            Assert.DoesNotThrow(() => result = AppPathsBootstrap.TryConfigureDocumentsRoot(string.Empty));

            Assert.IsFalse(result, "空文字は不正な値として false を返すはず。");
            Assert.AreEqual(fallback, DocumentsPaths.Root, "不正な値のときは既定値のままであるはず。");
        }

        [Test]
        public void TryConfigureDocumentsRoot_UnderAssetsFolder_ReturnsFalseAndKeepsDefault()
        {
            // PR #118 レビュー M-1: Awake は Application.dataPath を forbiddenPrefix として渡す。
            var fallback = MakeRoot("fallback-assets");
            DocumentsPaths.ConfigureDefault(fallback);
            var assetsFolder = Application.dataPath;

            LogAssert.Expect(LogType.Error, FallbackLogPattern);

            var result = AppPathsBootstrap.TryConfigureDocumentsRoot(
                Path.Combine(assetsFolder, "TsumugiQuiz", "Documents"), assetsFolder);

            Assert.IsFalse(result, "Assets/ 配下は不正な値として false を返すはず。");
            Assert.AreEqual(fallback, DocumentsPaths.Root, "不正な値のときは既定値のままであるはず。");
        }

        [Test]
        public void TryConfigureDocumentsRoot_PathWithDotDot_ConfiguresNormalizedPath()
        {
            var parent = MakeRoot("normalize");

            Assert.IsTrue(
                AppPathsBootstrap.TryConfigureDocumentsRoot(Path.Combine(parent, "sub", "..", "used")));
            Assert.AreEqual(Path.Combine(parent, "used"), DocumentsPaths.Root);
        }

        [Test]
        public void TryDisableInvalidEnvironmentRoot_InvalidValue_ClearsVariableAndFallsBack()
        {
            // PR #118 レビュー M-4: 不正な環境変数を放置すると DocumentsPaths.Root が
            // 参照のたびに例外を投げ、HostSetup の QuestionLibrary 生成まで巻き添えで落ちる。
            var fallback = MakeRoot("fallback-env");
            DocumentsPaths.ConfigureDefault(fallback);
            Environment.SetEnvironmentVariable(DocumentsPaths.RootEnvironmentVariable, "relative\\path");
            Assert.Throws<ArgumentException>(
                () => _ = DocumentsPaths.Root, "前提: 不正な環境変数があると Root は例外を投げる。");

            LogAssert.Expect(LogType.Error, new Regex(
                @"^\[AppPathsBootstrap\] 環境変数 TSUMUGI_DOCUMENTS_ROOT が不正なため"));

            var disabled = AppPathsBootstrap.TryDisableInvalidEnvironmentRoot(
                DocumentsPaths.RootEnvironmentVariable, "Documents ルート");

            Assert.IsTrue(disabled, "不正な値は無視されるはず。");
            Assert.IsNull(
                Environment.GetEnvironmentVariable(DocumentsPaths.RootEnvironmentVariable),
                "このプロセスの環境変数は消えるはず。");
            Assert.AreEqual(fallback, DocumentsPaths.Root, "既定値へフォールバックするはず。");
        }

        [Test]
        public void TryDisableInvalidEnvironmentRoot_ValidValue_KeepsVariable()
        {
            var envRoot = MakeRoot("env-valid");
            Environment.SetEnvironmentVariable(DocumentsPaths.RootEnvironmentVariable, envRoot);

            var disabled = AppPathsBootstrap.TryDisableInvalidEnvironmentRoot(
                DocumentsPaths.RootEnvironmentVariable, "Documents ルート");

            Assert.IsFalse(disabled, "妥当な値は無視されないはず。");
            Assert.AreEqual(envRoot, Environment.GetEnvironmentVariable(DocumentsPaths.RootEnvironmentVariable));
            Assert.AreEqual(envRoot, DocumentsPaths.Root);
        }

        [Test]
        public void TryDisableInvalidEnvironmentRoot_NotSet_DoesNothing()
        {
            Assert.IsFalse(AppPathsBootstrap.TryDisableInvalidEnvironmentRoot(
                DocumentsPaths.RootEnvironmentVariable, "Documents ルート"));
        }

        [Test]
        public void TryConfigureDocumentsRoot_RelativePath_ReturnsFalseAndKeepsDefault()
        {
            var fallback = MakeRoot("fallback2");
            DocumentsPaths.ConfigureDefault(fallback);

            LogAssert.Expect(LogType.Error, FallbackLogPattern);

            bool result = false;
            Assert.DoesNotThrow(() => result = AppPathsBootstrap.TryConfigureDocumentsRoot("relative\\path"));

            Assert.IsFalse(result, "相対パスは不正な値として false を返すはず。");
            Assert.AreEqual(fallback, DocumentsPaths.Root);
        }
    }
}
