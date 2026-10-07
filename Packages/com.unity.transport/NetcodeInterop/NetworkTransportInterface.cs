using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Netcode;
using Unity.Networking.Transport;
using UnityEngine;

using NetcodeEventType = Unity.Netcode.NetworkEvent;

namespace Unity.Networking.Transport.NetcodeInterop
{
    /// <summary>
    /// A <see cref="INetworkInterface"/> that can be used to wrap a <see cref="NetworkTransport"/>
    /// from Netcode for GameObjects, allowing it to be used as the underlying transport for a Unity
    /// Transport <see cref="NetworkDriver"/>.
    /// </summary>
    /// <remarks>
    /// This interface MUST be wrapped with <see cref="NetworkInterfaceUnmanagedWrapper"/> before it
    /// can be used to create a <see cref="NetworkDriver"/> since it uses managed types internally.
    /// Refer to the example usage for how to do this.
    /// </remarks>
    /// <example>
    /// <para>
    /// This example creates a game object only to hold the <see cref="NetworkTransport"/> since
    /// Unity Transport doesn't otherwise use game objects. In a real project, it is likely that you
    /// will have an existing game object that manages your networking that you could attach the
    /// transport to instead.
    /// </para>
    /// <code>
    ///     var go = new GameObject("NetcodeTransport");
    ///     GameObject.DontDestroyOnLoad(go);
    /// 
    ///     var transport = go.AddComponent&lt;MyNetcodeTransport&gt;();
    ///     var netif = new NetworkTransportInterface(transport);
    /// 
    ///     var driver = NetworkDriver.Create(netif);
    ///     // Use driver as you usually would...
    /// </code>
    /// </example>
    public struct NetworkTransportInterface : INetworkInterface, IConnectionListProvider
    {
        private NetworkTransport m_Transport;

        private ConnectionList m_ConnectionList;

        // For servers, client IDs are converted to/from endpoints directly. But for clients we
        // can't ensure that we will connect to the endpoint representation of ServerClientId. So we
        // track the server endpoint here and use ServerClientId instead whenever we encounter it.
        private NetworkEndpoint m_ServerEndpoint;

        private struct NetcodeEvent
        {
            public NetcodeEventType Type;
            public ulong ClientId;
            public ArraySegment<byte> Payload;
        }

        private Queue<NetcodeEvent> m_NetcodeEvents;
        private Dictionary<NetworkEndpoint, ConnectionId> m_EndpointToConnectionMap;

        private byte[] m_SendBuffer;

        private bool m_IsClient;
        private bool m_TransportFailed;

        /// <summary>
        /// Create a new network interface that wraps the given <see cref="NetworkTransport"/>.
        /// </summary>
        /// <param name="transport"><see cref="NetworkTransport"/> to wrap.</param>
        public NetworkTransportInterface(NetworkTransport transport)
        {
            m_Transport = transport;

            m_ConnectionList = default;
            m_ServerEndpoint = default;

            m_NetcodeEvents = new Queue<NetcodeEvent>();
            m_EndpointToConnectionMap = new Dictionary<NetworkEndpoint, ConnectionId>();

            m_SendBuffer = new byte[NetworkParameterConstants.AbsoluteMaxMessageSize];

            m_IsClient = true; // Will be set to false if Listen is called.
            m_TransportFailed = false;
        }

        ConnectionList IConnectionListProvider.CreateConnectionList()
        {
            m_ConnectionList = ConnectionList.Create();
            return m_ConnectionList;
        }

        // NetworkTransport doesn't offer a way to get this information, so use an invalid value.
        /// <inheritdoc/>
        public NetworkEndpoint LocalEndpoint => default(NetworkEndpoint);

        /// <inheritdoc/>
        public int Initialize(ref NetworkSettings settings, ref int packetPadding)
        {
            m_Transport.Initialize();
            m_Transport.OnTransportEvent += OnTransportEvent;

            return 0;
        }

        private void OnTransportEvent(NetcodeEventType eventType, ulong clientId, ArraySegment<byte> payload, float receiveTime)
        {
            m_NetcodeEvents.Enqueue(new NetcodeEvent
            {
                Type = eventType,
                ClientId = clientId,
                Payload = payload
            });
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_ConnectionList.Dispose();

            m_Transport.Shutdown();
            m_Transport = null;
        }

        /// <inheritdoc/>
        public int Bind(NetworkEndpoint endpoint)
        {
            // Nothing to do.
            return 0;
        }

        /// <inheritdoc/>
        public int Listen()
        {
            m_IsClient = false;
            return m_Transport.StartServer() ? 0 : (int)Error.StatusCode.NetworkSocketError;
        }

