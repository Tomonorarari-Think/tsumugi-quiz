using System;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.Tests.PlayMode.Core
{
    /// <summary>
    /// PlayMode テストの実行中、問題フォルダ・プリセットフォルダが<b>実ユーザーの</b> Documents ではなく
    /// 隔離された一時フォルダを指していることを確認する（#112 の受け入れ条件そのものの回帰テスト）。
    ///
    /// 静的な状態は Play Mode 突入時のドメインリロードで失われうるため、「隔離が実際のテスト実行時まで
    /// 効いている」ことをテスト自身で確認する意味がある（実測: 環境変数なしでも通る）。
    /// <c>PlayModeTestAssemblySetUp</c> の隔離が外れると、シーンテストが書くテスト用の問題セット JSON が
    /// 実ユーザーの <c>Documents\TsumugiQuiz\Questions\</c> に残り、並行する実機確認
    /// （<c>scripts/run-multi.ps1</c>）へ混入する。
    /// </summary>
    public class DocumentsRootIsolationTests
    {
        [Test]
        public void DocumentsRoot_IsNotUserDocumentsFolder()
        {
            var userDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            Assume.That(
                userDocuments,
                Is.Not.Null.And.Not.Empty,
                "実行環境の Documents フォルダを取得できないため、この検証は成立しない。");

            Assert.IsFalse(
                IsUnder(DocumentsPaths.Root, userDocuments),
                $"PlayMode テストの Documents ルートが実ユーザーの Documents 配下です: {DocumentsPaths.Root}");
        }

        [Test]
        public void QuestionsAndPresetsFolders_AreUnderIsolatedDocumentsRoot()
        {
            Assert.IsTrue(
                IsUnder(QuestionRepository.GetDefaultQuestionsFolderPath(), DocumentsPaths.Root),
                "問題フォルダは Documents ルート配下に解決されるはず。");
            Assert.IsTrue(
                IsUnder(RoomPresetStore.GetDefaultFolderPath(), DocumentsPaths.Root),
                "プリセットフォルダは Documents ルート配下に解決されるはず。");
        }

        private static bool IsUnder(string path, string parent)
        {
            var normalizedPath = Normalize(path);
            var normalizedParent = Normalize(parent);
            return normalizedPath.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(
                    normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path)
            => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
