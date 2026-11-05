using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance { get; private set; }

    public static bool IsLocked => Cursor.lockState == CursorLockMode.Locked;

    [SerializeField] private InputActionReference cancelAction;
    [SerializeField] private InputActionReference clickAction;

    private InputAction _screenClickAction;

    private void Awake()
    {
        Instance = this;
        InitializeClickAction();
    }

    private void InitializeClickAction()
    {
        if (clickAction != null)
        {
            _screenClickAction = clickAction.action;
        }
        else
        {
            _screenClickAction = new InputAction("ScreenClick", InputActionType.Button);
            _screenClickAction.AddBinding("<Mouse>/leftButton");
            _screenClickAction.AddBinding("<Mouse>/rightButton");
        }
    }

    private void OnEnable()
    {
        if (cancelAction != null)
        {
            cancelAction.action.performed += OnCancel;
            cancelAction.action.Enable();
        }

        if (_screenClickAction != null)
        {
            _screenClickAction.performed += OnClickPerformed;
        }
    }

    private void OnDisable()
    {
        if (cancelAction != null)
        {
            cancelAction.action.performed -= OnCancel;
            cancelAction.action.Disable();
        }

        if (_screenClickAction != null)
        {
            _screenClickAction.performed -= OnClickPerformed;
            _screenClickAction.Disable();
        }
    }

    private void Start() => SetCursorLocked(true);

    private void OnDestroy()
    {
        SetCursorLocked(false);
        if (clickAction == null && _screenClickAction != null)
        {
            _screenClickAction.Dispose();
        }
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

        if (_screenClickAction != null)
        {
            if (!locked)
            {
                _screenClickAction.Enable();
            }
            else
            {
                _screenClickAction.Disable();
            }
        }
    }
}