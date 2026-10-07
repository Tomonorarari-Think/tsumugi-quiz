using System;
using Unity.Collections;
using Unity.Networking.Transport.Error;
using UnityEngine;

namespace Unity.Networking.Transport
{
    /// <summary>
    /// Provides an API for managing the NetworkDriver connections.
    /// </summary>
    internal struct ConnectionList : IDisposable
    {
        private struct ConnectionData
        {
            public NetworkEndpoint Endpoint;
            public NetworkConnection.State State;
            // TODO: Eventually this field needs to be moved out of ConnectionData and become part of a ConnectionDataMap
            // in a new PathMTULayer that will sit above SimpleConnectionLayer.
            // At the current time, this field is populated by SimpleConnectionLayer as the last step of the connection
            // handshake, and shouldn't be accessed by any other layers. Additionally if any new layer is added above
            // SimpleConnectionLayer that replaces NetworkDriver's ConnectionList, the data in this field needs to be
            // propagated from SimpleConnectionList's ConnectionList up to NetworkDriver's.
            public int PathMtu;

            public bool NewlyDisconnecting;
            public bool NewlyConnectedByRemote;
            public bool NewlyConnectedByLocal;

            public Error.DisconnectReason DisconnectReason;
        }

        internal struct NewDisconnection
        {
            public ConnectionId Connection;
            public Error.DisconnectReason Reason;
        }

        private ConnectionDataMap<ConnectionData> m_Connections;

        /// <summary>
        /// The current count of connections.
        /// </summary>
        public int Count => m_Connections.Length;

        public bool IsCreated => m_Connections.IsCreated;

        internal ConnectionId ConnectionAt(int index) => m_Connections.ConnectionAt(index);
        internal NetworkConnection.State GetConnectionState(ConnectionId connectionId) => m_Connections[connectionId].State;

        internal NetworkEndpoint GetConnectionEndpoint(ConnectionId connectionId) => m_Connections[connectionId].Endpoint;
        internal void SetConnectionEndpoint(ConnectionId connectionId, NetworkEndpoint endpoint)
        {
            var data = m_Connections[connectionId];
            data.Endpoint = endpoint;
            m_Connections[connectionId] = data;
        }
        
        internal int GetConnectionPathMtu(ConnectionId connectionId) => m_Connections[connectionId].PathMtu;
        internal void SetConnectionPathMtu(ConnectionId connectionId, int pathMtu)
        {
            var data = m_Connections[connectionId];
            data.PathMtu = pathMtu;
            m_Connections[connectionId] = data;
        }

        public static ConnectionList Create()
        {
            return new ConnectionList(Allocator.Persistent);
        }

        private ConnectionList(Allocator allocator)
        {
            var defaultConnectionData = new ConnectionData { State = NetworkConnection.State.Disconnected, PathMtu = NetworkParameterConstants.AbsoluteMinimumMtuSize };
            m_Connections = new ConnectionDataMap<ConnectionData>(1, defaultConnectionData, allocator);
        }

        public void Dispose()
        {
            m_Connections.Dispose();
        }

        private ConnectionId GetNewConnection()
        {
            // First, try to find an existing connection that is closed and can be reused.
            for (int i = 0; i < Count; i++)
            {
                var connectionId = ConnectionAt(i);
                var connectionData = m_Connections[connectionId];

                if (connectionData.State == NetworkConnection.State.Disconnected)
                {
                    connectionId.Version++;
                    m_Connections.ClearData(ref connectionId);
                    return connectionId;
                }
            }

            // If we get here then all connection slots are in use. Add a new one.
            return new ConnectionId { Id = m_Connections.Length, Version = 1 };
        }

        /// <summary>
        /// Creates a new connection to the provided address and sets its state to Connecting.
        /// </summary>
        /// <param name="address">The endpoint to connect to.</param>
        /// <returns>Returns the ConnectionId identifier for the new created connection.</returns>
        /// <remarks>The connection is going to be fully connected only when FinishConnecting() is called.</remarks>
        internal ConnectionId StartConnecting(ref NetworkEndpoint address)
        {
            var connection = GetNewConnection();

            m_Connections[connection] = new ConnectionData
            {
                Endpoint = address,
                State = NetworkConnection.State.Connecting,
                PathMtu = NetworkParameterConstants.AbsoluteMinimumMtuSize
            };

            return connection;
        }

        /// <summary>
        /// Creates a new connection without a resolved endpoint.
        /// </summary>
        /// <param name="address">The endpoint to connect to.</param>
        /// <returns>Returns the ConnectionId identifier for the new created connection.</returns>
        /// <remarks>The connection is going to be fully connected only when FinishConnecting() is called.</remarks>
        internal ConnectionId StartConnecting()
        {
            var connection = GetNewConnection();

            m_Connections[connection] = new ConnectionData
            {
                State = NetworkConnection.State.Connecting,
                PathMtu = NetworkParameterConstants.AbsoluteMinimumMtuSize
            };

            return connection;
        }

        /// <summary>
        /// Completes a connection started by the local endpoint in Connecting state by setting it to Connected.
        /// </summary>
        /// <param name="connectionId">The connecting connection to be completed.</param>
        internal void FinishConnectingFromLocal(ref ConnectionId connectionId)
        {
            // TODO: we might want to restric the connection completion to the layer that
            // owns the connection list.

            CompleteConnecting(ref connectionId);

            var connectionData = m_Connections[connectionId];
            connectionData.NewlyConnectedByLocal = true;
            m_Connections[connectionId] = connectionData;
        }

