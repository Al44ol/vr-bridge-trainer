// ============================================================
// LowSpeedTrials.cs — Испытания модели 3 СС на малых скоростях (запрос 19.1)
//
// Дополняют стандартные испытания IMO (ManoeuvringTrials) тем, чего Номото не умеет:
//   1. Дрейф под поперечной силой: судно без хода, сила F на миделе на правый борт
//      (≈ буксир толкает в борт) — установившаяся скорость лагом и время её набора;
//      аналитика при u = r = 0: v = √(F / (½ρ·d·C_d·L)).
//   2. Разворот на месте парой сил: F на ПБ в носу (x = 0,45L) и F на ЛБ в корме (x = −0,45L),
//      момент 0,9·F·L — установившаяся угловая скорость; аналитика при u = v = 0:
//      r = √(N / (½ρ·d·C_d·L⁴/32)) (∫x²|x|dx по длине = L⁴/32).
//   3. «Толчок» машиной: с места «Полный вперёд», руль 35° ПБ — изменение курса за 30 с
//      с обдувом руля струёй винта (k_ps ассета) и без него (k_ps = 0).
//   4. Задний ход с места: «Полный назад», руль прямо — уход носа от поперечной силы винта.
// Сила по умолчанию F = 0,002·Δ·g (порядок тяги буксира: 1,4 т у 45-метровой модели, 43 т у Mariner).
// Шаг dt и запись — как у ManoeuvringTrials (TrialSettings), модель — ShipDynamicsModel в режиме 3 СС.
//
// Сравнение моделей (CompareMotionModels): те же испытания IMO на одном ассете —
// Номото/Норрбин, 3 СС с C_d по Хёрнеру на всех скоростях, 3 СС с согласованной циркуляцией,
// по желанию — эталон Mariner (ПМУ, Chislett & Strøm-Tejsen 1965).
// ============================================================
using System;
using System.Collections.Generic;

public static class LowSpeedTrials
{
    private const double Rad2Deg = 180.0 / Math.PI;
    private const double G = 9.81;

    /// <summary>Сила испытаний по умолчанию, Н: 0,002·Δ·g.</summary>
    public static double ReferenceForce(ShipParameters p) =>
        0.002 * Manoeuvring3DofCoefficients.For(p, null).Mass * G;

    // ─────────── Прогон ───────────

    private sealed class Run
    {
        public readonly ShipDynamicsModel Model;
        public readonly TrialSettings S;
        public readonly TrialResult Result;
        public double T, DeltaPsiDeg;
        private double nextSample;

        public Run(TrialSettings s, ShipParameters p, string id, string title)
        {
            S = s;
            Model = new ShipDynamicsModel(p ?? s.Parameters) { MotionOverride = MotionModel.Manoeuvring3Dof };
            Result = new TrialResult { Id = id, Title = title };
            Record();
            nextSample = s.SampleInterval;
        }

        public void Step(Action<ShipDynamicsModel> forces = null)
        {
            forces?.Invoke(Model);
            Model.Step(S.Dt);
            T += S.Dt;
            DeltaPsiDeg += Model.R * S.Dt * Rad2Deg;
            if (T + 1e-9 >= nextSample) { Record(); nextSample += S.SampleInterval; }
        }

        public void Record() => Result.Samples.Add(new TrialSample
        {
            T = T, X = Model.East, Y = Model.North,
            HeadingDeg = Model.Psi * Rad2Deg, DeltaPsiDeg = DeltaPsiDeg,
            U = Model.U, V = Model.V, RDegS = Model.R * Rad2Deg,
            RudderOrderedDeg = Model.Ordered, RudderActualDeg = Model.Delta,
        });

        public TrialMetric Metric(string name, double value, string unit, string note = "")
        {
            var m = new TrialMetric { Name = name, Value = value, Unit = unit, Note = note };
            Result.Metrics.Add(m);
            return m;
        }

        public TrialResult Finish(bool completed = true, string message = "")
        {
            int n = Result.Samples.Count;
            if (n == 0 || Result.Samples[n - 1].T < T - 1e-9) Record();
            Result.Completed = completed;
            Result.Message = message;
            return Result;
        }
    }

    private static double Tonnes(double newton) => newton / (1000.0 * G);

    // ─────────── 1. Дрейф под поперечной силой ───────────

