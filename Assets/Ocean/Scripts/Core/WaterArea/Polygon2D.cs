// ============================================================
// Polygon2D.cs — Плоская геометрия для акватории (B1)
//
// Координаты — WorldPosition (East, North), double. Многоугольники простые
// (без самопересечений), могут быть невыпуклыми; замыкание неявное
// (последняя вершина соединяется с первой).
//
//   • Contains — точка в многоугольнике (чётность пересечений луча);
//   • SegmentsIntersect — пересечение отрезков по знакам ориентаций
//     o(a,b,c) = (b − a) × (c − a), включая касание и коллинеарность;
//   • PolygonDistance — расстояние между многоугольниками: 0, если рёбра
//     пересекаются или один лежит внутри другого, иначе минимум расстояний
//     между рёбрами (для непересекающихся отрезков оно достигается на конце
//     одного из них);
//   • Triangulate — отсечение «ушей» (ear clipping), O(n²);
//   • Offset — смещение контура по нормалям со срезанием острых углов (откос берега).
// ============================================================
using System;
using System.Collections.Generic;

public static class Polygon2D
{
    private const double Eps = 1e-9;

    /// <summary>Знаковая площадь, м² (> 0 — обход против часовой стрелки при «север вверх»).</summary>
    public static double SignedArea(IList<WorldPosition> poly)
    {
        if (poly == null || poly.Count < 3) return 0.0;
        double s = 0.0;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            s += poly[j].East * poly[i].North - poly[i].East * poly[j].North;
        return 0.5 * s;
    }

