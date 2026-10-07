using System;
using Unity.Collections;
using UnityEngine;

namespace Unity.Networking.Transport
{
    /// <summary>
    /// Stores the result of a network operation. This is normally used when a job needs to return
    /// a result to its caller. For example the <see cref="ReceiveJobArguments"/> structure contains
    /// one which is used to report the result of receive operations on network interfaces, which
    /// is then reported through <see cref="NetworkDriver.ReceiveErrorCode"/>.
    /// </summary>
    public struct OperationResult : IDisposable
    {
        private struct ResultData
        {
            public FixedString64Bytes Label;
            public int ErrorCode;
        }

        private NativeReference<ResultData> m_Data;

        internal OperationResult(FixedString64Bytes label, Allocator allocator)
        {
            m_Data = new NativeReference<ResultData>(allocator);
            m_Data.Value = new ResultData { Label = label, ErrorCode = 0 };
        }

        /// <summary>
        /// Get and set the error code for the operation. Setting a non-zero value will result in
        /// the error also being logged to the console.
        /// </summary>
        /// <value>Numerical error code (0 is success, anything else is an error).</value>
        public int ErrorCode
        {
            get => m_Data.Value.ErrorCode;
            set
            {
                if (value != 0)
                {
                    Debug.LogError($"Error on {m_Data.Value.Label}, errorCode = {value}");
                }
                m_Data.Value = new ResultData { Label = m_Data.Value.Label, ErrorCode = value };
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_Data.IsCreated)
                m_Data.Dispose();
        }
    }
}
