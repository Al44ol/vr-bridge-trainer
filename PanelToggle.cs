// ============================================================
// PanelToggle.cs — Панель по кнопке B (правый контроллер Quest)
// Использует InputAction для корректной работы с OpenXR.
// ============================================================
using UnityEngine;
using UnityEngine.InputSystem;

public class PanelToggle : MonoBehaviour
{
    [Header("Настройки")]
    public bool startHidden = true;
    public float distanceFromHead = 1.2f;
    public float downOffset = 0.2f;

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

        // B на правом контроллере = secondaryButton
        bButtonAction = new InputAction("TogglePanel", InputActionType.Button);
        bButtonAction.AddBinding("<XRController>{RightHand}/secondaryButton");
        bButtonAction.AddBinding("<Keyboard>/tab"); // Tab на клавиатуре как запасной
        bButtonAction.performed += ctx => Toggle();
        bButtonAction.Enable();
    }

    void OnDestroy()
    {
        if (bButtonAction != null)
        {
            bButtonAction.Disable();
            bButtonAction.Dispose();
        }
    }

    void Toggle()
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
