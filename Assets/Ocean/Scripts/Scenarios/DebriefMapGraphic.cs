// ============================================================
// DebriefMapGraphic.cs — Схема траектории на VR-панели разбора (C3)
//
// Рисует описание схемы DebriefMapData (то же, что SVG в report.html) сеткой
// UI: треугольники мелей и суши, полосы линий, значки — цветом вершин, без
// текстур и своих шейдеров (материал UI по умолчанию; работает в SPI и на
// Quest). Вид — одно из окон DebriefMapData.Views, масштаб по осям одинаковый,
// всё вне окна отсекается в геометрии (MapGeometry). Подписи (цели,
// наименьшая дистанция, манёвр) — текстом TextMeshPro: их ставит ScenarioPanel
// по Labels() в координатах этой графики. Строится один раз на смену вида.
// D1: тот же класс — неподвижный слой карты окна инструктора (DebriefMap.BuildStatic),
// наследник InstructorMapGraphic — подвижный (суда, следы, векторы); примитивы — protected.
// Отметка вводной (MapLayer.Failure) — кольцо.
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// CanvasRenderer — явно: в uGUI этой версии его требует Image, но не базовый Graphic; без него
// XRI-лучи UI бросали MissingComponentException каждый кадр (Error Pause при открытии «Схемы», C4)
[RequireComponent(typeof(CanvasRenderer))]
public class DebriefMapGraphic : MaskableGraphic
{
    public struct Label
    {
        public Vector2 Local;
        public string Text;
        public Color Color;
    }

    public static readonly Color Background = new Color(0.05f, 0.08f, 0.11f, 1f);
    public static readonly Color LandColor = new Color(0.36f, 0.30f, 0.22f, 1f);
    public static readonly Color ShoalColor = new Color(0.85f, 0.6f, 0.2f, 0.42f);
    public static readonly Color FairwayColor = new Color(0.45f, 0.5f, 0.56f, 1f);
    public static readonly Color CorridorColor = new Color(0.35f, 0.75f, 0.45f, 0.75f);
    public static readonly Color RouteColor = new Color(0.45f, 0.85f, 0.55f, 1f);
    public static readonly Color OwnColor = new Color(0.55f, 0.9f, 1f, 1f);
    public static readonly Color[] TargetColors = { new Color(1f, 0.6f, 0.25f), new Color(0.95f, 0.45f, 0.85f), new Color(1f, 0.9f, 0.35f) };
    public static readonly Color OkColor = new Color(0.45f, 0.9f, 0.5f);
    public static readonly Color BadColor = new Color(1f, 0.35f, 0.3f);
    public static readonly Color GridColor = new Color(1f, 1f, 1f, 0.07f);
    public static readonly Color FailureColor = new Color(1f, 0.78f, 0.2f);

    private DebriefMapData data;
    private int viewIndex;
    protected MapRect view;
    protected double scale;   // единиц холста на метр
    protected Rect area;

    public DebriefMapData Data => data;
    public int ViewIndex => viewIndex;
    public DebriefMapView View => data != null && viewIndex >= 0 && viewIndex < data.Views.Count ? data.Views[viewIndex] : null;
    /// <summary>Шаг сетки, м (для подписи).</summary>
    public double GridStepM { get; private set; }

    public static Color TargetColor(int series) => series < 0 ? OwnColor : TargetColors[series % TargetColors.Length];

