#if STEAMWORKS
using System;
using UnityEngine;
using Steamworks;
using EOS.Friends;

namespace EOS.Core
{
    /// <summary>
    /// Production Steam implementation of ISteamService using Steamworks.NET.
    /// Active only when the STEAMWORKS scripting define symbol is enabled.
    /// </summary>
    public class SteamService : ISteamService
    {
        private HAuthTicket _ticket = HAuthTicket.Invalid;
        private Callback<GetTicketForWebApiResponse_t> _ticketCallback;
        private Action<bool, string, string> _pendingTicketCallback;

        public bool IsAvailable => SteamManager.Initialized;

        public bool HasActiveTicket => _ticket != HAuthTicket.Invalid;

        public string GetPersonaName()
        {
            if (!IsAvailable) return null;
            return SteamFriends.GetPersonaName();
        }

        public Texture2D GetAvatar()
        {
            return SteamProfileUtility.GetLocalAvatar();
        }

        public void RequestWebTicket(Action<bool, string, string> onComplete)
        {
            if (!IsAvailable)
            {
                onComplete?.Invoke(false, "Steam is not open or initialized. Please make sure Steam is running.", null);
                return;
            }

            _pendingTicketCallback = onComplete;
            _ticketCallback ??= Callback<GetTicketForWebApiResponse_t>.Create(OnSteamTicketReceived);

            _ticket = SteamUser.GetAuthTicketForWebApi("epiconlineservices");
            if (_ticket == HAuthTicket.Invalid)
            {
                var callback = _pendingTicketCallback;
                _pendingTicketCallback = null;
                callback?.Invoke(false, "Failed to request Steam Web API ticket!", null);
            }
        }

        private void OnSteamTicketReceived(GetTicketForWebApiResponse_t result)
        {
            var callback = _pendingTicketCallback;
            _pendingTicketCallback = null;

            if (result.m_eResult != EResult.k_EResultOK)
            {
                CancelTicket();
                callback?.Invoke(false, $"Steam ticket request failed: {result.m_eResult}", null);
                return;
            }

            byte[] ticketBytes = result.m_rgubTicket;
            string ticketHex = BitConverter.ToString(ticketBytes, 0, (int)result.m_cubTicket).Replace("-", "");
            string steamName = SteamFriends.GetPersonaName();
            callback?.Invoke(true, ticketHex, steamName);
        }

        public void CancelTicket()
        {
            if (_ticket != HAuthTicket.Invalid)
            {
                if (SteamManager.Initialized || SteamAPI.IsSteamRunning())
                {
                    SteamUser.CancelAuthTicket(_ticket);
                }
                _ticket = HAuthTicket.Invalid;
            }
        }

        public IPlatformFriendsProvider CreateFriendsProvider()
        {
            return new SteamFriendsProvider();
        }
    }

    internal static class SteamServiceRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterRuntime()
        {
            EOSSDKManager.SteamService = new SteamService();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditor()
        {
            EOSSDKManager.SteamService = new SteamService();
        }
#endif
    }
}
#endif