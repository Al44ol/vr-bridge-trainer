// ============================================================
// ShipCalibration.cs — Идентификация параметров модели тренажёра (A3.2)
//
// Метод выходной ошибки (output error): моделируем манёвр с кандидатными
// параметрами и минимизируем среднеквадратичное расхождение с эталоном
// (Нелдер–Мид, NelderMead.cs). Подбор не ручной: целевая функция и
// этапы фиксированы в коде, результат воспроизводим (тест-регрессия).
//
// Mariner (эталон — нелинейная модель MarinerModel, записи по времени):
//   0) τ_u — аналитически из X'_u (MarinerModel.SurgeTimeConstant);
//   1) C_r' — по u(t) эталонных циркуляций при эталонном r(t):
//        u(t+dt) = u* + (u − u*)·e^(−dt/τ_u), u* = U0 − C_r'·L·r²·τ_u;
//   2) K', T' (Номото) и K', T', α' (Норрбин) — по ψ(t) зигзагов и r(t), u(t)
//      циркуляций; модель получает тот же фактический угол руля δ(t), что
//      эталон (повтор руля), поэтому подбирается корпус, а не рулевая машина:
//        J = Σ_зигзаги (RMS Δψ / ψ_зад)² + Σ_циркуляции [(RMS r / r_уст)² + (RMS u / Δu_уст)²].
//   Этапы: Номото только по 10/10; только по циркуляции 35°; по всем манёврам;
//   Норрбин по всем манёврам; + линеаризация Mariner без подбора (для сравнения).
//
// KCS (эталон — опубликованные показатели EFD, без записей по времени):
//   J = Σ ((показатель модели − показатель EFD) / показатель EFD)²
//   по KcsEfdData.Targets (UseInFit), испытания стенда с рулевой машиной тренажёра.
// ============================================================
using System;
using System.Collections.Generic;

/// <summary>Один набор параметров (этап идентификации) и его результаты.</summary>
public sealed class CalibrationStage
{
    public string Id, Title, Description;
    public ShipParameters Parameters;
    public double Cost = double.NaN;
    public int Evaluations;
    /// <summary>RMS-ошибки повтора руля по манёврам (только Mariner): id → (Δψ °, r °/с, u м/с).</summary>
    public readonly Dictionary<string, (double psiDeg, double rDegS, double uMs)> ReplayErrors =
        new Dictionary<string, (double, double, double)>();
    /// <summary>Повтор руля эталона (только Mariner).</summary>
    public readonly List<TrialResult> Replays = new List<TrialResult>();
    /// <summary>Испытания стенда с рулевой машиной тренажёра.</summary>
    public readonly List<TrialResult> Trials = new List<TrialResult>();
}

/// <summary>Строка сравнения «источник / модель / погрешность».</summary>
public sealed class ComparisonRow
{
    public string TrialId, TrialTitle, Metric, Unit, Source;
    public bool InL, UseInFit = true;
    public double Reference;
    /// <summary>Значение по каждому этапу (в тех же единицах, что Reference).</summary>
    public double[] Values;
    public double ErrorPercent(int stage) => 100.0 * (Values[stage] - Reference) / Reference;
}

public sealed class CalibrationResult
{
    public string Ship, Title;
    public double TestSpeedMs, Dt;
    public ShipParameters Base;
    /// <summary>Эталонные испытания (Mariner — модель; KCS — пусто).</summary>
    public readonly List<TrialResult> Reference = new List<TrialResult>();
    public readonly List<CalibrationStage> Stages = new List<CalibrationStage>();
    public readonly List<ComparisonRow> Rows = new List<ComparisonRow>();
    /// <summary>Пояснения и ограничения для отчёта.</summary>
    public readonly List<string> Notes = new List<string>();
    /// <summary>Индекс этапа, рекомендуемого для ассета.</summary>
    public int Recommended;
}

public static class ShipCalibration
{
    private const double Rad2Deg = 180.0 / Math.PI;

    // ─────────── Общие процедуры ───────────

