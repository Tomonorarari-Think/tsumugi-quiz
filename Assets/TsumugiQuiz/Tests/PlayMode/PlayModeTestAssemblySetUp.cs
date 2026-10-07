using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Tests.Shared.Core;
using UnityEngine;

namespace TsumugiQuiz.Tests.PlayMode
{
    /// <summary>
    /// PlayMode テストアセンブリ全体で一度だけ走る前準備（#71、#112）。
    ///
    /// 多くの PlayMode テストは Boot シーンを経由せず <c>Main.unity</c> を直接ロードする
    /// （<c>MainSceneTestHelpers.LoadMainSceneAndGetRoot</c>）ため、Boot 常駐の
    /// <c>TsumugiQuiz.Network.AppPathsBootstrap</c> が一度も走らないまま
    /// <c>JsonConsentStorage</c> / <c>TtsService</c> が <see cref="AppPaths.DataRoot"/> を
    /// 参照するケースがある。ここで既定値（<see cref="AppPaths.ConfigureDefault"/>）だけ登録しておくことで、
    /// データルート未設定による例外を防ぐ。
    ///
    /// 既定値は優先順位の最下位（明示設定・環境変数より弱い）なので、<c>scripts/verify.ps1</c> が設定する
    /// 環境変数 <see cref="AppPaths.DataRootEnvironmentVariable"/>（worktree 分離のため）を妨げない。
    ///
    /// <para>
    /// #112: 同じ理由で <see cref="DocumentsPaths"/>（問題フォルダ・プリセットフォルダの親）も
    /// 隔離する。隔離しないと、問題セット JSON を書く PlayMode テスト
    /// （<c>LobbyGameStartSceneTests</c> / <c>QuestionEditorFormSceneTests</c> 等）や、
    /// 問題エディタ画面を開くだけのテスト（フォルダを自動作成する）が<b>実ユーザーの</b>
    /// <c>Documents\TsumugiQuiz\</c> を書き換えてしまい、並行する実機確認
    /// （<c>scripts/run-multi.ps1</c>）にテスト用の問題セットが混入する。
    /// 隔離先は <see cref="AppPaths.DataRoot"/> 配下の <c>documents-&lt;GUID&gt;</c>
    /// （= 実行単位ごとに独立）で、アセンブリの実行が終わったら削除する。
    /// </para>
    /// </summary>
    [SetUpFixture]
    public class PlayModeTestAssemblySetUp
    {
        private DocumentsRootScope _documentsRootScope;

        [OneTimeSetUp]
        public void ConfigureAppPathsDefault()
        {
            AppPaths.ConfigureDefault(Application.persistentDataPath);

            // AppPaths の既定値を登録したあとに呼ぶこと（隔離先の親に AppPaths.DataRoot を使うため）。
            _documentsRootScope = DocumentsRootScope.Redirect();
            Debug.Log($"[PlayModeTestAssemblySetUp] Documents ルートを隔離しました: {_documentsRootScope.Describe()}");
        }

        [OneTimeTearDown]
        public void RestoreDocumentsRoot()
        {
            _documentsRootScope?.Restore();
            _documentsRootScope = null;
        }
    }
}
