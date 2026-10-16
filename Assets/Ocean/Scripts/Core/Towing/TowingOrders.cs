// ============================================================
// TowingOrders.cs — Команды буксиру из меню, буксир в шине, записи и на картах (запрос 19.3б)
//
// Меню «Управление» (кнопка B) → «Буксир»: вызвать к корме / к носу / к миделю, режим (тянуть / толкать / стоп),
// направление силы на судно ступенями 45° — только допустимые для места буксира (трос от клюза не идёт через
// корпус): у кормы — от «на ЛБ» через «назад» до «на ПБ», у носа — через «вперёд», у миделя — все, кроме
// «вперёд» и «назад»; толкать — только «на ЛБ» / «на ПБ»; тяга — 10 / 25 / 50 / 100 % тяги на гаке.
// Клюз троса и точка толкания по месту (от миделя, доля L): корма −0,45 / −0,3; нос +0,45 / +0,3; мидель 0 / 0
// (у кормы — как на стенде 19.3а).
// Состояние словами (TugOrders.State): в «экзамене» — как докладывает шкипер, без кН («трос обтянут»),
// в «обучении» и «инструкторе» — натяжение троса и упор в борт числом.
// TowEvent (Core, TowingSystem) → ExerciseEvent (TowEventBus): единицы — уз, кН, % (шапка ExerciseEvents.cs).
// Буксир — цель реестра ExerciseTargets с id «TUG» (TugTarget): targets.csv, окно инструктора, планшет;
// оценка расхождения (C2) его не учитывает (ExerciseAssessor), CPA на планшете и в «Текущем балле» не считается.
// Строка mooring.csv дополнена столбцами буксира и ветра (TowingSample, в конце строки; у записей до 19.3б их нет).
// ============================================================
using System;
using System.Collections.Generic;

public static class TugOrders
{
    /// <summary>Ступени тяги, доля тяги на гаке.</summary>
    public static readonly double[] PowerSteps = { 0.1, 0.25, 0.5, 1.0 };

    private static readonly double[] SternDirs = { 270, 225, 180, 135, 90 };
    private static readonly double[] BowDirs = { 270, 315, 0, 45, 90 };
    private static readonly double[] MidshipDirs = { 225, 270, 315, 45, 90, 135 };
    private static readonly double[] PushDirs = { 270, 90 };

    /// <summary>Режимы в порядке переключателя меню.</summary>
    public static readonly TugMode[] Modes = { TugMode.Pull, TugMode.Stop, TugMode.Push };

    /// <summary>Допустимые направления силы на судно, ° (порядок переключателя: «<» — к ЛБ).</summary>
    public static double[] Directions(TugStation s, TugMode geometry)
    {
        if (geometry == TugMode.Push) return PushDirs;
        return s == TugStation.Bow ? BowDirs : s == TugStation.Midship ? MidshipDirs : SternDirs;
    }

    /// <summary>Направление при вызове: у кормы — назад, у носа — вперёд, у миделя — толкать на ЛБ.</summary>
    public static double DefaultDirection(TugStation s) => s == TugStation.Stern ? 180.0 : s == TugStation.Bow ? 0.0 : 270.0;

    /// <summary>Работа при вызове: у кормы и носа — на тросе, у миделя — у борта.</summary>
    public static TugMode DefaultGeometry(TugStation s) => s == TugStation.Midship ? TugMode.Push : TugMode.Pull;

    /// <summary>Номер ближайшего направления в списке.</summary>
    public static int NearestIndex(double[] dirs, double deg)
    {
        int best = 0;
        double bd = double.MaxValue;
        for (int i = 0; i < dirs.Length; i++)
        {
            double d = Math.Abs(Math.IEEERemainder(dirs[i] - deg, 360.0));
            if (d < bd - 1e-9) { bd = d; best = i; }
        }
        return best;
    }

    /// <summary>Клюз буксирного троса (на ДП), от миделя, м.</summary>
    public static double TowPointX(TugStation s, double length) => s == TugStation.Stern ? -0.45 * length : s == TugStation.Bow ? 0.45 * length : 0.0;

    /// <summary>Точка толкания по борту, от миделя, м.</summary>
    public static double PushPointX(TugStation s, double length) => s == TugStation.Stern ? -0.3 * length : s == TugStation.Bow ? 0.3 * length : 0.0;

