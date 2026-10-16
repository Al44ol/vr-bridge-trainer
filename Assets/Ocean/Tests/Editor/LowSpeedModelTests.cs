// ============================================================
// LowSpeedModelTests.cs — Модель 3 СС для малых скоростей (запрос 19.1)
//
// Модель Номото не изменилась (внешние силы игнорирует, V = 0); 3 СС: покой без сил,
// согласование K' и T' линеаризации с ассетом, установившаяся циркуляция = модели ассета,
// дрейф и разворот на месте — по аналитике поперечного сопротивления, торможение без скачков,
// знак поперечной силы винта, обдув руля, скорость относительно грунта, регрессия Clarke
// против ПМУ Mariner, испытания малых скоростей завершаются.
// ============================================================
using System;
using NUnit.Framework;
using UnityEngine;

public class LowSpeedModelTests
{
    private const double Dt = 0.02;

    /// <summary>Значения ассета ShipParameters_Model45m (судно сцены).</summary>
    public static ShipParameters Model45m()
    {
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        p.name = "ShipParameters_Model45m";
        p.length = 45f; p.beam = 8.71f; p.draft = 3f; p.blockCoefficient = 0.6f;
        p.serviceSpeedKnots = 15f; p.accelerationTimeConstant = 30f; p.decelerationTimeConstant = 60f;
        p.nomotoKPrime = 0.8f; p.nomotoTPrime = 2f;
        p.hullParallelForwardL = 0.4f;   // 19.3б-2: контур — как корпус сцены (Hull + BowTip)
        return p;
    }

    /// <summary>Значения ассета ShipParameters_Mariner (Норрбин, калибровка A3.2).</summary>
    public static ShipParameters Mariner()
    {
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        p.name = "ShipParameters_Mariner";
        p.length = 160.93f; p.beam = 23.16f; p.draft = 8f; p.blockCoefficient = 0.7f;
        p.serviceSpeedKnots = 15f; p.accelerationTimeConstant = 95.2f; p.decelerationTimeConstant = 150f;
        p.nomotoKPrime = 3.4845f; p.nomotoTPrime = 3.97f;
        p.yawModel = YawModel.Norrbin; p.norrbinAlphaPrime = 53.74f; p.turnSpeedLossCoeff = 0.806f;
        p.rudderRate = 5f;
        return p;
    }

    private static ShipDynamicsModel New3Dof(ShipParameters p) =>
        new ShipDynamicsModel(p) { MotionOverride = MotionModel.Manoeuvring3Dof };

    [Test]
    public void Course1Dof_IgnoresExternalForces_NoSway()
    {
        ShipParameters p = Model45m();
        var a = new ShipDynamicsModel(p) { U = 5.0, SpeedFactor = 0.5, RudderCommandDeg = 15 };
        var b = new ShipDynamicsModel(p) { U = 5.0, SpeedFactor = 0.5, RudderCommandDeg = 15 };
        for (int i = 0; i < 3000; i++)
        {
            b.AddForce(1e5, 1e5, 10.0);
            a.Step(Dt); b.Step(Dt);
        }
        Assert.AreEqual(MotionModel.Course1Dof, b.ActiveMotionModel);
        Assert.AreEqual(a.East, b.East, 0.0, "модель Номото внешние силы не учитывает — бит в бит");
        Assert.AreEqual(a.North, b.North, 0.0);
        Assert.AreEqual(a.Psi, b.Psi, 0.0);
        Assert.AreEqual(0.0, b.V);
    }

    [Test]
    public void Rest_NoForces_StaysAtRest()
    {
        ShipDynamicsModel m = New3Dof(Model45m());
        for (int i = 0; i < 6000; i++) m.Step(Dt);
        Assert.AreEqual(0.0, m.U, 1e-12);
        Assert.AreEqual(0.0, m.V, 1e-12);
        Assert.AreEqual(0.0, m.R, 1e-12);
        Assert.AreEqual(0.0, m.East, 1e-9);
        Assert.AreEqual(0.0, m.North, 1e-9);
    }

