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
    // Q: is there a good way to require the notification manager here ? , and is it sensible to keep it hooked with the EOSSDKManager Component. or is it ok in it's current state.
    public class EOSSDKManager : MonoBehaviour, IEOSSDKManager
    {
        [SerializeField] private EosApiKey eosApiKey;

        public static EOSSDKManager Instance { get; private set; }

        // can't we make this internal and will break after build 
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