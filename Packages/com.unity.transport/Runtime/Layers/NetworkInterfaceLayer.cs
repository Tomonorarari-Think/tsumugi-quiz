using System;
using Unity.Jobs;

namespace Unity.Networking.Transport
{
    internal struct NetworkInterfaceLayer : INetworkLayer
    {
        private NetworkInterfaceWrapper m_InterfaceWrapper;

        public NetworkInterfaceLayer(NetworkInterfaceWrapper wrapper)
        {
            m_InterfaceWrapper = wrapper;
        }

        public unsafe int Initialize(ref NetworkSettings settings, ref ConnectionList connectionList, ref int packetPadding) => 0;

        public void Dispose() => m_InterfaceWrapper.Dispose();

        public JobHandle ScheduleReceive(ref ReceiveJobArguments arguments, JobHandle dependency)
            => m_InterfaceWrapper.ScheduleReceive(ref arguments, dependency);

        public JobHandle ScheduleSend(ref SendJobArguments arguments, JobHandle dependency)
            => m_InterfaceWrapper.ScheduleSend(ref arguments, dependency);
    }
}
