// ============================================================
// ShipDynamicsModelTests.cs — EditMode-тесты модели движения и стенда испытаний
// Window → General → Test Runner → EditMode → Run All.
// ============================================================
using System;
using NUnit.Framework;
using UnityEngine;

public class ShipDynamicsModelTests
{
    private const double Dt = 0.02;

    private static ShipParameters MakeDefault()
    {
        // Совпадает с ShipParameters_Default.asset (заглушка до калибровки)
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        p.length = 150f; p.serviceSpeedKnots = 15f;
        p.accelerationTimeConstant = 90f; p.decelerationTimeConstant = 150f; p.asternFactor = 0.6f;
        p.nomotoKPrime = 0.8f; p.nomotoTPrime = 2.0f; p.minCalcSpeed = 0.05f;
        p.maxRudderAngle = 35f; p.rudderRate = 65f / 28f;
        return p;
    }

    // ─────────── Копия ShipNavigation.Step до рефакторинга (A2.2) — эталон ───────────
    private sealed class LegacyModel
    {
        public double psi, r, u, delta, ordered, east, north;

        public void Step(ShipParameters p, double cmd, double sf, double dt)
        {
            double maxRudder = p.maxRudderAngle;
            ordered = Clamp(cmd, -maxRudder, maxRudder);
            double maxStep = p.rudderRate * dt;
            delta += Clamp(ordered - delta, -maxStep, maxStep);

            double vService = p.ServiceSpeedMs;
            double uSet = sf >= 0.0 ? sf * vService : sf * vService * p.asternFactor;
            bool accelerating = Math.Abs(uSet) > Math.Abs(u) && u * uSet >= 0.0;
            double tau = Math.Max(0.1, accelerating ? p.accelerationTimeConstant : p.decelerationTimeConstant);
            u = uSet + (u - uSet) * Math.Exp(-dt / tau);

            double L = Math.Max(1.0, p.length);
            double U = Math.Max(Math.Abs(u), Math.Max(1e-4, p.minCalcSpeed));
            double K = p.nomotoKPrime * (u >= 0.0 ? U : -U) / L;
            double T = p.nomotoTPrime * L / U;
            double rSteady = K * delta * Mathf.Deg2Rad;
            r = rSteady + (r - rSteady) * Math.Exp(-dt / T);

            psi = Wrap2Pi(psi + r * dt);
            east += u * Math.Sin(psi) * dt;
            north += u * Math.Cos(psi) * dt;
        }

        private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
        private static double Wrap2Pi(double a) { a %= 2.0 * Math.PI; return a < 0 ? a + 2.0 * Math.PI : a; }
    }

    [Test]
    public void Refactor_MatchesLegacyStep_BitForBit()
    {
        // Команды как в шлеме: штурвал (float) и телеграф (9 положений вперёд/назад).
        // Скорость держится выше U_min — ниже неё модель исправлена намеренно (см. тест на стопе).
        var p = MakeDefault();
        var rnd = new System.Random(12345);
        float[] telegraph = { 1f, 0.75f, 0.5f, 0.25f, -0.25f, -0.5f, -1f };
        for (int run = 0; run < 5; run++)
        {
            var legacy = new LegacyModel { psi = 1.0, u = 7.0, east = 1000, north = -500 };
            var model = new ShipDynamicsModel(p) { Psi = 1.0, U = 7.0, East = 1000, North = -500 };
            float cmd = 0f, sf = 1f;
            for (int i = 0; i < 10000; i++)
            {
                if (i % 500 == 0)
                {
                    cmd = (float)(rnd.NextDouble() * 80.0 - 40.0);           // включая > δ_max
                    sf = telegraph[rnd.Next(telegraph.Length)];
                    if (run < 3 && sf < 0f) sf = -sf;                         // часть прогонов — только вперёд
                }
                legacy.Step(p, cmd, sf, Dt);
                model.RudderCommandDeg = cmd;
                model.SpeedFactor = sf;
                model.Step(Dt);
                if (Math.Abs(legacy.u) < p.minCalcSpeed) break;             // переход через ноль — вне сравнения
                Assert.AreEqual(legacy.psi, model.Psi, 0.0, $"ψ, прогон {run}, шаг {i}");
                Assert.AreEqual(legacy.r, model.R, 0.0, $"r, прогон {run}, шаг {i}");
                Assert.AreEqual(legacy.u, model.U, 0.0, $"u, прогон {run}, шаг {i}");
                Assert.AreEqual(legacy.delta, model.Delta, 0.0);
                Assert.AreEqual(legacy.east, model.East, 0.0);
                Assert.AreEqual(legacy.north, model.North, 0.0);
            }
        }
    }

    [Test]
    public void Model_IsDeterministic()
    {
        var p = MakeDefault();
        ShipDynamicsModel Run()
        {
            var m = new ShipDynamicsModel(p) { U = 5.0 };
            for (int i = 0; i < 30000; i++)
            {
                m.RudderCommandDeg = 20.0 * Math.Sin(i * 0.001);
                m.SpeedFactor = i < 15000 ? 1.0 : -0.5;
                m.Step(Dt);
            }
            return m;
        }
        ShipDynamicsModel a = Run(), b = Run();
        Assert.AreEqual(a.Psi, b.Psi, 0.0);
        Assert.AreEqual(a.East, b.East, 0.0);
        Assert.AreEqual(a.North, b.North, 0.0);
        Assert.AreEqual(a.U, b.U, 0.0);
    }

