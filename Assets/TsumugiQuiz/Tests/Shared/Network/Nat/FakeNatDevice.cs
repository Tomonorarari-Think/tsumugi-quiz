using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TsumugiQuiz.Network.Nat;

namespace TsumugiQuiz.Tests.Shared.Network.Nat
{
    /// <summary>
    /// テスト用の NAT デバイス。ルーターの応答（外部ポートの振り替え、拒否、既存マッピング）を
    /// 自由に組み立てられるようにする。
    /// </summary>
    internal sealed class FakeNatDevice : INatDevice
    {
        private readonly List<NatPortMapping> _existingMappings = new List<NatPortMapping>();

        /// <inheritdoc />
        public string ProtocolName { get; set; } = "UPnP";

        /// <inheritdoc />
        public string EndpointDescription { get; set; } = "192.168.1.1:1900";

        /// <summary>ルーターが答える外部 IP。</summary>
        public string ExternalIpAddress { get; set; } = "203.0.113.7";

        /// <summary>外部 IP 取得時に投げる例外（null なら成功）。</summary>
        public NatDeviceException ExternalIpException { get; set; }

        /// <summary>マッピング作成時に投げる例外（null なら成功）。</summary>
        public NatDeviceException CreateException { get; set; }

        /// <summary>一覧取得時に投げる例外（null なら成功）。</summary>
        public NatDeviceException GetAllException { get; set; }

        /// <summary>削除時に投げる例外（null なら成功）。</summary>
        public NatDeviceException DeleteException { get; set; }

        /// <summary>
        /// ルーターが実際に割り当てる外部ポート。null なら要求どおりに作る。
        /// docs/network-nat.md §1.3 の「要求した外部ポートが埋まっていた場合」を再現する。
        /// </summary>
        public int? AssignedPublicPort { get; set; }

        /// <summary>作成要求されたマッピング（呼ばれた順）。</summary>
        public List<NatPortMapping> CreatedMappings { get; } = new List<NatPortMapping>();

        /// <summary>削除要求されたマッピング（呼ばれた順）。</summary>
        public List<NatPortMapping> DeletedMappings { get; } = new List<NatPortMapping>();

        /// <summary><see cref="GetAllMappingsAsync"/> が呼ばれた回数。</summary>
        public int GetAllMappingsCallCount { get; private set; }

        /// <summary>ルーターに既に登録されているマッピング（掃除対象の再現に使う）。</summary>
        public List<NatPortMapping> ExistingMappings => _existingMappings;

        /// <summary>
        /// ルーターが返す内部ポート。null なら要求どおり。
        /// 「ルーターが要求と違う PrivatePort を返す」壊れた実装の再現に使う。
        /// </summary>
        public int? AssignedPrivatePort { get; set; }

        /// <inheritdoc />
        public Task<NatPortMapping> CreatePortMapAsync(NatPortMapping mapping, CancellationToken cancellationToken)
        {
            CreatedMappings.Add(mapping);

            if (CreateException != null)
            {
                return Task.FromException<NatPortMapping>(CreateException);
            }

            var created = new NatPortMapping(
                AssignedPrivatePort ?? mapping.PrivatePort,
                AssignedPublicPort ?? mapping.PublicPort,
                mapping.LifetimeSeconds,
                mapping.Description);

            // 実際のルーターと同じように、作成したマッピングは一覧に現れるようにする
            // （同じ外部ポートの既存エントリは置き換える）。
            _existingMappings.RemoveAll(entry => entry.PublicPort == created.PublicPort);
            _existingMappings.Add(created);

            return Task.FromResult(created);
        }

        /// <inheritdoc />
        public Task DeletePortMapAsync(NatPortMapping mapping, CancellationToken cancellationToken)
        {
            DeletedMappings.Add(mapping);

            if (DeleteException != null)
            {
                return Task.FromException(DeleteException);
            }

            _existingMappings.RemoveAll(entry => entry.PublicPort == mapping.PublicPort);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<NatPortMapping>> GetAllMappingsAsync(CancellationToken cancellationToken)
        {
            GetAllMappingsCallCount++;

            if (GetAllException != null)
            {
                return Task.FromException<IReadOnlyList<NatPortMapping>>(GetAllException);
            }

            return Task.FromResult<IReadOnlyList<NatPortMapping>>(_existingMappings.ToArray());
        }

        /// <inheritdoc />
        public Task<string> GetExternalIpAsync(CancellationToken cancellationToken)
            => ExternalIpException != null
                ? Task.FromException<string>(ExternalIpException)
                : Task.FromResult(ExternalIpAddress);
    }
}
