using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Assertions;
using Minimap.FPC;

namespace EOS
{
    [DisallowMultipleComponent]
    public class CustomizationUIManager : MonoBehaviour
    {
        [Header("Menu Panels")]
        [SerializeField] private GameObject menuPanel;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;

        [Header("Button Template Prefab")]
        [SerializeField] private Button colorButtonPrefab;

        [Header("Player Customization Controls")]
        [SerializeField] private Image playerColorPreview;
        [SerializeField] private Transform playerColorGridParent;
        [SerializeField] private Color[] playerPresetColors;

        [Header("Pet Customization Controls")]
        [SerializeField] private Toggle petActiveToggle;
        [SerializeField] private Image petColorPreview;
        [SerializeField] private Transform petColorGridParent;
        [SerializeField] private Color[] petPresetColors;

        private IPlayerDataStorage _playerDataStorage;
        private PlayerCustomizationData _customizationData;
        private PlayerController _localPlayer;
        private PetController _localPet;
        private bool _isMenuOpen;

        public bool IsMenuOpen => _isMenuOpen;
        public PlayerCustomizationData CustomizationData => _customizationData;

#if UNITY_EDITOR
        private void OnValidate()
        {
            Assert.IsNotNull(menuPanel, "[CustomizationUIManager] menuPanel is null! Please assign in inspector.");
            Assert.IsNotNull(openButton, "[CustomizationUIManager] openButton is null! Please assign in inspector.");
            Assert.IsNotNull(closeButton, "[CustomizationUIManager] closeButton is null! Please assign in inspector.");
            Assert.IsNotNull(colorButtonPrefab, "[CustomizationUIManager] colorButtonPrefab is null! Please assign in inspector.");
            Assert.IsNotNull(playerColorPreview, "[CustomizationUIManager] playerColorPreview is null! Please assign in inspector.");
            Assert.IsNotNull(playerColorGridParent, "[CustomizationUIManager] playerColorGridParent is null! Please assign in inspector.");
            Assert.IsNotNull(petColorPreview, "[CustomizationUIManager] petColorPreview is null! Please assign in inspector.");
            Assert.IsNotNull(petColorGridParent, "[CustomizationUIManager] petColorGridParent is null! Please assign in inspector.");
            Assert.IsNotNull(petActiveToggle, "[CustomizationUIManager] petActiveToggle is null! Please assign in inspector.");
            Assert.IsTrue(playerPresetColors != null && playerPresetColors.Length > 0, "[CustomizationUIManager] playerPresetColors is empty! Please assign in inspector.");
            Assert.IsTrue(petPresetColors != null && petPresetColors.Length > 0, "[CustomizationUIManager] petPresetColors is empty! Please assign in inspector.");
        }
#endif

        private void Awake()
        {
            _playerDataStorage = EOSSDKManager.Instance?.PlayerDataStorage;
            _customizationData = new PlayerCustomizationData();

            PlayerController.OnLocalPlayerSpawned += OnLocalPlayerSpawned;
            PetController.OnLocalPetSpawned += OnLocalPetSpawned;

            SetupUIListeners();
        }

        private void Start()
        {
            _playerDataStorage?.LoadJson<PlayerCustomizationData>(PlayerCustomizationData.CloudFileName, (success, loadedData, error) =>
            {
                if (success && loadedData != null)
                {
                    _customizationData = loadedData;
                }
                else if (error == "NotFound")
                {
                    // Save initial default customization to cloud if file doesn't exist yet
                    SaveToCloud();
                }

                RefreshUI();
                SyncToActiveEntities();
            });

            SetMenuOpen(false);
        }

        private void OnDestroy()
        {
            PlayerController.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;
            PetController.OnLocalPetSpawned -= OnLocalPetSpawned;
        }

        private void SetupUIListeners()
        {
            openButton.onClick.AddListener(OpenMenu);
            closeButton.onClick.AddListener(CloseMenu);

            petActiveToggle.isOn = _customizationData.hasPet;
            petActiveToggle.onValueChanged.AddListener(SetPetActive);

            SpawnPaletteButtons(playerColorGridParent, playerPresetColors, SetPlayerColor);
            SpawnPaletteButtons(petColorGridParent, petPresetColors, SetPetColor);
        }

        private void SpawnPaletteButtons(Transform gridParent, Color[] presetColors, Action<Color> onColorSelected)
        {
            for (int i = gridParent.childCount - 1; i >= 0; i--)
            {
                Destroy(gridParent.GetChild(i).gameObject);
            }

            for (int i = 0; i < presetColors.Length; i++)
            {
                Color chosenColor = presetColors[i];
                Button btn = Instantiate(colorButtonPrefab, gridParent);
                btn.gameObject.name = $"ColorButton_{i}";
                btn.gameObject.SetActive(true);

                btn.targetGraphic.color = chosenColor;
                btn.onClick.AddListener(() => onColorSelected(chosenColor));
            }
        }

        public void OpenMenu()
        {
            SetMenuOpen(true);
        }

        public void CloseMenu()
        {
            SetMenuOpen(false);
            SaveToCloud();
            SyncToActiveEntities();
        }

        public void SetMenuOpen(bool open)
        {
            _isMenuOpen = open;
            menuPanel.SetActive(open);

            if (open)
            {
                _localPlayer?.SetMovementEnable(false);
                RefreshUI();
            }
            else
            {
                _localPlayer?.SetMovementEnable(true);
            }
        }

        public void SetPlayerColor(Color color)
        {
            _customizationData.color = color;
            playerColorPreview.color = color;
        }

        public void SetPetColor(Color color)
        {
            _customizationData.petColor = color;
            petColorPreview.color = color;
        }

        public void SetPetActive(bool active)
        {
            _customizationData.hasPet = active;
            petActiveToggle.isOn = active;
        }

        private void SaveToCloud()
        {
            _playerDataStorage?.SaveJson(PlayerCustomizationData.CloudFileName, _customizationData);
        }

        private void RefreshUI()
        {
            playerColorPreview.color = _customizationData.color;
            petActiveToggle.isOn = _customizationData.hasPet;
            petColorPreview.color = _customizationData.petColor;
        }

        private void SyncToActiveEntities()
        {
            _localPlayer = PlayerController.LocalPlayer;
            _localPet = PetController.LocalPet;

            if (_localPlayer != null)
            {
                _localPlayer.CmdRequestColorChange(PlayerCustomizationData.ColorToVector3(_customizationData.color));
            }

            if (_localPet != null)
            {
                _localPet.CmdRequestColorChange(PlayerCustomizationData.ColorToVector3(_customizationData.petColor));
                _localPet.CmdSetPetActive(_customizationData.hasPet);
            }
        }

        private void OnLocalPlayerSpawned(PlayerController player)
        {
            _localPlayer = player;

            _localPlayer.CmdRequestColorChange(PlayerCustomizationData.ColorToVector3(_customizationData.color));

            if (_isMenuOpen)
            {
                _localPlayer.SetMovementEnable(false);
            }
        }

        private void OnLocalPetSpawned(PetController pet)
        {
            _localPet = pet;

            _localPet.CmdRequestColorChange(PlayerCustomizationData.ColorToVector3(_customizationData.petColor));
            _localPet.CmdSetPetActive(_customizationData.hasPet);
        }
    }
}