// ============================================================
// CalibrationTests.cs — EditMode-тесты калибровки (A3.2), класс ShipCalibrationTests
// (B1: файл переименован из ShipCalibrationTests.cs — Unity не включал его в сборку тестов)
// Window → General → Test Runner → EditMode → Run All.
//
// Расширения модели (Норрбин, потеря скорости), перенос эталона Mariner,
// оптимизатор, идентификация на синтетических данных и регрессия результата.
// ============================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class ShipCalibrationTests
{
    private const double Dt = 0.02;
    private const double Rad2Deg = 180.0 / Math.PI;

    private static ShipParameters MakeDefault()
    {
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        p.length = 150f; p.serviceSpeedKnots = 15f;
        p.accelerationTimeConstant = 90f; p.decelerationTimeConstant = 150f; p.asternFactor = 0.6f;
        p.nomotoKPrime = 0.8f; p.nomotoTPrime = 2.0f; p.minCalcSpeed = 0.05f;
        p.maxRudderAngle = 35f; p.rudderRate = 65f / 28f;
        return p;
    }

    private static ShipDynamicsModel RunCommands(ShipParameters p, int steps, Func<int, double> rudder, Func<int, double> sf)
    {
        var m = new ShipDynamicsModel(p) { U = p.ServiceSpeedMs };
        for (int i = 0; i < steps; i++)
        {
            m.RudderCommandDeg = rudder(i);
            m.SpeedFactor = sf(i);
            m.Step(Dt);
        }
        return m;
    }

    // ─────────── Расширения модели ───────────

    [Test]
    public void Norrbin_AlphaZero_EqualsNomoto_BitForBit()
    {
        ShipParameters a = MakeDefault(), b = MakeDefault();
        b.yawModel = YawModel.Norrbin; b.norrbinAlphaPrime = 0f;
        Func<int, double> rud = i => 30.0 * Math.Sin(i * 0.002);
        Func<int, double> sf = i => i < 20000 ? 1.0 : 0.5;
        ShipDynamicsModel ma = RunCommands(a, 30000, rud, sf), mb = RunCommands(b, 30000, rud, sf);
        Assert.AreEqual(ma.Psi, mb.Psi, 0.0);
        Assert.AreEqual(ma.R, mb.R, 0.0);
        Assert.AreEqual(ma.East, mb.East, 0.0);
    }

    [Test]
    public void Norrbin_SteadyYawRate_SolvesCubic()
    {
        ShipParameters p = MakeDefault();
        p.yawModel = YawModel.Norrbin; p.norrbinAlphaPrime = 20f;
        var m = new ShipDynamicsModel(p) { HoldServiceSpeed = true, RudderCommandDeg = 20.0, SpeedFactor = 1.0 };
        for (int i = 0; i < (int)(2000 / Dt); i++) m.Step(Dt);
        double expected = ShipDynamicsModel.SteadyYawRate(p, p.ServiceSpeedMs, 20.0);
        double L = p.length, U = p.ServiceSpeedMs, alpha = 20.0 * (L / U) * (L / U);
        double kd = p.nomotoKPrime * U / L * 20.0 * Mathf.Deg2Rad;
        Assert.AreEqual(kd, expected + alpha * expected * expected * expected, 1e-12, "корень кубического уравнения");
        Assert.AreEqual(expected, m.R, Math.Abs(expected) * 1e-9);
        Assert.Less(m.R, kd, "кубический член уменьшает установившуюся угловую скорость");
    }

    [Test]
    public void Norrbin_Integration_ConvergesWithStep()
    {
        ShipParameters p = MakeDefault();
        p.yawModel = YawModel.Norrbin; p.norrbinAlphaPrime = 50f;
        double Heading(double dt)
        {
            var m = new ShipDynamicsModel(p) { U = p.ServiceSpeedMs, SpeedFactor = 1.0, RudderCommandDeg = 35.0 };
            double psi = 0;
            for (int i = 0; i < (int)Math.Round(300.0 / dt); i++) { m.Step(dt); psi += m.R * dt; }
            return psi * Rad2Deg;
        }
        double coarse = Heading(Dt), fine = Heading(Dt / 10);
        Assert.AreEqual(fine, coarse, 0.05, $"Δψ за 300 с: dt = 0,02 с → {coarse:F3}°, dt = 0,002 с → {fine:F3}°");
    }

    [Test]
    public void SpeedLoss_SteadyTurn_MatchesAnalytic()
    {
        // Установившийся режим: u = u_зад − τ_разг·C_r'·L·r²
        ShipParameters p = MakeDefault();
        p.turnSpeedLossCoeff = 0.8f;
        var m = new ShipDynamicsModel(p) { U = p.ServiceSpeedMs, SpeedFactor = 1.0, RudderCommandDeg = 35.0 };
        for (int i = 0; i < (int)(3000 / Dt); i++) m.Step(Dt);
        double expected = p.ServiceSpeedMs - p.accelerationTimeConstant * p.turnSpeedLossCoeff * p.length * m.R * m.R;
        Assert.AreEqual(expected, m.U, 1e-6);
        Assert.Less(m.U, p.ServiceSpeedMs * 0.95, "на циркуляции 35° скорость должна заметно упасть");
    }

    [Test]
    public void SpeedLoss_WithoutTurn_CrashStopUnchanged()
    {
        ShipParameters a = MakeDefault(), b = MakeDefault();
        b.turnSpeedLossCoeff = 0.8f;
        var s = new TrialSettings { Parameters = a, TestSpeedMs = TrialSettings.DefaultTestSpeedMs(a) };
        var t = new TrialSettings { Parameters = b, TestSpeedMs = TrialSettings.DefaultTestSpeedMs(b) };
        TrialResult ra = ManoeuvringTrials.CrashStop(s), rb = ManoeuvringTrials.CrashStop(t);
        for (int i = 0; i < ra.Metrics.Count; i++)
            Assert.AreEqual(ra.Metrics[i].Value, rb.Metrics[i].Value, 0.0, ra.Metrics[i].Name);
    }

    [Test]
    public void ShipParameters_Clone_CopiesAllFields()
    {
        ShipParameters p = MakeDefault();
        p.yawModel = YawModel.Norrbin; p.norrbinAlphaPrime = 3.5f; p.turnSpeedLossCoeff = 0.25f;
        ShipParameters c = p.Clone();
        foreach (FieldInfo f in typeof(ShipParameters).GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (f.DeclaringType == typeof(ShipParameters))
                Assert.AreEqual(f.GetValue(p), f.GetValue(c), f.Name);
    }

    // ─────────── Эталон Mariner ───────────

    [Test]
    public void Mariner_StraightRun_KeepsNominalSpeed()
    {
        var m = new MarinerModel { PropellerAsymmetry = false };
        for (int i = 0; i < (int)(600 / Dt); i++) m.Step(Dt);
        Assert.AreEqual(MarinerModel.NominalSpeed, m.U, 1e-9);
        Assert.AreEqual(0.0, m.Psi, 1e-12);
    }

    [Test]
    public void Mariner_PropellerAsymmetry_TurnsWithZeroRudder()
    {
        var m = new MarinerModel { PropellerAsymmetry = true };
        for (int i = 0; i < (int)(600 / Dt); i++) m.Step(Dt);
        Assert.Greater(m.R, 0.0, "постоянные Y'_0, N'_0 из mariner.m поворачивают судно вправо");
    }

    [Test]
    public void Mariner_WithoutPropellerTerms_IsSymmetric()
    {
        var s = new TrialSettings { TestSpeedMs = MarinerModel.NominalSpeed, MaxDuration = 3000, ModelFactory = ShipCalibration.MarinerFactory(false) };
        TrialResult a = ManoeuvringTrials.Turning(s, true), b = ManoeuvringTrials.Turning(s, false);
        for (int i = 0; i < 4; i++)
            Assert.AreEqual(a.Metrics[i].Value, b.Metrics[i].Value, 1e-6, a.Metrics[i].Name);
    }

    [Test]
    public void Mariner_LinearNomoto_MatchesSmallRudderResponse()
    {
        MarinerModel.LinearNomoto(out double k, out double t, out _, out _, out _);
        Assert.Greater(k, 0.0);
        Assert.Greater(t, 0.0);
        // Линейная область Mariner очень узкая: при δ = 0,5° r' уже на 5 % меньше K'·δ, при 2° — на 31 %.
        // Поэтому проверка линеаризации — на δ = 0,02°.
        const double rudder = 0.02;
        var m = new MarinerModel { PropellerAsymmetry = false, RudderCommandDeg = rudder };
        for (int i = 0; i < (int)(1500 / Dt); i++) m.Step(Dt);
        double rPrime = m.R * MarinerModel.L / m.U;
        double expected = k * rudder / Rad2Deg;
        Assert.AreEqual(expected, rPrime, expected * 0.001, $"r' = {rPrime:E5}, K'·δ = {expected:E5}");
    }

    // ─────────── Оптимизатор и идентификация ───────────

    [Test]
    public void NelderMead_FindsRosenbrockMinimum()
    {
        NelderMeadResult r = NelderMead.Minimize(x => 100 * Math.Pow(x[1] - x[0] * x[0], 2) + Math.Pow(1 - x[0], 2),
                                                 new[] { -1.2, 1.0 }, new[] { 0.5, 0.5 }, 5000, 1e-14);
        Assert.AreEqual(1.0, r.X[0], 1e-4);
        Assert.AreEqual(1.0, r.X[1], 1e-4);
    }

    [Test]
    public void Identification_RecoversKnownNorrbinParameters()
    {
        // Синтетический эталон: та же модель с известными K', T', α' — подбор должен их вернуть
        ShipParameters basis = ShipCalibration.MarinerBase();
        ShipParameters truth = basis.Clone();
        truth.nomotoKPrime = 2.0f; truth.nomotoTPrime = 3.0f;
        truth.yawModel = YawModel.Norrbin; truth.norrbinAlphaPrime = 10f;
        var s = new TrialSettings { Parameters = truth, TestSpeedMs = truth.ServiceSpeedMs, Dt = Dt, MaxDuration = 3000, SampleInterval = Dt };
        var set = new List<TrialResult> { ManoeuvringTrials.Zigzag(s, 10, 10), ManoeuvringTrials.Turning(s, true) };

        ShipParameters fit = ShipCalibration.FitReplay(basis, set, true, Dt, out double cost, out _);
        Assert.AreEqual(2.0, fit.nomotoKPrime, 2.0 * 0.01, "K'");
        Assert.AreEqual(3.0, fit.nomotoTPrime, 3.0 * 0.01, "T'");
        Assert.AreEqual(10.0, fit.norrbinAlphaPrime, 10.0 * 0.01, "α'");
        Assert.Less(cost, 1e-6);
    }

    // ─────────── Регрессия результатов калибровки ───────────

    [Test]
    public void MarinerCalibration_ReproducesResult()
    {
        CalibrationResult r = ShipCalibration.CalibrateMariner();
        CalibrationStage nomoto = r.Stages.Find(s => s.Id == "nomoto_all");
        CalibrationStage norrbin = r.Stages.Find(s => s.Id == "norrbin_all");
        Assert.AreEqual("norrbin_all", r.Stages[r.Recommended].Id);
        // Значения — из прогона 10.10.2026 (ThesisNotes, раздел 3)
        Assert.AreEqual(0.8063, r.Base.turnSpeedLossCoeff, 0.8063 * 0.01, "C_r'");
        Assert.AreEqual(3.4845, norrbin.Parameters.nomotoKPrime, 3.4845 * 0.01, "K'");
        Assert.AreEqual(3.9699, norrbin.Parameters.nomotoTPrime, 3.9699 * 0.01, "T'");
        Assert.AreEqual(53.74, norrbin.Parameters.norrbinAlphaPrime, 53.74 * 0.02, "α'");
        Assert.Less(norrbin.Cost, 0.05);
        Assert.Less(norrbin.Cost * 10.0, nomoto.Cost, "Норрбин должен уменьшать J более чем в 10 раз");
    }

    [Test]
    public void KcsCalibration_NorrbinBetterThanNomoto()
    {
        CalibrationResult r = ShipCalibration.CalibrateKcs();
        Assert.AreEqual(2, r.Stages.Count);
        Assert.Less(r.Stages[1].Cost, r.Stages[0].Cost);
        Assert.Less(r.Stages[1].Cost, 0.5);
        Assert.AreEqual(34.72, KcsEfdData.ToFullScaleTime(5.640), 0.01);
        Assert.AreEqual(24.0, KcsEfdData.SpeedMs / ShipParameters.KnotsToMs, 0.05);
    }
}
