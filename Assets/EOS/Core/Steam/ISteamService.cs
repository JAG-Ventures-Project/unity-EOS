using System;
using UnityEngine;
using EOS.Friends;

namespace EOS.Core
{
    /// <summary>
    /// Abstraction layer for Steamworks SDK functionality.
    /// Allows the EOS library to compile and run cleanly with or without Steamworks installed.
    /// </summary>
    public interface ISteamService
    {
        bool IsAvailable { get; }
        bool HasActiveTicket { get; }
        string GetPersonaName();
        Texture2D GetAvatar();
        void RequestWebTicket(Action<bool, string, string> onComplete);
        void CancelTicket();
        IPlatformFriendsProvider CreateFriendsProvider();
    }
}

