// ============================================================
// TrafficEditing.cs — Другие суда сценария в редакторе (запрос 21.5а)
//
// Цель — TrafficShipDefinition (ScenarioDefinition.traffic, B3). Начальная позиция и путь считаются тем же
// TrafficPlanner.Resolve, что в тренажёре, поэтому на карте редактора — ровно то, что будет в упражнении.
//   • Add — цель в точку (позиция «точка», курс — на старт своего судна, 10 уз), id «T1», «T2»…, имя «Цель-1»…;
//     Remove; MoveStart — «точка»: позиция; «пеленг и дистанция»: пересчёт от старта своего судна;
//     «встреча»: место считается само (не двигается — тянуть ромб TCPA или ручку курса).
//   • SetPlacement — смена способа задания с сохранением места цели: «пеленг и дистанция» — от старта своего
//     судна; «встреча» — CPA, TCPA и борт по текущей геометрии (если суда уже расходятся — TCPA 12 мин).
//   • Meet — встреча без манёвра: своё судно — начальным курсом и скоростью, цель — со старта своим курсом
//     (CpaCalculator.FromCourses); положения обоих в момент TCPA — концы отрезка CPA на карте.
//   • MeetHandle / TcpaFrom — ручка «встречи» на пути своего судна: тянуть вдоль пути — TCPA (шаг 0,5 мин).
//   • Попадание мышью: судно ≤ 12 пикс. (позже добавленное — сверху), точка маршрута цели ≤ 8 пикс.
//   • Check — замечания редактора: цель ближе 0,5 мили к своему судну; без манёвра CPA меньше CPA_доп;
//     встречи нет (TCPA ≤ 0); своё судно поворачивает по маршруту раньше TCPA (CPA будет другой).
//     Остальное (мель, вне района, сторона, скорость) — ScenarioDefinition.Validate.
// ============================================================
using System;
using System.Collections.Generic;

/// <summary>Встреча цели со своим судном без манёвра (21.5а).</summary>
public struct TrafficEncounter
{
    /// <summary>Начальное состояние цели (TrafficPlanner.Resolve).</summary>
    public TrafficStart Start;
    public double CpaNm, TcpaMin, RangeNm, BearingDeg;
    /// <summary>Положения своего судна и цели в момент TCPA (TCPA ≤ 0 — на старте).</summary>
    public WorldPosition OwnAtCpa, TargetAtCpa;
    /// <summary>Сближение есть (TCPA > 0 и есть относительное движение).</summary>
    public bool Closing;
}

public static class TrafficEditing
{
    public const double ShipHitPx = 12.0, PointHitPx = 8.0, MeetHitPx = 9.0;
    public const float DefaultSpeedKn = 10f;
    /// <summary>Замечание «цель близко к своему судну на старте», мили.</summary>
    public const double MinStartRangeNm = 0.5;
    /// <summary>CPA_доп по умолчанию (AssessmentSettings), мили.</summary>
    public const double DefaultRequiredCpaNm = 0.5;
    public const double TcpaStepMin = 0.5;

    public static bool Valid(IList<TrafficShipDefinition> t, int i) => t != null && i >= 0 && i < t.Count && t[i] != null;

