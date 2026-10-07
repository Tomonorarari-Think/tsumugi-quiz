using System;
using System.Runtime.InteropServices;
using Unity.Burst;

namespace Unity.Networking.Transport
{
    /// <summary>
    /// Convenience wrapper around a Burst function pointer. Should only be used when defining
    /// functions for custom <see cref="INetworkPipelineStage"/> implementations.
    /// </summary>
    /// <typeparam name="T">Type of the delegate.</typeparam>
    public struct TransportFunctionPointer<T> where T : Delegate
    {
        /// <summary>
        /// Construct a wrapped function pointer from a delegate.
        /// </summary>
        /// <param name="executeDelegate">Delegate to wrap.</param>
        public TransportFunctionPointer(T executeDelegate)
        {
            Ptr = BurstCompiler.CompileFunctionPointer(executeDelegate);
        }

        /// <summary>The actual Burst function pointer being wrapped.</summary>
        public readonly FunctionPointer<T> Ptr;
    }
}
