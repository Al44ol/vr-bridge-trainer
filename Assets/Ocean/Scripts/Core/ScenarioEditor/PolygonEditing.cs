// ============================================================
// PolygonEditing.cs — Мели, суша (21.3а), фарватеры и причалы (21.3б) в редакторе акватории
//
// Фигура акватории — вид + номер в своём списке WaterArea:
//   мель и суша — многоугольник (замыкание неявное, ≥ 3 вершин); фарватер — ось-ломаная (≥ 2 точек)
//   и ширина; причал — кромка A→B (вода справа), ровно 2 точки.
//   • Points / SetPoints — точки фигуры копией (у причала — [a, b]): окно правит все виды одним кодом;
//     Vertices — сам список вершин (мель, суша, фарватер), у причала — null.
//   • Add — новая фигура: «Мель-1» (min(2; 0,5·глубина) м), «Суша-1» (10 м), «Фарватер-1» (2 кбт),
//     «Причал-1» (кромка не короче MinBerthM); Remove; Name / SetName / Find (по имени после отмены).
//   • HitShape (точка мира) — мели над сушей; HitShapeAt (пиксель окна) — сначала кромки причалов
//     и оси фарватеров (линии, ≤ LineHitPx), затем многоугольники; позже поставленная — сверху.
//   • NearestVertex / NearestMidpoint (замкнутый контур или ломаная) — ручки в пикселях окна карты;
//     InsertVertex, RemoveVertex (не меньше MinPoints), Translate — сдвиг от исходных точек.
//   • AreaM2, Perimeter, PolylineLength, SelfIntersects, OutsideBounds; причал — ApplyShip
//     (кранцы и кнехты по BerthArea.ForShip, кромка на месте), Flip, WaterSideOnLand.
// Снимок для отмены пишет вызывающий (AreaDocument.Commit).
// ============================================================
using System;
using System.Collections.Generic;

public enum AreaShapeKind { None, Shoal, Land, Fairway, Berth }

/// <summary>Фигура акватории: вид и номер в своём списке.</summary>
public struct AreaShape : IEquatable<AreaShape>
{
    public AreaShapeKind Kind;
    public int Index;

    public AreaShape(AreaShapeKind kind, int index) { Kind = kind; Index = index; }
    public static readonly AreaShape None = new AreaShape(AreaShapeKind.None, -1);
    public bool IsNone => Kind == AreaShapeKind.None;
    public bool Equals(AreaShape o) => Kind == o.Kind && Index == o.Index;
    public override bool Equals(object o) => o is AreaShape s && Equals(s);
    public override int GetHashCode() => ((int)Kind * 397) ^ Index;
}

public static class PolygonEditing
{
    public const int MinVertices = 3;
    /// <summary>Причал короче — не создаётся (AreaChecks предупреждает о том же).</summary>
    public const double MinBerthM = 20.0;
    /// <summary>Попадание по оси фарватера / кромке причала, пикс.</summary>
    public const double LineHitPx = 7.0;
    /// <summary>Ширина нового фарватера, м (2 кбт).</summary>
    public const float DefaultFairwayWidthM = 370.4f;

    public static string Prefix(AreaShapeKind k) => Title(k);

    public static string Title(AreaShapeKind k)
    {
        switch (k)
        {
            case AreaShapeKind.Land: return "Суша";
            case AreaShapeKind.Fairway: return "Фарватер";
            case AreaShapeKind.Berth: return "Причал";
            default: return "Мель";
        }
    }

    /// <summary>Многоугольник (мель, суша) — замкнут; фарватер и причал — нет.</summary>
    public static bool Closed(AreaShapeKind k) => k == AreaShapeKind.Shoal || k == AreaShapeKind.Land;

    /// <summary>Наименьшее число точек фигуры.</summary>
    public static int MinPoints(AreaShapeKind k) => Closed(k) ? MinVertices : 2;

    private static System.Collections.IList List(WaterArea a, AreaShapeKind k)
    {
        if (a == null) return null;
        switch (k)
        {
            case AreaShapeKind.Shoal: return a.shoals;
            case AreaShapeKind.Land: return a.land;
            case AreaShapeKind.Fairway: return a.fairways;
            case AreaShapeKind.Berth: return a.berths;
            default: return null;
        }
    }

    public static bool Valid(WaterArea a, AreaShape s)
    {
        System.Collections.IList l = List(a, s.Kind);
        return l != null && s.Index >= 0 && s.Index < l.Count && l[s.Index] != null;
    }

