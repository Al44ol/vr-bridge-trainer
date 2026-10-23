// ============================================================
// MonitorData.cs — Данные мониторов пульта M1, M5–M8 (запрос 16б, Core: без сцены, проверяется вне Unity)
//
//   • MapRange — масштаб карты M6: радиус окна (мили) из ряда 0,25 / 0,5 / 1 / 2 / 4 / 8 — наименьший, при котором
//     следующая точка маршрута видна с запасом 15 % (дальше 8 миль — 8, у самой точки — 0,25);
//   • TargetTable — таблица целей M7 (как у САРП): пеленг, дистанция, CPA, TCPA по векторам скоростей (свой — с учётом
//     сноса лагом в модели 3 СС); опасная цель — 0 < TCPA ≤ TCPA_пред и CPA < CPA_пред (по умолчанию 0,5 мили —
//     как CPA_доп оценки, 12 мин — обычная настройка САРП); порядок: сближающиеся по CPA, затем расходящиеся по дистанции;
//     рабочие суда (буксир, судно-клиент) не цели расхождения — исключаются, как в оценке;
//   • RouteReadout — точка маршрута M6: номер и имя следующей точки, дистанция и пеленг до неё, курс участка,
//     XTE (> 0 — правее линии) и предел коридора;
//   • MonitorMessages — какие события шины показывать в журнале M1 и какие — тревожные (красные);
//   • MonitorText — подписи: скорость, TCPA, мили, ветер в м/с, волнение, румб из 8.
// ============================================================
using System;
using System.Collections.Generic;

/// <summary>Масштаб карты монитора маршрута.</summary>
public static class MapRange
{
    public static readonly double[] RangesNm = { 0.25, 0.5, 1, 2, 4, 8 };
    public const double Margin = 1.15;

    /// <summary>Радиус окна карты, мили: точка на дистанции distanceM видна с запасом Margin.</summary>
    public static double Choose(double distanceM)
    {
        if (double.IsNaN(distanceM) || distanceM <= 0) return RangesNm[0];
        double need = distanceM * Margin / CpaCalculator.MetersPerNm;
        foreach (double r in RangesNm) if (r >= need) return r;
        return RangesNm[RangesNm.Length - 1];
    }
}

/// <summary>Цель для таблицы: имя, позиция, курс и скорость.</summary>
public struct TargetInput
{
    public string Id;
    public WorldPosition Position;
    public double CourseDeg, SpeedKnots;

    public TargetInput(string id, WorldPosition p, double courseDeg, double speedKnots)
    {
        Id = id; Position = p; CourseDeg = courseDeg; SpeedKnots = speedKnots;
    }
}

/// <summary>Строка таблицы целей.</summary>
public struct TargetRow
{
    public string Id;
    public double BearingDeg, RangeNm, CpaNm, TcpaMin;
    public bool Approaching, Dangerous;
}

public static class TargetTable
{
    public const double DefaultCpaLimitNm = 0.5;
    public const double DefaultTcpaLimitMin = 12.0;

    /// <summary>Вектор скорости своего судна (восток, север), м/с: продольная u и поперечная v (> 0 — к ПБ) при курсе ψ.</summary>
    public static void OwnVelocity(double headingDeg, double surgeKn, double swayKn, out double ve, out double vn)
    {
        double c = headingDeg * Math.PI / 180.0, u = surgeKn * ShipParameters.KnotsToMs, v = swayKn * ShipParameters.KnotsToMs;
        ve = u * Math.Sin(c) + v * Math.Cos(c);
        vn = u * Math.Cos(c) - v * Math.Sin(c);
    }

    public static bool IsDangerous(CpaResult c, double cpaLimitNm, double tcpaLimitMin) =>
        c.Tcpa > 0 && c.Tcpa <= tcpaLimitMin * 60.0 && c.Cpa < cpaLimitNm * CpaCalculator.MetersPerNm;

