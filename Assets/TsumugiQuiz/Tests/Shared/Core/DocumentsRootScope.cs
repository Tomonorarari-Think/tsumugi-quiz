using System;
using System.IO;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.Shared.Core
{
    /// <summary>
    /// <see cref="DocumentsPaths"/>（問題フォルダ <c>Documents\TsumugiQuiz\Questions\</c>・
    /// プリセットフォルダ <c>Documents\TsumugiQuiz\Presets\</c> の親）を、テスト実行中だけ
    /// 一時フォルダへ隔離するスコープ（#112。<c>RoomSettingsDraftScope</c> と同じ作法）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 背景: PlayMode のシーンテストは、テスト用の問題セット JSON を GUID 付きの名前で
    /// <b>実ユーザーの</b> <c>Documents\TsumugiQuiz\Questions\</c> へ書いていた。
    /// 並行して別 worktree で実機確認（<c>scripts/run-multi.ps1</c>）を行うと、そのビルドの
    /// 「ゲーム開始」がテスト用ファイルを読み込んでしまう（PR #104 の実機確認で観測）。
    /// teardown 漏れがあれば実ユーザーのデータにも残る。
    /// </para>
    /// <para>
    /// 本スコープは <see cref="DocumentsPaths.ConfigureDefault"/>（優先順位は明示設定・環境変数の
    /// <b>下</b>）でルートを差し替えるため、<c>-tq-documents-root</c> や環境変数
    /// <see cref="DocumentsPaths.RootEnvironmentVariable"/> による指定を妨げない。
    /// <see cref="Restore"/>（= <see cref="Dispose"/>）で既定の解決へ戻し、一時フォルダを削除する。
    /// </para>
    /// <para>
    /// 入れ子にしないこと: <see cref="Restore"/> は <see cref="DocumentsPaths.Reset"/> を呼ぶため、
    /// 外側のスコープの設定も一緒に解除してしまう（アセンブリ単位で 1 つだけ使う想定）。
    /// </para>
    /// </remarks>
    public sealed class DocumentsRootScope : IDisposable
    {
        private bool _restored;

        private DocumentsRootScope(string root) => Root = root;

        /// <summary>
        /// 現在有効なスコープ（アセンブリ単位のセットアップが作ったもの）。未設定なら null。
        /// <see cref="DocumentsPaths.Reset"/> を呼ぶテストが、後始末で隔離を復元するために使う
        /// （<see cref="ReapplyCurrent"/>）。
        /// </summary>
        public static DocumentsRootScope Current { get; private set; }

        /// <summary>
        /// 隔離先の Documents ルート（<see cref="ConfigureDefault"/> に渡した値。この配下に
        /// <c>TsumugiQuiz\Questions</c> 等が作られる想定だが、環境変数
        /// <see cref="DocumentsPaths.RootEnvironmentVariable"/> が既に設定されている場合は
        /// <see cref="ConfigureDefault"/> より優先されて実際には使われないため、フォルダ自体は
        /// 作らない（issue #122-1）。実際に有効な値は <see cref="DocumentsPaths.Root"/>（実効ルート）
        /// を参照すること。
        /// </summary>
        public string Root { get; }

        /// <summary>
        /// SetUpFixture のログ用の説明文（<c>"&lt;隔離先&gt; / 実効ルート(DocumentsPaths.Root): &lt;実効ルート&gt;"</c>）。
        /// <see cref="Root"/> と <see cref="DocumentsPaths.Root"/>（実効ルート）は、環境変数
        /// <see cref="DocumentsPaths.RootEnvironmentVariable"/> が有効なときに異なる値になりうるため、
        /// 両方を残す。<c>PlayModeTestAssemblySetUp</c> / <c>EditModeTestAssemblySetUp</c> で重複していた
        /// ログ組み立てをここへ集約する（issue #122 レビュー L-4）。
        /// </summary>
        public string Describe() => $"{Root} / 実効ルート(DocumentsPaths.Root): {DocumentsPaths.Root}";

        /// <summary>
        /// Documents ルートを一時フォルダ（<c>&lt;親&gt;\documents-&lt;GUID&gt;</c>）へ差し替える。
        /// </summary>
        /// <param name="parentFolderPath">
        /// 一時フォルダを作る親。null / 空の場合は <see cref="AppPaths.DataRoot"/>（実行単位ごとに
        /// 分かれるデータルート、#71）を使い、それも未設定なら OS の一時フォルダにフォールバックする。
        /// </param>
        public static DocumentsRootScope Redirect(string parentFolderPath = null)
        {
            var parent = string.IsNullOrWhiteSpace(parentFolderPath)
                ? ResolveDefaultParentFolder()
                : parentFolderPath;

            var root = Path.Combine(parent, "documents-" + Guid.NewGuid().ToString("N"));

            // issue #122 レビュー M-1: 環境変数（scripts/verify.ps1 が渡す隔離先。DocumentsPaths.Root の
            // 優先順位は Configure > 環境変数 > ConfigureDefault > 実ユーザーの Documents）が既に
            // 設定されている場合、この ConfigureDefault(root) は実際には使われない。ただし
            // ConfigureDefault 自体は必ず呼んでおく必要がある。呼ばずに早期 return すると、環境変数が
            // （テスト実行中の何らかの理由で）消えた瞬間に実ユーザーの Documents へ落ちてしまい、
            // 二重隔離のはずが単層になってしまうため。無駄にするのは空フォルダの作成だけに留める。
            var hasEnvironmentOverride = !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(DocumentsPaths.RootEnvironmentVariable));
            if (!hasEnvironmentOverride)
            {
                Directory.CreateDirectory(root);
            }

            DocumentsPaths.ConfigureDefault(root);

            var scope = new DocumentsRootScope(root);
            Current = scope;
            return scope;
        }

        /// <summary>
        /// 隔離を再適用する。<see cref="DocumentsPaths"/> の優先順位そのものを検証するテストは
        /// <see cref="DocumentsPaths.Reset"/> を呼ぶ必要があり、そのままだと同じ実行内の後続テストが
        /// 実ユーザーの Documents を見てしまう。そうしたテストは後始末でこれを呼ぶこと。
        /// </summary>
        public static void ReapplyCurrent() => Current?.Reapply();

        /// <summary>このスコープの隔離先を再度 <see cref="DocumentsPaths.ConfigureDefault"/> で登録する。</summary>
        public void Reapply()
        {
            if (!_restored)
            {
                DocumentsPaths.ConfigureDefault(Root);
            }
        }

        /// <summary>
        /// このスコープの <see cref="ConfigureDefault"/> 登録だけを解除し、一時フォルダ（作っていれば）を
        /// 削除する（多重呼び出しは無害）。issue #122 レビュー L-5: 環境変数
        /// <see cref="DocumentsPaths.RootEnvironmentVariable"/> が有効な間は、解除後もその隔離先が
        /// 引き続き使われる（<see cref="DocumentsPaths.Root"/> の優先順位で環境変数の方が上のため）。
        /// 環境変数が無い場合のみ、実際に実ユーザーの Documents へ戻る。
        /// </summary>
        public void Restore()
        {
            if (_restored)
            {
                return;
            }

            _restored = true;
            DocumentsPaths.Reset();

            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }

            if (!Directory.Exists(Root))
            {
                return;
            }

            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // 後始末に失敗しても、テスト結果には影響しない（一時フォルダのため放置してよい）。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <inheritdoc />
        public void Dispose() => Restore();

        private static string ResolveDefaultParentFolder()
        {
            try
            {
                return AppPaths.DataRoot;
            }
            catch (InvalidOperationException)
            {
                // AppPaths が未設定の実行（EditMode で ConfigureDefault も環境変数も無い場合）。
                // 隔離そのものは成立させたいので、OS の一時フォルダへ逃がす。
                return Path.GetTempPath();
            }
        }
    }
}
