using Mirror;

namespace EOS.UI.Analytics
{
    /// <summary>
    /// Probe sent from client to server to measure input-to-ack roundtrip latency and packet loss.
    /// </summary>
    public struct InputProbeMessage : NetworkMessage
    {
        public uint sequence;
        public double clientTime;
    }

    /// <summary>
    /// Acknowledgement returned immediately by the server with the server's processing timestamp.
    /// </summary>
    public struct InputProbeAckMessage : NetworkMessage
    {
        public uint sequence;
        public double clientTime;
        public double serverTime;
    }
}