    /// <summary>Набор стандартных испытаний для сравнения показателей.</summary>
    public static List<TrialResult> StandardTrials(TrialSettings s, bool withSmallRudderTurns)
    {
        var list = new List<TrialResult>
        {
            ManoeuvringTrials.Turning(s, true),
            ManoeuvringTrials.InitialTurning(s),
            ManoeuvringTrials.Zigzag(s, 10, 10),
            ManoeuvringTrials.Zigzag(s, 20, 20),
        };
        if (withSmallRudderTurns)
        {
            list.Insert(1, ManoeuvringTrials.Turning(s, true, 20));
            list.Insert(2, ManoeuvringTrials.Turning(s, true, 10));
        }
        return list;
    }

    private static TrialMetric FindMetric(List<TrialResult> trials, string trialId, string metric)
    {
        TrialResult r = trials.Find(t => t.Id == trialId);
        return r?.Metrics.Find(m => m.Name == metric);
    }

    private static double MetricValue(List<TrialResult> trials, string trialId, string metric, bool inL)
    {
        TrialMetric m = FindMetric(trials, trialId, metric);
        if (m == null) return double.NaN;
        return inL ? m.ValueInL : m.Value;
    }

    /// <summary>Параметры тренажёра → вектор z для оптимизатора (логарифмы: K', T', α' > 0).</summary>
    // Рабочие экземпляры: при подборе тысячи вычислений J — не создавать ScriptableObject на каждое
    private static ShipParameters scratchFit, scratchReplay;
    private static ShipParameters Scratch(ref ShipParameters s) =>
        s != null ? s : (s = UnityEngine.ScriptableObject.CreateInstance<ShipParameters>());

    /// <param name="target">null — новый экземпляр (результат этапа), иначе — запись в рабочий.</param>
    private static ShipParameters FromVector(ShipParameters basis, double[] z, bool norrbin, ShipParameters target = null)
    {
        ShipParameters p = target ?? basis.Clone();
        if (target != null) p.CopyFrom(basis);
        p.nomotoKPrime = (float)Math.Exp(z[0]);
        p.nomotoTPrime = (float)Math.Exp(z[1]);
        p.yawModel = norrbin ? YawModel.Norrbin : YawModel.Nomoto;
        p.norrbinAlphaPrime = norrbin ? (float)Math.Exp(z[2]) : 0f;
        return p;
    }

    /// <summary>Грубая сетка для начальной точки, затем Нелдер–Мид.</summary>
    private static NelderMeadResult Fit(Func<ShipParameters, double> cost, ShipParameters basis, bool norrbin)
    {
        double[] kGrid = { 0.3, 0.6, 1.2, 2.4, 4.8 };
        double[] tGrid = { 0.5, 1.0, 2.0, 4.0, 8.0 };
        double[] aGrid = norrbin ? new[] { 0.3, 1.0, 3.0, 10.0, 30.0 } : new[] { 0.0 };
        double best = double.MaxValue;
        double[] z0 = null;
        int evals = 0;
        foreach (double k in kGrid)
        foreach (double t in tGrid)
        foreach (double a in aGrid)
        {
            double[] z = norrbin ? new[] { Math.Log(k), Math.Log(t), Math.Log(a) } : new[] { Math.Log(k), Math.Log(t) };
            double f = cost(FromVector(basis, z, norrbin, Scratch(ref scratchFit)));
            evals++;
            if (f < best) { best = f; z0 = z; }
        }
        double[] step = norrbin ? new[] { 0.3, 0.3, 0.5 } : new[] { 0.3, 0.3 };
        NelderMeadResult res = NelderMead.Minimize(z => cost(FromVector(basis, z, norrbin, Scratch(ref scratchFit))), z0, step, 1500, 1e-9);
        res.Evaluations += evals;
        return res;
    }

    // ─────────── Повтор руля эталона ───────────