    public static string StationTitle(TugStation s) => s == TugStation.Stern ? "у кормы" : s == TugStation.Bow ? "у носа" : "у миделя";

    /// <summary>«к корме» / «к носу» / «к миделю» (команда «вызвать буксир …»).</summary>
    public static string StationTarget(TugStation s) => s == TugStation.Stern ? "к корме" : s == TugStation.Bow ? "к носу" : "к миделю";

    public static string ModeTitle(TugMode m) => m == TugMode.Pull ? "тянуть" : m == TugMode.Push ? "толкать" : "стоп";

    public static string PowerTitle(double share) => $"{ScenarioText.Num(share * 100.0)} %";

    /// <summary>Команда по положению переключателей меню.</summary>
    public static TugCommand Command(TugMode mode, double directionDeg, double power) =>
        mode == TugMode.Stop ? new TugCommand { Mode = TugMode.Stop, DirectionDeg = directionDeg, Power = 0.0 }
                             : new TugCommand { Mode = mode, DirectionDeg = directionDeg, Power = power };

    /// <summary>
    /// Состояние буксира словами. showForce — натяжение троса и упор числом («обучение», «инструктор»);
    /// без него — как докладывает шкипер: трос «слабый» (T < 5 % MBL), «обтянут» (< 30 %), «сильно натянут».
    /// </summary>
    public static string State(TowingSystem ts, bool showForce)
    {
        if (ts == null) return "буксира нет";
        TugCommand c = ts.Tug.Command;
        var parts = new List<string>();
        string where = StationTitle(ts.Station);
        if (ts.GoingAround) parts.Add($"{where}: переходит на другой борт");
        else if (c.Mode == TugMode.Stop) parts.Add($"{where}: стоп, держится {(ts.Geometry == TugMode.Push ? "у борта" : "на месте")}");
        else if (c.Mode == TugMode.Pull) parts.Add($"{where}: тянет {TugCommand.DirectionTitle(c.DirectionDeg)}, {PowerTitle(c.Power)}");
        else parts.Add($"{where}: толкает {TugCommand.DirectionTitle(c.DirectionDeg)}, {PowerTitle(c.Power)}" + (ts.PushContact || ts.GoingAround ? "" : " — подходит к борту"));

        TowLine l = ts.Line;
        if (l.Broken) parts.Add("ТРОС ОБОРВАН");
        else if (l.Fast)
        {
            double share = l.Mbl > 0 ? l.Tension / l.Mbl : 0.0;
            parts.Add(showForce
                ? $"трос {ScenarioText.Num(l.Length, 1)} м, {ScenarioText.Num(l.Tension / 1000.0)} кН ({ScenarioText.Num(share * 100.0)} % MBL)"
                : share < 0.05 ? "трос слабый" : share < 0.3 ? "трос обтянут" : "трос сильно натянут");
        }
        else if (ts.Geometry == TugMode.Pull) parts.Add("трос не подан");
        if (showForce && ts.PushContact) parts.Add($"упор в борт {ScenarioText.Num(ts.PushForce / 1000.0)} кН");
        return string.Join(" · ", parts);
    }
}

public static class TowEventBus
{
    /// <summary>Событие TowingSystem → событие шины упражнения (без времени и состояния судна — их ставит источник).</summary>
    public static ExerciseEvent ToExerciseEvent(in TowEvent e)
    {
        switch (e.Type)
        {
            case TowEventType.CommandChanged:
            {
                TugMode m = e.Subject == "стоп" ? TugMode.Stop : e.Subject.StartsWith("тянуть", StringComparison.Ordinal) ? TugMode.Pull : TugMode.Push;
                return new ExerciseEvent(ExerciseEventType.TugOrder) { Subject = e.Subject ?? "", Index = (int)m, Value = e.Value * 100.0, Value2 = e.Value2 };
            }
            case TowEventType.LineMadeFast: return new ExerciseEvent(ExerciseEventType.TowLineMadeFast) { Value = e.Value, Value2 = e.Value2 / 1000.0 };
            case TowEventType.LineReleased: return new ExerciseEvent(ExerciseEventType.TowLineReleased);
            case TowEventType.LineOverload: return new ExerciseEvent(ExerciseEventType.TowLineOverload) { Value = e.Value / 1000.0, Value2 = e.Value2 };
            case TowEventType.LineOverloadCleared: return new ExerciseEvent(ExerciseEventType.TowLineOverloadCleared);
            case TowEventType.LineBroken: return new ExerciseEvent(ExerciseEventType.TowLineBroken) { Value = e.Value / 1000.0 };
            case TowEventType.PushContact: return new ExerciseEvent(ExerciseEventType.TugPushContact) { Value = e.Value / ShipParameters.KnotsToMs };
        }
        throw new ArgumentOutOfRangeException(nameof(e), e.Type, "нет соответствия в шине");
    }
}

