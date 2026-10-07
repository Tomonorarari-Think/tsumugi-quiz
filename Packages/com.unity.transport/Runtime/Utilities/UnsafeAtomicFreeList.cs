using System;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Networking.Transport.Utilities.LowLevel.Unsafe
{
    internal unsafe struct UnsafeAtomicFreeList : IDisposable
    {
        private struct InternalState
        {
            [NativeDisableUnsafePtrRestriction]
            public int* Buffer;
            public int BufferSize;
            public int Length;
            public Allocator Allocator;
        }

        [NativeDisableUnsafePtrRestriction]
        private InternalState* m_State;

        public int Capacity => m_State->Length;
        public int InUse => m_State->Buffer[0] - m_State->Buffer[1];

        public bool IsCreated => m_State != null;

        /// <summary>
        /// Initializes a new instance of the AtomicFreeList struct.
        /// </summary>
        /// <param name="capacity">The number of elements the free list can store.</param>
        /// <param name="allocator">The <see cref="Allocator"/> used to allocate the memory.</param>
        public UnsafeAtomicFreeList(int capacity, Allocator allocator)
        {
            m_State = (InternalState*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<InternalState>(), UnsafeUtility.AlignOf<InternalState>(), allocator);
            m_State->Allocator = allocator;
            m_State->BufferSize = UnsafeUtility.SizeOf<int>() * (capacity + 2);
            m_State->Length = capacity;
            m_State->Buffer = (int*)UnsafeUtility.Malloc(m_State->BufferSize, UnsafeUtility.AlignOf<int>(), allocator);
            UnsafeUtility.MemClear(m_State->Buffer, m_State->BufferSize);
        }

        public void Dispose()
        {
            if (IsCreated)
            {
                UnsafeUtility.Free(m_State->Buffer, m_State->Allocator);
                UnsafeUtility.Free(m_State, m_State->Allocator);
                m_State = null;
            }
        }

        public void Reset()
        {
            UnsafeUtility.MemClear(m_State->Buffer, m_State->BufferSize);
        }

        /// <summary>
        /// Inserts an item on top of the stack.
        /// </summary>
        /// <param name="item">The item to push onto the stack.</param>
        public unsafe void Push(int item)
        {
            int* buffer = m_State->Buffer;
            int idx = Interlocked.Increment(ref buffer[1]) - 1;
            while (Interlocked.CompareExchange(ref buffer[idx + 2], item + 1, 0) != 0)
            {
            }
        }

        /// <summary>
        /// Remove and return a value from the top of the stack
        /// </summary>
        /// <remarks>
        /// <value>The removed value from the top of the stack.</value>
        public unsafe int Pop()
        {
            int* buffer = m_State->Buffer;
            int idx = buffer[1] - 1;
            while (idx >= 0 && Interlocked.CompareExchange(ref buffer[1], idx, idx + 1) != idx + 1)
                idx = buffer[1] - 1;

            if (idx >= 0)
            {
                int val = 0;
                while (val == 0)
                {
                    val = Interlocked.Exchange(ref buffer[2 + idx], 0);
                }

                return val - 1;
            }

            idx = Interlocked.Increment(ref buffer[0]) - 1;
            if (idx >= Capacity)
            {
                Interlocked.Decrement(ref buffer[0]);
                return -1;
            }

            return idx;
        }
    }
}
