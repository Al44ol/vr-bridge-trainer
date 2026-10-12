// ============================================================
// HeightAdjust.cs — Настройка высоты игрока в VR
//
// Создаёт world-space панель с ползунком высоты.
// Открывается из меню «Параметры» (Y левого контроллера) → «Рост игрока…» (D1).
// До D1 — кнопкой X (левый контроллер: primaryButton) или H на клавиатуре; теперь только
// при controllerButton (по умолчанию выкл — кнопка X свободна).
// Повесить на XR Origin Hands (XR Rig).
// ============================================================
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class HeightAdjust : MonoBehaviour
{
    [Header("Настройки высоты")]
    [Tooltip("Текущее смещение по Y (метры). Отрицательное = опустить вниз.")]
    public float heightOffset = 0f;

    [Tooltip("Диапазон смещения")]
    public float minOffset = -2f;
    public float maxOffset = 1f;

    [Header("Вызов")]
    [Tooltip("Кнопка X левого контроллера и клавиша H (D1: по умолчанию выкл — панель из меню Y «Рост игрока…»)")]
    public bool controllerButton;

    [Header("Панель")]
    public float distanceFromHead = 1.0f;
    public float downOffset = 0.1f;

    // Ссылки на UI
    private GameObject panelRoot;
    private Slider heightSlider;
    private TextMeshProUGUI valueLabel;
    private Canvas panelCanvas;

    private InputAction yButtonAction;
    private bool panelVisible = false;

    void Start()
    {
        BuildPanel();
        ApplyHeight();

        // X на левом контроллере = primaryButton левого (Y — secondaryButton)
        if (controllerButton)
        {
            yButtonAction = new InputAction("HeightPanel", InputActionType.Button);
            yButtonAction.AddBinding("<XRController>{LeftHand}/primaryButton");
            yButtonAction.AddBinding("<Keyboard>/h");
            yButtonAction.performed += _ => TogglePanel();
            yButtonAction.Enable();
        }
    }

    private BridgeMenuCommand menuCommand;

    void OnEnable()
    {
        BridgeMenu.SetGroupOrder("Общее", -95);
        BridgeMenu.SetGroupExpanded("Общее", true);   // D1: на главной странице меню Y
        menuCommand = BridgeMenu.Register("Общее", () => panelVisible ? "Рост игрока: закрыть панель" : "Рост игрока…", TogglePanel)
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
        yButtonAction?.Disable();
        yButtonAction?.Dispose();
    }

    // ─── Применяем смещение к Camera Offset ───
    void ApplyHeight()
    {
        // Camera Offset — дочерний объект XR Origin
        Transform cameraOffset = transform.Find("Camera Offset");
        if (cameraOffset == null)
        {
            // Попробуем первый дочерний объект
            if (transform.childCount > 0)
                cameraOffset = transform.GetChild(0);
        }

        if (cameraOffset != null)
        {
            Vector3 pos = cameraOffset.localPosition;
            pos.y = heightOffset;
            cameraOffset.localPosition = pos;
        }
        else
        {
            // Если Camera Offset не найден — двигаем сам XR Origin
            Vector3 pos = transform.localPosition;
            pos.y = heightOffset;
            transform.localPosition = pos;
        }
    }

    // ─── Создаём панель из кода ───
    void BuildPanel()
    {
        // Корневой объект
        panelRoot = new GameObject("HeightAdjustPanel");
        panelRoot.transform.SetParent(transform);

        // Canvas
        panelCanvas = panelRoot.AddComponent<Canvas>();
        panelCanvas.renderMode = RenderMode.WorldSpace;
        panelCanvas.transform.localScale = Vector3.one * 0.002f;

        var scaler = panelRoot.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 100;

        // Tracked Device Graphic Raycaster — если доступен
        // (если нет — обычный Graphic Raycaster)
        var raycasterType = System.Type.GetType(
            "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (raycasterType != null)
            panelRoot.AddComponent(raycasterType);
        else
            panelRoot.AddComponent<GraphicRaycaster>();

        // Фон панели
        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(panelRoot.transform, false);
        var bgImage = bg.AddComponent<Image>();
        bgImage.color = new Color(0.1f, 0.1f, 0.15f, 0.92f);
        var bgRect = bg.GetComponent<RectTransform>();
        bgRect.sizeDelta = new Vector2(320, 160);
        bgRect.anchoredPosition = Vector2.zero;

        // Заголовок
        CreateLabel(bg.transform, "Высота игрока", new Vector2(0, 55), 20,
                    new Color(0.8f, 0.9f, 1f));

        // Метка значения
        var valueObj = CreateLabel(bg.transform, "0.00 м", new Vector2(0, 25), 18,
                                   Color.white);
        valueLabel = valueObj.GetComponent<TextMeshProUGUI>();

        // Ползунок
        heightSlider = CreateSlider(bg.transform, new Vector2(0, -10), new Vector2(280, 30));
        heightSlider.minValue = minOffset;
        heightSlider.maxValue = maxOffset;
        heightSlider.value = heightOffset;
        heightSlider.onValueChanged.AddListener(OnSliderChanged);

        // Подписи диапазона
        CreateLabel(bg.transform, minOffset.ToString("0") + " м",
                    new Vector2(-120, -40), 14, new Color(0.7f, 0.7f, 0.7f));
        CreateLabel(bg.transform, maxOffset.ToString("0") + " м",
                    new Vector2(120, -40), 14, new Color(0.7f, 0.7f, 0.7f));

        // Кнопки «Сброс» и «« Назад» (D1: закрыть панель и вернуться в меню «Параметры»)
        CreateButton(bg.transform, "Сброс", new Vector2(-65, -65), ResetHeight);
        CreateButton(bg.transform, "« Назад", new Vector2(65, -65), Back);

        panelCanvas.enabled = false;
    }

    void OnSliderChanged(float value)
    {
        heightOffset = value;
        if (valueLabel != null)
            valueLabel.text = value.ToString("+0.00;-0.00;0.00") + " м";
        ApplyHeight();
    }

    void ResetHeight()
    {
        heightOffset = 0f;
        heightSlider.value = 0f;
        ApplyHeight();
    }

    /// <summary>«« Назад»: закрыть панель и снова открыть меню «Параметры» (Y).</summary>
    void Back()
    {
        if (panelVisible) TogglePanel();
        BridgeMenu.OpenMenu(BridgeMenu.SettingsMenuId);
    }

    void TogglePanel()
    {
        panelVisible = !panelVisible;
        panelCanvas.enabled = panelVisible;
        if (panelVisible) PositionPanel();
    }

    void PositionPanel()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 fwd = cam.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd.Normalize();

        panelRoot.transform.position = cam.transform.position
                                     + fwd * distanceFromHead
                                     + Vector3.down * downOffset;
        panelRoot.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
    }

    // ─── Вспомогательные методы создания UI ───

    GameObject CreateLabel(Transform parent, string text, Vector2 pos, int size, Color color)
    {
        var go = new GameObject("Label_" + text.Substring(0, Mathf.Min(6, text.Length)));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(280, size + 6);
        rt.anchoredPosition = pos;
        return go;
    }

    Slider CreateSlider(Transform parent, Vector2 pos, Vector2 size)
    {
        // Контейнер
        var sliderObj = new GameObject("Slider");
        sliderObj.transform.SetParent(parent, false);
        var slider = sliderObj.AddComponent<Slider>();
        var sliderRect = sliderObj.GetComponent<RectTransform>();
        sliderRect.sizeDelta = size;
        sliderRect.anchoredPosition = pos;

        // Background
        var bg = new GameObject("Background");
        bg.transform.SetParent(sliderObj.transform, false);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.3f, 0.3f, 0.4f);
        var bgRect = bg.GetComponent<RectTransform>();
        bgRect.sizeDelta = size;
        bgRect.anchoredPosition = Vector2.zero;

        // Fill Area
        var fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        var faRect = fillArea.AddComponent<RectTransform>();
        faRect.sizeDelta = new Vector2(-20, size.y * 0.5f);
        faRect.anchoredPosition = Vector2.zero;
        faRect.anchorMin = new Vector2(0, 0.5f);
        faRect.anchorMax = new Vector2(1, 0.5f);

        var fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.3f, 0.6f, 1f);
        var fillRect = fill.GetComponent<RectTransform>();
        fillRect.sizeDelta = Vector2.zero;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(1, 1);

        // Handle
        var handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(sliderObj.transform, false);
        var haRect = handleArea.AddComponent<RectTransform>();
        haRect.sizeDelta = Vector2.zero;
        haRect.anchorMin = Vector2.zero;
        haRect.anchorMax = Vector2.one;

        var handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        var handleImg = handle.AddComponent<Image>();
        handleImg.color = Color.white;
        var handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(24, size.y * 1.4f);

        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;

        return slider;
    }

    void CreateButton(Transform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.25f, 0.45f, 0.7f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(action);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(120, 32);
        rt.anchoredPosition = pos;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 16;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        var tRect = textGo.GetComponent<RectTransform>();
        tRect.sizeDelta = new Vector2(120, 32);
        tRect.anchoredPosition = Vector2.zero;
    }
}
