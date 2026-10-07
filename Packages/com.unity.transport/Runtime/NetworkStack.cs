using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.TLS;
using UnityEngine;
using BurstRuntime = Unity.Burst.BurstRuntime;

namespace Unity.Networking.Transport
{
    internal unsafe struct NetworkStack : IDisposable
    {
        // TODO: disabling the safety check here for now, but we need to remove it asap.
        // As NetworkStack is in NetworkDriver and the driver is passed to multiple
        // jobs being schedulled at the same time we schedule stack update, we
        // are not supposed to write here. We know that for now we don't read/write
        // this in any other place, so it's fine, but moving pipelines to a layer should
        // allow us to fix it.
        [NativeDisableContainerSafetyRestriction]
        private NativeList<NetworkLayerWrapper> m_Layers;
        [NativeDisableContainerSafetyRestriction]
        private NativeList<int> m_AccumulatedPacketPadding;

        private int m_TotalPacketPadding;
        private ConnectionList m_Connections;

        private NetworkInterfaceWrapper m_InterfaceWrapper;

        internal long NetworkInterfaceTypeHash;
        internal INetworkInterface NetworkInterface => m_InterfaceWrapper.GetInterface();

        // TODO: for now we add an extra byte for pipeline id, should move to its own layer
        internal int PacketPadding => m_TotalPacketPadding + 1;

        internal ConnectionList Connections => m_Connections;

        internal static void Initialize(out NetworkStack stack)
        {
            stack = default;
            stack.m_Layers = new NativeList<NetworkLayerWrapper>(0, Allocator.Persistent);
            stack.m_AccumulatedPacketPadding = new NativeList<int>(0, Allocator.Persistent);
        }

        internal static void InitializeForSettings<N>
            (out NetworkStack stack, ref N networkInterface, ref NetworkSettings networkSettings,
            out PacketsQueue sendQueue, out PacketsQueue receiveQueue) where N : INetworkInterface
        {
            Initialize(out stack);

            stack.AddLayer(new BottomLayer(), ref networkSettings);
            stack.AddInterfaceLayerAndCreateQueues(ref networkInterface, ref networkSettings, out sendQueue, out receiveQueue);

            stack.AddLayer(new AnalyticsLayer(), ref networkSettings);

            // stack.AddLayer(new LogLayer(), ref networkSettings); // This will print packets for debugging

            var isRelay = networkSettings.TryGet<RelayNetworkParameter>(out _);

            var isSecure = isRelay
                ? networkSettings.GetRelayParameters().ServerData.IsSecure == 1
                : networkSettings.TryGet<TLS.SecureNetworkProtocolParameter>(out _);

#if !UNITY_WEBGL || UNITY_EDITOR
            if (networkInterface is WebSocketNetworkInterface)
            {
                // If using the TCP interface or WebSocket interface (on non-WebGL platforms), we need
                // to add the TLS layer before the simulator layer, since it expects a reliable stream.
                if (isSecure)
                    stack.AddLayer(new TLSLayer(), ref networkSettings);

                // On non-WebGL platforms, add the WebSocket layer if using WebSocket interface.
                if (networkInterface is WebSocketNetworkInterface)
                    stack.AddLayer(new WebSocketLayer(), ref networkSettings);
            }
#endif // !UNITY_WEBGL || UNITY_EDITOR

            // Now we can add the simulator layer, which should be as low in the stack as possible.
            if (networkSettings.TryGet<NetworkSimulatorParameter>(out _))
                stack.AddLayer(new SimulatorLayer(), ref networkSettings);

            // Determine if we need to add a DTLS layer or not. These layers can only be added on
            // non-WebGL platforms that support UnityTLS. Hence the complicated #if condition.
#if !UNITY_WEBGL || UNITY_EDITOR
            if (isSecure && !(networkInterface is WebSocketNetworkInterface))
                stack.AddLayer(new DTLSLayer(), ref networkSettings);
#endif

            if (isRelay)
            {
                if (networkInterface is IPCNetworkInterface)
                    throw new InvalidOperationException("Relay cannot be used with the IPC interface");

                stack.AddLayer(new RelayLayer(), ref networkSettings);
            }

            stack.AddLayer(new SimpleConnectionLayer(), ref networkSettings);

            // There's never going to be anything to re-order if we're using WebSockets.
            if (!(networkInterface is WebSocketNetworkInterface))
                stack.AddLayer(new SequenceReorderingLayer(), ref networkSettings);

            stack.AddLayer(new TopLayer(), ref networkSettings);
        }

