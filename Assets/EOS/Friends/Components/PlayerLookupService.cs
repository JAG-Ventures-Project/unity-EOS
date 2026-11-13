using System;
using System.Collections.Generic;
using EOS.Auth;
using EOS.Core;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using UnityEngine.Assertions;

namespace EOS.Friends
{
    /// <summary>
    /// Resolves player identifiers (32-char PUID or external Epic/Steam accounts) to ProductUserId
    /// and performs batch PUID mapping for native platform friend lists.
    /// </summary>
    internal sealed class PlayerLookupService
    {
        private readonly ConnectInterface _connectHandle;

        private Action<PlayerLookupResult> _pendingLookupCallback;
        private string _pendingSearchIdentifier;
        private Func<string, FriendRelationStatus> _pendingRelationshipLookup;

        public PlayerLookupService(ConnectInterface connectHandle)
        {
            _connectHandle = connectHandle;
        }

        public void ResolvePlatformFriendsPuids(List<PlatformFriend> platformFriends, Action onComplete)
        {
            if (platformFriends == null || platformFriends.Count == 0 || _connectHandle == null)
            {
                onComplete?.Invoke();
                return;
            }

            QueryBatchMappings(
                platformFriends,
                LoginProvider.Epic,
                ExternalAccountType.Epic,
                onComplete: () => QueryBatchMappings(
                    platformFriends,
                    LoginProvider.Steam,
                    ExternalAccountType.Steam,
                    onComplete));
        }

        public void ResolveExternalPlatformPuid(FriendModel friend, Action<bool, ProductUserId> onComplete)
        {
            if (friend == null || string.IsNullOrEmpty(friend.PlatformUserId) || _connectHandle == null)
            {
                onComplete?.Invoke(false, null);
                return;
            }

            ProductUserId localPuid = GetLocalPuid();
            if (localPuid == null)
            {
                onComplete?.Invoke(false, null);
                return;
            }

            ExternalAccountType accountType = friend.Platform == LoginProvider.Epic
                ? ExternalAccountType.Epic
                : ExternalAccountType.Steam;

            var queryOptions = new QueryExternalAccountMappingsOptions
            {
                LocalUserId = localPuid,
                AccountIdType = accountType,
                ExternalAccountIds = new[] { friend.PlatformUserId }
            };

            _connectHandle.QueryExternalAccountMappings(
                queryOptions,
                (this, friend, accountType, onComplete),
                HandleSinglePlatformResolveCallback);
        }

        public void ResolvePlayer(
            string identifier,
            Func<string, FriendRelationStatus> getRelationship,
            Action<PlayerLookupResult> onComplete)
        {
            Assert.IsNotNull(onComplete, "[PlayerLookupService] onComplete callback must not be null.");

            if (string.IsNullOrWhiteSpace(identifier))
            {
                onComplete(PlayerLookupResult.Failure(identifier, "Search query cannot be empty."));
                return;
            }

            string trimmed = identifier.Trim();

            string localPuidString = ConnectManager.Instance?.LocalProductUserIdString;
            if (string.Equals(trimmed, localPuidString, StringComparison.OrdinalIgnoreCase))
            {
                onComplete(PlayerLookupResult.Failure(identifier, "You cannot search or add yourself."));
                return;
            }

            // Case 1: Direct 32-character hexadecimal ProductUserId
            if (trimmed.Length == 32)
            {
                ProductUserId puid = ProductUserId.FromString(trimmed);
                if (puid != null && puid.IsValid())
                {
                    ProductUserId localPuid = GetLocalPuid();
                    if (localPuid != null && _connectHandle != null)
                    {
                        _pendingSearchIdentifier = trimmed;
                        _pendingLookupCallback = onComplete;
                        _pendingRelationshipLookup = getRelationship;

                        var queryOptions = new QueryProductUserIdMappingsOptions
                        {
                            LocalUserId = localPuid,
                            ProductUserIds = new[] { puid }
                        };

                        _connectHandle.QueryProductUserIdMappings(queryOptions, this, HandleQueryProductUserIdMappingsCallback);
                        return;
                    }

                    FriendRelationStatus status = getRelationship != null ? getRelationship(trimmed) : FriendRelationStatus.None;
                    var model = new FriendModel
                    {
                        ProductUserId = trimmed,
                        DisplayName = "Player",
                        Platform = LoginProvider.None,
                        Status = status,
                        Presence = FriendPresenceState.Online
                    };
                    onComplete(PlayerLookupResult.Success(trimmed, model));
                    return;
                }
            }

            // Case 2: External Account ID (e.g. Steam ID64) query via ConnectInterface
            ProductUserId callerPuid = GetLocalPuid();
            if (callerPuid != null && _connectHandle != null)
            {
                _pendingSearchIdentifier = trimmed;
                _pendingLookupCallback = onComplete;
                _pendingRelationshipLookup = getRelationship;

                var queryOptions = new QueryExternalAccountMappingsOptions
                {
                    LocalUserId = callerPuid,
                    AccountIdType = ExternalAccountType.Steam,
                    ExternalAccountIds = new[] { trimmed }
                };

                _connectHandle.QueryExternalAccountMappings(queryOptions, this, HandleLookupExternalAccountMappingsCallback);
                return;
            }

            onComplete(PlayerLookupResult.Failure(trimmed, "Player not found. Provide a valid 32-character Product User ID."));
        }