    public void SetView(DebriefMapData d, int index)
    {
        data = d;
        viewIndex = d == null || d.Views.Count == 0 ? 0 : Mathf.Clamp(index, 0, d.Views.Count - 1);
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected void Layout()
    {
        area = GetPixelAdjustedRect();
        DebriefMapView v = View;
        if (v == null) return;
        view = v.Bounds.WithAspect(area.width / Mathf.Max(1f, area.height));
        scale = area.width / view.Width;
        GridStepM = SvgPlot.NiceStep((float)Math.Max(view.Width, view.Height));
    }

    public Vector2 ToLocal(WorldPosition p) =>
        new Vector2((float)(area.xMin + (p.East - view.MinE) * scale), (float)(area.yMin + (p.North - view.MinN) * scale));

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Layout();
        Quad(vh, new Vector2(area.xMin, area.yMin), new Vector2(area.xMax, area.yMin), new Vector2(area.xMax, area.yMax), new Vector2(area.xMin, area.yMax), Background);
        if (View == null) return;

        // Сетка
        for (double e = Math.Ceiling(view.MinE / GridStepM) * GridStepM; e <= view.MaxE; e += GridStepM)
            Segment(vh, new WorldPosition(e, view.MinN), new WorldPosition(e, view.MaxN), 1f, GridColor);
        for (double n = Math.Ceiling(view.MinN / GridStepM) * GridStepM; n <= view.MaxN; n += GridStepM)
            Segment(vh, new WorldPosition(view.MinE, n), new WorldPosition(view.MaxE, n), 1f, GridColor);

        foreach (MapPolygon p in data.Polygons)
            Polygon(vh, p.Points, p.Layer == MapLayer.Land ? LandColor : ShoalColor);
        foreach (MapLine l in data.Lines)
            if (l.Layer == MapLayer.Fairway) Polyline(vh, l.Points, 1.2f, FairwayColor, dashed: true);
        foreach (MapLine l in data.Lines)
            if (l.Layer == MapLayer.Corridor) Polyline(vh, l.Points, 1.2f, CorridorColor, dashed: true);
        foreach (MapLine l in data.Lines)
            if (l.Layer == MapLayer.Route) Polyline(vh, l.Points, 1.6f, RouteColor);
        foreach (MapMarker m in data.Markers)
        {
            if (m.Layer == MapLayer.Waypoint) Ring(vh, m.Position, 4.5f, 1.5f, RouteColor);
            else if (m.Layer == MapLayer.Aid) Diamond(vh, m.Position, 4f, AidColor(m.Aid));
        }
        foreach (MapLine l in data.Lines)
            if (l.Layer == MapLayer.TargetTrack) Polyline(vh, l.Points, 2.4f, TargetColor(l.Series));
        foreach (MapLine l in data.Lines)
            if (l.Layer == MapLayer.OwnTrack) Polyline(vh, l.Points, 3.2f, OwnColor);
        DebriefMapView v = View;
        foreach (MapMarker m in data.Markers)
            if (m.Layer == MapLayer.TimeTick && Math.Abs(Math.IEEERemainder(m.Time, v.TickStepS)) < 1e-6)
                Dot(vh, m.Position, 2.6f, OwnColor);
        foreach (MapLine l in data.Lines)
            if (l.Layer == MapLayer.MinLink && l.Points.Count == 2) Polyline(vh, l.Points, 2f, l.Ok ? OkColor : BadColor);
        foreach (MapMarker m in data.Markers)
            switch (m.Layer)
            {
                case MapLayer.MinPoint: Dot(vh, m.Position, 4f, m.Ok ? OkColor : BadColor); break;
                case MapLayer.Manoeuvre: Ring(vh, m.Position, 8f, 2f, OwnColor); break;
                case MapLayer.Start: Dot(vh, m.Position, 4.5f, Color.white); break;
                case MapLayer.Accident: Cross(vh, m.Position, 9f, 3f, BadColor); break;
                case MapLayer.Failure: Ring(vh, m.Position, 7f, 2.5f, FailureColor); break;   // D1: вводная
            }
    }

    /// <summary>Подписи для текущего вида (координаты этой графики).</summary>
    public List<Label> Labels()
    {
        var list = new List<Label>();
        if (View == null) return list;
        Layout();
        foreach (MapLine l in data.Lines)
        {
            if (l.Layer == MapLayer.TargetTrack)
            {
                for (int i = l.Points.Count - 1; i >= 0; i--)
                    if (view.Contains(l.Points[i])) { list.Add(new Label { Local = ToLocal(l.Points[i]) + new Vector2(6, 6), Text = l.Label, Color = TargetColor(l.Series) }); break; }
            }
            else if (l.Layer == MapLayer.MinLink && l.Points.Count == 2 && view.Contains(l.Points[0]) && view.Contains(l.Points[1]))
            {
                Vector2 mid = (ToLocal(l.Points[0]) + ToLocal(l.Points[1])) * 0.5f;
                list.Add(new Label { Local = mid + new Vector2(8, 0), Text = l.Label, Color = l.Ok ? OkColor : BadColor });
            }
        }
        foreach (MapMarker m in data.Markers)
            if ((m.Layer == MapLayer.Manoeuvre || m.Layer == MapLayer.Accident || m.Layer == MapLayer.Failure) && view.Contains(m.Position))
                list.Add(new Label
                {
                    Local = ToLocal(m.Position) + new Vector2(10, -10),
                    Text = m.Layer == MapLayer.Manoeuvre ? $"манёвр ({m.Label})" : m.Label,
                    Color = m.Layer == MapLayer.Manoeuvre ? OwnColor : m.Layer == MapLayer.Failure ? FailureColor : BadColor
                });
        return list;
    }