        /// <inheritdoc/>
        public JobHandle ScheduleReceive(ref ReceiveJobArguments arguments, JobHandle dep)
        {
            if (m_TransportFailed)
            {
                arguments.ReceiveResult.ErrorCode = (int)Error.StatusCode.NetworkSocketError;
                return dep;
            }

            // NetworkTransport can't be assumed to work off the main thread. We need to run its
            // operations here and then force a sync point with the UTP job to interact with the
            // data structures managed by UTP. The alternative is queuing all incoming packets and
            // events and processing them in the next main thread update, adding extra latency.

            m_Transport.EarlyUpdate();

            dep.Complete();

            ProcessTransportEvents(ref arguments.ReceiveQueue);
            ProcessConnectionList();

            return default;
        }

        private void ProcessConnectionList()
        {
            int count = m_ConnectionList.Count;
            for (int i = 0; i < count; i++)
            {
                var connectionId = m_ConnectionList.ConnectionAt(i);
                var connectionState = m_ConnectionList.GetConnectionState(connectionId);
                var connectionEndpoint = m_ConnectionList.GetConnectionEndpoint(connectionId);

                switch (connectionState)
                {
                    case NetworkConnection.State.Connecting:
                        if (m_IsClient)
                        {
                            if (m_EndpointToConnectionMap.Count == 0)
                            {
                                m_ServerEndpoint = connectionEndpoint;
                                if (!m_Transport.StartClient())
                                {
                                    Debug.LogError("NetcodeInterop: Failed to start client on wrapped NetworkTransport.");
                                    m_TransportFailed = true;
                                }
                                m_EndpointToConnectionMap.Add(connectionEndpoint, connectionId);
                            }
                        }
                        else
                        {
                            // It's an error to try to create a client connection on a server.
                            // There are some cases in UTP where this kinda works, but it won't
                            // for sure when wrapping a NetworkTransport so raise an error.
                            Debug.LogError("NetcodeInterop: Can't establish client connections with Connect() on a server when wrapping a NetworkTransport.");
                            m_ConnectionList.StartDisconnecting(ref connectionId, Error.DisconnectReason.ProtocolError);
                            m_ConnectionList.FinishDisconnecting(ref connectionId);
                        }
                        break;
                    case NetworkConnection.State.Disconnecting:
                        if (m_IsClient)
                        {
                            // We're a client disconnecting from the server. The endpoint check is
                            // there to ensure we don't call DisconnectRemoteClient on a client if
                            // the user created a second connection on the same driver.
                            if (m_EndpointToConnectionMap.ContainsKey(connectionEndpoint))
                                m_Transport.DisconnectLocalClient();
                        }
                        else
                        {
                            // We're a server disconnecting a remote client.
                            var clientId = GetClientIdForEndpoint(connectionEndpoint);
                            m_Transport.DisconnectRemoteClient(clientId);
                        }

                        m_ConnectionList.FinishDisconnecting(ref connectionId);
                        m_EndpointToConnectionMap.Remove(connectionEndpoint);
                        break;
                    default:
                        continue;
                }
            }
        }

        private void ProcessTransportEvents(ref PacketsQueue receiveQueue)
        {
            // First we process all events generated asynchronously by OnTransportEvent.
            while (m_NetcodeEvents.TryDequeue(out var ev))
                HandleNetcodeEvent(ev.Type, ev.ClientId, ev.Payload, ref receiveQueue);

            // Then we process all events that can be polled directly from the transport.
            var eventType = default(NetcodeEventType);
            while ((eventType = m_Transport.PollEvent(out var clientId, out var payload, out _)) != NetcodeEventType.Nothing)
                HandleNetcodeEvent(eventType, clientId, payload, ref receiveQueue);
        }

        private void HandleNetcodeEvent(NetcodeEventType eventType, ulong clientId, ArraySegment<byte> payload, ref PacketsQueue receiveQueue)
        {
            switch (eventType)
            {
                case NetcodeEventType.Connect:
                    HandleIncomingConnection(clientId);
                    break;
                case NetcodeEventType.Disconnect:
                    HandleIncomingDisconnection(clientId);
                    break;
                case NetcodeEventType.Data:
                    HandleIncomingData(clientId, payload, ref receiveQueue);
                    break;
                case NetcodeEventType.TransportFailure:
                    Debug.LogError("NetcodeInterop: Failure of wrapped NetworkTransport. NetworkDriver needs to be recreated.");
                    m_TransportFailed = true;
                    break;
            }
        }

