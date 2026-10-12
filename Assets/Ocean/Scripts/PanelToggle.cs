// ============================================================
// PanelToggle.cs — Панель погоды: меню «Параметры» (Y) → «Погода (панель)…» (D1)
// До D1 — кнопка B правого контроллера (InputAction для OpenXR) и Tab; теперь только
// при controllerButton (по умолчанию выкл — кнопка B свободна).
// ============================================================
using UnityEngine;
using UnityEngine.InputSystem;

public class PanelToggle : MonoBehaviour
{
    [Header("Настройки")]
    public bool startHidden = true;
    public float distanceFromHead = 1.2f;
    public float downOffset = 0.2f;
    [Tooltip("Кнопка B правого контроллера и Tab (D1: по умолчанию выкл — панель из меню Y «Погода (панель)…»)")]
    public bool controllerButton;
    [Tooltip("Название команды в меню Y")]
    public string menuTitle = "Погода (панель)";

    private BridgeMenuCommand menuCommand;

    private Canvas canvas;
    private bool isVisible;

    // InputAction для кнопки B на правом контроллере
    private InputAction bButtonAction;

    void Start()
    {
        canvas = GetComponent<Canvas>();
        isVisible = !startHidden;
        if (canvas != null)
            canvas.enabled = isVisible;

        if (controllerButton)
        {
            // B на правом контроллере = secondaryButton
            bButtonAction = new InputAction("TogglePanel", InputActionType.Button);
            bButtonAction.AddBinding("<XRController>{RightHand}/secondaryButton");
            bButtonAction.AddBinding("<Keyboard>/tab"); // Tab на клавиатуре как запасной
            bButtonAction.performed += ctx => Toggle();
            bButtonAction.Enable();
        }
    }

    void OnEnable()
    {
        BridgeMenu.SetGroupOrder("Общее", -95);
        BridgeMenu.SetGroupExpanded("Общее", true);   // D1: на главной странице меню Y
        menuCommand = BridgeMenu.Register("Общее", () => isVisible ? $"{menuTitle}: закрыть" : $"{menuTitle}…", Toggle)
            .InMenu(BridgeMenu.SettingsMenuId);
    }

    void OnDisable()
    {
        BridgeMenu.Unregister(menuCommand);
        menuCommand = null;
    }

    void OnDestroy()
    {
        if (bButtonAction != null)
        {
            bButtonAction.Disable();
            bButtonAction.Dispose();
        }
    }

    public void Toggle()
    {
        isVisible = !isVisible;

        if (canvas != null)
            canvas.enabled = isVisible;

        if (isVisible)
            PositionInFrontOfPlayer();
    }

    void PositionInFrontOfPlayer()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 forward = cam.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
            forward = cam.transform.forward;
        forward.Normalize();

        transform.position = cam.transform.position
                           + forward * distanceFromHead
                           + Vector3.down * downOffset;

        transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }
}
