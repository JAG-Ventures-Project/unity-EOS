using System;
using EOS.Test.Pet;
using EOS.Test.FPC;
using EOS.Test.Cursor;
using EOS.Core;
using UnityEngine;
using UnityEngine.UI;

namespace EOS.Test.Customization
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
            if (UnityEditor.EditorUtility.IsPersistent(this))
            {
                return;
            }

            if (menuPanel == null) Debug.LogWarning("[CustomizationUIManager] menuPanel is null! Please assign in inspector.", this);
            if (openButton == null) Debug.LogWarning("[CustomizationUIManager] openButton is null! Please assign in inspector.", this);
            if (closeButton == null) Debug.LogWarning("[CustomizationUIManager] closeButton is null! Please assign in inspector.", this);
            if (colorButtonPrefab == null) Debug.LogWarning("[CustomizationUIManager] colorButtonPrefab is null! Please assign in inspector.", this);
            if (playerColorPreview == null) Debug.LogWarning("[CustomizationUIManager] playerColorPreview is null! Please assign in inspector.", this);
            if (playerColorGridParent == null) Debug.LogWarning("[CustomizationUIManager] playerColorGridParent is null! Please assign in inspector.", this);
            if (playerPresetColors == null || playerPresetColors.Length == 0) Debug.LogWarning("[CustomizationUIManager] playerPresetColors is empty! Please assign in inspector.", this);
            if (petPresetColors == null || petPresetColors.Length == 0) Debug.LogWarning("[CustomizationUIManager] petPresetColors is empty! Please assign in inspector.", this);
        }
#endif

        private void Awake()
        {
            _playerDataStorage = PlayerDataStorageManager.Instance;
            _customizationData = new PlayerCustomizationData();

            SetupPlayerUIListeners();

            PlayerController.OnLocalPlayerSpawned += OnLocalPlayerSpawned;
            PetController.OnLocalPetSpawned += OnLocalPetSpawned;
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

        private void SetupPlayerUIListeners()
        {
            openButton.onClick.AddListener(OpenMenu);
            closeButton.onClick.AddListener(CloseMenu);

            SpawnPaletteButtons(playerColorGridParent, playerPresetColors, SetPlayerColor);
        }

        private void SetupPetUIListeners()
        {
            petActiveToggle.isOn = _customizationData.hasPet;
            petActiveToggle.onValueChanged.AddListener(SetPetActive);

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
            CursorManager.Instance?.SetCursorLocked(true);
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
            SetupPetUIListeners();

            _localPet.CmdRequestColorChange(PlayerCustomizationData.ColorToVector3(_customizationData.petColor));
            _localPet.CmdSetPetActive(_customizationData.hasPet);
        }
    }
}