// ============================================================
// PolygonEditing.cs — Мели и суша в редакторе акватории (запрос 21.3а)
//
// Фигура акватории — многоугольник WaterArea.shoals / WaterArea.land (замыкание неявное).
//   • Shape — какая фигура (вид + номер в своём списке); Vertices / Name — доступ без switch в окне.
//   • Add — новая мель («Мель-1», 2 м) или суша («Суша-1», 10 м) из нарисованных вершин (≥ 3).
//   • HitShape — фигура под точкой: мели над сушей (так рисует карта), в списке — позже поставленная сверху.
//   • NearestVertex / NearestMidpoint — ручки вершин и середин сторон в пикселях окна карты;
//     InsertVertex — новая вершина на стороне, RemoveVertex — не меньше 3, Translate — сдвиг фигуры.
//   • SelfIntersects — несмежные стороны пересекаются (карта и тренажёр такую фигуру закрашивают
//     не полностью, глубина по правилу чётности — «дыры»); AreaM2 — площадь, м².
// Снимок для отмены пишет вызывающий (AreaDocument.Commit).
// ============================================================
using System;
using System.Collections.Generic;

public enum AreaShapeKind { None, Shoal, Land }

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

    public static string Prefix(AreaShapeKind k) => k == AreaShapeKind.Land ? "Суша" : "Мель";
    public static string Title(AreaShapeKind k) => k == AreaShapeKind.Land ? "Суша" : "Мель";

    public static bool Valid(WaterArea a, AreaShape s)
    {
        if (a == null || s.Index < 0) return false;
        switch (s.Kind)
        {
            case AreaShapeKind.Shoal: return a.shoals != null && s.Index < a.shoals.Count && a.shoals[s.Index] != null;
            case AreaShapeKind.Land: return a.land != null && s.Index < a.land.Count && a.land[s.Index] != null;
            default: return false;
        }
    }

    public static List<WorldPosition> Vertices(WaterArea a, AreaShape s)
    {
        if (!Valid(a, s)) return null;
        return s.Kind == AreaShapeKind.Shoal ? a.shoals[s.Index].vertices : a.land[s.Index].vertices;
    }

    public static string Name(WaterArea a, AreaShape s)
    {
        if (!Valid(a, s)) return null;
        return s.Kind == AreaShapeKind.Shoal ? a.shoals[s.Index].name : a.land[s.Index].name;
    }

    public static void SetName(WaterArea a, AreaShape s, string name)
    {
        if (!Valid(a, s)) return;
        if (s.Kind == AreaShapeKind.Shoal) a.shoals[s.Index].name = name;
        else a.land[s.Index].name = name;
    }

    /// <summary>Найти фигуру по виду и имени (после отмены номер мог измениться).</summary>
    public static AreaShape Find(WaterArea a, AreaShapeKind kind, string name)
    {
        if (a == null || name == null) return AreaShape.None;
        int i = kind == AreaShapeKind.Shoal ? a.shoals.FindIndex(x => x != null && x.name == name)
              : kind == AreaShapeKind.Land ? a.land.FindIndex(x => x != null && x.name == name) : -1;
        return i >= 0 ? new AreaShape(kind, i) : AreaShape.None;
    }

    /// <summary>«Мель-1», «Мель-2»… — первый свободный номер среди фигур этого вида.</summary>
    public static string NextName(WaterArea a, AreaShapeKind k)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (k == AreaShapeKind.Shoal) { foreach (ShoalArea s in a.shoals) if (s?.name != null) used.Add(s.name.Trim()); }
        else foreach (LandArea l in a.land) if (l?.name != null) used.Add(l.name.Trim());
        string p = Prefix(k);
        for (int n = 1; ; n++)
            if (!used.Contains($"{p}-{n}")) return $"{p}-{n}";
    }

    /// <summary>Новая фигура из вершин (копия списка); None — меньше 3 вершин.</summary>
    public static AreaShape Add(WaterArea a, AreaShapeKind k, IList<WorldPosition> pts)
    {
        if (a == null || pts == null || pts.Count < MinVertices) return AreaShape.None;
        var v = new List<WorldPosition>(pts);
        if (k == AreaShapeKind.Shoal)
        {
            a.shoals.Add(new ShoalArea { name = NextName(a, k), depth = Math.Min(2f, Math.Max(0.1f, 0.5f * a.defaultDepth)), vertices = v });
            return new AreaShape(k, a.shoals.Count - 1);
        }
        if (k == AreaShapeKind.Land)
        {
            a.land.Add(new LandArea { name = NextName(a, k), vertices = v });
            return new AreaShape(k, a.land.Count - 1);
        }
        return AreaShape.None;
    }

    public static bool Remove(WaterArea a, AreaShape s)
    {
        if (!Valid(a, s)) return false;
        if (s.Kind == AreaShapeKind.Shoal) a.shoals.RemoveAt(s.Index);
        else a.land.RemoveAt(s.Index);
        return true;
    }

    /// <summary>Фигура под точкой: сначала мели (рисуются поверх суши), затем суша; позже добавленная — сверху.</summary>
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

    /// <summary>Середина стороны i → i+1 (замкнутый контур).</summary>
    public static WorldPosition Midpoint(IList<WorldPosition> poly, int i)
    {
        WorldPosition p = poly[i], q = poly[(i + 1) % poly.Count];
        return new WorldPosition(0.5 * (p.East + q.East), 0.5 * (p.North + q.North));
    }

    /// <summary>Ручка середины стороны у пикселя (не дальше maxPx): номер стороны i (между i и i+1), −1 — нет.</summary>
    public static int NearestMidpoint(IList<WorldPosition> poly, MapViewport v, double px, double py, double maxPx)
    {
        if (poly == null || v == null || poly.Count < 2) return -1;
        int best = -1;
        double bestD = maxPx;
        for (int i = 0; i < poly.Count; i++)
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

    /// <summary>Удалить вершину, если останется не меньше 3.</summary>
    public static bool RemoveVertex(IList<WorldPosition> poly, int i)
    {
        if (poly == null || i < 0 || i >= poly.Count || poly.Count <= MinVertices) return false;
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
        double s = 0.0;
        for (int i = 0; i < poly.Count; i++) s += (poly[(i + 1) % poly.Count] - poly[i]).Magnitude;
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
}
