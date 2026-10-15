// ============================================================
// BerthingTrials.cs — Стенд швартовки без Play Mode (запрос 19.2)
//
// Для любого ShipParameters (модель 3 СС, dt = 0,02 с) у «своего» причала BerthArea.ForShip:
//   1. Навал лагом (параллельно кромке) со скоростью v_n = 0,05…0,5 уз: наибольшее обжатие
//      (доля δ_н), сумма реакций, поглощённая энергия W — против ½·m₂₂·v_n² (присоединённая масса
//      модели 3 СС) и оценки PIANC (2002) E = ½·m·v²·C_M (C_M = 1 + 2d/B, C_E = 1 при навале лагом);
//      коэффициент восстановления e = v_отх/v_n. Для 0,2 уз — запись по времени и петля
//      «обжатие — реакция» (гистерезис резины).
//   2. Навал носом под углом 5° и 10° (0,2 уз): эффективная доля энергии W/(½·m₂₂·v²) — аналог C_E.
//   3. Удержание на шести концах: судно у кранцев, концы поданы и выбраны (60 с), затем
//      (а) на тормозах лебёдок, (б) на кнехтах судна — нарастающая сила MBL/200 в секунду на миделе
//      от причала (как прижимной/отжимной ветер, 19.3) или вдоль причала (как течение):
//      сила первой перегрузки (T > 0,5·MBL), первого обрыва или «травления» (смещение > 2 м), какой конец.
//   4. Подруливающее 100 % на ходу 0/2/4/6 уз: сила, изменение курса и угловая скорость за 60 с.
// ============================================================
using System;
using System.Collections.Generic;

public static class BerthingTrials
{
    public const double Dt = 0.02;
    private const double Kn = 0.514444, Rad2Deg = 180.0 / Math.PI;

    public sealed class ImpactRun
    {
        public double ApproachKn, AngleDeg, Vn;
        public double Energy, KineticM22, Pianc, MaxRatio, MaxForce, Rebound;
        public int Fenders;
        public bool QuayImpact, FenderOverload;
        public double Restitution => Vn > 0 ? Rebound / Vn : double.NaN;
        public double EnergyShare => KineticM22 > 0 ? Energy / KineticM22 : double.NaN;
        /// <summary>t, с; зазор борт—кромка на миделе, м; ΣR, Н; скорость к кромке, м/с.</summary>
        public readonly List<(double t, double gap, double force, double vn)> Series = new List<(double, double, double, double)>();
        /// <summary>Наибольшее обжатие, м — ΣR, Н.</summary>
        public readonly List<(double defl, double force)> Loop = new List<(double, double)>();
    }

    public sealed class HoldRun
    {
        public bool OnBitts, Longitudinal;
        public double ForceOverload = double.NaN, ForceLimit = double.NaN;
        public string LineOverload = "", LineLimit = "", LimitKind = "";
        /// <summary>F, Н; смещение от начала, м; натяжения концов, Н.</summary>
        public readonly List<(double force, double offset, double[] tension)> Series = new List<(double, double, double[])>();
        public string Title => (Longitudinal ? "вдоль причала" : "от причала") + (OnBitts ? ", концы на кнехтах" : ", на тормозах лебёдок");
    }

    public sealed class ThrusterRun
    {
        public double SpeedKn, Force, Factor, DeltaPsi, R, V;
        public readonly List<(double t, double psiDeg)> Series = new List<(double, double)>();
    }

    public sealed class Report
    {
        public ShipParameters Parameters;
        public BerthArea Berth;
        public double Mass, M22, Mbl, Thrust;
        public readonly List<ImpactRun> Impacts = new List<ImpactRun>();
        public readonly List<ImpactRun> Angled = new List<ImpactRun>();
        public readonly List<HoldRun> Holds = new List<HoldRun>();
        public readonly List<ThrusterRun> Thrusters = new List<ThrusterRun>();
        public FenderSpec Fender => Berth.Fender;
    }

    public static readonly double[] ImpactSpeedsKn = { 0.05, 0.1, 0.2, 0.3, 0.5 };
    public static readonly double[] AnglesDeg = { 5.0, 10.0 };
    public static readonly double[] ThrusterSpeedsKn = { 0.0, 2.0, 4.0, 6.0 };

