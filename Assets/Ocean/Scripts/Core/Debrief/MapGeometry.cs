// ============================================================
// MapGeometry.cs — Геометрия схемы траектории (C3)
//
// Общая для отрисовки схемы в SVG (report.html) и на VR-панели:
//   • ClipSegment — отсечение отрезка прямоугольником (Лианг — Барски);
//   • ClipPolygon — отсечение многоугольника (Сазерленд — Ходжмен; окно выпуклое);
//   • Triangulate — триангуляция простого многоугольника «отрезанием ушей»
//     (мели и суша на VR-панели рисуются треугольниками);
//   • Decimate — прореживание ломаной: точка ближе eps к последней
//     оставленной пропускается (траектория 5 Гц → не больше точек, чем пикселей).
// Координаты — восток/север, м (как WorldPosition).
// ============================================================
using System;
using System.Collections.Generic;

public struct MapRect
{
    public double MinE, MinN, MaxE, MaxN;

    public MapRect(double minE, double minN, double maxE, double maxN) { MinE = minE; MinN = minN; MaxE = maxE; MaxN = maxN; }

    public static MapRect Empty => new MapRect(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
    public bool IsEmpty => MinE > MaxE || MinN > MaxN;
    public double Width => MaxE - MinE;
    public double Height => MaxN - MinN;
    public WorldPosition Center => new WorldPosition(0.5 * (MinE + MaxE), 0.5 * (MinN + MaxN));

    public void Include(WorldPosition p)
    {
        if (double.IsNaN(p.East) || double.IsNaN(p.North)) return;
        MinE = Math.Min(MinE, p.East); MaxE = Math.Max(MaxE, p.East);
        MinN = Math.Min(MinN, p.North); MaxN = Math.Max(MaxN, p.North);
    }

    public bool Contains(WorldPosition p) => p.East >= MinE && p.East <= MaxE && p.North >= MinN && p.North <= MaxN;

    /// <summary>Расширить: поля (доля размера) и не меньше minSize по каждой оси.</summary>
    public MapRect Padded(double fraction, double minSize)
    {
        if (IsEmpty) return new MapRect(-minSize / 2, -minSize / 2, minSize / 2, minSize / 2);
        WorldPosition c = Center;
        double w = Math.Max(Width * (1 + 2 * fraction), minSize), h = Math.Max(Height * (1 + 2 * fraction), minSize);
        return new MapRect(c.East - w / 2, c.North - h / 2, c.East + w / 2, c.North + h / 2);
    }

    /// <summary>Расширить меньшую сторону до соотношения ширина/высота aspect (одинаковый масштаб по осям).</summary>
    public MapRect WithAspect(double aspect)
    {
        WorldPosition c = Center;
        double w = Width, h = Height;
        if (w / h < aspect) w = h * aspect; else h = w / aspect;
        return new MapRect(c.East - w / 2, c.North - h / 2, c.East + w / 2, c.North + h / 2);
    }
}

public static class MapGeometry
{
    /// <summary>Отсечение отрезка a–b прямоугольником r (false — отрезок вне окна).</summary>
    public static bool ClipSegment(MapRect r, ref WorldPosition a, ref WorldPosition b)
    {
        double x0 = a.East, y0 = a.North, dx = b.East - a.East, dy = b.North - a.North;
        double t0 = 0, t1 = 1;
        if (!Clip(-dx, x0 - r.MinE, ref t0, ref t1) || !Clip(dx, r.MaxE - x0, ref t0, ref t1) ||
            !Clip(-dy, y0 - r.MinN, ref t0, ref t1) || !Clip(dy, r.MaxN - y0, ref t0, ref t1)) return false;
        b = new WorldPosition(x0 + t1 * dx, y0 + t1 * dy);
        a = new WorldPosition(x0 + t0 * dx, y0 + t0 * dy);
        return true;
    }

    private static bool Clip(double p, double q, ref double t0, ref double t1)
    {
        if (Math.Abs(p) < 1e-12) return q >= 0;
        double t = q / p;
        if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
        else { if (t < t0) return false; if (t < t1) t1 = t; }
        return true;
    }

    /// <summary>Отсечение многоугольника прямоугольником (Сазерленд — Ходжмен).</summary>
    public static List<WorldPosition> ClipPolygon(IList<WorldPosition> poly, MapRect r)
    {
        var res = new List<WorldPosition>(poly);
        res = ClipEdge(res, p => p.East >= r.MinE, (a, b) => AtE(a, b, r.MinE));
        res = ClipEdge(res, p => p.East <= r.MaxE, (a, b) => AtE(a, b, r.MaxE));
        res = ClipEdge(res, p => p.North >= r.MinN, (a, b) => AtN(a, b, r.MinN));
        res = ClipEdge(res, p => p.North <= r.MaxN, (a, b) => AtN(a, b, r.MaxN));
        return res;
    }

    private static List<WorldPosition> ClipEdge(List<WorldPosition> pts, Func<WorldPosition, bool> inside, Func<WorldPosition, WorldPosition, WorldPosition> cross)
    {
        var res = new List<WorldPosition>();
        if (pts.Count == 0) return res;
        WorldPosition prev = pts[pts.Count - 1];
        bool prevIn = inside(prev);
        foreach (WorldPosition p in pts)
        {
            bool pin = inside(p);
            if (pin != prevIn) res.Add(cross(prev, p));
            if (pin) res.Add(p);
            prev = p; prevIn = pin;
        }
        return res;
    }

