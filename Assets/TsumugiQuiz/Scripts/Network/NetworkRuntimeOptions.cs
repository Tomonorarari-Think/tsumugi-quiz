using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// 起動時のコマンドライン引数から決まるネットワーク設定（不変）。
    /// 同一 PC で複数プロセスを起動して検証するときに使う（docs/network.md §10.3）。
    ///
    /// 認識する引数:
    /// <code>
    /// -tq-port &lt;n&gt;   待ち受け / 接続先のポート（別名 -port）
    /// -tq-name &lt;s&gt;   プレイヤー名
    /// </code>
    /// </summary>
    public readonly struct NetworkRuntimeOptions
    {
        /// <summary>プレイヤー名を指定する引数。</summary>
        public const string PlayerNameArgument = "-tq-name";

        private NetworkRuntimeOptions(ushort port, string playerName)
        {
            Port = port;
            PlayerName = playerName;
        }

        /// <summary>
        /// ポート番号。引数が無ければ <see cref="NetworkConstants.DefaultPort"/>。
        ///
        /// レビュー M-3（issue #8）: <c>-tq-port 0</c> はここでは「未指定」の意味に扱い、
        /// <see cref="NetworkConstants.DefaultPort"/> にフォールバックする（<see cref="FromCommandLine(CommandLineOptions)"/>
        /// 参照）。これは <c>TsumugiQuiz.UI.LaunchOptionsRunner.TryGetPort</c>（-tq-host 自動ホスト開始が使う）が
        /// <c>0</c> を「OS に空きポートを選ばせる」という正当な指定としてそのまま扱うのとは意図的に異なる
        /// （同一 PC での複数プロセス起動でポート衝突を避けたいという別の要求のため）。
        /// この <see cref="Port"/> はログ表示・診断用途に留め、実際に使用される（バインドされた）ポート番号を
        /// 保証するものではない（実ポートは <c>NetworkService.ActivePort</c>、ホスト開始後に確定する）。
        /// </summary>
        public ushort Port { get; }

        /// <summary>プレイヤー名。引数が無ければ空文字。</summary>
        public string PlayerName { get; }

        /// <summary>引数指定が無い場合の既定値。</summary>
        public static NetworkRuntimeOptions Default
            => new NetworkRuntimeOptions(NetworkConstants.DefaultPort, string.Empty);

        /// <summary>
        /// コマンドライン引数の配列から設定を組み立てる。
        /// </summary>
        /// <param name="args"><c>System.Environment.GetCommandLineArgs()</c> の戻り値など。</param>
        public static NetworkRuntimeOptions FromCommandLine(string[] args)
            => FromCommandLine(CommandLineOptions.Parse(args));

        /// <summary>
        /// 解析済みのコマンドライン引数から設定を組み立てる。
        /// ポートが数値として読めない、または 0 の場合は既定値にフォールバックする
        /// （不正な引数で起動不能にしない）。
        /// </summary>
        /// <param name="options">解析済みの引数。null なら既定値を返す。</param>
        public static NetworkRuntimeOptions FromCommandLine(CommandLineOptions options)
        {
            if (options == null)
            {
                return Default;
            }

            var port = NetworkConstants.DefaultPort;
            if (options.TryGetUInt16(out var parsedPort, NetworkConstants.PortArgument, NetworkConstants.PortArgumentAlias)
                && parsedPort != 0)
            {
                port = parsedPort;
            }

            var playerName = string.Empty;
            if (options.TryGetString(out var rawName, PlayerNameArgument)
                && PlayerNameValidator.TryNormalize(rawName, out var normalizedName))
            {
                playerName = normalizedName;
            }

            return new NetworkRuntimeOptions(port, playerName);
        }
    }
}
