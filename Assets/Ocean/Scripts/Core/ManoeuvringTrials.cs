// ============================================================
// ManoeuvringTrials.cs — Стандартные манёвренные испытания (A3, часть 1)
//
// Испытания прогоняются на чистой модели ShipDynamicsModel (без сцены
// и Play Mode) с фиксированным шагом dt. Методики и критерии —
// IMO Res. MSC.137(76) «Standards for ship manoeuvrability» (2002)
// и пояснения MSC/Circ.1053 (2002).
//
// Модель — любая IVesselModel (A3.2): по умолчанию ShipDynamicsModel(Parameters),
// для калибровки — эталон MarinerModel через TrialSettings.ModelFactory.
//
// Система отсчёта испытания: старт в (0, 0), начальный курс 000° (север),
//   X — вправо от начального курса (восток), м;
//   Y — вдоль начального курса (север), м.
//
// Скорость испытаний (MSC.137(76), п. 4.2.1): не менее 90 % скорости,
// соответствующей 85 % максимальной мощности (MCR). Модель не знает
// мощности двигателя; принимается V_эксп ≈ скорость при 85 % MCR,
// поэтому по умолчанию V_исп = 0.9·V_эксп (можно задать вручную).
//
// Критерии (MSC.137(76), п. 5.2):
//   5.2.1 Поворотливость: выдвиг ≤ 4.5L, тактический диаметр ≤ 5L
//         (циркуляция с рулём 35° или максимальным).
//   5.2.2 Начальная поворотливость: руль 10°, путь до изменения курса
//         на 10° от начального ≤ 2.5L.
//   5.2.3 Устойчивость на курсе/одерживание (зигзаг):
//     1) 1-й угол зарыскивания 10°/10°:
//          ≤ 10°,             если L/V < 10 с;
//          ≤ 20°,             если L/V ≥ 30 с;
//          ≤ 5 + ½·(L/V) °,   если 10 ≤ L/V < 30 с;
//     2) 2-й угол 10°/10° — не более чем на 15° больше критерия 1-го угла;
//     3) 1-й угол 20°/20° ≤ 25°.
//   5.2.4 Торможение: путь при полном заднем (track reach) ≤ 15L
//         (Администрация может увеличить для крупных судов, не более 20L).
// ============================================================
using System;
using System.Collections.Generic;

/// <summary>Одна точка записи испытания.</summary>
public struct TrialSample
{
    public double T;              // с
    public double X, Y;           // м (X — вправо от начального курса, Y — вдоль)
    public double HeadingDeg;     // курс 0..360
    public double DeltaPsiDeg;    // накопленное изменение курса, ° (> 0 — вправо)
    public double U;              // м/с
    public double RDegS;          // °/с
    public double RudderOrderedDeg, RudderActualDeg;
    public double V;              // поперечная скорость, м/с (> 0 — на ПБ; модель 3 СС, запрос 19.1)
}

/// <summary>Показатель испытания со значением и, если есть, критерием IMO.</summary>
public sealed class TrialMetric
{
    public string Name;
    public double Value = double.NaN;
    public string Unit;
    /// <summary>Длина судна для пересчёта в L (NaN — показатель не в длинах).</summary>
    public double LengthForL = double.NaN;
    public string Criterion = "";
    /// <summary>true — соответствует, false — нет, null — критерия нет.</summary>
    public bool? Pass;
    public string Note = "";

    public double ValueInL => double.IsNaN(LengthForL) ? double.NaN : Value / LengthForL;
}

public sealed class TrialResult
{
    public string Id;
    public string Title;
    public bool Completed;
    public string Message = "";
    public readonly List<TrialSample> Samples = new List<TrialSample>();
    public readonly List<TrialMetric> Metrics = new List<TrialMetric>();
}

