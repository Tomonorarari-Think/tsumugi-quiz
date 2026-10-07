using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Tests.Shared.Core;
using UnityEngine;

namespace TsumugiQuiz.Tests.EditMode
{
    /// <summary>
    /// EditMode テストアセンブリ全体で一度だけ走る前準備（#112。
    /// <c>TsumugiQuiz.Tests.PlayMode.PlayModeTestAssemblySetUp</c> の EditMode 版）。
    ///
    /// <para>
    /// <see cref="DocumentsPaths"/>（問題フォルダ <c>Documents\TsumugiQuiz\Questions\</c>・
    /// プリセットフォルダの親）を一時フォルダへ隔離し、EditMode テストが<b>実ユーザーの</b>
    /// Documents を読み書きしないようにする。2026-09-17 時点では既定フォルダに触れる EditMode
    /// テストは無い（実測: 本 fixture 無しでも実ユーザーの <c>Documents\TsumugiQuiz\</c> は
    /// 変化しなかった）が、<c>QuestionLibrary</c> / <c>QuestionSetEditorService</c> /
    /// <c>RoomPresetStore</c> はいずれも引数を省略すると既定フォルダ（＝実 Documents）を使うため、
    /// 将来テストが増えたときの保険として先に敷いておく。
    /// </para>
    ///
    /// <para>
    /// <see cref="AppPaths"/> は EditMode では既定値を登録しない（<c>AppPathsTests</c> が
    /// 「未設定なら例外」を検証しているため）。隔離先の親は
    /// <see cref="DocumentsRootScope.Redirect"/> が <c>TSUMUGI_DATA_ROOT</c>（<c>scripts/verify.ps1</c>）
    /// または OS の一時フォルダから解決する。
    /// </para>
    /// </summary>
    [SetUpFixture]
    public class EditModeTestAssemblySetUp
    {
        private DocumentsRootScope _documentsRootScope;

        [OneTimeSetUp]
        public void IsolateDocumentsRoot()
        {
            _documentsRootScope = DocumentsRootScope.Redirect();
            Debug.Log($"[EditModeTestAssemblySetUp] Documents ルートを隔離しました: {_documentsRootScope.Describe()}");
        }

        [OneTimeTearDown]
        public void RestoreDocumentsRoot()
        {
            _documentsRootScope?.Restore();
            _documentsRootScope = null;
        }
    }
}
