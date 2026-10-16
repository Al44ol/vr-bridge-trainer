// ============================================================
// MooringRecording.cs — Запись швартовки: mooring.csv (запрос 19.2б-3б)
//
// Строка mooring.csv — состояние у причала в тот же момент, что строка track.csv
// (5 Гц по времени упражнения, TrackSampler): зазоры борта до кромки у носа и у кормы,
// скорость этих точек к причалу, угол к кромке (BerthReadout, как строка «ПРИЧАЛ» NavDisplay),
// ход относительно грунта, кранцы (касание, наибольшее обжатие — доля номинального δ_н,
// суммарная реакция), касание кромки корпусом, подруливающее (команда и факт), место
// обучающегося (BridgeStation), этап задания швартовки и по шести концам — состояние,
// кнехт, длина хорды, натяжение.
// Когда пишется (ExerciseRecorder): в сценарии швартовки — всегда (причал задания); в прочих —
// с первого момента, когда причал считается (MooringController, ближе 600 м), и до конца
// упражнения; файла нет, если причал не понадобился.
// Срез — одна функция MooringSampler.Build (Core): её вызывает и запись в сцене, и прогон
// «за обучающегося» (ExerciseSimulation, часть «в») — строки совпадают бит в бит, как track.csv.
// ============================================================
using System;
using System.Collections.Generic;
using System.Text;

/// <summary>Состояние конца в mooring.csv (столбец *_state).</summary>
public enum MooringLineStatus
{
    Aboard,      // на борту (не подан или отдан)
    HaulingIn,   // подан, лебёдка выбирает
    PayingOut,   // подан, лебёдка травит
    Holding,     // подан, на тормозе лебёдки
    OnBitts,     // подан, на кнехтах судна
    Broken       // оборван
}

/// <summary>Конец в строке mooring.csv.</summary>
public struct MooringLineSample
{
    public MooringLineStatus Status;
    /// <summary>Кнехт причала (−1 — не подан).</summary>
    public int Bollard;
    /// <summary>Длина хорды клюз — кнехт, м (NaN — не подан).</summary>
    public double LengthM;
    /// <summary>Натяжение, кН.</summary>
    public double TensionKn;

    public bool Fast => Status != MooringLineStatus.Aboard && Status != MooringLineStatus.Broken;
}

/// <summary>Строка mooring.csv.</summary>
public struct MooringSample
{
    public const int LineCount = 6;

    public double Time;
    /// <summary>Причал — номер в акватории (WaterArea.berths); −1 — причала рядом нет (поля зазоров пустые).</summary>
    public int Berth;
    public double BowGapM, SternGapM;
    /// <summary>Скорость точек борта к кромке, уз (> 0 — к причалу).</summary>
    public double BowSpeedKn, SternSpeedKn;
    /// <summary>Угол ДП к кромке, ° (отрицательный — нос к причалу).</summary>
    public double AngleDeg;
    /// <summary>Ход относительно грунта, уз.</summary>
    public double SpeedOverGroundKn;
    public bool FenderContact;
    /// <summary>Наибольшее обжатие кранца, доля номинального δ_н.</summary>
    public double FenderDeflectionRatio;
    /// <summary>Суммарная реакция кранцев, кН.</summary>
    public double FenderForceKn;
    /// <summary>Корпус зашёл за кромку (удар о причал).</summary>
    public bool QuayContact;
    /// <summary>Подруливающее: команда и фактическая тяга, % (> 0 — нос вправо).</summary>
    public double ThrusterOrderPct, ThrusterActualPct;
    /// <summary>Место обучающегося (BridgeStation).</summary>
    public int Station;
    /// <summary>Сценарий швартовки: выполнено заданий (0…4) и сколько держится условие «ошвартован», с; −1 — не швартовка.</summary>
    public int StagesDone;
    public double MooredHoldS;
    public MooringLineSample[] Lines;
    /// <summary>19.3б: буксир и ветер (столбцы в конце строки).</summary>
    public TowingSample Towing;

