// ============================================================
// BridgeMenu.cs — Вызываемое VR-меню команд (B1)
//
// Кнопка A (правый контроллер Quest) открывает/закрывает меню. Панель
// появляется перед игроком (≈0,6 м, на уровне груди), лицом к нему, и остаётся
// в системе судна (объект — дочерний к Ship): качается вместе с мостиком,
// за головой не ездит. Нажатие — лучом или касанием (XRUIInputModule).
//
// Меню не знает конкретных команд: модули регистрируют их сами,
//     BridgeMenu.Register("Судно", "Снять с мели", Refloat, () => grounded);
// кнопки строятся при открытии; недоступная сейчас команда — серая.
// Флаг InstructorOnly — задел на окно инструктора (D1).
// Прыжок шаблона XR (тоже кнопка A) отключает Setup B1 Water Area.
// ============================================================
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Команда меню мостика.</summary>
public sealed class BridgeMenuCommand
{
    public string Group;
    public Func<string> Title;
    public Action Execute;
    /// <summary>Доступна ли сейчас (null — всегда).</summary>
    public Func<bool> IsAvailable;
    /// <summary>Команда инструктора (D1: в окне инструктора; пока доступна и в шлеме).</summary>
    public bool InstructorOnly;
    /// <summary>Закрыть меню после выполнения.</summary>
    public bool CloseOnExecute = true;

    public bool Available => IsAvailable == null || IsAvailable();
}

public class BridgeMenu : MonoBehaviour
{
    // ── Реестр команд (статический: модули регистрируются в любом порядке) ──
    private static readonly List<BridgeMenuCommand> commands = new List<BridgeMenuCommand>();
    public static IReadOnlyList<BridgeMenuCommand> Commands => commands;
    public static BridgeMenu Instance { get; private set; }

