using System;
#if MIRROR
using Mirror;
#endif

namespace EOS.Test.Analytics
{
    // AI Implementation
    [Serializable]
    public struct PlayerPingData
    {
        public int connectionId;
        public string playerName;
        public int pingMs;

        public PlayerPingData(int connectionId, string playerName, int pingMs)
        {
            this.connectionId = connectionId;
            this.playerName = playerName;
            this.pingMs = pingMs;
        }
    }

    /// <summary>
    /// Network message broadcast by the host/server to all clients containing
    /// the current ping of all connected players (excluding the host).
    /// </summary>
    public struct PlayersPingListMessage
#if MIRROR
        : NetworkMessage
#endif
    {
        public PlayerPingData[] players;
    }

    /// <summary>
    /// Network message sent by a client to register their display name with the server.
    /// </summary>
    public struct RegisterPlayerNameMessage
#if MIRROR
        : NetworkMessage
#endif
    {
        public string playerName;
    }
}
