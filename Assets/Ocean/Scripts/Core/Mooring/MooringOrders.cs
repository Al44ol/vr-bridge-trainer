// ============================================================
// MooringOrders.cs — Положения подруливающего, команды меню «Управление», события швартовки в шину
// (запрос 19.2б)
//
// Подруливающее: 5 положений рычага (решение пользователя) — ЛБ 100 / 50 / 0 / ПБ 50 / 100 %;
// команда BowThruster.Command = −1 … 1 (> 0 — сила на правый борт).
// Меню «Управление» (кнопка B правого контроллера): команды экипажу «как рулевому» — руль
// ступенями (лево/право 5, 10, 20, 35°, прямо), телеграф по положениям, подруливающее.
// MooringEvent (Core, MooringSystem) → ExerciseEvent: единицы — уз, кДж, кН (шапка ExerciseEvents.cs).
// Часть 2: команды концов (LineOrder → MooringSystem.Order), состояние конца словами (в «экзамене» — без кН),
// провис конца в сцене (MooringLineShape), строка причала на NavDisplay (BerthReadout), места на мостике.
// ============================================================
using System;

public enum ThrusterOrder
{
    Port100,
    Port50,
    Zero,
    Starboard50,
    Starboard100
}

public static class ThrusterOrders
{
    public const int Count = 5;
    public const int ZeroIndex = (int)ThrusterOrder.Zero;

    /// <summary>Команда BowThruster.Command, −1 … 1 (> 0 — ПБ).</summary>
    public static double Command(int index) => 0.5 * (Clamp(index) - ZeroIndex);

    /// <summary>Ближайшее положение к команде (−1 … 1).</summary>
    public static int IndexOf(double command) => Clamp((int)Math.Round(command * 2.0) + ZeroIndex);

    public static int Clamp(int index) => Math.Max(0, Math.Min(Count - 1, index));

    public static string Title(int index) => TitleOfPercent(Command(index) * 100.0);

    /// <summary>«ЛБ 50 %», «0», «ПБ 100 %» по проценту (> 0 — ПБ).</summary>
    public static string TitleOfPercent(double percent)
    {
        if (double.IsNaN(percent)) return "—";
        if (Math.Abs(percent) < 0.5) return "0";
        return $"{(percent > 0 ? "ПБ" : "ЛБ")} {ScenarioText.Num(Math.Abs(percent))} %";
    }

    /// <summary>Подпись на шкале рычага.</summary>
    public static string ScaleLabel(int index)
    {
        double p = Command(index) * 100.0;
        return Math.Abs(p) < 0.5 ? "0" : ScenarioText.Num(Math.Abs(p));
    }
}

public static class ControlOrders
{
    public const string Rudder = "руль", Telegraph = "машина", Thruster = "подруливающее";

    /// <summary>Ступени руля по меню, ° (> 0 — ПБ).</summary>
    public static readonly double[] RudderSteps = { -35, -20, -10, -5, 0, 5, 10, 20, 35 };

    /// <summary>Ближайшая ступень руля к углу, номер в RudderSteps.</summary>
    public static int NearestRudderStep(double deg)
    {
        int best = 0;
        for (int i = 1; i < RudderSteps.Length; i++)
            if (Math.Abs(RudderSteps[i] - deg) < Math.Abs(RudderSteps[best] - deg)) best = i;
        return best;
    }

    /// <summary>Руль словами: «прямо», «лево 10°», «право 35°».</summary>
    public static string RudderTitle(double deg)
    {
        if (double.IsNaN(deg)) return "—";
        if (Math.Abs(deg) < 0.05) return "прямо";
        return $"{(deg > 0 ? "право" : "лево")} {ScenarioText.Num(Math.Abs(deg))}°";
    }

    /// <summary>Значение команды для журнала: руль — °, машина — положение телеграфа, подруливающее — %.</summary>
    public static string ValueText(string subject, double value)
    {
        if (double.IsNaN(value)) return "—";
        switch (subject)
        {
            case Rudder: return RudderTitle(value);
            case Telegraph: return ScenarioText.Telegraph(ScenarioText.OrderFromSpeedFactor(value));
            case Thruster: return ThrusterOrders.TitleOfPercent(value);
        }
        return ScenarioText.Num(value, 1);
    }
}

