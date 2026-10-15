// ============================================================
// MooringSystem.cs — Причал, кранцы, концы и подруливающее в одном шаге (запрос 19.2)
//
// Вызов перед шагом модели: system.Apply(model, dt, time) → силы в ShipDynamicsModel.AddForce
// (действуют только в модели 3 СС, запрос 19.1), затем model.Step(dt). Силы считаются по
// состоянию в начале шага и «заморожены» на шаг RK4 (dt = 0,02 с; оценка устойчивости —
// ThesisNotes 19.11).
//
// Кранец i (на t_i, вылет D_н): обжатие δ_i = D_н − n_min, n_min — ближайшая к кромке точка
// корпуса в полосе кранца (5 сечений по длине кранца); реакция R(δ) с гистерезисом (FenderModel),
// трение вдоль кромки F_т = −μ·R·th(v_t/0,05 м/с). Сила — в точке контакта на корпусе.
// Кромка (без кранца): корпус зашёл за кромку (n < 0) — «удар о причал», жёсткость
// k_п = 20·R_н/δ_н, разгрузка — с гистерезисом 0,8.
// Концы (MooringLine): горизонтальная составляющая натяжения в клюзе — к кнехту.
// Подруливающее (BowThruster) — поперечная сила в носу.
//
// Навал (эпизод): начинается с первого касания после ≥ 1 с без контакта; v_n — скорость
// точки контакта к кромке в момент касания; пока кранцы обжимаются — копится работа
// W = Σ∫R_i·max(0, δ̇_i)dt (энергия, поглощённая кранцами), наибольшее δ/δ_н и ΣR; когда
// обжатие 0,3 с не растёт — событие BerthingImpact. Сравнение W с ½·m₂₂·v_n² и с PIANC —
// стенд BerthingTrials.
// ============================================================
using System;
using System.Collections.Generic;

public enum MooringEventType
{
    BerthingImpact,       // навал на кранцы: Value — v_n, м/с; Value2 — энергия W, Дж; Value3 — max δ/δ_н; Index — число кранцев
    FenderOverload,       // кранец обжат сверх номинала: Index — кранец, Value — δ/δ_н
    QuayImpact,           // корпус ударил о кромку причала: Value — v_n, м/с
    LineMadeFast,         // конец подан: Index — конец, Value — длина, м; Value2 — кнехт
    LineReleased,         // конец отдан: Index
    LineOverload,         // натяжение > 0,5·MBL: Index, Value — T, Н; Value2 — T/MBL
    LineOverloadCleared,  // натяжение < 0,45·MBL: Index
    LineBroken            // обрыв: Index, Value — T_max, Н
}

public struct MooringEvent
{
    public MooringEventType Type;
    public double Time;
    public int Index;
    public double Value, Value2, Value3;
    public string Subject;

    public string Describe()
    {
        switch (Type)
        {
            case MooringEventType.BerthingImpact:
                return $"Навал на кранцы: {ScenarioText.Num(Value / ShipParameters.KnotsToMs, 2)} уз ({ScenarioText.Num(Value, 2)} м/с), " +
                       $"энергия {ScenarioText.Num(Value2 / 1000.0, 1)} кДж, обжатие {ScenarioText.Num(Value3 * 100.0)} % номинала, кранцев {Index}";
            case MooringEventType.FenderOverload: return $"КРАНЕЦ {Index + 1} ПЕРЕГРУЖЕН: обжатие {ScenarioText.Num(Value * 100.0)} % номинала";
            case MooringEventType.QuayImpact: return $"УДАР О ПРИЧАЛ корпусом: {ScenarioText.Num(Value / ShipParameters.KnotsToMs, 2)} уз";
            case MooringEventType.LineMadeFast: return $"Подан {Subject}: {ScenarioText.Num(Value, 1)} м, кнехт {(int)Value2 + 1}";
            case MooringEventType.LineReleased: return $"Отдан {Subject}";
            case MooringEventType.LineOverload: return $"ПЕРЕГРУЗКА: {Subject}, {ScenarioText.Num(Value / 1000.0)} кН";
            case MooringEventType.LineOverloadCleared: return $"Натяжение в норме: {Subject}";
            case MooringEventType.LineBroken: return $"ОБРЫВ: {Subject}, {ScenarioText.Num(Value / 1000.0)} кН";
        }
        return Type.ToString();
    }
}