    // ─────────── Примитивы ───────────
    protected static Color AidColor(AidType t)
    {
        switch (IalaMarks.Get(t).MainColor)
        {
            case MarkColor.Red: return new Color(1f, 0.3f, 0.25f);
            case MarkColor.Green: return new Color(0.3f, 0.9f, 0.35f);
            case MarkColor.Yellow: return new Color(1f, 0.85f, 0.2f);
            case MarkColor.White: return Color.white;
            default: return new Color(0.8f, 0.8f, 0.8f);   // чёрный знак на тёмном фоне — светло-серым
        }
    }

    private readonly List<(double s0, double s1)> dashRuns = new List<(double s0, double s1)>();

    protected void Polyline(VertexHelper vh, List<WorldPosition> pts, float width, Color c, bool dashed = false)
    {
        List<WorldPosition> dec = MapGeometry.Decimate(pts, 1.5 / scale);
        double dash = 7 / scale, gap = 5 / scale, phase = 0;
        for (int i = 1; i < dec.Count; i++)
        {
            WorldPosition a = dec[i - 1], b = dec[i];
            if (!dashed) { Segment(vh, a, b, width, c); continue; }
            double de = b.East - a.East, dn = b.North - a.North, len = Math.Sqrt(de * de + dn * dn);
            // Штрихи — MapGeometry.DashRuns (фаза в пределах периода; прежний цикл зацикливался, C4)
            MapGeometry.DashRuns(len, dash, gap, ref phase, dashRuns);
            foreach ((double s0, double s1) in dashRuns)
                Segment(vh, new WorldPosition(a.East + de * s0 / len, a.North + dn * s0 / len),
                            new WorldPosition(a.East + de * s1 / len, a.North + dn * s1 / len), width, c);
        }
    }

    protected void Segment(VertexHelper vh, WorldPosition a, WorldPosition b, float width, Color c)
    {
        if (!MapGeometry.ClipSegment(view, ref a, ref b)) return;
        Vector2 pa = ToLocal(a), pb = ToLocal(b);
        Vector2 d = pb - pa;
        if (d.sqrMagnitude < 1e-6f) return;
        Vector2 n = new Vector2(-d.y, d.x).normalized * (width * 0.5f);
        Quad(vh, pa - n, pb - n, pb + n, pa + n, c);
    }

    protected void Polygon(VertexHelper vh, List<WorldPosition> pts, Color c)
    {
        if (pts == null || pts.Count < 3) return;
        List<WorldPosition> clipped = MapGeometry.ClipPolygon(pts, view);
        if (clipped.Count < 3) return;
        List<int> tri = MapGeometry.Triangulate(clipped);
        int start = vh.currentVertCount;
        foreach (WorldPosition p in clipped) vh.AddVert(ToLocal(p), c, Vector2.zero);
        for (int i = 0; i + 2 < tri.Count; i += 3) vh.AddTriangle(start + tri[i], start + tri[i + 1], start + tri[i + 2]);
    }

    protected void Dot(VertexHelper vh, WorldPosition p, float r, Color c)
    {
        if (!view.Contains(p)) return;
        Vector2 o = ToLocal(p);
        int start = vh.currentVertCount;
        vh.AddVert(o, c, Vector2.zero);
        const int seg = 10;
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2 / seg;
            vh.AddVert(o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, c, Vector2.zero);
        }
        for (int i = 0; i < seg; i++) vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % seg);
    }

    protected void Ring(VertexHelper vh, WorldPosition p, float r, float w, Color c)
    {
        if (!view.Contains(p)) return;
        Vector2 o = ToLocal(p);
        const int seg = 14;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
            Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)), d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
            Quad(vh, o + d0 * (r - w / 2), o + d1 * (r - w / 2), o + d1 * (r + w / 2), o + d0 * (r + w / 2), c);
        }
    }

    protected void Diamond(VertexHelper vh, WorldPosition p, float r, Color c)
    {
        if (!view.Contains(p)) return;
        Vector2 o = ToLocal(p);
        Quad(vh, o + new Vector2(0, -r), o + new Vector2(r, 0), o + new Vector2(0, r), o + new Vector2(-r, 0), c);
    }

    protected void Cross(VertexHelper vh, WorldPosition p, float r, float w, Color c)
    {
        if (!view.Contains(p)) return;
        Vector2 o = ToLocal(p);
        foreach (Vector2 d in new[] { new Vector2(1, 1), new Vector2(1, -1) })
        {
            Vector2 a = o - d * (r * 0.7071f), b = o + d * (r * 0.7071f);
            Vector2 n = new Vector2(-d.y, d.x).normalized * (w * 0.5f);
            Quad(vh, a - n, b - n, b + n, a + n, c);
        }
    }

    protected static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a, col, Vector2.zero);
        vh.AddVert(b, col, Vector2.zero);
        vh.AddVert(c, col, Vector2.zero);
        vh.AddVert(d, col, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i + 2, i + 3, i);
    }
}