    public static List<TargetRow> Build(WorldPosition own, double headingDeg, double surgeKn, double swayKn,
                                        IEnumerable<TargetInput> targets, double cpaLimitNm = DefaultCpaLimitNm,
                                        double tcpaLimitMin = DefaultTcpaLimitMin, Func<string, bool> exclude = null)
    {
        OwnVelocity(headingDeg, surgeKn, swayKn, out double oe, out double on);
        var rows = new List<TargetRow>();
        if (targets == null) return rows;
        foreach (TargetInput t in targets)
        {
            if (exclude != null && exclude(t.Id)) continue;
            CpaCalculator.Velocity(t.CourseDeg, t.SpeedKnots, out double te, out double tn);
            CpaResult c = CpaCalculator.Compute(own, oe, on, t.Position, te, tn);
            rows.Add(new TargetRow
            {
                Id = t.Id,
                BearingDeg = c.BearingDeg,
                RangeNm = c.Range / CpaCalculator.MetersPerNm,
                CpaNm = c.Cpa / CpaCalculator.MetersPerNm,
                TcpaMin = c.Tcpa / 60.0,
                Approaching = c.Tcpa > 0,
                Dangerous = IsDangerous(c, cpaLimitNm, tcpaLimitMin)
            });
        }
        rows.Sort((a, b) =>
        {
            if (a.Approaching != b.Approaching) return a.Approaching ? -1 : 1;
            return a.Approaching ? a.CpaNm.CompareTo(b.CpaNm) : a.RangeNm.CompareTo(b.RangeNm);
        });
        return rows;
    }
}

/// <summary>Следующая точка маршрута и положение относительно участка.</summary>
public struct RouteReadout
{
    public bool Valid;
    /// <summary>Индекс следующей точки в маршруте (1…N−1) и всего точек.</summary>
    public int Next, Count;
    public string Name;
    public double DistanceM, BearingDeg, LegCourseDeg, Xte, XteLimit;
    public bool Outside;

    /// <summary>Участок leg (от точки leg к leg + 1) и позиция судна p.</summary>
    public static RouteReadout For(IReadOnlyList<RouteWaypoint> route, int leg, WorldPosition p)
    {
        var r = new RouteReadout();
        if (route == null || route.Count < 2) return r;
        leg = Math.Max(0, Math.Min(leg, route.Count - 2));
        WorldPosition a = route[leg].position, b = route[leg + 1].position;
        double de = b.East - p.East, dn = b.North - p.North;
        r.Valid = true;
        r.Next = leg + 1;
        r.Count = route.Count;
        r.Name = route[leg + 1].name;
        r.DistanceM = Math.Sqrt(de * de + dn * dn);
        r.BearingDeg = Bearing(de, dn);
        r.LegCourseDeg = Bearing(b.East - a.East, b.North - a.North);
        r.Xte = RouteTracker.CrossTrack(a, b, p);
        r.XteLimit = route[leg + 1].xteLimit;
        r.Outside = Math.Abs(r.Xte) > r.XteLimit;
        return r;
    }

    private static double Bearing(double de, double dn)
    {
        double deg = Math.Atan2(de, dn) * 180.0 / Math.PI;
        return deg < 0 ? deg + 360.0 : deg;
    }
}

/// <summary>Вид события в журнале монитора «Сообщения».</summary>
public enum MessageKind { Hidden, Normal, Alarm }

