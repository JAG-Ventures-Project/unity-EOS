using System;
using System.Collections.Generic;
using EOS.Auth;
using EOS.Core;
using UnityEngine.Assertions;

namespace EOS.Friends
{
    /// <summary>
    /// Owns cross-play friend requests, removals, blocklist CRUD, relationship lookups,
    /// and cloud-store persistence mutations.
    /// </summary>
    internal sealed class FriendRelationshipService
    {
        private readonly ICrossPlayFriendsStore _friendsStore;
        private readonly IFriendSignalChannel _signalChannel;
        private readonly PlayerLookupService _lookupService;
        private readonly Action _notifyListChanged;

        private Action<bool, string> _pendingAddFriendCallback;
        private Action<bool, string> _pendingStoreMutationCallback;
        private FriendSignal _pendingSignalToSend;
        private string _pendingMutationSuccessMessage;

        public FriendRelationshipService(
            ICrossPlayFriendsStore friendsStore,
            IFriendSignalChannel signalChannel,
            PlayerLookupService lookupService,
            Action notifyListChanged)
        {
            _friendsStore = friendsStore;
            _signalChannel = signalChannel;
            _lookupService = lookupService;
            _notifyListChanged = notifyListChanged;
        }

        public FriendRelationStatus GetRelationship(
            string identifier,
            string displayName,
            IReadOnlyList<FriendModel> allFriends,
            IReadOnlyList<PlatformFriend> cachedPlatformFriends)
        {
            if (string.IsNullOrEmpty(identifier) && string.IsNullOrEmpty(displayName)) return FriendRelationStatus.None;
            if (!string.IsNullOrEmpty(identifier))
            {
                if (_friendsStore.IsBlocked(identifier)) return FriendRelationStatus.Blocked;
                if (_friendsStore.IsFriend(identifier)) return FriendRelationStatus.CrossPlay;
                if (_friendsStore.IsPendingIncoming(identifier)) return FriendRelationStatus.PendingIncoming;
                if (_friendsStore.IsPendingOutgoing(identifier)) return FriendRelationStatus.PendingOutgoing;
            }

            for (int i = 0; i < allFriends.Count; i++)
            {
                FriendModel friend = allFriends[i];
                if (!string.IsNullOrEmpty(identifier) && (
                    string.Equals(friend.ProductUserId, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(friend.PlatformUserId, identifier, StringComparison.OrdinalIgnoreCase)))
                {
                    return friend.Status;
                }
                if (!string.IsNullOrEmpty(displayName) &&
                    string.Equals(friend.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    return friend.Status;
                }
            }

            for (int i = 0; i < cachedPlatformFriends.Count; i++)
            {
                PlatformFriend pf = cachedPlatformFriends[i];
                if (!string.IsNullOrEmpty(identifier) && (
                    string.Equals(pf.PlatformUserId, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pf.ProductUserId, identifier, StringComparison.OrdinalIgnoreCase)))
                {
                    return FriendRelationStatus.Platform;
                }
                if (!string.IsNullOrEmpty(displayName) &&
                    string.Equals(pf.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    return FriendRelationStatus.Platform;
                }
            }

            return FriendRelationStatus.None;
        }

        public void AddFriend(
            string searchIdentifier,
            Func<string, FriendRelationStatus> getRelationship,
            Action<bool, string> onComplete = null)
        {
            _pendingAddFriendCallback = onComplete;
            _lookupService.ResolvePlayer(searchIdentifier, getRelationship, HandleAddFriendResolved);
        }

        private void HandleAddFriendResolved(PlayerLookupResult result)
        {
            Action<bool, string> callback = _pendingAddFriendCallback;
            _pendingAddFriendCallback = null;

            if (!result.Found)
            {
                callback?.Invoke(false, result.Error);
                return;
            }

            SendFriendRequest(result.Player.ProductUserId, result.Player.DisplayName, result.Player.Platform, callback);
        }

        public void SendFriendRequest(string productUserId, string displayName, LoginProvider platform, Action<bool, string> onComplete = null)
        {
            if (_friendsStore.IsFriend(productUserId))
            {
                onComplete?.Invoke(false, "Player is already in your friends list.");
                return;
            }
            if (_friendsStore.IsBlocked(productUserId))
            {
                onComplete?.Invoke(false, "Player is blocked. Unblock them first.");
                return;
            }
            if (_friendsStore.IsPendingOutgoing(productUserId))
            {
                onComplete?.Invoke(false, "Friend request already sent.");
                return;
            }

            if (!_friendsStore.AddPendingOutgoing(productUserId, displayName, platform))
            {
                onComplete?.Invoke(false, "Could not queue friend request.");
                return;
            }

            FriendSignal signal = CreateRelationshipSignal(FriendSignalType.FriendRequest, productUserId);
            _friendsStore.EnqueueSignal(signal);
            ExecuteStoreMutation(signal, "Friend request sent!", onComplete);
        }

        public void AcceptFriendRequest(string productUserId, Action<bool, string> onComplete = null)
        {
            string displayName = "Player";
            LoginProvider platform = LoginProvider.None;
            if (_friendsStore.TryGetPendingIncoming(productUserId, out CrossPlayFriendEntry entry))
            {
                displayName = entry.displayName;
                platform = (LoginProvider)entry.platform;
            }

            _friendsStore.RemovePendingIncoming(productUserId);
            _friendsStore.AddFriend(productUserId, displayName, platform);

            FriendSignal signal = CreateRelationshipSignal(FriendSignalType.RequestAccepted, productUserId);
            _friendsStore.EnqueueSignal(signal);
            ExecuteStoreMutation(signal, "Friend request accepted!", onComplete);
        }

        public void DeclineFriendRequest(string productUserId, Action<bool, string> onComplete = null)
        {
            _friendsStore.RemovePendingIncoming(productUserId);

            FriendSignal signal = CreateRelationshipSignal(FriendSignalType.RequestDeclined, productUserId);
            _friendsStore.EnqueueSignal(signal);
            ExecuteStoreMutation(signal, "Friend request declined.", onComplete);
        }

        public void RemoveFriend(string productUserId, Action<bool, string> onComplete = null)
        {
            if (!_friendsStore.RemoveFriend(productUserId))
            {
                onComplete?.Invoke(false, "Player was not in cross-play friends list.");
                return;
            }

            FriendSignal signal = CreateRelationshipSignal(FriendSignalType.Unfriended, productUserId);
            _friendsStore.EnqueueSignal(signal);
            ExecuteStoreMutation(signal, "Friend removed.", onComplete);
        }

        public void BlockPlayer(string productUserId, string displayName, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(string.IsNullOrEmpty(productUserId), "[FriendRelationshipService] productUserId must not be null or empty.");

            _friendsStore.RemovePendingIncoming(productUserId);
            _friendsStore.RemovePendingOutgoing(productUserId);
            _friendsStore.BlockPlayer(productUserId, displayName, LoginProvider.None);

            ExecuteStoreMutation(null, "Player blocked.", onComplete);
        }

        public void BlockFriend(FriendModel friend, Action<bool, string> onComplete = null)
        {
            if (friend == null)
            {
                onComplete?.Invoke(false, "Friend parameter cannot be null.");
                return;
            }

            string id = !string.IsNullOrEmpty(friend.ProductUserId) ? friend.ProductUserId : friend.PlatformUserId;
            BlockPlayer(id, friend.DisplayName, onComplete);
        }

        public void UnblockPlayer(string productUserId, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(string.IsNullOrEmpty(productUserId), "[FriendRelationshipService] productUserId must not be null or empty.");

            if (!_friendsStore.UnblockPlayer(productUserId))
            {
                onComplete?.Invoke(false, "Player was not blocked.");
                return;
            }

            ExecuteStoreMutation(null, "Player unblocked.", onComplete);
        }

        public void HandleRelationshipSignal(string senderPuid, FriendSignal signal)
        {
            switch (signal.type)
            {
                case FriendSignalType.FriendRequest:
                    if (_friendsStore.IsBlocked(senderPuid)) return;
                    if (_friendsStore.IsFriend(senderPuid))
                    {
                        // Mutual healing: We already consider sender a friend, but sender does not have us in their list.
                        FriendSignal autoAccept = CreateRelationshipSignal(FriendSignalType.RequestAccepted, senderPuid);
                        _signalChannel?.Send(senderPuid, autoAccept);
                        return;
                    }

                    string senderName = string.IsNullOrWhiteSpace(signal.senderName) ? "Player" : signal.senderName;
                    _friendsStore.AddPendingIncoming(senderPuid, senderName, (LoginProvider)signal.senderPlatform);
                    _friendsStore.Save(HandleAutoSaveCompleted);
                    break;

                case FriendSignalType.RequestAccepted:
                    _friendsStore.RemovePendingOutgoing(senderPuid);
                    string accName = string.IsNullOrWhiteSpace(signal.senderName) ? "Player" : signal.senderName;
                    _friendsStore.AddFriend(senderPuid, accName, (LoginProvider)signal.senderPlatform);
                    _friendsStore.Save(HandleAutoSaveCompleted);
                    break;

                case FriendSignalType.RequestDeclined:
                    _friendsStore.RemovePendingOutgoing(senderPuid);
                    _friendsStore.Save(HandleAutoSaveCompleted);
                    break;

                case FriendSignalType.Unfriended:
                    if (_friendsStore.RemoveFriend(senderPuid))
                    {
                        _friendsStore.Save(HandleAutoSaveCompleted);
                    }
                    break;
            }
        }

        public void OnSignalAcknowledged(string signalId)
        {
            if (_friendsStore.RemoveSignal(signalId))
            {
                _friendsStore.Save(HandleOutboxSaveCompleted);
            }
        }

        private void ExecuteStoreMutation(FriendSignal signalToSend, string successMessage, Action<bool, string> onComplete)
        {
            _pendingSignalToSend = signalToSend;
            _pendingMutationSuccessMessage = successMessage;
            _pendingStoreMutationCallback = onComplete;
            _friendsStore.Save(HandleStoreMutationSaved);
        }

        private void HandleStoreMutationSaved(bool success, string error)
        {
            FriendSignal signalToSend = _pendingSignalToSend;
            string successMessage = _pendingMutationSuccessMessage;
            Action<bool, string> callback = _pendingStoreMutationCallback;

            _pendingSignalToSend = null;
            _pendingMutationSuccessMessage = null;
            _pendingStoreMutationCallback = null;

            if (success)
            {
                if (signalToSend != null && !string.IsNullOrEmpty(signalToSend.targetPuid))
                {
                    _signalChannel?.Send(signalToSend.targetPuid, signalToSend);
                }

                _notifyListChanged.Invoke();
            }

            callback?.Invoke(success, success ? successMessage : error);
        }

        private static void HandleOutboxSaveCompleted(bool success, string error)
        {
            // Background signal removal completed; no state mutation required
        }

        private void HandleAutoSaveCompleted(bool success, string error)
        {
            if (success)
            {
                _notifyListChanged.Invoke();
            }
        }

        private static FriendSignal CreateRelationshipSignal(FriendSignalType type, string targetPuid)
        {
            return new FriendSignal
            {
                id = Guid.NewGuid().ToString("N"),
                type = type,
                senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                senderPlatform = (int)LocalPlayerIdentity.CurrentPlatform,
                targetPuid = targetPuid,
                createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
        }
    }
}

