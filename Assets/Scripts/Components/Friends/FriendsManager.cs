using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Platform;
using UnityEngine;
using UnityEngine.Assertions;

namespace EOS
{
    // AI implementation: High-level social coordinator unifying native platform friends, cross-platform cloud friends, and blocklists
    public class FriendsManager : IFriendsService
    {
        private readonly PlatformInterface _platformHandle;
        private readonly ConnectInterface _connectHandle;
        private readonly ICrossPlayFriendsStore _friendsStore;
        private readonly ILobbyInviteService _inviteService;
        private IPlatformFriendsProvider _platformProvider;

        private readonly List<FriendModel> _allFriends = new();
        private readonly List<FriendModel> _blockedPlayers = new();
        private List<PlatformFriend> _cachedPlatformFriends = new();

        private Action _pendingRefreshComplete;
        private Action<bool, string> _pendingAddFriendCallback;
        private string _pendingSearchIdentifier;

        public IReadOnlyList<FriendModel> AllFriends => _allFriends;
        public IReadOnlyList<FriendModel> BlockedPlayers => _blockedPlayers;
        public bool IsRefreshing { get; private set; }

        public event Action OnFriendsListUpdated;
        public event Action<FriendModel> OnFriendStatusChanged;

        public FriendsManager(
            PlatformInterface platformInterface,
            ICrossPlayFriendsStore friendsStore,
            ILobbyInviteService inviteService)
        {
            _platformHandle = platformInterface;
            _friendsStore = friendsStore;
            _inviteService = inviteService;

            Assert.IsNotNull(_platformHandle, "[FriendsManager] PlatformInterface must not be null.");
            Assert.IsNotNull(_friendsStore, "[FriendsManager] ICrossPlayFriendsStore must not be null.");
            Assert.IsNotNull(_inviteService, "[FriendsManager] ILobbyInviteService must not be null.");

            _connectHandle = _platformHandle.GetConnectInterface();
        }

        public void Initialize()
        {
            ConfigurePlatformProvider();
            _platformProvider?.Initialize();
        }

        private void ConfigurePlatformProvider()
        {
            switch (AuthPreferences.LastLoginProvider)
            {
                case LoginProvider.Steam:
                    _platformProvider = new SteamFriendsProvider();
                    break;
                case LoginProvider.Epic:
                    _platformProvider = new EpicFriendsProvider(_platformHandle, EOSSDKManager.Instance.AuthManager);
                    break;
                default:
                    _platformProvider = null;
                    break;
            }
        }

        public void RefreshFriends(Action onComplete = null)
        {
            if (IsRefreshing) return;

            if (_platformProvider == null || _platformProvider.Platform != AuthPreferences.LastLoginProvider)
            {
                _platformProvider?.Shutdown();
                ConfigurePlatformProvider();
                _platformProvider?.Initialize();
            }

            IsRefreshing = true;
            _pendingRefreshComplete = onComplete;

            if (_platformProvider != null && _platformProvider.IsAvailable)
            {
                _platformProvider.FetchFriends(HandlePlatformFriendsFetched);
            }
            else
            {
                _cachedPlatformFriends.Clear();
                HandlePlatformFriendsFetched(_cachedPlatformFriends);
            }
        }

        private void HandlePlatformFriendsFetched(List<PlatformFriend> platformFriends)
        {
            _cachedPlatformFriends = platformFriends ?? new List<PlatformFriend>();
            _friendsStore.Load(HandleCloudStoreLoaded);
        }

        private void HandleCloudStoreLoaded(bool success)
        {
            BuildUnifiedFriendsList();
            IsRefreshing = false;

            OnFriendsListUpdated?.Invoke();

            Action callback = _pendingRefreshComplete;
            _pendingRefreshComplete = null;
            callback?.Invoke();
        }

        private void BuildUnifiedFriendsList()
        {
            _allFriends.Clear();
            _blockedPlayers.Clear();

            // 1. Process Blocked Players from Cloud Store
            var blockedEntries = _friendsStore.Blocked;
            for (int i = 0; i < blockedEntries.Count; i++)
            {
                CrossPlayFriendEntry b = blockedEntries[i];
                _blockedPlayers.Add(new FriendModel
                {
                    ProductUserId = b.productUserId,
                    DisplayName = b.displayName,
                    Platform = (LoginProvider)b.platform,
                    Status = FriendRelationStatus.Blocked,
                    Presence = FriendPresenceState.Offline,
                    AvatarTexture = null
                });
            }

            var addedKeys = new HashSet<string>();

            // 2. Process Native Platform Friends
            for (int i = 0; i < _cachedPlatformFriends.Count; i++)
            {
                PlatformFriend pf = _cachedPlatformFriends[i];
                if (string.IsNullOrEmpty(pf.PlatformUserId)) continue;

                // Check if blocked
                if (_friendsStore.IsBlocked(pf.PlatformUserId)) continue;

                _allFriends.Add(new FriendModel
                {
                    PlatformUserId = pf.PlatformUserId,
                    DisplayName = pf.DisplayName,
                    Platform = pf.Platform,
                    Status = FriendRelationStatus.Platform,
                    Presence = pf.Presence,
                    AvatarTexture = pf.AvatarTexture
                });

                addedKeys.Add(pf.PlatformUserId);
            }

            // 3. Process Cloud Cross-Play Friends
            var crossPlayEntries = _friendsStore.Friends;
            for (int i = 0; i < crossPlayEntries.Count; i++)
            {
                CrossPlayFriendEntry cf = crossPlayEntries[i];
                if (string.IsNullOrEmpty(cf.productUserId)) continue;
                if (addedKeys.Contains(cf.productUserId)) continue;
                if (_friendsStore.IsBlocked(cf.productUserId)) continue;

                _allFriends.Add(new FriendModel
                {
                    ProductUserId = cf.productUserId,
                    DisplayName = cf.displayName,
                    Platform = (LoginProvider)cf.platform,
                    Status = FriendRelationStatus.CrossPlay,
                    Presence = FriendPresenceState.Online,
                    AvatarTexture = null
                });

                addedKeys.Add(cf.productUserId);
            }
        }

