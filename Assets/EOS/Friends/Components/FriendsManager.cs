using System;
using System.Collections.Generic;
using EOS.Auth;
using EOS.Core;
using EOS.Lobby;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Platform;
using UnityEngine;
using UnityEngine.Assertions;

namespace EOS.Friends
{
    /// <summary>
    /// Social coordinator unifying native platform friends, cross-platform cloud friends,
    /// blocklists, and peer-to-peer friend synchronization behind IFriendsService.
    /// </summary>
    public class FriendsManager : IFriendsService
    {
        private readonly PlatformInterface _platformHandle;
        private readonly ConnectInterface _connectHandle;
        private readonly ICrossPlayFriendsStore _friendsStore;
        private readonly IFriendSignalChannel _signalChannel;

        private readonly FriendsPresenceService _presenceService;
        private readonly PlayerLookupService _lookupService;
        private readonly FriendRelationshipService _relationshipService;
        private readonly FriendInviteService _inviteHandler;

        private ILobbyInviteService _inviteService;
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
        private readonly HashSet<string> _addedKeys = new();
        private List<PlatformFriend> _cachedPlatformFriends = new();

        private Action _pendingRefreshComplete;
        private string _pendingUnblockPuid;
        private Action<bool, string> _pendingUnblockCallback;
        private bool _isInitialized;

        public IReadOnlyList<FriendModel> AllFriends => _allFriends;
        public IReadOnlyList<FriendModel> BlockedPlayers => _blockedPlayers;
        public IReadOnlyList<FriendModel> IncomingRequests => _incomingRequests;
        public IReadOnlyList<FriendModel> OutgoingRequests => _outgoingRequests;
        public bool IsRefreshing { get; private set; }
        public bool IsInitialized => _isInitialized;

        public ICrossPlayFriendsStore CrossPlayStore => _friendsStore;
        public ILobbyInviteService LobbyInvites => _inviteService;
        public IFriendSignalChannel FriendSignals => _signalChannel;

        public event Action OnFriendsListUpdated;
        public static event Action OnInitialized;

        private static FriendsManager _instance;
        public static bool HasInstance => _instance != null;
        public static FriendsManager Instance => _instance ??= CreateDefaultInstance();

        private static FriendsManager CreateDefaultInstance()
        {
            PlatformInterface platform = EOSSDKManager.PlatformInterface;
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

            return new FriendsManager(platform, store, invites, signals, lobby);
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

            _presenceService = new FriendsPresenceService(_friendsStore, _signalChannel);
            _lookupService = new PlayerLookupService(_connectHandle);
            _relationshipService = new FriendRelationshipService(_friendsStore, _signalChannel, _lookupService, RebuildAndNotify);
            _inviteHandler = new FriendInviteService(
                _friendsStore,
                _signalChannel,
                _lookupService,
                _presenceService,
                () => UseLobbySystem,
                () => ActiveLobby,
                () => _inviteService,
                RebuildAndNotify);

            if (_signalChannel != null)
            {
                _signalChannel.OnSignalReceived += OnSignalReceived;
                _signalChannel.OnSignalAcknowledged += _relationshipService.OnSignalAcknowledged;
            }

            ConnectManager.OnLoginSuccess += HandleConnectLoginSuccess;
            SessionAuthCoordinator.OnLoggedOut += HandleLoggedOut;
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

            RebuildAndNotify();
        }

        public void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            ConfigurePlatformProvider();
            _platformProvider?.Initialize();
            _signalChannel?.Start();
            EOSSDKManager.Instance.Service.OnTick += HandleTick;

            if (UseLobbySystem)
            {
                SubscribeLobbyEvents(ActiveLobby);
            }

            OnInitialized?.Invoke();
        }