        /// <summary>
        /// Completes a connection started by the remote endpoint in Connecting state by setting it to Connected.
        /// </summary>
        /// <param name="connectionId">The connecting connection to be completed.</param>
        internal void FinishConnectingFromRemote(ref ConnectionId connectionId)
        {
            // TODO: we might want to restric the connection completion to the layer that
            // owns the connection list.

            CompleteConnecting(ref connectionId);

            var connectionData = m_Connections[connectionId];
            connectionData.NewlyConnectedByRemote = true;
            m_Connections[connectionId] = connectionData;
        }

        private void CompleteConnecting(ref ConnectionId connectionId)
        {
            var connectionData = m_Connections[connectionId];

            if (connectionData.State != NetworkConnection.State.Connecting)
                return;

            connectionData.State = NetworkConnection.State.Connected;
            m_Connections[connectionId] = connectionData;
        }

        internal ConnectionId AcceptConnection()
        {
            var count = Count;
            for (int i = 0; i < count; i++)
            {
                var connectionId = ConnectionAt(i);
                var connectionData = m_Connections[connectionId];

                if (connectionData.State == NetworkConnection.State.Connected && connectionData.NewlyConnectedByRemote)
                {
                    connectionData.NewlyConnectedByRemote = false;
                    m_Connections[connectionId] = connectionData;
                    return connectionId;
                }
            }

            return default;
        }

        internal bool IsConnectionAccepted(ref ConnectionId connectionId)
        {
            var connectionData = m_Connections[connectionId];
            return !connectionData.NewlyConnectedByRemote;
        }

        /// <summary>
        /// Sets the state of the connection to Disconnecting.
        /// </summary>
        /// <param name="connectionId">The connection to disconnect.</param>
        /// <param name="reason">The disconnect reason.</param>
        /// <remarks>
        /// The connection is going to be fully disconnected only when FinishDisconnecting is
        /// called. A Disconnect event with the provided reason will be enqueued at the begining of
        /// the next ScheduleUpdate call.
        /// </remarks>
        internal void StartDisconnecting(ref ConnectionId connectionId, Error.DisconnectReason reason = Error.DisconnectReason.Default)
        {
            var connectionData = m_Connections[connectionId];

            if (connectionData.State == NetworkConnection.State.Disconnected ||
                connectionData.State == NetworkConnection.State.Disconnecting)
            {
                Debug.LogWarning("Attempting to disconnect an already disconnected connection");
                return;
            }

            connectionData.State = NetworkConnection.State.Disconnecting;
            connectionData.DisconnectReason = reason;
            connectionData.NewlyDisconnecting = true;
            m_Connections[connectionId] = connectionData;
        }

        /// <summary>
        /// Completes a disconnection by setting the state of the connection to Disconnected.
        /// </summary>
        /// <param name="connectionId">The disconnecting connection to be completed.</param>
        internal void FinishDisconnecting(ref ConnectionId connectionId)
        {
            var connectionData = m_Connections[connectionId];

            if (connectionData.State != NetworkConnection.State.Disconnecting)
            {
                Debug.LogWarning($"Attempting to complete a disconnection with state different to Disconnecting ({connectionData.State})");
                return;
            }

            connectionData.State = NetworkConnection.State.Disconnected;
            m_Connections[connectionId] = connectionData;
        }

        internal void UpdateConnectionAddress(ref ConnectionId connection, ref NetworkEndpoint address)
        {
            var connectionData = m_Connections[connection];
            if (connectionData.Endpoint != address)
            {
                connectionData.Endpoint = address;
                m_Connections[connection] = connectionData;
            }
        }

        internal NativeList<ConnectionId> QueryIncomingConnections(Allocator allocator)
        {
            var incomingConnections = new NativeList<ConnectionId>(Count, allocator);

            for (int i = 0; i < Count; i++)
            {
                var connectionId = ConnectionAt(i);
                var connectionData = m_Connections[connectionId];

                if (connectionData.NewlyConnectedByRemote)
                    incomingConnections.Add(connectionId);
            }

            return incomingConnections;
        }

        internal NativeList<ConnectionId> GetFinishedConnections(Allocator allocator)
        {
            var finishedConnections = new NativeList<ConnectionId>(Count, allocator);

            for (int i = 0; i < Count; i++)
            {
                var connectionId = ConnectionAt(i);
                var connectionData = m_Connections[connectionId];

                if (connectionData.NewlyConnectedByLocal)
                {
                    finishedConnections.Add(connectionId);
                    connectionData.NewlyConnectedByLocal = false;
                    m_Connections[connectionId] = connectionData;
                }
            }

            return finishedConnections;
        }

        internal NativeList<NewDisconnection> GetNewDisconnections(Allocator allocator)
        {
            var count = Count;
            var newlyDisconnecting = new NativeList<NewDisconnection>(count, allocator);

            for (int i = 0; i < count; i++)
            {
                var connectionId = ConnectionAt(i);
                var connectionData = m_Connections[connectionId];

                if (connectionData.NewlyDisconnecting)
                {
                    newlyDisconnecting.Add(new NewDisconnection
                    {
                        Connection = connectionId,
                        Reason = connectionData.DisconnectReason,
                    });

                    connectionData.NewlyDisconnecting = false;
                    m_Connections[connectionId] = connectionData;
                }
            }

            return newlyDisconnecting;
        }

        public override bool Equals(object obj)
        {
            return obj is ConnectionList list &&
                this == list;
        }

        public override int GetHashCode()
        {
            return m_Connections.GetHashCode();
        }

        public static unsafe bool operator==(ConnectionList a, ConnectionList b)
        {
            return a.m_Connections == b.m_Connections;
        }

        public static unsafe bool operator!=(ConnectionList a, ConnectionList b)
        {
            return !(a == b);
        }
    }
}
