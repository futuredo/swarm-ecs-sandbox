using System;

namespace SwarmECS.Runtime.Networking
{
    [Serializable]
    internal sealed class SwarmUdpSessionReport
    {
        public string version = "0.4.0";
        public string role;
        public uint peerId;
        public int processId;
        public bool success;
        public string failure;
        public uint sessionId;
        public int localPort;
        public int finalTick;
        public int confirmedTick;
        public int predictedTick;
        public int inputDelayTicks;
        public int predictionLeadTicks;
        public int agentCount;
        public uint seed;
        public string logicHash;
        public string configHash;
        public string finalStateHash;
        public string authorityFinalStateHash;
        public int authorityCommands;
        public int receivedAuthorityCommands;
        public int lateAuthorityCommands;
        public int rollbackCount;
        public int rollbackMaximumTicks;
        public int rollbackP50Ticks;
        public int rollbackP95Ticks;
        public int rollbackP99Ticks;
        public int serverHashSamples;
        public int confirmedHashSamples;
        public int unresolvedHashMismatches;
        public int missingLocalHashSamples;
        public long averageRttMilliseconds;
        public long maximumRttMilliseconds;
        public int reliableRetransmissions;
        public int pendingReliablePackets;
        public long receivedDatagrams;
        public long sentDatagrams;
        public long sentBytes;
        public long rejectedDatagrams;
        public long socketErrors;
        public long inboundQueueDrops;
        public long weakScheduled;
        public long weakDelivered;
        public long weakLossDrops;
        public long weakCapacityDrops;
        public long weakDuplicates;
        public long weakReorders;
        public int elapsedMilliseconds;
    }
}