    /// <summary>Центр тяжести площади (для вырожденных — среднее вершин).</summary>
    public static WorldPosition Centroid(IList<WorldPosition> poly)
    {
        if (poly == null || poly.Count == 0) return default;
        double a = SignedArea(poly);
        if (Math.Abs(a) < 1e-6)
        {
            double e = 0, n = 0;
            foreach (WorldPosition p in poly) { e += p.East; n += p.North; }
            return new WorldPosition(e / poly.Count, n / poly.Count);
        }
        double ce = 0, cn = 0;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            double cross = poly[j].East * poly[i].North - poly[i].East * poly[j].North;
            ce += (poly[j].East + poly[i].East) * cross;
            cn += (poly[j].North + poly[i].North) * cross;
        }
        return new WorldPosition(ce / (6.0 * a), cn / (6.0 * a));
    }

    /// <summary>Точка внутри многоугольника (на границе — не определено, для посадки неважно).</summary>
    public static bool Contains(IList<WorldPosition> poly, WorldPosition p)
    {
        if (poly == null || poly.Count < 3) return false;
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            WorldPosition a = poly[i], b = poly[j];
            if ((a.North > p.North) != (b.North > p.North))
            {
                double e = a.East + (p.North - a.North) * (b.East - a.East) / (b.North - a.North);
                if (p.East < e) inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>Ориентация тройки: > 0 — против часовой, < 0 — по часовой, 0 — на одной прямой.</summary>
    public static double Orientation(WorldPosition a, WorldPosition b, WorldPosition c) =>
        (b.East - a.East) * (c.North - a.North) - (b.North - a.North) * (c.East - a.East);

    private static bool OnSegment(WorldPosition a, WorldPosition b, WorldPosition p) =>
        p.East <= Math.Max(a.East, b.East) + Eps && p.East >= Math.Min(a.East, b.East) - Eps &&
        p.North <= Math.Max(a.North, b.North) + Eps && p.North >= Math.Min(a.North, b.North) - Eps;

    /// <summary>Пересекаются ли отрезки ab и cd (включая касание).</summary>
    public static bool SegmentsIntersect(WorldPosition a, WorldPosition b, WorldPosition c, WorldPosition d)
    {
        double o1 = Orientation(a, b, c), o2 = Orientation(a, b, d);
        double o3 = Orientation(c, d, a), o4 = Orientation(c, d, b);
        if (((o1 > Eps && o2 < -Eps) || (o1 < -Eps && o2 > Eps)) &&
            ((o3 > Eps && o4 < -Eps) || (o3 < -Eps && o4 > Eps)))
            return true;
        if (Math.Abs(o1) <= Eps && OnSegment(a, b, c)) return true;
        if (Math.Abs(o2) <= Eps && OnSegment(a, b, d)) return true;
        if (Math.Abs(o3) <= Eps && OnSegment(c, d, a)) return true;
        if (Math.Abs(o4) <= Eps && OnSegment(c, d, b)) return true;
        return false;
    }

    /// <summary>Расстояние от точки до отрезка, м.</summary>
    public static double PointSegmentDistance(WorldPosition p, WorldPosition a, WorldPosition b)
    {
        double ex = b.East - a.East, ny = b.North - a.North;
        double len2 = ex * ex + ny * ny;
        double t = len2 > 0.0 ? ((p.East - a.East) * ex + (p.North - a.North) * ny) / len2 : 0.0;
        t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
        double de = a.East + t * ex - p.East, dn = a.North + t * ny - p.North;
        return Math.Sqrt(de * de + dn * dn);
    }

    /// <summary>Расстояние между отрезками, м (0 — пересекаются).</summary>
    public static double SegmentDistance(WorldPosition a, WorldPosition b, WorldPosition c, WorldPosition d)
    {
        if (SegmentsIntersect(a, b, c, d)) return 0.0;
        return Math.Min(Math.Min(PointSegmentDistance(a, c, d), PointSegmentDistance(b, c, d)),
                        Math.Min(PointSegmentDistance(c, a, b), PointSegmentDistance(d, a, b)));
    }

    /// <summary>Охватывающий прямоугольник.</summary>
    public static void Bounds(IList<WorldPosition> poly, out WorldPosition min, out WorldPosition max)
    {
        min = new WorldPosition(double.MaxValue, double.MaxValue);
        max = new WorldPosition(double.MinValue, double.MinValue);
        if (poly == null) return;
        foreach (WorldPosition p in poly)
        {
            if (p.East < min.East) min.East = p.East;
            if (p.North < min.North) min.North = p.North;
            if (p.East > max.East) max.East = p.East;
            if (p.North > max.North) max.North = p.North;
        }
    }

    /// <summary>Расстояние между охватывающими прямоугольниками (оценка снизу), м.</summary>
    public static double BoundsDistance(WorldPosition minA, WorldPosition maxA, WorldPosition minB, WorldPosition maxB)
    {
        double de = Math.Max(0.0, Math.Max(minA.East - maxB.East, minB.East - maxA.East));
        double dn = Math.Max(0.0, Math.Max(minA.North - maxB.North, minB.North - maxA.North));
        return Math.Sqrt(de * de + dn * dn);
    }

    /// <summary>
    /// Расстояние между многоугольниками, м: 0 — пересекаются (рёбра пересекаются
    /// или один целиком внутри другого).
    /// </summary>
    public static double PolygonDistance(IList<WorldPosition> a, IList<WorldPosition> b)
    {
        if (a == null || b == null || a.Count < 2 || b.Count < 2) return double.PositiveInfinity;
        double best = double.PositiveInfinity;
        for (int i = 0, ip = a.Count - 1; i < a.Count; ip = i++)
        {
            for (int j = 0, jp = b.Count - 1; j < b.Count; jp = j++)
            {
                double d = SegmentDistance(a[ip], a[i], b[jp], b[j]);
                if (d < best)
                {
                    best = d;
                    if (best <= 0.0) return 0.0;
                }
            }
        }
        // Рёбра не пересекаются: пересечение возможно, только если один внутри другого
        if (Contains(b, a[0]) || Contains(a, b[0])) return 0.0;
        return best;
    }

    /// <summary>
    /// Триангуляция простого многоугольника отсечением ушей.
    /// Возвращает индексы вершин, тройки — против часовой стрелки при «север вверх».
    /// </summary>
    public static List<int> Triangulate(IList<WorldPosition> poly)
    {
        var tris = new List<int>();
        int n = poly != null ? poly.Count : 0;
        if (n < 3) return tris;

        var idx = new List<int>(n);
        bool ccw = SignedArea(poly) > 0.0;
        for (int i = 0; i < n; i++) idx.Add(ccw ? i : n - 1 - i);

        int guard = 0;
        while (idx.Count > 3 && guard++ < n * n)
        {
            bool clipped = false;
            for (int k = 0; k < idx.Count; k++)
            {
                int i0 = idx[(k + idx.Count - 1) % idx.Count], i1 = idx[k], i2 = idx[(k + 1) % idx.Count];
                WorldPosition a = poly[i0], b = poly[i1], c = poly[i2];
                if (Orientation(a, b, c) <= Eps) continue;   // вогнутая вершина или вырожденная
                bool empty = true;
                for (int m = 0; m < idx.Count; m++)
                {
                    int im = idx[m];
                    if (im == i0 || im == i1 || im == i2) continue;
                    if (PointInTriangle(poly[im], a, b, c)) { empty = false; break; }
                }
                if (!empty) continue;
                tris.Add(i0); tris.Add(i1); tris.Add(i2);
                idx.RemoveAt(k);
                clipped = true;
                break;
            }
            if (!clipped) break;   // многоугольник с самопересечением — остаток не триангулируется
        }
        if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
        return tris;
    }

    private static bool PointInTriangle(WorldPosition p, WorldPosition a, WorldPosition b, WorldPosition c)
    {
        double d1 = Orientation(a, b, p), d2 = Orientation(b, c, p), d3 = Orientation(c, a, p);
        bool hasNeg = d1 < -Eps || d2 < -Eps || d3 < -Eps;
        bool hasPos = d1 > Eps || d2 > Eps || d3 > Eps;
        return !(hasNeg && hasPos);
    }

    /// <summary>
    /// Смещение контура наружу на distance (м) по биссектрисам углов; длина сдвига
    /// вершины ограничена 3·distance (острые углы не «выстреливают»).
    /// </summary>
    public static List<WorldPosition> Offset(IList<WorldPosition> poly, double distance)
    {
        int n = poly.Count;
        var result = new List<WorldPosition>(n);
        double sign = SignedArea(poly) > 0.0 ? 1.0 : -1.0;   // наружу — вправо при обходе против часовой
        for (int i = 0; i < n; i++)
        {
            WorldPosition prev = poly[(i + n - 1) % n], cur = poly[i], next = poly[(i + 1) % n];
            Normal(prev, cur, sign, out double n1e, out double n1n);
            Normal(cur, next, sign, out double n2e, out double n2n);
            double be = n1e + n2e, bn = n1n + n2n;
            double bl = Math.Sqrt(be * be + bn * bn);
            if (bl < 1e-9) { be = n1e; bn = n1n; bl = 1.0; }
            be /= bl; bn /= bl;
            double cosHalf = be * n1e + bn * n1n;
            double shift = distance / Math.Max(cosHalf, 1.0 / 3.0);
            result.Add(new WorldPosition(cur.East + be * shift, cur.North + bn * shift));
        }
        return result;
    }

    // Внешняя единичная нормаль ребра ab
    private static void Normal(WorldPosition a, WorldPosition b, double sign, out double ne, out double nn)
    {
        double de = b.East - a.East, dn = b.North - a.North;
        double l = Math.Sqrt(de * de + dn * dn);
        if (l < 1e-9) { ne = 0; nn = 0; return; }
        ne = sign * dn / l;
        nn = -sign * de / l;
    }
}
