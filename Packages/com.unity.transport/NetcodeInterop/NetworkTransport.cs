#if !NGO_PROVIDES_NETWORK_TRANSPORT
using System;
using UnityEngine;

// This file contains stripped-down copies of all relevant types in NGO that third-party transports
// depend on. This allows these transports to be used in UTP without dragging all of NGO in.

namespace Unity.Netcode
{
    /// <summary>
    /// A copy of the same abstract class in Netcode for GameObjects (NGO), stripped down to only
    /// what is necessary to use implementations of this class as transports in Unity Transport.
    /// This is only defined if the same class is not already provided by NGO.
    /// </summary>
    public abstract class NetworkTransport : MonoBehaviour
    {
        /// <summary>For clients, the client ID of the server. Unused for servers.</summary>
        public abstract ulong ServerClientId { get; }

        /// <summary>Whether this transport is supported.</summary>
        /// <value><c>true</c> if is supported; otherwise, <c>false</c>.</value>
        public virtual bool IsSupported => true;

        /// <summary>Delegate for transport network events.</summary>
        /// <param name="eventType">The type of network event that occurred.</param>
        /// <param name="clientId">The ID of the client associated with this event.</param>
        /// <param name="payload">The data payload received with this event.</param>
        /// <param name="receiveTime">The time when this event was received.</param>
        public delegate void TransportEventDelegate(NetworkEvent eventType, ulong clientId, ArraySegment<byte> payload, float receiveTime);

        /// <summary>
        /// Occurs when the transport has a new transport network event. Can be used to make an
        /// event-based transport instead of a poll-based one. Must be called on the main thread.
        /// </summary>
        public event TransportEventDelegate OnTransportEvent;

        /// <summary>
        /// Invoke the <see cref="OnTransportEvent"/>. Must be called on the main thread.
        /// </summary>
        /// <param name="eventType">The event type.</param>
        /// <param name="clientId">The client ID this event is for.</param>
        /// <param name="payload">The incoming data payload (if a data event).</param>
        /// <param name="receiveTime">The time the event was received.</param>
        protected void InvokeOnTransportEvent(NetworkEvent eventType, ulong clientId, ArraySegment<byte> payload, float receiveTime)
        {
            OnTransportEvent?.Invoke(eventType, clientId, payload, receiveTime);
        }

        /// <summary>Send a payload to the specified client.</summary>
        /// <remarks>
        /// Note that in the context of usage within Unity Transport, the network delivery parameter
        /// will only ever be called with <see cref="NetworkDelivery.Unreliable"/>. Unity Transport
        /// handles other delivery types at a higher level using its own mechanisms. That parameter
        /// remains present in the API only for compatibility with existing transports.
        /// </remarks>
        /// <param name="clientId">The client to send to.</param>
        /// <param name="payload">The data to send.</param>
        /// <param name="networkDelivery">The delivery type.</param>
        public abstract void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery);

        /// <summary>Poll for incoming events.</summary>
        /// <param name="clientId">The client ID this event is for.</param>
        /// <param name="payload">The incoming data payload (if any).</param>
        /// <param name="receiveTime">The time the event was received.</param>
        /// <returns>The type of the polled event.</returns>
        public abstract NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime);

        /// <summary>Connect a client to the server.</summary>
        /// <returns><c>true</c> on success, <c>false</c> otherwise.</returns>
        public abstract bool StartClient();

        /// <summary>Start listening for incoming connections.</summary>
        /// <returns><c>true</c> on success, <c>false</c> otherwise.</returns>
        public abstract bool StartServer();

        /// <summary>Disconnect a client from the server.</summary>
        /// <param name="clientId">The client ID to disconnect.</param>
        public abstract void DisconnectRemoteClient(ulong clientId);

        /// <summary>Disconnect the local client from the server.</summary>
        public abstract void DisconnectLocalClient();

        /// <summary>
        /// Get the round-trip time for the given client. This method is optional (return 0 if no
        /// supported). It is never actually called by Unity Transport, and in this context it is
        /// recommended to instead rely on <see cref="NetworkDriver.GetConnectionStatistics"/>.
        /// </summary>
        /// <param name="clientId">The clientId to get the RTT from</param>
        /// <returns>Returns the round trip time in milliseconds </returns>
        public abstract ulong GetCurrentRtt(ulong clientId);

        /// <summary>Shut down the transport.</summary>
        public abstract void Shutdown();

        /// <summary>Initialize the transport.</summary>
        /// <param name="networkManager">With Unity Transport, always will be null.</param>
        public abstract void Initialize(NetworkManager networkManager = null);

        /// <summary>
        /// Will be invoked by Unity Transport before processing any received packets. Usually this
        /// method is a good place to read packets from a socket and accept new connections.
        /// </summary>
        protected virtual void OnEarlyUpdate() { }

        /// <summary>Actual method invoked by Unity Transport.</summary>
        public void EarlyUpdate()
        {
            OnEarlyUpdate();
        }

        /// <summary>
        /// Will be invoked by Unity Transport after processing received packets and enqueueing any
        /// resulting events or packets. Usually this method should be used to send packets if the
        /// <see cref="Send"/> method only enqueues them.
        /// </summary>
        protected virtual void OnPostLateUpdate() { }

        /// <summary>Actual method invoked by Unity Transport.</summary>
        public void PostLateUpdate()
        {
            OnPostLateUpdate();
        }
    }

    /// <summary>
    /// An empty shell only present for compatibility with existing transports (which take an
    /// optional <c>NetworkManager</c> parameter in their <see cref="NetworkTransport.Initialize"/>
    /// method). This type is not actually used by Unity Transport, which will always pass null to
    /// <see cref="NetworkTransport.Initialize"/>.
    /// </summary>
    public class NetworkManager { }

    /// <summary>
    /// Delivery methods used by <see cref="NetworkTransport"/>. Note that Unity Transport only ever
    /// makes use of <see cref="Unreliable"/>. For a network transport that will only be used with
    /// Unity Transport (and not Netcode for GameObjects), there is no point in implementing any
    /// other delivery type. They are present here for compatibility only.
    /// </summary>
    public enum NetworkDelivery
    {
        /// <summary>No guarantees about either order or delivery.</summary>
        Unreliable,

        /// <summary>Unused by Unity Transport.</summary>
        UnreliableSequenced,

        /// <summary>Unused by Unity Transport.</summary>
        Reliable,

        /// <summary>Unused by Unity Transport.</summary>
        ReliableSequenced,

        /// <summary>Unused by Unity Transport.</summary>
        ReliableFragmentedSequenced
    }

    /// <summary>
    /// Types of network events that a <see cref="NetworkTransport"/> can report.
    /// </summary>
    public enum NetworkEvent
    {
        /// <summary>Data was received on the transport.</summary>
        Data,

        /// <summary>A client (either local or remote) has connected.</summary>
        Connect,

        /// <summary>A remote client has disconnected.</summary>
        Disconnect,

        /// <summary>Transport has encountered an unrecoverable failure.</summary>
        TransportFailure,

        /// <summary>No new event. Only relevant when polling for events.</summary>
        Nothing
    }
}

#endif