        private void HandleIncomingConnection(ulong clientId)
        {
            var endpoint = GetEndpointForClientId(clientId);
            if (m_EndpointToConnectionMap.TryGetValue(endpoint, out var connectionId))
            {
                // To have heard of that endpoint before we MUST be a client.
                if (!m_IsClient)
                {
                    Debug.LogError("NetcodeInterop: Received a connection from a known endpoint on a server, likely indicating a bug in the NetworkTransport.");
                    m_ConnectionList.StartDisconnecting(ref connectionId, Error.DisconnectReason.ProtocolError);
                    m_ConnectionList.FinishDisconnecting(ref connectionId);
                    m_EndpointToConnectionMap.Remove(endpoint);
                }
                else
                {
                    m_ConnectionList.FinishConnectingFromLocal(ref connectionId);
                }
            }
            else
            {
                connectionId = m_ConnectionList.StartConnecting(ref endpoint);
                m_ConnectionList.FinishConnectingFromRemote(ref connectionId);
                m_EndpointToConnectionMap.Add(endpoint, connectionId);
            }
        }

        private void HandleIncomingDisconnection(ulong clientId)
        {
            var endpoint = GetEndpointForClientId(clientId);
            if (m_EndpointToConnectionMap.TryGetValue(endpoint, out var connectionId))
            {
                m_ConnectionList.StartDisconnecting(ref connectionId, Error.DisconnectReason.ClosedByRemote);
                m_ConnectionList.FinishDisconnecting(ref connectionId);
                m_EndpointToConnectionMap.Remove(endpoint);
            }

            // We don't show an error if we couldn't find the endpoint because it can happen in
            // normal usage if it just so happens that UTP requested a disconnection (in a layer
            // above) in the same update that the NetworkTransport flagged one.
        }

        private unsafe void HandleIncomingData(ulong clientId, ArraySegment<byte> payload, ref PacketsQueue receiveQueue)
        {
            var endpoint = GetEndpointForClientId(clientId);
            if (m_EndpointToConnectionMap.TryGetValue(endpoint, out var connectionId))
            {
                var connectionState = m_ConnectionList.GetConnectionState(connectionId);
                if (connectionState != NetworkConnection.State.Connected)
                    return;

                if (receiveQueue.EnqueuePacket(out var receiveProcessor))
                {
                    receiveProcessor.ConnectionRef = connectionId;
                    receiveProcessor.EndpointRef = endpoint;

                    fixed (byte* payloadPtr = payload.Array)
                    {
                        receiveProcessor.AppendToPayload(payloadPtr + payload.Offset, payload.Count);
                    }
                }

                // We don't do anything if we couldn't enqueue the packet. It just means the receive
                // queue capacity is not high enough and there's already a warning for that.
            }
            else
            {
                Debug.LogError("NetcodeInterop: Received a packet from an unknown endpoint, likely indicating a bug in the NetworkTransport.");
            }
        }

        /// <inheritdoc/>
        public unsafe JobHandle ScheduleSend(ref SendJobArguments arguments, JobHandle dep)
        {
            if (m_TransportFailed)
                return dep;

            // NetworkTransport can't be assumed to work off the main thread. Forcing a sync point
            // here is not ideal, but the alternative is to copy all outgoing packets to a separate
            // queue and delay sending them until the next main thread update, adding latency.
            dep.Complete();

            SendAllOutgoingPackets(ref arguments.SendQueue);

            // OnPostLateUpdate is usually used to flush out send buffers.
            m_Transport.PostLateUpdate();

            return default;
        }

        private unsafe void SendAllOutgoingPackets(ref PacketsQueue sendQueue)
        {
            int count = sendQueue.Count;
            for (int i = 0; i < count; i++)
            {
                var packetProcessor = sendQueue[i];
                if (packetProcessor.Length == 0)
                    continue;

                var packetPtr = (byte*)packetProcessor.GetUnsafePayloadPtr() + packetProcessor.Offset;
                Marshal.Copy((IntPtr)packetPtr, m_SendBuffer, 0, packetProcessor.Length);

                var clientId = GetClientIdForEndpoint(packetProcessor.EndpointRef);
                var payload = new ArraySegment<byte>(m_SendBuffer, 0, packetProcessor.Length);
                m_Transport.Send(clientId, payload, NetworkDelivery.Unreliable);
            }
        }

        private unsafe NetworkEndpoint GetEndpointForClientId(ulong clientId)
        {
            if (clientId == m_Transport.ServerClientId)
                return m_ServerEndpoint;

            var endpoint = default(NetworkEndpoint);
            endpoint.Family = NetworkFamily.Custom;
            UnsafeUtility.MemCpy(endpoint.RawAddressPtr, &clientId, sizeof(ulong));
            return endpoint;
        }

        private unsafe ulong GetClientIdForEndpoint(NetworkEndpoint endpoint)
        {
            if (endpoint == m_ServerEndpoint)
                return m_Transport.ServerClientId;

            ulong clientId = 0;
            UnsafeUtility.MemCpy(&clientId, endpoint.RawAddressPtr, sizeof(ulong));
            return clientId;
        }
    }
}