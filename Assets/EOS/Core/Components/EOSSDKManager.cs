using UnityEngine;
using EOS.Auth;
using System;
using Epic.OnlineServices.Platform;

namespace EOS.Core
{
    /// <summary>
    /// MonoBehaviour runner that hosts EOSPlatformService within Unity's lifecycle.
    /// Handles frame updates (Tick), scene persistence, and application quit shutdown.
    /// Owns local display name and platform convenience accessors decoupled from EOSPlatformService.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class EOSSDKManager : MonoBehaviour, IEOSSDKManager
    {
        [SerializeField] private EosApiKey eosApiKey;
        [SerializeField] private bool haveAuth = false;

        public static EOSSDKManager Instance { get; private set; }
        public static PlatformInterface PlatformInterface => Instance?.Service?.PlatformInterface;
        public static event Action OnShutdown;

        public static ISteamService SteamService { get; set; } = new NullSteamService();
        public static INetworkSessionService NetworkSession => NetworkService.Current;
        public static string LocalDisplayName { get; set; } = "Player";

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
            if (UnityEditor.EditorUtility.IsPersistent(this))
            {
                return;
            }

            if (eosApiKey == null)
            {
                Debug.LogWarning("[EOSSDKManager] EosApiKey is null! Please assign an EosApiKey ScriptableObject in the inspector.", this);
            }
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
            _platformService.OnBeforeShutdown += HandlePlatformBeforeShutdown;
            if (!haveAuth)
            {
                _platformService.OnPlatformReady += DefaultAuth;
            }
            _platformService.Initialize(eosApiKey);
        }

        private void HandlePlatformBeforeShutdown()
        {
            OnShutdown?.Invoke();
        }

        private void Update()
        {
            _platformService.Tick();
        }

        private void OnApplicationQuit()
        {
            if (SteamService.HasActiveTicket)
            {
                SteamService.CancelTicket();
            }
            _platformService?.Shutdown();
        }

        private void DefaultAuth()
        {
            GuestLoginManager.LoginAsGuest("Guest", true, null);
        }
    }
}