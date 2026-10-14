using System;
using System.Runtime.InteropServices;
using Epic.OnlineServices;
using Epic.OnlineServices.Platform;
using UnityEngine;
using UnityEngine.Assertions;
using Steamworks;

namespace EOS
{
    /// <summary>
    /// Initialization steps implemented according to Epic Online Services Unity documentation:
    /// https://dev.epicgames.com/docs/epic-online-services/game-engines/eos-unity-integration/eos-unity-integration-windows
    /// with AI Implementaion and fixes on top of it
    /// </summary>
    [RequireComponent(typeof(SteamManager))]
    public class EOSSDKManager : MonoBehaviour, I_EOSSDKManager
    {
        [SerializeField] private EosApiKey eosApiKey;

        public static EOSSDKManager Instance { get; private set; }
        // can't we make this internal and will break after build 
        public static HAuthTicket SteamAuthTicket { get; set; } = HAuthTicket.Invalid;
        public EosApiKey EosApiKey
        {
            get => eosApiKey;
            set => eosApiKey = value;
        }

        private static PlatformInterface s_eosPlatformInterface;
        private ConnectManager _connectManager;
        private AuthManager _authManager;
        

        // we have to load the C libs manually in the editor because unity can't compile them in the run time . but they are ok in the build
    #if UNITY_EDITOR
        // AI Implementation 
        [DllImport("Kernel32.dll")]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        [DllImport("Kernel32.dll")]
        private static extern int FreeLibrary(IntPtr hLibModule);

        [DllImport("Kernel32.dll")]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        private static IntPtr s_libraryPointer = IntPtr.Zero;

        private static void LoadEditorLibrary()
        {
            if (s_libraryPointer != IntPtr.Zero) return;

            string dllFileName = Config.LibraryName.EndsWith(".dll") ? Config.LibraryName : Config.LibraryName + ".dll";
            string libraryPath = System.IO.Path.Combine(Application.dataPath, "Mirror/Transports/EpicOnlineTransport/EOSSDK", dllFileName);

            if (!System.IO.File.Exists(libraryPath))
            {
                libraryPath = System.IO.Path.Combine(Application.dataPath, "Mirror/Runtime/Transport/EpicOnlineTransport/EOSSDK", dllFileName);
            }

            s_libraryPointer = LoadLibrary(libraryPath);
            if (s_libraryPointer == IntPtr.Zero)
            {
                throw new Exception("[EOSSDKManager] Failed to load EOS native library: " + libraryPath);
            }

            Bindings.Hook(s_libraryPointer, GetProcAddress);
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
                Destroy(transform.root.gameObject);
                return;
            }

            Assert.IsNotNull(eosApiKey, "[EOSSDKManager] EosApiKey is null! Please assign an EosApiKey ScriptableObject in the inspector.");

    #if UNITY_EDITOR
            LoadEditorLibrary();
    #endif

            var initResult = InitializePlatformInterface();
            var isAlreadyConfigured = Application.isEditor && initResult == Result.AlreadyConfigured;
            Assert.IsTrue(initResult == Result.Success || isAlreadyConfigured,
                $"[EOSSDKManager] InitializePlatformInterface failed with result: {initResult}");

            s_eosPlatformInterface = CreatePlatformInterface();
            Assert.IsNotNull(s_eosPlatformInterface, "[EOSSDKManager] CreatePlatformInterface returned null!");

            _connectManager = new ConnectManager(s_eosPlatformInterface);
            _authManager = new AuthManager(s_eosPlatformInterface);
        }
        
        private void Update()
        {
            s_eosPlatformInterface.Tick();
        }
        
        public PlatformInterface GetEOSPlatformInterface()
        {
            return s_eosPlatformInterface;
        }

        public Result InitializePlatformInterface()
        {
            var initOptions = new InitializeOptions
            {
                ProductName = eosApiKey.epicProductName,
                ProductVersion = eosApiKey.epicProductVersion,
                AllocateMemoryFunction = IntPtr.Zero,
                ReallocateMemoryFunction = IntPtr.Zero,
                ReleaseMemoryFunction = IntPtr.Zero
            };

            var overrideThreadAffinity = new InitializeThreadAffinity
            {
                NetworkWork = 0,
                StorageIo = 0,
                WebSocketIo = 0,
                P2PIo = 0,
                HttpRequestIo = 0
            };

            initOptions.OverrideThreadAffinity = overrideThreadAffinity;

            return PlatformInterface.Initialize(initOptions);
        }

        public PlatformInterface CreatePlatformInterface()
        {
            var platformOptions = new WindowsOptions
            {
                CacheDirectory = Application.temporaryCachePath,
                IsServer = false,
                ProductId = eosApiKey.epicProductId,
                SandboxId = eosApiKey.epicSandboxId,
                DeploymentId = eosApiKey.epicDeploymentId,
                TickBudgetInMilliseconds = 16, // 16ms is almost 60fps , we will change it later to be the current fps
                ClientCredentials = new ClientCredentials
                {
                    ClientId = eosApiKey.epicClientId,
                    ClientSecret = eosApiKey.epicClientSecret
                },
                Flags =
    #if UNITY_EDITOR
                    PlatformFlags.LoadingInEditor
    #else
                    PlatformFlags.None
    #endif
            };

            return PlatformInterface.Create(platformOptions);
        }

        public bool ShutDownEOSSDK()
        {
            try
            {
                // cancel active steam ticket
                if (SteamAuthTicket != HAuthTicket.Invalid && SteamAPI.IsSteamRunning())
                {
                    SteamUser.CancelAuthTicket(SteamAuthTicket);
                    SteamAuthTicket = HAuthTicket.Invalid;   
                }
                
                s_eosPlatformInterface.Release();
                s_eosPlatformInterface = null;

                var result = PlatformInterface.Shutdown();

    #if UNITY_EDITOR
                // AI Implementation - for freeing the C libs
                if (s_libraryPointer != IntPtr.Zero)
                {
                    Bindings.Unhook();
                    while (FreeLibrary(s_libraryPointer) != 0) { }
                    s_libraryPointer = IntPtr.Zero;
                }
    #endif

                return result == Result.Success;
            }
            catch (Exception e)
            {
                Debug.LogError("[EOSSDKManager] Failed to shut down the EOS SDK: " + e);
                return false;
            }
        }

        private void OnApplicationQuit()
        {
            ShutDownEOSSDK();
        }


        public ConnectManager ConnectManager
        {
            get => _connectManager;
        }

        public AuthManager AuthManager
        {
            get => _authManager;
        }
    }
}