    [Test]
    public void Stopped_RudderDoesNotTurnShip()
    {
        var p = MakeDefault();
        var m = new ShipDynamicsModel(p) { U = 0.0, SpeedFactor = 0.0, RudderCommandDeg = 35.0 };
        double turned = 0;
        for (int i = 0; i < (int)(600 / Dt); i++) { m.Step(Dt); turned += m.R * Dt; }
        Assert.Less(Math.Abs(turned * 180.0 / Math.PI), 1.0, "за 10 мин на стопе с рулём 35° курс изменился ≥ 1°");
    }

    [TestCase(1.0)]
    [TestCase(0.5)]
    [TestCase(-0.5)]
    [TestCase(-1.0)]
    public void SteadySpeed_EqualsServiceSpeedTimesTelegraph(double sf)
    {
        var p = MakeDefault();
        var m = new ShipDynamicsModel(p) { SpeedFactor = sf };
        for (int i = 0; i < (int)(3000 / Dt); i++) m.Step(Dt);   // > 20 τ_торм
        double expected = sf >= 0 ? sf * p.ServiceSpeedMs : sf * p.ServiceSpeedMs * p.asternFactor;
        Assert.AreEqual(expected, m.U, 1e-6);
    }

    [Test]
    public void SteeringGear_MeetsSolas_II1_Reg29()
    {
        // СОЛАС гл. II-1, правило 29.3.2: с 35° одного борта на 30° другого не более чем за 28 с
        var p = MakeDefault();
        var m = new ShipDynamicsModel(p) { Delta = -35.0, RudderCommandDeg = 30.0, U = p.ServiceSpeedMs, SpeedFactor = 1.0 };
        double t = 0;
        while (m.Delta < 30.0 - 1e-9 && t < 60) { m.Step(Dt); t += Dt; }
        // Допуск — один шаг: ω_δ = 65/28 хранится во float и чуть меньше точного значения
        Assert.LessOrEqual(t, 28.0 + Dt + 1e-9, $"перекладка 35° ЛБ → 30° ПБ заняла {t:F2} с");
    }

    [Test]
    public void TurningCircle_MatchesTheory()
    {
        var p = MakeDefault();
        var s = new TrialSettings { Parameters = p, TestSpeedMs = TrialSettings.DefaultTestSpeedMs(p) };
        TrialResult r = ManoeuvringTrials.Turning(s, true);
        Assert.IsTrue(r.Completed, r.Message);
        double d = r.Metrics.Find(m => m.Name.StartsWith("Диаметр установившейся")).Value;
        double theory = p.TheoreticalCircleDiameter(35f);
        Assert.AreEqual(theory, d, theory * 0.01, $"D = {d:F1} м, теория {theory:F1} м");
    }

    [Test]
    public void TurningCircle_PortAndStarboardAreSymmetric()
    {
        var p = MakeDefault();
        var s = new TrialSettings { Parameters = p, TestSpeedMs = TrialSettings.DefaultTestSpeedMs(p) };
        TrialResult a = ManoeuvringTrials.Turning(s, true), b = ManoeuvringTrials.Turning(s, false);
        for (int i = 0; i < 4; i++)
            Assert.AreEqual(a.Metrics[i].Value, b.Metrics[i].Value, 1e-6, a.Metrics[i].Name);
    }

    [Test]
    public void CrashStop_MatchesAnalyticSolution()
    {
        // u(t) = u_зад + (V0 − u_зад)·e^(−t/τ_торм), u_зад = −k_зх·V_эксп → t_стоп = τ·ln((V0 − u_зад)/(−u_зад))
        var p = MakeDefault();
        var s = new TrialSettings { Parameters = p, TestSpeedMs = TrialSettings.DefaultTestSpeedMs(p) };
        TrialResult r = ManoeuvringTrials.CrashStop(s);
        double uSet = -p.asternFactor * p.ServiceSpeedMs;
        double tStop = p.decelerationTimeConstant * Math.Log((s.TestSpeedMs - uSet) / -uSet);
        double reach = uSet * tStop + p.decelerationTimeConstant * (s.TestSpeedMs - uSet) * (1 - Math.Exp(-tStop / p.decelerationTimeConstant));
        Assert.AreEqual(tStop, r.Metrics.Find(m => m.Name == "Время до остановки").Value, 0.05);
        Assert.AreEqual(reach, r.Metrics.Find(m => m.Name.StartsWith("Путь торможения")).Value, reach * 0.002);
    }

    [TestCase(5.0, 10.0)]
    [TestCase(10.0, 10.0)]
    [TestCase(20.0, 15.0)]
    [TestCase(29.9, 19.95)]
    [TestCase(30.0, 20.0)]
    [TestCase(45.0, 20.0)]
    public void ImoFirstOvershootCriterion(double lOverV, double expected)
    {
        Assert.AreEqual(expected, ManoeuvringTrials.FirstOvershoot10Limit(lOverV), 1e-9);
        Assert.AreEqual(expected + 15.0, ManoeuvringTrials.SecondOvershoot10Limit(lOverV), 1e-9);
    }

    [Test]
    public void Zigzag_ProducesTwoOvershoots()
    {
        var p = MakeDefault();
        var s = new TrialSettings { Parameters = p, TestSpeedMs = TrialSettings.DefaultTestSpeedMs(p) };
        TrialResult r = ManoeuvringTrials.Zigzag(s, 10, 10);
        Assert.IsTrue(r.Completed, r.Message);
        Assert.Greater(r.Metrics[0].Value, 0.0);
        Assert.Greater(r.Metrics[1].Value, 0.0);
        Assert.IsNotNull(r.Metrics[0].Pass);
    }
}
