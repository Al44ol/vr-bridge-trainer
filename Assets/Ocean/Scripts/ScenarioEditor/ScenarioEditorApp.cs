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
// 21.2б — знаки МАМС; 21.3 — мели, суша, фарватеры, причалы; 21.4 — слой сценария.
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

public class ScenarioEditorApp : MonoBehaviour
{
    private enum Tool { Select, Bounds }

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
        Header(p, "Скоро", ref y);
        foreach (string s in new[] { "Знаки МАМС — 21.2б", "Мели и суша — 21.3", "Фарватеры — 21.3", "Причалы — 21.3", "Сценарий — 21.4" })
        {
            x = Pad;
            ui.Button(p, ref x, y, w, s, MonitorUi.DisabledColor, null);
            y -= MonitorUi.RowHeight + MonitorUi.Gap;
        }
        y -= 2f * MonitorUi.Gap;
        TextMeshProUGUI help = MonitorUi.Text(p,
            "Колесо — масштаб\nПравая кнопка или левая на пустом месте — сдвиг\nCtrl+Z / Ctrl+Y — отменить / повторить\nCtrl+S — сохранить\nEsc — инструмент «Выбор»",
            14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(help.rectTransform, Pad, y, w, 160f);
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
        Header(p, "Акватория", ref y);
        nameField = Row(p, "Имя", ref y, w, false, TMP_InputField.ContentType.Standard, v => Edit(() => doc.Rename(v)));
        depthField = Row(p, "Глубина по умолчанию, м", ref y, w, false, TMP_InputField.ContentType.Standard,
                         v => EditNumber(v, d => d > 0.0, d => doc.Area.defaultDepth = (float)d));
        Label(p, "Описание", ref y, w);
        descField = MonitorUi.Field(p, Pad, y, w, 90f, "для кого и для чего", true, TMP_InputField.ContentType.Standard,
                                    v => Edit(() => { doc.Area.description = v; doc.Commit(); }), 16f);
        y -= 90f + 2f * MonitorUi.Gap;
        Header(p, "Границы района, м", ref y);
        float half = (w - MonitorUi.Gap) * 0.5f;
        Label(p, "Восток: от … до", ref y, w);
        eMinField = MonitorUi.Field(p, Pad, y, half, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditBound(v, 0));
        eMaxField = MonitorUi.Field(p, Pad + half + MonitorUi.Gap, y, half, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditBound(v, 1));
        y -= 34f + MonitorUi.Gap;
        Label(p, "Север: от … до", ref y, w);
        nMinField = MonitorUi.Field(p, Pad, y, half, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditBound(v, 2));
        nMaxField = MonitorUi.Field(p, Pad + half + MonitorUi.Gap, y, half, 34f, "", false, TMP_InputField.ContentType.Standard, v => EditBound(v, 3));
        y -= 34f + 3f * MonitorUi.Gap;
        Header(p, "Проверка", ref y);
        checkText = MonitorUi.Text(p, "", 15f, Color.white, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(checkText.rectTransform, Pad, y, w, 380f);
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
    }

    private static void Set(TMP_InputField f, string v)
    {
        if (f != null && !f.isFocused) f.SetTextWithoutNotify(v);
    }

    private void Undo()
    {
        if (doc != null && doc.Undo()) { mapDirty = true; FillFields(); Refresh(); }
    }

    private void Redo()
    {
        if (doc != null && doc.Redo()) { mapDirty = true; FillFields(); Refresh(); }
    }

    private void SetTool(Tool t)
    {
        tool = t;
        dragHandle = -1;
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
        if (k.escapeKey.wasPressedThisFrame)
        {
            if (dialogOpen && doc != null) CloseDialog();
            else SetTool(Tool.Select);
        }
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
        if (inside && (m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame)) { panning = true; lastLocal = local; }
        if (inside && m.leftButton.wasPressedThisFrame)
        {
            if (hot >= 0) dragHandle = hot;
            else { panning = true; lastLocal = local; }
        }
        // Перетаскивание
        if (panning)
        {
            Vector2 d = local - lastLocal;
            lastLocal = local;
            viewport.Pan(d.x, d.y);
        }
        if (dragHandle >= 0) MoveHandle(dragHandle, snapped);
        // Отпустили
        bool anyHeld = m.leftButton.isPressed || m.rightButton.isPressed || m.middleButton.isPressed;
        if (!anyHeld)
        {
            if (dragHandle >= 0)
            {
                dragHandle = -1;
                if (doc.Commit()) { FillFields(); Refresh(); }
            }
            panning = false;
        }

        overlay.HotHandle = dragHandle >= 0 ? dragHandle : hot;
        overlay.ShowCursor = inside && tool == Tool.Bounds;
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
            mapData = DebriefMap.BuildStatic(SessionArea.From(doc.Area), null);
            mapStatic.SetView(mapData, 0);
            mapDirty = false;
            builtVersion = -1;
        }
        overlay.GridStep = GridStep;
        overlay.BoundsMin = doc.Area.boundsMin;
        overlay.BoundsMax = doc.Area.boundsMax;
        overlay.ShowHandles = tool == Tool.Bounds;
        if (viewport.Version != builtVersion)
        {
            builtVersion = viewport.Version;
            if (mapData.Views.Count > 0) mapData.Views[0].Bounds = viewport.Bounds;
            mapStatic.SetVerticesDirty();
            overlay.SetVerticesDirty();
        }
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
        string saved = doc.SavedAt.HasValue ? $"сохранено {doc.SavedAt.Value:HH:mm}" : doc.Dirty ? "не сохранено" : "";
        statusText.text = $"{pos} · 1 пикс. = {MonitorUi.Num(viewport.MetersPerPixel, viewport.MetersPerPixel < 10 ? 1 : 0)} м · сетка {MapGrid.StepTitle(GridStep)}" +
                          $"{(snap ? " (привязка)" : "")} · {saved}{(lastDraftText.Length > 0 ? " · " + lastDraftText : "")}";
    }
}