    public bool HasBerth => Berth >= 0 && !double.IsNaN(BowGapM);

    /// <summary>Наименьший зазор (нос или корма), м; NaN — причала нет.</summary>
    public double MinGapM => HasBerth ? Math.Min(BowGapM, SternGapM) : double.NaN;
}

public static class MooringRecording
{
    /// <summary>Префиксы столбцов концов (порядок — MooringLineKind).</summary>
    public static readonly string[] LinePrefixes = { "head", "fwd_breast", "fwd_spring", "aft_spring", "aft_breast", "stern" };

    public static readonly string Header = BuildHeader();

    private static string BuildHeader()
    {
        var b = new StringBuilder("t_s;berth;bow_gap_m;stern_gap_m;bow_speed_kn;stern_speed_kn;angle_deg;sog_kn;" +
                                  "fender_contact;fender_defl_ratio;fender_force_kn;quay_contact;thruster_order_pct;thruster_actual_pct;" +
                                  "station;stages_done;moored_hold_s");
        foreach (string p in LinePrefixes) b.Append($";{p}_state;{p}_bollard;{p}_length_m;{p}_tension_kn");
        foreach (string c in TowingSample.Columns) b.Append(';').Append(c);   // 19.3б
        return b.ToString();
    }

    /// <summary>Названия состояний концов по номеру (session.json → recording.mooring_line_states).</summary>
    public static List<string> StatusTitles()
    {
        var list = new List<string>();
        foreach (MooringLineStatus s in Enum.GetValues(typeof(MooringLineStatus))) list.Add(StatusTitle(s));
        return list;
    }

    public static string StatusTitle(MooringLineStatus s)
    {
        switch (s)
        {
            case MooringLineStatus.HaulingIn: return "выбирается";
            case MooringLineStatus.PayingOut: return "травится";
            case MooringLineStatus.Holding: return "на тормозе";
            case MooringLineStatus.OnBitts: return "на кнехтах";
            case MooringLineStatus.Broken: return "оборван";
            default: return "на борту";
        }
    }

    public static MooringLineStatus StatusOf(MooringLine l)
    {
        if (l == null || l.State == MooringLineState.Aboard) return MooringLineStatus.Aboard;
        if (l.State == MooringLineState.Broken) return MooringLineStatus.Broken;
        if (l.OnBitts) return MooringLineStatus.OnBitts;
        return l.Winch == WinchCommand.HaulIn ? MooringLineStatus.HaulingIn : l.Winch == WinchCommand.PayOut ? MooringLineStatus.PayingOut : MooringLineStatus.Holding;
    }

