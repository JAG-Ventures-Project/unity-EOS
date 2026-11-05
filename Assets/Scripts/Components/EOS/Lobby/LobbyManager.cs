using System;
using System.Text;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.Platform;
using UnityEngine;
using UnityEngine.Assertions;

namespace EOS
{
    public class LobbyManager : ILobby
    {
        public const string JoinCodeKey = "JOIN_CODE";
        public const string HostAddressKey = "HOST_ADDRESS";
        public const string MatchStartedKey = "MATCH_STARTED";
        public const string ReadyKey = "READY";
        public const string DisplayNameKey = "DISPLAY_NAME";

        public string CurrentLobbyId { get; private set; }
        public string CurrentJoinCode { get; private set; }
        public int CurrentMemberCount { get; private set; }
        public bool IsInLobby => !string.IsNullOrEmpty(CurrentLobbyId);
        public bool IsLobbyOwner { get; private set; }
        public bool AreAllJoinersReady { get; private set; }
        public bool IsCleaningOrphan { get; private set; }
        public bool HasGameStarted => _hasGameStarted;
        public ProductUserId LocalProductUserId => _localProductUserId;

        public event Action<string> OnLobbyCreated;
        public event Action<string> OnLobbyJoined;
        public event Action OnLobbyLeft;
        public event Action<ProductUserId> OnPlayerJoined;
        public event Action<ProductUserId> OnPlayerLeft;
        public event Action<string> OnLobbyError;
        public event Action<string> OnGameStarted;
        public event Action<bool> OnReadyStatusChanged;
        public event Action OnLobbyUpdated;
        public event Action<LobbyInviteCallbackData> OnLobbyInviteReceived;

        private const string BucketId = "GameLobby";
        private const string CodeCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private readonly PlatformInterface _platformHandle;
        private readonly LobbyInterface _lobbyHandle;
        private ProductUserId _localProductUserId;

        private ulong _memberStatusNotificationId = Common.InvalidNotificationid;
        private ulong _lobbyUpdateNotificationId = Common.InvalidNotificationid;
        private ulong _memberUpdateNotificationId = Common.InvalidNotificationid;
        private ulong _inviteNotificationId = Common.InvalidNotificationid;
        private string _currentHostAddress;
        private bool _hasGameStarted;


        public LobbyManager(PlatformInterface platformInterface)
        {
            _platformHandle = platformInterface;
            Assert.IsNotNull(_platformHandle, "[LobbyManager] PlatformInterface must not be null.");

            _lobbyHandle = _platformHandle.GetLobbyInterface();
            Assert.IsNotNull(_lobbyHandle, "[LobbyManager] LobbyInterface failed to initialize.");

            SubscribeToInviteNotifications();
        }

        /// <summary>
        /// Create a new lobby. Upon successful creation, the local player will automatically join the lobby as its owner.
        /// </summary>
        public void CreateLobby(uint maxMembers, Action<bool, string> onComplete = null)
        {
            Assert.IsTrue(!IsInLobby, "[LobbyManager] Cannot create lobby: Player is already in an active lobby.");
            Assert.IsFalse(IsCleaningOrphan, "[LobbyManager] Cannot create lobby: Orphan cleanup is still pending.");
            _localProductUserId = EOSSDKManager.Instance.Service.GetLocalProductUserId();
            Assert.IsNotNull(_localProductUserId, "[LobbyManager] LocalProductUserId must not be null.");

            ExecuteCreateLobby(_localProductUserId, maxMembers, onComplete);
        }

        // AI implementation: Checks local dirty lobby flag and triggers direct single-call orphan cleanup on login.
        public void CheckAndCleanOrphanLobbyOnLogin(Action onComplete = null)
        {
            _localProductUserId = EOSSDKManager.Instance.Service.GetLocalProductUserId();

            if (!LobbyPreferences.HasOrphanLobby(_localProductUserId, out string orphanLobbyId, out bool isOwner))
            {
                IsCleaningOrphan = false;
                onComplete?.Invoke();
                return;
            }

            IsCleaningOrphan = true;
            Debug.Log($"[LobbyManager] Dirty exit detected. Cleaning up orphan lobby {orphanLobbyId} (isOwner: {isOwner})...");
            try
            {
                if (isOwner)
                {
                    var destroyOptions = new DestroyLobbyOptions { LobbyId = orphanLobbyId, LocalUserId = _localProductUserId };
                    _lobbyHandle.DestroyLobby(destroyOptions, onComplete, HandleDirectDestroyOrphanCallback);
                }
                else
                {
                    var leaveOptions = new LeaveLobbyOptions { LobbyId = orphanLobbyId, LocalUserId = _localProductUserId };
                    _lobbyHandle.LeaveLobby(leaveOptions, onComplete, HandleDirectLeaveOrphanCallback);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] Direct orphan cleanup failed: {ex.Message}");
                IsCleaningOrphan = false;
                LobbyPreferences.ClearInLobby(_localProductUserId);
                onComplete?.Invoke();
            }
        }

        private void HandleDirectDestroyOrphanCallback(DestroyLobbyCallbackInfo data)
        {
            // AI implementation
            IsCleaningOrphan = false;
            var onComplete = data.ClientData as Action;
            LobbyPreferences.ClearInLobby(_localProductUserId);
            Debug.Log($"[LobbyManager] Destroy orphan lobby result: {data.ResultCode}");
            onComplete?.Invoke();
        }

        private void HandleDirectLeaveOrphanCallback(LeaveLobbyCallbackInfo data)
        {
            // AI implementation
            IsCleaningOrphan = false;
            var onComplete = data.ClientData as Action;
            LobbyPreferences.ClearInLobby(_localProductUserId);
            Debug.Log($"[LobbyManager] Leave orphan lobby result: {data.ResultCode}");
            onComplete?.Invoke();
        }

