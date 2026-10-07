using System.Threading;
using System.Threading.Tasks;

namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// NAT デバイスの探索。実装は <see cref="MonoNatDiscovery"/>、テストでは差し替える。
    /// </summary>
    public interface INatDiscovery
    {
        /// <summary>
        /// UPnP / NAT-PMP で NAT デバイスを探す。最初に見つかった 1 台を返す。
        /// </summary>
        /// <param name="timeoutMs">探索のタイムアウト（ミリ秒）。</param>
        /// <param name="cancellationToken">キャンセル用。</param>
        /// <returns>見つかったデバイス。タイムアウトした場合は null（例外にしない）。</returns>
        Task<INatDevice> DiscoverAsync(int timeoutMs, CancellationToken cancellationToken);
    }
}
