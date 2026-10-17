// ============================================================
// ScenarioEditorShapes.cs — Мели и суша в окне редактора (запрос 21.3а; часть ScenarioEditorApp)
//
// Рисование (инструменты «Мель», «Суша»): щелчок — вершина в узле сетки; щелчок по первой вершине,
// двойной щелчок или Enter — замкнуть (не меньше 3 вершин); Backspace — убрать последнюю; Esc — отменить.
// Правка выделенной фигуры: квадратные ручки — вершины (тянуть), круглые на серединах сторон — новая
// вершина (тянуть); в «Выборе» щелчок внутри фигуры — выделить, внутри уже выделенной — тянуть
// целиком (шагами сетки; первый щелчок по берегу только выделяет, мышь при этом сдвигает карту);
// Delete над вершиной — удалить её (если вершин больше 3), иначе — всю фигуру.
// Справа — свойства (имя, глубина мели / высота суши, вершины, площадь, периметр).
// Геометрия — PolygonEditing (Core); снимок отмены — после законченной правки (отпустили кнопку).
// ============================================================
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public partial class ScenarioEditorApp
{
    private AreaShape selShape = AreaShape.None, hoverShape = AreaShape.None;
    private int hotVertex = -1, hotMid = -1, dragVertex = -1;
    private bool dragShape, shapeMoved;
    private List<WorldPosition> shapeFrom;
    private WorldPosition pressSnapped;
    private readonly List<WorldPosition> draft = new List<WorldPosition>();
    private AreaShapeKind draftKind;
    private float lastClickTime = -10f;
    private Vector2 lastClickLocal;
    private RectTransform shapeGroup;
    private TMP_InputField shapeNameField, shapeValueField;
    private TextMeshProUGUI shapeTitle, shapeValueLabel, shapeInfo;

    private static bool IsDrawTool(Tool t) => t == Tool.Shoal || t == Tool.Land;
    private List<WorldPosition> SelVerts => doc != null ? PolygonEditing.Vertices(doc.Area, selShape) : null;

    // ─────────── Палитра, свойства ───────────
    private void BuildShapePalette(RectTransform p, ref float y, float w)
    {
        Header(p, "Мели и суша", ref y);
        foreach (Tool t in new[] { Tool.Shoal, Tool.Land })
        {
            Tool tt = t;
            float x = Pad;
            RectTransform b = ui.Button(p, ref x, y, w, tt == Tool.Shoal ? "Мель" : "Суша", MonitorUi.NeutralColor, () => SetTool(tt), null,
                                        () => tool == tt ? MonitorUi.ActiveColor : MonitorUi.NeutralColor, () => doc != null);
            TextMeshProUGUI bt = b.GetComponentInChildren<TextMeshProUGUI>();
            bt.alignment = TextAlignmentOptions.Left;
            bt.rectTransform.offsetMin = new Vector2(36f, bt.rectTransform.offsetMin.y);
            RectTransform sw = MonitorUi.Panel(b, "Swatch", tt == Tool.Shoal ? new Color(0.85f, 0.6f, 0.2f, 0.85f) : DebriefMapGraphic.LandColor);
            sw.GetComponent<Image>().raycastTarget = false;
            MonitorUi.Place(sw, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(18f, 18f), new Vector2(0.5f, 0.5f));
            y -= MonitorUi.RowHeight + MonitorUi.Gap;
        }
    }

    private float BuildShapeProps(RectTransform p, float w)
    {
        RectTransform g = shapeGroup = Group(p, "ShapeProps");
        float y = -Pad;
        shapeTitle = MonitorUi.Text(g, "Мель", 19f, MonitorUi.TitleColor, TextAlignmentOptions.TopLeft);
        shapeTitle.fontStyle = FontStyles.Bold;
        MonitorUi.TopLeft(shapeTitle.rectTransform, Pad, y, 600f, 28f);
        y -= 30f;
        TextMeshProUGUI hint = MonitorUi.Text(g, "Delete — удалить фигуру (над вершиной — вершину) · Esc — снять выделение", 14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(hint.rectTransform, Pad, y, w, 40f);
        y -= 42f;
        shapeNameField = Row(g, "Имя", ref y, w, false, TMP_InputField.ContentType.Standard, v =>
        {
            string n = (v ?? "").Trim();
            if (doc == null || SelVerts == null || n.Length == 0) { FillFields(); return; }
            Edit(() => { PolygonEditing.SetName(doc.Area, selShape, n); doc.Commit(); });
        });
        shapeValueLabel = MonitorUi.Text(g, "", 15f, MonitorUi.MutedColor, TextAlignmentOptions.BottomLeft);
        MonitorUi.TopLeft(shapeValueLabel.rectTransform, Pad, y, w, 22f);
        y -= 22f;
        shapeValueField = MonitorUi.Field(g, Pad, y, w, 34f, "", false, TMP_InputField.ContentType.Standard, EditShapeValue);
        y -= 34f + MonitorUi.Gap;
        shapeInfo = MonitorUi.Text(g, "", 15f, Color.white, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(shapeInfo.rectTransform, Pad, y, w, 44f);
        y -= 48f;
        TextMeshProUGUI help = MonitorUi.Text(g,
            "Квадратные ручки — вершины (тянуть); круглые на серединах сторон — новая вершина. Фигуру целиком — тянуть за внутреннюю область в «Выборе».",
            14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(help.rectTransform, Pad, y, w, 60f);
        y -= 64f;
        float x = Pad;
        ui.Button(g, ref x, y, w, "Удалить фигуру", MonitorUi.StopColor, DeleteShape);
        y -= MonitorUi.RowHeight + 3f * MonitorUi.Gap;
        return y;
    }

    private void FillShapeFields()
    {
        List<WorldPosition> v = SelVerts;
        if (v == null) return;
        bool shoal = selShape.Kind == AreaShapeKind.Shoal;
        shapeTitle.text = PolygonEditing.Title(selShape.Kind);
        shapeValueLabel.text = shoal ? "Наименьшая глубина над мелью, м" : "Высота берега над водой, м";
        Set(shapeNameField, PolygonEditing.Name(doc.Area, selShape));
        Set(shapeValueField, MonitorUi.Num(shoal ? doc.Area.shoals[selShape.Index].depth : doc.Area.land[selShape.Index].height, 1));
        double s = PolygonEditing.AreaM2(v);
        shapeInfo.text = $"Вершин {v.Count} · площадь {MonitorUi.Num(s / 10000.0, s < 1e5 ? 2 : 1)} га · периметр {MonitorUi.Num(PolygonEditing.Perimeter(v), 0)} м" +
                         (PolygonEditing.SelfIntersects(v) ? "\n<color=#FFC85A>стороны пересекаются</color>" : "");
    }

    // Глубина мели ≥ 0 (0 — осыхает), высота суши > 0
    private void EditShapeValue(string text)
    {
        if (SelVerts == null) return;
        bool shoal = selShape.Kind == AreaShapeKind.Shoal;
        if (!MonitorUi.TryNumber(text, out double v) || (shoal ? v < 0.0 : v <= 0.0)) { FillFields(); return; }
        Edit(() =>
        {
            if (shoal) doc.Area.shoals[selShape.Index].depth = (float)v;
            else doc.Area.land[selShape.Index].height = (float)v;
            doc.Commit();
        });
    }

    // ─────────── Выделение ───────────
    /// <summary>Незаконченный ввод в поле — к прежнему выделению (onEndEdit до смены).</summary>
    private static void EndFieldEdit()
    {
        EventSystem es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);
    }

    private void SelectShape(AreaShape s)
    {
        if (doc == null || !PolygonEditing.Valid(doc.Area, s)) s = AreaShape.None;
        if (s.Equals(selShape) && (s.IsNone || selectedAid < 0)) return;
        EndFieldEdit();
        selShape = s;
        if (!s.IsNone) selectedAid = -1;
        hotVertex = hotMid = dragVertex = -1;
        ShowPropsGroup();
        FillFields();
        Refresh();
    }

    // ─────────── Мышь ───────────
    // Ручки выделенной фигуры под курсором (не в «Границах» и не во время рисования)
    private void ShapeHover(bool inside, double px, double py, WorldPosition world)
    {
        List<WorldPosition> v = SelVerts;
        bool busy = dragVertex >= 0 || dragShape;
        if (!busy)
        {
            hotVertex = hotMid = -1;
            if (inside && v != null && tool != Tool.Bounds && draft.Count == 0)
            {
                hotVertex = PolygonEditing.NearestVertex(v, viewport, px, py, 9.0);
                if (hotVertex < 0) hotMid = PolygonEditing.NearestMidpoint(v, viewport, px, py, 7.0);
            }
        }
        hoverShape = inside && tool == Tool.Select && !busy && hotVertex < 0 && hotMid < 0 ? PolygonEditing.HitShape(doc.Area, world) : AreaShape.None;
        if (hoverShape.Equals(selShape)) hoverShape = AreaShape.None;
    }

    /// <summary>Нажатие на ручку выделенной фигуры: true — взято.</summary>
    private bool ShapeHandlePress(WorldPosition snapped)
    {
        List<WorldPosition> v = SelVerts;
        if (v == null) return false;
        if (hotVertex >= 0) { dragVertex = hotVertex; return true; }
        if (hotMid >= 0)
        {
            dragVertex = PolygonEditing.InsertVertex(v, hotMid, snapped);
            hotMid = -1;
            mapDirty = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// «Выбор»: щелчок внутри фигуры — выделить. Тянуть целиком можно только уже выделенную: иначе
    /// обычный сдвиг карты левой кнопкой по берегу двигал бы весь берег.
    /// </summary>
    private bool ShapeBodyPress(Vector2 local, WorldPosition world, WorldPosition snapped)
    {
        AreaShape s = PolygonEditing.HitShape(doc.Area, world);
        if (s.IsNone) return false;
        if (!s.Equals(selShape) || selectedAid >= 0)
        {
            SelectAid(-1);
            SelectShape(s);
            panning = true;   // первый щелчок — выделить; дальше мышь сдвигает карту
            lastLocal = local;
            return true;
        }
        dragShape = true;
        shapeMoved = false;
        shapeFrom = new List<WorldPosition>(SelVerts);
        pressSnapped = snapped;
        pressLocal = local;
        return true;
    }

    private void ShapeDrag(Vector2 local, WorldPosition snapped)
    {
        List<WorldPosition> v = SelVerts;
        if (v == null) { dragVertex = -1; dragShape = false; return; }
        if (dragVertex >= 0 && dragVertex < v.Count && !v[dragVertex].Equals(snapped))
        {
            v[dragVertex] = snapped;
            mapDirty = true;
            FillShapeFields();
        }
        if (dragShape)
        {
            if (!shapeMoved && (local - pressLocal).magnitude > 3f) shapeMoved = true;
            if (shapeMoved)
            {
                PolygonEditing.Translate(v, shapeFrom, snapped.East - pressSnapped.East, snapped.North - pressSnapped.North);
                mapDirty = true;
            }
        }
    }

    private void ShapeRelease()
    {
        if (dragVertex < 0 && !dragShape) return;
        dragVertex = -1;
        dragShape = false;
        mapDirty = true;
        if (doc.Commit()) { FillFields(); Refresh(); }
    }

    // ─────────── Рисование ───────────
    private void DraftClick(Vector2 local, double px, double py, WorldPosition snapped)
    {
        bool dbl = draft.Count > 0 && Time.unscaledTime - lastClickTime < 0.35f && (local - lastClickLocal).magnitude < 5f;
        lastClickTime = Time.unscaledTime;
        lastClickLocal = local;
        if (draft.Count == 0)
        {
            draftKind = tool == Tool.Land ? AreaShapeKind.Land : AreaShapeKind.Shoal;
            SelectAid(-1);
            SelectShape(AreaShape.None);
            draft.Add(snapped);
            return;
        }
        if (dbl || (draft.Count >= PolygonEditing.MinVertices && DraftCloseHot(px, py))) { FinishDraft(); return; }
        if (!draft[draft.Count - 1].Equals(snapped)) draft.Add(snapped);
    }

    private bool DraftCloseHot(double px, double py)
    {
        if (draft.Count < PolygonEditing.MinVertices) return false;
        viewport.ToPixel(draft[0], out double x, out double y);
        return (x - px) * (x - px) + (y - py) * (y - py) <= 10.0 * 10.0;
    }

    private void FinishDraft()
    {
        if (draft.Count < PolygonEditing.MinVertices) return;   // меньше 3 вершин — продолжать рисовать
        AreaShape s = PolygonEditing.Add(doc.Area, draftKind, draft);
        draft.Clear();
        doc.Commit();
        mapDirty = true;
        SelectShape(s);
        Debug.Log($"[Редактор] {PolygonEditing.Title(s.Kind)} «{PolygonEditing.Name(doc.Area, s)}»: вершин {SelVerts.Count}.");
    }

    /// <summary>Клавиши рисования; true — нажатие использовано.</summary>
    private bool DraftKeys(Keyboard k)
    {
        if (draft.Count == 0) return false;
        if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) { FinishDraft(); return true; }
        if (k.backspaceKey.wasPressedThisFrame) { draft.RemoveAt(draft.Count - 1); return true; }
        if (k.escapeKey.wasPressedThisFrame) { draft.Clear(); return true; }
        return false;
    }

    // ─────────── Удаление ───────────
    /// <summary>Delete: знак; вершина под курсором (если вершин больше 3); иначе вся фигура.</summary>
    private void DeleteSelected()
    {
        if (SelAid != null) { DeleteAid(); return; }
        List<WorldPosition> v = SelVerts;
        if (v == null) return;
        if (hotVertex >= 0 && PolygonEditing.RemoveVertex(v, hotVertex))
        {
            hotVertex = -1;
            mapDirty = true;
            Edit(() => doc.Commit());
            return;
        }
        DeleteShape();
    }

    private void DeleteShape()
    {
        if (doc == null || !PolygonEditing.Remove(doc.Area, selShape)) return;
        selShape = AreaShape.None;
        hotVertex = hotMid = dragVertex = -1;
        dragShape = false;
        ShowPropsGroup();
        Edit(() => doc.Commit());
    }

    // ─────────── Слой карты ───────────
    private void FeedShapeOverlay(WorldPosition snapped)
    {
        overlay.Shape.Clear();
        List<WorldPosition> v = SelVerts;
        if (v != null) overlay.Shape.AddRange(v);
        overlay.ShapeHandles = v != null && tool != Tool.Bounds && draft.Count == 0;
        overlay.HotVertex = dragVertex >= 0 ? dragVertex : hotVertex;
        overlay.HotMid = hotMid;
        overlay.HoverShape.Clear();
        List<WorldPosition> h = doc != null ? PolygonEditing.Vertices(doc.Area, hoverShape) : null;
        if (h != null) overlay.HoverShape.AddRange(h);
        overlay.Draft.Clear();
        overlay.Draft.AddRange(draft);
        overlay.DraftCloseHot = false;
        if (draft.Count >= PolygonEditing.MinVertices)
        {
            viewport.ToPixel(snapped, out double sx, out double sy);
            overlay.DraftCloseHot = DraftCloseHot(sx, sy);
        }
    }

    // Строка состояния при рисовании
    private string DraftStatus() =>
        draft.Count == 0 ? "" :
        $" · {PolygonEditing.Title(draftKind)}: вершин {draft.Count} — щелчок по первой, двойной щелчок или Enter — замкнуть; Backspace — убрать последнюю; Esc — отменить";
}