        private void ExecuteCreateLobby(ProductUserId puid, uint maxMembers, Action<bool, string> onComplete)
        {
            var createOptions = new CreateLobbyOptions
            {
                LocalUserId = puid,
                MaxLobbyMembers = maxMembers,
                PermissionLevel = LobbyPermissionLevel.Publicadvertised,
                PresenceEnabled = false,
                BucketId = BucketId
            };
            _lobbyHandle.CreateLobby(createOptions, (maxMembers, onComplete), HandleCreateLobbyCallback);
        }

        private void HandleCreateLobbyCallback(CreateLobbyCallbackInfo data)
        {
            var (maxMembers, onComplete) = ((uint, Action<bool, string>))data.ClientData;

            if (data.ResultCode != Result.Success)
            {
                CompleteOperation(onComplete, false, $"CreateLobby failed: {data.ResultCode}");
                return;
            }
            CurrentLobbyId = data.LobbyId;
            CurrentJoinCode = GenerateShortCode(5);
            IsLobbyOwner = true;
            LobbyPreferences.SetInLobby(_localProductUserId, CurrentLobbyId, isOwner: true);
            // Set the searchable Short Code and Host Address attributes
            SetInitialLobbyAttributes(CurrentLobbyId, CurrentJoinCode, onComplete);
        }

        private void SetInitialLobbyAttributes(string lobbyId, string joinCode, Action<bool, string> onComplete)
        {
            var modOptions = new UpdateLobbyModificationOptions
            {
                LobbyId = lobbyId,
                LocalUserId = _localProductUserId
            };
            Result result = _lobbyHandle.UpdateLobbyModification(modOptions, out LobbyModification modHandle);
            if (result != Result.Success)
            {
                CompleteOperation(onComplete, false, $"UpdateLobbyModification failed: {result}");
                return;
            }
            // adding JOIN_CODE attribute (Public so it is searchable)
            var joinCodeAttr = new AttributeData { Key = JoinCodeKey, Value = joinCode };
            var joinCodeOptions = new LobbyModificationAddAttributeOptions
            {
                Attribute = joinCodeAttr,
                Visibility = LobbyAttributeVisibility.Public
            };
            modHandle.AddAttribute(joinCodeOptions);
            // adding HOST_ADDRESS attribute (Host PUID for P2P transport connection)
            var hostAttr = new AttributeData { Key = HostAddressKey, Value = _localProductUserId.ToString() };
            var hostOptions = new LobbyModificationAddAttributeOptions
            {
                Attribute = hostAttr,
                Visibility = LobbyAttributeVisibility.Public
            };
            modHandle.AddAttribute(hostOptions);
            // adding MATCH_STARTED attribute (initially false)
            var matchStartedAttr = new AttributeData { Key = MatchStartedKey, Value = "false" };
            var matchStartedOptions = new LobbyModificationAddAttributeOptions
            {
                Attribute = matchStartedAttr,
                Visibility = LobbyAttributeVisibility.Public
            };
            modHandle.AddAttribute(matchStartedOptions);

            // adding DISPLAY_NAME member attribute for host
            modHandle.AddMemberAttribute(new LobbyModificationAddMemberAttributeOptions
            {
                Attribute = new AttributeData { Key = DisplayNameKey, Value = EOSSDKManager.LocalDisplayName },
                Visibility = LobbyAttributeVisibility.Public
            });

            var updateOptions = new UpdateLobbyOptions { LobbyModificationHandle = modHandle };
            _lobbyHandle.UpdateLobby(updateOptions, (onComplete, modHandle), HandleUpdateLobbyCallback);
        }

        private void HandleUpdateLobbyCallback(UpdateLobbyCallbackInfo data)
        {
            var (onComplete, modHandle) = ((Action<bool, string>, LobbyModification))data.ClientData;
            modHandle?.Release();

            if (data.ResultCode != Result.Success)
            {
                CompleteOperation(onComplete, false, $"Setting lobby attributes failed: {data.ResultCode}");
                return;
            }
            CurrentMemberCount = 1;
            AreAllJoinersReady = true;
            SubscribeToNotifications();
            OnLobbyCreated?.Invoke(CurrentJoinCode);
            CompleteOperation(onComplete, true, CurrentJoinCode);
        }

        /// <summary>
        /// Searches for a lobby matching the given short code and joins it.
        /// </summary>
        public void JoinLobbyByCode(string joinCode, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(IsCleaningOrphan, "[LobbyManager] Cannot join lobby: Orphan cleanup is still pending.");
            _localProductUserId = EOSSDKManager.Instance.Service.GetLocalProductUserId();
            string formattedCode = joinCode.Trim().ToUpperInvariant();
            var searchOptions = new CreateLobbySearchOptions { MaxResults = 50 };
            Result result = _lobbyHandle.CreateLobbySearch(searchOptions, out LobbySearch searchHandle);
            if (result != Result.Success)
            {
                CompleteOperation(onComplete, false, $"CreateLobbySearch failed: {result}");
                return;
            }

            // Filter search by BucketId
            var bucketParamOptions = new LobbySearchSetParameterOptions
            {
                Parameter = new AttributeData { Key = "bucket", Value = BucketId },
                ComparisonOp = ComparisonOp.Equal
            };
            searchHandle.SetParameter(bucketParamOptions);

            // Filter search by JOIN_CODE
            var paramOptions = new LobbySearchSetParameterOptions
            {
                Parameter = new AttributeData { Key = JoinCodeKey, Value = formattedCode },
                ComparisonOp = ComparisonOp.Equal
            };
            searchHandle.SetParameter(paramOptions);

            // Filter search by MATCH_STARTED == "false" so backend excludes matches that have already started
            var matchParamOptions = new LobbySearchSetParameterOptions
            {
                Parameter = new AttributeData { Key = MatchStartedKey, Value = "false" },
                ComparisonOp = ComparisonOp.Equal
            };
            searchHandle.SetParameter(matchParamOptions);

            var findOptions = new LobbySearchFindOptions { LocalUserId = _localProductUserId };
            searchHandle.Find(findOptions, (searchHandle, onComplete, formattedCode), HandleFindLobbiesCallback);
        }

