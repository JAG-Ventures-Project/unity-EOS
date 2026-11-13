using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using EOS.Core;
using EOS.Auth;
using EOS.Lobby;
using EOS.Friends;
using EOS.Notification;

namespace EOS.Editor
{
    /// <summary>
    /// Editor utility and menu commands to quickly instantiate and setup EOS systems in any scene.
    /// Supports Core, Auth, P2P Network, Lobby, Friends Overlay, Notification, and Analytics.
    /// </summary>
    public static class EOSSystemSetup
    {
        public const string CorePrefabPath = "Assets/EOS/Core/Prefabs/EOSManager.prefab";
        public const string AuthManagerPrefabPath = "Assets/EOS/Auth/Prefabs/LoginManager.prefab";
        public const string AuthMenuPrefabPath = "Assets/EOS/Auth/Prefabs/Login Menu.prefab";
        public const string P2PPrefabPath = "Assets/EOS/Mirror/P2P/Prefabs/NetworkManager.prefab";
        public const string LobbyManagerPrefabPath = "Assets/EOS/Lobby/Prefabs/LobbyManager.prefab";
        public const string LobbyCanvasPrefabPath = "Assets/EOS/Lobby/Prefabs/Canvas.prefab";
        public const string FriendsCanvasPrefabPath = "Assets/EOS/Friends/Prefabs/Canvas.prefab";
        public const string NotificationManagerPrefabPath = "Assets/EOS/Notification/Prefabs/NotificationManager.prefab";
        public const string AnalyticsManagerPrefabPath = "Assets/EOS/Test/Analytics/Prefabs/AnalyticsManager.prefab";
        public const string AnalyticsCanvasPrefabPath = "Assets/EOS/Test/Analytics/Prefabs/Canvas.prefab";

        #region Status Checkers

        public static bool HasCoreInScene => Object.FindFirstObjectByType<EOSSDKManager>() != null;
        public static bool HasAuthInScene => Object.FindFirstObjectByType<EOSLoginManager>() != null;
        public static bool HasP2PInScene => FindComponentByName("EOSNetworkManager") != null;
        public static bool HasLobbyInScene => Object.FindFirstObjectByType<LobbyUIManager>() != null;
        public static bool HasFriendsInScene => Object.FindFirstObjectByType<FriendsOverlayManager>() != null;
        public static bool HasNotificationInScene => Object.FindFirstObjectByType<NotificationManager>() != null;
        public static bool HasAnalyticsInScene => FindComponentByName("PingAnalyticsManager") != null || FindComponentByName("AnalyticsManager") != null;

        #endregion

        #region Menu Item Commands

        [MenuItem("EOS/Add System to Scene/1. Core (EOSManager)", false, 1)]
        public static GameObject AddCoreSystem()
        {
            if (HasCoreInScene)
            {
                if (!EditorUtility.DisplayDialog("System Already Present", "EOSManager is already present in this scene. Do you want to add another instance anyway?", "Add Duplicate", "Cancel"))
                {
                    Selection.activeGameObject = Object.FindFirstObjectByType<EOSSDKManager>().gameObject;
                    return null;
                }
            }

            GameObject instance = InstantiatePrefabToScene(CorePrefabPath, "Add EOS Core Manager");
            if (instance != null)
            {
                Debug.Log("[EOS Setup] Successfully added Core (EOSManager) to active scene.");
            }
            return instance;
        }

