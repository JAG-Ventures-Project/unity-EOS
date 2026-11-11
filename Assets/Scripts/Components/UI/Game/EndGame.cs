using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Assertions;
using Mirror;

namespace EOS
{
    public class EndGame : MonoBehaviour
    {
        [SerializeField] private Button endGameButton;

#if UNITY_EDITOR
        private void OnValidate()
        {
            Assert.IsNotNull(endGameButton, "[EndGame] endGameButton is null! Please assign in inspector.");
        }
#endif

        private void Awake()
        {
            endGameButton.onClick.AddListener(OnEndGameButtonClicked);
        }

        private void OnDestroy()
        {
            if (endGameButton != null)
            {
                endGameButton.onClick.RemoveListener(OnEndGameButtonClicked);
            }
        }

        private void OnEndGameButtonClicked()
        {
            endGameButton.interactable = false;
            EOSNetworkManager.Instance?.StopNetwork();
        }
    }
}
