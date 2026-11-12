using System;
using EOS.Notification;
using EOS.P2P;
using EOS.Lobby;
using EOS.Auth;
using EOS.Core;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Platform;
using UnityEngine;
using UnityEngine.Assertions;

namespace EOS.Friends
{
    /// <Summary>
    /// social coordinator unifying native platform friends, cross-platform cloud friends,
    /// blocklists, and peer-to-peer friend synchronization. Complies strictly with zero-lambda and assertion invariants.
    /// </Summary>
    public class FriendsManager : IFriendsService
    {
        private readonly PlatformInterface _platformHandle;
        private readonly ConnectInterface _connectHandle;
        private readonly ICrossPlayFriendsStore _friendsStore;
        private ILobbyInviteService _inviteService;
        private readonly IFriendSignalChannel _signalChannel;
        private ILobby _lobby;
        private IPlatformFriendsProvider _platformProvider;

        private static bool _useLobbySystem = true;

        /// <summary>
        /// Controls whether the EOS Lobby system is enabled (true) or Direct P2P mode is used (false).
        /// When false, EOS Lobby calls are bypassed completely to optimize network flow.
        /// </summary>
        public static bool UseLobbySystem
        {
            get => _useLobbySystem;
            set
            {
                if (_useLobbySystem != value)
                {
                    _useLobbySystem = value;
                    _instance?.ReconfigureLobbyMode(value);
                }
            }
        }

        private ILobby ActiveLobby => UseLobbySystem ? (_lobby ?? LobbyManager.Instance) : null;

        private readonly List<FriendModel> _allFriends = new();
        private readonly List<FriendModel> _blockedPlayers = new();
        private readonly List<FriendModel> _incomingRequests = new();
        private readonly List<FriendModel> _outgoingRequests = new();
        private List<PlatformFriend> _cachedPlatformFriends = new();
        private readonly Dictionary<string, DateTime> _onlinePuids = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FriendPresenceState> _peerPresence = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _peerLobbyId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _peerHostAddress = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _peerJoinCode = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FriendModel> _incomingJoinRequests = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pendingOutgoingJoinRequests = new(StringComparer.OrdinalIgnoreCase);
        private string _pendingJoinLobbyCode;
        private float _lastHeartbeatTimer;
        private FriendPresenceState _lastLocalPresence = FriendPresenceState.Offline;

        private Action _pendingRefreshComplete;
        private Action<bool, string> _pendingAddFriendCallback;
        private Action<PlayerLookupResult> _pendingLookupCallback;
        private string _pendingSearchIdentifier;

        private Action<bool, string> _pendingStoreMutationCallback;
        private FriendSignal _pendingSignalToSend;
        private string _pendingMutationSuccessMessage;

        public IReadOnlyList<FriendModel> AllFriends => _allFriends;
        public IReadOnlyList<FriendModel> BlockedPlayers => _blockedPlayers;
        public IReadOnlyList<FriendModel> IncomingRequests => _incomingRequests;
        public IReadOnlyList<FriendModel> OutgoingRequests => _outgoingRequests;
        public bool IsRefreshing { get; private set; }

        public ICrossPlayFriendsStore CrossPlayStore => _friendsStore;
        public ILobbyInviteService LobbyInvites => _inviteService;
        public IFriendSignalChannel FriendSignals => _signalChannel;

        public event Action OnFriendsListUpdated;
        public event Action<FriendModel> OnFriendStatusChanged;

        private static FriendsManager _instance;
        public static FriendsManager Instance => _instance ??= CreateDefaultInstance();

        private static FriendsManager CreateDefaultInstance()
        {
            var platform = EpicTransport.EOSSDKComponent.PlatformInterface;
            var store = new CrossPlayFriendsStore(PlayerDataStorageManager.Instance);
            var signals = new EOSFriendSignalChannel();

            ILobby lobby = null;
            ILobbyInviteService invites;

            if (UseLobbySystem)
            {
                lobby = LobbyManager.Instance;
                invites = new LobbyInviteManager(lobby, store);
            }
            else
            {
                invites = new DirectP2PInviteManager(store);
            }

            var manager = new FriendsManager(platform, store, invites, signals, lobby);
            return manager;
        }

        public FriendsManager(
            PlatformInterface platformInterface,
            ICrossPlayFriendsStore friendsStore,
            ILobbyInviteService inviteService,
            IFriendSignalChannel signalChannel = null,
            ILobby lobby = null)
        {
            _platformHandle = platformInterface;
            _friendsStore = friendsStore;
            _inviteService = inviteService;
            _signalChannel = signalChannel;
            _lobby = lobby;

            Assert.IsNotNull(_platformHandle, "[FriendsManager] PlatformInterface must not be null.");
            Assert.IsNotNull(_friendsStore, "[FriendsManager] ICrossPlayFriendsStore must not be null.");
            Assert.IsNotNull(_inviteService, "[FriendsManager] ILobbyInviteService must not be null.");

            _connectHandle = _platformHandle.GetConnectInterface();

            if (_signalChannel != null)
            {
                _signalChannel.OnSignalReceived += OnSignalReceived;
                _signalChannel.OnSignalAcknowledged += OnSignalAcknowledged;
            }

            ConnectManager.OnLoginSuccess += HandleConnectLoginSuccess;
            AuthManager.OnLoggedOut += HandleLoggedOut;

            EOSSDKManager.Instance.Service.OnTick -= HandleTick;
            EOSSDKManager.Instance.Service.OnTick += HandleTick;
            EOSSDKManager.Instance.Service.OnBeforeShutdown -= HandlePlatformShutdown;
            EOSSDKManager.Instance.Service.OnBeforeShutdown += HandlePlatformShutdown;

            if (UseLobbySystem)
            {
                LobbyManager.ExternalDisplayNameResolver = ResolveCrossPlayDisplayName;
            }
        }

        public void ReconfigureLobbyMode(bool useLobby)
        {
            _inviteService?.Dispose();

            if (useLobby)
            {
                _lobby = LobbyManager.Instance;
                _inviteService = new LobbyInviteManager(_lobby, _friendsStore);
                LobbyManager.ExternalDisplayNameResolver = ResolveCrossPlayDisplayName;
                if (_isInitialized)
                {
                    SubscribeLobbyEvents(_lobby);
                }
            }
            else
            {
                UnsubscribeLobbyEvents(_lobby);
                _lobby = null;
                _inviteService = new DirectP2PInviteManager(_friendsStore);
            }

            BuildUnifiedFriendsList();
            OnFriendsListUpdated?.Invoke();
        }

        private LoginProvider GetCurrentPlatform() => LocalPlayerIdentity.CurrentPlatform;

        private bool _isInitialized;

        public void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            ConfigurePlatformProvider();
            _platformProvider?.Initialize();
            _signalChannel?.Start();

            if (UseLobbySystem)
            {
                SubscribeLobbyEvents(ActiveLobby);
            }
        }

        private void SubscribeLobbyEvents(ILobby lobby)
        {
            if (lobby == null) return;
            lobby.OnLobbyJoined += HandleLobbyPresenceEvent;
            lobby.OnLobbyLeft += HandleLobbyLeftEvent;
            lobby.OnPlayerJoined += HandleLobbyPlayerEvent;
            lobby.OnPlayerLeft += HandleLobbyPlayerEvent;
            lobby.OnGameStarted += HandleLobbyPresenceEvent;
            lobby.OnLobbyUpdated += HandleLobbyUpdatedEvent;
        }

        private void UnsubscribeLobbyEvents(ILobby lobby)
        {
            if (lobby == null) return;
            lobby.OnLobbyJoined -= HandleLobbyPresenceEvent;
            lobby.OnLobbyLeft -= HandleLobbyLeftEvent;
            lobby.OnPlayerJoined -= HandleLobbyPlayerEvent;
            lobby.OnPlayerLeft -= HandleLobbyPlayerEvent;
            lobby.OnGameStarted -= HandleLobbyPresenceEvent;
            lobby.OnLobbyUpdated -= HandleLobbyUpdatedEvent;
        }