/// <summary>Буксир в реестре целей (targets.csv, окно инструктора, планшет). Оценка расхождения его не учитывает.</summary>
public sealed class TugTarget : ITrackedTarget
{
    public const string Id = "TUG";
    public readonly TugVessel Tug;

    public TugTarget(TugVessel tug) { Tug = tug; }

    public static bool IsTug(string id) => id == Id;

    public string TargetId => Id;
    public WorldPosition Position => Tug.Position;
    public double HeadingDeg => Tug.HeadingDeg;
    /// <summary>Ход относительно грунта, уз (буксир ходит и лагом).</summary>
    public double SpeedKnots
    {
        get
        {
            ShipDynamicsModel m = Tug.Model;
            return Math.Sqrt(m.VelocityEast * m.VelocityEast + m.VelocityNorth * m.VelocityNorth) / ShipParameters.KnotsToMs;
        }
    }
    public double LengthM => Tug.Model.Parameters != null ? Tug.Model.Parameters.length : 22.0;
    public double BeamM => Tug.Model.Parameters != null ? Tug.Model.Parameters.beam : 8.0;
}

/// <summary>Состояние троса в mooring.csv (столбец towline_state).</summary>
public enum TowLineStatus
{
    NotFast,   // не подан (или отдан)
    Fast,      // подан
    Broken     // оборван
}

/// <summary>Буксир и ветер в строке mooring.csv (19.3б, столбцы в конце строки).</summary>
public struct TowingSample
{
    /// <summary>Буксир вызван.</summary>
    public bool Present;
    public TugStation Station;
    public TugMode Mode;
    /// <summary>Направление силы на судно, ° от ДП; доля тяги, %.</summary>
    public double DirectionDeg, PowerPct;
    public TowLineStatus Line;
    /// <summary>Длина хорды троса, м (NaN — не подан); натяжение, кН.</summary>
    public double LineLengthM, LineTensionKn;
    /// <summary>Упор буксира в борт, кН.</summary>
    public double PushKn;
    /// <summary>Ветер: скорость, уз; откуда, °.</summary>
    public double WindKn, WindFromDeg;

    public static TowingSample None => new TowingSample
    {
        Present = false, DirectionDeg = double.NaN, PowerPct = double.NaN, LineLengthM = double.NaN,
        LineTensionKn = double.NaN, PushKn = double.NaN, WindKn = 0.0, WindFromDeg = double.NaN
    };

    public static readonly string[] Columns =
        { "tug_station", "tug_mode", "tug_dir_deg", "tug_power_pct", "towline_state", "towline_length_m", "towline_tension_kn", "tug_push_kn", "wind_kn", "wind_from_deg" };

    public static string ModeTitle(TugMode m) => TugOrders.ModeTitle(m);

    public static List<string> LineTitles() => new List<string> { "не подан", "подан", "оборван" };

    public static List<string> ModeTitles()
    {
        var l = new List<string>();
        foreach (TugMode m in Enum.GetValues(typeof(TugMode))) l.Add(TugOrders.ModeTitle(m));
        return l;
    }

    public static List<string> StationTitles()
    {
        var l = new List<string>();
        foreach (TugStation s in Enum.GetValues(typeof(TugStation))) l.Add(TugOrders.StationTitle(s));
        return l;
    }