        public void AddFriend(string searchIdentifier, Action<bool, string> onComplete = null)
        {
            if (string.IsNullOrWhiteSpace(searchIdentifier))
            {
                onComplete?.Invoke(false, "Search identifier cannot be empty.");
                return;
            }

            string trimmed = searchIdentifier.Trim();

            // Case 1: Direct 32-character hexadecimal ProductUserId
            if (trimmed.Length == 32)
            {
                ProductUserId puid = ProductUserId.FromString(trimmed);
                if (puid != null && puid.IsValid())
                {
                    ExecuteAddFriendByProductUserId(trimmed, "Player", LoginProvider.None, onComplete);
                    return;
                }
            }

            // Case 2: External Account ID (e.g. Steam ID64) query via ConnectInterface
            if (_connectHandle != null && EOSSDKManager.Instance.ConnectManager?.LocalProductUserId != null)
            {
                _pendingSearchIdentifier = trimmed;
                _pendingAddFriendCallback = onComplete;

                var queryOptions = new QueryExternalAccountMappingsOptions
                {
                    LocalUserId = EOSSDKManager.Instance.ConnectManager.LocalProductUserId,
                    AccountIdType = ExternalAccountType.Steam,
                    ExternalAccountIds = new[] { trimmed }
                };

                _connectHandle.QueryExternalAccountMappings(queryOptions, this, HandleQueryExternalAccountMappingsCallback);
                return;
            }

            onComplete?.Invoke(false, "Player not found. Please provide a valid 32-character Product User ID.");
        }

        private static void HandleQueryExternalAccountMappingsCallback(QueryExternalAccountMappingsCallbackInfo data)
        {
            var manager = data.ClientData as FriendsManager;
            if (manager == null) return;

            Action<bool, string> callback = manager._pendingAddFriendCallback;
            string identifier = manager._pendingSearchIdentifier;
            manager._pendingAddFriendCallback = null;
            manager._pendingSearchIdentifier = null;

            if (data.ResultCode != Result.Success)
            {
                callback?.Invoke(false, "Player not found on the network.");
                return;
            }

            var getOptions = new GetExternalAccountMappingsOptions
            {
                LocalUserId = EOSSDKManager.Instance.ConnectManager.LocalProductUserId,
                AccountIdType = ExternalAccountType.Steam,
                TargetExternalUserId = identifier
            };

            ProductUserId mappedPuid = manager._connectHandle.GetExternalAccountMapping(getOptions);
            if (mappedPuid != null && mappedPuid.IsValid())
            {
                manager.ExecuteAddFriendByProductUserId(mappedPuid.ToString(), identifier, LoginProvider.Steam, callback);
            }
            else
            {
                callback?.Invoke(false, "Player does not have an active cross-play account.");
            }
        }

        private Action<bool, string> _pendingStoreMutationCallback;

        private void HandleStoreMutationSaved(bool success, string error)
        {
            Action<bool, string> callback = _pendingStoreMutationCallback;
            _pendingStoreMutationCallback = null;

            if (success)
            {
                BuildUnifiedFriendsList();
                OnFriendsListUpdated?.Invoke();
            }

            callback?.Invoke(success, error);
        }

        private void ExecuteAddFriendByProductUserId(string puid, string displayName, LoginProvider platform, Action<bool, string> onComplete)
        {
            bool added = _friendsStore.AddFriend(puid, displayName, platform);
            if (!added)
            {
                onComplete?.Invoke(false, "Player is already a friend or is blocked.");
                return;
            }

            _pendingStoreMutationCallback = onComplete;
            _friendsStore.Save(HandleStoreMutationSaved);
        }

        public void RemoveFriend(string productUserId, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(string.IsNullOrEmpty(productUserId), "[FriendsManager] productUserId must not be null or empty.");

            bool removed = _friendsStore.RemoveFriend(productUserId);
            if (!removed)
            {
                onComplete?.Invoke(false, "Player was not in cross-play friends list.");
                return;
            }

            _pendingStoreMutationCallback = onComplete;
            _friendsStore.Save(HandleStoreMutationSaved);
        }

        public void BlockPlayer(string productUserId, string displayName, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(string.IsNullOrEmpty(productUserId), "[FriendsManager] productUserId must not be null or empty.");

            _friendsStore.BlockPlayer(productUserId, displayName, LoginProvider.None);
            _pendingStoreMutationCallback = onComplete;
            _friendsStore.Save(HandleStoreMutationSaved);
        }

        public void UnblockPlayer(string productUserId, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(string.IsNullOrEmpty(productUserId), "[FriendsManager] productUserId must not be null or empty.");

            bool unblocked = _friendsStore.UnblockPlayer(productUserId);
            if (!unblocked)
            {
                onComplete?.Invoke(false, "Player was not blocked.");
                return;
            }

            _pendingStoreMutationCallback = onComplete;
            _friendsStore.Save(HandleStoreMutationSaved);
        }

        public void InviteFriendToLobby(ProductUserId targetUserId, Action<bool, string> onComplete = null)
        {
            _inviteService.InviteFriendToLobby(targetUserId, onComplete);
        }

        public void Shutdown()
        {
            _platformProvider?.Shutdown();
            _allFriends.Clear();
            _blockedPlayers.Clear();
            _cachedPlatformFriends.Clear();
        }
    }
}
