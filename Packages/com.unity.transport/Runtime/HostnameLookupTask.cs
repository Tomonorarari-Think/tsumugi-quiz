using Unity.Baselib.LowLevel;
using Unity.Collections;
using UnityEngine;

namespace Unity.Networking.Transport
{
    internal unsafe struct HostnameLookupTask
    {
        private Binding.Baselib_NetworkAddress_HostnameLookupHandle* m_LookupHandle;

        private FixedString512Bytes m_Address;

        private int RetryCount;

        public bool IsCreated => m_LookupHandle != null;

        public NetworkConnection Connection;
        public ushort Port;

        public static bool Create(FixedString512Bytes address, out HostnameLookupTask task, out Binding.Baselib_NetworkAddress result, out Binding.Baselib_ErrorState error)
        {
            task = new HostnameLookupTask();
            return task.Initialize(address, out result, out error);
        }

        private bool Initialize(FixedString512Bytes address, out Binding.Baselib_NetworkAddress result, out Binding.Baselib_ErrorState error)
        {
            var addressBytes = address.GetUnsafePtr();
            var errorState = default(Binding.Baselib_ErrorState);
            var localResult = default(Binding.Baselib_NetworkAddress);
            Binding.Baselib_NetworkAddress_HostnameLookupHandle* handle;

            do
            {
                if (RetryCount > 10)
                {
                    errorState.code = Binding.Baselib_ErrorCode.NoSupportedAddressFound;
                    error = errorState;
                    result = default;
                    return true;
                }
                handle = Binding.Baselib_NetworkAddress_HostnameLookup(addressBytes, &localResult, &errorState);
                ++RetryCount;
            } while (errorState.code == Binding.Baselib_ErrorCode.TryAgain);

            error = errorState;
            if (errorState.code == Binding.Baselib_ErrorCode.Success)
            {
                result = localResult;
                m_LookupHandle = handle;
                m_Address = address;
                return true;
            }

            if (handle != null)
            {
                result = default;
                return true;
            }

            result = default;
            return false;
        }

        public bool CheckStatus(out Binding.Baselib_NetworkAddress result, out Binding.Baselib_ErrorState error)
        {
            var errorState = default(Binding.Baselib_ErrorState);
            var localResult = default(Binding.Baselib_NetworkAddress);
            var finished = Binding.Baselib_NetworkAddress_HostnameLookupCheckStatus(m_LookupHandle, &localResult, &errorState);
            if (finished)
            {
                if (errorState.code == Binding.Baselib_ErrorCode.TryAgain)
                {
                    if (RetryCount > 10)
                    {
                        errorState.code = Binding.Baselib_ErrorCode.NoSupportedAddressFound;
                        error = errorState;
                        result = default;
                        return true;
                    }
                    return Initialize(m_Address, out result, out error);
                }
                error = errorState;
                if (errorState.code == Binding.Baselib_ErrorCode.Success)
                {
                    result = localResult;
                }
                else
                {
                    result = default;
                }

                m_LookupHandle = null;
                return true;
            }

            result = default;
            error = default;
            return false;
        }
    }
}