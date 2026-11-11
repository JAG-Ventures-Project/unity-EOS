using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace EOS
{
    [DisallowMultipleComponent]
    public class MenuManager : MonoBehaviour
    {
        [Header("UI Controls")]
        [SerializeField] private Button startGameButton;

        [Header("Scene Settings")]
        [SerializeField, HideInInspector] private string gameSceneName = "LoginScene";


#if UNITY_EDITOR
        [SerializeField] private UnityEditor.SceneAsset gameScene;

        private void OnValidate()
        {
            Assert.IsNotNull(startGameButton, "[MenuManager] startGameButton is null! Please assign in inspector.");
            Assert.IsNotNull(gameScene, "[MenuManager] gameScene is null! Please assign in inspector.");
            gameSceneName = gameScene.name;
        }
#endif

        private void Awake()
        {
            startGameButton.onClick.AddListener(OnStartGameClicked);
        }

        private void OnDestroy()
        {
            startGameButton.onClick.RemoveListener(OnStartGameClicked);
        }

        public void OnStartGameClicked()
        {
            EOSNetworkManager.Instance.StartEosHost(gameSceneName);
        }
    }
}