        private void HandleFindLobbiesCallback(LobbySearchFindCallbackInfo data)
        {
            var (searchHandle, onComplete, joinCode) = ((LobbySearch, Action<bool, string>, string))data.ClientData;
            if (data.ResultCode != Result.Success)
            {
                searchHandle.Release();
                CompleteOperation(onComplete, false, $"Search failed: {data.ResultCode}");
                return;
            }
            var countOptions = new LobbySearchGetSearchResultCountOptions();
            uint count = searchHandle.GetSearchResultCount(countOptions);
            if (count == 0)
            {
                searchHandle.Release();
                CompleteOperation(onComplete, false, "Lobby not found. Please verify the room code.");
                return;
            }
            var copyOptions = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = 0 };
            Result copyResult = searchHandle.CopySearchResultByIndex(copyOptions, out LobbyDetails detailsHandle);
            searchHandle.Release();
            if (copyResult != Result.Success)
            {
                CompleteOperation(onComplete, false, $"Failed to copy lobby search details: {copyResult}");
                return;
            }
            // get host address from details
            var attrOptions = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = HostAddressKey };
            string hostAddress = string.Empty;
            if (detailsHandle.CopyAttributeByKey(attrOptions, out Epic.OnlineServices.Lobby.Attribute hostAttr) == Result.Success && hostAttr?.Data?.Value != null)
            {
                hostAddress = hostAttr.Data.Value.AsUtf8;
            }
            // verify match has not already started
            var matchAttrOptions = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = MatchStartedKey };
            if (detailsHandle.CopyAttributeByKey(matchAttrOptions, out Epic.OnlineServices.Lobby.Attribute matchAttr) == Result.Success && matchAttr?.Data?.Value != null)
            {
                if (matchAttr.Data.Value.AsUtf8 == "true")
                {
                    detailsHandle.Release();
                    CompleteOperation(onComplete, false, "Match has already started in this room.");
                    return;
                }
            }
            // join the lobby - pass detailsHandle to callback so it stays alive during async join
            var joinOptions = new JoinLobbyOptions
            {
                LobbyDetailsHandle = detailsHandle,
                LocalUserId = _localProductUserId,
                PresenceEnabled = false
            };
            _lobbyHandle.JoinLobby(joinOptions, (onComplete, joinCode, hostAddress, detailsHandle), HandleJoinLobbyCallback);
        }

        private void HandleJoinLobbyCallback(JoinLobbyCallbackInfo data)
        {
            var (onComplete, joinCode, hostAddress, detailsHandle) = ((Action<bool, string>, string, string, LobbyDetails))data.ClientData;
            uint memberCount = detailsHandle.GetMemberCount(new LobbyDetailsGetMemberCountOptions());
            detailsHandle?.Release();

            if (data.ResultCode != Result.Success)
            {
                string errorMsg = (data.ResultCode == Result.LobbyTooManyPlayers) ? "Lobby is full." : $"JoinLobby failed: {data.ResultCode}";
                CompleteOperation(onComplete, false, errorMsg);
                return;
            }
            CurrentLobbyId = data.LobbyId;
            CurrentJoinCode = joinCode;
            IsLobbyOwner = false;
            LobbyPreferences.SetInLobby(_localProductUserId, CurrentLobbyId, isOwner: false);
            _currentHostAddress = hostAddress;
            _hasGameStarted = false;
            CurrentMemberCount = (int)memberCount + 1;
            SubscribeToNotifications();
            UpdateAndCheckReadyStatus();
            SetLocalMemberDisplayName(EOSSDKManager.LocalDisplayName);
            OnLobbyJoined?.Invoke(hostAddress);
            CompleteOperation(onComplete, true, hostAddress);
        }

        /// <summary>
        /// Joins an existing lobby using an invitation ID.
        /// </summary>
        public void JoinLobbyByInvite(string inviteId, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(IsCleaningOrphan, "[LobbyManager] Cannot join lobby: Orphan cleanup is still pending.");
            _localProductUserId = EOSSDKManager.Instance.Service.GetLocalProductUserId();
            Assert.IsNotNull(_localProductUserId, "[LobbyManager] LocalProductUserId must not be null.");

            var copyOptions = new CopyLobbyDetailsHandleByInviteIdOptions { InviteId = inviteId };
            Result result = _lobbyHandle.CopyLobbyDetailsHandleByInviteId(copyOptions, out LobbyDetails detailsHandle);
            if (result != Result.Success || detailsHandle == null)
            {
                CompleteOperation(onComplete, false, $"Failed to inspect lobby invite: {result}");
                return;
            }

            var infoOptions = new LobbyDetailsCopyInfoOptions();
            if (detailsHandle.CopyInfo(infoOptions, out LobbyDetailsInfo info) == Result.Success && info != null)
            {
                if (info.AvailableSlots == 0)
                {
                    detailsHandle.Release();
                    CompleteOperation(onComplete, false, "Lobby is full.");
                    return;
                }
            }

            var matchAttrOptions = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = MatchStartedKey };
            if (detailsHandle.CopyAttributeByKey(matchAttrOptions, out Epic.OnlineServices.Lobby.Attribute matchAttr) == Result.Success && matchAttr?.Data?.Value != null)
            {
                if (matchAttr.Data.Value.AsUtf8 == "true")
                {
                    detailsHandle.Release();
                    CompleteOperation(onComplete, false, "Match has already started in this room.");
                    return;
                }
            }

            var codeAttrOptions = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = JoinCodeKey };
            string joinCode = string.Empty;
            if (detailsHandle.CopyAttributeByKey(codeAttrOptions, out Epic.OnlineServices.Lobby.Attribute codeAttr) == Result.Success && codeAttr?.Data?.Value != null)
            {
                joinCode = codeAttr.Data.Value.AsUtf8;
            }

            var hostAttrOptions = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = HostAddressKey };
            string hostAddress = string.Empty;
            if (detailsHandle.CopyAttributeByKey(hostAttrOptions, out Epic.OnlineServices.Lobby.Attribute hostAttr) == Result.Success && hostAttr?.Data?.Value != null)
            {
                hostAddress = hostAttr.Data.Value.AsUtf8;
            }

            var joinOptions = new JoinLobbyOptions
            {
                LobbyDetailsHandle = detailsHandle,
                LocalUserId = _localProductUserId,
                PresenceEnabled = false
            };

            _lobbyHandle.JoinLobby(joinOptions, (onComplete, joinCode, hostAddress, detailsHandle), HandleJoinLobbyCallback);
        }

        public void SendInvite(ProductUserId targetUserId, Action<bool, string> onComplete = null)
        {
            Assert.IsTrue(IsInLobby, "[LobbyManager] Cannot send invite: Not in a lobby.");
            Assert.IsNotNull(targetUserId, "[LobbyManager] targetUserId must not be null.");

            var options = new SendInviteOptions
            {
                LobbyId = CurrentLobbyId,
                LocalUserId = _localProductUserId,
                TargetUserId = targetUserId
            };

            _lobbyHandle.SendInvite(options, onComplete, HandleSendInviteCallback);
        }

        private void HandleSendInviteCallback(SendInviteCallbackInfo data)
        {
            var onComplete = data.ClientData as Action<bool, string>;
            if (data.ResultCode != Result.Success)
            {
                CompleteOperation(onComplete, false, $"SendInvite failed: {data.ResultCode}");
                return;
            }

            CompleteOperation(onComplete, true, null);
        }

        public void RejectInvite(string inviteId, Action<bool, string> onComplete = null)
        {
            _localProductUserId = EOSSDKManager.Instance.Service.GetLocalProductUserId();
            Assert.IsNotNull(_localProductUserId, "[LobbyManager] LocalProductUserId must not be null.");
            Assert.IsFalse(string.IsNullOrEmpty(inviteId), "[LobbyManager] inviteId must not be null or empty.");

            var options = new RejectInviteOptions
            {
                InviteId = inviteId,
                LocalUserId = _localProductUserId
            };

            _lobbyHandle.RejectInvite(options, onComplete, HandleRejectInviteCallback);
        }

        private void HandleRejectInviteCallback(RejectInviteCallbackInfo data)
        {
            var onComplete = data.ClientData as Action<bool, string>;
            if (data.ResultCode != Result.Success)
            {
                CompleteOperation(onComplete, false, $"RejectInvite failed: {data.ResultCode}");
                return;
            }

            CompleteOperation(onComplete, true, null);
        }

        /// <summary>
        /// Leaves or destroys the current lobby depending on whether the local player is host.
        /// </summary>
        public void LeaveLobby(Action<bool> onComplete = null)
        {
            if (!IsInLobby)
            {
                CompleteOperation(onComplete, true);
                return;
            }

            if (IsLobbyOwner)
            {
                var destroyOptions = new DestroyLobbyOptions { LobbyId = CurrentLobbyId, LocalUserId = _localProductUserId };
                _lobbyHandle.DestroyLobby(destroyOptions, onComplete, HandleDestroyLobbyCallback);
            }
            else
            {
                var leaveOptions = new LeaveLobbyOptions { LobbyId = CurrentLobbyId, LocalUserId = _localProductUserId };
                _lobbyHandle.LeaveLobby(leaveOptions, onComplete, HandleLeaveLobbyCallback);
            }
        }

        public void KickMember(ProductUserId targetUserId, Action<bool, string> onComplete = null)
        {
            Assert.IsTrue(IsLobbyOwner, "[LobbyManager] Only the lobby owner can kick members.");
            Assert.IsNotNull(targetUserId, "[LobbyManager] TargetUserId must not be null.");

            var kickOptions = new KickMemberOptions
            {
                LobbyId = CurrentLobbyId,
                LocalUserId = _localProductUserId,
                TargetUserId = targetUserId
            };

            _lobbyHandle.KickMember(kickOptions, onComplete, HandleKickMemberCallback);
        }

        private void HandleKickMemberCallback(KickMemberCallbackInfo data)
        {
            var onComplete = data.ClientData as Action<bool, string>;
            if (data.ResultCode != Result.Success)
            {
                CompleteOperation(onComplete, false, $"KickMember failed: {data.ResultCode}");
                return;
            }

            CompleteOperation(onComplete, true, null);
        }

        private void HandleDestroyLobbyCallback(DestroyLobbyCallbackInfo data)
        {
            var onComplete = data.ClientData as Action<bool>;
            ResetState();
            OnLobbyLeft?.Invoke();
            bool success = data.ResultCode == Result.Success;
            CompleteOperation(onComplete, success, success ? null : $"DestroyLobby failed: {data.ResultCode}");
        }

        private void HandleLeaveLobbyCallback(LeaveLobbyCallbackInfo data)
        {
            var onComplete = data.ClientData as Action<bool>;
            ResetState();
            OnLobbyLeft?.Invoke();
            bool success = data.ResultCode == Result.Success;
            CompleteOperation(onComplete, success, success ? null : $"LeaveLobby failed: {data.ResultCode}");
        }

        private void ResetState()
        {
            LobbyPreferences.ClearInLobby(_localProductUserId);
            UnsubscribeFromNotifications();
            CurrentLobbyId = null;
            CurrentJoinCode = null;
            IsLobbyOwner = false;
            CurrentMemberCount = 0;
            AreAllJoinersReady = false;
            _currentHostAddress = null;
            _hasGameStarted = false;
        }

        private void SubscribeToInviteNotifications()
        {
            if (_inviteNotificationId != Common.InvalidNotificationid) return;

            var options = new AddNotifyLobbyInviteReceivedOptions();
            _inviteNotificationId = _lobbyHandle.AddNotifyLobbyInviteReceived(options, this, OnLobbyInviteReceivedCallback);
        }

        private void UnsubscribeFromInviteNotifications()
        {
            if (_inviteNotificationId != Common.InvalidNotificationid)
            {
                _lobbyHandle.RemoveNotifyLobbyInviteReceived(_inviteNotificationId);
                _inviteNotificationId = Common.InvalidNotificationid;
            }
        }

        private static void OnLobbyInviteReceivedCallback(LobbyInviteReceivedCallbackInfo data)
        {
            var manager = data.ClientData as LobbyManager;
            if (manager == null || string.IsNullOrEmpty(data.InviteId)) return;

            var copyOptions = new CopyLobbyDetailsHandleByInviteIdOptions { InviteId = data.InviteId };
            Result result = manager._lobbyHandle.CopyLobbyDetailsHandleByInviteId(copyOptions, out LobbyDetails detailsHandle);
            if (result != Result.Success || detailsHandle == null) return;

            var infoOptions = new LobbyDetailsCopyInfoOptions();
            detailsHandle.CopyInfo(infoOptions, out LobbyDetailsInfo info);

            var codeAttrOptions = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = JoinCodeKey };
            string joinCode = string.Empty;
            if (detailsHandle.CopyAttributeByKey(codeAttrOptions, out Epic.OnlineServices.Lobby.Attribute codeAttr) == Result.Success && codeAttr?.Data?.Value != null)
            {
                joinCode = codeAttr.Data.Value.AsUtf8;
            }

            var hostAttrOptions = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = HostAddressKey };
            string hostAddress = string.Empty;
            if (detailsHandle.CopyAttributeByKey(hostAttrOptions, out Epic.OnlineServices.Lobby.Attribute hostAttr) == Result.Success && hostAttr?.Data?.Value != null)
            {
                hostAddress = hostAttr.Data.Value.AsUtf8;
            }

            string hostName = string.Empty;
            if (info?.LobbyOwnerUserId != null)
            {
                var memberAttrOptions = new LobbyDetailsCopyMemberAttributeByKeyOptions
                {
                    AttrKey = DisplayNameKey,
                    TargetUserId = info.LobbyOwnerUserId
                };
                if (detailsHandle.CopyMemberAttributeByKey(memberAttrOptions, out Epic.OnlineServices.Lobby.Attribute nameAttr) == Result.Success && nameAttr?.Data?.Value != null)
                {
                    hostName = nameAttr.Data.Value.AsUtf8;
                }
            }

            uint availableSlots = info?.AvailableSlots ?? 0;
            uint maxMembers = info?.MaxMembers ?? 0;
            string lobbyId = info?.LobbyId;
            ProductUserId ownerUserId = info?.LobbyOwnerUserId;

            detailsHandle.Release();

            var callbackData = new LobbyInviteCallbackData
            {
                InviteId = data.InviteId,
                LobbyId = lobbyId,
                InviterUserId = ownerUserId,
                InviterDisplayName = hostName,
                JoinCode = joinCode,
                HostAddress = hostAddress,
                AvailableSlots = availableSlots,
                MaxMembers = maxMembers
            };

            manager.OnLobbyInviteReceived?.Invoke(callbackData);
        }

        private void SubscribeToNotifications()
        {
            UnsubscribeFromNotifications();

            var memberOptions = new AddNotifyLobbyMemberStatusReceivedOptions();
            _memberStatusNotificationId = _lobbyHandle.AddNotifyLobbyMemberStatusReceived(
                memberOptions, this, OnMemberStatusChanged);

            var updateOptions = new AddNotifyLobbyUpdateReceivedOptions();
            _lobbyUpdateNotificationId = _lobbyHandle.AddNotifyLobbyUpdateReceived(
                updateOptions, this, OnLobbyUpdateReceived);

            var memberUpdateOptions = new AddNotifyLobbyMemberUpdateReceivedOptions();
            _memberUpdateNotificationId = _lobbyHandle.AddNotifyLobbyMemberUpdateReceived(
                memberUpdateOptions, this, OnLobbyMemberUpdateReceived);
        }

        private void UnsubscribeFromNotifications()
        {
            if (_memberStatusNotificationId != Common.InvalidNotificationid)
            {
                _lobbyHandle.RemoveNotifyLobbyMemberStatusReceived(_memberStatusNotificationId);
                _memberStatusNotificationId = Common.InvalidNotificationid;
            }

            if (_lobbyUpdateNotificationId != Common.InvalidNotificationid)
            {
                _lobbyHandle.RemoveNotifyLobbyUpdateReceived(_lobbyUpdateNotificationId);
                _lobbyUpdateNotificationId = Common.InvalidNotificationid;
            }

            if (_memberUpdateNotificationId != Common.InvalidNotificationid)
            {
                _lobbyHandle.RemoveNotifyLobbyMemberUpdateReceived(_memberUpdateNotificationId);
                _memberUpdateNotificationId = Common.InvalidNotificationid;
            }
        }

        private static void OnLobbyMemberUpdateReceived(LobbyMemberUpdateReceivedCallbackInfo data)
        {
            var manager = data.ClientData as LobbyManager;
            if (manager == null || string.IsNullOrEmpty(data.LobbyId) || data.LobbyId != manager.CurrentLobbyId)
                return;

            manager.UpdateAndCheckReadyStatus();
            manager.OnLobbyUpdated?.Invoke();
        }

        private static void OnLobbyUpdateReceived(LobbyUpdateReceivedCallbackInfo data)
        {
            var manager = data.ClientData as LobbyManager;
            if (manager == null || string.IsNullOrEmpty(data.LobbyId) || data.LobbyId != manager.CurrentLobbyId)
                return;

            var copyOptions = new CopyLobbyDetailsHandleOptions
            {
                LobbyId = data.LobbyId,
                LocalUserId = manager._localProductUserId
            };

            if (manager._lobbyHandle.CopyLobbyDetailsHandle(copyOptions, out LobbyDetails detailsHandle) == Result.Success && detailsHandle != null)
            {
                var attrOptions = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = MatchStartedKey };
                if (detailsHandle.CopyAttributeByKey(attrOptions, out Epic.OnlineServices.Lobby.Attribute matchAttr) == Result.Success && matchAttr?.Data?.Value != null)
                {
                    if (matchAttr.Data.Value.AsUtf8 == "true")
                    {
                        if (!manager._hasGameStarted)
                        {
                            manager._hasGameStarted = true;

                            if (!manager.IsLobbyOwner)
                            {
                                manager.OnGameStarted?.Invoke(manager._currentHostAddress);
                            }
                        }
                    }
                    else if (matchAttr.Data.Value.AsUtf8 == "false")
                    {
                        manager._hasGameStarted = false;
                    }
                }
                detailsHandle.Release();
                manager.UpdateAndCheckReadyStatus();
                manager.OnLobbyUpdated?.Invoke();
            }
        }

        private static void OnMemberStatusChanged(LobbyMemberStatusReceivedCallbackInfo data)
        {
            var manager = data.ClientData as LobbyManager;
            if (manager == null || string.IsNullOrEmpty(data.LobbyId) || data.LobbyId != manager.CurrentLobbyId)
                return;

            // Check if the local player themselves got disconnected or kicked
            if (data.TargetUserId == manager._localProductUserId)
            {
                if (data.CurrentStatus == LobbyMemberStatus.Disconnected || data.CurrentStatus == LobbyMemberStatus.Kicked)
                {
                    manager.ResetState();
                    manager.OnLobbyLeft?.Invoke();
                    manager.OnLobbyError?.Invoke($"Disconnected from room ({data.CurrentStatus}).");
                    return;
                }
            }

            switch (data.CurrentStatus)
            {
                case LobbyMemberStatus.Joined:
                    manager.CurrentMemberCount++;
                    manager.UpdateAndCheckReadyStatus(); // for when a new player joins and everyone is ready
                    manager.OnPlayerJoined?.Invoke(data.TargetUserId);
                    break;
                case LobbyMemberStatus.Left:
                case LobbyMemberStatus.Disconnected:
                case LobbyMemberStatus.Kicked:
                    manager.CurrentMemberCount = Math.Max(1, manager.CurrentMemberCount - 1);
                    manager.UpdateAndCheckReadyStatus(); // when someone leaves check if all of the others are ready
                    manager.OnPlayerLeft?.Invoke(data.TargetUserId);
                    break;
                case LobbyMemberStatus.Closed:
                    // Host closed/left the lobby
                    if (!manager.IsLobbyOwner)
                    {
                        manager.ResetState();
                        manager.OnLobbyLeft?.Invoke();
                        manager.OnLobbyError?.Invoke("The host closed the room.");
                    }
                    break;
            }
        }

        public void StartGame(Action<bool, string> onComplete = null)
        {
            if (!IsInLobby || !IsLobbyOwner)
            {
                CompleteOperation(onComplete, false, "[LobbyManager] Cannot start game: Not the lobby owner or not in a lobby.");
                return;
            }

            var modOptions = new UpdateLobbyModificationOptions
            {
                LobbyId = CurrentLobbyId,
                LocalUserId = _localProductUserId
            };
            Result result = _lobbyHandle.UpdateLobbyModification(modOptions, out LobbyModification modHandle);
            if (result != Result.Success)
            {
                CompleteOperation(onComplete, false, $"[LobbyManager] UpdateLobbyModification failed: {result}");
                return;
            }

            // Lock the lobby permissions to Inviteonly so it is no longer publicly advertised or searchable
            var permissionOptions = new LobbyModificationSetPermissionLevelOptions
            {
                PermissionLevel = LobbyPermissionLevel.Inviteonly
            };
            modHandle.SetPermissionLevel(permissionOptions);

            // Mark MATCH_STARTED as true
            var startAttr = new AttributeData { Key = MatchStartedKey, Value = "true" };
            var startOptions = new LobbyModificationAddAttributeOptions
            {
                Attribute = startAttr,
                Visibility = LobbyAttributeVisibility.Public
            };
            modHandle.AddAttribute(startOptions);

            var updateOptions = new UpdateLobbyOptions { LobbyModificationHandle = modHandle };
            _lobbyHandle.UpdateLobby(updateOptions, (onComplete, modHandle), HandleStartGameUpdateCallback);
        }

        private void HandleStartGameUpdateCallback(UpdateLobbyCallbackInfo data)
        {
            var (onComplete, modHandle) = ((Action<bool, string>, LobbyModification))data.ClientData;
            modHandle?.Release();

            if (data.ResultCode != Result.Success)
            {
                CompleteOperation(onComplete, false, $"[LobbyManager] StartGame failed: {data.ResultCode}");
                return;
            }

            _hasGameStarted = true;
            CompleteOperation(onComplete, true, null);
        }

        public void SetReadyStatus(bool isReady, Action<bool, string> onComplete = null)
        {
            if (!IsInLobby)
            {
                CompleteOperation(onComplete, false, "[LobbyManager] Cannot set ready status: Not in a lobby.");
                return;
            }

            var modOptions = new UpdateLobbyModificationOptions
            {
                LobbyId = CurrentLobbyId,
                LocalUserId = _localProductUserId
            };
            Result result = _lobbyHandle.UpdateLobbyModification(modOptions, out LobbyModification modHandle);
            if (result != Result.Success)
            {
                CompleteOperation(onComplete, false, $"[LobbyManager] UpdateLobbyModification failed: {result}");
                return;
            }

            var readyAttr = new AttributeData { Key = ReadyKey, Value = isReady ? "true" : "false" };
            var memberAttrOptions = new LobbyModificationAddMemberAttributeOptions
            {
                Attribute = readyAttr,
                Visibility = LobbyAttributeVisibility.Public
            };
            modHandle.AddMemberAttribute(memberAttrOptions);

            var updateOptions = new UpdateLobbyOptions { LobbyModificationHandle = modHandle };
            _lobbyHandle.UpdateLobby(updateOptions, (onComplete, modHandle), HandleCallback);
        }

        public void ResetMatchState(Action<bool, string> onComplete = null)
        {
            if (!IsInLobby || !IsLobbyOwner)
            {
                CompleteOperation(onComplete, false, "[LobbyManager] Cannot reset match state: Not the lobby owner or not in a lobby.");
                return;
            }

            var modOptions = new UpdateLobbyModificationOptions
            {
                LobbyId = CurrentLobbyId,
                LocalUserId = _localProductUserId
            };
            Result result = _lobbyHandle.UpdateLobbyModification(modOptions, out LobbyModification modHandle);
            if (result != Result.Success)
            {
                CompleteOperation(onComplete, false, $"[LobbyManager] UpdateLobbyModification failed: {result}");
                return;
            }

            // Restore lobby to Publicadvertised so it can be searched/joined again
            var permissionOptions = new LobbyModificationSetPermissionLevelOptions
            {
                PermissionLevel = LobbyPermissionLevel.Publicadvertised
            };
            modHandle.SetPermissionLevel(permissionOptions);

            // Mark MATCH_STARTED as false
            var matchStartedAttr = new AttributeData { Key = MatchStartedKey, Value = "false" };
            var matchStartedOptions = new LobbyModificationAddAttributeOptions
            {
                Attribute = matchStartedAttr,
                Visibility = LobbyAttributeVisibility.Public
            };
            modHandle.AddAttribute(matchStartedOptions);

            var updateOptions = new UpdateLobbyOptions { LobbyModificationHandle = modHandle };
            _lobbyHandle.UpdateLobby(updateOptions, (onComplete, modHandle), HandleResetMatchStateUpdateCallback);
        }

        private void HandleResetMatchStateUpdateCallback(UpdateLobbyCallbackInfo data)
        {
            var (onComplete, modHandle) = ((Action<bool, string>, LobbyModification))data.ClientData;
            modHandle?.Release();

            if (data.ResultCode != Result.Success)
            {
                CompleteOperation(onComplete, false, $"[LobbyManager] ResetMatchState failed: {data.ResultCode}");
                return;
            }

            _hasGameStarted = false;
            CompleteOperation(onComplete, true, null);
        }

        private void HandleCallback(UpdateLobbyCallbackInfo data)
        {
            var (onComplete, modHandle) = ((Action<bool, string>, LobbyModification))data.ClientData;
            modHandle?.Release();

            if (data.ResultCode != Result.Success)
            {
                CompleteOperation(onComplete, false, $"[LobbyManager] Lobby update failed: {data.ResultCode}");
                return;
            }

            CompleteOperation(onComplete, true, null);
        }

        public void SetLocalMemberDisplayName(string displayName, Action<bool, string> onComplete = null)
        {
            if (!IsInLobby) return;

            var modOptions = new UpdateLobbyModificationOptions { LobbyId = CurrentLobbyId, LocalUserId = _localProductUserId };
            Result result = _lobbyHandle.UpdateLobbyModification(modOptions, out LobbyModification modHandle);
            if (result != Result.Success)
            {
                CompleteOperation(onComplete, false, $"[LobbyManager] UpdateLobbyModification failed: {result}");
                return;
            }

            modHandle.AddMemberAttribute(new LobbyModificationAddMemberAttributeOptions
            {
                Attribute = new AttributeData { Key = DisplayNameKey, Value = displayName },
                Visibility = LobbyAttributeVisibility.Public
            });

            _lobbyHandle.UpdateLobby(new UpdateLobbyOptions { LobbyModificationHandle = modHandle }, (onComplete, modHandle), HandleCallback);
        }

        public List<ProductUserId> GetCurrentMemberUserIds()
        {
            var userIds = new List<ProductUserId>();
            var copyOptions = new CopyLobbyDetailsHandleOptions { LobbyId = CurrentLobbyId, LocalUserId = _localProductUserId };
            if (_lobbyHandle.CopyLobbyDetailsHandle(copyOptions, out LobbyDetails detailsHandle) != Result.Success)
            {
                if (_localProductUserId != null) userIds.Add(_localProductUserId);
                return userIds;
            }

            uint count = detailsHandle.GetMemberCount(new LobbyDetailsGetMemberCountOptions());
            for (uint i = 0; i < count; i++)
            {
                ProductUserId puid = detailsHandle.GetMemberByIndex(new LobbyDetailsGetMemberByIndexOptions { MemberIndex = i });
                if (puid != null) userIds.Add(puid);
            }

            detailsHandle.Release();
            return userIds;
        }

        public string GetMemberDisplayName(ProductUserId userId)
        {
            if (userId == null || userId == _localProductUserId)
                return EOSSDKManager.LocalDisplayName;

            var copyOptions = new CopyLobbyDetailsHandleOptions { LobbyId = CurrentLobbyId, LocalUserId = _localProductUserId };
            if (_lobbyHandle.CopyLobbyDetailsHandle(copyOptions, out LobbyDetails detailsHandle) != Result.Success)
                return "Player";

            var attrOptions = new LobbyDetailsCopyMemberAttributeByKeyOptions { TargetUserId = userId, AttrKey = DisplayNameKey };
            string displayName = "Player";
            if (detailsHandle.CopyMemberAttributeByKey(attrOptions, out var attr) == Result.Success && attr?.Data?.Value != null)
            {
                displayName = attr.Data.Value.AsUtf8;
            }

            detailsHandle.Release();
            return displayName;
        }

        public bool IsMemberReady(ProductUserId userId)
        {
            if (userId == null || !IsInLobby) return false;
            if (userId == _localProductUserId && IsLobbyOwner) return true;

            var copyOptions = new CopyLobbyDetailsHandleOptions { LobbyId = CurrentLobbyId, LocalUserId = _localProductUserId };
            if (_lobbyHandle.CopyLobbyDetailsHandle(copyOptions, out LobbyDetails detailsHandle) != Result.Success)
                return false;

            if (detailsHandle.CopyInfo(new LobbyDetailsCopyInfoOptions(), out LobbyDetailsInfo info) == Result.Success && info != null)
            {
                if (userId == info.LobbyOwnerUserId)
                {
                    detailsHandle.Release();
                    return true;
                }
            }

            var attrOptions = new LobbyDetailsCopyMemberAttributeByKeyOptions { TargetUserId = userId, AttrKey = ReadyKey };
            bool isReady = false;
            if (detailsHandle.CopyMemberAttributeByKey(attrOptions, out var attr) == Result.Success && attr?.Data?.Value != null)
            {
                isReady = attr.Data.Value.AsUtf8 == "true";
            }

            detailsHandle.Release();
            return isReady;
        }

        public bool UpdateAndCheckReadyStatus()
        {
            var copyOptions = new CopyLobbyDetailsHandleOptions
            {
                LobbyId = CurrentLobbyId,
                LocalUserId = _localProductUserId
            };
            if (_lobbyHandle.CopyLobbyDetailsHandle(copyOptions, out LobbyDetails detailsHandle) != Result.Success || detailsHandle == null)
            {
                return AreAllJoinersReady;
            }

            uint memberCount = detailsHandle.GetMemberCount(new LobbyDetailsGetMemberCountOptions());
            var copyInfoOptions = new LobbyDetailsCopyInfoOptions();
            ProductUserId ownerUserId = null;
            if (detailsHandle.CopyInfo(copyInfoOptions, out LobbyDetailsInfo info) == Result.Success && info != null)
            {
                ownerUserId = info.LobbyOwnerUserId;
            }

            bool allReady = true;
            int joinerCount = 0;

            for (uint i = 0; i < memberCount; i++)
            {
                var memberOptions = new LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
                ProductUserId memberPuid = detailsHandle.GetMemberByIndex(memberOptions);

                if (memberPuid == null || memberPuid == ownerUserId)
                    continue;

                joinerCount++;
                var attrOptions = new LobbyDetailsCopyMemberAttributeByKeyOptions
                {
                    TargetUserId = memberPuid,
                    AttrKey = ReadyKey
                };
                if (detailsHandle.CopyMemberAttributeByKey(attrOptions, out Epic.OnlineServices.Lobby.Attribute attr) == Result.Success && attr?.Data?.Value != null)
                {
                    if (attr.Data.Value.AsUtf8 != "true")
                    {
                        allReady = false;
                    }
                }
                else
                {
                    allReady = false;
                }
            }

            detailsHandle.Release();

            AreAllJoinersReady = (joinerCount == 0) || allReady;
            OnReadyStatusChanged?.Invoke(AreAllJoinersReady);
            return AreAllJoinersReady;
        }

        private static string GenerateShortCode(int length)
        {
            var builder = new StringBuilder(length);
            var random = new System.Random();
            for (int i = 0; i < length; i++)
            {
                builder.Append(CodeCharacters[random.Next(CodeCharacters.Length)]);
            }
            return builder.ToString();
        }

        #region Operation Completion Helpers

        private void CompleteOperation(Action<bool, string> onComplete, bool success, string resultOrError = null)
        {
            if (!success)
            {
                OnLobbyError?.Invoke(resultOrError);
            }

            onComplete?.Invoke(success, resultOrError);
        }

        private void CompleteOperation(Action<bool> onComplete, bool success, string error = null)
        {
            if (!success)
            {
                OnLobbyError?.Invoke(error);
            }

            onComplete?.Invoke(success);
        }

        #endregion

        public void Dispose()
        {
            UnsubscribeFromInviteNotifications();
            LobbyPreferences.ClearInLobby(_localProductUserId);
            UnsubscribeFromNotifications();

            if (IsInLobby)
            {
                LeaveLobby();
            }
        }

    }
}