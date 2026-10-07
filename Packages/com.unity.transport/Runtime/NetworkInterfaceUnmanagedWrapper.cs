using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Networking.Transport.Utilities;

namespace Unity.Networking.Transport
{
    /// <summary>
    /// An unmanaged network interface that can act as a wrapper for a managed one. Use
    /// <see cref="ManagedNetworkInterfaceExtensions.WrapToUnmanaged"/> to obtain an instance. Do
    /// not create one manually.
    /// </summary>
    /// <typeparam name="T">Type of the network interface to wrap.</typeparam>
    [Obsolete("This shouldn't be used anymore. Managed network interfaces can now be used directly in NetworkDriver.")]
    public unsafe struct NetworkInterfaceUnmanagedWrapper<T> : INetworkInterface, IConnectionListProvider where T : INetworkInterface
    {
        private static ManagedCallWrapper s_InitializeWrapper;

        internal NetworkInterfaceWrapper m_InterfaceWrapper;

        internal NetworkInterfaceUnmanagedWrapper(ref T networkInterface)
        {
            if (!s_InitializeWrapper.IsCreated)
                s_InitializeWrapper = new ManagedCallWrapper(&InitializeWrapper);
        
            m_InterfaceWrapper = new NetworkInterfaceWrapper(networkInterface);
        }

        // We don't know if the wrapped interface provides a connection list, but it's really hard
        // to get at the wrapped interface when we don't know the type offhand, so just implement
        // the interface anyway and return a default value if the wrapped interface didn't actually
        // provide a connection list. NetworkInterfaceLayer just needs to know that default can be
        // returned and act accordingly.
        ConnectionList IConnectionListProvider.CreateConnectionList()
        {
            var netif = m_InterfaceWrapper.GetInterface();
            if (netif is IConnectionListProvider provider)
            {
                var list = provider.CreateConnectionList();
                return list;
            }
            else
            {
                return default;
            }
        }

        /// <inheritdoc/>
        public NetworkEndpoint LocalEndpoint => m_InterfaceWrapper.GetLocalEndpoint();

        /// <inheritdoc/>
        public int Bind(NetworkEndpoint endpoint) => m_InterfaceWrapper.Bind(endpoint);

        /// <inheritdoc/>
        public void Dispose() => m_InterfaceWrapper.Dispose();

        private struct InitializeArguments
        {
            public NetworkInterfaceWrapper WrappedInterface;
            public NetworkSettings NetworkSettings;
            public int PacketPadding;
            public int ReturnValue;
        }

        private static void InitializeWrapper(void* argumentsPtr, int argumentsSize)
        {
            ref var arguments = ref ManagedCallWrapper.ArgumentsFromPtr<InitializeArguments>(argumentsPtr, argumentsSize);
            var netIf = arguments.WrappedInterface.GetInterface();
            arguments.ReturnValue = netIf.Initialize(ref arguments.NetworkSettings, ref arguments.PacketPadding);
        }

        /// <inheritdoc/>
        public int Initialize(ref NetworkSettings settings, ref int packetPadding)
        {
            var arguments = new InitializeArguments
            {
                WrappedInterface = m_InterfaceWrapper,
                NetworkSettings = settings,
                PacketPadding = packetPadding,
            };

            s_InitializeWrapper.Invoke(ref arguments);

            // As they are ref arguments we need to reassign them in case they changed.
            settings = arguments.NetworkSettings;
            packetPadding = arguments.PacketPadding;

            return arguments.ReturnValue;
        }

        /// <inheritdoc/>
        public int Listen() => m_InterfaceWrapper.Listen();

        /// <inheritdoc/>
        public JobHandle ScheduleReceive(ref ReceiveJobArguments receiveJobArguments, JobHandle dep)
            => m_InterfaceWrapper.ScheduleReceive(ref receiveJobArguments, dep);

        /// <inheritdoc/>
        public JobHandle ScheduleSend(ref SendJobArguments sendJobArguments, JobHandle dep)
            => m_InterfaceWrapper.ScheduleSend(ref sendJobArguments, dep);
    }

    /// <summary>Extension methods to work with a managed <see cref="INetworkInterface"/>.</summary>
    public static class ManagedNetworkInterfaceExtensions
    {
        /// <summary>
        /// Creates an unmanaged wrapper for a managed <see cref="INetworkInterface"/>.
        /// </summary>
        /// <typeparam name="T">The type of the managed network interface.</typeparam>
        /// <param name="networkInterface">Interface instance to wrap.</param>
        /// <returns>Unmanaged wrapper instance for the network interface.</returns>
        /// <exception cref="InvalidOperationException">
        /// If the type network interface is already an unmanaged type.
        /// </exception>
        [Obsolete("This method is no longer necessary. Managed network interfaces can now be used directly in NetworkDriver.")]
        public static NetworkInterfaceUnmanagedWrapper<T> WrapToUnmanaged<T>(this T networkInterface) where T : INetworkInterface
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (networkInterface == null)
                throw new ArgumentNullException(nameof(networkInterface));

            // It would be tempting to also check if UnsafeUtility.IsUnmanaged<T>() here, but that
            // would prevent wrapping interfaces that don't have any managed fields but are still
            // using managed code in their methods.
#endif
            return new NetworkInterfaceUnmanagedWrapper<T>(ref networkInterface);
        }
    }
}
