using UnityEngine;
using UnityEngine.InputSystem;

public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance { get; private set; }

    public static bool IsLocked => Cursor.lockState == CursorLockMode.Locked;

    [SerializeField] private InputActionReference cancelAction;

    private InputAction _cancelAction;

    private void Awake() => Instance = this;
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
    private void OnDestroy() => SetCursorLocked(false);
    private void OnCancel(InputAction.CallbackContext ctx) => ToggleCursor();
    public void ToggleCursor() => SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);
    public void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}