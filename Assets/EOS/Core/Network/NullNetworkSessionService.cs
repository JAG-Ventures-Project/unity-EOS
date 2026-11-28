using System;
using UnityEngine;

namespace EOS.Core
{
    /// <summary>
    /// Safe no-op implementation used when no networking framework (like Mirror) is active.
    /// </summary>
    public class NullNetworkSessionService : INetworkSessionService
    {
        public bool IsServerActive => false;
        public bool IsClientConnected => false;
        public bool IsClientActive => false;
        public bool AutoRejoinMatch => false;

        public event Action OnUnexpectedClientDisconnect
        {
            add { }
            remove { }
        }

        public void StartHost(string targetScene = null)
        {
            Debug.LogWarning("[NetworkSession] No network session provider is active. StartHost skipped.");
        }

        public void StartClient(string hostAddress)
        {
            Debug.LogWarning("[NetworkSession] No network session provider is active. StartClient skipped.");
        }

        public void StopNetwork()
        {
        }

        public Func<string, string> CustomAddressDisplayNameResolver { get; set; }
        public Func<string, bool> ConnectionValidator { get; set; }
        public bool IsPlayerInSession(string productUserId, string displayName = null) => false;
    }
}