public sealed class TrialSettings
{
    public ShipParameters Parameters;
    /// <summary>Скорость испытаний (подход), м/с.</summary>
    public double TestSpeedMs;
    /// <summary>Шаг интегрирования, с.</summary>
    public double Dt = 0.02;
    /// <summary>Предельная длительность испытания, с.</summary>
    public double MaxDuration = 1800.0;
    /// <summary>Порог «судно остановилось» для пассивного торможения, уз.</summary>
    public double StopThresholdKn = 0.1;
    /// <summary>Интервал записи траектории в CSV, с.</summary>
    public double SampleInterval = 0.5;
    /// <summary>
    /// Другая модель судна (A3.2: эталон Mariner, повтор руля при идентификации).
    /// Аргумент — начальная скорость, м/с. null — ShipDynamicsModel(Parameters).
    /// </summary>
    public Func<double, IVesselModel> ModelFactory;
    /// <summary>Модель движения ShipDynamicsModel вместо указанной в ассете (запрос 19.1); null — как в ассете.</summary>
    public MotionModel? Motion;

    public static double DefaultTestSpeedMs(ShipParameters p) => 0.9 * p.ServiceSpeedMs;
}

public static class ManoeuvringTrials
{
    public const double KnotsToMs = ShipParameters.KnotsToMs;
    private const double Rad2Deg = 180.0 / Math.PI;

    // ─────────── Критерии MSC.137(76) ───────────

    /// <summary>Критерий 1-го угла зарыскивания 10°/10°, градусы (MSC.137(76) п. 5.2.3.1).</summary>
    public static double FirstOvershoot10Limit(double lOverV)
    {
        if (lOverV < 10.0) return 10.0;
        if (lOverV >= 30.0) return 20.0;
        return 5.0 + 0.5 * lOverV;
    }

    /// <summary>Критерий 2-го угла зарыскивания 10°/10°, градусы (п. 5.2.3.2): +15° к критерию 1-го.</summary>
    public static double SecondOvershoot10Limit(double lOverV) => FirstOvershoot10Limit(lOverV) + 15.0;

    public const double AdvanceLimitL = 4.5;          // п. 5.2.1
    public const double TacticalDiameterLimitL = 5.0; // п. 5.2.1
    public const double InitialTurningLimitL = 2.5;   // п. 5.2.2
    public const double FirstOvershoot20Limit = 25.0; // п. 5.2.3.3
    public const double TrackReachLimitL = 15.0;      // п. 5.2.4

    // ─────────── Прогон ───────────

    /// <summary>Модель + запись + геометрия относительно точки старта.</summary>
    private sealed class Run
    {
        public readonly IVesselModel Model;
        public readonly TrialSettings S;
        public readonly TrialResult Result;
        public double T, DeltaPsiDeg, Path;
        private double nextSample;

        public Run(TrialSettings s, string id, string title, double u0, double sf)
        {
            S = s;
            if (s.ModelFactory == null)
                Model = new ShipDynamicsModel(s.Parameters) { Psi = 0, U = u0, SpeedFactor = sf, MotionOverride = s.Motion };
            else
            {
                Model = s.ModelFactory(u0);
                Model.SpeedFactor = sf;
            }
            Result = new TrialResult { Id = id, Title = title };
            Record();
            nextSample = s.SampleInterval;
        }

        public double X => Model.East;
        public double Y => Model.North;
        public double L => Model.Length;
        public bool TimeLeft => T < S.MaxDuration;

        public void Step()
        {
            Model.Step(S.Dt);
            T += S.Dt;
            DeltaPsiDeg += Model.R * S.Dt * Rad2Deg;
            Path += (Model is ShipDynamicsModel sdm ? sdm.SpeedThroughWater : Math.Abs(Model.U)) * S.Dt;
            if (T + 1e-9 >= nextSample)
            {
                Record();
                nextSample += S.SampleInterval;
            }
        }

        public void Record()
        {
            Result.Samples.Add(new TrialSample
            {
                T = T, X = X, Y = Y,
                HeadingDeg = Model.Psi * Rad2Deg,
                DeltaPsiDeg = DeltaPsiDeg,
                U = Model.U,
                RDegS = Model.R * Rad2Deg,
                RudderOrderedDeg = Model.Ordered,
                RudderActualDeg = Model.Delta,
                V = Model is ShipDynamicsModel sdm ? sdm.V : 0.0,
            });
        }