    public static TrialResult Drift(TrialSettings s, double force = double.NaN, double duration = 600.0)
    {
        double f = double.IsNaN(force) ? ReferenceForce(s.Parameters) : force;
        var run = new Run(s, null, "lowspeed_drift", $"Дрейф под силой {Tonnes(f):0.#} т в борт (ПБ), без хода");
        Manoeuvring3DofCoefficients c = run.Model.Coefficients3Dof;
        double vAn = c.SteadyDriftSpeed(f);
        double t90 = double.NaN, lat300 = double.NaN;
        while (run.T < duration - 1e-9)
        {
            run.Step(m => m.AddForce(0.0, f, 0.0));
            if (double.IsNaN(t90) && run.Model.V >= 0.9 * vAn) t90 = run.T;
            if (double.IsNaN(lat300) && run.T >= 300.0 - 1e-9) lat300 = Lateral(run);
        }
        run.Metric("Сила", Tonnes(f), "т", "0,002·Δ·g, на миделе");
        run.Metric("Скорость лагом в конце", run.Model.V / ManoeuvringTrials.KnotsToMs, "уз",
            $"аналитика √(F/(½ρ·d·C_d·L)) = {vAn / ManoeuvringTrials.KnotsToMs:0.###} уз, C_d = {c.CrossFlowCd:0.###}");
        run.Metric("Время до 90 % аналитической скорости лагом", t90, "с");
        run.Metric("Смещение лагом за 300 с", lat300, "м");
        run.Metric("Изменение курса", run.DeltaPsiDeg, "°", "переходный поворот от перекрёстных присоединённых масс Y'_ṙ, N'_v̇ (нос и корма несимметричны), затем курс постоянен");
        return run.Finish();
    }

    // Смещение на ПБ от начального курса 000°: восток
    private static double Lateral(Run run) => run.Model.East;

    // ─────────── 2. Разворот на месте парой сил ───────────

    public static TrialResult Spin(TrialSettings s, double force = double.NaN, double duration = 600.0)
    {
        double f = double.IsNaN(force) ? ReferenceForce(s.Parameters) : force;
        double L = Math.Max(1.0, s.Parameters.length);
        double arm = 0.45 * L;
        var run = new Run(s, null, "lowspeed_spin", $"Разворот на месте: пара сил по {Tonnes(f):0.#} т (нос вправо, корма влево)");
        Manoeuvring3DofCoefficients c = run.Model.Coefficients3Dof;
        double rAn = c.SteadySpinRate(2.0 * arm * f);
        double t90 = double.NaN, drift90 = double.NaN;
        while (run.T < duration - 1e-9)
        {
            run.Step(m => { m.AddForce(0.0, f, arm); m.AddForce(0.0, -f, -arm); });
            if (double.IsNaN(t90) && run.DeltaPsiDeg >= 90.0)
            {
                t90 = run.T;
                drift90 = Math.Sqrt(run.Model.East * run.Model.East + run.Model.North * run.Model.North);
            }
        }
        run.Metric("Сила каждого из пары", Tonnes(f), "т", $"плечо ±0,45L, момент {2.0 * arm * f / 1000.0:0} кН·м");
        run.Metric("Угловая скорость в конце", run.Model.R * Rad2Deg, "°/с",
            $"аналитика √(N/(½ρ·d·C_d·L⁴/32)) = {rAn * Rad2Deg:0.###} °/с");
        run.Metric("Время поворота на 90°", t90, "с");
        run.Metric("Смещение миделя за поворот на 90°", drift90, "м");
        return run.Finish(!double.IsNaN(t90), double.IsNaN(t90) ? "не повернуло на 90°" : "");
    }

    // ─────────── 3. «Толчок» машиной с рулём ───────────

    public static TrialResult Kick(TrialSettings s, bool slipstream, double duration = 30.0)
    {
        ShipParameters p = s.Parameters;
        ShipParameters q = p;
        if (!slipstream)
        {
            q = p.Clone();
            q.rudderSlipstreamFactor = 0f;
        }
        double rudder = Math.Min(35.0, p.maxRudderAngle);
        string id = slipstream ? "lowspeed_kick" : "lowspeed_kick_noslip";
        string title = slipstream
            ? $"«Толчок»: с места «Полный вперёд», руль {rudder:0}° ПБ, обдув k_ps = {p.rudderSlipstreamFactor:0.##}"
            : $"«Толчок»: то же без обдува руля (k_ps = 0)";
        var run = new Run(s, q, id, title);
        run.Model.SpeedFactor = 1.0;
        run.Model.RudderCommandDeg = rudder;
        while (run.T < duration - 1e-9) run.Step();
        run.Metric("Изменение курса за " + duration.ToString("0") + " с", run.DeltaPsiDeg, "°");
        run.Metric("Продвижение вперёд", run.Model.North, "м");
        run.Metric("Смещение вправо", run.Model.East, "м");
        run.Metric("Скорость в конце", run.Model.U / ManoeuvringTrials.KnotsToMs, "уз");
        return run.Finish();
    }

