using System;
using Mirror;
using UnityEngine;
using UnityEngine.Assertions;

namespace Minimap.FPC
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(NetworkTransformReliable))]
    [RequireComponent(typeof(BoxCollider))]
    [RequireComponent(typeof(Renderer))]
    public class PetController : NetworkBehaviour
    {
        public static PetController LocalPet { get; private set; }
        public static event Action<PetController> OnLocalPetSpawned;

        [Header("Follow Settings")]
        [SerializeField] private Vector3 _followOffset = new Vector3(0.7f, 1.2f, -0.6f);
        [SerializeField] private float _followSpeed = 6f;
        [SerializeField] private float _rotationSpeed = 8f;

        [Header("Hover Animation")]
        [SerializeField] private float _hoverFrequency = 2f;
        [SerializeField] private float _hoverAmplitude = 0.12f;

        [Header("Component References")]
        [SerializeField] private Renderer _meshRenderer;
        [SerializeField] private Collider _collider;
        [SerializeField] private NetworkTransformReliable _networkTransform;

        private Transform _targetOwner;
        private Vector3 _lastPosition;

#if UNITY_EDITOR
        private void OnValidate()
        {            
            TryGetComponent(out _meshRenderer);
            TryGetComponent(out _collider);
            TryGetComponent(out _networkTransform);
            Assert.IsNotNull(_meshRenderer, "[PetController] Renderer component missing.");
            Assert.IsNotNull(_collider, "[PetController] Collider component missing.");
            Assert.IsNotNull(_networkTransform, "[PetController] NetworkTransformReliable component missing.");
        }
#endif

#region Synchronized State (SyncVars)
        [SyncVar(hook = nameof(OnPetColorChanged))]
        private Vector3 _netColor = Vector3.one;
        [SyncVar(hook = nameof(OnPetActiveChanged))]
        private bool _isPetActive = true;
        [SyncVar]
        private Vector3 _netVelocity;
        [SyncVar]
        private Vector3 _netCoordinates;
        [SyncVar]
        private uint _netLevel = 1;
        
        public Vector3 NetVelocity => _netVelocity;
        public Vector3 NetCoordinates => _netCoordinates;
        public uint NetLevel => _netLevel;
        public Color BodyColor => new Color(_netColor.x, _netColor.y, _netColor.z);
        public bool IsPetActive => _isPetActive;
#endregion

        private void Awake()
        {
            this.enabled = false;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _lastPosition = transform.position;
            UpdatePetActiveState(_isPetActive);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            this.enabled = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            SetColor(BodyColor);
            UpdatePetActiveState(_isPetActive);
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();
            LocalPet = this;
            OnLocalPetSpawned?.Invoke(this);
        }

        public override void OnStopAuthority()
        {
            base.OnStopAuthority();
            if (LocalPet == this) LocalPet = null;
        }

        private void OnDestroy()
        {
            if (LocalPet == this) LocalPet = null;
        }

        // AI Implementation
        private void FixedUpdate()
        {
            Vector3 targetPos = _targetOwner.position + _targetOwner.TransformDirection(_followOffset);
            targetPos.y += Mathf.Sin(Time.time * _hoverFrequency) * _hoverAmplitude;
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.fixedDeltaTime * _followSpeed);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetOwner.rotation, Time.fixedDeltaTime * _rotationSpeed);
            
            // Authoritative SyncVar calculations
            _netVelocity = (transform.position - _lastPosition) / Time.fixedDeltaTime;
            _netCoordinates = new Vector3(transform.position.x, transform.position.z, transform.position.y);
            _lastPosition = transform.position;
        }

        [Server]
        public void SetOwner(Transform owner)
        {
            _targetOwner = owner;    
            // or just set it to true if 100% we will have owner 
            this.enabled = _isPetActive && (_targetOwner != null);
        }

        [Server]
        public void SetLevel(int level)
        {
            _netLevel = (uint)Mathf.Max(1, level);
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

        [Command]
        public void CmdSetPetActive(bool active)
        {
            _isPetActive = active;
            UpdatePetActiveState(active);
        }

        private void OnPetColorChanged(Vector3 oldColor, Vector3 newColor)
        {
            SetColor(new Color(newColor.x, newColor.y, newColor.z));
        }

        private void OnPetActiveChanged(bool oldVal, bool newVal)
        {
            UpdatePetActiveState(newVal);
        }

        // AI implementation: Completely stops movement, network syncing, rendering, and physics when pet is disabled.
        private void UpdatePetActiveState(bool active)
        {
            if (isServer)
            {
                this.enabled = active;

                if (active)
                {
                    // Snap directly to owner upon becoming active to avoid interpolating across the map
                    transform.position = _targetOwner.position + _targetOwner.TransformDirection(_followOffset);
                    transform.rotation = _targetOwner.rotation;
                    _lastPosition = transform.position;
                    _netVelocity = Vector3.zero;
                    _netCoordinates = new Vector3(transform.position.x, transform.position.z, transform.position.y);
                }
                else if (!active)
                {
                    _netVelocity = Vector3.zero;
                }
            }
            else
            {
                this.enabled = false;
            }
            
            _meshRenderer.enabled = active;
            _collider.enabled = active;
        }

        public void SetColor(Color color)
        {
            _meshRenderer.material.color = color;
        }
    }
}