    [TestCase(0)]
    [TestCase(1)]
    public void Fit_LinearKAndT_MatchAsset(int ship)
    {
        ShipParameters p = ship == 0 ? Model45m() : Mariner();
        Manoeuvring3DofCoefficients c = Manoeuvring3DofCoefficients.Build(p);
        Assert.IsTrue(c.FitExact, c.FitNote);
        Assert.IsTrue(c.StableOnCourse, "линеаризация после согласования устойчива на курсе");
        Assert.AreEqual(p.nomotoKPrime, c.LinearKPrime, 1e-4);
        Assert.AreEqual(p.nomotoTPrime, c.LinearTPrime, 1e-4);
        Assert.Less(c.YdeltaPrime, 0.0, "руль на ПБ даёт силу на ЛБ (корма влево, нос вправо)");
        TestContext.WriteLine($"{p.name}: k_N = {c.YawDampingFactor:F4}, Y'_δ = {c.YdeltaPrime:F6}, C(k_N=1) = {c.StabilityIndex(1.0):E3}");
    }

    [TestCase(0)]
    [TestCase(1)]
    public void TurningCircle_MatchesAssetModel(int ship)
    {
        ShipParameters p = ship == 0 ? Model45m() : Mariner();
        Manoeuvring3DofCoefficients c = Manoeuvring3DofCoefficients.Build(p);
        Assert.IsTrue(c.CircleMatched, c.CircleNote);
        Assert.AreEqual(c.CircleTargetD, c.CircleD, 0.01 * c.CircleTargetD, "диаметр установившейся циркуляции 35°");
        // Без согласования (C_d по Хёрнеру на всех скоростях) циркуляция заметно другая — согласование нужно
        Assert.Greater(Math.Abs(c.CircleHoernerD - c.CircleTargetD), 0.2 * c.CircleTargetD);
        TestContext.WriteLine($"{p.name}: D цель {c.CircleTargetD:F0} м, 3 СС {c.CircleD:F0} м, Хёрнер {c.CircleHoernerD:F0} м, k_∞ = {c.CrossFlowHighSpeedFactor:F3}");
    }

    [Test]
    public void Drift_UnderLateralForce_MatchesCrossFlowAnalytic()
    {
        ShipParameters p = Model45m();
        ShipDynamicsModel m = New3Dof(p);
        double f = LowSpeedTrials.ReferenceForce(p);
        for (int i = 0; i < 45000; i++) { m.AddForce(0.0, f, 0.0); m.Step(Dt); }
        double vAn = m.Coefficients3Dof.SteadyDriftSpeed(f);
        Assert.AreEqual(vAn, m.V, 0.02 * vAn, "v = √(F/(½ρ·d·C_d·L))");
        Assert.AreEqual(0.0, m.R, 1e-5, "курс установился");
        Assert.Greater(m.East, 0.0, "дрейф на правый борт (курс 000° — на восток)");
    }

    [Test]
    public void Spin_UnderCouple_MatchesCrossFlowAnalytic()
    {
        ShipParameters p = Model45m();
        ShipDynamicsModel m = New3Dof(p);
        double f = LowSpeedTrials.ReferenceForce(p), arm = 0.45 * p.length;
        for (int i = 0; i < 30000; i++)
        {
            m.AddForce(0.0, f, arm);
            m.AddForce(0.0, -f, -arm);
            m.Step(Dt);
        }
        double rAn = m.Coefficients3Dof.SteadySpinRate(2.0 * arm * f);
        Assert.AreEqual(rAn, m.R, 0.02 * rAn, "r = √(N/(½ρ·d·C_d·L⁴/32))");
        Assert.Greater(m.R, 0.0, "нос вправо");
        Assert.Less(Math.Sqrt(m.East * m.East + m.North * m.North), 0.2 * p.length, "разворот на месте — мидель почти не смещается");
    }

    [Test]
    public void Deceleration_FullToStopWithRudder_NoJumps()
    {
        ShipParameters p = Model45m();
        ShipDynamicsModel m = New3Dof(p);
        m.U = p.ServiceSpeedMs;
        m.SpeedFactor = 0.0;
        m.RudderCommandDeg = 20.0;
        double maxDr = 0.0, maxDv = 0.0, maxDu = 0.0;
        for (int i = 0; i < 30000; i++)
        {
            double u = m.U, v = m.V, r = m.R;
            m.Step(Dt);
            Assert.IsFalse(double.IsNaN(m.U) || double.IsNaN(m.V) || double.IsNaN(m.R));
            maxDu = Math.Max(maxDu, Math.Abs(m.U - u));
            maxDv = Math.Max(maxDv, Math.Abs(m.V - v));
            maxDr = Math.Max(maxDr, Math.Abs(m.R - r));
        }
        // Переход «полный ход → стоп» проходит через весь диапазон коэффициента k(u) — без скачков:
        // за шаг 0,02 с изменения не больше, чем на разгоне с места
        Assert.Less(maxDu, 0.01, "Δu за шаг, м/с");
        Assert.Less(maxDv, 0.005, "Δv за шаг, м/с");
        Assert.Less(maxDr * 180.0 / Math.PI, 0.01, "Δr за шаг, °/с");
        Assert.Less(Math.Abs(m.U), 0.2, "судно остановилось");
    }

