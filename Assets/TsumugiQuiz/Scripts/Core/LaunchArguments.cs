namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 同一 PC でのマルチプロセス手動検証（docs/network.md §10.3、issue #8）に使う
    /// <c>-tq-</c> 系コマンドライン引数のキー名。
    ///
    /// <c>-tq-port</c>（<c>TsumugiQuiz.Network.NetworkConstants.PortArgument</c>）と
    /// <c>-tq-name</c>（<c>TsumugiQuiz.Network.NetworkRuntimeOptions.PlayerNameArgument</c>）は
    /// Network 層に既に定義済みのため、ここには含めない（Core は Network に依存しないため
    /// <c>&lt;see cref&gt;</c> で参照できない。LOW レビュー対応）。
    /// </summary>
    public static class LaunchArguments
    {
        /// <summary>起動時に自動でホストを開始するフラグ（値を伴わない）。</summary>
        public const string Host = "-tq-host";

        /// <summary>起動時に自動で参加する参加コード。</summary>
        public const string Join = "-tq-join";

        /// <summary>
        /// データルート（<see cref="AppPaths.DataRoot"/> 相当）の上書き先パス。
        /// <c>TsumugiQuiz.Network.AppPathsBootstrap</c>（issue #71）が Boot でこの値を読み取り、
        /// <see cref="AppPaths.Configure"/> に渡して実際に適用する。本クラス・
        /// <see cref="CommandLineOptions"/> 自体はキー名の定義とパースのみを担う。
        /// </summary>
        public const string DataRoot = "-tq-data-root";

        /// <summary>
        /// Documents ルート（<see cref="DocumentsPaths.Root"/> 相当、問題フォルダ・プリセットフォルダの親）の
        /// 上書き先パス（#112）。<c>TsumugiQuiz.Network.AppPathsBootstrap</c> が Boot でこの値を読み取り、
        /// <see cref="DocumentsPaths.Configure"/> に渡して実際に適用する。
        /// 実機確認（<c>scripts/run-multi.ps1 -IsolateDocuments</c>）が、実ユーザーの
        /// <c>Documents\TsumugiQuiz\Questions\</c> に触れずに検証するために使う。
        /// </summary>
        public const string DocumentsRoot = "-tq-documents-root";

        /// <summary>ウィンドウ位置・サイズ。"x,y,w,h" 形式（<see cref="LaunchWindowRect"/>）。</summary>
        public const string Window = "-tq-window";
    }
}