    /// <summary>
    /// Модель тренажёра с фактическим углом руля эталона δ(t) на каждом шаге
    /// (рулевая машина без ограничения скорости). Записи эталона — с шагом dt.
    /// </summary>
    public static TrialResult Replay(ShipParameters p, TrialResult reference, double dt)
    {
        ShipParameters q = Scratch(ref scratchReplay);
        q.CopyFrom(p);
        q.rudderRate = 1e6f;
        q.maxRudderAngle = 90f;
        List<TrialSample> refS = reference.Samples;
        double u0 = refS[0].U;
        var m = new ShipDynamicsModel(q)
        {
            U = u0,
            SpeedFactor = ShipDynamicsModel.Clamp(u0 / Math.Max(1e-6, q.ServiceSpeedMs), 0.0, 1.0),
            Delta = refS[0].RudderActualDeg,
        };
        var res = new TrialResult { Id = reference.Id, Title = reference.Title + " — повтор руля", Completed = true };
        double dpsi = 0;
        res.Samples.Add(Sample(0, m, dpsi));
        for (int k = 1; k < refS.Count; k++)
        {
            m.RudderCommandDeg = refS[k].RudderActualDeg;
            m.Step(dt);
            dpsi += m.R * dt * Rad2Deg;
            res.Samples.Add(Sample(refS[k].T, m, dpsi));
        }
        return res;
    }

    private static TrialSample Sample(double t, ShipDynamicsModel m, double dpsi) => new TrialSample
    {
        T = t, X = m.East, Y = m.North, HeadingDeg = m.Psi * Rad2Deg, DeltaPsiDeg = dpsi,
        U = m.U, RDegS = m.R * Rad2Deg, RudderOrderedDeg = m.Ordered, RudderActualDeg = m.Delta,
    };

    private static double Rms(List<TrialSample> a, List<TrialSample> b, Func<TrialSample, double> f)
    {
        int n = Math.Min(a.Count, b.Count);
        double s = 0;
        for (int i = 0; i < n; i++) { double d = f(a[i]) - f(b[i]); s += d * d; }
        return n > 0 ? Math.Sqrt(s / n) : double.NaN;
    }

    /// <summary>Нормированные ошибки повтора по одному манёвру (вклад в J).</summary>
    private static double ReplayCost(TrialResult reference, TrialResult replay, out (double psi, double r, double u) rms)
    {
        List<TrialSample> a = reference.Samples, b = replay.Samples;
        rms = (Rms(a, b, s => s.DeltaPsiDeg), Rms(a, b, s => s.RDegS), Rms(a, b, s => s.U));
        TrialSample end = a[a.Count - 1];
        if (reference.Id.StartsWith("zigzag"))
        {
            double check = reference.Id == "zigzag_10_10" ? 10.0 : 20.0;
            return (rms.psi / check) * (rms.psi / check);
        }
        double rScale = Math.Max(1e-6, Math.Abs(end.RDegS));
        double uScale = Math.Max(1e-3, a[0].U - end.U);
        return (rms.r / rScale) * (rms.r / rScale) + (rms.u / uScale) * (rms.u / uScale);
    }

    /// <summary>J повтора руля по набору эталонных манёвров (записи с шагом dt).</summary>
    public static double ReplayCostTotal(ShipParameters p, List<TrialResult> set, double dt)
    {
        double j = 0;
        foreach (TrialResult r in set) j += ReplayCost(r, Replay(p, r, dt), out _);
        return j;
    }

    /// <summary>
    /// Подбор K', T' (и α' для Норрбина) по повтору руля эталона: сетка + Нелдер–Мид.
    /// Остальные параметры (L, скорость, C_r', τ) берутся из basis.
    /// </summary>
    public static ShipParameters FitReplay(ShipParameters basis, List<TrialResult> set, bool norrbin, double dt,
                                           out double cost, out int evaluations)
    {
        NelderMeadResult r = Fit(p => ReplayCostTotal(p, set, dt), basis, norrbin);
        cost = r.F;
        evaluations = r.Evaluations;
        return FromVector(basis, r.X, norrbin);
    }

    // ─────────── Mariner ───────────

