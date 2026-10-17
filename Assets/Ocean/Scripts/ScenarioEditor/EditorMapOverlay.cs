// ============================================================
// EditorMapOverlay.cs — Слой редактора поверх карты: сетка, границы района, ручки, курсор (запрос 21.2а)
//
// Карта акватории под ним — DebriefMapGraphic (то же, что в окне инструктора и разборе). Здесь —
// только то, что нужно для правки: сетка шага привязки (прорежена до читаемой), рамка границ района
// с ручками в углах и на серединах сторон (выделенная — ярче), крестик узла привязки под курсором.
// 21.2б — знаки МАМС (форма и цвет по типу, выделенный — жёлтое кольцо, под курсором — голубое).
// 21.3а — мель/суша: выделенная — жёлтый контур, квадратные ручки вершин и круглые — середин сторон;
// под курсором — голубой контур; рисуемая — голубая ломаная, линия к курсору и замыкание.
// 21.3б — кромки фарватеров (ось ± ширина/2), стрелка «вода» у причалов; фарватер и причал — без замыкания.
// 21.4а — слой сценария: коридор XTE, линия маршрута с ручками точек, середин участков и кромки XTE, судно на старте.
// Координаты: пиксель окна MapViewport = локальная точка от левого нижнего угла этой графики.
// ============================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class EditorMapOverlay : MaskableGraphic
{
    public static readonly Color GridMinor = new Color(1f, 1f, 1f, 0.06f);
    public static readonly Color GridMajor = new Color(1f, 1f, 1f, 0.14f);
    public static readonly Color BoundsColor = new Color(0.62f, 0.55f, 1f, 0.95f);
    public static readonly Color HandleColor = new Color(0.62f, 0.55f, 1f, 1f);
    public static readonly Color HandleHot = new Color(1f, 0.85f, 0.3f, 1f);
    public static readonly Color CursorColor = new Color(0.55f, 0.9f, 1f, 0.9f);

    public MapViewport Viewport;
    public double GridStep = 50.0;
    public bool ShowBounds = true, ShowHandles;
    public WorldPosition BoundsMin, BoundsMax;
    /// <summary>Ручка под курсором или перетаскиваемая (BoundsHandles), −1 — нет.</summary>
    public int HotHandle = -1;
    public bool ShowCursor;
    public WorldPosition Cursor;
    /// <summary>21.2б: знаки МАМС (рисуются здесь, а не картой — тянуть без перестройки карты).</summary>
    public readonly List<(WorldPosition p, AidType t)> Aids = new List<(WorldPosition, AidType)>();
    public int SelectedAid = -1, HotAid = -1;
    /// <summary>Только значок знака этого типа в центре (кнопка палитры), без карты.</summary>
    public AidType? Swatch;
    /// <summary>21.3а: выделенная фигура (контур, ручки вершин и середин сторон), фигура под курсором, рисуемый контур.</summary>
    public readonly List<WorldPosition> Shape = new List<WorldPosition>();
    public readonly List<WorldPosition> HoverShape = new List<WorldPosition>();
    public readonly List<WorldPosition> Draft = new List<WorldPosition>();
    public int HotVertex = -1, HotMid = -1;
    public bool ShapeHandles, DraftCloseHot;
    /// <summary>21.3б: фарватер и причал — открытые (без замыкания); ручки середин — не у причала;
    /// кольцо «закончить» — у первой (многоугольник) или последней (фарватер) точки.</summary>
    public bool ShapeClosed = true, ShapeMidHandles = true, DraftClosed = true, HoverClosed = true;
    public int DraftCloseIndex;
    /// <summary>21.3б: кромки всех фарватеров (ось ± ширина/2) и стрелка «вода» у каждого причала.</summary>
    public readonly List<(List<WorldPosition> axis, float width)> Fairways = new List<(List<WorldPosition>, float)>();
    public readonly List<(WorldPosition a, WorldPosition b)> Berths = new List<(WorldPosition, WorldPosition)>();
    public static readonly Color FairwayEdge = new Color(0.55f, 0.65f, 0.75f, 0.45f);
    public static readonly Color WaterArrow = new Color(0.45f, 0.75f, 1f, 0.9f);
    /// <summary>21.4а: слой сценария — маршрут с коридором XTE (ручки точек, середин участков, кромки), судно на старте.</summary>
    public bool ScenarioLayer;
    public List<RouteWaypoint> Route;
    public int SelectedPoint = -1, HotPoint = -1, HotLegMid = -1, HotXte = -1;
    public WorldPosition StartPos, HeadingHandle;
    public float StartHeading, ShipLength = 45f, ShipBeam = 10f;
    public bool StartSelected, StartHot, HeadingHot;
    public static readonly Color RouteColor = new Color(1f, 0.6f, 0.25f, 1f);
    public static readonly Color CorridorFill = new Color(1f, 0.6f, 0.25f, 0.08f);
    public static readonly Color CorridorEdge = new Color(1f, 0.6f, 0.25f, 0.4f);
    public static readonly Color OwnShipColor = new Color(0.35f, 0.9f, 0.45f, 1f);

    /// <summary>Ручки рамки: 0–3 — углы (ЮЗ, ЮВ, СВ, СЗ), 4–7 — середины сторон (Ю, В, С, З).</summary>
    public static List<WorldPosition> BoundsHandles(WorldPosition min, WorldPosition max)
    {
        double cx = 0.5 * (min.East + max.East), cy = 0.5 * (min.North + max.North);
        return new List<WorldPosition>
        {
            new WorldPosition(min.East, min.North), new WorldPosition(max.East, min.North),
            new WorldPosition(max.East, max.North), new WorldPosition(min.East, max.North),
            new WorldPosition(cx, min.North), new WorldPosition(max.East, cy),
            new WorldPosition(cx, max.North), new WorldPosition(min.East, cy)
        };
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (Swatch.HasValue) { AidSymbol(vh, GetPixelAdjustedRect().center, Swatch.Value); return; }   // значок кнопки палитры
        if (Viewport == null) return;
        Rect r = GetPixelAdjustedRect();
        MapRect b = Viewport.Bounds;

        // Сетка: прореженный шаг; каждая 5-я — ярче
        double s = MapGrid.DisplayStep(GridStep, Viewport.MetersPerPixel);
        foreach (double e in MapGrid.Lines(b.MinE, b.MaxE, s))
        {
            float x = Px(new WorldPosition(e, 0), r).x;
            Quad(vh, new Vector2(x - 0.5f, r.yMin), new Vector2(x + 0.5f, r.yMax), Major(e, s) ? GridMajor : GridMinor);
        }
        foreach (double n in MapGrid.Lines(b.MinN, b.MaxN, s))
        {
            float y = Px(new WorldPosition(0, n), r).y;
            Quad(vh, new Vector2(r.xMin, y - 0.5f), new Vector2(r.xMax, y + 0.5f), Major(n, s) ? GridMajor : GridMinor);
        }

        if (ShowBounds)
        {
            Vector2 p0 = Px(BoundsMin, r), p1 = Px(BoundsMax, r);
            float t = 2f;
            Quad(vh, new Vector2(p0.x, p0.y - t * 0.5f), new Vector2(p1.x, p0.y + t * 0.5f), BoundsColor);
            Quad(vh, new Vector2(p0.x, p1.y - t * 0.5f), new Vector2(p1.x, p1.y + t * 0.5f), BoundsColor);
            Quad(vh, new Vector2(p0.x - t * 0.5f, p0.y), new Vector2(p0.x + t * 0.5f, p1.y), BoundsColor);
            Quad(vh, new Vector2(p1.x - t * 0.5f, p0.y), new Vector2(p1.x + t * 0.5f, p1.y), BoundsColor);
            if (ShowHandles)
            {
                List<WorldPosition> hs = BoundsHandles(BoundsMin, BoundsMax);
                for (int i = 0; i < hs.Count; i++)
                {
                    Vector2 c = Px(hs[i], r);
                    float h = i == HotHandle ? 8f : 6f;
                    Quad(vh, c - new Vector2(h, h), c + new Vector2(h, h), i == HotHandle ? HandleHot : HandleColor);
                }
            }
        }

        // 21.3б: кромки фарватеров (по отрезкам оси, ± ширина/2) и стрелки «вода» у причалов
        foreach (var f in Fairways)
        {
            float half = (float)(0.5 * f.width / Viewport.MetersPerPixel);
            if (half < 2f || f.axis == null) continue;   // мелко — хватит оси на карте
            for (int i = 0; i + 1 < f.axis.Count; i++)
            {
                Vector2 p0 = Px(f.axis[i], r), p1 = Px(f.axis[i + 1], r), d = p1 - p0;
                if (d.sqrMagnitude < 1e-6f) continue;
                Vector2 n = new Vector2(-d.y, d.x).normalized * half;
                Seg(vh, p0 + n, p1 + n, 1f, FairwayEdge);
                Seg(vh, p0 - n, p1 - n, 1f, FairwayEdge);
            }
        }
        foreach (var bq in Berths)
        {
            Vector2 p0 = Px(bq.a, r), p1 = Px(bq.b, r), d = p1 - p0;
            if (d.magnitude < 8f) continue;
            Vector2 t = d.normalized, n = new Vector2(t.y, -t.x);   // справа от A→B — вода
            Vector2 m = 0.5f * (p0 + p1), tip = m + n * 16f;
            Seg(vh, m + n * 3f, tip, 2f, WaterArrow);
            Tri(vh, tip + n * 5f, tip - t * 5f, tip + t * 5f, WaterArrow);
        }

        if (ScenarioLayer) DrawScenario(vh, r);   // 21.4а

        // 21.3а: фигуры
        if (HoverShape.Count >= 2) Outline(vh, HoverShape, r, 1.5f, new Color(CursorColor.r, CursorColor.g, CursorColor.b, 0.6f), HoverClosed);
        if (Shape.Count >= 2)
        {
            Outline(vh, Shape, r, ShapeClosed ? 2f : 3f, HandleHot, ShapeClosed);
            if (ShapeHandles)
            {
                for (int i = 0; ShapeMidHandles && i < PolygonEditing.EdgeCount(Shape, ShapeClosed); i++)
                {
                    Vector2 m = Px(PolygonEditing.Midpoint(Shape, i), r);
                    Disc(vh, m, i == HotMid ? 6f : 4f, i == HotMid ? HandleHot : new Color(HandleColor.r, HandleColor.g, HandleColor.b, 0.7f));
                }
                for (int i = 0; i < Shape.Count; i++)
                {
                    Vector2 c = Px(Shape[i], r);
                    float h = i == HotVertex ? 7f : 5f;
                    Quad(vh, c - new Vector2(h + 1f, h + 1f), c + new Vector2(h + 1f, h + 1f), new Color(0.05f, 0.05f, 0.06f, 1f));
                    Quad(vh, c - new Vector2(h, h), c + new Vector2(h, h), i == HotVertex ? HandleHot : Color.white);
                }
            }
        }
        if (Draft.Count > 0)
        {
            Outline(vh, Draft, r, 2f, CursorColor, false);
            Seg(vh, Px(Draft[Draft.Count - 1], r), Px(Cursor, r), 1.5f, new Color(CursorColor.r, CursorColor.g, CursorColor.b, 0.6f));
            if (DraftClosed && Draft.Count >= 2) Seg(vh, Px(Cursor, r), Px(Draft[0], r), 1f, new Color(CursorColor.r, CursorColor.g, CursorColor.b, 0.25f));
            for (int i = 0; i < Draft.Count; i++) Disc(vh, Px(Draft[i], r), 4f, CursorColor);
            if (DraftCloseHot && DraftCloseIndex >= 0 && DraftCloseIndex < Draft.Count) Ring(vh, Px(Draft[DraftCloseIndex], r), 10f, 2f, HandleHot);
        }

        for (int i = 0; i < Aids.Count; i++)
        {
            Vector2 c = Px(Aids[i].p, r);
            if (c.x < r.xMin - 20f || c.x > r.xMax + 20f || c.y < r.yMin - 20f || c.y > r.yMax + 20f) continue;
            if (i == SelectedAid) Ring(vh, c, 13f, 2.5f, HandleHot);
            else if (i == HotAid) Ring(vh, c, 12f, 1.5f, CursorColor);
            AidSymbol(vh, c, Aids[i].t);
        }

        if (ShowCursor)
        {
            Vector2 c = Px(Cursor, r);
            Quad(vh, new Vector2(c.x - 9f, c.y - 0.75f), new Vector2(c.x + 9f, c.y + 0.75f), CursorColor);
            Quad(vh, new Vector2(c.x - 0.75f, c.y - 9f), new Vector2(c.x + 0.75f, c.y + 9f), CursorColor);
        }
    }

    // 21.4а: коридор XTE (полоса ± XTE участка), линия маршрута, ручки; судно на старте по курсу, в масштабе
    // (не меньше 18 × 7 пикс.), у выделенного — линия и ручка курса
    private void DrawScenario(VertexHelper vh, Rect r)
    {
        float mpp = (float)Viewport.MetersPerPixel;
        List<RouteWaypoint> rt = Route;
        int n = rt != null ? rt.Count : 0;
        for (int i = 0; i + 1 < n; i++)
        {
            if (rt[i] == null || rt[i + 1] == null) continue;
            Vector2 p0 = Px(rt[i].position, r), p1 = Px(rt[i + 1].position, r), d = p1 - p0;
            if (d.sqrMagnitude < 1e-6f) continue;
            float half = rt[i + 1].xteLimit / mpp;
            Vector2 nn = new Vector2(d.y, -d.x).normalized;   // вправо от участка
            if (half >= 1f)
            {
                Seg(vh, p0, p1, 2f * half, CorridorFill);
                Seg(vh, p0 + nn * half, p1 + nn * half, 1f, CorridorEdge);
                Seg(vh, p0 - nn * half, p1 - nn * half, 1f, CorridorEdge);
            }
            Seg(vh, p0, p1, 2.5f, RouteColor);
        }
        for (int i = 0; i + 1 < n; i++)
        {
            if (rt[i] == null || rt[i + 1] == null) continue;
            Vector2 m = Px(RouteEditing.LegMid(rt, i), r);
            Disc(vh, m, i == HotLegMid ? 6f : 4f, i == HotLegMid ? HandleHot : new Color(RouteColor.r, RouteColor.g, RouteColor.b, 0.7f));
            if (RouteEditing.XteHandleVisible(rt, i, Viewport))
            {
                Vector2 h = Px(RouteEditing.XteHandle(rt, i), r);
                Diamond(vh, h, i == HotXte ? 8f : 6f, new Color(0.05f, 0.05f, 0.06f, 1f));
                Diamond(vh, h, i == HotXte ? 6f : 4.5f, i == HotXte ? HandleHot : RouteColor);
            }
        }
        for (int i = 0; i < n; i++)
        {
            if (rt[i] == null) continue;
            Vector2 c = Px(rt[i].position, r);
            bool sel = i == SelectedPoint, hot = i == HotPoint;
            float h = sel || hot ? 7f : 5f;
            Quad(vh, c - new Vector2(h + 1f, h + 1f), c + new Vector2(h + 1f, h + 1f), new Color(0.05f, 0.05f, 0.06f, 1f));
            Quad(vh, c - new Vector2(h, h), c + new Vector2(h, h), sel ? HandleHot : hot ? CursorColor : (i == 0 ? Color.white : RouteColor));
        }

        // Судно на старте
        float a = StartHeading * Mathf.Deg2Rad;
        Vector2 f = new Vector2(Mathf.Sin(a), Mathf.Cos(a)), s = new Vector2(f.y, -f.x);
        float L = Mathf.Max(18f, ShipLength / mpp), B = Mathf.Max(7f, ShipBeam / mpp);
        Vector2 c0 = Px(StartPos, r);
        Vector2 bow = c0 + f * (0.5f * L), sb = c0 + f * (0.2f * L) + s * (0.5f * B), pb = c0 + f * (0.2f * L) - s * (0.5f * B);
        Vector2 ss = c0 - f * (0.5f * L) + s * (0.5f * B), ps = c0 - f * (0.5f * L) - s * (0.5f * B);
        Color hull = StartSelected ? HandleHot : StartHot ? CursorColor : OwnShipColor;
        if (StartSelected)
        {
            Vector2 hh = Px(HeadingHandle, r);
            Seg(vh, bow, hh, 1.5f, new Color(hull.r, hull.g, hull.b, 0.7f));
            Disc(vh, hh, HeadingHot ? 7f : 5f, HeadingHot ? CursorColor : HandleHot);
        }
        Tri(vh, bow, pb, sb, hull);
        Tri(vh, pb, ps, ss, hull);
        Tri(vh, pb, ss, sb, hull);
        Seg(vh, c0 - f * (0.3f * L), c0 + f * (0.35f * L), 1.5f, new Color(0.05f, 0.05f, 0.06f, 0.8f));   // диаметральная — где нос
    }

    private static bool Major(double v, double s) => System.Math.Abs(System.Math.IEEERemainder(v, 5.0 * s)) < 0.5 * s;

    /// <summary>Точка мира → локальная точка графики.</summary>
    public Vector2 Px(WorldPosition p, Rect r)
    {
        Viewport.ToPixel(p, out double px, out double py);
        return new Vector2(r.xMin + (float)px, r.yMin + (float)py);
    }

    // Знак по форме МАМС (упрощённо, вид сверху): левый — квадрат (цилиндр), правый — треугольник (конус),
    // кардинальные — ромб с чёрной серединой, отдельной опасности — круг с красной серединой,
    // осевой — красный круг с белой серединой
    public static void AidSymbol(VertexHelper vh, Vector2 c, AidType t)
    {
        Color main = DebriefMapGraphic.AidColor(t), dark = new Color(0.05f, 0.05f, 0.06f, 1f);
        switch (t)
        {
            case AidType.LateralPort:
                Quad(vh, c - new Vector2(6.5f, 6.5f), c + new Vector2(6.5f, 6.5f), dark);
                Quad(vh, c - new Vector2(5f, 5f), c + new Vector2(5f, 5f), main);
                break;
            case AidType.LateralStarboard:
                Tri(vh, c + new Vector2(0f, 9f), c + new Vector2(-8f, -6f), c + new Vector2(8f, -6f), dark);
                Tri(vh, c + new Vector2(0f, 6.5f), c + new Vector2(-5.5f, -4.5f), c + new Vector2(5.5f, -4.5f), main);
                break;
            case AidType.IsolatedDanger:
                Disc(vh, c, 7f, dark);
                Disc(vh, c, 5.5f, new Color(0.8f, 0.8f, 0.8f, 1f));
                Disc(vh, c, 2.5f, new Color(1f, 0.3f, 0.25f, 1f));
                break;
            case AidType.SafeWater:
                Disc(vh, c, 7f, dark);
                Disc(vh, c, 5.5f, main);
                Disc(vh, c, 2.5f, Color.white);
                break;
            default:   // кардинальные
                Diamond(vh, c, 9f, dark);
                Diamond(vh, c, 7f, new Color(1f, 0.85f, 0.2f, 1f));
                Diamond(vh, c, 3f, dark);
                break;
        }
    }

    // Ломаная по точкам мира (closed — с замыканием)
    private void Outline(VertexHelper vh, List<WorldPosition> pts, Rect r, float w, Color col, bool closed)
    {
        int n = pts.Count;
        for (int i = 0; i + 1 < n; i++) Seg(vh, Px(pts[i], r), Px(pts[i + 1], r), w, col);
        if (closed && n >= 3) Seg(vh, Px(pts[n - 1], r), Px(pts[0], r), w, col);
    }

    // Отрезок толщиной w пикс.
    private static void Seg(VertexHelper vh, Vector2 a, Vector2 b, float w, Color col)
    {
        Vector2 d = b - a;
        if (d.sqrMagnitude < 1e-6f) return;
        Vector2 nrm = new Vector2(-d.y, d.x).normalized * (0.5f * w);
        int i = vh.currentVertCount;
        vh.AddVert(new Vector3(a.x + nrm.x, a.y + nrm.y), col, Vector2.zero);
        vh.AddVert(new Vector3(b.x + nrm.x, b.y + nrm.y), col, Vector2.zero);
        vh.AddVert(new Vector3(b.x - nrm.x, b.y - nrm.y), col, Vector2.zero);
        vh.AddVert(new Vector3(a.x - nrm.x, a.y - nrm.y), col, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }

    private static void Tri(VertexHelper vh, Vector2 a, Vector2 b, Vector2 d, Color col)
    {
        int i = vh.currentVertCount;
        vh.AddVert(new Vector3(a.x, a.y), col, Vector2.zero);
        vh.AddVert(new Vector3(b.x, b.y), col, Vector2.zero);
        vh.AddVert(new Vector3(d.x, d.y), col, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
    }

    private static void Diamond(VertexHelper vh, Vector2 c, float h, Color col)
    {
        Tri(vh, c + new Vector2(0f, h), c + new Vector2(-h, 0f), c + new Vector2(h, 0f), col);
        Tri(vh, c + new Vector2(0f, -h), c + new Vector2(h, 0f), c + new Vector2(-h, 0f), col);
    }

    private static void Disc(VertexHelper vh, Vector2 c, float rad, Color col)
    {
        const int n = 16;
        for (int k = 0; k < n; k++)
        {
            float a0 = 2f * Mathf.PI * k / n, a1 = 2f * Mathf.PI * (k + 1) / n;
            Tri(vh, c, c + rad * new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)), c + rad * new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)), col);
        }
    }

    private static void Ring(VertexHelper vh, Vector2 c, float rad, float w, Color col)
    {
        const int n = 24;
        for (int k = 0; k < n; k++)
        {
            float a0 = 2f * Mathf.PI * k / n, a1 = 2f * Mathf.PI * (k + 1) / n;
            Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)), d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
            Tri(vh, c + rad * d0, c + (rad - w) * d0, c + rad * d1, col);
            Tri(vh, c + rad * d1, c + (rad - w) * d0, c + (rad - w) * d1, col);
        }
    }

    private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Color c)
    {
        int i = vh.currentVertCount;
        vh.AddVert(new Vector3(a.x, a.y), c, Vector2.zero);
        vh.AddVert(new Vector3(a.x, b.y), c, Vector2.zero);
        vh.AddVert(new Vector3(b.x, b.y), c, Vector2.zero);
        vh.AddVert(new Vector3(b.x, a.y), c, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }
}
