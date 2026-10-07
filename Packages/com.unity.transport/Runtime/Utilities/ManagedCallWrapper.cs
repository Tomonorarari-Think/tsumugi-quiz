using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Networking.Transport
{
    /// <summary>
    /// A wrapper for a managed function pointer that can be called from unmanaged contexts (e.g.
    /// from Burst-compiled code). Unlike manually using an unmanaged function pointer, this wrapper
    /// allows function pointers with generic types arguments.
    /// </summary>
    /// <remarks>
    /// The main limitation of this wrapper is that it can't handle various function signatures. As
    /// such, the wrapped managed function must not return anything and mus receive two arguments: a
    /// void pointer and an integer with the size of the data pointed by the void pointer. All
    /// arguments and return values (if any) must be handled through a structure pointed to by the
    /// void pointer argument.
    /// </remarks>
    internal unsafe struct ManagedCallWrapper
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void TrampolineDelegate(void* functionPtr, void* arguments, int argumentsSize);

        // This is the method that will be called from unmanaged code. Every wrapped function is
        // just passed to this method as a function pointer. This is how we can support methods that
        // usually wouldn't be compatible with unmanaged function pointers, such as generic methods.
        [AOT.MonoPInvokeCallback(typeof(TrampolineDelegate))]
        private static void Trampoline(void* functionPtr, void* arguments, int argumentsSize)
        {
            try
            {
                ((delegate * < void*, int, void >)functionPtr)(arguments, argumentsSize);
            }
            catch (Exception e)
            {
                // We can't bubble up execptions through the managed-unmanaged boundary as this is
                // not defined behavior, so we catch everything here and just log the exception.
                UnityEngine.Debug.LogError($"Exception thrown in a managed function called from unmanaged code: {e}");
            }
        }

        private static readonly SharedStatic<IntPtr> s_TrampolinePtr = SharedStatic<IntPtr>.GetOrCreate<ManagedCallWrapper, TrampolineKey>();
        private class TrampolineKey {}

        // We need to keep the trampoline delegate alive for the entire lifetime of the application,
        // so we keep a reference to it here. We also don't reset this static on load since the
        // value remains valid (and unchanged) across domain reloads.
        private static object s_TrampolineDelegateKeepAlive;

        private static void Initialize()
        {
            if (s_TrampolineDelegateKeepAlive != null)
                return;

            var trampolineDelegate = new TrampolineDelegate(Trampoline);
            s_TrampolineDelegateKeepAlive = trampolineDelegate;
            s_TrampolinePtr.Data = Marshal.GetFunctionPointerForDelegate(trampolineDelegate);
        }

        [NativeDisableUnsafePtrRestriction] IntPtr m_WrappedFunctionPtr;

        /// <summary>Whether the wrapping is wrapping anything.</summary>
        public bool IsCreated => m_WrappedFunctionPtr != default;

        /// <summary>
        /// Create a wrapper for a managed function that can be called from Burst.
        /// </summary>
        /// <param name="managedFunctionPtr">
        /// Pointer to a managed function to be wrapped. This function can't return anything and
        /// must receive two arguments: a void pointer and an integer with the size of the data the
        /// void pointer argument is pointing to. All arguments and return values (if any) must be
        /// handled through a structure pointed to by the void pointer argument. Furthermore this
        /// must be a static function (as you can't take addresses of non-static functions).
        /// </param>
        public ManagedCallWrapper(delegate* < void*, int, void > managedFunctionPtr)
        {
            Initialize();
            m_WrappedFunctionPtr = new IntPtr(managedFunctionPtr);
        }

        /// <summary>Invoke the wrapped function with raw pointer/size arguments.</summary>
        public void Invoke(void* arguments, int argumentsSize)
        {
            CheckIsCreated();
            ((delegate * unmanaged[Cdecl] < void*, void*, int, void >)s_TrampolinePtr.Data)(((void*)m_WrappedFunctionPtr), arguments, argumentsSize);
        }

        /// <summary>Invoke the wrapped function with an arguments structure.</summary>
        public void Invoke<T>(ref T arguments) where T : unmanaged
        {
            fixed(void* argumentsPtr = &arguments)
            {
                Invoke(argumentsPtr, UnsafeUtility.SizeOf<T>());
            }
        }

        /// <summary>Utility method to convert a void pointer to an arguments structure.</summary>
        public static ref T ArgumentsFromPtr<T>(void* argumentsPtr, int size) where T : unmanaged
        {
            CheckArgumentsSize<T>(size);
            return ref *(T*)argumentsPtr;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void CheckIsCreated()
        {
            if (!IsCreated)
                throw new NullReferenceException("Trying to invoke a null function pointer.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckArgumentsSize<T>(int size) where T : unmanaged
        {
            if (size != UnsafeUtility.SizeOf<T>())
                throw new InvalidOperationException("The requested argument type size does not match the provided one.");
        }
    }
}