    /// <summary>Параметры тренажёра для Mariner до подбора K', T', α' (источники — в ShipParameters_Sources.md).</summary>
    public static ShipParameters MarinerBase()
    {
        var p = UnityEngine.ScriptableObject.CreateInstance<ShipParameters>();
        p.name = "ShipParameters_Mariner";
        p.length = (float)MarinerModel.L;                                       // mariner.m
        p.serviceSpeedKnots = (float)(MarinerModel.NominalSpeed / ShipParameters.KnotsToMs); // U0 mariner.m
        p.accelerationTimeConstant = (float)MarinerModel.SurgeTimeConstant();   // из X'_u, m'₁₁
        p.decelerationTimeConstant = 150f;                                      // ЗАГЛУШКА (нет данных)
        p.asternFactor = 0.6f;                                                  // ЗАГЛУШКА (нет данных)
        p.minCalcSpeed = 0.05f;
        p.maxRudderAngle = 35f;                                                 // MSC.137(76), циркуляция 35°
        p.rudderRate = 5f;                                                      // mariner.m Ddelta_max
        p.nomotoKPrime = 1f; p.nomotoTPrime = 2f;
        return p;
    }

    /// <summary>Фактор эталона: Mariner без асимметрии винта (тренажёрная модель симметрична).</summary>
    public static Func<double, IVesselModel> MarinerFactory(bool asymmetry) =>
        u0 => new MarinerModel(u0) { PropellerAsymmetry = asymmetry };

