// ============================================================
// PanelToggle.cs — Панель погоды: меню «Параметры» (Y) → «Погода (панель)…» (D1)
// До D1 — кнопка B правого контроллера (InputAction для OpenXR) и Tab; теперь только
// при controllerButton (по умолчанию выкл — кнопка B свободна).
// D1: под кнопками панели — «« Назад» (копия первой кнопки панели, создаётся при запуске,
// сцену не правит): закрыть панель и снова открыть меню «Параметры».
// ============================================================
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

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
        AddBackButton();

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
            .OpensWindow()   // меню закрывается — панели не накладываются
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

    /// <summary>«« Назад»: закрыть панель и снова открыть меню «Параметры» (Y).</summary>
    public void Back()
    {
        if (isVisible) Toggle();
        BridgeMenu.OpenMenu(BridgeMenu.SettingsMenuId);
    }

    // Кнопка «« Назад» — копия первой кнопки панели (тот же вид), под панелью по центру
    private void AddBackButton()
    {
        Button sample = GetComponentInChildren<Button>(true);
        var panel = transform as RectTransform;
        if (sample == null || panel == null || transform.Find("BackButton") != null) return;
        Button back = Instantiate(sample, transform);
        back.name = "BackButton";
        back.onClick = new Button.ButtonClickedEvent();   // без действий кнопки-образца (пресет погоды)
        back.onClick.AddListener(Back);
        var rt = (RectTransform)back.transform;
        var src = (RectTransform)sample.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(src.rect.width, src.rect.height);
        rt.anchoredPosition = new Vector2(0f, -0.1f * src.rect.height);
        foreach (TMP_Text l in back.GetComponentsInChildren<TMP_Text>(true)) l.text = "« Назад";
        foreach (Text l in back.GetComponentsInChildren<Text>(true)) l.text = "« Назад";
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
