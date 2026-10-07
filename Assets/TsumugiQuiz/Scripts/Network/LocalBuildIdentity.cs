using TsumugiQuiz.Core.Network;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// このプロセスのビルドの識別子（#204、docs/network.md §2.3「バージョンとビルドの一致」）。
    /// ホストは承認でクライアントの <c>clientBuildHash</c> と照合し、クライアントは承認ペイロードに載せる。
    /// </summary>
    /// <remarks>
    /// 実体は <see cref="Application.buildGUID"/>。Unity のドキュメントによれば、ビルドのたびに一意の GUID が作られ、
    /// Editor では空文字を返す。ビルドの手順で値を作る必要が無く、Editor（PlayMode テストを含む）では常に同じ値になる。
    /// プロセスの中では変わらないので、最初に読んだ値を使い続ける（メインスレッドから読むこと）。
    /// </remarks>
    public static class LocalBuildIdentity
    {
        private static string _buildHash;

        /// <summary>ビルドの識別子。Editor では空文字。null にはならない。</summary>
        public static string BuildHash => _buildHash ??= Application.buildGUID ?? string.Empty;

        /// <summary>画面・ログに出すビルドの番号（<see cref="BuildDisplayNumber.From"/>。Editor では 0）。</summary>
        public static ushort DisplayNumber => BuildDisplayNumber.From(BuildHash);
    }
}