    public static Report RunAll(ShipParameters p)
    {
        BerthArea berth = BerthArea.ForShip(p);
        Manoeuvring3DofCoefficients c = Manoeuvring3DofCoefficients.For(p, null);
        var r = new Report { Parameters = p, Berth = berth, Mass = c.Mass, M22 = c.M22, Mbl = MooringLine.MblFor(p), Thrust = BowThruster.MaxThrustFor(p) };
        foreach (double v in ImpactSpeedsKn) r.Impacts.Add(Impact(p, berth, v, 0.0, Math.Abs(v - 0.2) < 1e-9));
        foreach (double a in AnglesDeg) r.Angled.Add(Impact(p, berth, 0.2, a, false));
        foreach (bool lon in new[] { false, true })
            foreach (bool bitts in new[] { false, true })
                r.Holds.Add(Hold(p, berth, bitts, lon));
        foreach (double v in ThrusterSpeedsKn) r.Thrusters.Add(Thruster(p, v));
        return r;
    }

    // ─────────── Постановка судна ───────────

    /// <summary>
    /// Судно вдоль кромки (на восток, левым бортом к причалу), середина причала; angleDeg > 0 — нос к причалу.
    /// Ближайшая к кромке точка корпуса — в gap м от лица кранцев.
    /// </summary>
    public static ShipDynamicsModel Place(ShipParameters p, BerthArea berth, double gap, double angleDeg = 0.0)
    {
        var m = new ShipDynamicsModel(p) { MotionOverride = MotionModel.Manoeuvring3Dof };
        m.Psi = (berth.BearingDeg - angleDeg) / Rad2Deg;
        if (m.Psi < 0) m.Psi += 2 * Math.PI;
        WorldPosition mid = berth.ToWorld(0.5 * berth.Length, 0.0);
        m.East = mid.East;
        m.North = mid.North;
        double minN = double.PositiveInfinity;
        foreach (var (x, y) in MooringHull.Outline(p.length, p.beam))
        {
            berth.ToLocal(MooringHull.ToWorld(m.East, m.North, m.Psi, x, y), out _, out double n);
            minN = Math.Min(minN, n);
        }
        WorldPosition na = berth.NAxis;
        double shift = berth.fenderOuterDiameterM + gap - minN;
        m.East += shift * na.East;
        m.North += shift * na.North;
        return m;
    }

    /// <summary>Скорость v (м/с) в направлении мирового вектора dir → u, V связанных осей.</summary>
    private static void SetVelocity(ShipDynamicsModel m, WorldPosition dir, double v)
    {
        double s = Math.Sin(m.Psi), c = Math.Cos(m.Psi);
        double e = dir.East * v, n = dir.North * v;
        m.U = e * s + n * c;
        m.V = e * c - n * s;
    }

    // ─────────── 1–2. Навал ───────────

    public static ImpactRun Impact(ShipParameters p, BerthArea berth, double approachKn, double angleDeg, bool record)
    {
        ShipDynamicsModel m = Place(p, berth, 0.3, angleDeg);
        var sys = new MooringSystem(berth, p);
        double vn = approachKn * Kn;
        WorldPosition na = berth.NAxis;
        SetVelocity(m, new WorldPosition(-na.East, -na.North), vn);
        Manoeuvring3DofCoefficients c = Manoeuvring3DofCoefficients.For(p, null);
        var run = new ImpactRun
        {
            ApproachKn = approachKn, AngleDeg = angleDeg, Vn = vn,
            KineticM22 = 0.5 * c.M22 * vn * vn,
            Pianc = BerthArea.PiancBerthingEnergy(c.Mass, p.draft, p.beam, vn, 1.0)
        };
        double t = 0.0, nextRec = 0.0;
        bool touched = false, impactSeen = false;
        for (int i = 0; i < (int)(180.0 / Dt); i++)
        {
            sys.Apply(m, Dt, t);
            m.Step(Dt);
            t += Dt;
            double maxDefl = 0.0;
            foreach (double d in sys.FenderDeflection) maxDefl = Math.Max(maxDefl, d);
            run.MaxForce = Math.Max(run.MaxForce, sys.TotalFenderForce);
            foreach (MooringEvent e in sys.Events)
            {
                if (e.Type == MooringEventType.BerthingImpact)
                {
                    // Под углом — несколько навалов (нос, затем корма): энергия суммируется
                    impactSeen = true;
                    run.Energy += e.Value2;
                    run.MaxRatio = Math.Max(run.MaxRatio, e.Value3);
                    run.Fenders = Math.Max(run.Fenders, e.Index);
                }
                if (e.Type == MooringEventType.QuayImpact) run.QuayImpact = true;
                if (e.Type == MooringEventType.FenderOverload) run.FenderOverload = true;
            }
            sys.Events.Clear();
            if (sys.InContact) touched = true;
            double away = sys.ApproachSpeed(m, 0.0);
            if (record && t + 1e-9 >= nextRec)
            {
                run.Series.Add((t, sys.DistanceToFace(m, 0.0), sys.TotalFenderForce, away));
                nextRec += 0.1;
            }
            if (record && touched) run.Loop.Add((maxDefl, sys.TotalFenderForce));
            // Конец: контакта нет, нос и корма отходят от кромки
            double L4 = 0.4 * p.length;
            if (touched && !sys.InContact && impactSeen && sys.ApproachSpeed(m, L4) < 0.0 && sys.ApproachSpeed(m, -L4) < 0.0)
            {
                run.Rebound = -away;   // скорость отхода миделя от кромки
                break;
            }
        }
        return run;
    }