    public static string NextId(IList<TrafficShipDefinition> t)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (t != null) for (int i = 0; i < t.Count; i++) if (t[i] != null) used.Add(t[i].TargetId(i));
        for (int k = 1; ; k++) if (!used.Contains("T" + k)) return "T" + k;
    }

    public static string NextName(IList<TrafficShipDefinition> t)
    {
        var used = new HashSet<string>();
        if (t != null) foreach (TrafficShipDefinition d in t) if (d != null && d.name != null) used.Add(d.name);
        for (int k = 1; ; k++) if (!used.Contains("Цель-" + k)) return "Цель-" + k;
    }

    /// <summary>Цель в точку p: «точка», курс — на старт своего судна; номер новой цели.</summary>
    public static int Add(List<TrafficShipDefinition> t, WorldPosition p, OwnShipInitialState own)
    {
        var d = new TrafficShipDefinition
        {
            id = NextId(t),
            name = NextName(t),
            placement = TrafficPlacement.Absolute,
            position = p,
            speedKnots = DefaultSpeedKn,
            headingDeg = own != null && (own.position - p).Magnitude > 1.0 ? (float)Math.Round(RouteEditing.Bearing(p, own.position)) % 360f : 0f
        };
        t.Add(d);
        return t.Count - 1;
    }

    public static bool Remove(List<TrafficShipDefinition> t, int i)
    {
        if (!Valid(t, i)) return false;
        t.RemoveAt(i);
        return true;
    }

    public static WorldPosition StartOf(TrafficShipDefinition d, OwnShipInitialState own) => TrafficPlanner.Resolve(d, own).Position;

    /// <summary>Курс цели на старте (по маршруту — на первую точку).</summary>
    public static double HeadingOf(TrafficShipDefinition d, OwnShipInitialState own) => TrafficPlanner.Resolve(d, own).HeadingDeg;

    /// <summary>Сдвиг цели мышью; false — место считается само («встреча»).</summary>
    public static bool MoveStart(TrafficShipDefinition d, OwnShipInitialState own, WorldPosition p)
    {
        switch (d.placement)
        {
            case TrafficPlacement.Absolute: d.position = p; return true;
            case TrafficPlacement.RelativeToOwnShip: SetRelative(d, own, p); return true;
            default: return false;
        }
    }

    private static void SetRelative(TrafficShipDefinition d, OwnShipInitialState own, WorldPosition p)
    {
        WorldPosition o = own != null ? own.position : default;
        d.bearingDeg = (float)RouteEditing.Bearing(o, p);
        d.distanceNm = (float)((p - o).Magnitude / CpaCalculator.MetersPerNm);
    }

    /// <summary>Смена способа задания — цель остаётся на месте (для «встречи» — по текущей геометрии).</summary>
    public static void SetPlacement(TrafficShipDefinition d, OwnShipInitialState own, TrafficPlacement to)
    {
        if (d.placement == to) return;
        TrafficEncounter e = Meet(d, own);
        WorldPosition p = e.Start.Position;
        d.headingDeg = (float)e.Start.HeadingDeg;
        switch (to)
        {
            case TrafficPlacement.Absolute: d.position = p; break;
            case TrafficPlacement.RelativeToOwnShip: SetRelative(d, own, p); break;
            case TrafficPlacement.MeetingPoint:
                if (e.Closing)
                {
                    d.cpaNm = (float)e.CpaNm;
                    d.tcpaMinutes = (float)e.TcpaMin;
                    d.meetingSide = SideAt(own, e.TargetAtCpa - e.OwnAtCpa);
                }
                else if (d.tcpaMinutes <= 0f) d.tcpaMinutes = 12f;
                break;
        }
        d.placement = to;
    }

    /// <summary>Борт своего судна, на котором цель в момент CPA (по знаку векторного произведения).</summary>
    public static MeetingSide SideAt(OwnShipInitialState own, WorldPosition rel)
    {
        double psi = (own != null ? own.headingDeg : 0.0) * Math.PI / 180.0;
        double right = rel.East * Math.Cos(psi) - rel.North * Math.Sin(psi);   // проекция на направление «правый борт»
        return right >= 0.0 ? MeetingSide.Starboard : MeetingSide.Port;
    }

    // ─────────── Встреча ───────────
    public static TrafficEncounter Meet(TrafficShipDefinition d, OwnShipInitialState own)
    {
        own = own ?? new OwnShipInitialState();
        TrafficStart st = TrafficPlanner.Resolve(d, own);
        CpaResult c = CpaCalculator.FromCourses(own.position, own.headingDeg, own.speedKnots, st.Position, st.HeadingDeg, st.SpeedKnots);
        double t = Math.Max(0.0, c.Tcpa);
        CpaCalculator.Velocity(own.headingDeg, own.speedKnots, out double oe, out double on);
        CpaCalculator.Velocity(st.HeadingDeg, st.SpeedKnots, out double te, out double tn);
        return new TrafficEncounter
        {
            Start = st,
            CpaNm = c.Cpa / CpaCalculator.MetersPerNm,
            TcpaMin = c.Tcpa / 60.0,
            RangeNm = c.Range / CpaCalculator.MetersPerNm,
            BearingDeg = c.BearingDeg,
            OwnAtCpa = own.position + new WorldPosition(oe, on) * t,
            TargetAtCpa = st.Position + new WorldPosition(te, tn) * t,
            Closing = c.Tcpa > 1e-6 && c.RelativeSpeed > 1e-6
        };
    }

    /// <summary>Ручка «встречи»: где своё судно будет через TCPA цели (заданный TCPA).</summary>
    public static WorldPosition MeetHandle(TrafficShipDefinition d, OwnShipInitialState own)
    {
        CpaCalculator.Velocity(own.headingDeg, own.speedKnots, out double oe, out double on);
        return own.position + new WorldPosition(oe, on) * (Math.Max(0f, d.tcpaMinutes) * 60.0);
    }

    /// <summary>TCPA по точке на пути своего судна (проекция на курс), мин, шаг step, не меньше step; −1 — своё судно стоит.</summary>
    public static float TcpaFrom(OwnShipInitialState own, WorldPosition p, double step = TcpaStepMin)
    {
        double v = own.speedKnots * ShipParameters.KnotsToMs;
        if (v < 1e-3) return -1f;
        double psi = own.headingDeg * Math.PI / 180.0;
        WorldPosition r = p - own.position;
        double along = r.East * Math.Sin(psi) + r.North * Math.Cos(psi);
        double min = along / v / 60.0;
        return (float)Math.Max(step, Math.Round(min / step) * step);
    }

    // ─────────── Маршрут цели ───────────
    public static int AddPoint(TrafficShipDefinition d, WorldPosition p)
    {
        if (d.route == null) d.route = new List<WorldPosition>();
        d.route.Add(p);
        return d.route.Count - 1;
    }

    public static bool RemovePoint(TrafficShipDefinition d, int i)
    {
        if (d.route == null || i < 0 || i >= d.route.Count) return false;
        d.route.RemoveAt(i);
        return true;
    }

    // ─────────── Попадание мышью ───────────
    private static double PixDist(MapViewport v, WorldPosition p, double px, double py)
    {
        v.ToPixel(p, out double x, out double y);
        return Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
    }

    /// <summary>Цель под курсором (позже добавленная — сверху), −1 — нет.</summary>
    public static int HitShip(IList<TrafficShipDefinition> t, OwnShipInitialState own, MapViewport v, double px, double py, double maxPx = ShipHitPx)
    {
        if (t == null) return -1;
        for (int i = t.Count - 1; i >= 0; i--)
            if (t[i] != null && PixDist(v, StartOf(t[i], own), px, py) <= maxPx) return i;
        return -1;
    }

    /// <summary>Точка маршрута цели под курсором, −1 — нет.</summary>
    public static int HitPoint(TrafficShipDefinition d, MapViewport v, double px, double py, double maxPx = PointHitPx)
    {
        if (d?.route == null) return -1;
        for (int i = d.route.Count - 1; i >= 0; i--)
            if (PixDist(v, d.route[i], px, py) <= maxPx) return i;
        return -1;
    }

    public static bool HitMeet(TrafficShipDefinition d, OwnShipInitialState own, MapViewport v, double px, double py, double maxPx = MeetHitPx) =>
        d != null && d.placement == TrafficPlacement.MeetingPoint && PixDist(v, MeetHandle(d, own), px, py) <= maxPx;

    // ─────────── Замечания редактора ───────────
    public static List<string> Check(ScenarioDefinition s)
    {
        var w = new List<string>();
        if (s?.traffic == null || s.initial == null) return w;
        OwnShipInitialState own = s.initial;
        double turnMin = OwnTurnMinutes(s, out string turnAt);
        for (int i = 0; i < s.traffic.Count; i++)
        {
            TrafficShipDefinition d = s.traffic[i];
            if (d == null) continue;
            string tag = $"Судно {d.TargetId(i)} «{d.name}»";
            TrafficEncounter e = Meet(d, own);
            if (!string.IsNullOrEmpty(e.Start.Problem)) continue;   // уже в Validate
            if (e.RangeNm < MinStartRangeNm)
                w.Add($"{tag}: на старте в {ScenarioText.Num(e.RangeNm, 2)} мили от своего судна (ближе {ScenarioText.Num(MinStartRangeNm, 1)}).");
            if (!e.Closing)
            {
                w.Add($"{tag}: суда уже расходятся (TCPA ≤ 0) — встречи не будет.");
                continue;
            }
            double req = d.requiredCpaNm > 0f ? d.requiredCpaNm : DefaultRequiredCpaNm;
            if (e.CpaNm < req)
                w.Add($"{tag}: без манёвра CPA {ScenarioText.Num(e.CpaNm, 2)} мили < CPA_доп {ScenarioText.Num(req, 2)} через {ScenarioText.Num(e.TcpaMin, 1)} мин — опасное сближение (так и задумано?).");
            if (turnMin == 0.0)
                w.Add($"{tag}: начальный курс своего судна расходится с курсом на «{turnAt}» больше 10° — " +
                      "встреча рассчитана по начальному курсу, фактическая CPA будет другой.");
            else if (turnMin > 0.0 && turnMin < e.TcpaMin)
                w.Add($"{tag}: своё судно поворачивает у «{turnAt}» через {ScenarioText.Num(turnMin, 1)} мин, раньше TCPA {ScenarioText.Num(e.TcpaMin, 1)} мин — " +
                      "встреча рассчитана по начальному курсу, фактическая CPA будет другой.");
        }
        return w;
    }

    /// <summary>
    /// Когда своё судно сойдёт с начального курса, мин: курс на вторую точку маршрута отличается от начального
    /// больше 10° — сразу (0); иначе — время до второй точки, если за ней есть поворот; −1 — поворота нет.
    /// </summary>
    public static double OwnTurnMinutes(ScenarioDefinition s, out string at)
    {
        at = "";
        List<RouteWaypoint> r = s.route;
        OwnShipInitialState own = s.initial;
        if (r == null || r.Count < 2 || r[1] == null || own == null || own.speedKnots < 0.1f) return -1.0;
        at = r[1].name;
        WorldPosition to = r[1].position - own.position;
        if (to.Magnitude < 1.0) return -1.0;
        double diff = Math.Abs(((RouteEditing.Bearing(own.position, r[1].position) - own.headingDeg) % 360.0 + 540.0) % 360.0 - 180.0);
        if (diff > 10.0) return 0.0;
        if (r.Count < 3) return -1.0;
        return to.Magnitude / (own.speedKnots * ShipParameters.KnotsToMs) / 60.0;
    }
}