        public TrialResult Finish(bool completed, string message = "")
        {
            int n = Result.Samples.Count;
            if (n == 0 || Result.Samples[n - 1].T < T - 1e-9) Record();
            Result.Completed = completed;
            Result.Message = message;
            return Result;
        }

        public TrialMetric Metric(string name, double value, string unit, bool inL = false)
        {
            var m = new TrialMetric { Name = name, Value = value, Unit = unit, LengthForL = inL ? L : double.NaN };
            Result.Metrics.Add(m);
            return m;
        }
    }

    // Линейная интерполяция величины в момент пересечения порога
    private static double Lerp(double a, double b, double f) => a + (b - a) * f;
    private static double Frac(double prev, double cur, double threshold) =>
        Math.Abs(cur - prev) < 1e-12 ? 1.0 : (threshold - prev) / (cur - prev);

    private static double TestSf(TrialSettings s) => s.Parameters == null ? 1.0 :
        ShipDynamicsModel.Clamp(s.TestSpeedMs / Math.Max(1e-6, s.Parameters.ServiceSpeedMs), 0.0, 1.0);

    private static void SetCriterion(TrialMetric m, string text, bool pass)
    {
        m.Criterion = text;
        m.Pass = double.IsNaN(m.Value) ? (bool?)null : pass;
    }

    // ─────────── Циркуляция ───────────

    /// <summary>Циркуляция с рулём 35° (или δ_max, если меньше): starboard — на правый борт.</summary>
    public static TrialResult Turning(TrialSettings s, bool starboard, double rudderDeg = 35.0)
    {
        double rudder = s.Parameters != null ? Math.Min(rudderDeg, s.Parameters.maxRudderAngle) : rudderDeg;
        string side = starboard ? "ПБ" : "ЛБ";
        var run = new Run(s, $"turning_{rudderDeg:F0}_{(starboard ? "stbd" : "port")}",
                          $"Циркуляция, руль {rudder:F0}° {side}", s.TestSpeedMs, TestSf(s));
        double sign = starboard ? 1.0 : -1.0;
        run.Model.RudderCommandDeg = sign * rudder;

        double advance = double.NaN, transfer = double.NaN, tactical = double.NaN, steadyD = double.NaN;
        double t90 = double.NaN, t180 = double.NaN, t360 = double.NaN, u360 = double.NaN;
        double x360 = 0, y360 = 0;
        bool done = false;
        while (run.TimeLeft)
        {
            double prevTurn = sign * run.DeltaPsiDeg, prevX = run.X, prevY = run.Y, prevT = run.T, prevU = run.Model.U;
            run.Step();
            double turn = sign * run.DeltaPsiDeg;
            if (double.IsNaN(t90) && turn >= 90.0)
            {
                double f = Frac(prevTurn, turn, 90.0);
                t90 = Lerp(prevT, run.T, f);
                advance = Lerp(prevY, run.Y, f);
                transfer = sign * Lerp(prevX, run.X, f);
            }
            if (double.IsNaN(t180) && turn >= 180.0)
            {
                double f = Frac(prevTurn, turn, 180.0);
                t180 = Lerp(prevT, run.T, f);
                tactical = sign * Lerp(prevX, run.X, f);
            }
            if (double.IsNaN(t360) && turn >= 360.0)
            {
                double f = Frac(prevTurn, turn, 360.0);
                t360 = Lerp(prevT, run.T, f);
                x360 = Lerp(prevX, run.X, f); y360 = Lerp(prevY, run.Y, f);
                u360 = Lerp(prevU, run.Model.U, f);
            }
            if (turn >= 540.0)
            {
                double f = Frac(prevTurn, turn, 540.0);
                double x = Lerp(prevX, run.X, f), y = Lerp(prevY, run.Y, f);
                // Точки при 360° и 540° диаметрально противоположны на установившейся циркуляции
                steadyD = Math.Sqrt((x - x360) * (x - x360) + (y - y360) * (y - y360));
                done = true;
                break;
            }
        }

        var p = s.Parameters;
        SetCriterion(run.Metric("Выдвиг (advance)", advance, "м", true),
            $"≤ {AdvanceLimitL:0.0#}L (MSC.137(76) 5.2.1)", advance / run.L <= AdvanceLimitL);
        run.Metric("Прямое смещение (transfer)", transfer, "м", true);
        SetCriterion(run.Metric("Тактический диаметр", tactical, "м", true),
            $"≤ {TacticalDiameterLimitL:0.0#}L (MSC.137(76) 5.2.1)", tactical / run.L <= TacticalDiameterLimitL);
        var d = run.Metric("Диаметр установившейся циркуляции", steadyD, "м", true);
        if (s.ModelFactory == null && p != null && p.yawModel == YawModel.Nomoto && p.turnSpeedLossCoeff == 0f)
            d.Note = $"теория 2L/(K'·δ) = {p.TheoreticalCircleDiameter((float)rudder):F0} м";
        var v = run.Metric("Скорость на установившейся циркуляции", u360 / KnotsToMs, "уз");
        v.Note = $"на подходе {s.TestSpeedMs / KnotsToMs:F1} уз; потеря {100.0 * (1.0 - u360 / s.TestSpeedMs):F0} %";
        run.Metric("Время поворота на 90°", t90, "с");
        run.Metric("Время поворота на 180°", t180, "с");
        run.Metric("Время поворота на 360°", t360, "с");
        return run.Finish(done, done ? "" : $"за {s.MaxDuration:F0} с судно не повернуло на 540°");
    }

