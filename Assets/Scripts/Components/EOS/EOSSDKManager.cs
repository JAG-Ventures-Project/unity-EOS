using System;
using Epic.OnlineServices;
using Epic.OnlineServices.Platform;
using UnityEngine;
using UnityEngine.Assertions;
using Steamworks;

namespace EOS
{
    /// <summary>
    /// MonoBehaviour runner that hosts EOSPlatformService within Unity's lifecycle.
    /// Handles frame updates (Tick), scene persistence, and application quit shutdown.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(SteamManager))]
    public class EOSSDKManager : MonoBehaviour, IEOSSDKManager
    {
        [SerializeField] private EosApiKey eosApiKey;

        public static EOSSDKManager Instance { get; private set; }

        public static HAuthTicket SteamAuthTicket { get; set; } = HAuthTicket.Invalid;
        public static string LocalDisplayName { get; set; } = "Player";

        public EosApiKey EosApiKey
        {
            get => eosApiKey;
            set => eosApiKey = value;
        }

        private EOSPlatformService _platformService;

        public EOSPlatformService Service => _platformService;
        public ConnectManager ConnectManager => _platformService?.ConnectManager;
        public AuthManager AuthManager => _platformService?.AuthManager;
        public LobbyManager Lobby => _platformService?.Lobby;
        public PlayerDataStorageManager PlayerDataStorage => _platformService?.PlayerDataStorage;
        public CrossPlayFriendsStore CrossPlayStore => _platformService?.CrossPlayStore;
        public LobbyInviteManager LobbyInvites => _platformService?.LobbyInvites;
        public FriendsManager Friends => _platformService?.Friends;

#if UNITY_EDITOR
        private void OnValidate()
        {
            Assert.IsNotNull(eosApiKey, "[EOSSDKManager] EosApiKey is null! Please assign an EosApiKey ScriptableObject in the inspector.");
        }
#endif

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(transform.root.gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            Application.runInBackground = true;

            _platformService = new EOSPlatformService();
            _platformService.Initialize(eosApiKey);
        }

        private void Update()
        {
            _platformService.Tick();
        }

        private void OnApplicationQuit()
        {
            _platformService.Shutdown();
        }
    }
}
