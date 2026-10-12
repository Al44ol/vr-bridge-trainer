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
// B2: порядок групп — BridgeMenu.SetGroupOrder (меньше — выше); команда с
// hideWhenUnavailable не показывается, пока недоступна (команды сценария по фазе:
// «Начать» — только на брифинге и т. п.), панель пересобирается сама.
// Прыжок шаблона XR (тоже кнопка A) отключает Setup B1 Water Area.
// B3: RegisterStepper — строка «< значение >» (выбор значения стрелками, меню не закрывается).
// B4: несколько меню — у команды признак Menu (пусто — меню A), у экземпляра
// BridgeMenu — menuId. Второе меню «Параметры» (SettingsMenuId) — на кнопке Y
// левого контроллера: свет рубки, подсветка приборов, экспозиция ночи.
//     BridgeMenu.RegisterStepper("Свет", …).InMenu(BridgeMenu.SettingsMenuId);
// Прежние вызовы Register попадают в меню A, как раньше. Открытие одного меню закрывает другие.
// D1: подменю — меню было перегружено. Группа показывается одной кнопкой «Вводные »», на её
// странице — команды группы и «« Назад»; меню открывается с главной страницы. Группы, отмеченные
// SetGroupExpanded (сценарий, сигнализация, общие параметры), раскрыты прямо на главной.
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
    /// <summary>Недоступную команду не показывать (а не делать серой) — B2: команды сценария по фазе.</summary>
    public bool HideWhenUnavailable;

    /// <summary>Переключатель «◀ значение ▶» (B3): кнопки-стрелки по бокам, меню не закрывается.</summary>
    public Action Previous, Next;
    public bool IsStepper => Previous != null || Next != null;

    /// <summary>В каком меню команда (B4): пусто — меню A, BridgeMenu.SettingsMenuId — меню «Параметры» (Y).</summary>
    public string Menu = "";

    public bool Available => IsAvailable == null || IsAvailable();
    public bool Visible => !HideWhenUnavailable || Available;

    /// <summary>Перенести команду в другое меню (B4).</summary>
    public BridgeMenuCommand InMenu(string menu)
    {
        Menu = menu ?? "";
        BridgeMenu.RebuildOpen();
        return this;
    }
}

public class BridgeMenu : MonoBehaviour
{
    // ── Реестр команд (статический: модули регистрируются в любом порядке) ──
    private static readonly List<BridgeMenuCommand> commands = new List<BridgeMenuCommand>();
    public static IReadOnlyList<BridgeMenuCommand> Commands => commands;
    /// <summary>Меню A (основное).</summary>
    public static BridgeMenu Instance { get; private set; }
    /// <summary>Меню «Параметры» на кнопке Y (B4).</summary>
    public const string SettingsMenuId = "Параметры";
    private static readonly List<BridgeMenu> instances = new List<BridgeMenu>();

