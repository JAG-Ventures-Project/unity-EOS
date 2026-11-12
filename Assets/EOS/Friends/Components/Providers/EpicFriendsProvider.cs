using System;
using EOS.Auth;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Friends;
using Epic.OnlineServices.Platform;
using Epic.OnlineServices.UserInfo;
using UnityEngine.Assertions;

namespace EOS.Friends
{
    // AI implementation: Native Epic Games platform provider querying Epic Account Services (EAS) friends and presence
    public class EpicFriendsProvider : IPlatformFriendsProvider
    {
        public LoginProvider Platform => LoginProvider.Epic;
        public bool IsAvailable => _authManager != null && _authManager.LocalEpicAccountId != null && _authManager.LocalEpicAccountId.IsValid();

        private readonly PlatformInterface _platformHandle;
        private readonly AuthManager _authManager;
        private readonly FriendsInterface _friendsHandle;
        private readonly UserInfoInterface _userInfoHandle;

        private Action<List<PlatformFriend>> _pendingFetchCallback;

        public EpicFriendsProvider(PlatformInterface platformInterface, AuthManager authManager)
        {
            _platformHandle = platformInterface;
            _authManager = authManager;
            Assert.IsNotNull(_platformHandle, "[EpicFriendsProvider] PlatformInterface must not be null.");

            _friendsHandle = _platformHandle.GetFriendsInterface();
            _userInfoHandle = _platformHandle.GetUserInfoInterface();
        }

        public void Initialize()
        {
            // Initialized via constructor
        }

        public void FetchFriends(Action<List<PlatformFriend>> onComplete)
        {
            if (!IsAvailable || _friendsHandle == null)
            {
                onComplete?.Invoke(new List<PlatformFriend>());
                return;
            }

            _pendingFetchCallback = onComplete;
            var queryOptions = new QueryFriendsOptions { LocalUserId = _authManager.LocalEpicAccountId };
            _friendsHandle.QueryFriends(queryOptions, this, HandleQueryFriendsCallback);
        }

        private static void HandleQueryFriendsCallback(QueryFriendsCallbackInfo data)
        {
            var provider = data.ClientData as EpicFriendsProvider;
            if (provider == null) return;

            var results = new List<PlatformFriend>();

            if (data.ResultCode != Result.Success)
            {
                provider._pendingFetchCallback?.Invoke(results);
                provider._pendingFetchCallback = null;
                return;
            }

            var countOptions = new GetFriendsCountOptions { LocalUserId = provider._authManager.LocalEpicAccountId };
            int count = provider._friendsHandle.GetFriendsCount(countOptions);

            for (int i = 0; i < count; i++)
            {
                var indexOptions = new GetFriendAtIndexOptions
                {
                    LocalUserId = provider._authManager.LocalEpicAccountId,
                    Index = i
                };
                EpicAccountId friendId = provider._friendsHandle.GetFriendAtIndex(indexOptions);
                if (friendId == null || !friendId.IsValid()) continue;

                var statusOptions = new GetStatusOptions
                {
                    LocalUserId = provider._authManager.LocalEpicAccountId,
                    TargetUserId = friendId
                };
                FriendsStatus status = provider._friendsHandle.GetStatus(statusOptions);
                if (status != FriendsStatus.Friends) continue;

                string displayName = friendId.ToString();
                if (provider._userInfoHandle != null)
                {
                    var copyOptions = new CopyUserInfoOptions
                    {
                        LocalUserId = provider._authManager.LocalEpicAccountId,
                        TargetUserId = friendId
                    };
                    if (provider._userInfoHandle.CopyUserInfo(copyOptions, out UserInfoData userInfo) == Result.Success && userInfo != null)
                    {
                        displayName = !string.IsNullOrEmpty(userInfo.DisplayName) ? userInfo.DisplayName : displayName;
                    }
                }

                results.Add(new PlatformFriend
                {
                    PlatformUserId = friendId.ToString(),
                    DisplayName = displayName,
                    Platform = LoginProvider.Epic,
                    Presence = FriendPresenceState.Offline,
                    AvatarTexture = null
                });
            }

            provider._pendingFetchCallback?.Invoke(results);
            provider._pendingFetchCallback = null;
        }

        public void Shutdown()
        {
            _pendingFetchCallback = null;
        }
    }
}