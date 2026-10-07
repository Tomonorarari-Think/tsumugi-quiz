using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor.Scripting.LifecycleManagement;
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Networking.Transport
{
    /// <summary>
    /// A wrapper for a <see cref="INetworkInterface"/> that provides the operations needed at
    /// runtime, but callable from unmanaged code (e.g. from Burst-compiled code). Unlike
    /// <see cref="NetworkInterfaceUnmanagedWrapper"/>, this wrapper erases the actual type of the
    /// wrapped network interface, making it easier to use in Burst-compiled code.
    /// </summary>
    /// <remarks>
    /// This doesn't implement <see cref="INetworkInterface"/> even though it's very similar because
    /// it's not meant to be itself a network interface, and because some operations are not wrapped
    /// (like initialization, which is expected to happen in a managed context).
    /// </remarks>
    internal unsafe partial struct NetworkInterfaceWrapper : IDisposable
    {
        private static readonly SharedStatic<ManagedCallWrapper> s_DisposeWrapper = SharedStatic<ManagedCallWrapper>.GetOrCreate<NetworkInterfaceWrapper, DisposeKey>();
        private static readonly SharedStatic<ManagedCallWrapper> s_GetLocalEndpointWrapper = SharedStatic<ManagedCallWrapper>.GetOrCreate<NetworkInterfaceWrapper, GetLocalEndpointKey>();
        private static readonly SharedStatic<ManagedCallWrapper> s_BindWrapper = SharedStatic<ManagedCallWrapper>.GetOrCreate<NetworkInterfaceWrapper, BindKey>();
        private static readonly SharedStatic<ManagedCallWrapper> s_ListenWrapper = SharedStatic<ManagedCallWrapper>.GetOrCreate<NetworkInterfaceWrapper, ListenKey>();
        private static readonly SharedStatic<ManagedCallWrapper> s_ScheduleReceiveWrapper = SharedStatic<ManagedCallWrapper>.GetOrCreate<NetworkInterfaceWrapper, ScheduleReceiveKey>();
        private static readonly SharedStatic<ManagedCallWrapper> s_ScheduleSendWrapper = SharedStatic<ManagedCallWrapper>.GetOrCreate<NetworkInterfaceWrapper, ScheduleSendKey>();

        private class DisposeKey {}
        private class GetLocalEndpointKey {}
        private class BindKey {}
        private class ListenKey {}
        private class ScheduleReceiveKey {}
        private class ScheduleSendKey {}

        private static List<INetworkInterface> s_Interfaces;

#if UNITY_EDITOR
        [OnEnteringEditMode]
        [OnCodeUnloading]
        private static void ResetStatics()
        {
            // Any users not disposing drivers correctly will result in sockets not being cleared
            // properly. We're doing this as a convenience to avoid hard to debug issues like "port
            // already in use" which would require the user to restart the editor to fix.
            if (s_Interfaces != null)
            {
                foreach (var netIf in s_Interfaces)
                    netIf?.Dispose();
                s_Interfaces.Clear();
            }

            // We don't reset the function wrappers since those are valid across domain reloads.
        }
#endif

        private static void InitializeStatics()
        {
            if (s_Interfaces != null)
                return;

            s_Interfaces = new List<INetworkInterface>();

            s_DisposeWrapper.Data = new ManagedCallWrapper(&DisposeWrapper);
            s_GetLocalEndpointWrapper.Data = new ManagedCallWrapper(&GetLocalEndpointWrapper);
            s_BindWrapper.Data = new ManagedCallWrapper(&BindWrapper);
            s_ListenWrapper.Data = new ManagedCallWrapper(&ListenWrapper);
            s_ScheduleReceiveWrapper.Data = new ManagedCallWrapper(&ScheduleReceiveWrapper);
            s_ScheduleSendWrapper.Data = new ManagedCallWrapper(&ScheduleSendWrapper);
        }

        // Index of the wrapped interface in the s_Interfaces list.
        private int m_InterfaceIndex;

        public NetworkInterfaceWrapper(INetworkInterface netIf)
        {
            InitializeStatics();

            var freeSpot = s_Interfaces.IndexOf(null);
            if (freeSpot >= 0)
            {
                m_InterfaceIndex = freeSpot;
                s_Interfaces[freeSpot] = netIf;
                return;
            }
            else
            {
                m_InterfaceIndex = s_Interfaces.Count;
                s_Interfaces.Add(netIf);
            }
        }

        /// <summary>
        /// Get the wrapped <see cref="INetworkInterface"/>. <see cref="NetworkInterfaceWrapper"/>
        /// doesn't know the actual type of the wrapped interface, so it can only return a boxed
        /// base interface. Consequently, this is only callable from managed code.
        /// </summary>
        public INetworkInterface GetInterface()
        {
            CheckInterfaceIndex(m_InterfaceIndex);
            return s_Interfaces[m_InterfaceIndex];
        }

        // Arguments structure containing everything needed to call any of the wrapped methods. Yes,
        // this is not as "safe" as having separate arguments structures for each method, but it's
        // less boilerplate and we're already dealing with void pointers, so safety clearly isn't an
        // important concern here.
        private struct Arguments
        {
            public NetworkEndpoint Endpoint;
            public ReceiveJobArguments ReceiveArguments;
            public SendJobArguments SendArguments;
            public JobHandle Dependency;
            public JobHandle ScheduledJob;
            public int ReturnValue;
            public int InterfaceIndex;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            var args = new Arguments { InterfaceIndex = m_InterfaceIndex };
            s_DisposeWrapper.Data.Invoke(ref args);
        }

        private static void DisposeWrapper(void* arguments, int size)
        {
            ref var args = ref ManagedCallWrapper.ArgumentsFromPtr<Arguments>(arguments, size);
            s_Interfaces[args.InterfaceIndex]?.Dispose();
            s_Interfaces[args.InterfaceIndex] = null;
        }

        /// <inheritdoc cref="INetworkInterface.LocalEndpoint"/>
        public NetworkEndpoint GetLocalEndpoint()
        {
            var args = new Arguments { InterfaceIndex = m_InterfaceIndex };
            s_GetLocalEndpointWrapper.Data.Invoke(ref args);
            return args.Endpoint;
        }

        private static void GetLocalEndpointWrapper(void* arguments, int size)
        {
            ref var args = ref ManagedCallWrapper.ArgumentsFromPtr<Arguments>(arguments, size);
            CheckInterfaceIndex(args.InterfaceIndex);

            args.Endpoint = s_Interfaces[args.InterfaceIndex].LocalEndpoint;
        }

        /// <inheritdoc cref="INetworkInterface.Bind"/>
        public int Bind(NetworkEndpoint endpoint)
        {
            var args = new Arguments { Endpoint = endpoint, InterfaceIndex = m_InterfaceIndex };
            s_BindWrapper.Data.Invoke(ref args);
            return args.ReturnValue;
        }

        private static void BindWrapper(void* arguments, int size)
        {
            ref var args = ref ManagedCallWrapper.ArgumentsFromPtr<Arguments>(arguments, size);
            CheckInterfaceIndex(args.InterfaceIndex);

            args.ReturnValue = s_Interfaces[args.InterfaceIndex].Bind(args.Endpoint);
        }

        /// <inheritdoc cref="INetworkInterface.Listen"/>
        public int Listen()
        {
            var args = new Arguments { InterfaceIndex = m_InterfaceIndex };
            s_ListenWrapper.Data.Invoke(ref args);
            return args.ReturnValue;
        }

        private static void ListenWrapper(void* arguments, int size)
        {
            ref var args = ref ManagedCallWrapper.ArgumentsFromPtr<Arguments>(arguments, size);
            CheckInterfaceIndex(args.InterfaceIndex);

            args.ReturnValue = s_Interfaces[args.InterfaceIndex].Listen();
        }

        /// <inheritdoc cref="INetworkInterface.ScheduleReceive"/>
        public JobHandle ScheduleReceive(ref ReceiveJobArguments arguments, JobHandle dep)
        {
            var args = new Arguments { ReceiveArguments = arguments, Dependency = dep, InterfaceIndex = m_InterfaceIndex };
            s_ScheduleReceiveWrapper.Data.Invoke(ref args);
            arguments = args.ReceiveArguments;
            return args.ScheduledJob;
        }

        private static void ScheduleReceiveWrapper(void* arguments, int size)
        {
            ref var args = ref ManagedCallWrapper.ArgumentsFromPtr<Arguments>(arguments, size);
            CheckInterfaceIndex(args.InterfaceIndex);

            args.ScheduledJob = s_Interfaces[args.InterfaceIndex].ScheduleReceive(ref args.ReceiveArguments, args.Dependency);
        }

        /// <inheritdoc cref="INetworkInterface.ScheduleSend"/>
        public JobHandle ScheduleSend(ref SendJobArguments arguments, JobHandle dep)
        {
            var args = new Arguments { SendArguments = arguments, Dependency = dep, InterfaceIndex = m_InterfaceIndex };
            s_ScheduleSendWrapper.Data.Invoke(ref args);
            arguments = args.SendArguments;
            return args.ScheduledJob;
        }

        private static void ScheduleSendWrapper(void* arguments, int size)
        {
            ref var args = ref ManagedCallWrapper.ArgumentsFromPtr<Arguments>(arguments, size);
            CheckInterfaceIndex(args.InterfaceIndex);

            args.ScheduledJob = s_Interfaces[args.InterfaceIndex].ScheduleSend(ref args.SendArguments, args.Dependency);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckInterfaceIndex(int index)
        {
            if (index < 0 || index >= s_Interfaces.Count || s_Interfaces[index] == null)
                throw new InvalidOperationException("Trying to access an invalid wrapped network interface.");
        }
    }
}