    // ─────────── 3. Удержание на концах ───────────

    public static HoldRun Hold(ShipParameters p, BerthArea berth, bool onBitts, bool longitudinal)
    {
        ShipDynamicsModel m = Place(p, berth, 0.05);
        var sys = new MooringSystem(berth, p);
        if (p.length > 100) sys.FairleadHeightM = 5.0;
        double t = 0.0;
        for (int i = 0; i < sys.Lines.Count; i++) sys.TryGiveLine(i, m, t, out _);
        foreach (MooringLine l in sys.Lines) l.Winch = WinchCommand.HaulIn;
        for (int i = 0; i < (int)(60.0 / Dt); i++) { sys.Apply(m, Dt, t); m.Step(Dt); t += Dt; }
        foreach (MooringLine l in sys.Lines) { l.Winch = WinchCommand.Hold; l.OnBitts = onBitts; }
        for (int i = 0; i < (int)(30.0 / Dt); i++) { sys.Apply(m, Dt, t); m.Step(Dt); t += Dt; }
        sys.Events.Clear();

        var run = new HoldRun { OnBitts = onBitts, Longitudinal = longitudinal };
        WorldPosition dir = longitudinal ? berth.TAxis : berth.NAxis;
        WorldPosition start = new WorldPosition(m.East, m.North);
        double mbl = MooringLine.MblFor(p), rate = mbl / 200.0, nextRec = 0.0;
        for (int i = 0; i < (int)(600.0 / Dt); i++)
        {
            double f = rate * i * Dt;
            MooringHull.ApplyWorldForce(m, f * dir.East, f * dir.North, new WorldPosition(m.East, m.North));
            sys.Apply(m, Dt, t);
            m.Step(Dt);
            t += Dt;
            WorldPosition d = new WorldPosition(m.East, m.North) - start;
            double offset = d.East * dir.East + d.North * dir.North;
            if (i * Dt + 1e-9 >= nextRec)
            {
                run.Series.Add((f, offset, sys.Lines.ConvertAll(l => l.Tension).ToArray()));
                nextRec += 1.0;
            }
            foreach (MooringEvent e in sys.Events)
            {
                if (e.Type == MooringEventType.LineOverload && double.IsNaN(run.ForceOverload)) { run.ForceOverload = f; run.LineOverload = e.Subject; }
                if (e.Type == MooringEventType.LineBroken && double.IsNaN(run.ForceLimit)) { run.ForceLimit = f; run.LineLimit = e.Subject; run.LimitKind = "обрыв"; }
            }
            sys.Events.Clear();
            if (double.IsNaN(run.ForceLimit) && offset > 2.0)
            {
                run.ForceLimit = f;
                run.LimitKind = onBitts ? "смещение > 2 м" : "травление тормозов (смещение > 2 м)";
                MooringLine most = sys.Lines[0];
                foreach (MooringLine l in sys.Lines) if (l.Tension > most.Tension) most = l;
                run.LineLimit = most.Title();
            }
            if (!double.IsNaN(run.ForceLimit) && (run.LimitKind == "обрыв" || offset > 10.0)) break;
        }
        return run;
    }

    // ─────────── 4. Подруливающее ───────────

    public static ThrusterRun Thruster(ShipParameters p, double speedKn)
    {
        var m = new ShipDynamicsModel(p) { MotionOverride = MotionModel.Manoeuvring3Dof, U = speedKn * Kn, SpeedFactor = speedKn * Kn / p.ServiceSpeedMs };
        BowThruster th = BowThruster.For(p);
        th.Command = 1.0;
        var run = new ThrusterRun { SpeedKn = speedKn, Factor = BowThruster.SpeedFactor(speedKn * Kn) };
        double psi0 = m.Psi, t = 0.0, acc = 0.0;
        for (int i = 0; i < (int)(60.0 / Dt); i++)
        {
            th.Apply(m, Dt);
            m.Step(Dt);
            t += Dt;
            acc += m.R * Dt;
            if (i % 50 == 49) run.Series.Add((t, acc * Rad2Deg));
        }
        run.Force = th.Force;
        run.DeltaPsi = acc * Rad2Deg;
        run.R = m.R * Rad2Deg;
        run.V = m.V;
        return run;
    }
}