        public void Dispose()
        {
            var layersCount = m_Layers.Length;
            for (int i = 0; i < layersCount; i++)
            {
                m_Layers.ElementAt(i).Dispose();
            }

            m_Layers.Dispose();
            m_AccumulatedPacketPadding.Dispose();
        }

        internal void AddLayer<T>(T layer, ref NetworkSettings settings) where T : unmanaged, INetworkLayer
            => AddLayer(ref layer, ref settings);

        internal void AddLayer<T>(ref T layer, ref NetworkSettings settings) where T : unmanaged, INetworkLayer
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var oldPadding = m_TotalPacketPadding;
            var result = layer.Initialize(ref settings, ref m_Connections, ref m_TotalPacketPadding);
            if (m_TotalPacketPadding < oldPadding)
            {
                throw new InvalidOperationException($"Layer {typeof(T).ToString()} has decreased total padding. Negative packet paddings are invalid.");
            }
#else
            var result = layer.Initialize(ref settings, ref m_Connections, ref m_TotalPacketPadding);
#endif

            if (result != 0)
            {
                Debug.LogError($"Failed to initialize the NetworkStack. Layer {typeof(T).ToString()} with error code: {result}.");
                return;
            }

            m_Layers.Add(NetworkLayerWrapper.Create(ref layer));
            m_AccumulatedPacketPadding.Add(m_TotalPacketPadding);
        }

        internal void AddInterfaceLayerAndCreateQueues<N>(ref N networkInterface, ref NetworkSettings settings,
            out PacketsQueue sendQueue, out PacketsQueue receiveQueue) where N : INetworkInterface
        {
            sendQueue = default;
            receiveQueue = default;

            // For network interfaces we don't want to initialize the interface through the layer
            // (since it's hard to keep a reference to the initialized interface), so we do it here.

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var oldPadding = m_TotalPacketPadding;
            var result = networkInterface.Initialize(ref settings, ref m_TotalPacketPadding);
            if (m_TotalPacketPadding < oldPadding)
            {
                throw new InvalidOperationException($"Interface {typeof(N).ToString()} has decreased total padding. Negative packet paddings are invalid.");
            }
#else
            var result = networkInterface.Initialize(ref settings, ref m_TotalPacketPadding);
#endif

            if (result != 0)
            {
                Debug.LogError($"Failed to initialize the network interface {typeof(N).ToString()} with error code: {result}.");
                return;
            }

            if (networkInterface is IConnectionListProvider provider)
            {
                var list = provider.CreateConnectionList();
                if (list.IsCreated)
                {
                    m_Connections = list;
                    networkInterface = (N)provider;
                }
            }

            CreateQueues(ref networkInterface, ref settings, out sendQueue, out receiveQueue);

            m_InterfaceWrapper = new NetworkInterfaceWrapper(networkInterface);
            NetworkInterfaceTypeHash = BurstRuntime.GetHashCode64<N>();

            var layer = new NetworkInterfaceLayer(m_InterfaceWrapper);
            m_Layers.Add(NetworkLayerWrapper.Create(ref layer));
            m_AccumulatedPacketPadding.Add(m_TotalPacketPadding);
        }

        internal bool TryGetLayer<T>(out T layer) where T : unmanaged, INetworkLayer
        {
            foreach (var layerWrapper in m_Layers)
            {
                if (layerWrapper.IsType<T>())
                {
                    layer = layerWrapper.CastRef<T>();
                    return true;
                }
            }
            layer = default;
            return false;
        }