        private void QueryBatchMappings(
            List<PlatformFriend> platformFriends,
            LoginProvider provider,
            ExternalAccountType accountType,
            Action onComplete)
        {
            ProductUserId localPuid = GetLocalPuid();
            if (localPuid == null)
            {
                onComplete?.Invoke();
                return;
            }

            var externalIds = new List<string>();
            for (int i = 0; i < platformFriends.Count; i++)
            {
                PlatformFriend pf = platformFriends[i];
                if (pf.Platform == provider && !string.IsNullOrEmpty(pf.PlatformUserId))
                {
                    externalIds.Add(pf.PlatformUserId);
                }
            }

            if (externalIds.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var queryOptions = new QueryExternalAccountMappingsOptions
            {
                LocalUserId = localPuid,
                AccountIdType = accountType,
                ExternalAccountIds = externalIds.ToArray()
            };

            _connectHandle.QueryExternalAccountMappings(
                queryOptions,
                (this, platformFriends, provider, accountType, onComplete),
                HandleBatchMappingsCallback);
        }

        private static void HandleBatchMappingsCallback(QueryExternalAccountMappingsCallbackInfo data)
        {
            var (service, platformFriends, provider, accountType, onComplete) =
                ((PlayerLookupService, List<PlatformFriend>, LoginProvider, ExternalAccountType, Action))data.ClientData;

            ProductUserId localPuid = GetLocalPuid();
            if (data.ResultCode == Result.Success && localPuid != null)
            {
                for (int i = 0; i < platformFriends.Count; i++)
                {
                    PlatformFriend pf = platformFriends[i];
                    if (pf.Platform != provider || string.IsNullOrEmpty(pf.PlatformUserId)) continue;

                    var getOptions = new GetExternalAccountMappingsOptions
                    {
                        LocalUserId = localPuid,
                        AccountIdType = accountType,
                        TargetExternalUserId = pf.PlatformUserId
                    };

                    ProductUserId mapped = service._connectHandle.GetExternalAccountMapping(getOptions);
                    if (mapped != null && mapped.IsValid())
                    {
                        pf.ProductUserId = mapped.ToString();
                    }
                }
            }

            onComplete?.Invoke();
        }

        private static void HandleSinglePlatformResolveCallback(QueryExternalAccountMappingsCallbackInfo data)
        {
            var (service, friend, accountType, onComplete) =
                ((PlayerLookupService, FriendModel, ExternalAccountType, Action<bool, ProductUserId>))data.ClientData;

            ProductUserId localPuid = GetLocalPuid();
            if (data.ResultCode == Result.Success && localPuid != null)
            {
                var getOptions = new GetExternalAccountMappingsOptions
                {
                    LocalUserId = localPuid,
                    AccountIdType = accountType,
                    TargetExternalUserId = friend.PlatformUserId
                };

                ProductUserId mapped = service._connectHandle.GetExternalAccountMapping(getOptions);
                if (mapped != null && mapped.IsValid())
                {
                    friend.ProductUserId = mapped.ToString();
                    onComplete?.Invoke(true, mapped);
                    return;
                }
            }

            onComplete?.Invoke(false, null);
        }

        private static void HandleLookupExternalAccountMappingsCallback(QueryExternalAccountMappingsCallbackInfo data)
        {
            if (data.ClientData is not PlayerLookupService service) return;

            Action<PlayerLookupResult> callback = service._pendingLookupCallback;
            string identifier = service._pendingSearchIdentifier;
            Func<string, FriendRelationStatus> getRelationship = service._pendingRelationshipLookup;
            service.ClearPendingLookup();

            if (data.ResultCode != Result.Success)
            {
                callback?.Invoke(PlayerLookupResult.Failure(identifier, "Player not found on the network."));
                return;
            }

            var getOptions = new GetExternalAccountMappingsOptions
            {
                LocalUserId = GetLocalPuid(),
                AccountIdType = ExternalAccountType.Steam,
                TargetExternalUserId = identifier
            };

            ProductUserId mappedPuid = service._connectHandle.GetExternalAccountMapping(getOptions);
            if (mappedPuid != null && mappedPuid.IsValid())
            {
                string puidStr = mappedPuid.ToString();
                FriendRelationStatus status = getRelationship != null ? getRelationship(puidStr) : FriendRelationStatus.None;
                string resolvedName = identifier;

                var copyOptions = new CopyProductUserInfoOptions { TargetUserId = mappedPuid };
                if (service._connectHandle.CopyProductUserInfo(copyOptions, out ExternalAccountInfo userInfo) == Result.Success &&
                    !string.IsNullOrEmpty(userInfo.DisplayName))
                {
                    resolvedName = userInfo.DisplayName;
                }

                var model = new FriendModel
                {
                    ProductUserId = puidStr,
                    PlatformUserId = identifier,
                    DisplayName = resolvedName,
                    Platform = LoginProvider.Steam,
                    Status = status,
                    Presence = FriendPresenceState.Online
                };
                callback?.Invoke(PlayerLookupResult.Success(identifier, model));
            }
            else
            {
                callback?.Invoke(PlayerLookupResult.Failure(identifier, "Player does not have an active cross-play account."));
            }
        }

        private static void HandleQueryProductUserIdMappingsCallback(QueryProductUserIdMappingsCallbackInfo data)
        {
            if (data.ClientData is not PlayerLookupService service) return;

            Action<PlayerLookupResult> callback = service._pendingLookupCallback;
            string identifier = service._pendingSearchIdentifier;
            Func<string, FriendRelationStatus> getRelationship = service._pendingRelationshipLookup;
            service.ClearPendingLookup();

            ProductUserId targetPuid = ProductUserId.FromString(identifier);
            string displayName = null;
            LoginProvider platform = LoginProvider.None;

            if (service._connectHandle != null)
            {
                var copyOptions = new CopyProductUserInfoOptions { TargetUserId = targetPuid };
                if (service._connectHandle.CopyProductUserInfo(copyOptions, out ExternalAccountInfo userInfo) == Result.Success)
                {
                    displayName = userInfo.DisplayName;
                    platform = MapExternalAccountType(userInfo.AccountIdType);
                }

                if (string.IsNullOrEmpty(displayName))
                {
                    var countOptions = new GetProductUserExternalAccountCountOptions { TargetUserId = targetPuid };
                    uint count = service._connectHandle.GetProductUserExternalAccountCount(countOptions);
                    for (uint i = 0; i < count; i++)
                    {
                        var copyByIndex = new CopyProductUserExternalAccountByIndexOptions
                        {
                            TargetUserId = targetPuid,
                            ExternalAccountInfoIndex = i
                        };
                        if (service._connectHandle.CopyProductUserExternalAccountByIndex(copyByIndex, out ExternalAccountInfo extInfo) == Result.Success &&
                            !string.IsNullOrEmpty(extInfo.DisplayName))
                        {
                            displayName = extInfo.DisplayName;
                            platform = MapExternalAccountType(extInfo.AccountIdType);
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(displayName))
            {
                displayName = "Player";
            }

            FriendRelationStatus status = getRelationship != null ? getRelationship(identifier) : FriendRelationStatus.None;
            var model = new FriendModel
            {
                ProductUserId = identifier,
                DisplayName = displayName,
                Platform = platform,
                Status = status,
                Presence = FriendPresenceState.Online
            };

            callback?.Invoke(PlayerLookupResult.Success(identifier, model));
        }

        private void ClearPendingLookup()
        {
            _pendingLookupCallback = null;
            _pendingSearchIdentifier = null;
            _pendingRelationshipLookup = null;
        }

        private static ProductUserId GetLocalPuid()
        {
            return ConnectManager.Instance?.LocalProductUserId;
        }

        private static LoginProvider MapExternalAccountType(ExternalAccountType type)
        {
            return type switch
            {
                ExternalAccountType.Epic => LoginProvider.Epic,
                ExternalAccountType.Steam => LoginProvider.Steam,
                _ => LoginProvider.Guest
            };
        }
    }
}