    // ─────────── Начальная поворотливость ───────────

    /// <summary>Руль 10° ПБ, путь до изменения курса на 10° (MSC.137(76) 5.2.2).</summary>
    public static TrialResult InitialTurning(TrialSettings s)
    {
        var run = new Run(s, "initial_turning_10", "Начальная поворотливость, руль 10° ПБ", s.TestSpeedMs, TestSf(s));
        run.Model.RudderCommandDeg = 10.0;
        double path = double.NaN, along = double.NaN, time = double.NaN;
        bool done = false;
        while (run.TimeLeft)
        {
            double prevTurn = run.DeltaPsiDeg, prevPath = run.Path, prevY = run.Y, prevT = run.T;
            run.Step();
            if (run.DeltaPsiDeg >= 10.0)
            {
                double f = Frac(prevTurn, run.DeltaPsiDeg, 10.0);
                path = Lerp(prevPath, run.Path, f);
                along = Lerp(prevY, run.Y, f);
                time = Lerp(prevT, run.T, f);
                done = true;
                break;
            }
        }
        SetCriterion(run.Metric("Путь до изменения курса на 10°", path, "м", true),
            $"≤ {InitialTurningLimitL:0.0#}L (MSC.137(76) 5.2.2)", path / run.L <= InitialTurningLimitL);
        run.Metric("Продвижение вдоль начального курса", along, "м", true);
        run.Metric("Время до изменения курса на 10°", time, "с");
        // Записать немного после события, чтобы на графике был виден поворот
        double tEnd = run.T + 30.0;
        while (done && run.T < tEnd && run.TimeLeft) run.Step();
        return run.Finish(done, done ? "" : "курс не изменился на 10°");
    }

    // ─────────── Зигзаг ───────────

