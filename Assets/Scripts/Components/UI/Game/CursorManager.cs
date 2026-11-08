using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance { get; private set; }

    public static bool IsLocked => Cursor.lockState == CursorLockMode.Locked;

    [SerializeField] private InputActionReference cancelAction;
    [SerializeField] private InputActionReference clickAction;

#if UNITY_EDITOR
    private void OnValidate()
    {
        Assert.IsNotNull(cancelAction, "[CursorManager] Cancel Action is null");
        Assert.IsNotNull(clickAction, "[CursorManager] Click Action is null");
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

        clickAction.action.performed += OnClickPerformed;
        clickAction.action.Enable();
    }

    private void OnDisable()
    {
        cancelAction.action.performed -= OnCancel;
        cancelAction.action.Disable();

        clickAction.action.performed -= OnClickPerformed;
        clickAction.action.Disable();
    }

    private void Start() => SetCursorLocked(true);

    private void OnDestroy()
    {
        SetCursorLocked(false);
    }

    private void OnCancel(InputAction.CallbackContext ctx) => ToggleCursor();

    private void OnClickPerformed(InputAction.CallbackContext ctx)
    {
        if (!IsLocked)
        {
            if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
            {
                SetCursorLocked(true);
            }
        }
    }

    public void ToggleCursor() => SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);

    public void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;

        if (!locked)
        {
            clickAction.action.Enable();
        }
        else
        {
            clickAction.action.Disable();
        }
    }
}