public sealed class MooringSystem
{
    public const double FrictionSpeed = 0.05, QuayStiffnessFactor = 20.0, QuayHysteresis = 0.8;
    public const double SeparationTime = 1.0, PeakHoldTime = 0.3;

    public BerthArea Berth;
    public ShipParameters Parameters;
    /// <summary>Высота клюзов над водой, м.</summary>
    public double FairleadHeightM = 2.5;
    public readonly List<MooringLine> Lines = new List<MooringLine>();
    public BowThruster Thruster;
    /// <summary>События последних шагов (вызывающий забирает и очищает).</summary>
    public readonly List<MooringEvent> Events = new List<MooringEvent>();

    // ── Состояние для индикации ──
    public double[] FenderDeflection { get; private set; } = new double[0];
    public double[] FenderReaction { get; private set; } = new double[0];
    public double TotalFenderForce { get; private set; }
    public double QuayPenetration { get; private set; }
    public bool InContact { get; private set; }

    private List<double> fenderT = new List<double>();
    private bool[] fenderOverloaded = new bool[0];
    private BerthArea builtFor;
    private Manoeuvring3DofCoefficients coeff;

    // Эпизод навала
    private enum Episode { Idle, Loading, Resting }
    private Episode episode = Episode.Idle;
    private double sinceContact = 1e9, sinceGrowth, episodeVn, episodeEnergy, episodeMaxRatio;
    private int episodeFenders;
    private bool quayReported;
    private readonly HashSet<int> episodeTouched = new HashSet<int>();

    public MooringSystem(BerthArea berth, ShipParameters p)
    {
        Berth = berth;
        Parameters = p;
        Thruster = BowThruster.For(p);
        CreateDefaultLines();
    }

    /// <summary>Шесть концов: продольные, прижимные, шпринги (клюзы и направления — доли L).</summary>
    public void CreateDefaultLines()
    {
        Lines.Clear();
        double L = Parameters.length, mbl = MooringLine.MblFor(Parameters), max = MooringLine.MaxLengthFor(Parameters);
        Lines.Add(new MooringLine(MooringLineKind.HeadLine, 0.45 * L, 0.35 * L, mbl, max));
        Lines.Add(new MooringLine(MooringLineKind.ForwardBreast, 0.38 * L, 0.0, mbl, max));
        Lines.Add(new MooringLine(MooringLineKind.ForwardSpring, 0.30 * L, -0.35 * L, mbl, max));
        Lines.Add(new MooringLine(MooringLineKind.AftSpring, -0.30 * L, 0.35 * L, mbl, max));
        Lines.Add(new MooringLine(MooringLineKind.AftBreast, -0.38 * L, 0.0, mbl, max));
        Lines.Add(new MooringLine(MooringLineKind.SternLine, -0.45 * L, -0.35 * L, mbl, max));
    }

    /// <summary>Сбросить всё: концы на борт, подруливающее 0, эпизоды.</summary>
    public void ResetState()
    {
        foreach (MooringLine l in Lines) l.Reset();
        Thruster?.Reset();
        Events.Clear();
        episode = Episode.Idle;
        sinceContact = 1e9;
        quayReported = false;
        Array.Clear(fenderOverloaded, 0, fenderOverloaded.Length);
    }

