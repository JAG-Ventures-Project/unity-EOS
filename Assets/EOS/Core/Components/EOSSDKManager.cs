using UnityEngine;
using UnityEngine.Assertions;
using EOS.Auth;
using System;

namespace EOS.Core
{
    /// <summary>
    /// MonoBehaviour runner that hosts EOSPlatformService within Unity's lifecycle.
    /// Handles frame updates (Tick), scene persistence, and application quit shutdown.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class EOSSDKManager : MonoBehaviour, IEOSSDKManager
    {
        [SerializeField] private EosApiKey eosApiKey;
        [SerializeField] private bool haveAuth = false;

        public static EOSSDKManager Instance { get; private set; }

        public static ISteamService SteamService { get; set; } = new NullSteamService();
        public static INetworkSessionService NetworkSession => NetworkService.Current;
        public static string LocalDisplayName
        {
            get => EpicTransport.EOSSDKComponent.DisplayName;
            set => EpicTransport.EOSSDKComponent.DisplayName = value;
        }

        public EosApiKey EosApiKey
        {
            get => eosApiKey;
            set => eosApiKey = value;
        }

        private EOSPlatformService _platformService;

        public EOSPlatformService Service => _platformService;

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
            if (!haveAuth)
            {
                _platformService.OnPlatformReady += DefaultAuth;
            }
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

        private void DefaultAuth()
        {
            GuestLoginManager.LoginAsGuest("Guest", true, null);
        }
    }
}