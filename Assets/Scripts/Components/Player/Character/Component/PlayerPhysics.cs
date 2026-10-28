using UnityEngine;
using UnityEngine.InputSystem;

namespace Minimap.FPC
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]

    public class PlayerPhysics : MonoBehaviour, IPlayerPhysics
    {
        [SerializeField] private SOPlayerPhysicsSettings controllerProperties;
        private Rigidbody _rb;
        private CapsuleCollider _collider;
        private Vector2 _currentMove;
        private bool _isSprinting;
        private PlayerController _controller;

        private void Awake()
        {
            TryGetComponent(out _rb);
            TryGetComponent(out _collider);
            TryGetComponent(out _controller);
        }

        void FixedUpdate()
        {
            HandleMovement();
            _controller.UpdateServerSyncVars(_rb.linearVelocity, transform.position);
        }
        /// <summary>
        /// AI Implementation
        /// </summary>
        public void SetMoveInput(Vector2 moveCalled) => _currentMove = moveCalled;
        /// <summary>
        /// AI Implementation
        /// </summary>
        public void HandleMovement()
        {
            float currentSpeed = _isSprinting ? controllerProperties.moveSpeed * controllerProperties.sprintMultiplier : controllerProperties.moveSpeed;
            Vector3 moveDirection = transform.right * _currentMove.x + transform.forward * _currentMove.y;
            Vector3 targetVelocity = moveDirection * currentSpeed;
            _rb.linearVelocity = new Vector3(targetVelocity.x, _rb.linearVelocity.y, targetVelocity.z);
        }
        /// <summary>
        /// AI Implementation
        /// </summary>
        public void HandleRotation(Quaternion yaw)
        {
            _rb.MoveRotation(yaw);
        }
        /// <summary>
        /// AI Implementation
        /// </summary>
        public void HandleJump()
        {
            if (!IsGrounded()) return;
            _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, controllerProperties.jumpForce, _rb.linearVelocity.z);

        }
        /// <summary>
        /// AI Implementation
        /// </summary>
        public bool IsGrounded()
        {
            Vector3 origin = transform.position;
            float distance = _collider.bounds.extents.y - controllerProperties.groundRadius + controllerProperties.groundDistance;
            return Physics.SphereCast(origin, controllerProperties.groundRadius, Vector3.down, out _, distance, controllerProperties.groundLayer.value, QueryTriggerInteraction.Ignore);
        }

        public void SetSprinting(bool isSprinting) => _isSprinting = isSprinting;
        
        public void EnablePhysics(bool isEnable = true) 
        { 
            _rb.isKinematic = !isEnable; 
            this.enabled = isEnable; 
        }
    }
}