        private void HandleLobbyPresenceEvent(string hostAddress)
        {
            UpdateLobbyPresence();
        }

        private void HandleLobbyLeftEvent()
        {
            UpdateLobbyPresence();
        }

        private void HandleLobbyPlayerEvent(ProductUserId _)
        {
            UpdateLobbyPresence();
        }

        private void HandleLobbyUpdatedEvent()
        {
            UpdateLobbyPresence();
        }

        // AI Implementation: Updates in-lobby presence in-place without discarding existing friend models
        private void UpdateLobbyPresence()
        {
            if (!UseLobbySystem) return;
            ILobby activeLobby = ActiveLobby;
            bool inLobby = activeLobby != null && activeLobby.IsInLobby;
            bool gameStarted = inLobby && activeLobby.HasGameStarted;

            var lobbyMemberPuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (inLobby)
            {
                List<ProductUserId> members = activeLobby.GetCurrentMemberUserIds();
                if (members != null)
                {
                    for (int i = 0; i < members.Count; i++)
                    {
                        if (members[i] != null)
                        {
                            string puid = members[i].ToString();
                            lobbyMemberPuids.Add(puid);
                            _onlinePuids[puid] = DateTime.UtcNow;
                        }
                    }
                }
            }

            for (int i = 0; i < _allFriends.Count; i++)
            {
                FriendModel friend = _allFriends[i];
                string key = friend.ProductUserId;
                if (!string.IsNullOrEmpty(key) && lobbyMemberPuids.Contains(key))
                {
                    friend.Presence = gameStarted ? FriendPresenceState.InMatch : FriendPresenceState.InLobby;
                }
                else if (friend.Presence == FriendPresenceState.InLobby || friend.Presence == FriendPresenceState.InMatch)
                {
                    friend.Presence = FriendPresenceState.Online;
                }
            }

            _allFriends.Sort(SortFriendsPresenceFirst);
            OnFriendsListUpdated?.Invoke();
        }

        private void ConfigurePlatformProvider()
        {
            _platformProvider = GetCurrentPlatform() switch
            {
                LoginProvider.Steam => new SteamFriendsProvider(),
                LoginProvider.Epic => new EpicFriendsProvider(_platformHandle, AuthManager.Instance),
                _ => null,
            };

        }

        public void RefreshFriends(Action onComplete = null)
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            if (IsRefreshing) return;

            ConfigurePlatformProvider();
            _platformProvider?.Initialize();

            IsRefreshing = true;
            _pendingRefreshComplete = onComplete;

