using System;
using System.IO;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// ユーザーの Documents フォルダ配下のパス解決（<c>%USERPROFILE%\Documents</c> 相当）の共通ヘルパー。
    /// <c>TsumugiQuiz.Questions.QuestionRepository</c>（問題データ）・<c>TsumugiQuiz.Room.RoomPresetStore</c>
    /// （プリセット）がともに Documents 配下を既定フォルダとするため、
    /// 「<see cref="Environment.GetFolderPath"/> が空文字列を返す」異常系の扱いをここに集約する
    /// （#26 統括判断 M12。環境によっては <c>SpecialFolder.MyDocuments</c> が空を返すことがある）。
    ///
    /// <para>
    /// #112: PlayMode テスト・実機確認（<c>scripts/run-multi.ps1</c>）が<b>実ユーザーの</b>
    /// <c>Documents\TsumugiQuiz\Questions\</c> を共有してしまい、テスト用の問題セット JSON が
    /// 並行実行中のビルドに混入する問題があったため、<see cref="AppPaths"/> と同じ作法で
    /// ルートの差し替え口を設けた。
    /// </para>
    ///
    /// <see cref="Root"/> を決める優先順位:
    /// <list type="number">
    ///   <item><description><see cref="Configure"/> による明示設定（テスト、または起動オプション
    ///     <c>-tq-documents-root</c>（<see cref="LaunchArguments.DocumentsRoot"/>）。
    ///     後者は <c>TsumugiQuiz.Network.AppPathsBootstrap</c> が Boot で読み取って設定する）</description></item>
    ///   <item><description>環境変数 <see cref="RootEnvironmentVariable"/></description></item>
    ///   <item><description><see cref="ConfigureDefault"/> で登録された既定値
    ///     （PlayMode / EditMode のテストアセンブリが、実ユーザーの Documents に触れないよう
    ///     一時フォルダを登録する）</description></item>
    ///   <item><description>実ユーザーの Documents フォルダ
    ///     （<see cref="Environment.GetFolderPath"/>。本番の通常起動時は常にこれ）</description></item>
    /// </list>
    ///
    /// 上書きの値は <see cref="RootPathValidator"/> で検証・正規化する（<c>..</c> を含むパスは
    /// 解決済みの絶対パスになる。PR #118 レビュー M-1）。
    ///
    /// 静的な状態を持つため、テストは必ず自分が使う値を明示的に設定し、終了時に <see cref="Reset"/> で
    /// 後始末すること（他のテストの実行順に依存しないようにするため）。
    /// </summary>
    public static class DocumentsPaths
    {
        /// <summary>Documents ルートを指定する環境変数名（<see cref="AppPaths.DataRootEnvironmentVariable"/> と同じ作法）。</summary>
        public const string RootEnvironmentVariable = "TSUMUGI_DOCUMENTS_ROOT";

        private static string _explicitRoot;
        private static string _defaultRoot;

        /// <summary>
        /// Documents ルートを明示的に設定する（テスト、または起動オプション <c>-tq-documents-root</c>）。
        /// 環境変数・既定値・実ユーザーの Documents のいずれよりも優先される。
        /// </summary>
        /// <param name="root">絶対パス。</param>
        /// <exception cref="ArgumentException"><paramref name="root"/> が空、または絶対パスでないとき。</exception>
        public static void Configure(string root)
        {
            _explicitRoot = RootPathValidator.Validate(root, "DocumentsPaths.Configure の root", nameof(root));
        }

        /// <summary>
        /// 既定値を登録する。<see cref="Configure"/> による明示設定・環境変数のどちらも無いときだけ使われ、
        /// 実ユーザーの Documents フォルダよりは優先される
        /// （テストアセンブリ全体を一括で隔離する用途。<see cref="AppPaths.ConfigureDefault"/> と同じ作法）。
        /// </summary>
        /// <param name="root">絶対パス。</param>
        /// <exception cref="ArgumentException"><paramref name="root"/> が空、または絶対パスでないとき。</exception>
        public static void ConfigureDefault(string root)
        {
            _defaultRoot = RootPathValidator.Validate(root, "DocumentsPaths.ConfigureDefault の root", nameof(root));
        }

        /// <summary>
        /// 明示設定・既定値をすべて解除し、実ユーザーの Documents フォルダへ戻す（テストの後始末用）。
        /// 環境変数は解除しない（プロセスの環境設定は本クラスが管理する対象ではないため）。
        /// </summary>
        public static void Reset()
        {
            _explicitRoot = null;
            _defaultRoot = null;
        }

        /// <summary>
        /// 現在有効な Documents ルートの絶対パス（優先順位は本クラスのコメントを参照）。
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// 上書きがいずれも無く、<see cref="Environment.GetFolderPath"/> が空・空白文字列を返したとき
        /// （実行環境の破損したユーザープロファイル等、まれなケース）。
        /// </exception>
        /// <exception cref="ArgumentException">
        /// 環境変数 <see cref="RootEnvironmentVariable"/> の値が絶対パスでないとき。
        /// </exception>
        public static string Root
        {
            get
            {
                if (_explicitRoot != null)
                {
                    return _explicitRoot;
                }

                var envValue = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
                if (!string.IsNullOrWhiteSpace(envValue))
                {
                    return RootPathValidator.Validate(
                        envValue, $"環境変数 {RootEnvironmentVariable}", RootEnvironmentVariable);
                }

                if (_defaultRoot != null)
                {
                    return _defaultRoot;
                }

                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrWhiteSpace(documents))
                {
                    throw new InvalidOperationException(
                        "ユーザーの Documents フォルダを取得できませんでした" +
                        "（Environment.GetFolderPath(SpecialFolder.MyDocuments) が空文字列を返しました）。" +
                        "ユーザープロファイルの設定を確認してください。");
                }

                return documents;
            }
        }

        /// <summary><see cref="Root"/> 配下のパスを組み立てる（<see cref="Path.Combine(string[])"/> の薄いラッパー）。</summary>
        /// <param name="parts">結合する要素（0 件なら <see cref="Root"/> をそのまま返す）。</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="parts"/> に空要素が含まれるとき（PR #118 レビュー L-1。
        /// <see cref="AppPaths.Combine"/> と同じ契約にそろえる。空要素を黙って無視すると
        /// 意図しないフォルダ（例: Documents 直下）へ書き込んでしまうため）。
        /// </exception>
        public static string Combine(params string[] parts)
        {
            if (parts == null || parts.Length == 0)
            {
                return Root;
            }

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                {
                    throw new ArgumentException("空のパス要素は指定できません。", nameof(parts));
                }
            }

            var combined = new string[parts.Length + 1];
            combined[0] = Root;
            Array.Copy(parts, 0, combined, 1, parts.Length);
            return Path.Combine(combined);
        }
    }
}
