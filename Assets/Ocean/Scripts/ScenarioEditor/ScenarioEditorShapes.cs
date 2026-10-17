// ============================================================
// ScenarioEditorShapes.cs — Мели, суша (21.3а), фарватеры и причалы (21.3б) в окне редактора
// (часть ScenarioEditorApp, partial-класс)
//
// Рисование: щелчок — точка в узле сетки.
//   мель, суша — замкнуть: щелчок по первой вершине, двойной щелчок или Enter (≥ 3 вершин);
//   фарватер — закончить: щелчок по последней точке, двойной щелчок или Enter (≥ 2 точек);
//   причал — два щелчка: A и B (кромка не короче 20 м; вода справа от A→B).
//   Backspace — убрать последнюю точку; Esc — отменить.
// Правка выделенной фигуры: квадратные ручки — точки (тянуть), круглые на серединах сторон — новая
// точка (не у причала); в «Выборе» щелчок по фигуре — выделить (мель, суша — внутри; фарватер, причал —
// по линии), по уже выделенной — тянуть целиком шагами сетки (первый щелчок по невыделенной только
// выделяет, мышь при этом сдвигает карту); Delete над точкой — удалить её (если останется не меньше
// наименьшего числа), иначе — всю фигуру.
// Справа — свойства: мель / суша / фарватер (имя, глубина / высота / ширина, точки, площадь или длина);
// причал — своя группа (имя, глубина, высота кромки, «Вода с другой стороны», кранцы и кнехты под судно).
// Геометрия — PolygonEditing (Core): точки фигуры читаются и пишутся копией (у причала — [a, b]).
// Снимок отмены — после законченной правки (отпустили кнопку).
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
    private RectTransform shapeGroup, berthGroup;
    private TMP_InputField shapeNameField, shapeValueField, berthNameField, berthDepthField, berthHeightField;
    private TextMeshProUGUI shapeTitle, shapeValueLabel, shapeInfo, berthInfo;

    private static bool IsDrawTool(Tool t) => t == Tool.Shoal || t == Tool.Land || t == Tool.Fairway || t == Tool.Berth;

    private static AreaShapeKind DrawKind(Tool t)
    {
        switch (t)
        {
            case Tool.Land: return AreaShapeKind.Land;
            case Tool.Fairway: return AreaShapeKind.Fairway;
            case Tool.Berth: return AreaShapeKind.Berth;
            default: return AreaShapeKind.Shoal;
        }
    }

    /// <summary>Точки выделенной фигуры (копия); null — не выделена.</summary>
    private List<WorldPosition> SelPoints => doc != null ? PolygonEditing.Points(doc.Area, selShape) : null;
    private BerthArea SelBerth => selShape.Kind == AreaShapeKind.Berth && doc != null && PolygonEditing.Valid(doc.Area, selShape) ? doc.Area.berths[selShape.Index] : null;

    // ─────────── Палитра ───────────
    private void BuildShapePalette(RectTransform p, ref float y, float w)
    {
        Header(p, "Объекты акватории", ref y);
        foreach (Tool t in new[] { Tool.Shoal, Tool.Land, Tool.Fairway, Tool.Berth })
        {
            Tool tt = t;
            float x = Pad;
            RectTransform b = ui.Button(p, ref x, y, w, PolygonEditing.Title(DrawKind(tt)), MonitorUi.NeutralColor, () => SetTool(tt), null,
                                        () => tool == tt ? MonitorUi.ActiveColor : MonitorUi.NeutralColor, () => doc != null);
            TextMeshProUGUI bt = b.GetComponentInChildren<TextMeshProUGUI>();
            bt.alignment = TextAlignmentOptions.Left;
            bt.rectTransform.offsetMin = new Vector2(36f, bt.rectTransform.offsetMin.y);
            bool line = tt == Tool.Fairway || tt == Tool.Berth;
            Color c = tt == Tool.Shoal ? new Color(0.85f, 0.6f, 0.2f, 0.85f) : tt == Tool.Land ? DebriefMapGraphic.LandColor
                    : tt == Tool.Fairway ? DebriefMapGraphic.FairwayColor : DebriefMapGraphic.QuayColor;
            RectTransform sw = MonitorUi.Panel(b, "Swatch", c);
            sw.GetComponent<Image>().raycastTarget = false;
            MonitorUi.Place(sw, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 0f), line ? new Vector2(22f, 4f) : new Vector2(18f, 18f), new Vector2(0.5f, 0.5f));
            y -= MonitorUi.RowHeight + MonitorUi.Gap;
        }
    }

    // ─────────── Свойства: мель, суша, фарватер ───────────
    private float BuildShapeProps(RectTransform p, float w)
    {
        RectTransform g = shapeGroup = Group(p, "ShapeProps");
        float y = -Pad;
        shapeTitle = MonitorUi.Text(g, "Мель", 19f, MonitorUi.TitleColor, TextAlignmentOptions.TopLeft);
        shapeTitle.fontStyle = FontStyles.Bold;
        MonitorUi.TopLeft(shapeTitle.rectTransform, Pad, y, 600f, 28f);
        y -= 30f;
        TextMeshProUGUI hint = MonitorUi.Text(g, "Delete — удалить фигуру (над точкой — точку) · Esc — снять выделение", 14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(hint.rectTransform, Pad, y, w, 40f);
        y -= 42f;
        shapeNameField = Row(g, "Имя", ref y, w, false, TMP_InputField.ContentType.Standard, EditShapeName);
        shapeValueLabel = MonitorUi.Text(g, "", 15f, MonitorUi.MutedColor, TextAlignmentOptions.BottomLeft);
        MonitorUi.TopLeft(shapeValueLabel.rectTransform, Pad, y, w, 22f);
        y -= 22f;
        shapeValueField = MonitorUi.Field(g, Pad, y, w, 34f, "", false, TMP_InputField.ContentType.Standard, EditShapeValue);
        y -= 34f + MonitorUi.Gap;
        shapeInfo = MonitorUi.Text(g, "", 15f, Color.white, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(shapeInfo.rectTransform, Pad, y, w, 44f);
        y -= 48f;
        TextMeshProUGUI help = MonitorUi.Text(g,
            "Квадратные ручки — точки (тянуть); круглые на серединах сторон — новая точка. Фигуру целиком — тянуть, когда она уже выделена.",
            14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(help.rectTransform, Pad, y, w, 60f);
        y -= 64f;
        float x = Pad;
        ui.Button(g, ref x, y, w, "Удалить", MonitorUi.StopColor, DeleteShape);
        y -= MonitorUi.RowHeight + 3f * MonitorUi.Gap;
        return y;
    }

    // ─────────── Свойства: причал ───────────
    private float BuildBerthProps(RectTransform p, float w)
    {
        RectTransform g = berthGroup = Group(p, "BerthProps");
        float y = -Pad, half = (w - MonitorUi.Gap) * 0.5f;
        Header(g, "Причал", ref y);
        TextMeshProUGUI hint = MonitorUi.Text(g, "Кромка A→B, вода справа (стрелка) · Delete — удалить · Esc — снять выделение", 14f, MonitorUi.MutedColor, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(hint.rectTransform, Pad, y, w, 40f);
        y -= 42f;
        berthNameField = Row(g, "Имя", ref y, w, false, TMP_InputField.ContentType.Standard, EditShapeName);
        Label(g, "Глубина у причала, м · высота кромки, м", ref y, w);
        berthDepthField = MonitorUi.Field(g, Pad, y, half, 34f, "", false, TMP_InputField.ContentType.Standard,
                                          v => EditBerthNumber(v, (b, d) => b.depthM = (float)d));
        berthHeightField = MonitorUi.Field(g, Pad + half + MonitorUi.Gap, y, half, 34f, "", false, TMP_InputField.ContentType.Standard,
                                           v => EditBerthNumber(v, (b, d) => b.quayHeightM = (float)d));
        y -= 34f + MonitorUi.Gap;
        berthInfo = MonitorUi.Text(g, "", 15f, Color.white, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(berthInfo.rectTransform, Pad, y, w, 66f);
        y -= 70f;
        float x = Pad;
        ui.Button(g, ref x, y, w, "Вода с другой стороны", MonitorUi.NeutralColor, () => EditBerth(PolygonEditing.Flip));
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
        Label(g, "Кранцы и кнехты под судно (кромка на месте):", ref y, w);
        int n = 0;
        x = Pad;
        foreach (ShipParameters s in Catalog.Ships)
        {
            if (s == null || n >= 6) continue;
            ShipParameters ship = s;
            if (n > 0 && n % 2 == 0) { y -= MonitorUi.RowHeight + MonitorUi.Gap; x = Pad; }
            ui.Button(g, ref x, y, half, ShipLabel(ship), MonitorUi.ButtonColor,
                      () => EditBerth(b => PolygonEditing.ApplyShip(b, ship.length, ship.beam, ship.draft, ship.blockCoefficient)), null, null, null,
                      MonitorUi.RowHeight, 14f);
            n++;
        }
        if (n > 0) y -= MonitorUi.RowHeight + MonitorUi.Gap;
        y -= MonitorUi.Gap;
        x = Pad;
        ui.Button(g, ref x, y, w, "Удалить причал", MonitorUi.StopColor, DeleteShape);
        y -= MonitorUi.RowHeight + 3f * MonitorUi.Gap;
        return y;
    }

    // «Model45m · 45 м», «Mariner · 161 м»
    private static string ShipLabel(ShipParameters s)
    {
        string n = s.name ?? "";
        if (n.StartsWith("ShipParameters_")) n = n.Substring("ShipParameters_".Length);
        return $"{n} · {MonitorUi.Num(s.length, 0)} м";
    }

    private void FillShapeFields()
    {
        if (doc == null) return;
        BerthArea b = SelBerth;
        if (b != null)
        {
            Set(berthNameField, b.name);
            Set(berthDepthField, MonitorUi.Num(b.depthM, 1));
            Set(berthHeightField, MonitorUi.Num(b.quayHeightM, 1));
            berthInfo.text = $"Длина {MonitorUi.Num(b.Length, 0)} м · кромка A→B {MonitorUi.Num(b.BearingDeg, 0)}°\n" +
                             $"Кранцы Ø {MonitorUi.Num(b.fenderOuterDiameterM, 2)} × {MonitorUi.Num(b.fenderLengthM, 1)} м через {MonitorUi.Num(b.fenderSpacingM, 0)} м, " +
                             $"{MonitorUi.Num(b.fenderRatedReactionKn, 0)} кН · кнехты через {MonitorUi.Num(b.bollardSpacingM, 0)} м";
            return;
        }
        List<WorldPosition> v = SelPoints;
        if (v == null) return;
        AreaShapeKind k = selShape.Kind;
        shapeTitle.text = PolygonEditing.Title(k);
        Set(shapeNameField, PolygonEditing.Name(doc.Area, selShape));
        if (k == AreaShapeKind.Fairway)
        {
            float wd = doc.Area.fairways[selShape.Index].width;
            shapeValueLabel.text = $"Ширина, м (= {MonitorUi.Num(wd / WaterArea.CableMeters, 2)} кбт)";
            Set(shapeValueField, MonitorUi.Num(wd, 1));
            double len = PolygonEditing.PolylineLength(v);
            shapeInfo.text = $"Точек {v.Count} · длина оси {MonitorUi.Num(len, 0)} м ({MonitorUi.Num(len / 1852.0, 2)} мили)";
            return;
        }
        bool shoal = k == AreaShapeKind.Shoal;
        shapeValueLabel.text = shoal ? "Наименьшая глубина над мелью, м" : "Высота берега над водой, м";
        Set(shapeValueField, MonitorUi.Num(shoal ? doc.Area.shoals[selShape.Index].depth : doc.Area.land[selShape.Index].height, 1));
        double s = PolygonEditing.AreaM2(v);
        shapeInfo.text = $"Вершин {v.Count} · площадь {MonitorUi.Num(s / 10000.0, s < 1e5 ? 2 : 1)} га · периметр {MonitorUi.Num(PolygonEditing.Perimeter(v), 0)} м" +
                         (PolygonEditing.SelfIntersects(v) ? "\n<color=#FFC85A>стороны пересекаются</color>" : "");
    }

    private void EditShapeName(string text)
    {
        string n = (text ?? "").Trim();
        if (doc == null || SelPoints == null || n.Length == 0) { FillFields(); return; }
        Edit(() => { PolygonEditing.SetName(doc.Area, selShape, n); doc.Commit(); });
    }

    // Глубина мели ≥ 0 (0 — осыхает), высота суши и ширина фарватера > 0
    private void EditShapeValue(string text)
    {
        if (SelPoints == null) return;
        AreaShapeKind k = selShape.Kind;
        if (!MonitorUi.TryNumber(text, out double v) || (k == AreaShapeKind.Shoal ? v < 0.0 : v <= 0.0)) { FillFields(); return; }
        Edit(() =>
        {
            if (k == AreaShapeKind.Shoal) doc.Area.shoals[selShape.Index].depth = (float)v;
            else if (k == AreaShapeKind.Land) doc.Area.land[selShape.Index].height = (float)v;
            else doc.Area.fairways[selShape.Index].width = (float)v;
            doc.Commit();
        });
    }

    private void EditBerth(System.Action<BerthArea> change)
    {
        BerthArea b = SelBerth;
        if (b == null) return;
        mapDirty = true;
        Edit(() => { change(b); doc.Commit(); });
    }

    // Глубина и высота кромки > 0
    private void EditBerthNumber(string text, System.Action<BerthArea, double> set)
    {
        if (SelBerth == null) return;
        if (!MonitorUi.TryNumber(text, out double v) || v <= 0.0) { FillFields(); return; }
        EditBerth(b => set(b, v));
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
    private void ShapeHover(bool inside, double px, double py)
    {
        List<WorldPosition> v = SelPoints;
        bool busy = dragVertex >= 0 || dragShape;
        if (!busy)
        {
            hotVertex = hotMid = -1;
            if (inside && v != null && tool != Tool.Bounds && draft.Count == 0)
            {
                hotVertex = PolygonEditing.NearestVertex(v, viewport, px, py, 9.0);
                if (hotVertex < 0 && selShape.Kind != AreaShapeKind.Berth)
                    hotMid = PolygonEditing.NearestMidpoint(v, viewport, px, py, 7.0, PolygonEditing.Closed(selShape.Kind));
            }
        }
        hoverShape = inside && tool == Tool.Select && !busy && hotVertex < 0 && hotMid < 0 ? PolygonEditing.HitShapeAt(doc.Area, viewport, px, py) : AreaShape.None;
        if (hoverShape.Equals(selShape)) hoverShape = AreaShape.None;
    }

    /// <summary>Нажатие на ручку выделенной фигуры: true — взято.</summary>
    private bool ShapeHandlePress(WorldPosition snapped)
    {
        List<WorldPosition> v = SelPoints;
        if (v == null) return false;
        if (hotVertex >= 0) { dragVertex = hotVertex; return true; }
        if (hotMid >= 0)
        {
            dragVertex = PolygonEditing.InsertVertex(v, hotMid, snapped);
            PolygonEditing.SetPoints(doc.Area, selShape, v);
            hotMid = -1;
            mapDirty = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// «Выбор»: щелчок по фигуре — выделить. Тянуть целиком можно только уже выделенную: иначе
    /// обычный сдвиг карты левой кнопкой по берегу двигал бы весь берег.
    /// </summary>
    private bool ShapeBodyPress(Vector2 local, double px, double py, WorldPosition snapped)
    {
        AreaShape s = PolygonEditing.HitShapeAt(doc.Area, viewport, px, py);
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
        shapeFrom = SelPoints;
        pressSnapped = snapped;
        pressLocal = local;
        return true;
    }

    private void ShapeDrag(Vector2 local, WorldPosition snapped)
    {
        List<WorldPosition> v = SelPoints;
        if (v == null) { dragVertex = -1; dragShape = false; return; }
        if (dragVertex >= 0 && dragVertex < v.Count && !v[dragVertex].Equals(snapped))
        {
            v[dragVertex] = snapped;
            PolygonEditing.SetPoints(doc.Area, selShape, v);
            mapDirty = true;
            FillShapeFields();
        }
        if (dragShape)
        {
            if (!shapeMoved && (local - pressLocal).magnitude > 3f) shapeMoved = true;
            if (shapeMoved)
            {
                PolygonEditing.Translate(v, shapeFrom, snapped.East - pressSnapped.East, snapped.North - pressSnapped.North);
                PolygonEditing.SetPoints(doc.Area, selShape, v);
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
            draftKind = DrawKind(tool);
            SelectAid(-1);
            SelectShape(AreaShape.None);
            draft.Add(snapped);
            return;
        }
        if (draftKind == AreaShapeKind.Berth)
        {
            // Второй щелчок — конец кромки; короче 20 м — не принимается (подсказка в строке состояния)
            if ((snapped - draft[0]).Magnitude < PolygonEditing.MinBerthM) return;
            draft.Add(snapped);
            FinishDraft();
            return;
        }
        if (dbl || DraftCloseHot(px, py)) { FinishDraft(); return; }
        if (!draft[draft.Count - 1].Equals(snapped)) draft.Add(snapped);
    }

    // Щелчок «закончить»: у многоугольника — первая вершина (≥ 3), у фарватера — последняя точка (≥ 2)
    private int DraftCloseIndex => PolygonEditing.Closed(draftKind) ? 0 : draft.Count - 1;

    private bool DraftCloseHot(double px, double py)
    {
        if (draftKind == AreaShapeKind.Berth || draft.Count < PolygonEditing.MinPoints(draftKind)) return false;
        viewport.ToPixel(draft[DraftCloseIndex], out double x, out double y);
        return (x - px) * (x - px) + (y - py) * (y - py) <= 10.0 * 10.0;
    }

    private void FinishDraft()
    {
        if (draft.Count < PolygonEditing.MinPoints(draftKind)) return;   // мало точек — продолжать рисовать
        AreaShape s = PolygonEditing.Add(doc.Area, draftKind, draft);
        if (s.IsNone) return;
        draft.Clear();
        doc.Commit();
        mapDirty = true;
        SelectShape(s);
        Debug.Log($"[Редактор] {PolygonEditing.Title(s.Kind)} «{PolygonEditing.Name(doc.Area, s)}»: точек {SelPoints.Count}.");
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
    /// <summary>Delete: знак; точка под курсором (если останется не меньше наименьшего числа); иначе вся фигура.</summary>
    private void DeleteSelected()
    {
        if (SelAid != null) { DeleteAid(); return; }
        List<WorldPosition> v = SelPoints;
        if (v == null) return;
        if (hotVertex >= 0 && selShape.Kind != AreaShapeKind.Berth && PolygonEditing.RemoveVertex(v, hotVertex, PolygonEditing.MinPoints(selShape.Kind)))
        {
            PolygonEditing.SetPoints(doc.Area, selShape, v);
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
        List<WorldPosition> v = SelPoints;
        if (v != null) overlay.Shape.AddRange(v);
        overlay.ShapeClosed = PolygonEditing.Closed(selShape.Kind);
        overlay.ShapeMidHandles = selShape.Kind != AreaShapeKind.Berth;
        overlay.ShapeHandles = v != null && tool != Tool.Bounds && draft.Count == 0;
        overlay.HotVertex = dragVertex >= 0 ? dragVertex : hotVertex;
        overlay.HotMid = hotMid;
        overlay.HoverShape.Clear();
        List<WorldPosition> h = doc != null ? PolygonEditing.Points(doc.Area, hoverShape) : null;
        if (h != null) overlay.HoverShape.AddRange(h);
        overlay.HoverClosed = PolygonEditing.Closed(hoverShape.Kind);
        overlay.Draft.Clear();
        overlay.Draft.AddRange(draft);
        overlay.DraftClosed = PolygonEditing.Closed(draftKind);
        overlay.DraftCloseIndex = DraftCloseIndex;
        overlay.DraftCloseHot = false;
        if (draft.Count > 0)
        {
            viewport.ToPixel(snapped, out double sx, out double sy);
            overlay.DraftCloseHot = DraftCloseHot(sx, sy);
        }
        overlay.Fairways.Clear();
        overlay.Berths.Clear();
        if (doc == null) return;
        foreach (FairwayLine f in doc.Area.fairways) if (f != null) overlay.Fairways.Add((f.axis, f.width));
        foreach (BerthArea b in doc.Area.berths) if (b != null) overlay.Berths.Add((b.a, b.b));
    }

    // Строка состояния при рисовании
    private string DraftStatus()
    {
        if (draft.Count == 0) return "";
        string t = PolygonEditing.Title(draftKind);
        switch (draftKind)
        {
            case AreaShapeKind.Berth:
                return $" · {t}: A поставлена — щелчок в B (кромка не короче {MonitorUi.Num(PolygonEditing.MinBerthM, 0)} м, вода справа от A→B); Esc — отменить";
            case AreaShapeKind.Fairway:
                return $" · {t}: точек {draft.Count} — щелчок по последней, двойной щелчок или Enter — закончить; Backspace — убрать последнюю; Esc — отменить";
            default:
                return $" · {t}: вершин {draft.Count} — щелчок по первой, двойной щелчок или Enter — замкнуть; Backspace — убрать последнюю; Esc — отменить";
        }
    }
}