public static class MonitorMessages
{
    /// <summary>
    /// Не показываются: действия самого обучающегося (руль, телеграф, тифон, подруливающее, концы, джойстик, переходы),
    /// служебные события оценки (обнаружение цели, начало ситуации — в «экзамене» это подсказка), планшета, режима и
    /// срабатывание триггера (видимое сообщение триггера — ScenarioMessage). Тревожные — опасность и повреждения.
    /// </summary>
    public static MessageKind Kind(ExerciseEventType t)
    {
        switch (t)
        {
            case ExerciseEventType.HelmStarted:
            case ExerciseEventType.HelmEnded:
            case ExerciseEventType.TelegraphChanged:
            case ExerciseEventType.HornStarted:
            case ExerciseEventType.HornEnded:
            case ExerciseEventType.TargetDetected:
            case ExerciseEventType.EncounterStarted:
            case ExerciseEventType.SoundSignal:
            case ExerciseEventType.PlotterRevealTargets:
            case ExerciseEventType.TrainerModeChanged:
            case ExerciseEventType.ThrusterChanged:
            case ExerciseEventType.ControlOrder:
            case ExerciseEventType.LineCommand:
            case ExerciseEventType.StationChanged:
            case ExerciseEventType.TugOrder:
            case ExerciseEventType.JoystickChanged:
            case ExerciseEventType.HeadingHoldChanged:
            case ExerciseEventType.JoystickHoldChanged:
            case ExerciseEventType.TriggerFired:
                return MessageKind.Hidden;
            case ExerciseEventType.CorridorExit:
            case ExerciseEventType.ShoalWarningStarted:
            case ExerciseEventType.Grounded:
            case ExerciseEventType.Collision:
            case ExerciseEventType.EngineFailure:
            case ExerciseEventType.SteeringFailure:
            case ExerciseEventType.BerthingImpact:
            case ExerciseEventType.FenderOverload:
            case ExerciseEventType.QuayImpact:
            case ExerciseEventType.LineOverload:
            case ExerciseEventType.LineBroken:
            case ExerciseEventType.TowLineOverload:
            case ExerciseEventType.TowLineBroken:
            case ExerciseEventType.TugWorkHullContact:
                return MessageKind.Alarm;
            default:
                return MessageKind.Normal;
        }
    }

    /// <summary>Отказы — только когда инструктор включил оповещение (иначе журнал выдал бы скрытую вводную).</summary>
    public static bool IsFailure(ExerciseEventType t) =>
        t == ExerciseEventType.EngineFailure || t == ExerciseEventType.EngineRestored ||
        t == ExerciseEventType.SteeringFailure || t == ExerciseEventType.SteeringRestored;
}

public static class MonitorText
{
    /// <summary>Скорость, уз: «12,4», «−3,2» (назад).</summary>
    public static string Speed(double kn) => ScenarioText.Num(Math.Abs(kn) < 0.05 ? 0.0 : kn, 1);

    /// <summary>Мили: до 10 — с двумя знаками, дальше — с одним.</summary>
    public static string Nm(double nm) => ScenarioText.Num(nm, nm < 10 ? 2 : 1);

    /// <summary>TCPA, мин: «12,5», расходящаяся цель — «—».</summary>
    public static string Tcpa(double min) => min <= 0 ? "—" : ScenarioText.Num(Math.Min(min, 999), 1);

    /// <summary>Ветер: узлы → м/с.</summary>
    public static double KnotsToMs(double kn) => kn * ShipParameters.KnotsToMs;

    /// <summary>Румб из 8: «С», «СВ», «В», …</summary>
    public static string Point8(double deg)
    {
        string[] p = { "С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ" };
        double d = deg % 360.0;
        if (d < 0) d += 360.0;
        return p[(int)Math.Floor((d + 22.5) / 45.0) % 8];
    }

    /// <summary>Волнение по погоде сцены.</summary>
    public static string Sea(WeatherPresetId w)
    {
        switch (w)
        {
            case WeatherPresetId.Calm: return "штиль";
            case WeatherPresetId.Storm: return "шторм";
            case WeatherPresetId.Night: return "слабое";
            default: return "умеренное";
        }
    }

    /// <summary>Сторона отклонения: «12 м ПБ» (правее линии), «0 м».</summary>
    public static string Xte(double xte) =>
        Math.Abs(xte) < 0.5 ? "0 м" : $"{ScenarioText.Num(Math.Abs(xte))} м {(xte > 0 ? "ПБ" : "ЛБ")}";
}