    // Если в настройках Play Mode отключена перезагрузка домена — не тащить команды прошлого запуска
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        commands.Clear();
        Instance = null;
    }

    public static BridgeMenuCommand Register(string group, Func<string> title, Action execute,
                                             Func<bool> isAvailable = null, bool instructorOnly = false)
    {
        var c = new BridgeMenuCommand { Group = group, Title = title, Execute = execute, IsAvailable = isAvailable, InstructorOnly = instructorOnly };
        commands.Add(c);
        if (Instance != null && Instance.IsOpen) Instance.Rebuild();
        return c;
    }

    public static BridgeMenuCommand Register(string group, string title, Action execute,
                                             Func<bool> isAvailable = null, bool instructorOnly = false) =>
        Register(group, () => title, execute, isAvailable, instructorOnly);

    public static void Unregister(BridgeMenuCommand c)
    {
        if (c == null) return;
        commands.Remove(c);
        if (Instance != null && Instance.IsOpen) Instance.Rebuild();
    }

    [Header("Положение панели")]
    public float distanceFromHead = 0.6f;
    public float downOffset = 0.25f;
    [Tooltip("Масштаб холста, м на пиксель")]
    public float canvasScale = 0.0012f;

    [Header("Управление")]
    [Tooltip("Кнопка вызова: A на правом контроллере")]
    public string toggleBinding = "<XRController>{RightHand}/primaryButton";

    private const float Width = 360f, ButtonHeight = 40f, GroupHeight = 26f, Pad = 12f;
    private static readonly Color PanelColor = new Color(0.08f, 0.1f, 0.13f, 0.94f);
    private static readonly Color ButtonColor = new Color(0.22f, 0.4f, 0.62f);
    private static readonly Color DisabledColor = new Color(0.25f, 0.27f, 0.3f);

    private InputAction toggleAction;
    private GameObject panel;
    private Canvas canvas;
    private RectTransform background;
    private readonly List<(BridgeMenuCommand cmd, Button btn, Image img, TextMeshProUGUI label)> items = new List<(BridgeMenuCommand, Button, Image, TextMeshProUGUI)>();
    private float nextRefresh;

    public bool IsOpen => canvas != null && canvas.enabled;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[BridgeMenu] В сцене несколько меню — используется первое.", this);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        BuildPanel();
        toggleAction = new InputAction("BridgeMenu", InputActionType.Button);
        toggleAction.AddBinding(toggleBinding);
        toggleAction.performed += _ => Toggle();
        toggleAction.Enable();
    }

    void OnDestroy()
    {
        toggleAction?.Disable();
        toggleAction?.Dispose();
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (!IsOpen || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;
        RefreshStates();
    }

    [ContextMenu("Открыть / закрыть меню")]
    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (canvas == null) BuildPanel();
        Rebuild();
        PositionInFrontOfPlayer();
        canvas.enabled = true;
    }

    public void Close()
    {
        if (canvas != null) canvas.enabled = false;
    }

    // ─────────── Построение ───────────
    private void BuildPanel()
    {
        panel = new GameObject("BridgeMenuPanel", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        panel.transform.localScale = Vector3.one * canvasScale;
        panel.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 100;

        var raycasterType = Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (raycasterType != null) panel.AddComponent(raycasterType);
        else panel.AddComponent<GraphicRaycaster>();

        var bg = new GameObject("Background", typeof(RectTransform));
        bg.transform.SetParent(panel.transform, false);
        bg.AddComponent<Image>().color = PanelColor;
        background = bg.GetComponent<RectTransform>();
        canvas.enabled = false;
    }

    // Пересобрать кнопки по текущему реестру
    private void Rebuild()
    {
        if (background == null) return;
        for (int i = background.childCount - 1; i >= 0; i--)
            Destroy(background.GetChild(i).gameObject);
        items.Clear();

        // Порядок групп — порядок первой регистрации
        var groups = new List<string>();
        foreach (BridgeMenuCommand c in commands)
            if (!groups.Contains(c.Group ?? "")) groups.Add(c.Group ?? "");

        float rows = 0f;
        rows += 34f;                                             // заголовок
        foreach (string g in groups)
        {
            rows += GroupHeight;
            foreach (BridgeMenuCommand c in commands) if ((c.Group ?? "") == g) rows += ButtonHeight + 6f;
        }
        if (commands.Count == 0) rows += 30f;
        rows += ButtonHeight + 10f;                              // «Закрыть»
        float height = rows + 2f * Pad;
        background.sizeDelta = new Vector2(Width, height);

        float y = height * 0.5f - Pad;
        Label("Меню мостика", ref y, 34f, 22f, new Color(0.85f, 0.92f, 1f), TextAlignmentOptions.Center);
        if (commands.Count == 0)
            Label("Нет команд", ref y, 30f, 16f, new Color(0.6f, 0.6f, 0.6f), TextAlignmentOptions.Center);

        foreach (string g in groups)
        {
            Label(g, ref y, GroupHeight, 15f, new Color(0.62f, 0.68f, 0.75f), TextAlignmentOptions.Left);
            foreach (BridgeMenuCommand c in commands)
            {
                if ((c.Group ?? "") != g) continue;
                BridgeMenuCommand cmd = c;
                (Button btn, Image img, TextMeshProUGUI label) = MakeButton(ref y, () => Execute(cmd));
                items.Add((cmd, btn, img, label));
            }
        }
        y -= 4f;
        (Button close, Image closeImg, TextMeshProUGUI closeLabel) = MakeButton(ref y, Close);
        closeImg.color = new Color(0.35f, 0.35f, 0.4f);
        closeLabel.text = "Закрыть";
        RefreshStates();
    }

    private void Execute(BridgeMenuCommand c)
    {
        if (!c.Available) return;
        try { c.Execute?.Invoke(); }
        catch (Exception e) { Debug.LogException(e, this); }
        if (c.CloseOnExecute) Close();
        else RefreshStates();
    }

    private void RefreshStates()
    {
        foreach (var it in items)
        {
            bool on = it.cmd.Available;
            it.btn.interactable = on;
            it.img.color = on ? ButtonColor : DisabledColor;
            string title = it.cmd.Title != null ? it.cmd.Title() : "—";
            it.label.text = it.cmd.InstructorOnly ? title + " <size=70%>(инстр.)</size>" : title;
            it.label.color = on ? Color.white : new Color(0.6f, 0.6f, 0.6f);
        }
    }

    private void Label(string text, ref float y, float h, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(background, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.alignment = align;
        tmp.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(Width - 2f * Pad, h);
        rt.anchoredPosition = new Vector2(0f, y - h * 0.5f);
        y -= h;
    }

    private (Button, Image, TextMeshProUGUI) MakeButton(ref float y, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(background, false);
        var img = go.AddComponent<Image>();
        img.color = ButtonColor;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(action);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(Width - 2f * Pad, ButtonHeight);
        rt.anchoredPosition = new Vector2(0f, y - ButtonHeight * 0.5f);
        y -= ButtonHeight + 6f;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 17f; tmp.color = Color.white; tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        textGo.GetComponent<RectTransform>().sizeDelta = rt.sizeDelta;
        return (btn, img, tmp);
    }

    private void PositionInFrontOfPlayer()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 fwd = cam.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = cam.transform.up;
        fwd.y = 0f;
        fwd.Normalize();
        panel.transform.position = cam.transform.position + fwd * distanceFromHead + Vector3.down * downOffset;
        // Панель наклонена к глазам: смотрит из точки панели на голову
        Vector3 toPanel = panel.transform.position - cam.transform.position;
        panel.transform.rotation = Quaternion.LookRotation(toPanel.normalized, Vector3.up);
    }
}
