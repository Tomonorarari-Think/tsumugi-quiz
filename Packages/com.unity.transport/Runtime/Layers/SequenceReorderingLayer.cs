using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace Unity.Networking.Transport
{
    // This layer is used in combination with UnreliablePipelineStage (when it's the last-ish stage
    // in a pipeline) to re-order packets that might have arrived out-of-order within an update. We
    // do this in a network layer since the pipeline stage only sees one packet at a time (we could
    // refactor it but it would be messy).
    //
    // Also since this layer looks into the headers of the first pipeline stage, it needs to be very
    // near the top of the network stack. Nothing adding headers must be placed above it otherwise
    // its assumptions about where to find the pipeline byte and sequence number will be off.
    internal struct SequenceReorderingLayer : INetworkLayer
    {
        private const int k_MaxPipelines = 255;

        private NativeBitArray m_PipelinesToReorder;

        public int Initialize(ref NetworkSettings settings, ref ConnectionList connectionList, ref int packetPadding)
        {
            m_PipelinesToReorder = new NativeBitArray(k_MaxPipelines, Allocator.Persistent);

            return 0;
        }

        public void Dispose()
        {
            m_PipelinesToReorder.Dispose();
        }

        [BurstCompile]
        internal struct ReceiveJob : IJob
        {
            public PacketsQueue ReceiveQueue;
            public NativeBitArray.ReadOnly PipelinesToReorder;

            public void Execute()
            {
                var count = ReceiveQueue.Count;

                // We're using a sort of connection- and pipeline-aware insertion sort here. It's
                // really just an insertion sort where on the back/inner loop we we make sure we
                // only consider packets from the same connection and pipeline.
                //
                // Yes, insertion sort is O(n^2) in the worst case and that's bad, but:
                //   1. The number of packets we're expecting to process per update is not expected
                //      to be terribly high. No need to take out the O(n log n) big guns.
                //   2. We're expecting most packets to already be in the right order most of the
                //      time, where adaptive algorithms like insertion sort perform really well.
                //   3. We'd likely need to defer to an existing implementation for a more efficient
                //      sort, and that wouldn't play well with PacketsQueue. (The best approach is
                //      likely to sort indices and then reorder the queue but that's non-trivial.)

                for (int i = 1; i < count; i++)
                {
                    int j = i; // Element we're "sending back" through the sorted part of the queue.
                    int k = j - 1; // Element we're considering for swapping with j.

                    while (j > 0 && k >= 0)
                    {
                        var jPacket = ReceiveQueue[j];
                        if (jPacket.Length < sizeof(byte) + sizeof(ushort))
                            break; // Can't possibly be a packet we're interested in.

                        var jPipeline = jPacket.GetPayloadDataRef<byte>();
                        if (jPipeline == 0 || !PipelinesToReorder.IsSet(jPipeline - 1))
                            break; // Not from a pipeline we're interested in.

                        var kPacket = ReceiveQueue[k];

                        if (kPacket.Length < sizeof(byte) + sizeof(ushort) ||
                            kPacket.GetPayloadDataRef<byte>() != jPipeline ||
                            kPacket.ConnectionRef != jPacket.ConnectionRef)
                        {
                            // This is not a valid swappable candidate. Look further back.
                            k--;
                            continue;
                        }
                        else
                        {
                            // This is a valid swappable candidate. Compare sequence numbers.
                            var jSequence = jPacket.GetPayloadDataRef<ushort>(sizeof(byte));
                            var kSequence = kPacket.GetPayloadDataRef<ushort>(sizeof(byte));

                            if (SequenceHelpers.GreaterThan16(kSequence, jSequence))
                            {
                                // Order between j and k is wrong, swap them and look further back
                                // to see if we need to swap j even further back in the queue.
                                ReceiveQueue.Swap(j, k);
                                j = k;
                                k--;
                            }
                            else
                            {
                                // Packet j is correctly ordered with respect to packet k (and thus
                                // all packets before k too). We're done with this packet.
                                break;
                            }
                        }
                    }
                }
            }
        }

        public JobHandle ScheduleReceive(ref ReceiveJobArguments arguments, JobHandle dependency)
        {
            if (!m_PipelinesToReorder.TestAny(0, k_MaxPipelines))
                return dependency;

            return new ReceiveJob
            {
                ReceiveQueue = arguments.ReceiveQueue,
                PipelinesToReorder = m_PipelinesToReorder.AsReadOnly()
            }.Schedule(dependency);
        }

        public JobHandle ScheduleSend(ref SendJobArguments arguments, JobHandle dependency)
        {
            return dependency;
        }

        internal void MarkPipelineForReordering(byte pipeline)
        {
            m_PipelinesToReorder.Set(pipeline - 1, true);
        }
    }
}