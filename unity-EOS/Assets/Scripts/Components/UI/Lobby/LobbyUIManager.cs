using System;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;
using Epic.OnlineServices;

namespace EOS
{
    [DisallowMultipleComponent]
    public class LobbyUIManager : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject menuPanel;
        [SerializeField] private GameObject inLobbyPanel;
        
        [Header("Menu View Controls")]
        [SerializeField] private Button createLobbyButton;
        [SerializeField] private TMP_InputField joinCodeInputField;
        [SerializeField] private Button joinLobbyButton;
        [SerializeField] private uint maxLobbyMembers = 4;

        [Header("In-Lobby View Controls")]
        [SerializeField] private TextMeshProUGUI roomCodeText;
        [SerializeField] private Button copyCodeButton;
        [SerializeField] private TextMeshProUGUI memberCountText;
        [SerializeField] private Button leaveLobbyButton;
        [SerializeField] private Button startGameButton; 
        
        private ILobby _lobby;
        private int _currentMemberCount;

#if UNITY_EDITOR
        private void OnValidate()
        {
            Assert.IsNotNull(menuPanel, "[LobbyUIManager] menuPanel is null! Please assign in inspector.");
            Assert.IsNotNull(inLobbyPanel, "[LobbyUIManager] inLobbyPanel is null! Please assign in inspector.");
            Assert.IsNotNull(createLobbyButton, "[LobbyUIManager] createLobbyButton is null! Please assign in inspector.");
            Assert.IsNotNull(joinCodeInputField, "[LobbyUIManager] joinCodeInputField is null! Please assign in inspector.");
            Assert.IsNotNull(joinLobbyButton, "[LobbyUIManager] joinLobbyButton is null! Please assign in inspector.");
            Assert.IsNotNull(roomCodeText, "[LobbyUIManager] roomCodeText is null! Please assign in inspector.");
            Assert.IsNotNull(copyCodeButton, "[LobbyUIManager] copyCodeButton is null! Please assign in inspector.");
            Assert.IsNotNull(memberCountText, "[LobbyUIManager] memberCountText is null! Please assign in inspector.");
            Assert.IsNotNull(leaveLobbyButton, "[LobbyUIManager] leaveLobbyButton is null! Please assign in inspector.");
            Assert.IsNotNull(startGameButton, "[LobbyUIManager] startGameButton is null! Please assign in inspector.");
        }
#endif
        
        private void Awake()
        {
            Assert.IsNotNull(EOSSDKManager.Instance, "[LobbyUIManager] EOSSDKManager.Instance is null!");
            
            _lobby = new LobbyManager();
            Assert.IsNotNull(_lobby, "[LobbyUIManager] Failed to instantiate LobbyManager.");
            
            SetPanelState(isInLobby: false);
            SetMenuButtonsInteractable(true);
            
            createLobbyButton.onClick.AddListener(OnCreateLobbyButtonClicked);
            joinLobbyButton.onClick.AddListener(OnJoinLobbyButtonClicked);
            copyCodeButton.onClick.AddListener(OnCopyCodeButtonClicked);
            leaveLobbyButton.onClick.AddListener(OnLeaveLobbyButtonClicked);
            startGameButton.onClick.AddListener(OnStartGameButtonClicked);

            _lobby.OnLobbyCreated += HandleOnLobbyCreated;
            _lobby.OnLobbyJoined += HandleOnLobbyJoined;
            _lobby.OnLobbyLeft += HandleOnLobbyLeft;
            _lobby.OnPlayerJoined += HandleOnPlayerJoined;
            _lobby.OnPlayerLeft += HandleOnPlayerLeft;
            _lobby.OnLobbyError += HandleOnLobbyError;
        }
        
        private void OnDestroy()
        {
            _lobby.OnLobbyCreated -= HandleOnLobbyCreated;
            _lobby.OnLobbyJoined -= HandleOnLobbyJoined;
            _lobby.OnLobbyLeft -= HandleOnLobbyLeft;
            _lobby.OnPlayerJoined -= HandleOnPlayerJoined;
            _lobby.OnPlayerLeft -= HandleOnPlayerLeft;
            _lobby.OnLobbyError -= HandleOnLobbyError;
            _lobby.Dispose();
        }
        