        public void RefreshFriends(Action onComplete = null)
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            if (IsRefreshing) return;

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
            _lookupService.ResolvePlatformFriendsPuids(_cachedPlatformFriends, HandlePlatformFriendsPuidsResolved);
        }

        private void HandlePlatformFriendsPuidsResolved()
        {
            _friendsStore.Load(HandleCloudStoreLoaded);
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
            if (_signalChannel == null || !_signalChannel.IsActive) return;

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

        public void PingFriend(string targetPuid)
            => _presenceService.PingFriend(targetPuid, UseLobbySystem, ActiveLobby);

        public void PingAllFriends()
            => _presenceService.PingAllFriends(_allFriends, UseLobbySystem, ActiveLobby);

        private void RebuildAndNotify()
        {
            BuildUnifiedFriendsList();
            OnFriendsListUpdated?.Invoke();
        }

        private void BuildUnifiedFriendsList()
        {
            _allFriends.Clear();
            _blockedPlayers.Clear();
            _incomingRequests.Clear();
            _outgoingRequests.Clear();

            // 1. Blocked Players from Cloud Store
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
            bool inLobby = UseLobbySystem && activeLobby != null && activeLobby.IsInLobby;
            bool gameStarted = inLobby && activeLobby.HasGameStarted;
            HashSet<string> lobbyMemberPuids = _presenceService.CollectLobbyMemberInfo(activeLobby, inLobby, out HashSet<string> lobbyMemberNames);

            _addedKeys.Clear();
            var crossPlayEntries = _friendsStore.Friends;

            // 2. Native Platform Friends
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

                FriendPresenceState presence = _presenceService.ResolveFriendPresence(
                    puid, pf.PlatformUserId, pf.DisplayName, pf.Presence,
                    inLobby, gameStarted, lobbyMemberPuids, lobbyMemberNames);

                var model = new FriendModel
                {
                    ProductUserId = pf.ProductUserId,
                    PlatformUserId = pf.PlatformUserId,
                    DisplayName = pf.DisplayName,
                    Platform = pf.Platform,
                    Status = FriendRelationStatus.Platform,
                    Presence = presence,
                    AvatarTexture = pf.AvatarTexture
                };
                _presenceService.PopulatePeerLobbyInfo(model);
                _allFriends.Add(model);

                _addedKeys.Add(pf.PlatformUserId);
                if (!string.IsNullOrEmpty(pf.ProductUserId)) _addedKeys.Add(pf.ProductUserId);
                if (!string.IsNullOrWhiteSpace(pf.DisplayName)) _addedKeys.Add(pf.DisplayName.ToLowerInvariant());
            }

            // 3. Cloud Cross-Play Friends
            for (int i = 0; i < crossPlayEntries.Count; i++)
            {
                CrossPlayFriendEntry cf = crossPlayEntries[i];
                if (string.IsNullOrEmpty(cf.productUserId)) continue;
                if (_friendsStore.IsBlocked(cf.productUserId)) continue;

                bool alreadyAdded = _addedKeys.Contains(cf.productUserId);
                if (!alreadyAdded && !string.IsNullOrWhiteSpace(cf.displayName) && _addedKeys.Contains(cf.displayName.ToLowerInvariant()))
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

                FriendPresenceState nativeFallback = FriendPresenceState.Offline;
                for (int p = 0; p < _cachedPlatformFriends.Count; p++)
                {
                    if (string.Equals(_cachedPlatformFriends[p].DisplayName, cf.displayName, StringComparison.OrdinalIgnoreCase))
                    {
                        nativeFallback = _cachedPlatformFriends[p].Presence;
                        break;
                    }
                }

                FriendPresenceState presence = _presenceService.ResolveFriendPresence(
                    cf.productUserId, null, cf.displayName, nativeFallback,
                    inLobby, gameStarted, lobbyMemberPuids, lobbyMemberNames);

                var model = new FriendModel
                {
                    ProductUserId = cf.productUserId,
                    DisplayName = cf.displayName,
                    Platform = (LoginProvider)cf.platform,
                    Status = FriendRelationStatus.CrossPlay,
                    Presence = presence,
                    AvatarTexture = null
                };
                _presenceService.PopulatePeerLobbyInfo(model);
                _allFriends.Add(model);

                _addedKeys.Add(cf.productUserId);
                if (!string.IsNullOrWhiteSpace(cf.displayName)) _addedKeys.Add(cf.displayName.ToLowerInvariant());
            }

            // 4. Sort All Friends: In-match / In-lobby / Online presence on top, then alphabetical
            _allFriends.Sort(SortFriendsPresenceFirst);

            // 5. Incoming Join Requests & Friend Requests
            _inviteHandler.CopyIncomingJoinRequestsTo(_incomingRequests);

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

            // 6. Outgoing Requests
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

        private static int SortFriendsPresenceFirst(FriendModel a, FriendModel b)
        {
            int scoreA = GetPresenceSortPriority(a.Presence);
            int scoreB = GetPresenceSortPriority(b.Presence);
            return scoreA != scoreB
                ? scoreA.CompareTo(scoreB)
                : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
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
            => _relationshipService.GetRelationship(identifier, displayName, _allFriends, _cachedPlatformFriends);

        public FriendRelationStatus GetRelationship(string identifier)
            => GetRelationship(identifier, null);

        public void ResolvePlayer(string identifier, Action<PlayerLookupResult> onComplete)
            => _lookupService.ResolvePlayer(identifier, GetRelationship, onComplete);

        public void AddFriend(string searchIdentifier, Action<bool, string> onComplete = null)
            => _relationshipService.AddFriend(searchIdentifier, GetRelationship, onComplete);

        public void SendFriendRequest(string productUserId, string displayName, LoginProvider platform, Action<bool, string> onComplete = null)
            => _relationshipService.SendFriendRequest(productUserId, displayName, platform, onComplete);

        public void AcceptFriendRequest(string productUserId, Action<bool, string> onComplete = null)
            => _relationshipService.AcceptFriendRequest(productUserId, onComplete);

        public void DeclineFriendRequest(string productUserId, Action<bool, string> onComplete = null)
            => _relationshipService.DeclineFriendRequest(productUserId, onComplete);

        public void RemoveFriend(string productUserId, Action<bool, string> onComplete = null)
            => _relationshipService.RemoveFriend(productUserId, onComplete);

        public void BlockPlayer(string productUserId, string displayName, Action<bool, string> onComplete = null)
            => _relationshipService.BlockPlayer(productUserId, displayName, onComplete);

        public void BlockFriend(FriendModel friend, Action<bool, string> onComplete = null)
            => _relationshipService.BlockFriend(friend, onComplete);

        public void UnblockPlayer(string productUserId, Action<bool, string> onComplete = null)
        {
            _pendingUnblockPuid = productUserId;
            _pendingUnblockCallback = onComplete;
            _relationshipService.UnblockPlayer(productUserId, HandleUnblockCompleted);
        }

        private void HandleUnblockCompleted(bool success, string msg)
        {
            string puid = _pendingUnblockPuid;
            Action<bool, string> callback = _pendingUnblockCallback;
            _pendingUnblockPuid = null;
            _pendingUnblockCallback = null;

            if (success)
            {
                PingFriend(puid);
            }
            callback?.Invoke(success, msg);
        }

        public void InviteFriend(FriendModel friend, Action<bool, string> onComplete = null)
            => _inviteHandler.InviteFriend(friend, onComplete);

        public void InviteFriendToLobby(ProductUserId targetUserId, Action<bool, string> onComplete = null)
            => _inviteHandler.InviteFriendToLobby(targetUserId, onComplete);

        public void SendDirectGameInvite(string targetPuid, Action<bool, string> onComplete = null)
            => _inviteHandler.SendDirectGameInvite(targetPuid, onComplete);

        public void SendDirectInviteAccepted(string targetPuid)
            => _inviteHandler.SendDirectInviteAccepted(targetPuid);

        public void SendDirectInviteDeclined(string targetPuid)
            => _inviteHandler.SendDirectInviteDeclined(targetPuid);

        public bool IsPendingJoinRequestTarget(string puid)
            => _inviteHandler.IsPendingJoinRequestTarget(puid);

        public void ClearPendingJoinRequest(string puid)
            => _inviteHandler.ClearPendingJoinRequest(puid);

        public void RemoveIncomingJoinRequest(string senderPuid)
            => _inviteHandler.RemoveIncomingJoinRequest(senderPuid);

        public void SendJoinRequest(FriendModel friend, Action<bool, string> onComplete = null)
            => _inviteHandler.SendJoinRequest(friend, onComplete);

        public void AcceptJoinRequest(string productUserId, Action<bool, string> onComplete = null)
            => _inviteHandler.AcceptJoinRequest(productUserId, onComplete);

        public void DeclineJoinRequest(string productUserId, Action<bool, string> onComplete = null)
            => _inviteHandler.DeclineJoinRequest(productUserId, onComplete);

        public bool IsPlayerInSameLobbyOrGame(FriendModel friend)
            => _inviteHandler.IsPlayerInSameLobbyOrGame(friend);

        public LobbyInviteModel GetPendingInviteForFriend(FriendModel friend)
            => _inviteHandler.GetPendingInviteForFriend(friend);

        private void OnSignalReceived(string senderPuid, FriendSignal signal)
        {
            if (string.IsNullOrEmpty(senderPuid) || signal == null) return;

            _presenceService.MarkPeerSeen(senderPuid);

            switch (signal.type)
            {
                case FriendSignalType.FriendRequest:
                case FriendSignalType.RequestAccepted:
                case FriendSignalType.RequestDeclined:
                case FriendSignalType.Unfriended:
                    _relationshipService.HandleRelationshipSignal(senderPuid, signal);
                    break;

                case FriendSignalType.PresencePing:
                    _presenceService.HandlePresencePing(senderPuid, signal, UseLobbySystem, ActiveLobby);
                    RebuildAndNotify();
                    break;

                case FriendSignalType.PresencePong:
                    _presenceService.HandlePresencePong(senderPuid, signal);
                    RebuildAndNotify();
                    break;

                case FriendSignalType.JoinRequest:
                case FriendSignalType.JoinRequestAccepted:
                case FriendSignalType.JoinRequestDeclined:
                case FriendSignalType.GameInvite:
                case FriendSignalType.GameInviteAccepted:
                case FriendSignalType.GameInviteDeclined:
                    _inviteHandler.HandleInviteSignal(senderPuid, signal);
                    break;
            }
        }

        private void SubscribeLobbyEvents(ILobby lobby)
        {
            if (lobby == null) return;
            lobby.OnLobbyJoined += HandleLobbyPresenceEvent;
            lobby.OnLobbyLeft += HandleLobbyLeftEvent;
            lobby.OnPlayerJoined += HandleLobbyPlayerJoined;
            lobby.OnPlayerLeft += HandleLobbyPlayerLeft;
            lobby.OnGameStarted += HandleLobbyPresenceEvent;
            lobby.OnLobbyUpdated += HandleLobbyUpdatedEvent;
        }

        private void UnsubscribeLobbyEvents(ILobby lobby)
        {
            if (lobby == null) return;
            lobby.OnLobbyJoined -= HandleLobbyPresenceEvent;
            lobby.OnLobbyLeft -= HandleLobbyLeftEvent;
            lobby.OnPlayerJoined -= HandleLobbyPlayerJoined;
            lobby.OnPlayerLeft -= HandleLobbyPlayerLeft;
            lobby.OnGameStarted -= HandleLobbyPresenceEvent;
            lobby.OnLobbyUpdated -= HandleLobbyUpdatedEvent;
        }

        private void HandleLobbyPresenceEvent(string hostAddress)
        {
            if (!string.IsNullOrEmpty(hostAddress))
            {
                _inviteHandler.RemoveIncomingJoinRequest(hostAddress);
                _inviteService.RemoveInviteFromPlayer(hostAddress);
            }
            UpdateLobbyPresence();
            _presenceService.SyncLocalPresence(UseLobbySystem, ActiveLobby);
            PingAllFriends();
        }

        private void HandleLobbyLeftEvent()
        {
            UpdateLobbyPresence();
            _presenceService.SyncLocalPresence(UseLobbySystem, ActiveLobby);
            PingAllFriends();
        }

        private void HandleLobbyPlayerJoined(ProductUserId userId)
        {
            if (userId != null && userId.IsValid())
            {
                string puid = userId.ToString();
                _inviteHandler.RemoveIncomingJoinRequest(puid);
                _inviteService.RemoveInviteFromPlayer(puid);
            }
            UpdateLobbyPresence();
        }

        private void HandleLobbyPlayerLeft(ProductUserId _) => UpdateLobbyPresence();
        private void HandleLobbyUpdatedEvent() => UpdateLobbyPresence();

        private void UpdateLobbyPresence()
        {
            if (!UseLobbySystem) return;
            _presenceService.UpdateLobbyPresenceInPlace(_allFriends, UseLobbySystem, ActiveLobby);
            _allFriends.Sort(SortFriendsPresenceFirst);
            OnFriendsListUpdated?.Invoke();
        }

        private void ConfigurePlatformProvider()
        {
            _platformProvider = LocalPlayerIdentity.CurrentPlatform switch
            {
                LoginProvider.Steam => EOSSDKManager.SteamService.CreateFriendsProvider(),
                LoginProvider.Epic => new EpicFriendsProvider(_platformHandle, AuthManager.Instance),
                _ => null,
            };
        }

        private void HandleConnectLoginSuccess(ProductUserId _) => Initialize();

        private void HandlePlatformShutdown() => Shutdown();

        private void HandleTick()
        {
            _signalChannel?.Tick();
            _presenceService.Tick(Time.deltaTime, UseLobbySystem, ActiveLobby, _allFriends);
        }

        private string ResolveCrossPlayDisplayName(string puid)
        {
            return _friendsStore.TryGetFriend(puid, out CrossPlayFriendEntry entry)
                ? entry.displayName
                : null;
        }

        private void HandleLoggedOut()
        {
            _isInitialized = false;
            EOSSDKManager.Instance.Service.OnTick -= HandleTick;

            if (_lobby != null)
            {
                UnsubscribeLobbyEvents(_lobby);
            }

            _presenceService.BroadcastOffline(_allFriends);
            _signalChannel?.Stop();
            _platformProvider?.Shutdown();
            _platformProvider = null;
            _inviteService.ClearAllInvites();
            _friendsStore.Reset();
            _presenceService.Clear();
            _inviteHandler.Clear();
            _allFriends.Clear();
            _blockedPlayers.Clear();
            _incomingRequests.Clear();
            _outgoingRequests.Clear();
            _cachedPlatformFriends.Clear();
        }

        public void Shutdown()
        {
            ConnectManager.OnLoginSuccess -= HandleConnectLoginSuccess;
            SessionAuthCoordinator.OnLoggedOut -= HandleLoggedOut;
            EOSSDKManager.Instance.Service.OnBeforeShutdown -= HandlePlatformShutdown;

            if (UseLobbySystem && LobbyManager.ExternalDisplayNameResolver == ResolveCrossPlayDisplayName)
            {
                LobbyManager.ExternalDisplayNameResolver = null;
            }

            if (_signalChannel != null)
            {
                _signalChannel.OnSignalReceived -= OnSignalReceived;
                _signalChannel.OnSignalAcknowledged -= _relationshipService.OnSignalAcknowledged;
            }

            _inviteService?.Dispose();

            HandleLoggedOut();
            _instance = null;
        }
    }
}