    private void Prepare()
    {
        if (builtFor == Berth && fenderT.Count == FenderDeflection.Length && fenderT.Count > 0) return;
        builtFor = Berth;
        fenderT = Berth != null ? Berth.FenderPositions() : new List<double>();
        FenderDeflection = new double[fenderT.Count];
        FenderReaction = new double[fenderT.Count];
        fenderOverloaded = new bool[fenderT.Count];
    }

    /// <summary>Кранцы вдоль кромки, t (м).</summary>
    public IReadOnlyList<double> FenderPositions { get { Prepare(); return fenderT; } }

    // ─────────── Геометрия корпуса в осях причала ───────────

    private readonly double[] hullT = new double[5], hullN = new double[5];

    private void HullLocal(ShipDynamicsModel m)
    {
        var outline = MooringHull.Outline(Parameters.length, Parameters.beam);
        for (int i = 0; i < outline.Length; i++)
        {
            WorldPosition w = MooringHull.ToWorld(m.East, m.North, m.Psi, outline[i].x, outline[i].y);
            Berth.ToLocal(w, out hullT[i], out hullN[i]);
        }
    }

    /// <summary>Наименьшее n корпуса на сечении t (NaN — сечение мимо корпуса).</summary>
    private double MinNAt(double t)
    {
        double best = double.NaN;
        int k = hullT.Length;
        for (int i = 0; i < k; i++)
        {
            double t1 = hullT[i], n1 = hullN[i], t2 = hullT[(i + 1) % k], n2 = hullN[(i + 1) % k];
            if ((t1 - t) * (t2 - t) > 0.0) continue;
            double n = Math.Abs(t2 - t1) < 1e-9 ? Math.Min(n1, n2) : n1 + (n2 - n1) * (t - t1) / (t2 - t1);
            if (double.IsNaN(best) || n < best) best = n;
        }
        return best;
    }

    /// <summary>Наименьшее n корпуса в полосе t0…t1 и где оно (t).</summary>
    private double MinNInRange(double t0, double t1, out double tAt)
    {
        double best = double.NaN;
        tAt = 0.5 * (t0 + t1);
        for (int i = 0; i < hullT.Length; i++)
            if (hullT[i] >= t0 && hullT[i] <= t1 && (double.IsNaN(best) || hullN[i] < best)) { best = hullN[i]; tAt = hullT[i]; }
        foreach (double te in new[] { t0, t1 })
        {
            double n = MinNAt(te);
            if (!double.IsNaN(n) && (double.IsNaN(best) || n < best)) { best = n; tAt = te; }
        }
        return best;
    }

    // ─────────── Шаг ───────────

