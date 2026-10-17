// ============================================================
// ScenarioEditorApp.cs — Редактор сценариев и карт на мониторе (запрос 21.2а — каркас и акватория)
//
// Сцена «ScenarioEditor» создаётся кодом (TrainerScenes.OpenEditor), всё окно строится здесь же.
// Монитор: своя камера без XR + холст 1920 × 1080 (как окно инструктора); в шлеме — надпись
// «Редактор открыт на мониторе».
//   ┌ верх: Новая · Открыть · Сохранить · Сохранить как · Отменить · Повторить · Сетка · Привязка · Тренажёр
//   │ слева — палитра (инструменты), в центре — карта с сеткой, справа — свойства и проверка
//   └ низ: координаты под курсором, глубина, масштаб, файл, черновик
// Мышь: колесо — масштаб к курсору; правая/средняя кнопка (и левая на пустом месте) — сдвиг;
// «Границы района» — тянуть ручки рамки (с привязкой к сетке). Клавиши: Ctrl+Z / Ctrl+Y, Ctrl+S, Esc.
// Документ — AreaDocument (Core): правка → Commit (отмена), сохранение с историей, черновик раз в 2 мин.
// 21.2б — знаки МАМС: палитра 8 типов, щелчок по пустому месту — поставить в узел сетки, щелчок по
// знаку — выделить и тянуть, Delete — удалить, справа — свойства знака (имя, тип, положение, огонь);
// правка знаков — AidEditing (Core). 21.3а — мели и суша (ScenarioEditorShapes.cs, partial-класс);
// 21.3б — фарватеры и причалы (там же); 21.4 — слой сценария.
// ============================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR;

public partial class ScenarioEditorApp : MonoBehaviour
{
    private enum Tool { Select, Bounds, Aid, Shoal, Land, Fairway, Berth }

    private const float TopBar = 56f, PaletteWidth = 230f, PropsWidth = 440f, StatusBar = 32f, Pad = 8f;
    private static readonly Color Background = new Color(0.05f, 0.06f, 0.08f, 1f);

    private readonly MonitorUi ui = new MonitorUi();
    private readonly MapViewport viewport = new MapViewport { Follow = false, MinMetersPerPixel = 0.1, MaxMetersPerPixel = 400.0 };
    private Canvas canvas;
    private RectTransform root, mapRect, dialog;
    private DebriefMapGraphic mapStatic;
    private EditorMapOverlay overlay;
    private TextMeshProUGUI titleText, checkText, statusText;
    private TMP_InputField nameField, depthField, descField, eMinField, eMaxField, nMinField, nMaxField;
    // 21.2б: знаки
    private RectTransform areaGroup, aidGroup;
    private TMP_InputField aidNameField, aidEField, aidNField, aidLightField;
    private readonly List<TextMeshProUGUI> aidLabels = new List<TextMeshProUGUI>();
    private AidType aidType = AidType.LateralPort;
    private int selectedAid = -1, hoverAid = -1;
    private bool dragAid, dragMoved;
    private Vector2 pressLocal;

    private AreaDocument doc;
    private DebriefMapData mapData;
    private int builtVersion = -1;
    private bool mapDirty = true;
    private Tool tool = Tool.Select;
    private int gridIndex = 1;   // 50 м
    private bool snap = true;
    private int dragHandle = -1;
    private bool panning, dialogOpen;
    private Vector2 lastLocal;
    private float nextDraft;
    private string lastDraftText = "";

    private static string Root => TrainerScenes.Root;
    private static ScenarioCatalog Catalog => TrainerScenes.Catalog ?? new ScenarioCatalog();
    private double GridStep => MapGrid.Steps[Mathf.Clamp(gridIndex, 0, MapGrid.Steps.Length - 1)];

    // ─────────── Запуск ───────────
    private IEnumerator Start()
    {
        // Тренажёр ещё выгружается — подождать (иначе два EventSystem и чужие камеры на кадр)
        AsyncOperation op = TrainerScenes.Unloading;
        while (op != null && !op.isDone) yield return null;

        SetupCameras();
        SetupEventSystem();
        Build();
        ui.AfterClick = Refresh;
        nextDraft = Time.unscaledTime + (float)AreaDocument.DraftPeriodS;

        AreaDocument draft = AreaDocument.ReadDraft(Root, out DateTime when);
        if (draft != null)
            Dialog("Несохранённый черновик",
                   $"После прошлого сеанса остался черновик акватории «{draft.Area.name}» от {when:dd.MM HH:mm} ({draft.Origin}).",
                   null, false,
                   ("Восстановить", MonitorUi.StartColor, () => { CloseDialog(); SetDocument(draft); }),
                   ("Удалить черновик", MonitorUi.StopColor, () => { AreaDocument.DeleteDraft(Root); CloseDialog(); OpenDialog(true); }));
        else OpenDialog(true);
    }

    private void SetupCameras()
    {
        // Монитор: только интерфейс, без XR
        var mon = new GameObject("EditorMonitorCamera").AddComponent<Camera>();
        mon.transform.SetParent(transform, false);
        mon.stereoTargetEye = StereoTargetEyeMask.None;
        mon.cullingMask = 0;
        mon.clearFlags = CameraClearFlags.SolidColor;
        mon.backgroundColor = Background;
        mon.depth = 100;
        UniversalAdditionalCameraData md = mon.GetUniversalAdditionalCameraData();
        md.allowXRRendering = false;
        md.renderPostProcessing = false;
        md.renderShadows = false;
        mon.gameObject.AddComponent<AudioListener>();

        // Шлем: надпись перед глазами (камера XR, на монитор не выводится)
        var hmd = new GameObject("EditorHeadsetCamera").AddComponent<Camera>();
        hmd.transform.SetParent(transform, false);
        hmd.clearFlags = CameraClearFlags.SolidColor;
        hmd.backgroundColor = Background;
        hmd.nearClipPlane = 0.05f;
        hmd.depth = 0;
        var note = new GameObject("HeadsetNote", typeof(RectTransform));
        note.transform.SetParent(hmd.transform, false);
        note.transform.localPosition = new Vector3(0f, 0f, 2f);
        note.transform.localScale = Vector3.one * 0.002f;
        var nc = note.AddComponent<Canvas>();
        nc.renderMode = RenderMode.WorldSpace;
        nc.GetComponent<RectTransform>().sizeDelta = new Vector2(900f, 300f);
        TextMeshProUGUI t = MonitorUi.Text(note.transform, "Редактор сценариев открыт на мониторе.\nСнимите шлем — работа мышью за компьютером.",
                                           40f, Color.white, TextAlignmentOptions.Center);
        MonitorUi.Stretch(t.rectTransform, 0f, 0f, 0f, 0f);

        // Зеркало шлема на мониторе — выключить (как в режиме окна инструктора)
        XRSettings.gameViewRenderMode = GameViewRenderMode.None;
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        foreach (XRDisplaySubsystem d in displays) if (d.running) d.SetPreferredMirrorBlitMode(XRMirrorViewBlitMode.None);
    }

