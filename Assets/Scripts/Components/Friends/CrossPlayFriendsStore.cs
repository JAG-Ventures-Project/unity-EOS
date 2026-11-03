using System;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

namespace EOS
{
    // AI implementation: Cloud storage persistence engine for cross-platform friends and blocklists
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
                _data = data;
            }
            else
            {
                // File does not exist yet on cloud or failed; initialize default data
                _data = new CrossPlayFriendsData();
            }

            IsLoaded = true;

            Action<bool> callback = _pendingLoadCallback;
            _pendingLoadCallback = null;
            callback?.Invoke(success || error == "NotFound");
        }

        public void Save(Action<bool, string> onComplete = null)
        {
            _storage.SaveJson(FriendsDataFileName, _data, onComplete);
        }

        public bool AddFriend(string productUserId, string displayName, LoginProvider platform)
        {
            Assert.IsFalse(string.IsNullOrEmpty(productUserId), "[CrossPlayFriendsStore] productUserId must not be null or empty.");

            if (IsFriend(productUserId) || IsBlocked(productUserId))
            {
                return false;
            }

            _data.friends.Add(new CrossPlayFriendEntry
            {
                productUserId = productUserId,
                displayName = displayName,
                platform = (int)platform,
                addedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

            return true;
        }

        public bool RemoveFriend(string productUserId)
        {
            int index = _data.friends.FindIndex(f => f.productUserId == productUserId);
            if (index >= 0)
            {
                _data.friends.RemoveAt(index);
                return true;
            }
            return false;
        }

        public bool BlockPlayer(string productUserId, string displayName, LoginProvider platform)
        {
            Assert.IsFalse(string.IsNullOrEmpty(productUserId), "[CrossPlayFriendsStore] productUserId must not be null or empty.");

            RemoveFriend(productUserId);

            if (!IsBlocked(productUserId))
            {
                _data.blocked.Add(new CrossPlayFriendEntry
                {
                    productUserId = productUserId,
                    displayName = displayName,
                    platform = (int)platform,
                    addedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
                return true;
            }
            return false;
        }

        public bool UnblockPlayer(string productUserId)
        {
            int index = _data.blocked.FindIndex(b => b.productUserId == productUserId);
            if (index >= 0)
            {
                _data.blocked.RemoveAt(index);
                return true;
            }
            return false;
        }

        public bool IsFriend(string productUserId)
        {
            return _data.friends.Exists(f => f.productUserId == productUserId);
        }

        public bool IsBlocked(string productUserId)
        {
            return _data.blocked.Exists(b => b.productUserId == productUserId);
        }
    }
}