    public static void Append(StringBuilder b, in MooringSample s)
    {
        CsvFormat.Num(b, s.Time, 3); CsvFormat.Sep(b);
        if (s.Berth >= 0) CsvFormat.Int(b, s.Berth);
        CsvFormat.Sep(b);
        CsvFormat.Num(b, s.BowGapM, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.SternGapM, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.BowSpeedKn, 3); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.SternSpeedKn, 3); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.AngleDeg, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.SpeedOverGroundKn, 3); CsvFormat.Sep(b);
        b.Append(s.FenderContact ? '1' : '0'); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.FenderDeflectionRatio, 3); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.FenderForceKn, 2); CsvFormat.Sep(b);
        b.Append(s.QuayContact ? '1' : '0'); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.ThrusterOrderPct, 0); CsvFormat.Sep(b);
        CsvFormat.Num(b, s.ThrusterActualPct, 1); CsvFormat.Sep(b);
        CsvFormat.Int(b, s.Station); CsvFormat.Sep(b);
        if (s.StagesDone >= 0) CsvFormat.Int(b, s.StagesDone);
        CsvFormat.Sep(b);
        CsvFormat.Num(b, s.MooredHoldS, 1);
        for (int i = 0; i < MooringSample.LineCount; i++)
        {
            MooringLineSample l = s.Lines != null && i < s.Lines.Length ? s.Lines[i] : default;
            CsvFormat.Sep(b); CsvFormat.Int(b, (int)l.Status);
            CsvFormat.Sep(b); if (l.Bollard >= 0) CsvFormat.Int(b, l.Bollard);
            CsvFormat.Sep(b); CsvFormat.Num(b, l.LengthM, 2);
            CsvFormat.Sep(b); CsvFormat.Num(b, l.TensionKn, 2);
        }
        s.Towing.Append(b);   // 19.3б
        b.Append('\n');
    }

    /// <summary>Строка в том виде, в каком её прочитает SessionReader (оценка в шлеме = по записи).</summary>
    public static MooringSample Quantize(in MooringSample s)
    {
        var q = new MooringSample
        {
            Time = CsvFormat.Round(s.Time, 3), Berth = s.Berth >= 0 ? s.Berth : -1,
            BowGapM = CsvFormat.Round(s.BowGapM, 2), SternGapM = CsvFormat.Round(s.SternGapM, 2),
            BowSpeedKn = CsvFormat.Round(s.BowSpeedKn, 3), SternSpeedKn = CsvFormat.Round(s.SternSpeedKn, 3),
            AngleDeg = CsvFormat.Round(s.AngleDeg, 2), SpeedOverGroundKn = CsvFormat.Round(s.SpeedOverGroundKn, 3),
            FenderContact = s.FenderContact, FenderDeflectionRatio = CsvFormat.Round(s.FenderDeflectionRatio, 3),
            FenderForceKn = CsvFormat.Round(s.FenderForceKn, 2), QuayContact = s.QuayContact,
            ThrusterOrderPct = CsvFormat.Round(s.ThrusterOrderPct, 0), ThrusterActualPct = CsvFormat.Round(s.ThrusterActualPct, 1),
            Station = s.Station, StagesDone = s.StagesDone >= 0 ? s.StagesDone : -1, MooredHoldS = CsvFormat.Round(s.MooredHoldS, 1),
            Lines = new MooringLineSample[MooringSample.LineCount],
            Towing = s.Towing.Quantize()
        };
        for (int i = 0; i < MooringSample.LineCount; i++)
        {
            MooringLineSample l = s.Lines != null && i < s.Lines.Length ? s.Lines[i] : default;
            q.Lines[i] = new MooringLineSample
            {
                Status = l.Status, Bollard = l.Bollard >= 0 ? l.Bollard : -1,
                LengthM = CsvFormat.Round(l.LengthM, 2), TensionKn = CsvFormat.Round(l.TensionKn, 2)
            };
        }
        return q;
    }

    /// <summary>Строка из полей CSV (get — значение столбца по имени, пусто — нет).</summary>
    public static MooringSample Parse(Func<string, string> get)
    {
        double D(string n) => CsvFormat.ParseDouble(get(n));
        int I(string n, int fallback) => CsvFormat.ParseInt(get(n), fallback);
        var s = new MooringSample
        {
            Time = D("t_s"), Berth = I("berth", -1),
            BowGapM = D("bow_gap_m"), SternGapM = D("stern_gap_m"),
            BowSpeedKn = D("bow_speed_kn"), SternSpeedKn = D("stern_speed_kn"),
            AngleDeg = D("angle_deg"), SpeedOverGroundKn = D("sog_kn"),
            FenderContact = I("fender_contact", 0) == 1, FenderDeflectionRatio = D("fender_defl_ratio"),
            FenderForceKn = D("fender_force_kn"), QuayContact = I("quay_contact", 0) == 1,
            ThrusterOrderPct = D("thruster_order_pct"), ThrusterActualPct = D("thruster_actual_pct"),
            Station = I("station", 0), StagesDone = I("stages_done", -1), MooredHoldS = D("moored_hold_s"),
            Lines = new MooringLineSample[MooringSample.LineCount],
            Towing = TowingSample.Parse(get)   // 19.3б; у записей до 19.3б — буксира нет, ветер 0
        };
        for (int i = 0; i < MooringSample.LineCount; i++)
        {
            string p = LinePrefixes[i];
            s.Lines[i] = new MooringLineSample
            {
                Status = (MooringLineStatus)Math.Max(0, Math.Min((int)MooringLineStatus.Broken, I(p + "_state", 0))),
                Bollard = I(p + "_bollard", -1), LengthM = D(p + "_length_m"), TensionKn = D(p + "_tension_kn")
            };
        }
        return s;
    }
}

