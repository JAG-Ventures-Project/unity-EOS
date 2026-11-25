using System;
using EOS.Auth;
using EOS.Core;
using System.Collections.Generic;
using UnityEngine.Assertions;

namespace EOS.Friends
{
    // AI implementation: Cloud storage persistence engine for cross-platform friends, friend requests and blocklists
    public class CrossPlayFriendsStore : ICrossPlayFriendsStore
    {
        private const string FriendsDataFileName = "friends_data.json";

        private readonly IPlayerDataStorage _storage;
        private CrossPlayFriendsData _data = new();
        private Action<bool> _pendingLoadCallback;

        public IReadOnlyList<CrossPlayFriendEntry> Friends => _data.friends;
        public IReadOnlyList<CrossPlayFriendEntry> Blocked => _data.blocked;
        public IReadOnlyList<CrossPlayFriendEntry> PendingIncoming => _data.pendingIncoming;
        public IReadOnlyList<CrossPlayFriendEntry> PendingOutgoing => _data.pendingOutgoing;
        public IReadOnlyList<FriendSignal> Outbox => _data.outbox;
        public bool IsLoaded { get; private set; }

        public CrossPlayFriendsStore(IPlayerDataStorage storage)
        {
            _storage = storage;
            Assert.IsNotNull(_storage, "[CrossPlayFriendsStore] IPlayerDataStorage must not be null.");
        }

        public void Load(Action<bool> onComplete)
        {
            _pendingLoadCallback = onComplete;
            _storage.LoadJson<CrossPlayFriendsData>(FriendsDataFileName, HandleLoadJsonCallback);
        }

        private void HandleLoadJsonCallback(bool success, CrossPlayFriendsData data, string error)
        {
            if (success && data != null)
            {
                data.EnsureInitialized();
                _data = data;
            }
            else if (error == PlayerDataStorageManager.NotFoundError)
            {
                // File does not exist yet on cloud; initialize default data
                _data = new CrossPlayFriendsData();
            }

            IsLoaded = _storage.IsFileLoaded(FriendsDataFileName);

            Action<bool> callback = _pendingLoadCallback;
            _pendingLoadCallback = null;
            callback?.Invoke(success || error == PlayerDataStorageManager.NotFoundError);
        }

        public void Save(Action<bool, string> onComplete = null)
        {
            _storage.SaveJson(FriendsDataFileName, _data, onComplete);
        }

        public void Reset()
        {
            _data = new CrossPlayFriendsData();
            _pendingLoadCallback = null;
            IsLoaded = false;
        }

        public bool AddFriend(string productUserId, string displayName, LoginProvider platform)
        {
            Assert.IsFalse(string.IsNullOrEmpty(productUserId), "[CrossPlayFriendsStore] productUserId must not be null or empty.");

            if (IsFriend(productUserId) || IsBlocked(productUserId))
            {
                return false;
            }

            _data.friends.Add(CreateEntry(productUserId, displayName, platform));
            return true;
        }

        public bool RemoveFriend(string productUserId)
        {
            return RemoveEntry(_data.friends, productUserId);
        }

        public bool BlockPlayer(string productUserId, string displayName, LoginProvider platform)
        {
            Assert.IsFalse(string.IsNullOrEmpty(productUserId), "[CrossPlayFriendsStore] productUserId must not be null or empty.");

            // Cancel any pending friend requests in either direction
            RemovePendingIncoming(productUserId);
            RemovePendingOutgoing(productUserId);

            if (IsBlocked(productUserId))
            {
                return false;
            }

            bool wasFriend = IsFriend(productUserId);
            CrossPlayFriendEntry entry = CreateEntry(productUserId, displayName, platform);
            entry.wasFriend = wasFriend;
            _data.blocked.Add(entry);
            return true;
        }

