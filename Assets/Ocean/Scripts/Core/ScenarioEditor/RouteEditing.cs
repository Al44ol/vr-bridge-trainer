// ============================================================
// RouteEditing.cs — Старт и маршрут сценария в редакторе (запрос 21.4а)
//
// Маршрут — список RouteWaypoint (ScenarioDefinition.route). XTE точки i — допуск бокового отклонения
// на участке (i−1 → i), как в RouteTracker; у первой точки не используется.
//   • Add — точка в конец (XTE — как у прежней последней, иначе 150 м); Insert — на участок, точка
//     получает XTE и «узкость» участка; Remove, Move; имена «WP1», «WP2»… (следующий свободный номер).
//   • Попадание мышью (пиксели MapViewport): точка ≤ 9 пикс. (поставленная позже — сверху), середина
//     участка ≤ 7 пикс. (вставка), ручка XTE — на кромке коридора справа от участка, посередине;
//     ручка видна, только если отстоит от оси не меньше 12 пикс. (иначе — приблизить карту).
//   • XteFrom — перпендикулярное расстояние от оси участка до курсора, шаг 10 м, не меньше шага.
//   • Старт: ручка курса — на heading на заданном расстоянии; курс от старта к курсору мыши (шаг 1° или 5°).
//   • Check — замечания редактора (сверх ScenarioDefinition.Validate): старт далеко от первой точки,
//     участок над глубинами меньше осадки + запас.
// ============================================================
using System;
using System.Collections.Generic;

public static class RouteEditing
{
    public const float DefaultXte = 150f;
    public const double XteStep = 10.0;
    public const double PointHitPx = 9.0, MidHitPx = 7.0, XteHitPx = 8.0, XteHandleMinPx = 12.0, StartHitPx = 14.0;
    /// <summary>Запас под килем для замечания «участок над мелью», м.</summary>
    public const double UnderKeelM = 1.0;

    // ─────────── Точки ───────────
    public static string NextName(IList<RouteWaypoint> r)
    {
        var used = new HashSet<string>();
        if (r != null) foreach (RouteWaypoint w in r) if (w != null && w.name != null) used.Add(w.name);
        for (int i = 1; ; i++) if (!used.Contains("WP" + i)) return "WP" + i;
    }

    public static bool Valid(IList<RouteWaypoint> r, int i) => r != null && i >= 0 && i < r.Count && r[i] != null;

    /// <summary>Точка в конец маршрута; номер новой точки.</summary>
    public static int Add(List<RouteWaypoint> r, WorldPosition p)
    {
        RouteWaypoint last = r.Count > 0 ? r[r.Count - 1] : null;
        float xte = last != null && last.xteLimit > 0f && r.Count > 1 ? last.xteLimit : DefaultXte;
        r.Add(new RouteWaypoint(NextName(r), p, xte, last != null && r.Count > 1 && last.narrowChannel));
        return r.Count - 1;
    }

    /// <summary>Точка на участок leg (между leg и leg+1); XTE и «узкость» — участка. Номер новой точки или −1.</summary>
    public static int Insert(List<RouteWaypoint> r, int leg, WorldPosition p)
    {
        if (!Valid(r, leg) || !Valid(r, leg + 1)) return -1;
        RouteWaypoint next = r[leg + 1];
        r.Insert(leg + 1, new RouteWaypoint(NextName(r), p, next.xteLimit, next.narrowChannel));
        return leg + 1;
    }

    public static bool Remove(List<RouteWaypoint> r, int i)
    {
        if (!Valid(r, i)) return false;
        r.RemoveAt(i);
        return true;
    }

    public static void Move(IList<RouteWaypoint> r, int i, WorldPosition p)
    {
        if (Valid(r, i)) r[i].position = p;
    }

    // ─────────── Участки ───────────
    public static int LegCount(IList<RouteWaypoint> r) => r == null ? 0 : Math.Max(0, r.Count - 1);

    public static WorldPosition LegMid(IList<RouteWaypoint> r, int leg) => (r[leg].position + r[leg + 1].position) * 0.5;

    public static double LegLength(IList<RouteWaypoint> r, int leg) => (r[leg + 1].position - r[leg].position).Magnitude;

    /// <summary>Курс участка, ° (0 = N, по часовой).</summary>
    public static double LegCourse(IList<RouteWaypoint> r, int leg) => Bearing(r[leg].position, r[leg + 1].position);

    public static double Bearing(WorldPosition from, WorldPosition to)
    {
        WorldPosition d = to - from;
        double c = Math.Atan2(d.East, d.North) * 180.0 / Math.PI;
        return c < 0 ? c + 360.0 : c;
    }

    // Единичная нормаль вправо от направления участка (восток, север)
    private static WorldPosition RightNormal(IList<RouteWaypoint> r, int leg)
    {
        WorldPosition d = r[leg + 1].position - r[leg].position;
        double len = d.Magnitude;
        return len < 1e-9 ? new WorldPosition(1, 0) : new WorldPosition(d.North / len, -d.East / len);
    }

    /// <summary>Ручка XTE участка: середина участка + XTE вправо.</summary>
    public static WorldPosition XteHandle(IList<RouteWaypoint> r, int leg) => LegMid(r, leg) + RightNormal(r, leg) * r[leg + 1].xteLimit;

