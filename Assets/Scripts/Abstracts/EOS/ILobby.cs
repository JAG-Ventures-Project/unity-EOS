using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;

namespace EOS
{

    internal interface ILobby
    {
        string CurrentLobbyId { get; }
        string CurrentJoinCode { get; }
        bool IsInLobby { get; }
        bool IsLobbyOwner { get; }
        event Action<string> OnLobbyCreated;      
        event Action<string> OnLobbyJoined;       
        event Action OnLobbyLeft;
        event Action<ProductUserId> OnPlayerJoined;
        event Action<ProductUserId> OnPlayerLeft;
        event Action<string> OnLobbyError;
        void CreateLobby(uint maxMembers, Action<bool, string> onComplete = null);
        void JoinLobbyByCode(string joinCode, Action<bool, string> onComplete = null);
        void LeaveLobby(Action<bool> onComplete = null);
        void Dispose();

    }
}