using UnityEngine;
using UnityEngine.InputSystem;

namespace EOS.Test.FPC
{
    public class CameraController : MonoBehaviour, IPlayerCamera
    {
        public Quaternion HorizontalRotation => horizontalHolder.rotation;

        [SerializeField] private Transform horizontalHolder;
        [SerializeField] private Transform verticalHolder;
        [SerializeField] private FPCameraSettings _cameraSettings;
        private InputAction _cameraLookAction;
        private Vector2 _cameraRotationInput;
        private Transform _target;
        private float _currentPitch = 0f;
        private float _lookX = 0f;
        private float _lookY = 0f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (UnityEditor.EditorUtility.IsPersistent(this))
            {
                return;
            }

            if (horizontalHolder == null) Debug.LogWarning("HorizontalHolder reference is missing!", this);
            if (verticalHolder == null) Debug.LogWarning("VerticalHolder reference is missing!", this);
            if (_cameraSettings == null) Debug.LogWarning("CameraSettings reference is missing!", this);
        }
#endif
        private void LateUpdate()
        {
            FollowTarget();
        }
        public void InputsListener(Vector2 lookInput)
        {
            _cameraRotationInput = lookInput;
            HandleCameraRotation();
        }

        public void SetCameraTarget(Transform target)
        {
            _target = target;
        }
        /// <summary>
        /// AI Implementations.
        /// </summary>
        public void FollowTarget()
        {
            transform.position = _target.position + Vector3.up * _cameraSettings.heightOffset;
        }

        /// <summary>
        /// AI Implementations.
        /// </summary>
        public void HandleCameraRotation()
        {

            _lookX = _cameraRotationInput.x * _cameraSettings.rotationSpeed / 30f;
            _lookY = _cameraRotationInput.y * _cameraSettings.rotationSpeed / 30f;
            horizontalHolder.Rotate(Vector3.up, _lookX);
            _currentPitch = Mathf.Clamp(_currentPitch - _lookY, _cameraSettings.minPitch, _cameraSettings.maxPitch);
            verticalHolder.localRotation = Quaternion.Euler(_currentPitch, 0f, 0f);
        }


    }
}