    public static CalibrationResult CalibrateMariner(double dt = 0.02)
    {
        var res = new CalibrationResult
        {
            Ship = "Mariner",
            Title = "Сухогруз класса Mariner (USS Compass Island), L = 160,93 м",
            TestSpeedMs = MarinerModel.NominalSpeed,
            Dt = dt,
            Base = MarinerBase(),
        };
        ShipParameters basis = res.Base;

        // Эталонные манёвры с записью на каждом шаге
        var refSettings = new TrialSettings
        {
            TestSpeedMs = MarinerModel.NominalSpeed, Dt = dt, MaxDuration = 3000, SampleInterval = dt,
            ModelFactory = MarinerFactory(false),
        };
        res.Reference.AddRange(StandardTrials(refSettings, true));
        List<TrialResult> fitSet = res.Reference.FindAll(r => r.Id.StartsWith("turning") || r.Id.StartsWith("zigzag"));
        List<TrialResult> turns = fitSet.FindAll(r => r.Id.StartsWith("turning"));

        // 1) Потеря скорости: C_r' по u(t) при эталонном r(t)
        double tauU = basis.accelerationTimeConstant;
        double CostSpeed(double c)
        {
            double j = 0;
            foreach (TrialResult t in turns)
            {
                List<TrialSample> s = t.Samples;
                double u = s[0].U, u0 = s[0].U, err = 0;
                for (int k = 1; k < s.Count; k++)
                {
                    double r = s[k - 1].RDegS / Rad2Deg;
                    double uT = u0 - c * basis.length * r * r * tauU;
                    u = uT + (u - uT) * Math.Exp(-dt / tauU);
                    double d = u - s[k].U;
                    err += d * d;
                }
                double scale = Math.Max(1e-3, u0 - s[s.Count - 1].U);
                j += err / s.Count / (scale * scale);
            }
            return j;
        }
        NelderMeadResult cr = NelderMead.Minimize(z => CostSpeed(Math.Exp(z[0])), new[] { Math.Log(0.05) }, new[] { 0.5 }, 400, 1e-12);
        basis.turnSpeedLossCoeff = (float)Math.Exp(cr.X[0]);

        // 2) Поворот
        double Cost(ShipParameters p, List<TrialResult> set) => ReplayCostTotal(p, set, dt);
        TrialResult zz10 = fitSet.Find(r => r.Id == "zigzag_10_10");
        TrialResult tc35 = fitSet.Find(r => r.Id == "turning_35_stbd");

        MarinerModel.LinearNomoto(out double kLin, out double tLin, out _, out _, out _);
        ShipParameters linear = basis.Clone();
        linear.nomotoKPrime = (float)kLin; linear.nomotoTPrime = (float)tLin;
        AddStage(res, "linear", "Номото: линеаризация Mariner (без подбора)",
            $"K', T' из линейных производных Mariner при U0 (T' = T1' + T2' − T3')", linear, Cost(linear, fitSet), 0);

        void FitStage(string id, string title, string descr, List<TrialResult> set, bool norrbin)
        {
            ShipParameters p0 = FitReplay(basis, set, norrbin, dt, out _, out int evals);
            AddStage(res, id, title, descr, p0, Cost(p0, fitSet), evals);
        }
        FitStage("nomoto_zz10", "Номото по зигзагу 10°/10°", "подбор K', T' только по зигзагу 10°/10°", new List<TrialResult> { zz10 }, false);
        FitStage("nomoto_tc35", "Номото по циркуляции 35°", "подбор K', T' только по циркуляции 35°", new List<TrialResult> { tc35 }, false);
        FitStage("nomoto_all", "Номото по всем манёврам", "подбор K', T' по зигзагам 10°/10°, 20°/20° и циркуляциям 10°, 20°, 35°", fitSet, false);
        FitStage("norrbin_all", "Норрбин по всем манёврам", "подбор K', T', α' по тем же манёврам", fitSet, true);
        res.Recommended = res.Stages.Count - 1;

        // Ошибки повтора и испытания стенда (рулевая машина тренажёра: 5 °/с, δ ≤ 35°)
        foreach (CalibrationStage st in res.Stages)
        {
            foreach (TrialResult r in fitSet)
            {
                TrialResult rep = Replay(st.Parameters, r, dt);
                ReplayCost(r, rep, out var e);
                st.ReplayErrors[r.Id] = (e.psi, e.r, e.u);
                st.Replays.Add(rep);
            }
            var s = new TrialSettings { Parameters = st.Parameters, TestSpeedMs = MarinerModel.NominalSpeed, Dt = dt, MaxDuration = 3000, SampleInterval = 0.5 };
            st.Trials.AddRange(StandardTrials(s, true));
        }

        // Таблица сравнения по показателям стенда (одинаковые определения у эталона и модели)
        foreach (TrialResult r in res.Reference)
        foreach (TrialMetric m in r.Metrics)
        {
            if (m.Name == "L/V" || double.IsNaN(m.Value)) continue;
            bool inL = !double.IsNaN(m.LengthForL);
            var row = new ComparisonRow
            {
                TrialId = r.Id, TrialTitle = r.Title, Metric = m.Name, InL = inL, Unit = inL ? "L" : m.Unit,
                Reference = inL ? m.ValueInL : m.Value,
                Source = "MarinerModel (Chislett & Strøm-Tejsen 1965; MSS mariner.m), без асимметрии винта",
                Values = new double[res.Stages.Count],
            };
            for (int i = 0; i < res.Stages.Count; i++) row.Values[i] = MetricValue(res.Stages[i].Trials, r.Id, m.Name, inL);
            res.Rows.Add(row);
        }

        // Асимметрия винта эталона (справочно)
        var asym = new TrialSettings { TestSpeedMs = MarinerModel.NominalSpeed, Dt = dt, MaxDuration = 3000, SampleInterval = 0.5, ModelFactory = MarinerFactory(true) };
        TrialResult aS = ManoeuvringTrials.Turning(asym, true), aP = ManoeuvringTrials.Turning(asym, false);
        double td(TrialResult t) => t.Metrics.Find(x => x.Name == "Тактический диаметр").ValueInL;
        res.Notes.Add($"Асимметрия винта в эталоне (Y'_0, N'_0): тактический диаметр 35° на ПБ {td(aS):F2}L, на ЛБ {td(aP):F2}L; " +
                      $"для идентификации члены выключены (модель тренажёра симметрична), без них {td(tc35):F2}L.");
        res.Notes.Add($"τ_u = m'₁₁·L/(−X'_u·U0) = {tauU:F1} с (постоянная продольного движения при n = const) → τ_разг; " +
                      "τ_торм, k_зх — заглушки: у эталона нет модели винта и заднего хода.");
        res.Notes.Add($"C_r' = {basis.turnSpeedLossCoeff:G4} — по u(t) эталонных циркуляций 10°, 20°, 35°.");
        res.Notes.Add($"Линеаризация Mariner: K' = {kLin:F3}, T' = {tLin:F3}.");
        return res;
    }

    private static void AddStage(CalibrationResult res, string id, string title, string descr, ShipParameters p, double cost, int evals)
    {
        p.name = res.Base.name;
        res.Stages.Add(new CalibrationStage { Id = id, Title = title, Description = descr, Parameters = p, Cost = cost, Evaluations = evals });
    }

