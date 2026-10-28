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
        int CurrentMemberCount { get; }
        bool IsInLobby { get; }
        bool IsLobbyOwner { get; }
        bool AreAllJoinersReady { get; }
        bool IsCleaningOrphan { get; }
        ProductUserId LocalProductUserId { get; }
        event Action<string> OnLobbyCreated;      
        event Action<string> OnLobbyJoined;       
        event Action OnLobbyLeft;
        event Action<ProductUserId> OnPlayerJoined;
        event Action<ProductUserId> OnPlayerLeft;
        event Action<string> OnLobbyError;
        event Action<string> OnGameStarted;
        event Action<bool> OnReadyStatusChanged;
        event Action OnLobbyUpdated;
        void CreateLobby(uint maxMembers, Action<bool, string> onComplete = null);
        void JoinLobbyByCode(string joinCode, Action<bool, string> onComplete = null);
        void LeaveLobby(Action<bool> onComplete = null);
        void StartGame(Action<bool, string> onComplete = null);
        void SetReadyStatus(bool isReady, Action<bool, string> onComplete = null);
        void SetLocalMemberDisplayName(string displayName, Action<bool, string> onComplete = null);
        void ResetMatchState(Action<bool, string> onComplete = null);
        bool UpdateAndCheckReadyStatus();
        List<ProductUserId> GetCurrentMemberUserIds();
        string GetMemberDisplayName(ProductUserId userId);
        bool IsMemberReady(ProductUserId userId);
        void CheckAndCleanOrphanLobbyOnLogin(Action onComplete = null);
        void Dispose();
    }
}
