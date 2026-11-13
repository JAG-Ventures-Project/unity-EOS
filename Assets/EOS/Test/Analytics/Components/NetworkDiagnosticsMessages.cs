#if MIRROR
using Mirror;
#endif

namespace EOS.Test.Analytics
{
    /// <summary>
    /// Probe sent from client to server to measure input-to-ack roundtrip latency and packet loss.
    /// </summary>
    public struct InputProbeMessage
#if MIRROR
        : NetworkMessage
#endif
    {
        public uint sequence;
        public double clientTime;
    }

    /// <summary>
    /// Acknowledgement returned immediately by the server with the server's processing timestamp.
    /// </summary>
    public struct InputProbeAckMessage
#if MIRROR
        : NetworkMessage
#endif
    {
        public uint sequence;
        public double clientTime;
        public double serverTime;
    }
}
