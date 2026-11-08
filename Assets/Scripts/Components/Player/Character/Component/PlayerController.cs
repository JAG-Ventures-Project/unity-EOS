using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Assertions;
using System;
using System.Collections.Generic;

namespace Minimap.FPC
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(PlayerInput))]
    [RequireComponent(typeof(PlayerPhysics))]
    public class PlayerController : NetworkBehaviour, IPlayerController
    {
        public static PlayerController LocalPlayer { get; private set; }
        public static event Action<PlayerController> OnLocalPlayerSpawned;
        [SerializeField] private CameraController _cameraController;
        [SerializeField] private PlayerVisual _playerVisual;
        private PlayerVisual _instantiatedVisual;
        private PlayerInput input;
        private IPlayerPhysics _playerPhysics;
        private InputActionMap _playerActionMap;
        private InputAction _cameraLookAction;
        private InputAction _moveAction;
        private InputAction _sprintAction;
        private InputAction _jumpAction;
        private Vector2 _moveInput;
        private Vector2 _cameraRotationInput;
        private bool _isSprinting;

        #region Synchronized State (SyncVars)

        [SyncVar(hook = nameof(OnColorChanged))]
        private Vector3 _netColor = Vector3.one;
        [SyncVar]
        private Vector3 _netVelocity;
        [SyncVar]
        private Vector3 _netCoordinates;
        public Vector3 NetVelocity => _netVelocity;
        public Vector3 NetCoordinates => _netCoordinates;
        public Color BodyColor => new Color(_netColor.x, _netColor.y, _netColor.z);
        #endregion


#if UNITY_EDITOR
        private void OnValidate()
        {
            Assert.IsNotNull(_playerVisual, "PlayerVisual reference is missing!");
            Assert.IsNotNull(_cameraController, "CameraManager reference is missing!");
        }
#endif

        void Awake()
        {
            TryGetComponent(out input);
            TryGetComponent(out _playerPhysics);
            _playerActionMap = input.actions.FindActionMap("Player", throwIfNotFound: true);
            _moveAction = _playerActionMap.FindAction("Move", throwIfNotFound: true);
            _sprintAction = _playerActionMap.FindAction("Sprint", throwIfNotFound: true);
            _jumpAction = _playerActionMap.FindAction("Jump", throwIfNotFound: true);
            _cameraLookAction = _playerActionMap.FindAction("Look", throwIfNotFound: true);
            this.enabled = false;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _instantiatedVisual = Instantiate(_playerVisual);
            _instantiatedVisual.Initialize(transform);
            _instantiatedVisual.SetColor(BodyColor);

            if (!isServer)
            {
                _playerPhysics.EnablePhysics(false);
            }
            if (!isLocalPlayer)
            {
                this.enabled = false;
            }
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            this.enabled = true;
            _cameraController = Instantiate(_cameraController);
            _cameraController.SetCameraTarget(transform);

            _playerActionMap.Enable();
            _jumpAction.performed += OnJumpPerformed;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            LocalPlayer = this;
            OnLocalPlayerSpawned?.Invoke(this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (_instantiatedVisual != null)
            {
                Destroy(_instantiatedVisual.gameObject);
                _instantiatedVisual = null;
            }
        }

        public override void OnStopLocalPlayer()
        {
            LocalPlayer = null;
            base.OnStopLocalPlayer();
            this.enabled = false;
            _playerActionMap.Disable();
            _jumpAction.performed -= OnJumpPerformed;
            Destroy(_cameraController.gameObject);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Update()
        {
            HandleInputs();
        }

        public void UpdateServerSyncVars(Vector3 velocity, Vector3 position)
        {
            _netVelocity = velocity;
            _netCoordinates = new Vector3(position.x, position.z, position.y);
        }

        /// <summary>
        /// AI Implementations.
        /// </summary>
        public void HandleInputs()
        {
            _moveInput = _moveAction.ReadValue<Vector2>();
            _isSprinting = _sprintAction.IsPressed();

            _cameraRotationInput = Cursor.lockState == CursorLockMode.Locked ? _cameraLookAction.ReadValue<Vector2>() : Vector2.zero;
            _cameraController.InputsListener(_cameraRotationInput);

            // _playerPhysics.HandleRotation(_cameraController.HorizontalRotation);
            CmdSendMovementInput(_moveInput, _cameraController.HorizontalRotation, _isSprinting);
        }

        [Command]
        private void CmdSendMovementInput(Vector2 moveInput, Quaternion yaw, bool isSprinting)
        {
            _playerPhysics.SetMoveInput(moveInput);
            _playerPhysics.HandleRotation(yaw);
            _playerPhysics.SetSprinting(isSprinting);
        }

        [Command]
        private void CmdJump()
        {
            _playerPhysics.HandleJump();
        }

        [Command]
        public void CmdRequestColorChange(Vector3 requestedRGB)
        {
            _netColor = new Vector3(
                Mathf.Clamp01(requestedRGB.x),
                Mathf.Clamp01(requestedRGB.y),
                Mathf.Clamp01(requestedRGB.z)
            );
        }

        private void OnColorChanged(Vector3 oldColor, Vector3 newColor)
        {
            if (_instantiatedVisual != null)
            {
                _instantiatedVisual.SetColor(new Color(newColor.x, newColor.y, newColor.z));
            }
        }


        public void SetMovementEnable(bool isEnable = true)
        {
            if (isEnable)
                _playerActionMap.Enable();
            else
                _playerActionMap.Disable();
        }

        private void OnJumpPerformed(InputAction.CallbackContext ctx) => CmdJump();

    }
}