public static class MooringEventBus
{
    /// <summary>Событие MooringSystem → событие шины упражнения (без времени и состояния судна — их ставит источник).</summary>
    public static ExerciseEvent ToExerciseEvent(in MooringEvent e)
    {
        const double kn = ShipParameters.KnotsToMs;
        switch (e.Type)
        {
            case MooringEventType.BerthingImpact:
                return new ExerciseEvent(ExerciseEventType.BerthingImpact)
                {
                    Value = e.Value / kn, Value2 = e.Value2 / 1000.0,
                    Index = (int)Math.Round(e.Value3 * 100.0), Subject = e.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
            case MooringEventType.FenderOverload:
                return new ExerciseEvent(ExerciseEventType.FenderOverload) { Index = e.Index, Value = e.Value };
            case MooringEventType.QuayImpact:
                return new ExerciseEvent(ExerciseEventType.QuayImpact) { Value = e.Value / kn };
            case MooringEventType.LineMadeFast:
                return new ExerciseEvent(ExerciseEventType.LineMadeFast) { Index = e.Index, Subject = e.Subject ?? "", Value = e.Value, Value2 = e.Value2 };
            case MooringEventType.LineReleased:
                return new ExerciseEvent(ExerciseEventType.LineReleased) { Index = e.Index, Subject = e.Subject ?? "" };
            case MooringEventType.LineOverload:
                return new ExerciseEvent(ExerciseEventType.LineOverload) { Index = e.Index, Subject = e.Subject ?? "", Value = e.Value / 1000.0, Value2 = e.Value2 };
            case MooringEventType.LineOverloadCleared:
                return new ExerciseEvent(ExerciseEventType.LineOverloadCleared) { Index = e.Index, Subject = e.Subject ?? "" };
            case MooringEventType.LineBroken:
                return new ExerciseEvent(ExerciseEventType.LineBroken) { Index = e.Index, Subject = e.Subject ?? "", Value = e.Value / 1000.0 };
        }
        throw new ArgumentOutOfRangeException(nameof(e), e.Type, "нет соответствия в шине");
    }
}

// ─────────── Часть 2 (19.2б-2): команды концов, провис, строка причала ───────────

/// <summary>Команда концу из меню «Управление» → «Концы» (номер — Value события LineCommand).</summary>
public enum LineOrder
{
    Give,      // подать на кнехт
    HaulIn,    // выбирать (лебёдка)
    PayOut,    // травить (лебёдка)
    Hold,      // держать (тормоз лебёдки)
    OnBitts,   // на кнехты судна
    Release    // отдать
}

public static class LineOrders
{
    public static string Title(LineOrder o)
    {
        switch (o)
        {
            case LineOrder.Give: return "подать";
            case LineOrder.HaulIn: return "выбирать";
            case LineOrder.PayOut: return "травить";
            case LineOrder.Hold: return "держать";
            case LineOrder.OnBitts: return "на кнехты";
            default: return "отдать";
        }
    }

    /// <summary>
    /// Состояние конца словами. showForce — натяжение числом (режимы «обучение», «инструктор»);
    /// без него — как докладывает экипаж: «слабо» (T < 5 % MBL), «обтянут» (< 30 %), «сильно натянут».
    /// </summary>
    public static string State(MooringLine l, bool showForce)
    {
        if (l == null) return "—";
        switch (l.State)
        {
            case MooringLineState.Aboard: return "на борту";
            case MooringLineState.Broken: return "ОБОРВАН";
        }
        string winch = l.OnBitts ? "на кнехтах" : l.Winch == WinchCommand.HaulIn ? "выбирается" : l.Winch == WinchCommand.PayOut ? "травится" : "на тормозе";
        double share = l.Mbl > 0 ? l.Tension / l.Mbl : 0.0;
        string force = showForce
            ? $"{ScenarioText.Num(l.Tension / 1000.0)} кН ({ScenarioText.Num(share * 100.0)} % MBL)"
            : share < 0.05 ? "слабо" : share < 0.3 ? "обтянут" : "сильно натянут";
        return $"{winch}, {ScenarioText.Num(l.Length, 1)} м, {force}";
    }
}

/// <summary>Форма конца в сцене: провис по параболе (вид; на силу не влияет — 19.16, п. 4).</summary>
public static class MooringLineShape
{
    /// <summary>
    /// Стрела провиса, м. Слабый конец (L₀ > c): длина параболы L ≈ c + 8s²/(3c) → s = √(3·c·(L₀ − c)/8).
    /// Обтянутый (L₀ ≤ c): малая стрела, тающая с натяжением: s = 0,01·c·(1 − T/(0,3·MBL)), не меньше 0.
    /// </summary>
    public static double Sag(double chord, double restLength, double tension, double mbl)
    {
        if (chord <= 0.0) return 0.0;
        if (restLength > chord) return Math.Min(0.5 * restLength, Math.Sqrt(3.0 * chord * (restLength - chord) / 8.0));
        double k = mbl > 0 ? Math.Min(1.0, tension / (0.3 * mbl)) : 1.0;
        return 0.01 * chord * (1.0 - k);
    }