    // Если в настройках Play Mode отключена перезагрузка домена — не тащить команды прошлого запуска
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        commands.Clear();
        groupOrder.Clear();
        expandedGroups.Clear();
        instances.Clear();
        Instance = null;
    }

    // Порядок групп (B2): меньше — выше; без порядка — 0, при равенстве — порядок первой регистрации
    private static readonly Dictionary<string, int> groupOrder = new Dictionary<string, int>();

    public static void SetGroupOrder(string group, int order)
    {
        groupOrder[group ?? ""] = order;
        RebuildOpen();
    }

    // Группы, раскрытые на главной странице (D1); остальные — подменю
    private static readonly HashSet<string> expandedGroups = new HashSet<string>();

    /// <summary>Команды группы — прямо на главной странице (true) или в подменю (false, по умолчанию).</summary>
    public static void SetGroupExpanded(string group, bool expanded)
    {
        if (expanded) expandedGroups.Add(group ?? "");
        else expandedGroups.Remove(group ?? "");
        RebuildOpen();
    }

    private static bool IsExpanded(string group) => string.IsNullOrEmpty(group) || expandedGroups.Contains(group);

    /// <summary>Пересобрать открытые меню (реестр изменился).</summary>
    public static void RebuildOpen()
    {
        foreach (BridgeMenu m in instances)
            if (m != null && m.IsOpen) m.Rebuild();
    }

    public static BridgeMenuCommand Register(string group, Func<string> title, Action execute,
                                             Func<bool> isAvailable = null, bool instructorOnly = false,
                                             bool hideWhenUnavailable = false)
    {
        var c = new BridgeMenuCommand
        {
            Group = group, Title = title, Execute = execute, IsAvailable = isAvailable,
            InstructorOnly = instructorOnly, HideWhenUnavailable = hideWhenUnavailable
        };
        commands.Add(c);
        RebuildOpen();
        return c;
    }

    public static BridgeMenuCommand Register(string group, string title, Action execute,
                                             Func<bool> isAvailable = null, bool instructorOnly = false,
                                             bool hideWhenUnavailable = false) =>
        Register(group, () => title, execute, isAvailable, instructorOnly, hideWhenUnavailable);

    /// <summary>
    /// Переключатель значения «◀ title ▶» (B3): previous/next — по стрелкам, меню остаётся открытым.
    /// </summary>
    public static BridgeMenuCommand RegisterStepper(string group, Func<string> title, Action previous, Action next,
                                                    Func<bool> isAvailable = null, bool instructorOnly = false,
                                                    bool hideWhenUnavailable = false)
    {
        BridgeMenuCommand c = Register(group, title, null, isAvailable, instructorOnly, hideWhenUnavailable);
        c.Previous = previous;
        c.Next = next;
        c.CloseOnExecute = false;
        RebuildOpen();
        return c;
    }

    public static void Unregister(BridgeMenuCommand c)
    {
        if (c == null) return;
        commands.Remove(c);
        RebuildOpen();
    }

    [Header("Положение панели")]
    public float distanceFromHead = 0.6f;
    public float downOffset = 0.25f;
    [Tooltip("Масштаб холста, м на пиксель")]
    public float canvasScale = 0.0012f;

    [Header("Управление")]
    [Tooltip("Кнопка вызова: A на правом контроллере (меню «Параметры» — Y левого: {LeftHand}/secondaryButton)")]
    public string toggleBinding = "<XRController>{RightHand}/primaryButton";
    [Tooltip("Какие команды показывает (B4): пусто — меню A, «Параметры» — BridgeMenu.SettingsMenuId")]
    public string menuId = "";
    [Tooltip("Заголовок панели")]
    public string menuTitle = "Меню мостика";

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
    private readonly List<BridgeMenuCommand> builtVisible = new List<BridgeMenuCommand>();
    private readonly List<(BridgeMenuCommand cmd, Button btn, Image img)> arrows = new List<(BridgeMenuCommand, Button, Image)>();
    private static readonly Color StepperColor = new Color(0.16f, 0.26f, 0.38f);
    private static readonly Color SubmenuColor = new Color(0.2f, 0.3f, 0.42f);
    /// <summary>Открытое подменю (группа); null — главная страница.</summary>
    private string page;

    public bool IsOpen => canvas != null && canvas.enabled;

    void Awake()
    {
        foreach (BridgeMenu m in instances)
            if (m != null && m != this && (m.menuId ?? "") == (menuId ?? ""))
                Debug.LogWarning($"[BridgeMenu] В сцене несколько меню «{menuId}» — команды покажут оба.", this);
        if (!instances.Contains(this)) instances.Add(this);
        if (string.IsNullOrEmpty(menuId) && Instance == null) Instance = this;
    }

    // Команда показывается в этом меню
    private bool Shows(BridgeMenuCommand c) => (c.Menu ?? "") == (menuId ?? "") && c.Visible;

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
        instances.Remove(this);
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
        foreach (BridgeMenu m in instances)
            if (m != null && m != this && m.IsOpen) m.Close();
        page = null;   // меню всегда открывается с главной страницы
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
        arrows.Clear();

        // Видимые сейчас команды; порядок групп — SetGroupOrder, затем порядок первой регистрации
        var visible = new List<BridgeMenuCommand>();
        foreach (BridgeMenuCommand c in commands) if (Shows(c)) visible.Add(c);
        builtVisible.Clear();
        builtVisible.AddRange(visible);

        var groups = new List<string>();
        foreach (BridgeMenuCommand c in visible)
            if (!groups.Contains(c.Group ?? "")) groups.Add(c.Group ?? "");
        var firstSeen = new List<string>(groups);
        groups.Sort((a, b) =>
        {
            int oa = groupOrder.TryGetValue(a, out int va) ? va : 0, ob = groupOrder.TryGetValue(b, out int vb) ? vb : 0;
            return oa != ob ? oa.CompareTo(ob) : firstSeen.IndexOf(a).CompareTo(firstSeen.IndexOf(b));
        });

        // Подменю (D1): открытая группа пропала (все её команды скрыты) — на главную
        if (page != null && !groups.Contains(page)) page = null;
        bool root = page == null;
        // На главной: раскрытые группы — заголовком и командами, остальные — кнопкой подменю;
        // на странице подменю — «« Назад» и команды группы
        var shown = new List<string>();
        var submenus = new List<string>();
        foreach (string g in groups)
        {
            if (!root) { if (g == page) shown.Add(g); }
            else if (IsExpanded(g)) shown.Add(g);
            else submenus.Add(g);
        }

        float rows = 0f;
        rows += 34f;                                             // заголовок
        if (!root) rows += ButtonHeight + 6f;                    // «Назад»
        foreach (string g in shown)
        {
            if (root) rows += GroupHeight;
            foreach (BridgeMenuCommand c in visible) if ((c.Group ?? "") == g) rows += ButtonHeight + 6f;
        }
        if (submenus.Count > 0) rows += GroupHeight + submenus.Count * (ButtonHeight + 6f);
        if (visible.Count == 0) rows += 30f;
        rows += ButtonHeight + 10f;                              // «Закрыть»
        float height = rows + 2f * Pad;
        background.sizeDelta = new Vector2(Width, height);

        float y = height * 0.5f - Pad;
        Label(root ? menuTitle : $"{menuTitle} · {page}", ref y, 34f, 22f, new Color(0.85f, 0.92f, 1f), TextAlignmentOptions.Center);
        if (visible.Count == 0)
            Label("Нет команд", ref y, 30f, 16f, new Color(0.6f, 0.6f, 0.6f), TextAlignmentOptions.Center);
        if (!root)
        {
            (Button back, Image backImg, TextMeshProUGUI backLabel) = MakeButton(ref y, () => OpenPage(null));
            backImg.color = new Color(0.35f, 0.35f, 0.4f);
            backLabel.text = "« Назад";
        }

        foreach (string g in shown)
        {
            if (root) Label(g, ref y, GroupHeight, 15f, new Color(0.62f, 0.68f, 0.75f), TextAlignmentOptions.Left);
            foreach (BridgeMenuCommand c in visible)
            {
                if ((c.Group ?? "") != g) continue;
                BridgeMenuCommand cmd = c;
                if (cmd.IsStepper)
                {
                    // «◀» — значение — «▶» в одной строке; меню не закрывается
                    const float arrow = 48f, gap = 6f;
                    float full = Width - 2f * Pad, mid = full - 2f * (arrow + gap);
                    float yRow = y;
                    (Button prev, Image prevImg, TextMeshProUGUI prevLabel) = MakeButton(ref yRow, () => Step(cmd, cmd.Previous), arrow, -0.5f * full + 0.5f * arrow);
                    yRow = y;
                    (Button next, Image nextImg, TextMeshProUGUI nextLabel) = MakeButton(ref yRow, () => Step(cmd, cmd.Next), arrow, 0.5f * full - 0.5f * arrow);
                    (Button btn, Image img, TextMeshProUGUI label) = MakeButton(ref y, null, mid, 0f);
                    // Символы «<» «>» — есть в шрифте TMP по умолчанию (◀ ▶ в LiberationSans нет)
                    prevLabel.richText = nextLabel.richText = false;
                    prevLabel.text = "<"; nextLabel.text = ">";
                    prevLabel.fontSize = nextLabel.fontSize = 26f;
                    prevLabel.fontStyle = nextLabel.fontStyle = FontStyles.Bold;
                    arrows.Add((cmd, prev, prevImg));
                    arrows.Add((cmd, next, nextImg));
                    items.Add((cmd, btn, img, label));
                    continue;
                }
                (Button b0, Image i0, TextMeshProUGUI l0) = MakeButton(ref y, () => Execute(cmd));
                items.Add((cmd, b0, i0, l0));
            }
        }
        if (submenus.Count > 0)
        {
            Label("Разделы", ref y, GroupHeight, 15f, new Color(0.62f, 0.68f, 0.75f), TextAlignmentOptions.Left);
            foreach (string g in submenus)
            {
                string group = g;
                (Button sb, Image si, TextMeshProUGUI sl) = MakeButton(ref y, () => OpenPage(group));
                si.color = SubmenuColor;
                sl.text = $"{group}  »";   // «▸» в шрифте TMP по умолчанию нет
            }
        }
        y -= 4f;
        (Button close, Image closeImg, TextMeshProUGUI closeLabel) = MakeButton(ref y, Close);
        closeImg.color = new Color(0.35f, 0.35f, 0.4f);
        closeLabel.text = "Закрыть";
        RefreshStates();
    }

    /// <summary>Открыть подменю группы (null — главная страница).</summary>
    private void OpenPage(string group)
    {
        page = group;
        Rebuild();
    }

    private void Execute(BridgeMenuCommand c)
    {
        if (!c.Available) return;
        try { c.Execute?.Invoke(); }
        catch (Exception e) { Debug.LogException(e, this); }
        if (c.CloseOnExecute) Close();
        else RefreshStates();
    }

    private void Step(BridgeMenuCommand c, Action a)
    {
        if (!c.Available || a == null) return;
        try { a(); }
        catch (Exception e) { Debug.LogException(e, this); }
        RefreshStates();
    }

    private void RefreshStates()
    {
        // Набор видимых команд изменился (фаза сценария) — пересобрать панель
        int n = 0;
        bool changed = false;
        foreach (BridgeMenuCommand c in commands)
        {
            if (!Shows(c)) continue;
            if (n >= builtVisible.Count || builtVisible[n] != c) { changed = true; break; }
            n++;
        }
        if (changed || n != builtVisible.Count)
        {
            Rebuild();
            return;
        }

        foreach (var a in arrows)
        {
            bool on = a.cmd.Available;
            a.btn.interactable = on;
            a.img.color = on ? ButtonColor : DisabledColor;
        }
        foreach (var it in items)
        {
            bool on = it.cmd.Available;
            it.btn.interactable = on && !it.cmd.IsStepper;
            it.img.color = !on ? DisabledColor : it.cmd.IsStepper ? StepperColor : ButtonColor;
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

    // width < 0 — во всю ширину; x — центр кнопки по горизонтали
    private (Button, Image, TextMeshProUGUI) MakeButton(ref float y, UnityEngine.Events.UnityAction action, float width = -1f, float x = 0f)
    {
        var go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(background, false);
        var img = go.AddComponent<Image>();
        img.color = ButtonColor;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (action != null) btn.onClick.AddListener(action);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width > 0f ? width : Width - 2f * Pad, ButtonHeight);
        rt.anchoredPosition = new Vector2(x, y - ButtonHeight * 0.5f);
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
