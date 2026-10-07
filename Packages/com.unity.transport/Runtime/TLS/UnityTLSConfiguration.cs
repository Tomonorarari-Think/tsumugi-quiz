using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport.Relay;
using Unity.TLS.LowLevel;

namespace Unity.Networking.Transport.TLS
{
    /// <summary>Secure transport protocols supported by UnityTLS.</summary>
    internal enum SecureTransportProtocol : uint
    {
        TLS = 0,
        DTLS = 1,
    }

    /// <summary>Utility containter for a UnityTLS configuration.</summary>
    internal unsafe struct UnityTLSConfiguration : IDisposable
    {
        private struct ConfigurationData
        {
            public Binding.unitytls_client_config ClientConfig;
            public UnityTLSCallbacks.CallbackContext CallbackContext;

            // We keep our own copies of these since the client configuration holds pointers to data
            // stored inside them and we want to ensure that this data remains valid for the entire
            // lifetime of the configuration.
            public SecureNetworkProtocolParameter SecureParameters;
            public RelayNetworkParameter RelayParameters;
        }

        [NativeDisableUnsafePtrRestriction]
        private ConfigurationData* m_ConfigData;

        public Binding.unitytls_client_config* ConfigPtr => &m_ConfigData->ClientConfig;
        public UnityTLSCallbacks.CallbackContext* CallbackContextPtr => &m_ConfigData->CallbackContext;

        public bool IsCreated => m_ConfigData != null;

        private static void InitializeFromSecureParameters(Binding.unitytls_client_config* config, ref SecureNetworkProtocolParameter parameters)
        {
            config->clientAuth = (uint)parameters.ClientAuthenticationPolicy;

            if (parameters.Hostname != default)
                config->hostname = parameters.Hostname.GetUnsafePtr();

            if (parameters.CACertificate.Length > 0)
            {
                config->caPEM = new Binding.unitytls_dataRef()
                {
                    dataPtr = parameters.CACertificate.GetUnsafePtr(),
                    dataLen = new UIntPtr((uint)parameters.CACertificate.Length)
                };
            }

            if (parameters.Certificate.Length > 0 && parameters.PrivateKey.Length > 0)
            {
                config->serverPEM = new Binding.unitytls_dataRef()
                {
                    dataPtr = parameters.Certificate.GetUnsafePtr(),
                    dataLen = new UIntPtr((uint)parameters.Certificate.Length)
                };

                config->privateKeyPEM = new Binding.unitytls_dataRef()
                {
                    dataPtr = parameters.PrivateKey.GetUnsafePtr(),
                    dataLen = new UIntPtr((uint)parameters.PrivateKey.Length)
                };
            }
        }

        private static void InitializeFromRelayParameters(Binding.unitytls_client_config* config, ref RelayNetworkParameter parameters)
        {
            config->hostname = (byte*)parameters.ServerData.HostString.GetUnsafePtr();

            // We only want to set up PSK authentication if using DTLS. Using TLS would mean we're
            // on WebSockets which require certificate authentication of the server.
            if (config->transportProtocol == (uint)SecureTransportProtocol.DTLS)
            {
                fixed (byte* hmacPtr = parameters.ServerData.HMACKey.Value)
                {
                    config->psk = new Binding.unitytls_dataRef()
                    {
                        dataPtr = hmacPtr,
                        dataLen = new UIntPtr(RelayHMACKey.k_Length)
                    };
                }

                fixed (byte* allocPtr = parameters.ServerData.AllocationId.Value)
                {
                    config->pskIdentity = new Binding.unitytls_dataRef()
                    {
                        dataPtr = allocPtr,
                        dataLen = new UIntPtr(RelayAllocationId.k_Length)
                    };
                }
            }
        }

        public UnityTLSConfiguration(ref NetworkSettings settings, SecureTransportProtocol protocol, ushort mtu = 0)
        {
            m_ConfigData = (ConfigurationData*)UnsafeUtility.Malloc(
                UnsafeUtility.SizeOf<ConfigurationData>(),
                UnsafeUtility.AlignOf<ConfigurationData>(),
                Allocator.Persistent);
            
            *m_ConfigData = default;

            Binding.unitytls_client_init_config(ConfigPtr);

            var netConfig = settings.GetNetworkConfigParameters();
            ConfigPtr->ssl_handshake_timeout_min = (uint)netConfig.connectTimeoutMS;
            ConfigPtr->ssl_handshake_timeout_max = (uint)(netConfig.maxConnectAttempts * netConfig.connectTimeoutMS);

            ConfigPtr->transportProtocol = (uint)protocol;
            ConfigPtr->transportUserData = (IntPtr)CallbackContextPtr;

            ConfigPtr->dataSendCB = UnityTLSCallbacks.GetSendCallbackPtr();
            ConfigPtr->dataReceiveCB = UnityTLSCallbacks.GetReceiveCallbackPtr();

            // Uncomment if you want UnityTLS and MbedTLS logs. Warning: extremely verbose!
            //ConfigPtr->logCallback = UnityTLSCallbacks.GetLogCallbackPtr();
            //ConfigPtr->tracelevel = Binding.UNITYTLS_LOGLEVEL_TRACE;

            ConfigPtr->mtu = mtu;

            if (settings.TryGet<RelayNetworkParameter>(out var relayParams))
            {
                m_ConfigData->RelayParameters = relayParams;
                InitializeFromRelayParameters(ConfigPtr, ref m_ConfigData->RelayParameters);
            }

            if (settings.TryGet<SecureNetworkProtocolParameter>(out var secureParams))
            {
                m_ConfigData->SecureParameters = secureParams;
                InitializeFromSecureParameters(ConfigPtr, ref m_ConfigData->SecureParameters);
            }
        }

        public void Dispose()
        {
            if (IsCreated)
            {
                UnsafeUtility.Free((void*)m_ConfigData, Allocator.Persistent);
                m_ConfigData = null;
            }
        }
    }
}
