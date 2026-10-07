using System;
using Unity.Collections;
using UnityEngine;

namespace Unity.Networking.Transport
{
    /// <summary>
    /// A generic map where the key is a ConnectionId and stored values correspond to
    /// the last valid connection version. Disconnected connections return the defaultDataValue.
    /// </summary>
    /// <typeparam name="T">Type of the data to store per connection.</typeparam>
    internal struct ConnectionDataMap<T> : IDisposable where T : unmanaged
    {
        private struct ConnectionSlot
        {
            public int Version;
            public T Value;
        }

        // First slot stores the default value.
        private NativeList<ConnectionSlot> m_List;

        public bool IsCreated => m_List.IsCreated;
        public int Length => m_List.Length - 1; // Account for default value at index 0.

        internal ConnectionDataMap(int initialCapacity, T defaultDataValue, Allocator allocator)
        {
            m_List = new NativeList<ConnectionSlot>(initialCapacity + 1, allocator);
            m_List.Add(new ConnectionSlot { Version = 0, Value = defaultDataValue });
        }

        public void Dispose()
        {
            m_List.Dispose();
        }

        internal T this[ConnectionId connection]
        {
            get
            {
                var connectionIndex = connection.Id + 1; // Account for default value at index 0.

                if (connectionIndex >= m_List.Length || connectionIndex <= 0)
                    return m_List[0].Value;

                var slot = m_List[connectionIndex];

                if (slot.Version != connection.Version)
                    return m_List[0].Value;

                return slot.Value;
            }
            set
            {
                var connectionIndex = connection.Id + 1; // Account for default value at index 0.

                if (connectionIndex <= 0)
                    return;

                if (connectionIndex >= m_List.Length)
                    m_List.Resize(connectionIndex + 1, NativeArrayOptions.ClearMemory);

                ref var slot = ref m_List.ElementAt(connectionIndex);

                if (slot.Version > connection.Version)
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    throw new ArgumentOutOfRangeException("The provided connection is not valid");
#else
                    Debug.LogError("The provided connection is not valid");
                    return;
#endif
                }
                
                slot.Version = connection.Version;
                slot.Value = value;
            }
        }

        internal void ClearData(ref ConnectionId connection)
        {
            this[connection] = m_List[0].Value;
        }

        internal ConnectionId ConnectionAt(int index)
        {
            var connectionIndex = index + 1; // Account for default value at index 0.

            if (connectionIndex <= 0 || connectionIndex >= m_List.Length)
                return default;

            return new ConnectionId
            {
                Id = index,
                Version = m_List[connectionIndex].Version,
            };
        }

        internal T DataAt(int index)
        {
            var connectionIndex = index + 1; // Account for default value at index 0.

            if (connectionIndex <= 0 || connectionIndex >= m_List.Length)
                return m_List[0].Value;

            return m_List[connectionIndex].Value;
        }

        public override bool Equals(object obj)
        {
            return obj is ConnectionDataMap<T> map &&
                this == map;
        }

        public override unsafe int GetHashCode()
        {
            return ((int)m_List.GetUnsafeList()).GetHashCode();
        }

        public static unsafe bool operator==(ConnectionDataMap<T> a, ConnectionDataMap<T> b)
        {
            return a.m_List.GetUnsafeList() == b.m_List.GetUnsafeList();
        }

        public static unsafe bool operator!=(ConnectionDataMap<T> a, ConnectionDataMap<T> b)
        {
            return !(a == b);
        }
    }
}