        [MenuItem("EOS/Add System to Scene/2. Auth (Login Menu & LoginManager)", false, 2)]
        public static void AddAuthSystem()
        {
            if (HasAuthInScene)
            {
                if (!EditorUtility.DisplayDialog("System Already Present", "Auth system is already present in this scene. Do you want to add another instance anyway?", "Add Duplicate", "Cancel"))
                {
                    Selection.activeGameObject = Object.FindFirstObjectByType<EOSLoginManager>().gameObject;
                    return;
                }
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Add EOS Auth System");
            int group = Undo.GetCurrentGroup();

            GameObject mgr = InstantiatePrefabToScene(AuthManagerPrefabPath, "Add LoginManager");
            GameObject menu = InstantiatePrefabToScene(AuthMenuPrefabPath, "Add Login Menu");
            EnsureEventSystem();

            Undo.CollapseUndoOperations(group);

            if (mgr != null || menu != null)
            {
                Selection.activeGameObject = menu != null ? menu : mgr;
                Debug.Log("[EOS Setup] Successfully added Auth (Login Menu & LoginManager) to active scene.");
            }
        }

        [MenuItem("EOS/Add System to Scene/3. P2P (NetworkManager)", false, 3)]
        public static GameObject AddP2PSystem()
        {
            if (HasP2PInScene)
            {
                if (!EditorUtility.DisplayDialog("System Already Present", "EOSNetworkManager is already present in this scene. Do you want to add another instance anyway?", "Add Duplicate", "Cancel"))
                {
                    Component existingComp = FindComponentByName("EOSNetworkManager");
                    if (existingComp != null)
                    {
                        Selection.activeGameObject = existingComp.gameObject;
                    }
                    return null;
                }
            }

            GameObject instance = InstantiatePrefabToScene(P2PPrefabPath, "Add EOS NetworkManager");
            if (instance != null)
            {
                Debug.Log("[EOS Setup] Successfully added P2P (NetworkManager) to active scene.");
            }
            return instance;
        }

        [MenuItem("EOS/Add System to Scene/4. Lobby (Canvas & LobbyManager)", false, 4)]
        public static void AddLobbySystem()
        {
            if (HasLobbyInScene)
            {
                if (!EditorUtility.DisplayDialog("System Already Present", "Lobby system is already present in this scene. Do you want to add another instance anyway?", "Add Duplicate", "Cancel"))
                {
                    Selection.activeGameObject = Object.FindFirstObjectByType<LobbyUIManager>().gameObject;
                    return;
                }
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Add EOS Lobby System");
            int group = Undo.GetCurrentGroup();

            GameObject mgr = InstantiatePrefabToScene(LobbyManagerPrefabPath, "Add LobbyManager");
            GameObject canvas = InstantiatePrefabToScene(LobbyCanvasPrefabPath, "Add Lobby Canvas");
            EnsureEventSystem();

            Undo.CollapseUndoOperations(group);

            if (mgr != null || canvas != null)
            {
                Selection.activeGameObject = canvas != null ? canvas : mgr;
                Debug.Log("[EOS Setup] Successfully added Lobby (Canvas & LobbyManager) to active scene.");
            }
        }

        [MenuItem("EOS/Add System to Scene/5. Friends (Friends Overlay Canvas)", false, 5)]
        public static GameObject AddFriendsSystem()
        {
            if (HasFriendsInScene)
            {
                if (!EditorUtility.DisplayDialog("System Already Present", "Friends Overlay is already present in this scene. Do you want to add another instance anyway?", "Add Duplicate", "Cancel"))
                {
                    Selection.activeGameObject = Object.FindFirstObjectByType<FriendsOverlayManager>().gameObject;
                    return null;
                }
            }

            GameObject instance = InstantiatePrefabToScene(FriendsCanvasPrefabPath, "Add Friends Overlay Canvas");
            EnsureEventSystem();

            if (instance != null)
            {
                Debug.Log("[EOS Setup] Successfully added Friends (Friends Overlay Canvas) to active scene.");
            }
            return instance;
        }

        [MenuItem("EOS/Add System to Scene/6. Notification (NotificationManager)", false, 6)]
        public static GameObject AddNotificationSystem()
        {
            if (HasNotificationInScene)
            {
                if (!EditorUtility.DisplayDialog("System Already Present", "NotificationManager is already present in this scene. Do you want to add another instance anyway?", "Add Duplicate", "Cancel"))
                {
                    Selection.activeGameObject = Object.FindFirstObjectByType<NotificationManager>().gameObject;
                    return null;
                }
            }

            GameObject instance = InstantiatePrefabToScene(NotificationManagerPrefabPath, "Add NotificationManager");
            if (instance != null)
            {
                Debug.Log("[EOS Setup] Successfully added Notification (NotificationManager) to active scene.");
            }
            return instance;
        }

        [MenuItem("EOS/Add System to Scene/7. Analytics (AnalyticsManager & Canvas)", false, 7)]
        public static void AddAnalyticsSystem()
        {
            if (HasAnalyticsInScene)
            {
                if (!EditorUtility.DisplayDialog("System Already Present", "Analytics system is already present in this scene. Do you want to add another instance anyway?", "Add Duplicate", "Cancel"))
                {
                    return;
                }
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Add EOS Analytics System");
            int group = Undo.GetCurrentGroup();

            GameObject mgr = InstantiatePrefabToScene(AnalyticsManagerPrefabPath, "Add AnalyticsManager");
            GameObject canvas = InstantiatePrefabToScene(AnalyticsCanvasPrefabPath, "Add Analytics Canvas");
            EnsureEventSystem();

            Undo.CollapseUndoOperations(group);

            if (mgr != null || canvas != null)
            {
                Selection.activeGameObject = canvas != null ? canvas : mgr;
                Debug.Log("[EOS Setup] Successfully added Analytics (AnalyticsManager & Canvas) to active scene.");
            }
        }

        [MenuItem("EOS/Add System to Scene/Add All Systems", false, 20)]
        public static void AddAllSystems()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Add All EOS Systems");
            int group = Undo.GetCurrentGroup();

            if (!HasCoreInScene) InstantiatePrefabToScene(CorePrefabPath, "Add Core");
            if (!HasP2PInScene) InstantiatePrefabToScene(P2PPrefabPath, "Add P2P");
            if (!HasAuthInScene)
            {
                InstantiatePrefabToScene(AuthManagerPrefabPath, "Add Auth Manager");
                InstantiatePrefabToScene(AuthMenuPrefabPath, "Add Auth Menu");
            }
            if (!HasLobbyInScene)
            {
                InstantiatePrefabToScene(LobbyManagerPrefabPath, "Add Lobby Manager");
                InstantiatePrefabToScene(LobbyCanvasPrefabPath, "Add Lobby Canvas");
            }
            if (!HasFriendsInScene) InstantiatePrefabToScene(FriendsCanvasPrefabPath, "Add Friends Overlay");
            if (!HasNotificationInScene) InstantiatePrefabToScene(NotificationManagerPrefabPath, "Add Notification Manager");
            if (!HasAnalyticsInScene)
            {
                InstantiatePrefabToScene(AnalyticsManagerPrefabPath, "Add Analytics Manager");
                InstantiatePrefabToScene(AnalyticsCanvasPrefabPath, "Add Analytics Canvas");
            }

            EnsureEventSystem();
            Undo.CollapseUndoOperations(group);

            Debug.Log("[EOS Setup] Finished adding missing EOS systems to the active scene.");
        }

        [MenuItem("EOS/Quick Setup Window", false, 0)]
        public static void OpenSetupWindow()
        {
            EOSSystemSetupWindow.ShowWindow();
        }

        #endregion

        #region Helpers

        public static GameObject InstantiatePrefabToScene(string assetPath, string undoName)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                Debug.LogError($"[EOS Setup] Could not find prefab at: {assetPath}");
                return null;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance != null)
            {
                Undo.RegisterCreatedObjectUndo(instance, undoName);
                StageUtility.PlaceGameObjectInCurrentStage(instance);
                GameObjectUtility.EnsureUniqueNameForSibling(instance);
                Selection.activeGameObject = instance;
                EditorSceneManager.MarkSceneDirty(instance.scene);
            }
            return instance;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var eventSystemGo = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
                eventSystemGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                eventSystemGo.AddComponent<StandaloneInputModule>();
#endif
                Undo.RegisterCreatedObjectUndo(eventSystemGo, "Create EventSystem");
                StageUtility.PlaceGameObjectInCurrentStage(eventSystemGo);
                Debug.Log("[EOS Setup] Created EventSystem for UI interaction.");
            }
        }