    private static WorldPosition AtE(WorldPosition a, WorldPosition b, double e)
    {
        double k = (e - a.East) / (b.East - a.East);
        return new WorldPosition(e, a.North + k * (b.North - a.North));
    }

    private static WorldPosition AtN(WorldPosition a, WorldPosition b, double n)
    {
        double k = (n - a.North) / (b.North - a.North);
        return new WorldPosition(a.East + k * (b.East - a.East), n);
    }

    /// <summary>Триангуляция простого многоугольника («уши»): тройки индексов вершин.</summary>
    /// <summary>
    /// Штрихи на отрезке длины len (штрих dash, пробел gap): интервалы [s0; s1] от начала отрезка.
    /// phase — положение в периоде на входе (переносится на следующий отрезок ломаной); хранится
    /// в пределах периода, шаг не меньше 10⁻⁶ периода. Прежний цикл копил фазу без приведения,
    /// и при phase % period = dash − 2·10⁻¹⁴ шаг переставал сдвигать позицию — бесконечный цикл,
    /// Unity зависал при открытии вкладки «Схема» (C4-01, 12.10.2026).
    /// </summary>
    public static void DashRuns(double len, double dash, double gap, ref double phase, List<(double s0, double s1)> runs)
    {
        runs.Clear();
        double period = dash + gap;
        if (!(len > 0) || !(dash > 0) || !(period > 0) || double.IsInfinity(len)) return;
        double minStep = 1e-6 * period;
        phase %= period;
        if (phase < 0) phase += period;
        double s = 0;
        while (s < len)
        {
            bool on = phase < dash;
            double run = Math.Max(minStep, on ? dash - phase : period - phase);
            run = Math.Min(run, len - s);
            if (on) runs.Add((s, s + run));
            s += run;
            phase += run;
            if (phase >= period - minStep) phase = 0;
            else if (on && phase >= dash - minStep) phase = dash;
        }
    }

    public static List<int> Triangulate(IList<WorldPosition> poly)
    {
        var tri = new List<int>();
        int n = poly.Count;
        if (n < 3) return tri;
        var idx = new List<int>(n);
        for (int i = 0; i < n; i++) idx.Add(i);
        if (Polygon2D.SignedArea(poly) < 0) idx.Reverse();   // против часовой стрелки
        int guard = 0;
        while (idx.Count > 3 && guard++ < 4 * n * n)
        {
            bool cut = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                WorldPosition a = poly[ia], b = poly[ib], c = poly[ic];
                if (Cross(a, b, c) <= 1e-9) continue;   // вогнутая вершина
                bool empty = true;
                foreach (int j in idx)
                {
                    if (j == ia || j == ib || j == ic) continue;
                    if (InTriangle(poly[j], a, b, c)) { empty = false; break; }
                }
                if (!empty) continue;
                tri.Add(ia); tri.Add(ib); tri.Add(ic);
                idx.RemoveAt(i);
                cut = true;
                break;
            }
            if (!cut) break;   // вырожденный многоугольник — остаток не рисуем
        }
        if (idx.Count == 3) { tri.Add(idx[0]); tri.Add(idx[1]); tri.Add(idx[2]); }
        return tri;
    }

    private static double Cross(WorldPosition a, WorldPosition b, WorldPosition c) =>
        (b.East - a.East) * (c.North - a.North) - (b.North - a.North) * (c.East - a.East);

    private static bool InTriangle(WorldPosition p, WorldPosition a, WorldPosition b, WorldPosition c) =>
        Cross(a, b, p) >= 0 && Cross(b, c, p) >= 0 && Cross(c, a, p) >= 0;

    /// <summary>Прореживание: точки ближе eps (м) к последней оставленной пропускаются; первая и последняя остаются.</summary>
    public static List<WorldPosition> Decimate(IList<WorldPosition> pts, double eps)
    {
        var res = new List<WorldPosition>();
        if (pts == null || pts.Count == 0) return res;
        res.Add(pts[0]);
        double e2 = eps * eps;
        for (int i = 1; i < pts.Count - 1; i++)
        {
            WorldPosition last = res[res.Count - 1];
            double de = pts[i].East - last.East, dn = pts[i].North - last.North;
            if (de * de + dn * dn >= e2) res.Add(pts[i]);
        }
        if (pts.Count > 1) res.Add(pts[pts.Count - 1]);
        return res;
    }

    /// <summary>Ломаная, смещённая вбок на offset (> 0 — вправо по ходу), — граница коридора участка.</summary>
    public static (WorldPosition a, WorldPosition b) Offset(WorldPosition a, WorldPosition b, double offset)
    {
        double de = b.East - a.East, dn = b.North - a.North;
        double len = Math.Sqrt(de * de + dn * dn);
        if (len < 1e-9) return (a, b);
        // вправо по ходу: (dn, −de)/len
        double oe = dn / len * offset, on = -de / len * offset;
        return (new WorldPosition(a.East + oe, a.North + on), new WorldPosition(b.East + oe, b.North + on));
    }
}