        internal static void CreateQueues<N>(ref N networkInterface, ref NetworkSettings settings, out PacketsQueue sendQueue, out PacketsQueue receiveQueue)
            where N : INetworkInterface
        {
            var networkConfig = settings.GetNetworkConfigParameters();
            var sendQueueCapacity = networkConfig.sendQueueCapacity;
            var receiveQueueCapacity = networkConfig.receiveQueueCapacity;
            var payloadSize = networkConfig.maxMessageSize;

#if !UNITY_WEBGL || UNITY_EDITOR
            if (networkInterface is UDPNetworkInterface udpInterface)
            {
                udpInterface.CreateQueues(sendQueueCapacity, receiveQueueCapacity, payloadSize, out sendQueue, out receiveQueue);
                networkInterface = (N)(INetworkInterface)udpInterface;
            }
            else
#endif
            {
                receiveQueue = new PacketsQueue(receiveQueueCapacity, payloadSize);
                sendQueue = new PacketsQueue(sendQueueCapacity, payloadSize);
            }

            if (sendQueue.Capacity != networkConfig.sendQueueCapacity)
            {
                sendQueue.Dispose();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                throw new InvalidOperationException(string.Format(
                    "The provided buffers count ({0}) must be equal to the sendQueueCapacity ({1})",
                    sendQueue.Capacity,
                    networkConfig.sendQueueCapacity));
#else
                Debug.LogError($"The provided buffers count ({sendQueue.Capacity}) must be equal to the sendQueueCapacity ({networkConfig.sendQueueCapacity})");
#endif
            }

            if (receiveQueue.Capacity != networkConfig.receiveQueueCapacity)
            {
                receiveQueue.Dispose();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                throw new InvalidOperationException(string.Format(
                    "The provided buffers count ({0}) must be equal to the receiveQueueCapacity ({1})",
                    receiveQueue.Capacity,
                    networkConfig.receiveQueueCapacity));
#else
                Debug.LogError($"The provided buffers count ({receiveQueue.Capacity}) must be equal to the receiveQueueCapacity ({networkConfig.receiveQueueCapacity})");
#endif
            }
        }

        internal int Bind(ref NetworkEndpoint endpoint) => m_InterfaceWrapper.Bind(endpoint);

        internal int Listen() => m_InterfaceWrapper.Listen();

        internal NetworkEndpoint GetLocalEndpoint() => m_InterfaceWrapper.GetLocalEndpoint();

        internal JobHandle ScheduleReceive(ref NetworkDriverReceiver driverReceiver, ref ConnectionList connectionList,
            ref NetworkEventQueue eventQueue, ref NetworkPipelineProcessor pipelineProcessor,
            ref NativeHashMap<ConnectionId, ConnectionPayload> connectionPayloads, long time, JobHandle dependency)
        {
            var jobArguments = new ReceiveJobArguments
            {
                ReceiveQueue = driverReceiver.ReceiveQueue,
                DriverReceiver = driverReceiver,
                ReceiveResult = driverReceiver.Result,
                EventQueue = eventQueue,
                PipelineProcessor = pipelineProcessor,
                ConnectionPayloads = connectionPayloads,
                Time = time,
            };

            var length = m_Layers.Length;
            for (var i = 0; i < length; ++i)
                dependency = m_Layers.ElementAt(i).ScheduleReceive(ref jobArguments, dependency);

            return dependency;
        }

        internal JobHandle ScheduleSend(ref NetworkDriverSender driverSender, long time, JobHandle dependency)
        {
            var jobArguments = new SendJobArguments
            {
                SendQueue = driverSender.SendQueue,
                Time = time,
            };

            dependency = driverSender.FlushPackets(dependency);

            for (var i = m_Layers.Length - 1; i >= 0; --i)
            {
                jobArguments.SendQueue.SetDefaultDataOffset(m_AccumulatedPacketPadding[i]);
                dependency = m_Layers.ElementAt(i).ScheduleSend(ref jobArguments, dependency);
            }

            return dependency;
        }
    }
}