    /// <summary>Силы причала, концов и подруливающего на следующий шаг модели.</summary>
    public void Apply(ShipDynamicsModel m, double dt, double time)
    {
        if (Thruster != null) Thruster.Apply(m, dt);
        if (Berth == null || Berth.Length < 1.0) return;
        Prepare();
        HullLocal(m);
        WorldPosition tAxis = Berth.TAxis, nAxis = Berth.NAxis;
        FenderSpec spec = Berth.Fender;
        double mu = Berth.fenderFriction;

        // Кранцы
        double total = 0.0, growth = 0.0, work = 0.0, maxRatio = 0.0;
        bool contact = false;
        double vnFirst = 0.0;
        for (int i = 0; i < fenderT.Count; i++)
        {
            double best = double.NaN, tBest = fenderT[i];
            for (int k = -2; k <= 2; k++)
            {
                double ts = fenderT[i] + 0.25 * k * spec.Length;
                double n = MinNAt(ts);
                if (!double.IsNaN(n) && (double.IsNaN(best) || n < best)) { best = n; tBest = ts; }
            }
            double defl = double.IsNaN(best) ? 0.0 : spec.OuterDiameter - best;
            if (defl <= 0.0)
            {
                FenderDeflection[i] = 0.0;
                FenderReaction[i] = 0.0;
                continue;
            }
            contact = true;
            WorldPosition p = Berth.ToWorld(tBest, Math.Max(0.0, best));
            WorldPosition v = MooringHull.PointVelocity(m, p);
            double rate = -(v.East * nAxis.East + v.North * nAxis.North);       // > 0 — обжимается
            double vt = v.East * tAxis.East + v.North * tAxis.North;
            double r = spec.Reaction(defl) * FenderCurve.HysteresisFactor(rate);
            double ft = -mu * r * Math.Tanh(vt / FrictionSpeed);
            MooringHull.ApplyWorldForce(m, r * nAxis.East + ft * tAxis.East, r * nAxis.North + ft * tAxis.North, p);
            FenderDeflection[i] = defl;
            FenderReaction[i] = r;
            total += r;
            growth += rate;
            if (rate > 0.0) work += r * rate * dt;
            double ratio = defl / spec.RatedDeflection;
            if (ratio > maxRatio) maxRatio = ratio;
            if (rate > vnFirst) vnFirst = rate;
            if (episode != Episode.Resting || episodeTouched.Count == 0) episodeTouched.Add(i);
            if (ratio > 1.0 && !fenderOverloaded[i])
            {
                fenderOverloaded[i] = true;
                Events.Add(new MooringEvent { Type = MooringEventType.FenderOverload, Time = time, Index = i, Value = ratio });
            }
            else if (ratio < 0.9) fenderOverloaded[i] = false;
        }
        TotalFenderForce = total;

        // Кромка причала
        double nq = MinNInRange(0.0, Berth.Length, out double tq);
        QuayPenetration = !double.IsNaN(nq) && nq < 0.0 ? -nq : 0.0;
        if (QuayPenetration > 0.0)
        {
            contact = true;
            WorldPosition p = Berth.ToWorld(tq, 0.0);
            WorldPosition v = MooringHull.PointVelocity(m, p);
            double rate = -(v.East * nAxis.East + v.North * nAxis.North);
            double f = QuayStiffnessFactor * spec.RatedStiffness * QuayPenetration * (rate >= 0.0 ? 1.0 : 1.0 - QuayHysteresis);
            double ft = -mu * f * Math.Tanh((v.East * tAxis.East + v.North * tAxis.North) / FrictionSpeed);
            MooringHull.ApplyWorldForce(m, f * nAxis.East + ft * tAxis.East, f * nAxis.North + ft * tAxis.North, p);
            if (!quayReported && rate > 0.0)
            {
                quayReported = true;
                Events.Add(new MooringEvent { Type = MooringEventType.QuayImpact, Time = time, Value = rate });
            }
        }
        else if (QuayPenetration == 0.0 && (double.IsNaN(nq) || nq > 0.1)) quayReported = false;
        InContact = contact;

        TrackEpisode(contact, growth, work, maxRatio, vnFirst, dt, time);

        // Концы
        double dz = FairleadHeightM - Berth.quayHeightM;
        coeff = Manoeuvring3DofCoefficients.For(Parameters, coeff);   // кэш: сравнение ключа без строк
        double swayMass = coeff.M22;
        for (int i = 0; i < Lines.Count; i++)
        {
            MooringLine line = Lines[i];
            if (line.State != MooringLineState.Fast) continue;
            WorldPosition fl = Fairlead(m, line);
            WorldPosition bp = Berth.BollardWorld(line.Bollard);
            double hE = bp.East - fl.East, hN = bp.North - fl.North;
            double chord = Math.Sqrt(hE * hE + hN * hN + dz * dz);
            MooringLineEvent ev = line.Update(chord, dt, swayMass);
            if (line.Tension > 0.0)
                MooringHull.ApplyWorldForce(m, line.Tension * hE / chord, line.Tension * hN / chord, fl);
            switch (ev)
            {
                case MooringLineEvent.Overload:
                    Events.Add(new MooringEvent { Type = MooringEventType.LineOverload, Time = time, Index = i, Value = line.Tension, Value2 = line.Tension / line.Mbl, Subject = line.Title() });
                    break;
                case MooringLineEvent.OverloadCleared:
                    Events.Add(new MooringEvent { Type = MooringEventType.LineOverloadCleared, Time = time, Index = i, Subject = line.Title() });
                    break;
                case MooringLineEvent.Broken:
                    Events.Add(new MooringEvent { Type = MooringEventType.LineBroken, Time = time, Index = i, Value = line.MaxTension, Subject = line.Title() });
                    break;
            }
        }
    }

