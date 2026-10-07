using System;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Tests.Shared.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="DocumentsPaths"/> の優先順位（明示設定 &gt; 環境変数 &gt; 既定値 &gt; 実ユーザーの Documents）と
    /// 入力検証を検証する（#112。<see cref="AppPaths"/> の <c>AppPathsTests</c> と同じ作法）。
    ///
    /// <see cref="DocumentsPaths"/> は静的な状態を持つため、各テストは自分が使う値を明示的に設定し、
    /// 終了時に必ず <see cref="DocumentsPaths.Reset"/> と環境変数の復元を行う
    /// （他のテストの実行順に依存しないため）。
    /// </summary>
    public class DocumentsPathsTests
    {
        private string _originalEnvironmentValue;

        [SetUp]
        public void SetUp()
        {
            // 他のテスト（や実行環境）が既に環境変数を設定しているケースに備え、退避してから空にする。
            _originalEnvironmentValue = Environment.GetEnvironmentVariable(DocumentsPaths.RootEnvironmentVariable);
            Environment.SetEnvironmentVariable(DocumentsPaths.RootEnvironmentVariable, null);
            DocumentsPaths.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            DocumentsPaths.Reset();
            Environment.SetEnvironmentVariable(DocumentsPaths.RootEnvironmentVariable, _originalEnvironmentValue);

            // 本クラスは優先順位そのものを検証するために Reset を多用するため、後始末で
            // アセンブリ単位の隔離（EditModeTestAssemblySetUp）を必ず戻す。
            DocumentsRootScope.ReapplyCurrent();
        }

        private static string MakeRoot(string suffix)
            => Path.Combine(Path.GetTempPath(), "TsumugiQuizDocumentsPathsTests_" + suffix);

        [Test]
        public void Root_NothingConfigured_ReturnsUserDocumentsFolder()
        {
            var expected = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            Assume.That(
                expected,
                Is.Not.Null.And.Not.Empty,
                "実行環境の Documents フォルダを取得できないため、この検証は成立しない。");

            Assert.AreEqual(expected, DocumentsPaths.Root);
        }

        [Test]
        public void Root_DefaultOnly_ReturnsDefault()
        {
            var defaultRoot = MakeRoot("default");

            DocumentsPaths.ConfigureDefault(defaultRoot);

            Assert.AreEqual(defaultRoot, DocumentsPaths.Root);
        }

        [Test]
        public void Root_EnvironmentVariable_OverridesDefault()
        {
            var defaultRoot = MakeRoot("default");
            var envRoot = MakeRoot("env");
            DocumentsPaths.ConfigureDefault(defaultRoot);
            Environment.SetEnvironmentVariable(DocumentsPaths.RootEnvironmentVariable, envRoot);

            Assert.AreEqual(envRoot, DocumentsPaths.Root);
        }

        [Test]
        public void Root_ExplicitConfigure_OverridesEnvironmentVariableAndDefault()
        {
            var defaultRoot = MakeRoot("default");
            var envRoot = MakeRoot("env");
            var explicitRoot = MakeRoot("explicit");
            DocumentsPaths.ConfigureDefault(defaultRoot);
            Environment.SetEnvironmentVariable(DocumentsPaths.RootEnvironmentVariable, envRoot);

            DocumentsPaths.Configure(explicitRoot);

            Assert.AreEqual(explicitRoot, DocumentsPaths.Root);
        }

        [Test]
        public void Reset_AfterConfigure_FallsBackToUserDocumentsFolder()
        {
            var expected = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            Assume.That(expected, Is.Not.Null.And.Not.Empty);
            DocumentsPaths.Configure(MakeRoot("explicit"));
            DocumentsPaths.ConfigureDefault(MakeRoot("default"));

            DocumentsPaths.Reset();

            Assert.AreEqual(expected, DocumentsPaths.Root);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("relative\\path")]
        [TestCase("relative/path")]
        public void Configure_InvalidRoot_Throws(string invalidRoot)
        {
            Assert.Throws<ArgumentException>(() => DocumentsPaths.Configure(invalidRoot));
        }

        [Test]
        public void Configure_Null_Throws()
        {
            Assert.Throws<ArgumentException>(() => DocumentsPaths.Configure(null));
        }

        [TestCase("")]
        [TestCase("relative\\path")]
        public void ConfigureDefault_InvalidRoot_Throws(string invalidRoot)
        {
            Assert.Throws<ArgumentException>(() => DocumentsPaths.ConfigureDefault(invalidRoot));
        }

        [Test]
        public void Root_RelativeEnvironmentVariable_Throws()
        {
            Environment.SetEnvironmentVariable(DocumentsPaths.RootEnvironmentVariable, "relative\\path");

            Assert.Throws<ArgumentException>(() => _ = DocumentsPaths.Root);
        }

        [Test]
        public void Combine_NoParts_ReturnsRoot()
        {
            var root = MakeRoot("combine-none");
            DocumentsPaths.Configure(root);

            Assert.AreEqual(root, DocumentsPaths.Combine());
            Assert.AreEqual(root, DocumentsPaths.Combine(null));
        }

        [Test]
        public void Combine_AppendsPartsUnderRoot()
        {
            var root = MakeRoot("combine-parts");
            DocumentsPaths.Configure(root);

            Assert.AreEqual(
                Path.Combine(root, "TsumugiQuiz", "Questions"),
                DocumentsPaths.Combine("TsumugiQuiz", "Questions"));
        }

        [Test]
        public void Configure_PathWithDotDot_StoresNormalizedPath()
        {
            // PR #118 レビュー M-1: RootPathValidator で正規化してから保持する。
            var parent = Path.Combine(Path.GetTempPath(), "TsumugiQuizDocumentsPathsTests_normalize");

            DocumentsPaths.Configure(Path.Combine(parent, "sub", "..", "used"));

            Assert.AreEqual(Path.Combine(parent, "used"), DocumentsPaths.Root);
        }

        [Test]
        public void Root_EnvironmentVariableWithDotDot_IsNormalized()
        {
            var parent = Path.Combine(Path.GetTempPath(), "TsumugiQuizDocumentsPathsTests_envnorm");
            Environment.SetEnvironmentVariable(
                DocumentsPaths.RootEnvironmentVariable, Path.Combine(parent, "sub", "..", "used"));

            Assert.AreEqual(Path.Combine(parent, "used"), DocumentsPaths.Root);
        }

        [Test]
        public void Combine_EmptyPart_Throws()
        {
            // PR #118 レビュー L-1: AppPaths.Combine と同じ契約。
            DocumentsPaths.Configure(MakeRoot("combine-empty"));

            Assert.Throws<ArgumentException>(() => DocumentsPaths.Combine("TsumugiQuiz", string.Empty));
            Assert.Throws<ArgumentException>(() => DocumentsPaths.Combine("TsumugiQuiz", null));
        }

        /// <summary>
        /// 問題フォルダ・プリセットフォルダが <see cref="DocumentsPaths"/> の差し替えに追随すること
        /// （#112 の本題。これらが固定パスだと PlayMode テストが実ユーザーの Documents を汚す）。
        /// </summary>
        [Test]
        public void ConfiguredRoot_IsUsedByQuestionsAndPresetsDefaultFolders()
        {
            var root = MakeRoot("consumers");
            DocumentsPaths.Configure(root);

            Assert.AreEqual(
                Path.Combine(root, "TsumugiQuiz", "Questions"),
                TsumugiQuiz.Questions.QuestionRepository.GetDefaultQuestionsFolderPath());
            Assert.AreEqual(
                Path.Combine(root, "TsumugiQuiz", "Presets"),
                TsumugiQuiz.Room.RoomPresetStore.GetDefaultFolderPath());
        }
    }
}
