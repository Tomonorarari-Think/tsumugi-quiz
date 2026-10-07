using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TsumugiQuiz.Network.Nat;

namespace TsumugiQuiz.Tests.EditMode.Network.Nat
{
    /// <summary>
    /// すべての操作で <see cref="NatDeviceException"/> **以外**の例外を投げるデバイス。
    /// 想定外の例外でもホスト開始を止めないことを確認するために使う。
    /// </summary>
    internal sealed class ThrowingNatDevice : INatDevice
    {
        private readonly Exception _exception;

        /// <summary>
        /// デバイスを作る。
        /// </summary>
        /// <param name="exception">各操作で投げる例外。</param>
        public ThrowingNatDevice(Exception exception) => _exception = exception;

        /// <inheritdoc />
        public string ProtocolName => "UPnP";

        /// <inheritdoc />
        public string EndpointDescription => "192.168.1.1:1900";

        /// <inheritdoc />
        public Task<NatPortMapping> CreatePortMapAsync(NatPortMapping mapping, CancellationToken cancellationToken)
            => Task.FromException<NatPortMapping>(_exception);

        /// <inheritdoc />
        public Task DeletePortMapAsync(NatPortMapping mapping, CancellationToken cancellationToken)
            => Task.FromException(_exception);

        /// <inheritdoc />
        public Task<IReadOnlyList<NatPortMapping>> GetAllMappingsAsync(CancellationToken cancellationToken)
            => Task.FromException<IReadOnlyList<NatPortMapping>>(_exception);

        /// <inheritdoc />
        public Task<string> GetExternalIpAsync(CancellationToken cancellationToken)
            => Task.FromException<string>(_exception);
    }
}