    /// <summary>Длина параболы со стрелой s над хордой c (приближение), м.</summary>
    public static double ParabolaLength(double chord, double sag) => chord + 8.0 * sag * sag / (3.0 * Math.Max(1e-9, chord));
}

/// <summary>
/// Строка причала на NavDisplay (19.2б-2): расстояние от борта до кромки по нормали у носа (конец
/// цилиндрической вставки, x = +0,25·L — «плечо») и у кормы (x = −0,45·L), скорость этих точек к кромке
/// (> 0 — к причалу), угол диаметральной плоскости к кромке. Борт — обращённый к причалу.
/// </summary>
public struct BerthReadout
{
    public const double BowX = 0.25, SternX = -0.45, ShowWithinM = 300.0;

    public bool Valid;
    public double DistanceM;          // до отрезка кромки (от точки судна)
    public double BowGapM, SternGapM;
    public double BowSpeedKn, SternSpeedKn;
    public double AngleDeg;

    public static BerthReadout Compute(BerthArea b, ShipParameters p, ShipDynamicsModel m)
    {
        var r = new BerthReadout();
        if (b == null || p == null || m == null || b.Length < 1.0) return r;
        var ship = new WorldPosition(m.East, m.North);
        r.DistanceM = b.DistanceTo(ship);
        WorldPosition n = b.NAxis;
        double sE = Math.Cos(m.Psi), sN = -Math.Sin(m.Psi);                  // на правый борт
        int side = -(sE * n.East + sN * n.North) >= 0.0 ? 1 : -1;            // как MooringSystem.SideToBerth
        Point(b, p, m, BowX * p.length, side, out r.BowGapM, out r.BowSpeedKn);
        Point(b, p, m, SternX * p.length, side, out r.SternGapM, out r.SternSpeedKn);
        double d = m.Psi * 180.0 / Math.PI - b.BearingDeg;
        d = ((d % 180.0) + 180.0) % 180.0;
        r.AngleDeg = d > 90.0 ? d - 180.0 : d;
        r.Valid = true;
        return r;
    }

    private static void Point(BerthArea b, ShipParameters p, ShipDynamicsModel m, double x, int side, out double gap, out double speedKn)
    {
        double y = side * MooringHull.HalfBeamAt(x, p);
        WorldPosition w = MooringHull.ToWorld(m.East, m.North, m.Psi, x, y);
        b.ToLocal(w, out _, out gap);
        WorldPosition v = MooringHull.PointVelocity(m, w);
        WorldPosition n = b.NAxis;
        speedKn = -(v.East * n.East + v.North * n.North) / ShipParameters.KnotsToMs;
    }

    /// <summary>Две строки для NavDisplay.</summary>
    public string Format()
    {
        if (!Valid) return "";
        return $"ПРИЧАЛ  нос {ScenarioText.Num(BowGapM, 1)} м · корма {ScenarioText.Num(SternGapM, 1)} м\n" +
               $"  к причалу нос {ScenarioText.Num(BowSpeedKn, 2)} · корма {ScenarioText.Num(SternSpeedKn, 2)} уз · угол {ScenarioText.Num(AngleDeg)}°";
    }
}

/// <summary>Место обучающегося на мостике (19.2б-2: переход «К штурвалу / Крыло ЛБ / Крыло ПБ»; Index события StationChanged).</summary>
public enum BridgeStation
{
    Helm,
    WingPort,
    WingStarboard
}

public static class BridgeStations
{
    public static string Title(BridgeStation s) =>
        s == BridgeStation.WingPort ? "крыло ЛБ" : s == BridgeStation.WingStarboard ? "крыло ПБ" : "у штурвала";
}