        public static Component FindComponentByName(string typeName)
        {
            foreach (var comp in Object.FindObjectsByType<Component>(FindObjectsSortMode.None))
            {
                if (comp != null && comp.GetType().Name == typeName)
                {
                    return comp;
                }
            }
            return null;
        }

        #endregion
    }

    /// <summary>
    /// Interactive editor window displaying visual cards and one-click install actions for all EOS systems.
    /// </summary>
    public class EOSSystemSetupWindow : EditorWindow
    {
        private Vector2 _scrollPos;

        public static void ShowWindow()
        {
            var window = GetWindow<EOSSystemSetupWindow>("EOS Setup", true);
            window.minSize = new Vector2(460, 520);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("EOS System Setup & Quick Installer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Quickly add, inspect, and configure modular EOS systems in the active scene.", MessageType.Info);
            EditorGUILayout.Space(6);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawSystemCard(
                title: "1. Core System",
                description: "Initializes Epic Online Services SDK and cloud PlayerData storage.",
                prefabs: "EOSManager.prefab",
                isInScene: EOSSystemSetup.HasCoreInScene,
                onAdd: () => EOSSystemSetup.AddCoreSystem(),
                onSelect: () => SelectObjectInScene<EOSSDKManager>()
            );

            DrawSystemCard(
                title: "2. Auth System",
                description: "Player authentication, persistent credentials, and login UI menu.",
                prefabs: "Login Menu.prefab, LoginManager.prefab",
                isInScene: EOSSystemSetup.HasAuthInScene,
                onAdd: () => EOSSystemSetup.AddAuthSystem(),
                onSelect: () => SelectObjectInScene<EOSLoginManager>()
            );

            DrawSystemCard(
                title: "3. P2P Network",
                description: "Mirror networking over EOS P2P transport with authoritative host/client routing.",
                prefabs: "NetworkManager.prefab",
                isInScene: EOSSystemSetup.HasP2PInScene,
                onAdd: () => EOSSystemSetup.AddP2PSystem(),
                onSelect: () => SelectObjectByName("EOSNetworkManager")
            );

            DrawSystemCard(
                title: "4. Lobby System",
                description: "Room matchmaking, 5-character join codes, invite handling, and lobby UI.",
                prefabs: "Canvas.prefab (Lobby), LobbyManager.prefab",
                isInScene: EOSSystemSetup.HasLobbyInScene,
                onAdd: () => EOSSystemSetup.AddLobbySystem(),
                onSelect: () => SelectObjectInScene<LobbyUIManager>()
            );

            DrawSystemCard(
                title: "5. Friends System",
                description: "Cross-platform cloud friends, native platform sync, invites, and overlay UI.",
                prefabs: "Canvas.prefab (Friends Overlay)",
                isInScene: EOSSystemSetup.HasFriendsInScene,
                onAdd: () => EOSSystemSetup.AddFriendsSystem(),
                onSelect: () => SelectObjectInScene<FriendsOverlayManager>()
            );

            DrawSystemCard(
                title: "6. Notification System",
                description: "On-screen toast notification dispatcher for invites, accepts, and game alerts.",
                prefabs: "NotificationManager.prefab",
                isInScene: EOSSystemSetup.HasNotificationInScene,
                onAdd: () => EOSSystemSetup.AddNotificationSystem(),
                onSelect: () => SelectObjectInScene<NotificationManager>()
            );

            DrawSystemCard(
                title: "7. Analytics System",
                description: "Real-time network ping telemetry, packet diagnostics, and analytics overlay.",
                prefabs: "AnalyticsManager.prefab, Canvas.prefab (Analytics)",
                isInScene: EOSSystemSetup.HasAnalyticsInScene,
                onAdd: () => EOSSystemSetup.AddAnalyticsSystem(),
                onSelect: () => SelectObjectByName("AnalyticsManager")
            );

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add All Missing Systems", GUILayout.Height(30)))
            {
                EOSSystemSetup.AddAllSystems();
            }
            if (GUILayout.Button("Ensure EventSystem", GUILayout.Height(30), GUILayout.Width(140)))
            {
                EOSSystemSetup.EnsureEventSystem();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);
        }

        private void DrawSystemCard(string title, string description, string prefabs, bool isInScene, System.Action onAdd, System.Action onSelect)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            if (isInScene)
            {
                GUI.color = new Color(0.3f, 1f, 0.4f);
                EditorGUILayout.LabelField("✓ In Scene", GUILayout.Width(75));
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = Color.gray;
                EditorGUILayout.LabelField("○ Missing", GUILayout.Width(75));
                GUI.color = Color.white;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField($"Prefabs: {prefabs}", EditorStyles.miniLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(isInScene ? "Add Another Instance" : "Add to Scene", GUILayout.Height(22)))
            {
                onAdd?.Invoke();
            }

            if (isInScene)
            {
                if (GUILayout.Button("Select", GUILayout.Width(70), GUILayout.Height(22)))
                {
                    onSelect?.Invoke();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        private void SelectObjectInScene<T>() where T : Component
        {
            T comp = Object.FindFirstObjectByType<T>();
            if (comp != null)
            {
                Selection.activeGameObject = comp.gameObject;
                EditorGUIUtility.PingObject(comp.gameObject);
            }
        }

        private static void SelectObjectByName(string typeName)
        {
            Component comp = EOSSystemSetup.FindComponentByName(typeName);
            if (comp != null)
            {
                Selection.activeGameObject = comp.gameObject;
                EditorGUIUtility.PingObject(comp.gameObject);
            }
        }
    }
}