    private static void SetupEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        var m = go.AddComponent<InputSystemUIInputModule>();
        m.AssignDefaultActions();
    }

    // ─────────── Окно ───────────
    private void Build()
    {
        canvas = MonitorUi.Canvas(transform, "EditorCanvas", 10);
        root = canvas.GetComponent<RectTransform>();

        // Верхняя панель
        RectTransform bar = MonitorUi.Panel(root, "TopBar", MonitorUi.BarColor);
        MonitorUi.Place(bar, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, TopBar), new Vector2(0.5f, 1f));
        float x = Pad, y = -(TopBar - MonitorUi.RowHeight) * 0.5f;
        ui.Button(bar, ref x, y, 100f, "Новая", MonitorUi.NeutralColor, () => Guarded(NewDialog));
        ui.Button(bar, ref x, y, 120f, "Открыть…", MonitorUi.NeutralColor, () => Guarded(() => OpenDialog(false)));
        ui.Button(bar, ref x, y, 120f, "Сохранить", MonitorUi.ButtonColor, () => Save(false), null,
                  () => doc != null && doc.Dirty ? MonitorUi.WarnColor : MonitorUi.ButtonColor, () => doc != null);
        ui.Button(bar, ref x, y, 160f, "Сохранить как…", MonitorUi.NeutralColor, SaveAsDialog, null, null, () => doc != null);
        x += 12f;
        ui.Button(bar, ref x, y, 120f, "Отменить", MonitorUi.NeutralColor, Undo, null, null, () => doc != null && doc.History.CanUndo);
        ui.Button(bar, ref x, y, 120f, "Повторить", MonitorUi.NeutralColor, Redo, null, null, () => doc != null && doc.History.CanRedo);
        x += 12f;
        ui.Button(bar, ref x, y, 150f, "", MonitorUi.NeutralColor, () => { gridIndex = (gridIndex + 1) % MapGrid.Steps.Length; mapDirty = true; },
                  () => "Сетка: " + MapGrid.StepTitle(GridStep));
        ui.Button(bar, ref x, y, 160f, "", MonitorUi.NeutralColor, () => snap = !snap,
                  () => snap ? "Привязка: вкл" : "Привязка: выкл", () => snap ? MonitorUi.ActiveColor : MonitorUi.NeutralColor);
        ui.Button(bar, ref x, y, 150f, "Весь район", MonitorUi.NeutralColor, FitArea, null, null, () => doc != null);
        titleText = MonitorUi.Text(bar, "", 18f, Color.white, TextAlignmentOptions.Left);
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        titleText.overflowMode = TextOverflowModes.Ellipsis;
        MonitorUi.TopLeft(titleText.rectTransform, x + 10f, y, 1920f - x - 10f - 2f * 170f - 3f * Pad, MonitorUi.RowHeight);
        float rx = 1920f - Pad - 2f * 170f - MonitorUi.Gap;
        ui.Button(bar, ref rx, y, 170f, "В тренажёр", MonitorUi.StartColor, () => Guarded(TrainerScenes.OpenTraining));
        ui.Button(bar, ref rx, y, 170f, "Выход", MonitorUi.NeutralColor, () => Guarded(TrainerScenes.Quit));

        BuildPalette();
        BuildMap();
        BuildProps();

        // Строка состояния
        RectTransform sb = MonitorUi.Panel(root, "StatusBar", MonitorUi.BarColor);
        MonitorUi.Place(sb, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, StatusBar), new Vector2(0.5f, 0f));
        statusText = MonitorUi.Text(sb, "", 15f, MonitorUi.MutedColor, TextAlignmentOptions.Left);
        statusText.textWrappingMode = TextWrappingModes.NoWrap;
        MonitorUi.Stretch(statusText.rectTransform, 12f, 0f, 12f, 0f);
    }

    private void BuildPalette()
    {
        RectTransform p = MonitorUi.Panel(root, "Palette", MonitorUi.PanelColor);
        MonitorUi.Place(p, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, StatusBar), new Vector2(PaletteWidth, -(TopBar + StatusBar)), new Vector2(0f, 0f));
        float y = -Pad;
        Header(p, "Инструменты", ref y);
        float w = PaletteWidth - 2f * Pad;
        float x = Pad;
        ui.Button(p, ref x, y, w, "Выбор / сдвиг", MonitorUi.NeutralColor, () => SetTool(Tool.Select), null,
                  () => tool == Tool.Select ? MonitorUi.ActiveColor : MonitorUi.NeutralColor);
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
        x = Pad;
        ui.Button(p, ref x, y, w, "Границы района", MonitorUi.NeutralColor, () => SetTool(Tool.Bounds), null,
                  () => tool == Tool.Bounds ? MonitorUi.ActiveColor : MonitorUi.NeutralColor, () => doc != null);
        y -= MonitorUi.RowHeight + 3f * MonitorUi.Gap;

        // 21.2б: знаки МАМС — выбрать тип, щелчок по карте ставит знак
        Header(p, "Знаки МАМС", ref y);
        foreach (AidType t in AidEditing.Palette)
        {
            AidType tt = t;
            x = Pad;
            RectTransform b = ui.Button(p, ref x, y, w, AidTitle(tt), MonitorUi.NeutralColor, () => { aidType = tt; SetTool(Tool.Aid); }, null,
                                        () => tool == Tool.Aid && aidType == tt ? MonitorUi.ActiveColor : MonitorUi.NeutralColor, () => doc != null);
            TextMeshProUGUI bt = b.GetComponentInChildren<TextMeshProUGUI>();
            bt.alignment = TextAlignmentOptions.Left;
            bt.rectTransform.offsetMin = new Vector2(36f, bt.rectTransform.offsetMin.y);
            var sw = new GameObject("Swatch", typeof(RectTransform));
            sw.transform.SetParent(b, false);
            MonitorUi.Place(sw.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(26f, 26f), new Vector2(0.5f, 0.5f));
            EditorMapOverlay g = sw.AddComponent<EditorMapOverlay>();
            g.raycastTarget = false;
            g.Swatch = tt;
            y -= MonitorUi.RowHeight + MonitorUi.Gap;
        }
        y -= 2f * MonitorUi.Gap;
        BuildShapePalette(p, ref y, w);   // 21.3а, 21.3б: мель, суша, фарватер, причал
        y -= 2f * MonitorUi.Gap;
        TextMeshProUGUI help = MonitorUi.Text(p,
            "Колесо — масштаб; правая кнопка — сдвиг\n" +
            "Знак: щелчок — поставить; по знаку — тянуть\n" +
            "Мель, суша: щелчки, Enter — замкнуть\n" +
            "Фарватер: щелчки, Enter — закончить\n" +
            "Причал: два щелчка — A и B, вода справа\n" +
            "Delete — удалить; Esc — «Выбор» / снять\nCtrl+Z / Ctrl+Y, Ctrl+S",
            14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(help.rectTransform, Pad, y, w, 200f);
    }

    private static string AidTitle(AidType t)
    {
        switch (t)
        {
            case AidType.LateralPort: return "Левый латеральный";
            case AidType.LateralStarboard: return "Правый латеральный";
            case AidType.CardinalNorth: return "Северный (N)";
            case AidType.CardinalEast: return "Восточный (E)";
            case AidType.CardinalSouth: return "Южный (S)";
            case AidType.CardinalWest: return "Западный (W)";
            case AidType.IsolatedDanger: return "Отдельной опасности";
            default: return "Осевой";
        }
    }

    private void BuildMap()
    {
        var holder = new GameObject("Map", typeof(RectTransform));
        holder.transform.SetParent(root, false);
        mapRect = holder.GetComponent<RectTransform>();
        MonitorUi.Stretch(mapRect, PaletteWidth + Pad, StatusBar + Pad, PropsWidth + Pad, TopBar + Pad);
        holder.AddComponent<RectMask2D>();
        var hit = holder.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);

        mapStatic = Layer<DebriefMapGraphic>("MapStatic");
        mapStatic.ShowGrid = false;
        overlay = Layer<EditorMapOverlay>("Overlay");
        overlay.Viewport = viewport;
    }

    private T Layer<T>(string name) where T : MaskableGraphic
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(mapRect, false);
        MonitorUi.Stretch(go.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
        T g = go.AddComponent<T>();
        g.raycastTarget = false;
        return g;
    }

    private void BuildProps()
    {
        RectTransform p = MonitorUi.Panel(root, "Props", MonitorUi.PanelColor);
        MonitorUi.Place(p, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, StatusBar), new Vector2(PropsWidth, -(TopBar + StatusBar)), new Vector2(1f, 0f));
        float w = PropsWidth - 2f * Pad, y = -Pad;
        float half = (w - MonitorUi.Gap) * 0.5f;
        // Свойства акватории — когда знак не выделен
        RectTransform g = areaGroup = Group(p, "AreaProps");
        Header(g, "Акватория", ref y);
        nameField = Row(g, "Имя", ref y, w, false, TMP_InputField.ContentType.Standard, v => Edit(() => doc.Rename(v)));
        depthField = Row(g, "Глубина по умолчанию, м", ref y, w, false, TMP_InputField.ContentType.Standard,
                         v => EditNumber(v, d => d > 0.0, d => doc.Area.defaultDepth = (float)d));
        Label(g, "Описание", ref y, w);
        descField = MonitorUi.Field(g, Pad, y, w, 90f, "для кого и для чего", true, TMP_InputField.ContentType.Standard,
                                    v => Edit(() => { doc.Area.description = v; doc.Commit(); }), 16f);
        y -= 90f + 2f * MonitorUi.Gap;
        Header(g, "Границы района, м", ref y);
        Label(g, "Восток: от … до", ref y, w);
        eMinField = MonitorUi.Field(g, Pad, y, half, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditBound(v, 0));
        eMaxField = MonitorUi.Field(g, Pad + half + MonitorUi.Gap, y, half, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditBound(v, 1));
        y -= 34f + MonitorUi.Gap;
        Label(g, "Север: от … до", ref y, w);
        nMinField = MonitorUi.Field(g, Pad, y, half, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditBound(v, 2));
        nMaxField = MonitorUi.Field(g, Pad + half + MonitorUi.Gap, y, half, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditBound(v, 3));
        y -= 34f + 3f * MonitorUi.Gap;

        // 21.2б: свойства выделенного знака — на месте свойств акватории
        g = aidGroup = Group(p, "AidProps");
        float ya = -Pad;
        Header(g, "Знак МАМС", ref ya);
        TextMeshProUGUI hint = MonitorUi.Text(g, "Delete — удалить · Esc — снять выделение (к свойствам акватории)", 14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(hint.rectTransform, Pad, ya, w, 20f);
        ya -= 24f;
        aidNameField = Row(g, "Имя", ref ya, w, false, TMP_InputField.ContentType.Standard,
                           v => EditAid(x => { string n = (v ?? "").Trim(); if (n.Length == 0) return false; x.name = n; return true; }));
        Label(g, "Тип", ref ya, w);
        float xa = Pad;
        ui.Button(g, ref xa, ya, 48f, "<", MonitorUi.NeutralColor, () => CycleAidType(-1));
        ui.Button(g, ref xa, ya, w - 2f * 48f - 2f * MonitorUi.Gap, "", MonitorUi.NeutralColor, () => CycleAidType(1),
                  () => SelAid != null ? IalaMarks.Get(SelAid.type).TitleRu : "");
        ui.Button(g, ref xa, ya, 48f, ">", MonitorUi.NeutralColor, () => CycleAidType(1));
        ya -= MonitorUi.RowHeight + MonitorUi.Gap;
        Label(g, "Положение, м: восток … север", ref ya, w);
        aidEField = MonitorUi.Field(g, Pad, ya, half, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditAidPos(v, true));
        aidNField = MonitorUi.Field(g, Pad + half + MonitorUi.Gap, ya, half, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditAidPos(v, false));
        ya -= 34f + MonitorUi.Gap;
        Label(g, "Огонь (пусто — по типу знака)", ref ya, w);
        aidLightField = MonitorUi.Field(g, Pad, ya, w, 34f, "", false, TMP_InputField.ContentType.Standard,
                                        v => EditAid(x => { x.lightCharacter = (v ?? "").Trim(); return true; }));
        ya -= 34f + MonitorUi.Gap;
        TextMeshProUGUI lh = MonitorUi.Text(g, "По МАМС: Fl R 4s · Fl G 4s · Q · Q(3) 10s · Q(6)+LFl 15s · Q(9) 15s · Fl(2) 5s · LFl 10s",
                                            14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(lh.rectTransform, Pad, ya, w, 40f);
        ya -= 44f;
        xa = Pad;
        ui.Button(g, ref xa, ya, half, "Огонь по умолчанию", MonitorUi.NeutralColor, () => EditAid(x => { x.lightCharacter = ""; return true; }), null, null,
                  () => SelAid != null && !string.IsNullOrEmpty(SelAid.lightCharacter));
        ui.Button(g, ref xa, ya, half, "Удалить знак", MonitorUi.StopColor, DeleteAid);
        ya -= MonitorUi.RowHeight + 3f * MonitorUi.Gap;
        float ys = BuildShapeProps(p, w);   // 21.3а, 21.3б: свойства мели / суши / фарватера
        float yb = BuildBerthProps(p, w);   // 21.3б: свойства причала
        ShowPropsGroup();

        float yc = Mathf.Min(Mathf.Min(y, ya), Mathf.Min(ys, yb));
        Header(p, "Проверка", ref yc);
        checkText = MonitorUi.Text(p, "", 15f, Color.white, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(checkText.rectTransform, Pad, yc, w, 1080f - TopBar - StatusBar + yc - Pad);
    }

    private static RectTransform Group(RectTransform p, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(p, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        MonitorUi.Stretch(rt, 0f, 0f, 0f, 0f);
        return rt;
    }

    private TMP_InputField Row(RectTransform p, string label, ref float y, float w, bool multiline, TMP_InputField.ContentType type, UnityEngine.Events.UnityAction<string> onEnd)
    {
        Label(p, label, ref y, w);
        TMP_InputField f = MonitorUi.Field(p, Pad, y, w, 34f, "", multiline, type, onEnd);
        y -= 34f + MonitorUi.Gap;
        return f;
    }

    private static void Label(RectTransform p, string text, ref float y, float w)
    {
        TextMeshProUGUI t = MonitorUi.Text(p, text, 15f, MonitorUi.MutedColor, TextAlignmentOptions.BottomLeft);
        MonitorUi.TopLeft(t.rectTransform, Pad, y, w, 22f);
        y -= 22f;
    }

    private static void Header(RectTransform p, string text, ref float y)
    {
        TextMeshProUGUI t = MonitorUi.Text(p, text, 19f, MonitorUi.TitleColor, TextAlignmentOptions.TopLeft);
        t.fontStyle = FontStyles.Bold;
        MonitorUi.TopLeft(t.rectTransform, Pad, y, 600f, 28f);
        y -= 30f;
    }

    // ─────────── Документ ───────────
    private void SetDocument(AreaDocument d)
    {
        doc = d;
        lastDraftText = "";
        mapDirty = true;
        selectedAid = -1;
        dragAid = false;
        selShape = AreaShape.None;
        draft.Clear();
        dragVertex = -1;
        dragShape = false;
        ShowPropsGroup();
        FitArea();
        FillFields();
        Refresh();
        Debug.Log($"[Редактор] Акватория «{d.Area.name}» ({d.Origin}).");
    }

    private void FitArea()
    {
        if (doc == null) return;
        Canvas.ForceUpdateCanvases();   // размер карты — до первой раскладки кадра
        viewport.SetSize(Mathf.Max(1f, mapRect.rect.width), Mathf.Max(1f, mapRect.rect.height));
        var r = new MapRect(doc.Area.boundsMin.East, doc.Area.boundsMin.North, doc.Area.boundsMax.East, doc.Area.boundsMax.North);
        if (r.Width > 0 && r.Height > 0) viewport.Fit(r, 0.06);
        mapDirty = true;
    }

    // Правка из поля: применить, снимок в историю, обновить карту и проверку
    private void Edit(Action change)
    {
        if (doc == null) return;
        change();
        mapDirty = true;
        FillFields();
        Refresh();
    }

    private void EditNumber(string text, Func<double, bool> ok, Action<double> set)
    {
        if (doc == null) return;
        if (MonitorUi.TryNumber(text, out double v) && ok(v)) Edit(() => { set(v); doc.Commit(); });
        else FillFields();   // не число — вернуть прежнее
    }

    private void EditBound(string text, int which)
    {
        if (doc == null || !MonitorUi.TryNumber(text, out double v)) { FillFields(); return; }
        Edit(() =>
        {
            WaterArea a = doc.Area;
            switch (which)
            {
                case 0: a.boundsMin = new WorldPosition(v, a.boundsMin.North); break;
                case 1: a.boundsMax = new WorldPosition(v, a.boundsMax.North); break;
                case 2: a.boundsMin = new WorldPosition(a.boundsMin.East, v); break;
                default: a.boundsMax = new WorldPosition(a.boundsMax.East, v); break;
            }
            doc.Commit();
        });
    }

    private void FillFields()
    {
        if (doc == null) return;
        WaterArea a = doc.Area;
        Set(nameField, a.name);
        Set(depthField, MonitorUi.Num(a.defaultDepth, 1));
        Set(descField, a.description ?? "");
        Set(eMinField, MonitorUi.Num(a.boundsMin.East, 0));
        Set(eMaxField, MonitorUi.Num(a.boundsMax.East, 0));
        Set(nMinField, MonitorUi.Num(a.boundsMin.North, 0));
        Set(nMaxField, MonitorUi.Num(a.boundsMax.North, 0));
        FillAidFields();
        FillShapeFields();
    }

    // ─────────── Знаки (21.2б) ───────────
    private AidToNavigation SelAid => doc != null && AidEditing.Valid(doc.Area, selectedAid) ? doc.Area.aids[selectedAid] : null;

    /// <summary>Выделить знак (−1 — снять); незаконченный ввод в поле сначала применяется к прежнему знаку.</summary>
    private void SelectAid(int i)
    {
        if (doc == null || !AidEditing.Valid(doc.Area, i)) i = -1;
        if (i == selectedAid) return;
        EventSystem es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);   // onEndEdit — прежнему знаку
        selectedAid = i;
        if (i >= 0) selShape = AreaShape.None;   // 21.3а: выделено что-то одно
        ShowPropsGroup();
        FillAidFields();
        Refresh();
    }

    // После отмены / повтора — тот же знак или фигура по имени (номер в списке мог измениться)
    private (string aid, AreaShapeKind kind, string shape) SelectionKey() =>
        (SelAid?.name, selShape.Kind, doc != null ? PolygonEditing.Name(doc.Area, selShape) : null);

    private void Reselect((string aid, AreaShapeKind kind, string shape) key)
    {
        selectedAid = key.aid == null || doc == null ? -1 : doc.Area.aids.FindIndex(x => x != null && x.name == key.aid);
        selShape = doc != null ? PolygonEditing.Find(doc.Area, key.kind, key.shape) : AreaShape.None;
        hotVertex = hotMid = dragVertex = -1;
        ShowPropsGroup();
    }

    private void ShowPropsGroup()
    {
        bool aid = SelAid != null, berth = !aid && SelBerth != null, shape = !aid && !berth && SelPoints != null;
        if (areaGroup != null) areaGroup.gameObject.SetActive(!aid && !shape && !berth);
        if (aidGroup != null) aidGroup.gameObject.SetActive(aid);
        if (shapeGroup != null) shapeGroup.gameObject.SetActive(shape);
        if (berthGroup != null) berthGroup.gameObject.SetActive(berth);
    }

    private void FillAidFields()
    {
        AidToNavigation x = SelAid;
        if (x == null) return;
        Set(aidNameField, x.name);
        Set(aidEField, MonitorUi.Num(x.position.East, 1));
        Set(aidNField, MonitorUi.Num(x.position.North, 1));
        Set(aidLightField, x.lightCharacter ?? "");
        if (aidLightField.placeholder is TextMeshProUGUI ph) ph.text = "по умолчанию: " + IalaMarks.Get(x.type).DefaultLight;
    }

    // Правка выделенного знака из поля; change → false — значение не принято (вернуть прежнее)
    private void EditAid(Func<AidToNavigation, bool> change)
    {
        AidToNavigation x = SelAid;
        if (x == null) return;
        Edit(() => { if (change(x)) doc.Commit(); });
    }

    private void EditAidPos(string text, bool east)
    {
        if (!MonitorUi.TryNumber(text, out double v)) { FillFields(); return; }
        EditAid(x => { x.position = east ? new WorldPosition(v, x.position.North) : new WorldPosition(x.position.East, v); return true; });
    }

    private void CycleAidType(int d)
    {
        AidToNavigation x = SelAid;
        if (x == null) return;
        int n = AidEditing.Palette.Length;
        int k = Array.IndexOf(AidEditing.Palette, x.type);
        AidType t = AidEditing.Palette[((k < 0 ? 0 : k) + d + n) % n];
        Edit(() => { AidEditing.SetType(doc.Area, selectedAid, t); doc.Commit(); });
    }

    private void DeleteAid()
    {
        if (doc == null || !AidEditing.Remove(doc.Area, selectedAid)) return;
        selectedAid = -1;
        dragAid = false;
        ShowPropsGroup();
        Edit(() => doc.Commit());
    }

    private static void Set(TMP_InputField f, string v)
    {
        if (f != null && !f.isFocused) f.SetTextWithoutNotify(v);
    }

    private void Undo()
    {
        var sel = SelectionKey();
        if (doc != null && doc.Undo()) { Reselect(sel); mapDirty = true; FillFields(); Refresh(); }
    }

    private void Redo()
    {
        var sel = SelectionKey();
        if (doc != null && doc.Redo()) { Reselect(sel); mapDirty = true; FillFields(); Refresh(); }
    }

    private void SetTool(Tool t)
    {
        tool = t;
        dragHandle = -1;
        dragAid = false;
        draft.Clear();   // 21.3а: недорисованная фигура отменяется
        dragVertex = -1;
        dragShape = false;
        Refresh();
    }

    // ─────────── Сохранение, открытие ───────────
    private void Save(bool overwrite, bool keepPrevious = false)
    {
        if (doc == null) return;
        AreaSaveStatus st = doc.Save(Root, Catalog, overwrite, DateTime.Now, keepPrevious);
        switch (st)
        {
            case AreaSaveStatus.Saved:
                lastDraftText = "";
                Debug.Log("[Редактор] " + doc.LastMessage);
                break;
            case AreaSaveStatus.NameTaken:
                Dialog("Имя занято", doc.LastMessage + " Заменить его этой акваторией? Прежний вариант уйдёт в «История».", null, true,
                       ("Заменить", MonitorUi.WarnColor, () => { CloseDialog(); Save(true, keepPrevious); }),
                       ("Отмена", MonitorUi.NeutralColor, CloseDialog));
                break;
            default:
                Dialog("Не сохранено", doc.LastMessage, null, true, ("Понятно", MonitorUi.NeutralColor, CloseDialog));
                break;
        }
        Refresh();
    }

    private void SaveAsDialog()
    {
        if (doc == null) return;
        Dialog("Сохранить как", "Новое имя акватории (файл в «Мои акватории»; прежний файл останется):", doc.Area.name + " копия", true,
               ("Сохранить", MonitorUi.ButtonColor, () =>
               {
                   string n = DialogInput();
                   CloseDialog();
                   if (string.IsNullOrWhiteSpace(n)) return;
                   doc.Rename(n);
                   FillFields();
                   Save(false, keepPrevious: true);
               }),
               ("Отмена", MonitorUi.NeutralColor, CloseDialog));
    }

    private void NewDialog()
    {
        Dialog("Новая акватория", "Имя (его увидит сценарий; имя встроенной занимать нельзя):", "Новая акватория", true,
               ("Создать", MonitorUi.StartColor, () =>
               {
                   string n = DialogInput();
                   CloseDialog();
                   SetDocument(AreaDocument.New(string.IsNullOrWhiteSpace(n) ? "Новая акватория" : n.Trim()));
               }),
               ("Отмена", MonitorUi.NeutralColor, CloseDialog));
    }

    /// <summary>Открыть: новая, копия встроенной, своя из «Мои акватории».</summary>
    private void OpenDialog(bool startup)
    {
        var buttons = new List<(string, Color, Action)>
        {
            ("Новая (пустая)", MonitorUi.StartColor, () => { CloseDialog(); NewDialog(); })
        };
        foreach (WaterArea a in Catalog.Areas)
        {
            WaterArea src = a;
            buttons.Add(($"Встроенная «{a.name}» — копией", MonitorUi.ButtonColor, () =>
            {
                CloseDialog();
                Dialog("Копия встроенной", $"Встроенная «{src.name}» не меняется — правится её копия. Имя копии:", "Моя акватория", true,
                       ("Создать копию", MonitorUi.StartColor, () =>
                       {
                           string n = DialogInput();
                           CloseDialog();
                           SetDocument(AreaDocument.CopyOf(src, string.IsNullOrWhiteSpace(n) ? "Моя акватория" : n.Trim()));
                       }),
                       ("Отмена", MonitorUi.NeutralColor, CloseDialog));
            }));
        }
        string dir = AreaDocument.AreasFolder(Root);
        var files = new List<string>();
        if (Directory.Exists(dir)) files.AddRange(Directory.GetFiles(dir, "*" + ScenarioFile.Extension));
        files.RemoveAll(f => !string.Equals(Path.GetExtension(f), ScenarioFile.Extension, StringComparison.OrdinalIgnoreCase));
        files.Sort(StringComparer.OrdinalIgnoreCase);
        int shown = 0;
        foreach (string f in files)
        {
            if (++shown > 10) break;
            string path = f;
            buttons.Add(($"Моя: {Path.GetFileNameWithoutExtension(f)}", MonitorUi.NeutralColor, () =>
            {
                var errors = new List<string>();
                AreaDocument d = AreaDocument.Open(path, errors);
                CloseDialog();
                if (d != null) SetDocument(d);
                else Dialog("Не открылся", string.Join("\n", errors), null, true, ("Понятно", MonitorUi.NeutralColor, () => { CloseDialog(); OpenDialog(doc == null); }));
            }));
        }
        buttons.Add(("Открыть папку в Проводнике", MonitorUi.NeutralColor, () => Application.OpenURL("file:///" + Root.Replace('\\', '/'))));
        if (!startup || doc != null) buttons.Add(("Отмена", MonitorUi.NeutralColor, CloseDialog));
        else buttons.Add(("В тренажёр", MonitorUi.NeutralColor, TrainerScenes.OpenTraining));
        string more = files.Count > 10 ? $"\nСвоих акваторий {files.Count} — показаны первые 10 (по имени)." : "";
        Dialog("Открыть акваторию", $"Папка: {Root}{more}", null, false, buttons.ToArray());
    }

    // Несохранённые правки — спросить перед действием
    private void Guarded(Action then)
    {
        if (doc == null || !doc.Dirty) { then(); return; }
        Dialog("Есть несохранённые правки", $"Акватория «{doc.Area.name}» изменена. Сохранить?", null, true,
               ("Сохранить", MonitorUi.ButtonColor, () =>
               {
                   CloseDialog();
                   Save(false);
                   if (!doc.Dirty) then();
               }),
               ("Не сохранять", MonitorUi.StopColor, () => { CloseDialog(); AreaDocument.DeleteDraft(Root); doc = null; then(); }),
               ("Отмена", MonitorUi.NeutralColor, CloseDialog));
    }

    // ─────────── Диалог ───────────
    private TMP_InputField dialogField;

    private void Dialog(string title, string text, string input, bool small, params (string label, Color color, Action action)[] buttons)
    {
        CloseDialog();
        dialogOpen = true;
        dialog = MonitorUi.Panel(root, "Dialog", new Color(0f, 0f, 0f, 0.6f));
        MonitorUi.Stretch(dialog, 0f, 0f, 0f, 0f);
        float w = 760f;
        float h = 150f + (input != null ? 50f : 0f) + buttons.Length * (MonitorUi.RowHeight + MonitorUi.Gap) + 40f;
        RectTransform box = MonitorUi.Panel(dialog, "Box", MonitorUi.PanelColor);
        MonitorUi.Place(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h), new Vector2(0.5f, 0.5f));
        TextMeshProUGUI t = MonitorUi.Text(box, title, 24f, MonitorUi.TitleColor, TextAlignmentOptions.TopLeft);
        t.fontStyle = FontStyles.Bold;
        MonitorUi.TopLeft(t.rectTransform, 20f, -16f, w - 40f, 34f);
        TextMeshProUGUI m = MonitorUi.Text(box, text, 17f, Color.white, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(m.rectTransform, 20f, -56f, w - 40f, 80f);
        float y = -140f;
        dialogField = null;
        if (input != null)
        {
            dialogField = MonitorUi.Field(box, 20f, y, w - 40f, 38f, "", false, TMP_InputField.ContentType.Standard, null);
            dialogField.text = input;
            dialogField.Select();
            dialogField.ActivateInputField();
            y -= 50f;
        }
        foreach (var b in buttons)
        {
            float x = 20f;
            (string label, Color color, Action action) bb = b;
            ui.Button(box, ref x, y, small ? 320f : w - 40f, bb.label, bb.color, () => bb.action());
            y -= MonitorUi.RowHeight + MonitorUi.Gap;
        }
    }

    private string DialogInput() => dialogField != null ? dialogField.text : "";

    private void CloseDialog()
    {
        if (dialog != null) Destroy(dialog.gameObject);
        dialog = null;
        dialogField = null;
        dialogOpen = false;
    }

    // ─────────── Кадр ───────────
    void Update()
    {
        if (canvas == null) return;
        Keys();
        if (!dialogOpen) MapMouse();
        if (doc != null && doc.Dirty && Time.unscaledTime >= nextDraft) WriteDraft();
        RedrawMap();
        ui.Refresh();
        Status();
    }

    private void Keys()
    {
        Keyboard k = Keyboard.current;
        if (k == null) return;
        GameObject sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        TMP_InputField f = sel != null ? sel.GetComponent<TMP_InputField>() : null;
        if (f != null && f.isFocused)
        {
            if (dialogOpen && f == dialogField && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame))
                dialog.GetComponentsInChildren<Button>()[0].onClick.Invoke();   // Enter — первая кнопка диалога
            return;
        }
        bool ctrl = k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed;
        bool shift = k.leftShiftKey.isPressed || k.rightShiftKey.isPressed;
        if (!dialogOpen && DraftKeys(k)) return;   // 21.3а: Enter, Backspace, Esc при рисовании
        if (k.escapeKey.wasPressedThisFrame)
        {
            if (dialogOpen && doc != null) CloseDialog();
            else if (tool != Tool.Select) SetTool(Tool.Select);
            else { SelectAid(-1); SelectShape(AreaShape.None); }
        }
        if (!dialogOpen && k.deleteKey.wasPressedThisFrame) DeleteSelected();
        if (dialogOpen || !ctrl) return;
        if (k.zKey.wasPressedThisFrame) { if (shift) Redo(); else Undo(); }
        else if (k.yKey.wasPressedThisFrame) Redo();
        else if (k.sKey.wasPressedThisFrame) Save(false);
    }

    private void MapMouse()
    {
        Mouse m = Mouse.current;
        if (m == null || doc == null) { overlay.ShowCursor = false; return; }
        Vector2 screen = m.position.ReadValue();
        bool inside = RectTransformUtility.ScreenPointToLocalPointInRectangle(mapRect, screen, null, out Vector2 local) && mapRect.rect.Contains(local);
        Rect rr = mapRect.rect;
        double px = local.x - rr.xMin, py = local.y - rr.yMin;
        WorldPosition world = viewport.ToWorld(px, py);
        WorldPosition snapped = snap ? MapGrid.Snap(world, GridStep) : world;

        // Масштаб — колесо
        float wheel = m.scroll.ReadValue().y;
        if (inside && Mathf.Abs(wheel) > 0.01f) viewport.ZoomAt(px, py, wheel > 0f ? 1.25 : 0.8);

        // Нажатие
        int hot = tool == Tool.Bounds && inside ? NearestHandle(px, py, 12.0) : -1;
        ShapeHover(inside, px, py);   // 21.3а: ручки выделенной фигуры, фигура под курсором
        bool shapeHandle = hotVertex >= 0 || hotMid >= 0;
        int hotAid = tool != Tool.Bounds && inside && !dragAid && dragHandle < 0 && dragVertex < 0 && !dragShape && !shapeHandle && draft.Count == 0
            ? AidEditing.HitTest(doc.Area, viewport, px, py, 11.0) : -1;
        if (inside && (m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame)) { panning = true; lastLocal = local; }
        if (inside && m.leftButton.wasPressedThisFrame)
        {
            if (hot >= 0) dragHandle = hot;
            else if (IsDrawTool(tool) && draft.Count > 0) DraftClick(local, px, py, snapped);
            else if (shapeHandle) ShapeHandlePress(snapped);
            else if (hotAid >= 0)
            {
                // Знак под курсором — выделить; двигается, когда мышь сдвинулась больше чем на 3 пикс.
                SelectAid(hotAid);
                dragAid = true;
                dragMoved = false;
                pressLocal = local;
            }
            else if (tool == Tool.Aid)
            {
                // Поставить знак выбранного типа в узел сетки; пока кнопка нажата — его можно тянуть
                int i = AidEditing.Add(doc.Area, aidType, snapped);
                SelectAid(i);
                dragAid = true;
                dragMoved = true;
                pressLocal = local;
            }
            else if (IsDrawTool(tool)) DraftClick(local, px, py, snapped);   // первая вершина мели / суши
            else if (tool == Tool.Select && ShapeBodyPress(local, px, py, snapped)) { }
            else
            {
                SelectAid(-1);
                SelectShape(AreaShape.None);
                panning = true;
                lastLocal = local;
            }
        }
        // Перетаскивание
        if (panning)
        {
            Vector2 d = local - lastLocal;
            lastLocal = local;
            viewport.Pan(d.x, d.y);
        }
        if (dragHandle >= 0) MoveHandle(dragHandle, snapped);
        if (dragAid && SelAid != null)
        {
            if (!dragMoved && (local - pressLocal).magnitude > 3f) dragMoved = true;
            if (dragMoved) { AidEditing.Move(doc.Area, selectedAid, snapped); FillAidFields(); }
        }
        if (dragVertex >= 0 || dragShape) ShapeDrag(local, snapped);
        // Отпустили
        bool anyHeld = m.leftButton.isPressed || m.rightButton.isPressed || m.middleButton.isPressed;
        if (!anyHeld)
        {
            if (dragHandle >= 0)
            {
                dragHandle = -1;
                if (doc.Commit()) { FillFields(); Refresh(); }
            }
            if (dragAid)
            {
                dragAid = false;
                if (doc.Commit()) { FillFields(); Refresh(); }   // поставили или передвинули — один шаг отмены
            }
            ShapeRelease();
            panning = false;
        }

        hoverAid = dragAid ? selectedAid : hotAid;
        overlay.HotHandle = dragHandle >= 0 ? dragHandle : hot;
        overlay.HotAid = hoverAid;
        overlay.ShowCursor = inside && (tool == Tool.Bounds || (tool == Tool.Aid && hotAid < 0) || (dragAid && dragMoved) ||
                                        (IsDrawTool(tool) && !shapeHandle) || dragVertex >= 0 || (dragShape && shapeMoved));
        FeedShapeOverlay(snapped);
        overlay.Cursor = snapped;
        cursorWorld = world;
        cursorInside = inside;
        overlay.SetVerticesDirty();
    }

    private WorldPosition cursorWorld;
    private bool cursorInside;

    private int NearestHandle(double px, double py, double maxPx)
    {
        List<WorldPosition> hs = EditorMapOverlay.BoundsHandles(doc.Area.boundsMin, doc.Area.boundsMax);
        int best = -1;
        double bestD = maxPx;
        for (int i = 0; i < hs.Count; i++)
        {
            viewport.ToPixel(hs[i], out double hx, out double hy);
            double d = Math.Max(Math.Abs(hx - px), Math.Abs(hy - py));
            if (d <= bestD) { bestD = d; best = i; }
        }
        return best;
    }

    // Ручка рамки → новая граница; ширина и высота не меньше шага сетки
    private void MoveHandle(int h, WorldPosition p)
    {
        WaterArea a = doc.Area;
        double minE = a.boundsMin.East, minN = a.boundsMin.North, maxE = a.boundsMax.East, maxN = a.boundsMax.North;
        double g = Math.Max(10.0, GridStep);
        bool left = h == 0 || h == 3 || h == 7, right = h == 1 || h == 2 || h == 5;
        bool bottom = h == 0 || h == 1 || h == 4, top = h == 2 || h == 3 || h == 6;
        if (left) minE = Math.Min(p.East, maxE - g);
        if (right) maxE = Math.Max(p.East, minE + g);
        if (bottom) minN = Math.Min(p.North, maxN - g);
        if (top) maxN = Math.Max(p.North, minN + g);
        a.boundsMin = new WorldPosition(minE, minN);
        a.boundsMax = new WorldPosition(maxE, maxN);
        FillFields();
    }

    private void RedrawMap()
    {
        if (doc == null) return;
        viewport.SetSize(Mathf.Max(1f, mapRect.rect.width), Mathf.Max(1f, mapRect.rect.height));
        if (mapDirty || mapData == null)
        {
            SessionArea sa = SessionArea.From(doc.Area);
            sa.aids.Clear();   // знаки рисует слой редактора (тянуть без перестройки карты)
            mapData = DebriefMap.BuildStatic(sa, null);
            mapStatic.SetView(mapData, 0);
            mapDirty = false;
            builtVersion = -1;
        }
        overlay.GridStep = GridStep;
        overlay.BoundsMin = doc.Area.boundsMin;
        overlay.BoundsMax = doc.Area.boundsMax;
        overlay.ShowHandles = tool == Tool.Bounds;
        overlay.Aids.Clear();
        foreach (AidToNavigation x in doc.Area.aids) if (x != null) overlay.Aids.Add((x.position, x.type));
        overlay.SelectedAid = SelAid != null ? selectedAid : -1;
        overlay.SetVerticesDirty();
        AidLabels();
        if (viewport.Version != builtVersion)
        {
            builtVersion = viewport.Version;
            if (mapData.Views.Count > 0) mapData.Views[0].Bounds = viewport.Bounds;
            mapStatic.SetVerticesDirty();
            overlay.SetVerticesDirty();
        }
    }

    // Имена знаков рядом со значками; гуще 60 на экране — не подписывать (каша)
    private void AidLabels()
    {
        List<AidToNavigation> aids = doc.Area.aids;
        Rect r = overlay.rectTransform.rect;
        int visible = 0;
        foreach (AidToNavigation x in aids)
            if (x != null && r.Contains(overlay.Px(x.position, r))) visible++;
        bool show = visible <= 60;
        int used = 0;
        for (int i = 0; i < aids.Count; i++)
        {
            AidToNavigation x = aids[i];
            if (!show || x == null) continue;
            Vector2 c = overlay.Px(x.position, r);
            if (!r.Contains(c)) continue;
            if (used >= aidLabels.Count)
            {
                TextMeshProUGUI t = MonitorUi.Text(overlay.transform, "", 14f, Color.white, TextAlignmentOptions.Left);
                t.textWrappingMode = TextWrappingModes.NoWrap;
                MonitorUi.Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 20f), new Vector2(0f, 0.5f));
                aidLabels.Add(t);
            }
            TextMeshProUGUI l = aidLabels[used++];
            if (l.text != x.name) l.text = x.name;
            l.color = i == selectedAid ? EditorMapOverlay.HandleHot : new Color(1f, 1f, 1f, 0.8f);
            l.rectTransform.anchoredPosition = c + new Vector2(12f, 0f);
            if (!l.gameObject.activeSelf) l.gameObject.SetActive(true);
        }
        for (int i = used; i < aidLabels.Count; i++)
            if (aidLabels[i].gameObject.activeSelf) aidLabels[i].gameObject.SetActive(false);
    }

    private void WriteDraft()
    {
        nextDraft = Time.unscaledTime + (float)AreaDocument.DraftPeriodS;
        try
        {
            doc.WriteDraft(Root);
            lastDraftText = $"черновик {DateTime.Now:HH:mm}";
        }
        catch (Exception e) { lastDraftText = "черновик не записан: " + e.Message; }
    }

    // Заголовок, проверка — после правки
    private void Refresh()
    {
        if (doc == null) { titleText.text = "<color=#9AA6B2>акватория не открыта</color>"; checkText.text = ""; return; }
        string file = doc.FilePath.Length > 0 ? Path.GetFileName(doc.FilePath) : "не сохранена";
        titleText.text = $"Акватория «<b>{doc.Area.name}</b>»{(doc.Dirty ? " <color=#FFC85A>*</color>" : "")}  <size=80%><color=#9AA6B2>{file} · {doc.Origin}</color></size>";
        AreaCheckResult c = AreaChecks.Check(doc.Area, Catalog);
        var b = new System.Text.StringBuilder();
        foreach (string e in c.Errors) b.AppendLine($"<color=#FF7A7A>× {e}</color>");
        foreach (string wn in c.Warnings) b.AppendLine($"<color=#FFC85A>! {wn}</color>");
        if (b.Length == 0) b.Append("<color=#6FE08A>Замечаний нет.</color>");
        WaterArea a = doc.Area;
        b.Append($"\n<color=#9AA6B2>Знаков {a.aids.Count}, мелей {a.shoals.Count}, суши {a.land.Count}, фарватеров {a.fairways.Count}, причалов {a.berths.Count}.</color>");
        if (!string.IsNullOrEmpty(doc.LastMessage)) b.Append($"\n\n<color=#9AA6B2>{doc.LastMessage}</color>");
        checkText.text = b.ToString();
    }

    private void Status()
    {
        if (doc == null) { statusText.text = $"Папка: {Root}"; return; }
        string pos = cursorInside
            ? $"E {MonitorUi.Num(cursorWorld.East, 0)} м · N {MonitorUi.Num(cursorWorld.North, 0)} м · глубина {MonitorUi.Num(doc.Area.DepthAt(cursorWorld), 1)} м"
            : "курсор вне карты";
        if (cursorInside && AidEditing.Valid(doc.Area, hoverAid))
        {
            AidToNavigation x = doc.Area.aids[hoverAid];
            pos += $" · знак «{x.name}» — {IalaMarks.Get(x.type).TitleRu.ToLowerInvariant()}, {x.Light}";
        }
        else if (cursorInside && !hoverShape.IsNone)
            pos += $" · {PolygonEditing.Title(hoverShape.Kind).ToLowerInvariant()} «{PolygonEditing.Name(doc.Area, hoverShape)}»";
        if (draft.Count > 0) { statusText.text = pos + DraftStatus(); return; }   // 21.3а: подсказка рисования
        string saved = doc.SavedAt.HasValue ? $"сохранено {doc.SavedAt.Value:HH:mm}" : doc.Dirty ? "не сохранено" : "";
        statusText.text = $"{pos} · 1 пикс. = {MonitorUi.Num(viewport.MetersPerPixel, viewport.MetersPerPixel < 10 ? 1 : 0)} м · сетка {MapGrid.StepTitle(GridStep)}" +
                          $"{(snap ? " (привязка)" : "")} · {saved}{(lastDraftText.Length > 0 ? " · " + lastDraftText : "")}";
    }
}
