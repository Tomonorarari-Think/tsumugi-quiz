using System;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// ビルドの識別子（承認ペイロードの <c>clientBuildHash</c>）を、画面に出す番号に写す（#204、docs/network.md §2.3）。
    /// 純粋関数で、Unity API に依存しない。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ビルドが違う相手の接続は、バージョン不一致の文言（<see cref="ConnectionRejectionMessages.ProtocolVersionMismatchFormat"/>）で
    /// 拒否する。#208 以降のクライアントはこの書式の <c>{0}</c> / <c>{1}</c> が ASCII の数字 1〜5 桁のときだけ自前の文言として
    /// そのまま出す（<see cref="DisconnectReasonLocalizer"/>）ので、識別子（GUID の 16 進 32 文字など）をそのまま入れず、
    /// 1〜<see cref="MaxNumber"/>（5 桁）の番号に写して入れる。
    /// </para>
    /// <para>
    /// 写し方は FNV-1a（32bit）を UTF-16 の符号単位ごとに回したもの。<c>string.GetHashCode</c> は実行環境によって
    /// プロセスごとに値が変わりうるので使わない（ホストとクライアントで同じ識別子から同じ番号が出る必要がある）。
    /// 番号は識別子より短いので、別の識別子が同じ番号になることがある。拒否の文言で 2 つの番号が同じに見えないよう、
    /// <see cref="ForMismatch"/> はその場合にクライアント側の番号をずらす。
    /// </para>
    /// </remarks>
    public static class BuildDisplayNumber
    {
        /// <summary>識別子が無い（空）ときの番号。#204 より前のクライアントは空の識別子を送る。</summary>
        public const ushort Unknown = 0;

        /// <summary>
        /// 番号の最大値。<see cref="ConnectionRejectionMessages.Create"/> の引数（ushort）に収まり、
        /// <see cref="DisconnectReasonLocalizer"/> の数字の桁数（1〜5 桁）にも収まる値にする。
        /// </summary>
        public const ushort MaxNumber = ushort.MaxValue;

        private const uint FnvOffsetBasis = 2166136261;
        private const uint FnvPrime = 16777619;

        /// <summary>
        /// 識別子を番号に写す。空・null は <see cref="Unknown"/>（0）、それ以外は 1〜<see cref="MaxNumber"/>。
        /// </summary>
        /// <param name="buildHash">ビルドの識別子。</param>
        /// <returns>画面に出す番号。</returns>
        public static ushort From(string buildHash)
        {
            if (string.IsNullOrEmpty(buildHash))
            {
                return Unknown;
            }

            var hash = FnvOffsetBasis;
            foreach (var c in buildHash)
            {
                hash ^= c;
                hash = unchecked(hash * FnvPrime);
            }

            return (ushort)((hash % MaxNumber) + 1);
        }

        /// <summary>
        /// ビルドが一致しないときに拒否の文言へ入れる、ホストとクライアントの番号を返す。
        /// 識別子が違うのに番号が同じになった場合は、クライアント側の番号を 1 つずらして（<see cref="MaxNumber"/> の次は 1）
        /// 必ず違う番号にする。識別子が同じ（一致している）ときは同じ番号を返す。
        /// </summary>
        /// <param name="hostBuildHash">ホストのビルドの識別子。</param>
        /// <param name="clientBuildHash">クライアントのビルドの識別子。</param>
        /// <returns>ホストの番号とクライアントの番号。</returns>
        public static (ushort Host, ushort Client) ForMismatch(string hostBuildHash, string clientBuildHash)
        {
            var host = From(hostBuildHash);
            var client = From(clientBuildHash);

            var sameBuild = string.Equals(
                hostBuildHash ?? string.Empty, clientBuildHash ?? string.Empty, StringComparison.Ordinal);
            if (!sameBuild && host == client)
            {
                // 両方が空なら識別子は同じなので、ここへ来るのは両方とも空でない（番号が 1〜MaxNumber の）場合だけ。
                client = (ushort)((client % MaxNumber) + 1);
            }

            return (host, client);
        }
    }
}
