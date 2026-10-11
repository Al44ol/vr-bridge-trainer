// ============================================================
// RouteTracker.cs — Контроль движения по маршруту (B2)
//
// Чистый класс: работает в сцене (ScenarioRunner, на каждом шаге модели),
// в тестах и — в C2 — офлайн по записанной траектории.
//
// Участок i — от точки W_i к точке W_{i+1}. Для позиции судна P:
//     t = (W_{i+1} − W_i) / |W_{i+1} − W_i|   — направление участка,
//     n = (t_N, −t_E)                          — нормаль вправо от линии пути,
//     XTE = (P − W_i)·n                        — боковое отклонение (> 0 — правее),
//     ATD = (P − W_i)·t                        — пройдено вдоль участка.
// Точка отсчёта — позиция судна в модели (ShipRoot), без смещения антенны.
//
// Прохождение точки:
//   • внутренняя точка W_{i+1} (есть следующий участок с направлением t₂) —
//     пересечение биссектрисы угла поворота: (P − W_{i+1})·(t + t₂) ≥ 0;
//     на прямой (t₂ = t) это перпендикуляр к участку, при развороте на 180°
//     (t + t₂ = 0) — тоже перпендикуляр;
//   • последняя точка — пересечение перпендикуляра (линии финиша): ATD ≥ |участка|.
// Точки проходятся строго по порядку; за один шаг может пройти несколько.
//
// Коридор участка: допустимое отклонение XTE_доп — у точки, к которой ведёт участок.
//     выход:   |XTE| > XTE_доп;
//     возврат: |XTE| < XTE_доп · (1 − η)   (гистерезис, против дребезга у границы).
// После прибытия в последнюю точку контроль прекращается.
// ============================================================
using System;
using System.Collections.Generic;

public enum RouteEventKind { WaypointPassed, CorridorExit, CorridorReturn, Arrived }

public struct RouteEvent
{
    public RouteEventKind Kind;
    /// <summary>Номер точки (прохождение) или участка (коридор).</summary>
    public int Index;
    public string Name;
    /// <summary>XTE, м (прохождение, выход); max |XTE| за выход (возврат).</summary>
    public double Xte;
    /// <summary>XTE доп., м (прохождение, выход); длительность выхода, с (возврат).</summary>
    public double Value2;
}

public sealed class RouteTracker
{
    /// <summary>Гистерезис возврата в коридор η (доля XTE_доп).</summary>
    public double ReturnHysteresis = 0.05;

    public IReadOnlyList<RouteWaypoint> Route { get; private set; }
    /// <summary>Активный участок: от точки Leg к точке Leg + 1.</summary>
    public int Leg { get; private set; }
    public int WaypointCount => Route != null ? Route.Count : 0;
    /// <summary>Пройдено точек (без начальной точки маршрута).</summary>
    public int PassedCount { get; private set; }
    /// <summary>Пройдена последняя точка.</summary>
    public bool Finished { get; private set; }

    public double Xte { get; private set; }
    public double Atd { get; private set; }
    public double LegLength { get; private set; }
    /// <summary>Курс участка, градусы.</summary>
    public double LegCourseDeg { get; private set; }
    public double XteLimit { get; private set; }
    public bool OutsideCorridor { get; private set; }

    // ── Статистика упражнения ──
    public double MaxAbsXte { get; private set; }
    public double TimeOutside { get; private set; }
    public int CorridorExits { get; private set; }

    /// <summary>События последнего Update.</summary>
    public IReadOnlyList<RouteEvent> Events => events;

    private readonly List<RouteEvent> events = new List<RouteEvent>();
    private double excursionStart, excursionMax;
    private bool started;

    public bool Valid => Route != null && Route.Count >= 2;

    public void Reset(IReadOnlyList<RouteWaypoint> route)
    {
        Route = route;
        Leg = 0;
        PassedCount = 0;
        Finished = !Valid;
        Xte = Atd = 0;
        OutsideCorridor = false;
        MaxAbsXte = TimeOutside = 0;
        CorridorExits = 0;
        excursionStart = excursionMax = 0;
        started = false;
        events.Clear();
        if (Valid) Geometry(new WorldPosition(Route[0].position.East, Route[0].position.North));
    }

