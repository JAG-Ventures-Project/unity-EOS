using UnityEngine;
using Steamworks;

namespace EOS.Core
{
    // Wraps the Steamworks.NET runtime lifecycle, initializing the native Steam API,
    // handling frame callback dispatching via SteamAPI.RunCallbacks(), and cleanly shutting down on teardown.
    [DisallowMultipleComponent]
    public class SteamManager : MonoBehaviour
    {
        private static SteamManager s_instance;
        private static bool s_everInitialized;

        public static bool Initialized => s_instance != null && s_everInitialized;

        private void Awake()
        {
            if (s_instance != null)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;

            if (!Packsize.Test())
            {
                Debug.LogError("[SteamManager] Packsize Test returned false, the wrong version of Steamworks.NET is being run in this platform.");
            }

            if (!DllCheck.Test())
            {
                Debug.LogError("[SteamManager] DllCheck Test returned false, one or more of the Steamworks binaries seems to be the wrong version.");
            }

            try
            {
                if (SteamAPI.RestartAppIfNecessary(AppId_t.Invalid))
                {
                    Application.Quit();
                    return;
                }
            }
            catch (System.DllNotFoundException e)
            {
                Debug.LogError("[SteamManager] Could not load [lib]steam_api.dll/so/dylib: " + e);
                return;
            }

            s_everInitialized = SteamAPI.Init();
            if (!s_everInitialized)
            {
                Debug.LogError("[SteamManager] SteamAPI.Init() failed. Refer to Valve's documentation or ensure Steam client is running.");
            }
            else
            {
                Debug.Log($"<color=green>[SteamManager] Steamworks initialized successfully! Logged in as: {SteamFriends.GetPersonaName()}</color>");
            }
        }

        private void Update()
        {
            if (s_everInitialized)
            {
                SteamAPI.RunCallbacks();
            }
        }

        private void OnDestroy()
        {
            if (s_instance != this) return;

            s_instance = null;
            if (s_everInitialized)
            {
                SteamAPI.Shutdown();
            }
        }
    }
}