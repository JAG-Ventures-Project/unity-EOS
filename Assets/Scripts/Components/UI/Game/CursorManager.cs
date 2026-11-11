using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;

public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance { get; private set; }

    public static bool IsLocked => Cursor.lockState == CursorLockMode.Locked;

    [SerializeField] private InputActionReference cancelAction;

#if UNITY_EDITOR
    private void OnValidate()
    {
        Assert.IsNotNull(cancelAction, "[CursorManager] Cancel Action is null");
    }
#endif

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
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

    public void ToggleCursor() => SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);

    public void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}