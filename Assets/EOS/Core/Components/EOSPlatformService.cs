using System;
using System.Runtime.InteropServices;
using Epic.OnlineServices;
using Epic.OnlineServices.Platform;
using UnityEngine;
using UnityEngine.Assertions;
using Unity.Profiling;

namespace EOS.Core
{
    /// <summary>
    /// Initialization steps implemented according to Epic Online Services Unity documentation:
    /// https://dev.epicgames.com/docs/epic-online-services/game-engines/eos-unity-integration/eos-unity-integration-windows
    /// with AI Implementaion,fixes and improvements on top of it
    /// </summary>
    public class EOSPlatformService : IDisposable
    {
        private PlatformInterface _platformInterface;

        public PlatformInterface PlatformInterface => _platformInterface;
        public bool IsInitialized => _platformInterface != null;

        public event Action OnTick;
        public event Action OnPlatformReady;
        public event Action OnBeforeShutdown;

        private static readonly ProfilerMarker s_EosTickMarker = new ProfilerMarker("EOS.PlatformTick");

#if UNITY_EDITOR
        // AI Implementation
        [DllImport("Kernel32.dll")]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        [DllImport("Kernel32.dll")]
        private static extern int FreeLibrary(IntPtr hLibModule);

        [DllImport("Kernel32.dll")]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        private static IntPtr s_libraryPointer = IntPtr.Zero;

        /// <summary>
        /// Resolves the path to the EOS native SDK library inside the Unity project.
        /// </summary>
        public static string ResolveEditorLibraryPath()
        {
            string dllFileName = Config.LibraryName.EndsWith(".dll") ? Config.LibraryName : Config.LibraryName + ".dll";
            string[] candidateRelativePaths =
            {
                "EOS/EpicOnlineTransport/EOSSDK",
                "Mirror/Transports/EpicOnlineTransport/EOSSDK",
                "Mirror/Runtime/Transport/EpicOnlineTransport/EOSSDK"
            };

            foreach (string relativeDir in candidateRelativePaths)
            {
                string candidate = System.IO.Path.Combine(Application.dataPath, relativeDir, dllFileName);
                if (System.IO.File.Exists(candidate))
                {
                    return candidate;
                }
            }

            string[] matches = System.IO.Directory.GetFiles(Application.dataPath, dllFileName, System.IO.SearchOption.AllDirectories);
            if (matches.Length > 0)
            {
                return matches[0];
            }

            return System.IO.Path.Combine(Application.dataPath, candidateRelativePaths[0], dllFileName);
        }

        private static void LoadEditorLibrary()
        {
            if (s_libraryPointer != IntPtr.Zero) return;

            string libraryPath = ResolveEditorLibraryPath();

            s_libraryPointer = LoadLibrary(libraryPath);
            if (s_libraryPointer == IntPtr.Zero)
            {
                throw new Exception("[EOSPlatformService] Failed to load EOS native library: " + libraryPath);
            }

            Bindings.Hook(s_libraryPointer, GetProcAddress);
        }

        private static void UnloadEditorLibrary()
        {
            if (s_libraryPointer != IntPtr.Zero)
            {
                Bindings.Unhook();
                while (FreeLibrary(s_libraryPointer) != 0) { }
                s_libraryPointer = IntPtr.Zero;
            }
        }
#endif

        public void Initialize(EosApiKey apiKey)
        {
            Assert.IsNotNull(apiKey, "[EOSPlatformService] EosApiKey must not be null.");

#if UNITY_EDITOR
            LoadEditorLibrary();
#endif

            Result initResult = InitializePlatform(apiKey);
            bool isAlreadyConfigured = Application.isEditor && initResult == Result.AlreadyConfigured;
            Assert.IsTrue(initResult == Result.Success || isAlreadyConfigured,
                $"[EOSPlatformService] InitializePlatform failed with result: {initResult}");

            _platformInterface = CreatePlatform(apiKey);
            Assert.IsNotNull(_platformInterface, "[EOSPlatformService] CreatePlatformInterface returned null!");

            OnPlatformReady?.Invoke();
        }

        public Result InitializePlatform(EosApiKey apiKey)
        {
            var initOptions = new InitializeOptions
            {
                ProductName = apiKey.epicProductName,
                ProductVersion = apiKey.epicProductVersion,
                AllocateMemoryFunction = IntPtr.Zero,
                ReallocateMemoryFunction = IntPtr.Zero,
                ReleaseMemoryFunction = IntPtr.Zero,
                OverrideThreadAffinity = null
            };

            return PlatformInterface.Initialize(initOptions);
        }

        public PlatformInterface CreatePlatform(EosApiKey apiKey)
        {
            // EOS requires a 64-character hexadecimal (256-bit) encryption key to initialize PlayerDataStorageInterface
            Assert.IsTrue(!string.IsNullOrEmpty(apiKey.encryptionKey) && apiKey.encryptionKey.Length == 64,
                "[EOSPlatformService] EosApiKey.encryptionKey must be a 64-character hexadecimal string! Right-click EosApiKey in the Inspector to generate one.");

            string encryptionKey = apiKey.encryptionKey;

            var platformOptions = new WindowsOptions
            {
                CacheDirectory = Application.temporaryCachePath,
                EncryptionKey = encryptionKey,
                IsServer = false,
                ProductId = apiKey.epicProductId,
                SandboxId = apiKey.epicSandboxId,
                DeploymentId = apiKey.epicDeploymentId,
                TickBudgetInMilliseconds = 0, // 0 = process all available work without artificial time limits
                ClientCredentials = new ClientCredentials
                {
                    ClientId = apiKey.epicClientId,
                    ClientSecret = apiKey.epicClientSecret
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

        public void Tick()
        {
            using (s_EosTickMarker.Auto())
            {
                _platformInterface.Tick();
            }
            OnTick?.Invoke();
        }

        public bool Shutdown()
        {
            try
            {
                OnBeforeShutdown?.Invoke();

                if (_platformInterface != null)
                {
                    _platformInterface.Release();
                    _platformInterface = null;
                }

                Result result = PlatformInterface.Shutdown();

#if UNITY_EDITOR
                UnloadEditorLibrary();
#endif

                return result == Result.Success;
            }
            catch (Exception e)
            {
                Debug.LogError("[EOSPlatformService] Failed to shut down the EOS SDK: " + e);
                return false;
            }
        }

        public void Dispose()
        {
            Shutdown();
        }
    }
}