    /// <summary>Сам список вершин (правится на месте); у причала — null.</summary>
    public static List<WorldPosition> Vertices(WaterArea a, AreaShape s)
    {
        if (!Valid(a, s)) return null;
        switch (s.Kind)
        {
            case AreaShapeKind.Shoal: return a.shoals[s.Index].vertices;
            case AreaShapeKind.Land: return a.land[s.Index].vertices;
            case AreaShapeKind.Fairway: return a.fairways[s.Index].axis;
            default: return null;
        }
    }

    /// <summary>Точки фигуры копией (причал — [a, b]); null — нет фигуры.</summary>
    public static List<WorldPosition> Points(WaterArea a, AreaShape s)
    {
        if (!Valid(a, s)) return null;
        if (s.Kind == AreaShapeKind.Berth) return new List<WorldPosition> { a.berths[s.Index].a, a.berths[s.Index].b };
        return new List<WorldPosition>(Vertices(a, s));
    }

    /// <summary>Записать точки фигуры (причал — первые две).</summary>
    public static void SetPoints(WaterArea a, AreaShape s, IList<WorldPosition> pts)
    {
        if (!Valid(a, s) || pts == null) return;
        if (s.Kind == AreaShapeKind.Berth)
        {
            if (pts.Count < 2) return;
            a.berths[s.Index].a = pts[0];
            a.berths[s.Index].b = pts[1];
            return;
        }
        List<WorldPosition> v = Vertices(a, s);
        v.Clear();
        v.AddRange(pts);
    }

    public static string Name(WaterArea a, AreaShape s)
    {
        if (!Valid(a, s)) return null;
        switch (s.Kind)
        {
            case AreaShapeKind.Shoal: return a.shoals[s.Index].name;
            case AreaShapeKind.Land: return a.land[s.Index].name;
            case AreaShapeKind.Fairway: return a.fairways[s.Index].name;
            default: return a.berths[s.Index].name;
        }
    }

    public static void SetName(WaterArea a, AreaShape s, string name)
    {
        if (!Valid(a, s)) return;
        switch (s.Kind)
        {
            case AreaShapeKind.Shoal: a.shoals[s.Index].name = name; break;
            case AreaShapeKind.Land: a.land[s.Index].name = name; break;
            case AreaShapeKind.Fairway: a.fairways[s.Index].name = name; break;
            default: a.berths[s.Index].name = name; break;
        }
    }

    private static IEnumerable<string> Names(WaterArea a, AreaShapeKind k)
    {
        System.Collections.IList l = List(a, k);
        if (l == null) yield break;
        for (int i = 0; i < l.Count; i++)
        {
            string n = Name(a, new AreaShape(k, i));
            if (n != null) yield return n;
        }
    }

    /// <summary>Найти фигуру по виду и имени (после отмены номер мог измениться).</summary>
    public static AreaShape Find(WaterArea a, AreaShapeKind kind, string name)
    {
        System.Collections.IList l = List(a, kind);
        if (l == null || name == null) return AreaShape.None;
        for (int i = 0; i < l.Count; i++)
            if (Name(a, new AreaShape(kind, i)) == name) return new AreaShape(kind, i);
        return AreaShape.None;
    }

