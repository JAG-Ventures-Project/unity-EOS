using System;
using UnityEngine;

namespace EOS.Friends
{
    /// <summary>
    /// Evaluates which card actions are valid for a target player/invite in a given tab
    /// and executes the selected action against the friends and lobby invite services.
    /// </summary>
    internal sealed class FriendActionExecutor
    {
        private IFriendsService _friendsService;
        private ILobbyInviteService _lobbyInviteService;

        public IFriendsService FriendsService => _friendsService;

        public event Action OnActionExecuted;

        public void Initialize(IFriendsService friendsService, ILobbyInviteService lobbyInviteService)
        {
            _friendsService = friendsService;
            _lobbyInviteService = lobbyInviteService;
        }

        public static bool IsActionAvailable(
            FriendActionType action,
            FriendModel friend,
            FriendsTabType tab,
            IFriendsService friendsService)
        {
            if (action == FriendActionType.None)
            {
                return false;
            }

            return tab switch
            {
                FriendsTabType.Blocked => action == FriendActionType.Unblock,

                FriendsTabType.AddFriend => action == FriendActionType.AddFriend &&
                    friend.Status != FriendRelationStatus.CrossPlay &&
                    friend.Status != FriendRelationStatus.Platform &&
                    friend.Status != FriendRelationStatus.PendingOutgoing &&
                    friend.Status != FriendRelationStatus.Blocked,

                FriendsTabType.Notifications => action == FriendActionType.Accept ||
                    action == FriendActionType.Decline ||
                    action == FriendActionType.Block,

                FriendsTabType.Friends => IsFriendsTabActionAvailable(action, friend, friendsService),

                _ => false
            };
        }

        private static bool IsFriendsTabActionAvailable(FriendActionType action, FriendModel friend, IFriendsService friendsService)
        {
            bool inSameSession = friendsService.IsPlayerInSameLobbyOrGame(friend);

            return action switch
            {
                FriendActionType.Invite => !inSameSession && friend.Presence != FriendPresenceState.Offline,
                FriendActionType.Join => !inSameSession && (friend.Presence == FriendPresenceState.InMatch || friend.Presence == FriendPresenceState.InLobby),
                FriendActionType.Remove => friend.Status == FriendRelationStatus.CrossPlay,
                FriendActionType.Block => true,
                _ => false
            };
        }

        public void Execute(FriendActionType action, FriendModel friend, LobbyInviteModel invite)
        {
            if (invite != null)
            {
                ExecuteForInvite(action, invite);
                return;
            }

            string id = !string.IsNullOrEmpty(friend.ProductUserId) ? friend.ProductUserId : friend.PlatformUserId;

            switch (action)
            {
                case FriendActionType.AddFriend:
                    _friendsService.SendFriendRequest(id, friend.DisplayName, friend.Platform, HandleActionCallback);
                    break;

                case FriendActionType.Invite:
                    if (_friendsService.IsPlayerInSameLobbyOrGame(friend)) return;
                    _friendsService.InviteFriend(friend, HandleActionCallback);
                    break;

                case FriendActionType.Join:
                    if (_friendsService.IsPlayerInSameLobbyOrGame(friend)) return;
                    LobbyInviteModel pendingInvite = _friendsService.GetPendingInviteForFriend(friend);
                    if (pendingInvite != null)
                    {
                        _lobbyInviteService.AcceptInvite(pendingInvite.InviteId, HandleActionCallback);
                        return;
                    }
                    _friendsService.SendJoinRequest(friend, HandleActionCallback);
                    break;

                case FriendActionType.Accept:
                    if (friend.IsJoinRequest)
                        _friendsService.AcceptJoinRequest(id, HandleActionCallback);
                    else
                        _friendsService.AcceptFriendRequest(id, HandleActionCallback);
                    break;

                case FriendActionType.Decline:
                    if (friend.IsJoinRequest)
                        _friendsService.DeclineJoinRequest(id, HandleActionCallback);
                    else
                        _friendsService.DeclineFriendRequest(id, HandleActionCallback);
                    break;

                case FriendActionType.Block:
                    _friendsService.BlockFriend(friend, HandleActionCallback);
                    break;

                case FriendActionType.Unblock:
                    _friendsService.UnblockPlayer(id, HandleActionCallback);
                    break;

                case FriendActionType.Remove:
                    _friendsService.RemoveFriend(id, HandleActionCallback);
                    break;
            }
        }

        private void ExecuteForInvite(FriendActionType action, LobbyInviteModel invite)
        {
            switch (action)
            {
                case FriendActionType.Accept:
                    _lobbyInviteService.AcceptInvite(invite.InviteId, HandleActionCallback);
                    break;

                case FriendActionType.Decline:
                    _lobbyInviteService.DeclineInvite(invite.InviteId);
                    OnActionExecuted?.Invoke();
                    break;

                case FriendActionType.Block:
                    _lobbyInviteService.DeclineInvite(invite.InviteId);
                    if (invite.InviterUserId != null && invite.InviterUserId.IsValid())
                    {
                        _friendsService.BlockPlayer(invite.InviterUserId.ToString(), invite.InviterDisplayName, HandleActionCallback);
                    }
                    else
                    {
                        OnActionExecuted?.Invoke();
                    }
                    break;
            }
        }

        private void HandleActionCallback(bool success, string message)
        {
            if (!success && !string.IsNullOrEmpty(message))
            {
                Debug.LogWarning($"[FriendActionExecutor] Action failed: {message}");
            }
            OnActionExecuted?.Invoke();
        }
    }
}