    /// <summary>XTE по точке p: расстояние от оси участка, шаг step, не меньше step.</summary>
    public static float XteFrom(IList<RouteWaypoint> r, int leg, WorldPosition p, double step = XteStep)
    {
        WorldPosition n = RightNormal(r, leg), d = p - r[leg].position;
        double dist = Math.Abs(d.East * n.East + d.North * n.North);
        double v = Math.Max(step, Math.Round(dist / step) * step);
        return (float)v;
    }

    // ─────────── Попадание мышью ───────────
    private static double PixDist(MapViewport v, WorldPosition p, double px, double py)
    {
        v.ToPixel(p, out double x, out double y);
        return Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
    }

    /// <summary>Точка маршрута под курсором (позже поставленная — сверху), −1 — нет.</summary>
    public static int HitPoint(IList<RouteWaypoint> r, MapViewport v, double px, double py, double maxPx = PointHitPx)
    {
        if (r == null) return -1;
        int best = -1;
        double bestD = maxPx;
        for (int i = r.Count - 1; i >= 0; i--)
        {
            if (r[i] == null) continue;
            double d = PixDist(v, r[i].position, px, py);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Середина участка под курсором (вставить точку), −1 — нет.</summary>
    public static int HitLegMid(IList<RouteWaypoint> r, MapViewport v, double px, double py, double maxPx = MidHitPx)
    {
        int best = -1;
        double bestD = maxPx;
        for (int i = 0; i < LegCount(r); i++)
        {
            if (r[i] == null || r[i + 1] == null) continue;
            double d = PixDist(v, LegMid(r, i), px, py);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Ручка XTE видна: отстоит от оси не меньше XteHandleMinPx (и участок не короче 3·XteHandleMinPx).</summary>
    public static bool XteHandleVisible(IList<RouteWaypoint> r, int leg, MapViewport v) =>
        r[leg] != null && r[leg + 1] != null &&
        r[leg + 1].xteLimit / v.MetersPerPixel >= XteHandleMinPx && LegLength(r, leg) / v.MetersPerPixel >= 3.0 * XteHandleMinPx;

    /// <summary>Ручка XTE под курсором (номер участка), −1 — нет.</summary>
    public static int HitXte(IList<RouteWaypoint> r, MapViewport v, double px, double py, double maxPx = XteHitPx)
    {
        int best = -1;
        double bestD = maxPx;
        for (int i = 0; i < LegCount(r); i++)
        {
            if (!XteHandleVisible(r, i, v)) continue;
            double d = PixDist(v, XteHandle(r, i), px, py);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    public static bool HitStart(OwnShipInitialState s, MapViewport v, double px, double py, double maxPx = StartHitPx) =>
        s != null && PixDist(v, s.position, px, py) <= maxPx;

    // ─────────── Старт ───────────
    /// <summary>Ручка курса: от старта по курсу на meters.</summary>
    public static WorldPosition HeadingHandle(WorldPosition pos, double headingDeg, double meters)
    {
        double a = headingDeg * Math.PI / 180.0;
        return pos + new WorldPosition(Math.Sin(a), Math.Cos(a)) * meters;
    }

    /// <summary>Курс от старта к точке, шаг stepDeg, 0…359.</summary>
    public static float HeadingTo(WorldPosition from, WorldPosition to, double stepDeg = 1.0)
    {
        if ((to - from).Magnitude < 1e-6) return 0f;
        double c = Math.Round(Bearing(from, to) / stepDeg) * stepDeg;
        if (c >= 360.0) c -= 360.0;
        return (float)c;
    }

    // ─────────── Замечания редактора ───────────
    /// <summary>Замечания сверх Validate: старт далеко от первой точки; участок над глубинами меньше осадки + запас.</summary>
    public static List<string> Check(ScenarioDefinition s)
    {
        var w = new List<string>();
        if (s == null || s.route == null) return w;
        List<RouteWaypoint> r = s.route;
        if (s.initial != null && Valid(r, 0) && Valid(r, 1))
        {
            double d = (s.initial.position - r[0].position).Magnitude;
            if (d > Math.Max(r[1].xteLimit, 50.0))
                w.Add($"Старт в {ScenarioText.Num(d, 0)} м от первой точки «{r[0].name}» — дальше XTE первого участка.");
        }
        double draft = s.shipParameters != null ? s.shipParameters.draft : 0.0;
        if (s.waterArea != null && draft > 0.0)
        {
            double need = draft + UnderKeelM;
            for (int i = 0; i < LegCount(r); i++)
            {
                if (r[i] == null || r[i + 1] == null) continue;
                double len = LegLength(r, i);
                int n = Math.Max(2, Math.Min(2000, (int)Math.Ceiling(len / 10.0)));
                double min = double.MaxValue;
                for (int k = 0; k <= n; k++)
                {
                    WorldPosition p = r[i].position + (r[i + 1].position - r[i].position) * ((double)k / n);
                    min = Math.Min(min, s.waterArea.DepthAt(p));
                }
                if (min < need)
                    w.Add($"Участок «{r[i].name}» → «{r[i + 1].name}» проходит над глубиной {ScenarioText.Num(min, 1)} м " +
                          $"(меньше осадки {ScenarioText.Num(draft, 1)} м + {ScenarioText.Num(UnderKeelM, 0)} м).");
            }
        }
        return w;
    }
}