        // --- Button Click Callbacks ---
        private void OnCreateLobbyButtonClicked()
        {
            SetMenuButtonsInteractable(false);
            _lobby.CreateLobby(maxLobbyMembers);
        }
        
        private void OnJoinLobbyButtonClicked()
        {
            string code = joinCodeInputField.text;
            if (string.IsNullOrWhiteSpace(code))
            {
                NotificationManager.Instance?.ShowNotification("Please enter a valid room code.", NotificationType.Warning);
                return;
            }
            SetMenuButtonsInteractable(false);
            _lobby.JoinLobbyByCode(code);
        }
        
        private void OnCopyCodeButtonClicked()
        {
            if (string.IsNullOrEmpty(_lobby.CurrentJoinCode)) return;
            // Copy room code to player's system clipboard
            GUIUtility.systemCopyBuffer = _lobby.CurrentJoinCode;
            NotificationManager.Instance?.ShowNotification($"Code copied: {_lobby.CurrentJoinCode}", NotificationType.Normal);
        }
        
        private void OnLeaveLobbyButtonClicked()
        {
            leaveLobbyButton.interactable = false;
            _lobby.LeaveLobby();
        }
        
        private void OnStartGameButtonClicked()
        {
            // Host clicked Start Game: Trigger scene transition or server gameplay start
            NotificationManager.Instance?.ShowNotification("Starting match...", NotificationType.Normal);
        }
        
        // --- ILobby Event Handlers ---
        private void HandleOnLobbyCreated(string shortCode)
        {
            _currentMemberCount = 1;
            roomCodeText.text = shortCode;
            UpdateMemberCountDisplay();
            if (startGameButton != null)
            {
                startGameButton.gameObject.SetActive(true);
            }
            SetPanelState(isInLobby: true);
            // Transport Host Startup:
            // EOSNetworkManagerBridge.Instance.StartEosHost();
        }
        
        private void HandleOnLobbyJoined(string hostAddress)
        {
            _currentMemberCount = 2; // Incremented as members are received
            roomCodeText.text = _lobby.CurrentJoinCode;
            UpdateMemberCountDisplay();
            if (startGameButton != null)
            {
                startGameButton.gameObject.SetActive(false); // Non-hosts cannot start match
            }
            SetPanelState(isInLobby: true);
            // Transport Client Startup using Host's ProductUserId:
            // EOSNetworkManagerBridge.Instance.StartEosClient(hostAddress);
        }
        
        private void HandleOnLobbyLeft()
        {
            SetPanelState(isInLobby: false);
            SetMenuButtonsInteractable(true);
            leaveLobbyButton.interactable = true;
            joinCodeInputField.text = string.Empty;
            // Transport Teardown:
            // EOSNetworkManagerBridge.Instance.StopNetwork();
        }
        
        private void HandleOnPlayerJoined(ProductUserId userId)
        {
            _currentMemberCount++;
            UpdateMemberCountDisplay();
            NotificationManager.Instance?.ShowNotification("A player joined the room.", NotificationType.Normal);
        }
        
        private void HandleOnPlayerLeft(ProductUserId userId)
        {
            _currentMemberCount = Mathf.Max(1, _currentMemberCount - 1);
            UpdateMemberCountDisplay();
            NotificationManager.Instance?.ShowNotification("A player left the room.", NotificationType.Normal);
        }
        
        private void HandleOnLobbyError(string errorMessage)
        {
            SetMenuButtonsInteractable(true);
            leaveLobbyButton.interactable = true;
            Debug.LogError($"[LobbyUIManager] {errorMessage}");
            NotificationManager.Instance?.ShowNotification(errorMessage, NotificationType.Error);
        }
        
        // --- Helper Methods ---
        private void SetPanelState(bool isInLobby)
        {
            menuPanel.SetActive(!isInLobby);
            inLobbyPanel.SetActive(isInLobby);
        }
        
        private void SetMenuButtonsInteractable(bool interactable)
        {
            createLobbyButton.interactable = interactable;
            joinLobbyButton.interactable = interactable;
            joinCodeInputField.interactable = interactable;
        }
        
        private void UpdateMemberCountDisplay()
        {
            if (memberCountText != null)
            {
                memberCountText.text = $"Players: {_currentMemberCount} / {maxLobbyMembers}";
            }
        }

    }
}