    [Test]
    public void Astern_PropellerSideForce_SwingsBowToStarboard()
    {
        ShipParameters p = Model45m();
        ShipDynamicsModel m = New3Dof(p);
        m.SpeedFactor = -1.0;
        double turned = 0.0;
        for (int i = 0; i < 3000; i++) { m.Step(Dt); turned += m.R * Dt; }
        Assert.Greater(turned * 180.0 / Math.PI, 2.0, "правый винт на заднем ходу: корма влево, нос вправо");

        ShipParameters q = Model45m();
        q.propellerSideForceFactor = 0f;
        ShipDynamicsModel n = New3Dof(q);
        n.SpeedFactor = -1.0;
        for (int i = 0; i < 3000; i++) n.Step(Dt);
        Assert.AreEqual(0.0, n.Psi, 1e-9, "без поперечной силы винта судно отходит прямо");
    }

    [Test]
    public void Kick_RudderSlipstream_TurnsFromRest()
    {
        var s = new TrialSettings { Parameters = Model45m(), Dt = Dt, SampleInterval = 1.0 };
        TrialResult with = LowSpeedTrials.Kick(s, true), without = LowSpeedTrials.Kick(s, false);
        double a = with.Metrics[0].Value, b = without.Metrics[0].Value;
        Assert.Greater(a, 2.0 * b, "обдув руля струёй винта: «толчок» машиной поворачивает с места");
        Assert.Greater(b, 0.0);
    }

    [Test]
    public void GroundVelocity_IncludesSway()
    {
        var m = new ShipDynamicsModel(Model45m()) { Psi = Math.PI / 2.0, U = 2.0, V = 0.5 };
        // Курс 090°: продольная — на восток, поперечная (на ПБ) — на юг
        Assert.AreEqual(2.0, m.VelocityEast, 1e-12);
        Assert.AreEqual(-0.5, m.VelocityNorth, 1e-12);
        Assert.AreEqual(Math.Sqrt(4.25), m.SpeedThroughWater, 1e-12);
    }

    [Test]
    public void Clarke_ForMarinerDimensions_ComparableToPmm()
    {
        ShipParameters p = Mariner();
        ClarkeDerivatives c = ClarkeDerivatives.Compute(p.length, p.beam, p.draft, p.blockCoefficient);
        ClarkeDerivatives m = MarinerModel.HydroDerivatives(out _, out _, out _);
        // Регрессия по главным размерениям — тот же знак и порядок, что ПМУ (в пределах ×2)
        void Close(string n, double a, double b)
        {
            Assert.AreEqual(Math.Sign(b), Math.Sign(a), n + ": знак");
            Assert.That(a / b, Is.InRange(0.5, 2.0), n);
            TestContext.WriteLine($"{n}: Clarke {a:F5}, ПМУ {b:F5}, отношение {a / b:F2}");
        }
        Close("Y'_v", c.Yv, m.Yv);
        Close("Y'_r", c.Yr, m.Yr);
        Close("N'_v", c.Nv, m.Nv);
        Close("N'_r", c.Nr, m.Nr);
        Close("Y'_v̇", c.Yvdot, m.Yvdot);
        Close("N'_ṙ", c.Nrdot, m.Nrdot);
    }

    [Test]
    public void LowSpeedTrials_AllComplete()
    {
        var s = new TrialSettings { Parameters = Model45m(), Dt = Dt, SampleInterval = 1.0 };
        foreach (TrialResult r in LowSpeedTrials.RunAll(s))
        {
            Assert.IsTrue(r.Completed, $"{r.Title}: {r.Message}");
            foreach (TrialMetric x in r.Metrics) Assert.IsFalse(double.IsNaN(x.Value), $"{r.Title}: {x.Name}");
        }
    }
}
