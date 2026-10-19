using System;
using System.Text;
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
        
        public string CurrentLobbyId { get; private set; }
        public string CurrentJoinCode { get; private set; }
        public bool IsInLobby => !string.IsNullOrEmpty(CurrentLobbyId);
        public bool IsLobbyOwner { get; private set; }
        
        public event Action<string> OnLobbyCreated;
        public event Action<string> OnLobbyJoined;
        public event Action OnLobbyLeft;
        public event Action<ProductUserId> OnPlayerJoined;
        public event Action<ProductUserId> OnPlayerLeft;
        public event Action<string> OnLobbyError;
        
        private const string BucketId = "GameLobby";
        private const string CodeCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        
        private readonly PlatformInterface _platformHandle;
        private readonly LobbyInterface _lobbyHandle;
        private readonly ProductUserId _localProductUserId;
        
        private ulong _memberStatusNotificationId = Common.InvalidNotificationid;
        

        public LobbyManager() {
            Assert.IsNotNull(EOSSDKManager.Instance, "[LobbyManager] EOSSDKManager.Instance is null!");
            
            _platformHandle = EOSSDKManager.Instance.Service.PlatformInterface;
            Assert.IsNotNull(_platformHandle, "[LobbyManager] PlatformInterface must not be null.");
            
            _lobbyHandle = _platformHandle.GetLobbyInterface();
            Assert.IsNotNull(_lobbyHandle, "[LobbyManager] LobbyInterface failed to initialize.");
            
            _localProductUserId = EOSSDKManager.Instance.Service.GetLocalProductUserId();
            Assert.IsNotNull(_localProductUserId, "[LobbyManager] LocalProductUserId is null! Ensure player is logged in via ConnectManager before creating/joining lobbies.");
            
            SubscribeToMemberStatusNotifications();
        }

        /// <summary>
        /// Create a new lobby. Upon successful creation, the local player will automatically join the lobby as its owner.
        /// </summary>
        public void CreateLobby(uint maxMembers, Action<bool, string> onComplete = null)
        {
            Assert.IsTrue(!IsInLobby, "[LobbyManager] Cannot create lobby: Player is already in an active lobby.");
            var createOptions = new CreateLobbyOptions
            {
                LocalUserId = _localProductUserId,
                MaxLobbyMembers = maxMembers,
                PermissionLevel = LobbyPermissionLevel.Publicadvertised,
                PresenceEnabled = false, // Not using EAS social overlay
                BucketId = BucketId
            };
            _lobbyHandle.CreateLobby(createOptions, onComplete, HandleCreateLobbyCallback);
        }

        private void HandleCreateLobbyCallback(CreateLobbyCallbackInfo data)
        {
            var onComplete = data.ClientData as Action<bool, string>;
            if (data.ResultCode != Result.Success)
            {
                string error = $"CreateLobby failed: {data.ResultCode}";
                OnLobbyError?.Invoke(error);
                onComplete?.Invoke(false, error);
                return;
            }
            CurrentLobbyId = data.LobbyId;
            CurrentJoinCode = GenerateShortCode(5);
            IsLobbyOwner = true;
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
                string error = $"UpdateLobbyModification failed: {result}";
                OnLobbyError?.Invoke(error);
                onComplete?.Invoke(false, error);
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
            var updateOptions = new UpdateLobbyOptions { LobbyModificationHandle = modHandle };
            _lobbyHandle.UpdateLobby(updateOptions, onComplete, HandleUpdateLobbyCallback);
            
            modHandle.Release();
        }
        
        private void HandleUpdateLobbyCallback(UpdateLobbyCallbackInfo data)
        {
            var onComplete = data.ClientData as Action<bool, string>;
            if (data.ResultCode != Result.Success)
            {
                string error = $"Setting lobby attributes failed: {data.ResultCode}";
                OnLobbyError?.Invoke(error);
                onComplete?.Invoke(false, error);
                return;
            }
            OnLobbyCreated?.Invoke(CurrentJoinCode);
            onComplete?.Invoke(true, CurrentJoinCode);
        }
        
        /// <summary>
        /// Searches for a lobby matching the given short code and joins it.
        /// </summary>
        public void JoinLobbyByCode(string joinCode, Action<bool, string> onComplete = null)
        {
            string formattedCode = joinCode.Trim().ToUpperInvariant();
            var searchOptions = new CreateLobbySearchOptions { MaxResults = 1 };
            Result result = _lobbyHandle.CreateLobbySearch(searchOptions, out LobbySearch searchHandle);
            if (result != Result.Success)
            {
                string error = $"CreateLobbySearch failed: {result}";
                OnLobbyError?.Invoke(error);
                onComplete?.Invoke(false, error);
                return;
            }
            // Filter search by JOIN_CODE
            var paramOptions = new LobbySearchSetParameterOptions
            {
                Parameter = new AttributeData { Key = JoinCodeKey, Value = formattedCode },
                ComparisonOp = ComparisonOp.Equal
            };
            searchHandle.SetParameter(paramOptions);
            var findOptions = new LobbySearchFindOptions { LocalUserId = _localProductUserId };
            searchHandle.Find(findOptions, (searchHandle, onComplete, formattedCode), HandleFindLobbiesCallback);
        }
        
        private void HandleFindLobbiesCallback(LobbySearchFindCallbackInfo data)
        {
            var (searchHandle, onComplete, joinCode) = ((LobbySearch, Action<bool, string>, string))data.ClientData;
            if (data.ResultCode != Result.Success)
            {
                searchHandle.Release();
                string error = $"Search failed: {data.ResultCode}";
                OnLobbyError?.Invoke(error);
                onComplete?.Invoke(false, error);
                return;
            }
            var countOptions = new LobbySearchGetSearchResultCountOptions();
            uint count = searchHandle.GetSearchResultCount(countOptions);
            if (count == 0)
            {
                searchHandle.Release();
                string error = "Lobby not found. Please verify the room code.";
                OnLobbyError?.Invoke(error);
                onComplete?.Invoke(false, error);
                return;
            }
            var copyOptions = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = 0 };
            Result copyResult = searchHandle.CopySearchResultByIndex(copyOptions, out LobbyDetails detailsHandle);
            searchHandle.Release();
            if (copyResult != Result.Success)
            {
                string error = $"Failed to copy lobby search details: {copyResult}";
                OnLobbyError?.Invoke(error);
                onComplete?.Invoke(false, error);
                return;
            }
            // get host address from details
            var attrOptions = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = HostAddressKey };
            string hostAddress = string.Empty;
            if (detailsHandle.CopyAttributeByKey(attrOptions, out Epic.OnlineServices.Lobby.Attribute hostAttr) == Result.Success && hostAttr?.Data?.Value != null)
            {
                hostAddress = hostAttr.Data.Value.AsUtf8;
            }
            // join the lobby
            var joinOptions = new JoinLobbyOptions
            {
                LobbyDetailsHandle = detailsHandle,
                LocalUserId = _localProductUserId,
                PresenceEnabled = false
            };
            _lobbyHandle.JoinLobby(joinOptions, (onComplete, joinCode, hostAddress), HandleJoinLobbyCallback);
            detailsHandle.Release();
        }
        
        private void HandleJoinLobbyCallback(JoinLobbyCallbackInfo data)
        {
            var (onComplete, joinCode, hostAddress) = ((Action<bool, string>, string, string))data.ClientData;
            if (data.ResultCode != Result.Success)
            {
                string error = $"JoinLobby failed: {data.ResultCode}";
                OnLobbyError?.Invoke(error);
                onComplete?.Invoke(false, error);
                return;
            }
            CurrentLobbyId = data.LobbyId;
            CurrentJoinCode = joinCode;
            IsLobbyOwner = false;
            OnLobbyJoined?.Invoke(hostAddress);
            onComplete?.Invoke(true, hostAddress);
        }

        /// <summary>
        /// Leaves or destroys the current lobby depending on whether the local player is host.
        /// </summary>
        public void LeaveLobby(Action<bool> onComplete = null)
        {
            if (!IsInLobby)
            {
                onComplete?.Invoke(true);
                return;
            }
            string lobbyId = CurrentLobbyId;
            ResetState();
            if (IsLobbyOwner)
            {
                var destroyOptions = new DestroyLobbyOptions { LobbyId = lobbyId, LocalUserId = _localProductUserId };
                _lobbyHandle.DestroyLobby(destroyOptions, onComplete, HandleDestroyLobbyCallback);
            }
            else
            {
                var leaveOptions = new LeaveLobbyOptions { LobbyId = lobbyId, LocalUserId = _localProductUserId };
                _lobbyHandle.LeaveLobby(leaveOptions, onComplete, HandleLeaveLobbyCallback);
            }
        }
        
        private void HandleDestroyLobbyCallback(DestroyLobbyCallbackInfo data)
        {
            var onComplete = data.ClientData as Action<bool>;
            OnLobbyLeft?.Invoke();
            onComplete?.Invoke(data.ResultCode == Result.Success);
        }
        
        private void HandleLeaveLobbyCallback(LeaveLobbyCallbackInfo data)
        {
            var onComplete = data.ClientData as Action<bool>;
            OnLobbyLeft?.Invoke();
            onComplete?.Invoke(data.ResultCode == Result.Success);
        }

        private void ResetState()
        {
            CurrentLobbyId = null;
            CurrentJoinCode = null;
            IsLobbyOwner = false;
        }
        
        private void SubscribeToMemberStatusNotifications()
        {
            var options = new AddNotifyLobbyMemberStatusReceivedOptions();
            _memberStatusNotificationId = _lobbyHandle.AddNotifyLobbyMemberStatusReceived(
                options, this, OnMemberStatusChanged);
        }
        
        private static void OnMemberStatusChanged(LobbyMemberStatusReceivedCallbackInfo data)
        {
            var manager = data.ClientData as LobbyManager;
            if (manager == null) return;
            switch (data.CurrentStatus)
            {
                case LobbyMemberStatus.Joined:
                    manager.OnPlayerJoined?.Invoke(data.TargetUserId);
                    break;
                case LobbyMemberStatus.Left:
                case LobbyMemberStatus.Disconnected:
                case LobbyMemberStatus.Kicked:
                    manager.OnPlayerLeft?.Invoke(data.TargetUserId);
                    break;
                case LobbyMemberStatus.Closed:
                    // Host closed/left the lobby
                    if (!manager.IsLobbyOwner)
                    {
                        manager.ResetState();
                        manager.OnLobbyLeft?.Invoke();
                    }
                    break;
            }
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
        
        public void Dispose()
        {
            if (_memberStatusNotificationId != Common.InvalidNotificationid)
            {
                _lobbyHandle.RemoveNotifyLobbyMemberStatusReceived(_memberStatusNotificationId);
                _memberStatusNotificationId = Common.InvalidNotificationid;
            }
        }
    
    }
}