    /// <summary>«Мель-1», «Фарватер-2»… — первый свободный номер среди фигур этого вида.</summary>
    public static string NextName(WaterArea a, AreaShapeKind k)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string n in Names(a, k)) used.Add(n.Trim());
        string p = Prefix(k);
        for (int n = 1; ; n++)
            if (!used.Contains($"{p}-{n}")) return $"{p}-{n}";
    }

    /// <summary>Новая фигура из точек (копия); None — точек мало или причал короче MinBerthM.</summary>
    public static AreaShape Add(WaterArea a, AreaShapeKind k, IList<WorldPosition> pts)
    {
        if (a == null || pts == null || pts.Count < MinPoints(k)) return AreaShape.None;
        var v = new List<WorldPosition>(pts);
        switch (k)
        {
            case AreaShapeKind.Shoal:
                a.shoals.Add(new ShoalArea { name = NextName(a, k), depth = Math.Min(2f, Math.Max(0.1f, 0.5f * a.defaultDepth)), vertices = v });
                return new AreaShape(k, a.shoals.Count - 1);
            case AreaShapeKind.Land:
                a.land.Add(new LandArea { name = NextName(a, k), vertices = v });
                return new AreaShape(k, a.land.Count - 1);
            case AreaShapeKind.Fairway:
                a.fairways.Add(new FairwayLine { name = NextName(a, k), width = DefaultFairwayWidthM, axis = v });
                return new AreaShape(k, a.fairways.Count - 1);
            case AreaShapeKind.Berth:
                if ((v[1] - v[0]).Magnitude < MinBerthM) return AreaShape.None;
                a.berths.Add(new BerthArea { name = NextName(a, k), a = v[0], b = v[1] });
                return new AreaShape(k, a.berths.Count - 1);
            default:
                return AreaShape.None;
        }
    }

    public static bool Remove(WaterArea a, AreaShape s)
    {
        if (!Valid(a, s)) return false;
        List(a, s.Kind).RemoveAt(s.Index);
        return true;
    }

    /// <summary>Многоугольник под точкой: сначала мели (рисуются поверх суши), затем суша; позже добавленная — сверху.</summary>
    public static AreaShape HitShape(WaterArea a, WorldPosition p)
    {
        if (a == null) return AreaShape.None;
        for (int i = a.shoals.Count - 1; i >= 0; i--)
            if (a.shoals[i] != null && a.shoals[i].vertices.Count >= MinVertices && Polygon2D.Contains(a.shoals[i].vertices, p))
                return new AreaShape(AreaShapeKind.Shoal, i);
        for (int i = a.land.Count - 1; i >= 0; i--)
            if (a.land[i] != null && a.land[i].vertices.Count >= MinVertices && Polygon2D.Contains(a.land[i].vertices, p))
                return new AreaShape(AreaShapeKind.Land, i);
        return AreaShape.None;
    }

    /// <summary>Фигура под пикселем окна: кромки причалов, оси фарватеров (≤ maxPx), затем многоугольники.</summary>
    public static AreaShape HitShapeAt(WaterArea a, MapViewport v, double px, double py, double maxPx = LineHitPx)
    {
        if (a == null || v == null) return AreaShape.None;
        WorldPosition p = v.ToWorld(px, py);
        double tol = maxPx * v.MetersPerPixel;
        for (int i = a.berths.Count - 1; i >= 0; i--)
            if (a.berths[i] != null && Polygon2D.PointSegmentDistance(p, a.berths[i].a, a.berths[i].b) <= tol)
                return new AreaShape(AreaShapeKind.Berth, i);
        for (int i = a.fairways.Count - 1; i >= 0; i--)
        {
            List<WorldPosition> ax = a.fairways[i]?.axis;
            if (ax == null) continue;
            for (int j = 0; j + 1 < ax.Count; j++)
                if (Polygon2D.PointSegmentDistance(p, ax[j], ax[j + 1]) <= tol) return new AreaShape(AreaShapeKind.Fairway, i);
        }
        return HitShape(a, p);
    }

    // ─────────── Вершины ───────────
    /// <summary>Вершина у пикселя окна карты (не дальше maxPx); −1 — нет.</summary>
    public static int NearestVertex(IList<WorldPosition> poly, MapViewport v, double px, double py, double maxPx)
    {
        if (poly == null || v == null) return -1;
        int best = -1;
        double bestD = maxPx;
        for (int i = 0; i < poly.Count; i++)
        {
            v.ToPixel(poly[i], out double x, out double y);
            double d = Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
            if (d <= bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Середина стороны i → i+1 (у замкнутого контура последняя сторона — к вершине 0).</summary>
    public static WorldPosition Midpoint(IList<WorldPosition> poly, int i)
    {
        WorldPosition p = poly[i], q = poly[(i + 1) % poly.Count];
        return new WorldPosition(0.5 * (p.East + q.East), 0.5 * (p.North + q.North));
    }

    /// <summary>Число сторон: замкнутый контур — n, ломаная — n − 1.</summary>
    public static int EdgeCount(IList<WorldPosition> poly, bool closed) => poly == null ? 0 : closed ? poly.Count : Math.Max(0, poly.Count - 1);

    /// <summary>Ручка середины стороны у пикселя (не дальше maxPx): номер стороны i (между i и i+1), −1 — нет.</summary>
    public static int NearestMidpoint(IList<WorldPosition> poly, MapViewport v, double px, double py, double maxPx, bool closed = true)
    {
        if (poly == null || v == null || poly.Count < 2) return -1;
        int best = -1;
        double bestD = maxPx;
        for (int i = 0; i < EdgeCount(poly, closed); i++)
        {
            v.ToPixel(Midpoint(poly, i), out double x, out double y);
            double d = Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
            if (d <= bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Новая вершина на стороне edge (после вершины edge); возвращает её номер.</summary>
    public static int InsertVertex(IList<WorldPosition> poly, int edge, WorldPosition p)
    {
        poly.Insert(edge + 1, p);
        return edge + 1;
    }

    /// <summary>Удалить вершину, если останется не меньше minCount (по умолчанию 3).</summary>
    public static bool RemoveVertex(IList<WorldPosition> poly, int i, int minCount = MinVertices)
    {
        if (poly == null || i < 0 || i >= poly.Count || poly.Count <= minCount) return false;
        poly.RemoveAt(i);
        return true;
    }

    /// <summary>Сдвиг всей фигуры: from — исходные вершины (снятые при нажатии), на (dE, dN), м.</summary>
    public static void Translate(IList<WorldPosition> poly, IList<WorldPosition> from, double dE, double dN)
    {
        for (int i = 0; i < poly.Count && i < from.Count; i++)
            poly[i] = new WorldPosition(from[i].East + dE, from[i].North + dN);
    }

    // ─────────── Свойства, проверка ───────────
    public static double AreaM2(IList<WorldPosition> poly) => poly == null || poly.Count < 3 ? 0.0 : Math.Abs(Polygon2D.SignedArea(poly));

    public static double Perimeter(IList<WorldPosition> poly)
    {
        if (poly == null || poly.Count < 2) return 0.0;
        return PolylineLength(poly) + (poly[0] - poly[poly.Count - 1]).Magnitude;
    }

    /// <summary>Длина ломаной без замыкания, м.</summary>
    public static double PolylineLength(IList<WorldPosition> pts)
    {
        if (pts == null) return 0.0;
        double s = 0.0;
        for (int i = 0; i + 1 < pts.Count; i++) s += (pts[i + 1] - pts[i]).Magnitude;
        return s;
    }

    /// <summary>Несмежные стороны замкнутого контура пересекаются (или вершины совпадают).</summary>
    public static bool SelfIntersects(IList<WorldPosition> poly)
    {
        int n = poly?.Count ?? 0;
        if (n < 4) return false;
        for (int i = 0; i < n; i++)
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue;   // смежные через замыкание
                if (Polygon2D.SegmentsIntersect(poly[i], poly[(i + 1) % n], poly[j], poly[(j + 1) % n])) return true;
            }
        return false;
    }

    /// <summary>Рамка фигуры не задевает район.</summary>
    public static bool OutsideBounds(WaterArea a, IList<WorldPosition> poly)
    {
        if (poly == null || poly.Count == 0) return false;
        Polygon2D.Bounds(poly, out WorldPosition min, out WorldPosition max);
        return max.East < a.boundsMin.East || min.East > a.boundsMax.East || max.North < a.boundsMin.North || min.North > a.boundsMax.North;
    }

    // ─────────── Причал ───────────
    /// <summary>Вода с другой стороны кромки: A и B меняются местами.</summary>
    public static void Flip(BerthArea b)
    {
        WorldPosition t = b.a;
        b.a = b.b;
        b.b = t;
    }

    /// <summary>
    /// Кранцы и кнехты под судно (BerthArea.ForShip: кранцы по энергии навала PIANC, кнехты через 0,2·L);
    /// кромка, имя и высота остаются; глубина у причала — не меньше осадки + 3 м.
    /// </summary>
    public static void ApplyShip(BerthArea b, double length, double beam, double draft, double blockCoefficient)
    {
        BerthArea f = BerthArea.ForShip(length, beam, draft, blockCoefficient);
        b.fenderOuterDiameterM = f.fenderOuterDiameterM;
        b.fenderRatedDeflectionM = f.fenderRatedDeflectionM;
        b.fenderRatedReactionKn = f.fenderRatedReactionKn;
        b.fenderLengthM = f.fenderLengthM;
        b.fenderSpacingM = f.fenderSpacingM;
        b.fenderEndMarginM = f.fenderEndMarginM;
        b.bollardSpacingM = f.bollardSpacingM;
        b.bollardEndMarginM = f.bollardEndMarginM;
        b.bollardSetbackM = f.bollardSetbackM;
        b.depthM = Math.Max(b.depthM, f.depthM);
    }

    /// <summary>Точка у середины кромки с водной стороны (на WaterSideM м) — на суше: кромка, видимо, перевёрнута.</summary>
    public static bool WaterSideOnLand(WaterArea a, BerthArea b)
    {
        if (a == null || b == null || b.Length < 1.0) return false;
        return a.DepthAt(b.ToWorld(0.5 * b.Length, WaterSideM)) <= 0f;
    }

    public const double WaterSideM = 10.0;
}