            if (_platformProvider.IsAvailable)
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
            ResolvePlatformFriendsPuids();
        }

        private void ResolvePlatformFriendsPuids()
        {
            ProductUserId localPuid = ConnectManager.Instance?.LocalProductUserId ?? EpicTransport.EOSSDKComponent.LocalUserProductId;
            if (_cachedPlatformFriends.Count == 0)
            {
                _friendsStore.Load(HandleCloudStoreLoaded);
                return;
            }

            var epicIds = new List<string>();
            for (int i = 0; i < _cachedPlatformFriends.Count; i++)
            {
                PlatformFriend pf = _cachedPlatformFriends[i];
                if (pf.Platform == LoginProvider.Epic && !string.IsNullOrEmpty(pf.PlatformUserId))
                {
                    epicIds.Add(pf.PlatformUserId);
                }
            }

            if (epicIds.Count > 0)
            {
                var queryOptions = new QueryExternalAccountMappingsOptions
                {
                    LocalUserId = localPuid,
                    AccountIdType = ExternalAccountType.Epic,
                    ExternalAccountIds = epicIds.ToArray()
                };
                _connectHandle.QueryExternalAccountMappings(queryOptions, this, HandleBatchEpicMappingsCallback);
                return;
            }

            ResolveSteamFriendsPuids();
        }

        private static void HandleBatchEpicMappingsCallback(QueryExternalAccountMappingsCallbackInfo data)
        {
            if (data.ClientData is not FriendsManager manager) return;

            ProductUserId localPuid = ConnectManager.Instance?.LocalProductUserId ?? EpicTransport.EOSSDKComponent.LocalUserProductId;
            if (data.ResultCode == Result.Success && localPuid != null)
            {
                for (int i = 0; i < manager._cachedPlatformFriends.Count; i++)
                {
                    PlatformFriend pf = manager._cachedPlatformFriends[i];
                    if (pf.Platform != LoginProvider.Epic || string.IsNullOrEmpty(pf.PlatformUserId)) continue;

                    var getOptions = new GetExternalAccountMappingsOptions
                    {
                        LocalUserId = localPuid,
                        AccountIdType = ExternalAccountType.Epic,
                        TargetExternalUserId = pf.PlatformUserId
                    };

                    ProductUserId mapped = manager._connectHandle.GetExternalAccountMapping(getOptions);
                    if (mapped != null && mapped.IsValid())
                    {
                        pf.ProductUserId = mapped.ToString();
                    }
                }
            }

            manager.ResolveSteamFriendsPuids();
        }

        private void ResolveSteamFriendsPuids()
        {
            ProductUserId localPuid = ConnectManager.Instance?.LocalProductUserId ?? EpicTransport.EOSSDKComponent.LocalUserProductId;
            if (_cachedPlatformFriends.Count == 0)
            {
                _friendsStore.Load(HandleCloudStoreLoaded);
                return;
            }

            var steamIds = new List<string>();
            for (int i = 0; i < _cachedPlatformFriends.Count; i++)
            {
                PlatformFriend pf = _cachedPlatformFriends[i];
                if (pf.Platform == LoginProvider.Steam && !string.IsNullOrEmpty(pf.PlatformUserId))
                {
                    steamIds.Add(pf.PlatformUserId);
                }
            }

            if (steamIds.Count > 0)
            {
                var queryOptions = new QueryExternalAccountMappingsOptions
                {
                    LocalUserId = localPuid,
                    AccountIdType = ExternalAccountType.Steam,
                    ExternalAccountIds = steamIds.ToArray()
                };
                _connectHandle.QueryExternalAccountMappings(queryOptions, this, HandleBatchSteamMappingsCallback);
                return;
            }

            _friendsStore.Load(HandleCloudStoreLoaded);
        }

        private static void HandleBatchSteamMappingsCallback(QueryExternalAccountMappingsCallbackInfo data)
        {
            if (data.ClientData is not FriendsManager manager) return;

            ProductUserId localPuid = ConnectManager.Instance?.LocalProductUserId ?? EpicTransport.EOSSDKComponent.LocalUserProductId;
            if (data.ResultCode == Result.Success && localPuid != null)
            {
                for (int i = 0; i < manager._cachedPlatformFriends.Count; i++)
                {
                    PlatformFriend pf = manager._cachedPlatformFriends[i];
                    if (pf.Platform != LoginProvider.Steam || string.IsNullOrEmpty(pf.PlatformUserId)) continue;

                    var getOptions = new GetExternalAccountMappingsOptions
                    {
                        LocalUserId = localPuid,
                        AccountIdType = ExternalAccountType.Steam,
                        TargetExternalUserId = pf.PlatformUserId
                    };

                    ProductUserId mapped = manager._connectHandle.GetExternalAccountMapping(getOptions);
                    if (mapped != null && mapped.IsValid())
                    {
                        pf.ProductUserId = mapped.ToString();
                    }
                }
            }

            manager._friendsStore.Load(manager.HandleCloudStoreLoaded);
        }

        private void HandleCloudStoreLoaded(bool success)
        {
            BuildUnifiedFriendsList();
            FlushOutbox();
            PingAllFriends();
            IsRefreshing = false;

            OnFriendsListUpdated?.Invoke();

            Action callback = _pendingRefreshComplete;
            _pendingRefreshComplete = null;
            callback?.Invoke();
        }

        private void FlushOutbox()
        {
            if (!_signalChannel.IsActive) return;

            var outbox = _friendsStore.Outbox;
            for (int i = 0; i < outbox.Count; i++)
            {
                FriendSignal sig = outbox[i];
                if (!string.IsNullOrEmpty(sig.targetPuid))
                {
                    _signalChannel.Send(sig.targetPuid, sig);
                }
            }
        }

        private string LocalProductUserIdString
        {
            get
            {
                return EpicTransport.EOSSDKComponent.LocalUserProductId.ToString();
            }
        }

        private FriendPresenceState GetLocalPresenceState()
        {
            if (Mirror.NetworkServer.active || (Mirror.NetworkClient.active && Mirror.NetworkClient.isConnected))
            {
                return FriendPresenceState.InMatch;
            }
            if (UseLobbySystem)
            {
                return _lobby.HasGameStarted ? FriendPresenceState.InMatch : FriendPresenceState.InLobby;
            }
            return FriendPresenceState.Online;
        }

        public void PingFriend(string targetPuid)
        {
            if (_friendsStore.IsBlocked(targetPuid)) return;

            var ping = new FriendSignal
            {
                id = Guid.NewGuid().ToString("N"),
                type = FriendSignalType.PresencePing,
                senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                senderPlatform = (int)GetCurrentPlatform(),
                targetPuid = targetPuid,
                createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                presenceState = (int)GetLocalPresenceState(),
                lobbyId = ActiveLobby?.CurrentLobbyId,
                hostAddress = ActiveLobby?.CurrentHostAddress ?? LocalProductUserIdString,
                joinCode = ActiveLobby?.CurrentJoinCode
            };
            _signalChannel.Send(targetPuid, ping);
        }

        public void PingAllFriends()
        {
            for (int i = 0; i < _allFriends.Count; i++)
            {
                FriendModel friend = _allFriends[i];
                if (!string.IsNullOrEmpty(friend.ProductUserId))
                {
                    PingFriend(friend.ProductUserId);
                }
            }
        }

        private void BuildUnifiedFriendsList()
        {
            _allFriends.Clear();
            _blockedPlayers.Clear();
            _incomingRequests.Clear();
            _outgoingRequests.Clear();

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

            ILobby activeLobby = ActiveLobby;
            var lobbyMemberPuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lobbyMemberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool inLobby = UseLobbySystem && activeLobby != null && activeLobby.IsInLobby;
            bool gameStarted = inLobby && activeLobby.HasGameStarted;

            if (inLobby)
            {
                List<ProductUserId> members = activeLobby.GetCurrentMemberUserIds();
                if (members != null)
                {
                    for (int m = 0; m < members.Count; m++)
                    {
                        if (members[m] != null)
                        {
                            string puidStr = members[m].ToString();
                            lobbyMemberPuids.Add(puidStr);
                            _onlinePuids[puidStr] = DateTime.UtcNow;
                            string name = activeLobby.GetMemberDisplayName(members[m]);
                            if (!string.IsNullOrWhiteSpace(name) && !name.Equals("Player", StringComparison.OrdinalIgnoreCase))
                            {
                                lobbyMemberNames.Add(name);
                            }
                        }
                    }
                }
            }

            var addedKeys = new HashSet<string>();

            // 2. Process Native Platform Friends
            var crossPlayEntries = _friendsStore.Friends;
            for (int i = 0; i < _cachedPlatformFriends.Count; i++)
            {
                PlatformFriend pf = _cachedPlatformFriends[i];
                if (string.IsNullOrEmpty(pf.PlatformUserId)) continue;
                if (_friendsStore.IsBlocked(pf.PlatformUserId)) continue;
                if (!string.IsNullOrEmpty(pf.ProductUserId) && _friendsStore.IsBlocked(pf.ProductUserId)) continue;

                string puid = pf.ProductUserId;
                for (int c = 0; c < crossPlayEntries.Count; c++)
                {
                    if (string.Equals(crossPlayEntries[c].displayName, pf.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(crossPlayEntries[c].productUserId))
                    {
                        puid = crossPlayEntries[c].productUserId;
                        pf.ProductUserId = puid;
                        break;
                    }
                }

                FriendPresenceState presence = pf.Presence;

                if (!string.IsNullOrEmpty(puid) && _peerPresence.TryGetValue(puid, out FriendPresenceState peerState) && _onlinePuids.TryGetValue(puid, out DateTime puidSeen) && (DateTime.UtcNow - puidSeen).TotalSeconds < 120)
                {
                    presence = peerState;
                }
                else if (inLobby && (
                    (!string.IsNullOrEmpty(pf.PlatformUserId) && lobbyMemberPuids.Contains(pf.PlatformUserId)) ||
                    (!string.IsNullOrEmpty(puid) && lobbyMemberPuids.Contains(puid)) ||
                    (!string.IsNullOrEmpty(pf.DisplayName) && lobbyMemberNames.Contains(pf.DisplayName))))
                {
                    presence = gameStarted ? FriendPresenceState.InMatch : FriendPresenceState.InLobby;
                }
                else if (!string.IsNullOrEmpty(puid) && _onlinePuids.TryGetValue(puid, out DateTime lastSeen) && (DateTime.UtcNow - lastSeen).TotalSeconds < 120)
                {
                    presence = FriendPresenceState.Online;
                }
                else if (pf.Presence != FriendPresenceState.Offline)
                {
                    presence = pf.Presence;
                }
                else
                {
                    presence = FriendPresenceState.Offline;
                }

                _allFriends.Add(new FriendModel
                {
                    ProductUserId = pf.ProductUserId,
                    PlatformUserId = pf.PlatformUserId,
                    DisplayName = pf.DisplayName,
                    Platform = pf.Platform,
                    Status = FriendRelationStatus.Platform,
                    Presence = presence,
                    LobbyId = !string.IsNullOrEmpty(puid) && _peerLobbyId.TryGetValue(puid, out string pLobby) ? pLobby : null,
                    HostAddress = !string.IsNullOrEmpty(puid) && _peerHostAddress.TryGetValue(puid, out string pHost) ? pHost : null,
                    AvatarTexture = pf.AvatarTexture
                });

                addedKeys.Add(pf.PlatformUserId);
                if (!string.IsNullOrEmpty(pf.ProductUserId))
                {
                    addedKeys.Add(pf.ProductUserId);
                }
                if (!string.IsNullOrWhiteSpace(pf.DisplayName))
                {
                    addedKeys.Add(pf.DisplayName.ToLowerInvariant());
                }
            }

            // 3. Process Cloud Cross-Play Friends
            for (int i = 0; i < crossPlayEntries.Count; i++)
            {
                CrossPlayFriendEntry cf = crossPlayEntries[i];
                if (string.IsNullOrEmpty(cf.productUserId)) continue;
                if (_friendsStore.IsBlocked(cf.productUserId)) continue;

                bool alreadyAdded = addedKeys.Contains(cf.productUserId);
                if (!alreadyAdded && !string.IsNullOrWhiteSpace(cf.displayName) && addedKeys.Contains(cf.displayName.ToLowerInvariant()))
                {
                    alreadyAdded = true;
                    for (int a = 0; a < _allFriends.Count; a++)
                    {
                        if (string.Equals(_allFriends[a].DisplayName, cf.displayName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.IsNullOrEmpty(_allFriends[a].ProductUserId))
                            {
                                _allFriends[a].ProductUserId = cf.productUserId;
                            }
                            break;
                        }
                    }
                }
                if (alreadyAdded) continue;

                FriendPresenceState presence = FriendPresenceState.Offline;
                string cpPuid = cf.productUserId;

                if (!string.IsNullOrEmpty(cpPuid) && _peerPresence.TryGetValue(cpPuid, out FriendPresenceState peerState) && _onlinePuids.TryGetValue(cpPuid, out DateTime puidSeen) && (DateTime.UtcNow - puidSeen).TotalSeconds < 120)
                {
                    presence = peerState;
                }
                else if (inLobby && (
                    (!string.IsNullOrEmpty(cpPuid) && lobbyMemberPuids.Contains(cpPuid)) ||
                    (!string.IsNullOrEmpty(cf.displayName) && lobbyMemberNames.Contains(cf.displayName))))
                {
                    presence = gameStarted ? FriendPresenceState.InMatch : FriendPresenceState.InLobby;
                }
                else if (!string.IsNullOrEmpty(cpPuid) && _onlinePuids.TryGetValue(cpPuid, out DateTime lastSeen) && (DateTime.UtcNow - lastSeen).TotalSeconds < 120)
                {
                    presence = FriendPresenceState.Online;
                }
                else
                {
                    for (int p = 0; p < _cachedPlatformFriends.Count; p++)
                    {
                        if (string.Equals(_cachedPlatformFriends[p].DisplayName, cf.displayName, StringComparison.OrdinalIgnoreCase))
                        {
                            presence = _cachedPlatformFriends[p].Presence;
                            break;
                        }
                    }
                }

                _allFriends.Add(new FriendModel
                {
                    ProductUserId = cf.productUserId,
                    DisplayName = cf.displayName,
                    Platform = (LoginProvider)cf.platform,
                    Status = FriendRelationStatus.CrossPlay,
                    Presence = presence,
                    LobbyId = !string.IsNullOrEmpty(cpPuid) && _peerLobbyId.TryGetValue(cpPuid, out string cpLobby) ? cpLobby : null,
                    HostAddress = !string.IsNullOrEmpty(cpPuid) && _peerHostAddress.TryGetValue(cpPuid, out string cpHost) ? cpHost : null,
                    AvatarTexture = null
                });

                addedKeys.Add(cf.productUserId);
                if (!string.IsNullOrWhiteSpace(cf.displayName))
                {
                    addedKeys.Add(cf.displayName.ToLowerInvariant());
                }
            }

            // 4. Sort All Friends: In-match / In-lobby / Online presence on top, then alphabetical
            _allFriends.Sort(SortFriendsPresenceFirst);

            // 5. Process Incoming Join Requests and Friend Requests
            foreach (FriendModel joinReq in _incomingJoinRequests.Values)
            {
                _incomingRequests.Add(joinReq);
            }

            var incoming = _friendsStore.PendingIncoming;
            for (int i = 0; i < incoming.Count; i++)
            {
                CrossPlayFriendEntry req = incoming[i];
                _incomingRequests.Add(new FriendModel
                {
                    ProductUserId = req.productUserId,
                    DisplayName = req.displayName,
                    Platform = (LoginProvider)req.platform,
                    Status = FriendRelationStatus.PendingIncoming,
                    Presence = FriendPresenceState.Online,
                    AvatarTexture = null
                });
            }

            // 6. Process Outgoing Requests
            var outgoing = _friendsStore.PendingOutgoing;
            for (int i = 0; i < outgoing.Count; i++)
            {
                CrossPlayFriendEntry req = outgoing[i];
                _outgoingRequests.Add(new FriendModel
                {
                    ProductUserId = req.productUserId,
                    DisplayName = req.displayName,
                    Platform = (LoginProvider)req.platform,
                    Status = FriendRelationStatus.PendingOutgoing,
                    Presence = FriendPresenceState.Offline,
                    AvatarTexture = null
                });
            }
        }

        // AI Implementation, sorting friends list by presence
        private static int SortFriendsPresenceFirst(FriendModel a, FriendModel b)
        {
            int scoreA = GetPresenceSortPriority(a.Presence);
            int scoreB = GetPresenceSortPriority(b.Presence);

            if (scoreA != scoreB)
            {
                return scoreA.CompareTo(scoreB);
            }
            return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetPresenceSortPriority(FriendPresenceState presence)
        {
            return presence switch
            {
                FriendPresenceState.InMatch => 0,
                FriendPresenceState.InLobby => 1,
                FriendPresenceState.Online => 2,
                _ => 3
            };
        }

        public FriendRelationStatus GetRelationship(string identifier, string displayName)
        {
            if (string.IsNullOrEmpty(identifier) && string.IsNullOrEmpty(displayName)) return FriendRelationStatus.None;
            if (!string.IsNullOrEmpty(identifier))
            {
                if (_friendsStore.IsBlocked(identifier)) return FriendRelationStatus.Blocked;
                if (_friendsStore.IsFriend(identifier)) return FriendRelationStatus.CrossPlay;
                if (_friendsStore.IsPendingIncoming(identifier)) return FriendRelationStatus.PendingIncoming;
                if (_friendsStore.IsPendingOutgoing(identifier)) return FriendRelationStatus.PendingOutgoing;
            }

            for (int i = 0; i < _allFriends.Count; i++)
            {
                FriendModel friend = _allFriends[i];
                if (!string.IsNullOrEmpty(identifier) && (
                    string.Equals(friend.ProductUserId, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(friend.PlatformUserId, identifier, StringComparison.OrdinalIgnoreCase)))
                {
                    return friend.Status;
                }
                if (!string.IsNullOrEmpty(displayName) && string.Equals(friend.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    return friend.Status;
                }
            }

            for (int i = 0; i < _cachedPlatformFriends.Count; i++)
            {
                PlatformFriend pf = _cachedPlatformFriends[i];
                if (!string.IsNullOrEmpty(identifier) && (
                    string.Equals(pf.PlatformUserId, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pf.ProductUserId, identifier, StringComparison.OrdinalIgnoreCase)))
                {
                    return FriendRelationStatus.Platform;
                }
                if (!string.IsNullOrEmpty(displayName) && string.Equals(pf.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    return FriendRelationStatus.Platform;
                }
            }

            return FriendRelationStatus.None;
        }

        public FriendRelationStatus GetRelationship(string identifier)
        {
            return GetRelationship(identifier, null);
        }

        public void ResolvePlayer(string identifier, Action<PlayerLookupResult> onComplete)
        {
            Assert.IsNotNull(onComplete, "[FriendsManager] onComplete callback must not be null.");

            if (string.IsNullOrWhiteSpace(identifier))
            {
                onComplete(PlayerLookupResult.Failure(identifier, "Search query cannot be empty."));
                return;
            }

            string trimmed = identifier.Trim();

            // Check if user is searching for themselves
            string localPuid = EpicTransport.EOSSDKComponent.LocalUserProductIdString;
            if (string.Equals(trimmed, localPuid, StringComparison.OrdinalIgnoreCase))
            {
                onComplete(PlayerLookupResult.Failure(identifier, "You cannot search or add yourself."));
                return;
            }

            // Case 1: Direct 32-character hexadecimal ProductUserId
            if (trimmed.Length == 32)
            {
                ProductUserId puid = ProductUserId.FromString(trimmed);
                if (puid.IsValid())
                {
                    if (ConnectManager.Instance?.LocalProductUserId != null)
                    {
                        _pendingSearchIdentifier = trimmed;
                        _pendingLookupCallback = onComplete;

                        var queryOptions = new QueryProductUserIdMappingsOptions
                        {
                            LocalUserId = ConnectManager.Instance.LocalProductUserId,
                            ProductUserIds = new[] { puid }
                        };

                        _connectHandle.QueryProductUserIdMappings(queryOptions, this, HandleQueryProductUserIdMappingsCallback);
                        return;
                    }

                    FriendRelationStatus status = GetRelationship(trimmed);
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
            if (ConnectManager.Instance?.LocalProductUserId != null)
            {
                _pendingSearchIdentifier = trimmed;
                _pendingLookupCallback = onComplete;

                var queryOptions = new QueryExternalAccountMappingsOptions
                {
                    LocalUserId = ConnectManager.Instance.LocalProductUserId,
                    AccountIdType = ExternalAccountType.Steam,
                    ExternalAccountIds = new[] { trimmed }
                };

                _connectHandle.QueryExternalAccountMappings(queryOptions, this, HandleLookupExternalAccountMappingsCallback);
                return;
            }

            onComplete(PlayerLookupResult.Failure(trimmed, "Player not found. Provide a valid 32-character Product User ID."));
        }

        private static void HandleLookupExternalAccountMappingsCallback(QueryExternalAccountMappingsCallbackInfo data)
        {
            if (data.ClientData is not FriendsManager manager) return;

            Action<PlayerLookupResult> callback = manager._pendingLookupCallback;
            string identifier = manager._pendingSearchIdentifier;
            manager._pendingLookupCallback = null;
            manager._pendingSearchIdentifier = null;

            if (data.ResultCode != Result.Success)
            {
                callback?.Invoke(PlayerLookupResult.Failure(identifier, "Player not found on the network."));
                return;
            }

            var getOptions = new GetExternalAccountMappingsOptions
            {
                LocalUserId = ConnectManager.Instance.LocalProductUserId,
                AccountIdType = ExternalAccountType.Steam,
                TargetExternalUserId = identifier
            };

            ProductUserId mappedPuid = manager._connectHandle.GetExternalAccountMapping(getOptions);
            if (mappedPuid.IsValid())
            {
                string puidStr = mappedPuid.ToString();
                FriendRelationStatus status = manager.GetRelationship(puidStr);
                string resolvedName = identifier;

                var copyOptions = new CopyProductUserInfoOptions { TargetUserId = mappedPuid };
                if (manager._connectHandle.CopyProductUserInfo(copyOptions, out ExternalAccountInfo userInfo) == Result.Success && !string.IsNullOrEmpty(userInfo.DisplayName))
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
            if (data.ClientData is not FriendsManager manager) return;

            Action<PlayerLookupResult> callback = manager._pendingLookupCallback;
            string identifier = manager._pendingSearchIdentifier;
            manager._pendingLookupCallback = null;
            manager._pendingSearchIdentifier = null;

            ProductUserId targetPuid = ProductUserId.FromString(identifier);
            string displayName = null;
            LoginProvider platform = LoginProvider.None;

            if (manager._connectHandle != null)
            {
                var copyOptions = new CopyProductUserInfoOptions { TargetUserId = targetPuid };
                if (manager._connectHandle.CopyProductUserInfo(copyOptions, out ExternalAccountInfo userInfo) == Result.Success)
                {
                    displayName = userInfo.DisplayName;
                    platform = MapExternalAccountType(userInfo.AccountIdType);
                }

                if (string.IsNullOrEmpty(displayName))
                {
                    var countOptions = new GetProductUserExternalAccountCountOptions { TargetUserId = targetPuid };
                    uint count = manager._connectHandle.GetProductUserExternalAccountCount(countOptions);
                    for (uint i = 0; i < count; i++)
                    {
                        var copyByIndex = new CopyProductUserExternalAccountByIndexOptions
                        {
                            TargetUserId = targetPuid,
                            ExternalAccountInfoIndex = i
                        };
                        if (manager._connectHandle.CopyProductUserExternalAccountByIndex(copyByIndex, out ExternalAccountInfo extInfo) == Result.Success)
                        {
                            if (!string.IsNullOrEmpty(extInfo.DisplayName))
                            {
                                displayName = extInfo.DisplayName;
                                platform = MapExternalAccountType(extInfo.AccountIdType);
                                break;
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(displayName))
            {
                displayName = "Player";
            }

            FriendRelationStatus status = manager.GetRelationship(identifier);
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

        private static LoginProvider MapExternalAccountType(ExternalAccountType type)
        {
            return type switch
            {
                ExternalAccountType.Epic => LoginProvider.Epic,
                ExternalAccountType.Steam => LoginProvider.Steam,
                _ => LoginProvider.Guest
            };
        }

        public void AddFriend(string searchIdentifier, Action<bool, string> onComplete = null)
        {
            _pendingAddFriendCallback = onComplete;
            ResolvePlayer(searchIdentifier, HandleAddFriendResolved);
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

            bool added = _friendsStore.AddPendingOutgoing(productUserId, displayName, platform);
            if (!added)
            {
                onComplete?.Invoke(false, "Could not queue friend request.");
                return;
            }

            var signal = new FriendSignal
            {
                id = Guid.NewGuid().ToString("N"),
                type = FriendSignalType.FriendRequest,
                senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                senderPlatform = (int)AuthPreferences.LastLoginProvider,
                targetPuid = productUserId,
                createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

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

            var signal = new FriendSignal
            {
                id = Guid.NewGuid().ToString("N"),
                type = FriendSignalType.RequestAccepted,
                senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                senderPlatform = (int)AuthPreferences.LastLoginProvider,
                targetPuid = productUserId,
                createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            _friendsStore.EnqueueSignal(signal);
            ExecuteStoreMutation(signal, "Friend request accepted!", onComplete);
        }

        public void DeclineFriendRequest(string productUserId, Action<bool, string> onComplete = null)
        {
            _friendsStore.RemovePendingIncoming(productUserId);

            var signal = new FriendSignal
            {
                id = Guid.NewGuid().ToString("N"),
                type = FriendSignalType.RequestDeclined,
                senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                senderPlatform = (int)AuthPreferences.LastLoginProvider,
                targetPuid = productUserId,
                createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            _friendsStore.EnqueueSignal(signal);
            ExecuteStoreMutation(signal, "Friend request declined.", onComplete);
        }

        public void RemoveFriend(string productUserId, Action<bool, string> onComplete = null)
        {
            bool removed = _friendsStore.RemoveFriend(productUserId);
            if (!removed)
            {
                onComplete?.Invoke(false, "Player was not in cross-play friends list.");
                return;
            }

            // BIDIRECTIONAL UNFRIENDING SYNC: Signal the other player to remove us as well
            var signal = new FriendSignal
            {
                id = Guid.NewGuid().ToString("N"),
                type = FriendSignalType.Unfriended,
                senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                senderPlatform = (int)AuthPreferences.LastLoginProvider,
                targetPuid = productUserId,
                createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            _friendsStore.EnqueueSignal(signal);
            ExecuteStoreMutation(signal, "Friend removed.", onComplete);
        }

        public void BlockPlayer(string productUserId, string displayName, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(string.IsNullOrEmpty(productUserId), "[FriendsManager] productUserId must not be null or empty.");

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
            Assert.IsFalse(string.IsNullOrEmpty(productUserId), "[FriendsManager] productUserId must not be null or empty.");

            bool unblocked = _friendsStore.UnblockPlayer(productUserId);
            if (!unblocked)
            {
                onComplete?.Invoke(false, "Player was not blocked.");
                return;
            }

            PingFriend(productUserId);
            ExecuteStoreMutation(null, "Player unblocked.", onComplete);
        }

        private FriendModel _pendingInviteFriend;
        private Action<bool, string> _pendingInviteCallback;

        public void InviteFriend(FriendModel friend, Action<bool, string> onComplete = null)
        {
            if (friend == null)
            {
                onComplete?.Invoke(false, "Friend parameter cannot be null.");
                return;
            }

            // 1. Direct ProductUserId check
            ProductUserId puid = !string.IsNullOrEmpty(friend.ProductUserId) ? ProductUserId.FromString(friend.ProductUserId) : null;
            if (puid.IsValid())
            {
                InviteFriendToLobby(puid, onComplete);
                return;
            }

            // 2. Check if cached platform friend has resolved PUID
            for (int i = 0; i < _cachedPlatformFriends.Count; i++)
            {
                PlatformFriend pf = _cachedPlatformFriends[i];
                if ((string.Equals(pf.PlatformUserId, friend.PlatformUserId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(pf.DisplayName, friend.DisplayName, StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrEmpty(pf.ProductUserId))
                {
                    puid = ProductUserId.FromString(pf.ProductUserId);
                    if (puid != null && puid.IsValid())
                    {
                        friend.ProductUserId = pf.ProductUserId;
                        InviteFriendToLobby(puid, onComplete);
                        return;
                    }
                }
            }

            // 3. Check if matching DisplayName in _allFriends has a PUID
            for (int i = 0; i < _allFriends.Count; i++)
            {
                FriendModel f = _allFriends[i];
                if (string.Equals(f.DisplayName, friend.DisplayName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(f.ProductUserId))
                {
                    puid = ProductUserId.FromString(f.ProductUserId);
                    if (puid != null && puid.IsValid())
                    {
                        friend.ProductUserId = f.ProductUserId;
                        InviteFriendToLobby(puid, onComplete);
                        return;
                    }
                }
            }

            // 4. Query external account mappings via EOS Connect
            string platformUserId = friend.PlatformUserId;
            for (int i = 0; i < _cachedPlatformFriends.Count; i++)
            {
                if (string.Equals(_cachedPlatformFriends[i].DisplayName, friend.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    platformUserId = _cachedPlatformFriends[i].PlatformUserId;
                    friend.PlatformUserId = platformUserId;
                    friend.Platform = _cachedPlatformFriends[i].Platform;
                    break;
                }
            }

            ProductUserId localPuid = ConnectManager.Instance?.LocalProductUserId ?? EpicTransport.EOSSDKComponent.LocalUserProductId;
            if (!string.IsNullOrEmpty(platformUserId))
            {
                ResolveAndInvitePlatformFriend(friend, onComplete);
                return;
            }

            onComplete?.Invoke(false, "This friend does not have an active cross-play ID to invite.");
        }

        private void ResolveAndInvitePlatformFriend(FriendModel friend, Action<bool, string> onComplete)
        {
            _pendingInviteFriend = friend;
            _pendingInviteCallback = onComplete;

            ExternalAccountType accountType = friend.Platform == LoginProvider.Epic
                ? ExternalAccountType.Epic
                : ExternalAccountType.Steam;

            ProductUserId localPuid = ConnectManager.Instance?.LocalProductUserId ?? EpicTransport.EOSSDKComponent.LocalUserProductId;

            var queryOptions = new QueryExternalAccountMappingsOptions
            {
                LocalUserId = localPuid,
                AccountIdType = accountType,
                ExternalAccountIds = new[] { friend.PlatformUserId }
            };

            _connectHandle.QueryExternalAccountMappings(queryOptions, this, HandleResolveAndInviteCallback);
        }

        private static void HandleResolveAndInviteCallback(QueryExternalAccountMappingsCallbackInfo data)
        {
            if (data.ClientData is not FriendsManager manager) return;

            FriendModel friend = manager._pendingInviteFriend;
            Action<bool, string> callback = manager._pendingInviteCallback;
            manager._pendingInviteFriend = null;
            manager._pendingInviteCallback = null;

            if (friend == null)
            {
                callback?.Invoke(false, "Invite target was lost.");
                return;
            }

            ProductUserId localPuid = ConnectManager.Instance?.LocalProductUserId ?? EpicTransport.EOSSDKComponent.LocalUserProductId;
            ExternalAccountType accountType = friend.Platform == LoginProvider.Epic
                ? ExternalAccountType.Epic
                : ExternalAccountType.Steam;

            if (data.ResultCode == Result.Success && localPuid != null)
            {
                var getOptions = new GetExternalAccountMappingsOptions
                {
                    LocalUserId = localPuid,
                    AccountIdType = accountType,
                    TargetExternalUserId = friend.PlatformUserId
                };

                ProductUserId mapped = manager._connectHandle.GetExternalAccountMapping(getOptions);
                if (mapped != null && mapped.IsValid())
                {
                    friend.ProductUserId = mapped.ToString();
                    manager.InviteFriendToLobby(mapped, callback);
                    return;
                }
            }

            callback?.Invoke(false, "This friend has not logged into the game yet.");
        }

        public void InviteFriendToLobby(ProductUserId targetUserId, Action<bool, string> onComplete = null)
        {
            if (_friendsStore.IsBlocked(targetUserId.ToString()))
            {
                onComplete?.Invoke(false, "Cannot invite a blocked player.");
                return;
            }
            _inviteService.InviteFriendToLobby(targetUserId, onComplete);
        }

        public void SendDirectGameInvite(string targetPuid, Action<bool, string> onComplete = null)
        {
            if (string.IsNullOrEmpty(targetPuid))
            {
                onComplete?.Invoke(false, "Target player ID is invalid.");
                return;
            }

            if (_friendsStore != null && _friendsStore.IsBlocked(targetPuid))
            {
                onComplete?.Invoke(false, "Cannot invite a blocked player.");
                return;
            }

            // Automatically start the Mirror game host if not already active
            if (EOSNetworkManager.Instance != null && !Mirror.NetworkServer.active)
            {
                EOSNetworkManager.Instance.StartEosHost();
            }

            string myPuid = LocalProductUserIdString;
            if (string.IsNullOrEmpty(myPuid))
            {
                onComplete?.Invoke(false, "Local user ProductUserId is unavailable.");
                return;
            }

            var signal = new FriendSignal
            {
                id = Guid.NewGuid().ToString("N"),
                type = FriendSignalType.GameInvite,
                senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                senderPlatform = (int)GetCurrentPlatform(),
                targetPuid = targetPuid,
                createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                presenceState = (int)FriendPresenceState.InMatch,
                lobbyId = null,
                hostAddress = myPuid,
                joinCode = null
            };

            _signalChannel?.Send(targetPuid, signal);
            NotificationManager.Show("Game invite sent!", NotificationType.Normal);
            onComplete?.Invoke(true, "Game invite sent!");
        }

        public void SendDirectInviteAccepted(string targetPuid)
        {
            if (string.IsNullOrEmpty(targetPuid)) return;
            var signal = new FriendSignal
            {
                id = Guid.NewGuid().ToString("N"),
                type = FriendSignalType.GameInviteAccepted,
                senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                senderPlatform = (int)GetCurrentPlatform(),
                targetPuid = targetPuid,
                createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            _signalChannel?.Send(targetPuid, signal);
        }

        public void SendDirectInviteDeclined(string targetPuid)
        {
            if (string.IsNullOrEmpty(targetPuid)) return;
            var signal = new FriendSignal
            {
                id = Guid.NewGuid().ToString("N"),
                type = FriendSignalType.GameInviteDeclined,
                senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                senderPlatform = (int)GetCurrentPlatform(),
                targetPuid = targetPuid,
                createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            _signalChannel?.Send(targetPuid, signal);
        }

        public bool IsPendingJoinRequestTarget(string puid) => !string.IsNullOrEmpty(puid) && _pendingOutgoingJoinRequests.Contains(puid);
        public void ClearPendingJoinRequest(string puid) { if (!string.IsNullOrEmpty(puid)) _pendingOutgoingJoinRequests.Remove(puid); }

        private FriendModel _pendingJoinFriend;
        private Action<bool, string> _pendingJoinCallback;

        public void SendJoinRequest(FriendModel friend, Action<bool, string> onComplete = null)
        {
            if (friend == null)
            {
                onComplete?.Invoke(false, "Friend cannot be null.");
                return;
            }

            string targetPuid = friend.ProductUserId;
            if (string.IsNullOrEmpty(targetPuid) && !string.IsNullOrEmpty(friend.DisplayName))
            {
                for (int i = 0; i < _allFriends.Count; i++)
                {
                    if (string.Equals(_allFriends[i].DisplayName, friend.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(_allFriends[i].ProductUserId))
                    {
                        targetPuid = _allFriends[i].ProductUserId;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(targetPuid))
            {
                for (int i = 0; i < _cachedPlatformFriends.Count; i++)
                {
                    if (string.Equals(_cachedPlatformFriends[i].DisplayName, friend.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(_cachedPlatformFriends[i].ProductUserId))
                    {
                        targetPuid = _cachedPlatformFriends[i].ProductUserId;
                        friend.ProductUserId = targetPuid;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(targetPuid))
            {
                string platformUserId = friend.PlatformUserId;
                if (string.IsNullOrEmpty(platformUserId))
                {
                    for (int i = 0; i < _cachedPlatformFriends.Count; i++)
                    {
                        if (string.Equals(_cachedPlatformFriends[i].DisplayName, friend.DisplayName, StringComparison.OrdinalIgnoreCase))
                        {
                            platformUserId = _cachedPlatformFriends[i].PlatformUserId;
                            friend.PlatformUserId = platformUserId;
                            friend.Platform = _cachedPlatformFriends[i].Platform;
                            break;
                        }
                    }
                }

                ProductUserId localPuid = ConnectManager.Instance?.LocalProductUserId ?? EpicTransport.EOSSDKComponent.LocalUserProductId;
                if (!string.IsNullOrEmpty(platformUserId) && _connectHandle != null && localPuid != null)
                {
                    ResolveAndSendJoinRequest(friend, onComplete);
                    return;
                }

                onComplete?.Invoke(false, "Cannot send join request: Friend's cross-play ID is not available.");
                return;
            }

            _pendingOutgoingJoinRequests.Add(targetPuid);

            var signal = new FriendSignal
            {
                id = Guid.NewGuid().ToString("N"),
                type = FriendSignalType.JoinRequest,
                senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                senderPlatform = (int)GetCurrentPlatform(),
                targetPuid = targetPuid,
                createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            _signalChannel?.Send(targetPuid, signal);
            onComplete?.Invoke(true, "Join request sent!");
        }

        private void ResolveAndSendJoinRequest(FriendModel friend, Action<bool, string> onComplete)
        {
            _pendingJoinFriend = friend;
            _pendingJoinCallback = onComplete;

            ExternalAccountType accountType = friend.Platform == LoginProvider.Epic
                ? ExternalAccountType.Epic
                : ExternalAccountType.Steam;

            ProductUserId localPuid = ConnectManager.Instance?.LocalProductUserId ?? EpicTransport.EOSSDKComponent.LocalUserProductId;

            var queryOptions = new QueryExternalAccountMappingsOptions
            {
                LocalUserId = localPuid,
                AccountIdType = accountType,
                ExternalAccountIds = new[] { friend.PlatformUserId }
            };

            _connectHandle.QueryExternalAccountMappings(queryOptions, this, HandleResolveAndJoinCallback);
        }

        private static void HandleResolveAndJoinCallback(QueryExternalAccountMappingsCallbackInfo data)
        {
            if (data.ClientData is not FriendsManager manager) return;

            FriendModel friend = manager._pendingJoinFriend;
            Action<bool, string> callback = manager._pendingJoinCallback;
            manager._pendingJoinFriend = null;
            manager._pendingJoinCallback = null;

            if (friend == null)
            {
                callback?.Invoke(false, "Join target was lost.");
                return;
            }

            ProductUserId localPuid = ConnectManager.Instance?.LocalProductUserId ?? EpicTransport.EOSSDKComponent.LocalUserProductId;
            ExternalAccountType accountType = friend.Platform == LoginProvider.Epic
                ? ExternalAccountType.Epic
                : ExternalAccountType.Steam;

            if (data.ResultCode == Result.Success && localPuid != null)
            {
                var getOptions = new GetExternalAccountMappingsOptions
                {
                    LocalUserId = localPuid,
                    AccountIdType = accountType,
                    TargetExternalUserId = friend.PlatformUserId
                };

                ProductUserId mapped = manager._connectHandle.GetExternalAccountMapping(getOptions);
                if (mapped != null && mapped.IsValid())
                {
                    friend.ProductUserId = mapped.ToString();
                    manager.SendJoinRequest(friend, callback);
                    return;
                }
            }

            callback?.Invoke(false, "Friend's cross-play ID could not be resolved.");
        }

        public void AcceptJoinRequest(string productUserId, Action<bool, string> onComplete = null)
        {
            _incomingJoinRequests.Remove(productUserId);

            if (!UseLobbySystem)
            {
                if (EOSNetworkManager.Instance != null && !Mirror.NetworkServer.active)
                {
                    EOSNetworkManager.Instance.StartEosHost();
                }

                string myPuid = LocalProductUserIdString;
                var directSignal = new FriendSignal
                {
                    id = Guid.NewGuid().ToString("N"),
                    type = FriendSignalType.JoinRequestAccepted,
                    senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                    senderPlatform = (int)GetCurrentPlatform(),
                    targetPuid = productUserId,
                    createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    presenceState = (int)FriendPresenceState.InMatch,
                    lobbyId = null,
                    hostAddress = myPuid,
                    joinCode = null
                };

                _signalChannel?.Send(productUserId, directSignal);
                BuildUnifiedFriendsList();
                OnFriendsListUpdated?.Invoke();
                onComplete?.Invoke(true, "Join request accepted!");
                return;
            }

            var signal = new FriendSignal
            {
                id = Guid.NewGuid().ToString("N"),
                type = FriendSignalType.JoinRequestAccepted,
                senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                senderPlatform = (int)GetCurrentPlatform(),
                targetPuid = productUserId,
                createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                presenceState = (int)GetLocalPresenceState(),
                lobbyId = ActiveLobby?.CurrentLobbyId,
                hostAddress = ActiveLobby?.CurrentHostAddress ?? LocalProductUserIdString,
                joinCode = ActiveLobby?.CurrentJoinCode
            };

            _signalChannel?.Send(productUserId, signal);

            ProductUserId targetPuidObj = ProductUserId.FromString(productUserId);
            if (targetPuidObj != null && targetPuidObj.IsValid() && ActiveLobby != null && ActiveLobby.IsInLobby)
            {
                ActiveLobby.SendInvite(targetPuidObj);
            }

            BuildUnifiedFriendsList();
            OnFriendsListUpdated?.Invoke();
            onComplete?.Invoke(true, "Join request accepted!");
        }

        public void DeclineJoinRequest(string productUserId, Action<bool, string> onComplete = null)
        {
            _incomingJoinRequests.Remove(productUserId);

            var signal = new FriendSignal
            {
                id = Guid.NewGuid().ToString("N"),
                type = FriendSignalType.JoinRequestDeclined,
                senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                senderPlatform = (int)GetCurrentPlatform(),
                targetPuid = productUserId,
                createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            _signalChannel?.Send(productUserId, signal);

            BuildUnifiedFriendsList();
            OnFriendsListUpdated?.Invoke();
            onComplete?.Invoke(true, "Join request declined.");
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

                BuildUnifiedFriendsList();
                OnFriendsListUpdated?.Invoke();
            }

            callback?.Invoke(success, success ? successMessage : error);
        }

        private void OnSignalReceived(string senderPuid, FriendSignal signal)
        {
            if (string.IsNullOrEmpty(senderPuid) || signal == null) return;

            _onlinePuids[senderPuid] = DateTime.UtcNow;

            switch (signal.type)
            {
                case FriendSignalType.FriendRequest:
                    if (_friendsStore.IsBlocked(senderPuid)) return;
                    if (_friendsStore.IsFriend(senderPuid))
                    {
                        // Mutual healing: We already consider sender a friend, but sender does not have us in their list!
                        // Reply with RequestAccepted so sender immediately restores us as a friend!
                        var autoAccept = new FriendSignal
                        {
                            id = Guid.NewGuid().ToString("N"),
                            type = FriendSignalType.RequestAccepted,
                            senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                            senderPlatform = (int)GetCurrentPlatform(),
                            targetPuid = senderPuid,
                            createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                        };
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
                    // BIDIRECTIONAL SYNC: When remote player unfriends us, remove them immediately
                    bool wasFriend = _friendsStore.RemoveFriend(senderPuid);
                    if (wasFriend)
                    {
                        _friendsStore.Save(HandleAutoSaveCompleted);
                    }
                    break;

                case FriendSignalType.PresencePing:
                    _onlinePuids[senderPuid] = DateTime.UtcNow;
                    _peerPresence[senderPuid] = (FriendPresenceState)signal.presenceState;
                    if (!string.IsNullOrEmpty(signal.lobbyId)) _peerLobbyId[senderPuid] = signal.lobbyId;
                    if (!string.IsNullOrEmpty(signal.hostAddress)) _peerHostAddress[senderPuid] = signal.hostAddress;
                    if (!string.IsNullOrEmpty(signal.joinCode)) _peerJoinCode[senderPuid] = signal.joinCode;

                    var pong = new FriendSignal
                    {
                        id = Guid.NewGuid().ToString("N"),
                        type = FriendSignalType.PresencePong,
                        senderName = EOSSDKManager.LocalDisplayName ?? "Player",
                        senderPlatform = (int)GetCurrentPlatform(),
                        targetPuid = senderPuid,
                        createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        presenceState = (int)GetLocalPresenceState(),
                        lobbyId = ActiveLobby?.CurrentLobbyId,
                        hostAddress = ActiveLobby?.CurrentHostAddress ?? LocalProductUserIdString,
                        joinCode = ActiveLobby?.CurrentJoinCode
                    };
                    _signalChannel?.Send(senderPuid, pong);
                    BuildUnifiedFriendsList();
                    OnFriendsListUpdated?.Invoke();
                    break;

                case FriendSignalType.PresencePong:
                    _onlinePuids[senderPuid] = DateTime.UtcNow;
                    _peerPresence[senderPuid] = (FriendPresenceState)signal.presenceState;
                    if (!string.IsNullOrEmpty(signal.lobbyId)) _peerLobbyId[senderPuid] = signal.lobbyId;
                    if (!string.IsNullOrEmpty(signal.hostAddress)) _peerHostAddress[senderPuid] = signal.hostAddress;
                    if (!string.IsNullOrEmpty(signal.joinCode)) _peerJoinCode[senderPuid] = signal.joinCode;
                    BuildUnifiedFriendsList();
                    OnFriendsListUpdated?.Invoke();
                    break;

                case FriendSignalType.JoinRequest:
                    NotificationManager.Show($"{signal.senderName} requested to join your match!", NotificationType.Normal);
                    _incomingJoinRequests[senderPuid] = new FriendModel
                    {
                        ProductUserId = senderPuid,
                        DisplayName = signal.senderName,
                        Platform = (LoginProvider)signal.senderPlatform,
                        Status = FriendRelationStatus.PendingIncoming,
                        Presence = FriendPresenceState.Online,
                        IsJoinRequest = true
                    };
                    BuildUnifiedFriendsList();
                    OnFriendsListUpdated?.Invoke();
                    break;

                case FriendSignalType.JoinRequestAccepted:
                    NotificationManager.Show($"{signal.senderName} accepted your join request! Joining...", NotificationType.Normal);
                    ClearPendingJoinRequest(senderPuid);
                    if (!UseLobbySystem)
                    {
                        if (!string.IsNullOrEmpty(signal.hostAddress))
                        {
                            FriendsOverlayManager.Instance?.PersistForGameScene();
                            EOSNetworkManager.Instance?.StartEosClient(signal.hostAddress);
                        }
                    }
                    else if (!string.IsNullOrEmpty(signal.joinCode) && ActiveLobby != null)
                    {
                        if (ActiveLobby.IsInLobby)
                        {
                            _pendingJoinLobbyCode = signal.joinCode;
                            ActiveLobby.LeaveLobby(HandleLeaveBeforeJoiningAcceptedLobby);
                        }
                        else
                        {
                            ActiveLobby.JoinLobbyByCode(signal.joinCode);
                        }
                    }
                    else if (!string.IsNullOrEmpty(signal.hostAddress))
                    {
                        FriendsOverlayManager.Instance?.PersistForGameScene();
                        EOSNetworkManager.Instance?.StartEosClient(signal.hostAddress);
                    }
                    break;

                case FriendSignalType.JoinRequestDeclined:
                    NotificationManager.Show($"{signal.senderName} declined your join request.", NotificationType.Warning);
                    break;

                case FriendSignalType.GameInvite:
                    if (_friendsStore != null && _friendsStore.IsBlocked(senderPuid)) return;

                    string inviterName = string.IsNullOrWhiteSpace(signal.senderName) ? "Player" : signal.senderName;
                    if (_friendsStore != null && (inviterName == "Player" || string.IsNullOrWhiteSpace(inviterName)))
                    {
                        if (_friendsStore.TryGetFriend(senderPuid, out CrossPlayFriendEntry friend))
                        {
                            inviterName = friend.displayName;
                        }
                    }

                    var directInvite = new LobbyInviteModel
                    {
                        InviteId = string.IsNullOrEmpty(signal.id) ? Guid.NewGuid().ToString("N") : signal.id,
                        LobbyId = null,
                        HostAddress = !string.IsNullOrEmpty(signal.hostAddress) ? signal.hostAddress : senderPuid,
                        JoinCode = null,
                        InviterDisplayName = inviterName,
                        InviterUserId = ProductUserId.FromString(senderPuid),
                        AvailableSlots = 4,
                        MaxMembers = 4,
                        ReceivedAt = DateTime.UtcNow
                    };

                    (_inviteService as DirectP2PInviteManager)?.RegisterDirectInvite(directInvite);
                    BuildUnifiedFriendsList();
                    OnFriendsListUpdated?.Invoke();
                    break;

                case FriendSignalType.GameInviteAccepted:
                    NotificationManager.Show($"{signal.senderName} accepted your game invite and is joining!", NotificationType.Normal);
                    break;

                case FriendSignalType.GameInviteDeclined:
                    NotificationManager.Show($"{signal.senderName} declined your game invite.", NotificationType.Warning);
                    break;
            }
        }

        private void HandleLeaveBeforeJoiningAcceptedLobby(bool success)
        {
            string code = _pendingJoinLobbyCode;
            _pendingJoinLobbyCode = null;
            if (!string.IsNullOrEmpty(code) && ActiveLobby != null)
            {
                ActiveLobby.JoinLobbyByCode(code);
            }
        }

        private void OnSignalAcknowledged(string signalId)
        {
            if (_friendsStore.RemoveSignal(signalId))
            {
                _friendsStore.Save(HandleOutboxSaveCompleted);
            }
        }

        private static void HandleOutboxSaveCompleted(bool success, string error)
        {
            // Background signal removal completed; no state mutation required
        }

        private void HandleAutoSaveCompleted(bool success, string error)
        {
            if (success)
            {
                BuildUnifiedFriendsList();
                OnFriendsListUpdated?.Invoke();
            }
        }

        private void HandleConnectLoginSuccess(ProductUserId _)
        {
            Initialize();
            _signalChannel.Start();
        }

        private void HandlePlatformShutdown()
        {
            Shutdown();
        }

        private void HandleTick()
        {
            _signalChannel?.Tick();

            _lastHeartbeatTimer += Time.deltaTime;
            FriendPresenceState currentPresence = GetLocalPresenceState();

            if (currentPresence != _lastLocalPresence)
            {
                _lastLocalPresence = currentPresence;
                _lastHeartbeatTimer = 0f;
                PingAllFriends();
            }
            else if (_lastHeartbeatTimer >= 15f)
            {
                _lastHeartbeatTimer = 0f;
                PingAllFriends();
            }
        }

        private string ResolveCrossPlayDisplayName(string puid)
        {
            if (_friendsStore.TryGetFriend(puid, out CrossPlayFriendEntry entry))
            {
                return entry.displayName;
            }
            return null;
        }

        private void HandleLoggedOut()
        {
            _isInitialized = false;

            if (_lobby != null)
            {
                UnsubscribeLobbyEvents(_lobby);
            }

            _signalChannel?.Stop();
            _platformProvider?.Shutdown();
            _platformProvider = null;
            _onlinePuids.Clear();
            _allFriends.Clear();
            _blockedPlayers.Clear();
            _incomingRequests.Clear();
            _outgoingRequests.Clear();
            _cachedPlatformFriends.Clear();
        }

        public void Shutdown()
        {
            ConnectManager.OnLoginSuccess -= HandleConnectLoginSuccess;
            AuthManager.OnLoggedOut -= HandleLoggedOut;

            EOSSDKManager.Instance.Service.OnTick -= HandleTick;
            EOSSDKManager.Instance.Service.OnBeforeShutdown -= HandlePlatformShutdown;

            if (UseLobbySystem && LobbyManager.ExternalDisplayNameResolver == ResolveCrossPlayDisplayName)
            {
                LobbyManager.ExternalDisplayNameResolver = null;
            }

            _signalChannel.OnSignalReceived -= OnSignalReceived;
            _signalChannel.OnSignalAcknowledged -= OnSignalAcknowledged;

            _inviteService?.Dispose();

            HandleLoggedOut();
            _instance = null;
        }
    }
}