    // ─────────── 4. Задний ход с места ───────────

    public static TrialResult Astern(TrialSettings s, double duration = 120.0)
    {
        ShipParameters p = s.Parameters;
        var run = new Run(s, null, "lowspeed_astern",
            $"Задний ход с места: «Полный назад», руль прямо, поперечная сила винта {100.0 * p.propellerSideForceFactor:0.#} % упора");
        run.Model.SpeedFactor = -1.0;
        double psi60 = double.NaN;
        while (run.T < duration - 1e-9)
        {
            run.Step();
            if (double.IsNaN(psi60) && run.T >= 60.0 - 1e-9) psi60 = run.DeltaPsiDeg;
        }
        run.Metric("Изменение курса за 60 с", psi60, "°", "> 0 — нос вправо (корма влево)");
        run.Metric($"Изменение курса за {duration:0} с", run.DeltaPsiDeg, "°");
        run.Metric("Скорость в конце", run.Model.U / ManoeuvringTrials.KnotsToMs, "уз");
        run.Metric("Отход кормой", -run.Model.North, "м");
        return run.Finish();
    }

    public static List<TrialResult> RunAll(TrialSettings s) => new List<TrialResult>
    {
        Drift(s), Spin(s), Kick(s, true), Kick(s, false), Astern(s),
    };

    // ─────────── Сравнение моделей движения ───────────

    public sealed class Variant
    {
        public string Name;
        public List<TrialResult> Results;
    }

    /// <summary>
    /// Стандартные испытания на одном ассете в вариантах: Номото/Норрбин; 3 СС (C_d по Хёрнеру на всех скоростях);
    /// 3 СС (согласованная циркуляция); при marinerReference — эталон Mariner на тех же условиях.
    /// </summary>
    public static List<Variant> CompareMotionModels(TrialSettings s, bool marinerReference)
    {
        ShipParameters p = s.Parameters;
        ShipParameters hoerner = p.Clone();
        hoerner.matchTurningCircle = false;
        ShipParameters matched = p.Clone();
        matched.matchTurningCircle = true;

        var list = new List<Variant>
        {
            new Variant { Name = p.yawModel == YawModel.Norrbin ? "Норрбин (ассет)" : "Номото (ассет)", Results = Standard(s, p, MotionModel.Course1Dof) },
            new Variant { Name = "3 СС, C_d по Хёрнеру", Results = Standard(s, hoerner, MotionModel.Manoeuvring3Dof) },
            new Variant { Name = "3 СС, согласованная", Results = Standard(s, matched, MotionModel.Manoeuvring3Dof) },
        };
        if (marinerReference)
        {
            var rs = new TrialSettings
            {
                Parameters = p, TestSpeedMs = s.TestSpeedMs, Dt = s.Dt, MaxDuration = s.MaxDuration,
                StopThresholdKn = s.StopThresholdKn, SampleInterval = s.SampleInterval,
                ModelFactory = u0 => new MarinerModel(u0) { PropellerAsymmetry = false, MaxRudderDeg = p.maxRudderAngle },
            };
            list.Add(new Variant { Name = "Эталон Mariner (ПМУ)", Results = new List<TrialResult>
            {
                ManoeuvringTrials.Turning(rs, true), ManoeuvringTrials.InitialTurning(rs),
                ManoeuvringTrials.Zigzag(rs, 10, 10), ManoeuvringTrials.Zigzag(rs, 20, 20),
            } });
        }
        return list;
    }

    private static List<TrialResult> Standard(TrialSettings s, ShipParameters p, MotionModel motion)
    {
        var t = new TrialSettings
        {
            Parameters = p, TestSpeedMs = s.TestSpeedMs, Dt = s.Dt, MaxDuration = s.MaxDuration,
            StopThresholdKn = s.StopThresholdKn, SampleInterval = s.SampleInterval, Motion = motion,
        };
        return new List<TrialResult>
        {
            ManoeuvringTrials.Turning(t, true), ManoeuvringTrials.InitialTurning(t),
            ManoeuvringTrials.Zigzag(t, 10, 10), ManoeuvringTrials.Zigzag(t, 20, 20),
            ManoeuvringTrials.CrashStop(t),
        };
    }
}
