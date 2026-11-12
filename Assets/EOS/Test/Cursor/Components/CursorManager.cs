using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;

namespace EOS.Test.Cursor
{
    public class CursorManager : MonoBehaviour
    {
        public static CursorManager Instance { get; private set; }

        public static bool IsLocked => UnityEngine.Cursor.lockState == CursorLockMode.Locked;

        [SerializeField] private InputActionReference cancelAction;

#if UNITY_EDITOR
        private void OnValidate()
        {
            Assert.IsNotNull(cancelAction, "[CursorManager] Cancel Action is null");
        }
#endif

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            cancelAction.action.performed += OnCancel;
            cancelAction.action.Enable();
        }

        private void OnDisable()
        {
            cancelAction.action.performed -= OnCancel;
            cancelAction.action.Disable();
        }

        private void Start() => SetCursorLocked(true);

        private void OnDestroy()
        {
            SetCursorLocked(false);
        }

        private void OnCancel(InputAction.CallbackContext ctx) => ToggleCursor();

        public void ToggleCursor() => SetCursorLocked(UnityEngine.Cursor.lockState != CursorLockMode.Locked);

        public void SetCursorLocked(bool locked)
        {
            UnityEngine.Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            UnityEngine.Cursor.visible = !locked;
        }
    }
}