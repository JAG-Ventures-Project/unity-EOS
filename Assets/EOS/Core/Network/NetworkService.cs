using System;

namespace EOS.Core
{
    /// <summary>
    /// Service locator providing access to the active network session service.
    /// Defaults to NullNetworkSessionService if Mirror/networking is not installed or active.
    /// </summary>
    public static class NetworkService
    {
        private static INetworkSessionService _current = new NullNetworkSessionService();

        public static event Action<INetworkSessionService> OnCurrentChanged;

        public static INetworkSessionService Current
        {
            get => _current ??= new NullNetworkSessionService();
            set
            {
                _current = value ?? new NullNetworkSessionService();
                OnCurrentChanged?.Invoke(_current);
            }
        }
    }
}

