using System.Globalization;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="RejectLogThrottle.Evaluate"/> の判定結果（docs/network.md §9、#52）。
    /// </summary>
    public readonly struct RejectLogDecision
    {
        /// <summary>
        /// 判定結果を作る。
        /// </summary>
        /// <param name="shouldLog">今回、実際にログを出してよいか。</param>
        /// <param name="suppressedSinceLastLog">
        /// 前回ログを出してから今回までの間に間引かれた件数（<paramref name="shouldLog"/> が false のときは 0）。
        /// </param>
        public RejectLogDecision(bool shouldLog, int suppressedSinceLastLog)
        {
            ShouldLog = shouldLog;
            SuppressedSinceLastLog = suppressedSinceLastLog;
        }

        /// <summary>今回ログを出してよいなら true。false なら間引かれた（何も出さない）。</summary>
        public bool ShouldLog { get; }

        /// <summary>
        /// 前回のログ出力から今回までに間引かれた件数。<see cref="ShouldLog"/> が true のときに、
        /// 「ほか N 件を間引きました」のようなサマリをログへ添えるために使う。
        /// </summary>
        public int SuppressedSinceLastLog { get; }

        /// <summary>
        /// <see cref="SuppressedSinceLastLog"/> を「（ほか N 件を間引きました）」という定型のサフィックスにする。
        /// 0 件（間引きが発生していない）なら空文字列を返す（#83、書式の重複を解消）。
        /// </summary>
        public string FormatSuppressedSuffix() =>
            SuppressedSinceLastLog > 0
                ? $"（ほか {SuppressedSinceLastLog.ToString(CultureInfo.InvariantCulture)} 件を間引きました）"
                : string.Empty;
    }
}
