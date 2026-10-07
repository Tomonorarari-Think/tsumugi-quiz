using System.Threading;
using System.Threading.Tasks;

namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// グローバル IP 確認サービスへの HTTP GET。
    /// テストでは実装を差し替えて、応答の検証ロジックだけを単体で確認する。
    /// </summary>
    public interface IIpLookupClient
    {
        /// <summary>
        /// URL からプレーンテキストを取得する。失敗しても例外は投げず結果で返す。
        /// </summary>
        /// <param name="url">https の絶対 URL。</param>
        /// <param name="timeoutMs">タイムアウト（ミリ秒）。</param>
        /// <param name="maxResponseBytes">受け付ける最大バイト数。超えたら失敗として返す。</param>
        /// <param name="cancellationToken">キャンセル用。</param>
        /// <returns>結果。</returns>
        Task<IpLookupResponse> GetTextAsync(string url, int timeoutMs, int maxResponseBytes, CancellationToken cancellationToken);
    }
}
