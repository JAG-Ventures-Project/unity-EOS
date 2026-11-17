using System;

namespace EOS.Core
{
    /// <summary>
    /// Service contract decoupling EOS gameplay and social layers from specific networking frameworks (Mirror, etc.).
    /// </summary>
    public interface INetworkSessionService
    {
        bool IsServerActive { get; }
        bool IsClientConnected { get; }
        bool IsClientActive { get; }

        event Action OnUnexpectedClientDisconnect;

        void StartHost(string targetScene = null);
        void StartClient(string hostAddress);
        void StopNetwork();

        Func<string, string> CustomAddressDisplayNameResolver { get; set; }
        Func<string, bool> ConnectionValidator { get; set; }
        bool IsPlayerInSession(string productUserId, string displayName = null);
    }
}