    // ─────────── KCS ───────────

    public static ShipParameters KcsBase()
    {
        var p = UnityEngine.ScriptableObject.CreateInstance<ShipParameters>();
        p.name = "ShipParameters_KCS";
        p.length = (float)KcsEfdData.Lpp;
        p.serviceSpeedKnots = (float)(KcsEfdData.SpeedMs / ShipParameters.KnotsToMs);   // Fn = 0,26
        p.accelerationTimeConstant = 90f;   // ЗАГЛУШКА
        p.decelerationTimeConstant = 150f;  // ЗАГЛУШКА
        p.asternFactor = 0.6f;              // ЗАГЛУШКА
        p.minCalcSpeed = 0.05f;
        p.maxRudderAngle = (float)KcsEfdData.RudderAngleDeg;
        p.rudderRate = (float)KcsEfdData.RudderRateDegS;
        p.turnSpeedLossCoeff = 0f;          // нет данных о скорости на циркуляции
        p.nomotoKPrime = 1f; p.nomotoTPrime = 2f;
        return p;
    }

    public static CalibrationResult CalibrateKcs(double dt = 0.02)
    {
        var res = new CalibrationResult
        {
            Ship = "KCS",
            Title = "Контейнеровоз KCS, L = 230 м (EFD MARIN, масштаб 1:37,89)",
            TestSpeedMs = KcsEfdData.SpeedMs,
            Dt = dt,
            Base = KcsBase(),
        };
        List<SourceValue> targets = KcsEfdData.Targets();
        List<SourceValue> fitTargets = targets.FindAll(t => t.UseInFit);

        List<TrialResult> Trials(ShipParameters p, double sample)
        {
            var s = new TrialSettings { Parameters = p, TestSpeedMs = res.TestSpeedMs, Dt = dt, MaxDuration = 3000, SampleInterval = sample };
            return StandardTrials(s, false);
        }
        double Cost(ShipParameters p)
        {
            List<TrialResult> tr = Trials(p, 1e9);
            double j = 0;
            foreach (SourceValue t in fitTargets)
            {
                double v = MetricValue(tr, t.TrialId, t.Metric, t.InL);
                if (double.IsNaN(v)) return 1e6;
                double e = (v - t.Value) / t.Value;
                j += e * e;
            }
            return j;
        }

        foreach (bool norrbin in new[] { false, true })
        {
            NelderMeadResult r = Fit(Cost, res.Base, norrbin);
            ShipParameters p = FromVector(res.Base, r.X, norrbin);
            AddStage(res, norrbin ? "norrbin" : "nomoto", norrbin ? "Норрбин" : "Номото",
                norrbin ? "подбор K', T', α' по показателям EFD" : "подбор K', T' по показателям EFD", p, Cost(p), r.Evaluations);
        }
        res.Recommended = res.Stages.Count - 1;
        foreach (CalibrationStage st in res.Stages) st.Trials.AddRange(Trials(st.Parameters, 0.5));

        foreach (SourceValue t in targets)
        {
            var row = new ComparisonRow
            {
                TrialId = t.TrialId, Metric = t.Metric, InL = t.InL, Unit = t.Unit, Reference = t.Value,
                Source = t.Source, UseInFit = t.UseInFit, Values = new double[res.Stages.Count],
            };
            for (int i = 0; i < res.Stages.Count; i++)
            {
                row.Values[i] = MetricValue(res.Stages[i].Trials, t.TrialId, t.Metric, t.InL);
                row.TrialTitle = res.Stages[i].Trials.Find(x => x.Id == t.TrialId)?.Title ?? t.TrialId;
            }
            res.Rows.Add(row);
        }
        res.Notes.Add("Эталон — показатели свободноходной модели (EFD MARIN), записи по времени в открытом доступе нет.");
        res.Notes.Add("Потеря скорости на циркуляции не подбиралась (C_r' = 0): числовых данных о скорости в источниках нет.");
        res.Notes.Add("τ_разг, τ_торм, k_зх — заглушки: торможение и разгон KCS в источниках не приводятся.");
        return res;
    }
}
