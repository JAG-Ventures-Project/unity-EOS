using UnityEngine;
using UnityEngine.Assertions;
using Steamworks;

namespace EOS.Core
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