    /// <summary>Зигзаг δ/ψ: первая перекладка на ПБ (MSC.137(76) 5.2.3, MSC/Circ.1053).</summary>
    public static TrialResult Zigzag(TrialSettings s, double rudderDeg, double headingDeg, int executes = 5)
    {
        var run = new Run(s, $"zigzag_{rudderDeg:F0}_{headingDeg:F0}",
                          $"Зигзаг {rudderDeg:F0}°/{headingDeg:F0}°", s.TestSpeedMs, TestSf(s));
        double sign = 1.0;
        run.Model.RudderCommandDeg = rudderDeg;
        var execTimes = new List<double>();
        var overshoots = new List<double>();
        double extreme = 0.0;     // экстремум изменения курса после последней перекладки
        while (run.TimeLeft && execTimes.Count < executes)
        {
            double prevTurn = run.DeltaPsiDeg, prevT = run.T;
            run.Step();
            double turn = run.DeltaPsiDeg;
            if (execTimes.Count > 0)
                extreme = sign < 0 ? Math.Max(extreme, turn) : Math.Min(extreme, turn);
            if (sign * turn >= headingDeg)
            {
                // Перекладка руля на другой борт
                if (execTimes.Count > 0)
                    overshoots.Add(Math.Abs(extreme) - headingDeg);
                execTimes.Add(Lerp(prevT, run.T, Frac(prevTurn, turn, sign * headingDeg)));
                sign = -sign;
                extreme = turn;
                run.Model.RudderCommandDeg = sign * rudderDeg;
            }
        }
        bool done = execTimes.Count >= executes;
        double v = s.TestSpeedMs;
        double lOverV = run.L / Math.Max(1e-6, v);
        double o1 = overshoots.Count > 0 ? overshoots[0] : double.NaN;
        double o2 = overshoots.Count > 1 ? overshoots[1] : double.NaN;

        var m1 = run.Metric("1-й угол зарыскивания", o1, "°");
        var m2 = run.Metric("2-й угол зарыскивания", o2, "°");
        if (Math.Abs(rudderDeg - 10.0) < 1e-6 && Math.Abs(headingDeg - 10.0) < 1e-6)
        {
            double lim1 = FirstOvershoot10Limit(lOverV), lim2 = SecondOvershoot10Limit(lOverV);
            SetCriterion(m1, $"≤ {lim1:F1}° при L/V = {lOverV:F1} с (MSC.137(76) 5.2.3.1)", o1 <= lim1);
            SetCriterion(m2, $"≤ {lim2:F1}° (MSC.137(76) 5.2.3.2)", o2 <= lim2);
        }
        else if (Math.Abs(rudderDeg - 20.0) < 1e-6 && Math.Abs(headingDeg - 20.0) < 1e-6)
        {
            SetCriterion(m1, $"≤ {FirstOvershoot20Limit:F0}° (MSC.137(76) 5.2.3.3)", o1 <= FirstOvershoot20Limit);
        }
        run.Metric("Время до 1-й перекладки", execTimes.Count > 0 ? execTimes[0] : double.NaN, "с");
        run.Metric("Время до 2-й перекладки", execTimes.Count > 1 ? execTimes[1] : double.NaN, "с");
        run.Metric("Период (1-я → 3-я перекладка)", execTimes.Count > 2 ? execTimes[2] - execTimes[0] : double.NaN, "с");
        run.Metric("L/V", lOverV, "с");
        return run.Finish(done, done ? "" : $"выполнено перекладок: {execTimes.Count} из {executes}");
    }

    // ─────────── Торможение и разгон ───────────

    /// <summary>Экстренное торможение: с подхода на скорости испытаний — «полный назад», руль прямо.</summary>
    public static TrialResult CrashStop(TrialSettings s)
    {
        var run = new Run(s, "crash_stop", "Торможение: полный вперёд → полный назад", s.TestSpeedMs, -1.0);
        double time = double.NaN, reach = double.NaN, head = double.NaN, lateral = double.NaN;
        bool done = false;
        while (run.TimeLeft)
        {
            double prevU = run.Model.U, prevT = run.T, prevPath = run.Path, prevY = run.Y, prevX = run.X;
            run.Step();
            if (run.Model.U <= 0.0)
            {
                double f = Frac(prevU, run.Model.U, 0.0);
                time = Lerp(prevT, run.T, f);
                reach = Lerp(prevPath, run.Path, f);
                head = Lerp(prevY, run.Y, f);
                lateral = Lerp(prevX, run.X, f);
                done = true;
                break;
            }
        }
        SetCriterion(run.Metric("Путь торможения (track reach)", reach, "м", true),
            $"≤ {TrackReachLimitL:0}L (MSC.137(76) 5.2.4)", reach / run.L <= TrackReachLimitL);
        run.Metric("Продвижение вдоль начального курса (head reach)", head, "м", true);
        var lat = run.Metric("Боковое смещение", lateral, "м", true);
        lat.Note = run.Model is ShipDynamicsModel sm && sm.Uses3Dof
            ? "3 СС: поперечная сила винта на заднем ходу (правый винт — корму влево)"
            : "модель без асимметрии винта — судно не уклоняется";
        run.Metric("Время до остановки", time, "с");
        return run.Finish(done, done ? "" : "судно не остановилось");
    }

