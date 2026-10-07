using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// 発見した NAT デバイス（ルーター）への操作。Mono.Nat の <c>INatDevice</c> を包む抽象。
    /// テストではこのインターフェースを差し替えて <see cref="PortMappingService"/> の状態遷移を検証する。
    ///
    /// 実装は **どのスレッドから呼ばれても動作する**こと（Mono.Nat のコールバックはワーカースレッド）。
    /// </summary>
    public interface INatDevice
    {
        /// <summary>デバイスが応答したプロトコル名（"UPnP" / "NAT-PMP"）。ログと報告に使う。</summary>
        string ProtocolName { get; }

        /// <summary>デバイスのアドレス（"192.168.1.1:1900" 形式）。ログに使う。</summary>
        string EndpointDescription { get; }

        /// <summary>ポートマッピングを作成する。</summary>
        /// <param name="mapping">作成するマッピング。</param>
        /// <param name="cancellationToken">キャンセル用。</param>
        /// <returns>ルーターが実際に作成したマッピング（<c>PublicPort</c> が要求値と異なることがある）。</returns>
        Task<NatPortMapping> CreatePortMapAsync(NatPortMapping mapping, CancellationToken cancellationToken);

        /// <summary>ポートマッピングを削除する。</summary>
        /// <param name="mapping">削除するマッピング。</param>
        /// <param name="cancellationToken">キャンセル用。</param>
        Task DeletePortMapAsync(NatPortMapping mapping, CancellationToken cancellationToken);

        /// <summary>ルーターに登録されている全マッピングを取得する。</summary>
        /// <param name="cancellationToken">キャンセル用。</param>
        Task<IReadOnlyList<NatPortMapping>> GetAllMappingsAsync(CancellationToken cancellationToken);

        /// <summary>ルーターが認識している外部 IP を取得する（docs/network-nat.md §2 の 1 段目）。</summary>
        /// <param name="cancellationToken">キャンセル用。</param>
        /// <returns>IPv4 のドット 10 進表記。取得できなければ空文字。</returns>
        Task<string> GetExternalIpAsync(CancellationToken cancellationToken);
    }
}