        public bool UnblockPlayer(string productUserId)
        {
            int index = IndexOf(_data.blocked, productUserId);
            if (index < 0) return false;

            CrossPlayFriendEntry entry = _data.blocked[index];
            _data.blocked.RemoveAt(index);

            // If the player was originally a friend and is not currently in the friends list, restore them
            if (entry.wasFriend && !IsFriend(productUserId))
            {
                _data.friends.Add(new CrossPlayFriendEntry
                {
                    productUserId = entry.productUserId,
                    displayName = entry.displayName,
                    platform = entry.platform,
                    addedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
            }

            return true;
        }

        public bool IsFriend(string productUserId)
        {
            return IndexOf(_data.friends, productUserId) >= 0;
        }

        public bool IsBlocked(string productUserId)
        {
            return IndexOf(_data.blocked, productUserId) >= 0;
        }

        public bool TryGetFriend(string productUserId, out CrossPlayFriendEntry entry)
        {
            return TryGetEntry(_data.friends, productUserId, out entry);
        }

        public bool AddPendingIncoming(string productUserId, string displayName, LoginProvider platform)
        {
            Assert.IsFalse(string.IsNullOrEmpty(productUserId), "[CrossPlayFriendsStore] productUserId must not be null or empty.");

            if (IsFriend(productUserId) || IsBlocked(productUserId) || IsPendingIncoming(productUserId))
            {
                return false;
            }

            _data.pendingIncoming.Add(CreateEntry(productUserId, displayName, platform));
            return true;
        }

        public bool RemovePendingIncoming(string productUserId)
        {
            return RemoveEntry(_data.pendingIncoming, productUserId);
        }

        public bool IsPendingIncoming(string productUserId)
        {
            return IndexOf(_data.pendingIncoming, productUserId) >= 0;
        }

        public bool TryGetPendingIncoming(string productUserId, out CrossPlayFriendEntry entry)
        {
            return TryGetEntry(_data.pendingIncoming, productUserId, out entry);
        }

        public bool AddPendingOutgoing(string productUserId, string displayName, LoginProvider platform)
        {
            Assert.IsFalse(string.IsNullOrEmpty(productUserId), "[CrossPlayFriendsStore] productUserId must not be null or empty.");

            if (IsFriend(productUserId) || IsBlocked(productUserId) || IsPendingOutgoing(productUserId))
            {
                return false;
            }

            _data.pendingOutgoing.Add(CreateEntry(productUserId, displayName, platform));
            return true;
        }

        public bool RemovePendingOutgoing(string productUserId)
        {
            return RemoveEntry(_data.pendingOutgoing, productUserId);
        }

        public bool IsPendingOutgoing(string productUserId)
        {
            return IndexOf(_data.pendingOutgoing, productUserId) >= 0;
        }

        public void EnqueueSignal(FriendSignal signal)
        {
            Assert.IsNotNull(signal, "[CrossPlayFriendsStore] signal must not be null.");
            _data.outbox.Add(signal);
        }

        public bool RemoveSignal(string signalId)
        {
            for (int i = 0; i < _data.outbox.Count; i++)
            {
                if (_data.outbox[i].id == signalId)
                {
                    _data.outbox.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        private static CrossPlayFriendEntry CreateEntry(string productUserId, string displayName, LoginProvider platform)
        {
            return new CrossPlayFriendEntry
            {
                productUserId = productUserId,
                displayName = displayName,
                platform = (int)platform,
                addedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
        }

        // Index-based lookups (instead of List.FindIndex/Exists with lambdas) so no closure is allocated per call
        private static int IndexOf(List<CrossPlayFriendEntry> list, string productUserId)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].productUserId == productUserId)
                {
                    return i;
                }
            }
            return -1;
        }

        private static bool RemoveEntry(List<CrossPlayFriendEntry> list, string productUserId)
        {
            int index = IndexOf(list, productUserId);
            if (index < 0)
            {
                return false;
            }

            list.RemoveAt(index);
            return true;
        }

        private static bool TryGetEntry(List<CrossPlayFriendEntry> list, string productUserId, out CrossPlayFriendEntry entry)
        {
            int index = IndexOf(list, productUserId);
            entry = index >= 0 ? list[index] : null;
            return index >= 0;
        }
    }
}