    /// <summary>Шаг контроля: позиция судна, время упражнения, шаг по времени (с).</summary>
    public IReadOnlyList<RouteEvent> Update(WorldPosition p, double time, double dt)
    {
        events.Clear();
        if (!Valid || Finished) return events;

        if (started && OutsideCorridor) TimeOutside += dt;
        started = true;

        Geometry(p);

        // Прохождение точек (по порядку, возможно несколько за шаг)
        while (!Finished && Passed(p))
        {
            int wp = Leg + 1;
            events.Add(new RouteEvent { Kind = RouteEventKind.WaypointPassed, Index = wp, Name = Route[wp].name, Xte = Xte, Value2 = XteLimit });
            PassedCount++;
            if (wp == Route.Count - 1)
            {
                Finished = true;
                if (OutsideCorridor) CloseExcursion(time);
                events.Add(new RouteEvent { Kind = RouteEventKind.Arrived, Index = wp, Name = Route[wp].name, Xte = Xte, Value2 = XteLimit });
                return events;
            }
            Leg++;
            Geometry(p);
        }

        // Коридор активного участка
        double a = Math.Abs(Xte);
        if (a > MaxAbsXte) MaxAbsXte = a;
        if (!OutsideCorridor)
        {
            if (a > XteLimit)
            {
                OutsideCorridor = true;
                CorridorExits++;
                excursionStart = time;
                excursionMax = a;
                events.Add(new RouteEvent { Kind = RouteEventKind.CorridorExit, Index = Leg, Name = Route[Leg + 1].name, Xte = Xte, Value2 = XteLimit });
            }
        }
        else
        {
            if (a > excursionMax) excursionMax = a;
            if (a < XteLimit * (1.0 - ReturnHysteresis)) CloseExcursion(time);
        }
        return events;
    }

    private void CloseExcursion(double time)
    {
        OutsideCorridor = false;
        events.Add(new RouteEvent { Kind = RouteEventKind.CorridorReturn, Index = Leg, Name = Route[Math.Min(Leg + 1, Route.Count - 1)].name, Xte = excursionMax, Value2 = time - excursionStart });
    }

    // XTE, ATD и параметры активного участка
    private void Geometry(WorldPosition p)
    {
        WorldPosition a = Route[Leg].position, b = Route[Leg + 1].position;
        Direction(a, b, out double te, out double tn, out double len);
        double dE = p.East - a.East, dN = p.North - a.North;
        Xte = dE * tn - dN * te;     // n = (t_N, −t_E)
        Atd = dE * te + dN * tn;
        LegLength = len;
        LegCourseDeg = (Math.Atan2(te, tn) * 180.0 / Math.PI + 360.0) % 360.0;
        XteLimit = Math.Max(0.0, Route[Leg + 1].xteLimit);
    }

    // Пройдена ли точка Leg + 1
    private bool Passed(WorldPosition p)
    {
        int wp = Leg + 1;
        if (wp == Route.Count - 1) return Atd >= LegLength;

        WorldPosition b = Route[wp].position;
        Direction(Route[Leg].position, b, out double t1e, out double t1n, out _);
        Direction(b, Route[wp + 1].position, out double t2e, out double t2n, out _);
        double be = t1e + t2e, bn = t1n + t2n;
        if (be * be + bn * bn < 1e-12) { be = t1e; bn = t1n; }   // разворот на 180°
        return (p.East - b.East) * be + (p.North - b.North) * bn >= 0.0;
    }

    private static void Direction(WorldPosition a, WorldPosition b, out double te, out double tn, out double len)
    {
        double de = b.East - a.East, dn = b.North - a.North;
        len = Math.Sqrt(de * de + dn * dn);
        if (len < 1e-9) { te = 0; tn = 1; len = 0; return; }
        te = de / len; tn = dn / len;
    }

    /// <summary>XTE точки P относительно участка A→B (> 0 — правее), м.</summary>
    public static double CrossTrack(WorldPosition a, WorldPosition b, WorldPosition p)
    {
        Direction(a, b, out double te, out double tn, out _);
        return (p.East - a.East) * tn - (p.North - a.North) * te;
    }
}