/// <summary>Срез состояния у причала — общий для записи в сцене и прогона «за обучающегося».</summary>
public static class MooringSampler
{
    /// <summary>
    /// Строка mooring.csv в момент t. berth — причал, к которому считаются зазоры (в сценарии швартовки —
    /// причал задания, иначе — причал, который считает MooringSystem), berthIndex — его номер в акватории;
    /// system — швартовка судна (null — ещё не было шага): кранцы, кромка и концы берутся, только если
    /// system считает именно этот причал; подруливающее — всегда. task — задание (null — не швартовка).
    /// </summary>
    public static MooringSample Build(double t, BerthArea berth, int berthIndex, ShipParameters p, ShipDynamicsModel m,
                                      MooringSystem system, int thrusterIndex, BridgeStation station, MooringTaskTracker task)
    {
        var s = new MooringSample
        {
            Time = t, Berth = berth != null ? berthIndex : -1,
            BowGapM = double.NaN, SternGapM = double.NaN, BowSpeedKn = double.NaN, SternSpeedKn = double.NaN, AngleDeg = double.NaN,
            Station = (int)station,
            StagesDone = task != null ? task.DoneCount : -1,
            MooredHoldS = task != null ? task.HoldTime : double.NaN,
            ThrusterOrderPct = ThrusterOrders.Command(thrusterIndex) * 100.0,
            ThrusterActualPct = system != null && system.Thruster != null ? system.Thruster.Actual * 100.0 : 0.0,
            Lines = new MooringLineSample[MooringSample.LineCount],
            Towing = TowingSample.None   // 19.3б: буксир и ветер ставит источник (TowingSample.Build)
        };
        if (m != null)
            s.SpeedOverGroundKn = Math.Sqrt(m.VelocityEast * m.VelocityEast + m.VelocityNorth * m.VelocityNorth) / ShipParameters.KnotsToMs;

        BerthReadout r = BerthReadout.Compute(berth, p, m);
        if (r.Valid)
        {
            s.BowGapM = r.BowGapM; s.SternGapM = r.SternGapM;
            s.BowSpeedKn = r.BowSpeedKn; s.SternSpeedKn = r.SternSpeedKn;
            s.AngleDeg = r.AngleDeg;
        }

        bool here = system != null && berth != null && system.Berth == berth;
        if (here)
        {
            double maxDefl = 0.0;
            foreach (double d in system.FenderDeflection) if (d > maxDefl) maxDefl = d;
            s.FenderContact = maxDefl > 0.0;
            s.FenderDeflectionRatio = berth.fenderRatedDeflectionM > 0 ? maxDefl / berth.fenderRatedDeflectionM : 0.0;
            s.FenderForceKn = system.TotalFenderForce / 1000.0;
            s.QuayContact = system.QuayPenetration > 0.0;
        }
        for (int i = 0; i < MooringSample.LineCount; i++)
        {
            MooringLine l = system != null && i < system.Lines.Count ? system.Lines[i] : null;
            var ls = new MooringLineSample { Status = MooringRecording.StatusOf(l), Bollard = -1, LengthM = double.NaN };
            if (l != null && l.State == MooringLineState.Fast)
            {
                ls.Bollard = l.Bollard;
                ls.LengthM = l.Length;
                ls.TensionKn = l.Tension / 1000.0;
            }
            s.Lines[i] = ls;
        }
        return s;
    }
}