    /// <summary>Срез: буксир (null — не вызван) и ветер.</summary>
    public static TowingSample Build(TowingSystem ts, Wind wind)
    {
        TowingSample s = None;
        s.WindKn = wind.SpeedMs / ShipParameters.KnotsToMs;
        s.WindFromDeg = wind.Calm ? double.NaN : ((wind.FromDeg % 360.0) + 360.0) % 360.0;
        if (ts == null) return s;
        TugCommand c = ts.Tug.Command;
        s.Present = true;
        s.Station = ts.Station;
        s.Mode = c.Mode;
        s.DirectionDeg = c.DirectionDeg;
        s.PowerPct = c.Mode == TugMode.Stop ? 0.0 : c.Power * 100.0;
        s.Line = ts.Line.Broken ? TowLineStatus.Broken : ts.Line.Fast ? TowLineStatus.Fast : TowLineStatus.NotFast;
        if (ts.Line.Fast) { s.LineLengthM = ts.Line.Length; s.LineTensionKn = ts.Line.Tension / 1000.0; }
        s.PushKn = ts.PushForce / 1000.0;
        return s;
    }

    public void Append(System.Text.StringBuilder b)
    {
        CsvFormat.Sep(b); if (Present) CsvFormat.Int(b, (int)Station);
        CsvFormat.Sep(b); if (Present) CsvFormat.Int(b, (int)Mode);
        CsvFormat.Sep(b); CsvFormat.Num(b, Present ? DirectionDeg : double.NaN, 0);
        CsvFormat.Sep(b); CsvFormat.Num(b, Present ? PowerPct : double.NaN, 0);
        CsvFormat.Sep(b); if (Present) CsvFormat.Int(b, (int)Line);
        CsvFormat.Sep(b); CsvFormat.Num(b, Present ? LineLengthM : double.NaN, 2);
        CsvFormat.Sep(b); CsvFormat.Num(b, Present ? LineTensionKn : double.NaN, 2);
        CsvFormat.Sep(b); CsvFormat.Num(b, Present ? PushKn : double.NaN, 2);
        CsvFormat.Sep(b); CsvFormat.Num(b, WindKn, 1);
        CsvFormat.Sep(b); CsvFormat.Num(b, WindFromDeg, 0);
    }

    public TowingSample Quantize() => new TowingSample
    {
        Present = Present, Station = Present ? Station : TugStation.Stern, Mode = Present ? Mode : TugMode.Stop,
        DirectionDeg = Present ? CsvFormat.Round(DirectionDeg, 0) : double.NaN,
        PowerPct = Present ? CsvFormat.Round(PowerPct, 0) : double.NaN,
        Line = Present ? Line : TowLineStatus.NotFast,
        LineLengthM = Present ? CsvFormat.Round(LineLengthM, 2) : double.NaN,
        LineTensionKn = Present ? CsvFormat.Round(LineTensionKn, 2) : double.NaN,
        PushKn = Present ? CsvFormat.Round(PushKn, 2) : double.NaN,
        WindKn = CsvFormat.Round(WindKn, 1), WindFromDeg = CsvFormat.Round(WindFromDeg, 0)
    };

    /// <summary>Из полей CSV; у записей до 19.3б столбцов нет — буксира нет, ветер 0.</summary>
    public static TowingSample Parse(Func<string, string> get)
    {
        TowingSample s = None;
        int mode = CsvFormat.ParseInt(get("tug_mode"), -1);
        double wind = CsvFormat.ParseDouble(get("wind_kn"));
        s.WindKn = double.IsNaN(wind) ? 0.0 : wind;
        s.WindFromDeg = CsvFormat.ParseDouble(get("wind_from_deg"));
        if (mode < 0) return s;
        s.Present = true;
        s.Mode = (TugMode)Math.Max(0, Math.Min((int)TugMode.Push, mode));
        s.Station = (TugStation)Math.Max(0, Math.Min((int)TugStation.Midship, CsvFormat.ParseInt(get("tug_station"), 0)));
        s.DirectionDeg = CsvFormat.ParseDouble(get("tug_dir_deg"));
        s.PowerPct = CsvFormat.ParseDouble(get("tug_power_pct"));
        s.Line = (TowLineStatus)Math.Max(0, Math.Min((int)TowLineStatus.Broken, CsvFormat.ParseInt(get("towline_state"), 0)));
        s.LineLengthM = CsvFormat.ParseDouble(get("towline_length_m"));
        s.LineTensionKn = CsvFormat.ParseDouble(get("towline_tension_kn"));
        s.PushKn = CsvFormat.ParseDouble(get("tug_push_kn"));
        return s;
    }
}