    private void TrackEpisode(bool contact, double growth, double work, double maxRatio, double vn, double dt, double time)
    {
        if (!contact)
        {
            sinceContact += dt;
            if (episode == Episode.Loading) FinishEpisode(time);
            if (sinceContact >= SeparationTime) { episode = Episode.Idle; episodeTouched.Clear(); }
            return;
        }
        if (episode == Episode.Idle)
        {
            episode = Episode.Loading;
            episodeVn = vn;
            episodeEnergy = 0.0;
            episodeMaxRatio = 0.0;
            sinceGrowth = 0.0;
        }
        sinceContact = 0.0;
        if (episode != Episode.Loading) return;
        if (vn > episodeVn) episodeVn = vn;
        episodeEnergy += work;
        if (maxRatio > episodeMaxRatio) episodeMaxRatio = maxRatio;
        sinceGrowth = growth > 0.0 ? 0.0 : sinceGrowth + dt;
        if (sinceGrowth >= PeakHoldTime) FinishEpisode(time);
    }

    private void FinishEpisode(double time)
    {
        episode = Episode.Resting;
        episodeFenders = episodeTouched.Count;
        Events.Add(new MooringEvent
        {
            Type = MooringEventType.BerthingImpact, Time = time, Index = episodeFenders,
            Value = episodeVn, Value2 = episodeEnergy, Value3 = episodeMaxRatio
        });
    }

    // ─────────── Концы ───────────

    /// <summary>Борт судна к причалу: +1 — правый, −1 — левый.</summary>
    public int SideToBerth(ShipDynamicsModel m)
    {
        WorldPosition n = Berth.NAxis;
        double sE = Math.Cos(m.Psi), sN = -Math.Sin(m.Psi);            // на правый борт
        return -(sE * n.East + sN * n.North) >= 0.0 ? 1 : -1;
    }

    /// <summary>Клюз конца (борт — к причалу, если конец на борту; иначе — тот, на который подан).</summary>
    public WorldPosition Fairlead(ShipDynamicsModel m, MooringLine line)
    {
        int side = line.State == MooringLineState.Fast ? line.Side : SideToBerth(m);
        double y = side * MooringHull.HalfBeamAt(line.FairleadX, Parameters.length, Parameters.beam);
        return MooringHull.ToWorld(m.East, m.North, m.Psi, line.FairleadX, y);
    }

    /// <summary>Подать конец i: ближайший к направлению конца кнехт в пределах L_max. false — reason.</summary>
    public bool TryGiveLine(int i, ShipDynamicsModel m, double time, out string reason)
    {
        reason = "";
        if (i < 0 || i >= Lines.Count || Berth == null) { reason = "нет конца или причала"; return false; }
        MooringLine line = Lines[i];
        if (line.State == MooringLineState.Fast) { reason = "уже подан"; return false; }
        int side = SideToBerth(m);
        double y = side * MooringHull.HalfBeamAt(line.FairleadX, Parameters.length, Parameters.beam);
        WorldPosition fl = MooringHull.ToWorld(m.East, m.North, m.Psi, line.FairleadX, y);
        WorldPosition target = MooringHull.ToWorld(m.East, m.North, m.Psi, line.FairleadX + line.LeadAlong, y);
        Berth.ToLocal(target, out double tt, out _);
        int bollard = Berth.NearestBollard(tt);
        WorldPosition bp = Berth.BollardWorld(bollard);
        double dz = FairleadHeightM - Berth.quayHeightM;
        double chord = Math.Sqrt((bp - fl).Magnitude * (bp - fl).Magnitude + dz * dz);
        if (chord > line.MaxLength)
        {
            reason = $"до кнехта {ScenarioText.Num(chord)} м — больше {ScenarioText.Num(line.MaxLength)} м";
            return false;
        }
        line.MakeFast(bollard, side, chord);
        Events.Add(new MooringEvent { Type = MooringEventType.LineMadeFast, Time = time, Index = i, Value = chord, Value2 = bollard, Subject = line.Title() });
        return true;
    }