    /// <summary>Пассивное торможение: «полный вперёд» → «стоп» до скорости ≤ порога.</summary>
    public static TrialResult PassiveStop(TrialSettings s)
    {
        double v0 = s.Parameters.ServiceSpeedMs;
        var run = new Run(s, "passive_stop", "Пассивное торможение: полный вперёд → стоп", v0, 0.0);
        double threshold = s.StopThresholdKn * KnotsToMs;
        double tHalf = double.NaN, dHalf = double.NaN, time = double.NaN, dist = double.NaN;
        bool done = false;
        while (run.TimeLeft)
        {
            double prevU = run.Model.U, prevT = run.T, prevPath = run.Path;
            run.Step();
            if (double.IsNaN(tHalf) && run.Model.U <= 0.5 * v0)
            {
                double f = Frac(prevU, run.Model.U, 0.5 * v0);
                tHalf = Lerp(prevT, run.T, f); dHalf = Lerp(prevPath, run.Path, f);
            }
            if (run.Model.U <= threshold)
            {
                double f = Frac(prevU, run.Model.U, threshold);
                time = Lerp(prevT, run.T, f); dist = Lerp(prevPath, run.Path, f);
                done = true;
                break;
            }
        }
        run.Metric("Время снижения скорости вдвое", tHalf, "с");
        run.Metric("Путь до снижения скорости вдвое", dHalf, "м", true);
        var t = run.Metric($"Время до скорости ≤ {s.StopThresholdKn:0.##} уз", time, "с");
        t.Note = "модель 1-го порядка: скорость убывает экспоненциально и не достигает нуля";
        run.Metric($"Путь до скорости ≤ {s.StopThresholdKn:0.##} уз", dist, "м", true);
        return run.Finish(done, done ? "" : $"за {s.MaxDuration:F0} с скорость не снизилась до порога");
    }

    /// <summary>Разгон: «стоп» → «полный вперёд» до 90 % установившейся скорости.</summary>
    public static TrialResult Acceleration(TrialSettings s)
    {
        double vSet = s.Parameters.ServiceSpeedMs;
        var run = new Run(s, "acceleration", "Разгон: стоп → полный вперёд", 0.0, 1.0);
        double target = 0.9 * vSet;
        double time = double.NaN, dist = double.NaN;
        bool done = false;
        while (run.TimeLeft)
        {
            double prevU = run.Model.U, prevT = run.T, prevPath = run.Path;
            run.Step();
            if (run.Model.U >= target)
            {
                double f = Frac(prevU, run.Model.U, target);
                time = Lerp(prevT, run.T, f); dist = Lerp(prevPath, run.Path, f);
                done = true;
                break;
            }
        }
        var t = run.Metric("Время до 90 % установившейся скорости", time, "с");
        t.Note = $"теория τ_разг·ln 10 = {s.Parameters.accelerationTimeConstant * Math.Log(10):F0} с";
        run.Metric("Путь до 90 % установившейся скорости", dist, "м", true);
        run.Metric("Установившаяся скорость", vSet / KnotsToMs, "уз");
        return run.Finish(done, done ? "" : "не достигнуто 90 % скорости");
    }

    /// <summary>Все испытания по порядку.</summary>
    public static List<TrialResult> RunAll(TrialSettings s)
    {
        return new List<TrialResult>
        {
            Turning(s, true),
            Turning(s, false),
            InitialTurning(s),
            Zigzag(s, 10, 10),
            Zigzag(s, 20, 20),
            CrashStop(s),
            PassiveStop(s),
            Acceleration(s),
        };
    }
}