    public void ReleaseLine(int i, double time)
    {
        if (i < 0 || i >= Lines.Count || Lines[i].State != MooringLineState.Fast) return;
        Lines[i].Release();
        Events.Add(new MooringEvent { Type = MooringEventType.LineReleased, Time = time, Index = i, Subject = Lines[i].Title() });
    }

    /// <summary>
    /// 19.2б: команда концу (меню «Управление» → «Концы»). Подать — на кнехт (TryGiveLine; оборванный
    /// подаётся заново — запасной конец); выбирать / травить / держать — лебёдка (снимает с кнехтов судна);
    /// на кнехты — закрепить на кнехтах судна (лебёдка не работает, тормоз не травит); отдать.
    /// false — команда не выполнена, reason — почему.
    /// </summary>
    public bool Order(int i, LineOrder order, ShipDynamicsModel m, double time, out string reason)
    {
        reason = "";
        if (i < 0 || i >= Lines.Count) { reason = "нет такого конца"; return false; }
        MooringLine line = Lines[i];
        if (order == LineOrder.Give) return TryGiveLine(i, m, time, out reason);
        if (line.State != MooringLineState.Fast) { reason = line.State == MooringLineState.Broken ? "конец оборван" : "конец не подан"; return false; }
        switch (order)
        {
            case LineOrder.HaulIn: line.Winch = WinchCommand.HaulIn; line.OnBitts = false; return true;
            case LineOrder.PayOut: line.Winch = WinchCommand.PayOut; line.OnBitts = false; return true;
            case LineOrder.Hold: line.Winch = WinchCommand.Hold; line.OnBitts = false; return true;
            case LineOrder.OnBitts: line.Winch = WinchCommand.Hold; line.OnBitts = true; return true;
            case LineOrder.Release: ReleaseLine(i, time); return true;
        }
        reason = "неизвестная команда";
        return false;
    }

    // ─────────── Индикация ───────────

    /// <summary>Расстояние от борта (сторона к причалу) в точке x до кромки, м (по нормали; < 0 — за кромкой).</summary>
    public double DistanceToFace(ShipDynamicsModel m, double x)
    {
        int side = SideToBerth(m);
        double y = side * MooringHull.HalfBeamAt(x, Parameters.length, Parameters.beam);
        Berth.ToLocal(MooringHull.ToWorld(m.East, m.North, m.Psi, x, y), out _, out double n);
        return n;
    }

    /// <summary>Скорость точки борта x к кромке, м/с (> 0 — приближается).</summary>
    public double ApproachSpeed(ShipDynamicsModel m, double x)
    {
        int side = SideToBerth(m);
        double y = side * MooringHull.HalfBeamAt(x, Parameters.length, Parameters.beam);
        WorldPosition v = MooringHull.PointVelocity(m, MooringHull.ToWorld(m.East, m.North, m.Psi, x, y));
        WorldPosition n = Berth.NAxis;
        return -(v.East * n.East + v.North * n.North);
    }

    /// <summary>Угол между диаметральной плоскостью и кромкой, градусы (0 — параллельно), −90…90.</summary>
    public double AngleToBerthDeg(ShipDynamicsModel m)
    {
        double d = m.Psi * 180.0 / Math.PI - Berth.BearingDeg;
        d = ((d % 180.0) + 180.0) % 180.0;
        return d > 90.0 ? d - 180.0 : d;
    }
}
