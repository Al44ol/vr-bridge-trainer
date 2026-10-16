// ============================================================
// TowingTests.cs — Буксир, трос, ветер (запрос 19.3а, Core)
//
// Ветер Isherwood: значения по таблице, знаки (с ПБ — сила на ЛБ, в нос — назад), симметрия по γ;
// установившийся дрейф = равновесие ветра и поперечного сопротивления. Разложение на две колонки.
// Модель 3 СС буксира устойчива (диагональные присоединённые массы), прежние суда не изменились.
// Тяга на гаке = заданной; толкание у миделя — судно идёт лагом без разворота, буксир перпендикулярно борту;
// тяга от кормы — корма на ЛБ, нос вправо; обрыв троса при перегрузке.
// ============================================================
using System;
using NUnit.Framework;

public class TowingTests
{
    private static ShipParameters Ship() => LowSpeedModelTests.Model45m();

    [Test]
    public void Wind_IsherwoodTableSignsAndSymmetry()
    {
        ShipWindArea a = ShipWindArea.Estimate(45, 10);
        Assert.AreEqual(270.0, a.LateralM2, 1e-6, "A_L = L·f + 0,3L·5 = 45·4,5 + 67,5");
        // γ = 90°: C_Y = B0 + B2·2A_F/B² + B5·C/L (строка 90 таблицы)
        WindLoad.Coefficients(a, 90, out double cx, out double cy, out double cn);
        double expCy = 0.889 + 0.138 * 2 * a.FrontalM2 / 100.0 - 0.66 * 0.5;
        Assert.AreEqual(expCy, cy, 1e-9);
        Assert.AreEqual(-(0.355 - 0.247 * a.PerimeterM / 45.0 + 0.018 * 1), cx, 1e-9);
        // Между узлами — линейно
        WindLoad.Coefficients(a, 85, out _, out double cy85, out _);
        WindLoad.Coefficients(a, 80, out _, out double cy80, out _);
        Assert.AreEqual(0.5 * (cy80 + cy), cy85, 1e-9);

        // Судно курсом 0°, без хода: ветер с востока (с ПБ) — сила на ЛБ; с севера (в нос) — назад
        WindForce fromStb = WindLoad.Compute(a, Wind.Knots(20, 90), 0, 0, 0);
        Assert.Less(fromStb.Y, 0.0);
        Assert.AreEqual(-90.0, fromStb.GammaDeg, 1e-6);
        WindForce head = WindLoad.Compute(a, Wind.Knots(20, 0), 0, 0, 0);
        Assert.Less(head.X, 0.0);
        Assert.AreEqual(0.0, head.Y, 1e-9);
        // Симметрия: ветер с ЛБ — та же сила с другим знаком Y и N
        WindForce fromPort = WindLoad.Compute(a, Wind.Knots(20, 270), 0, 0, 0);
        Assert.AreEqual(-fromStb.Y, fromPort.Y, 1e-6);
        Assert.AreEqual(-fromStb.N, fromPort.N, 1e-6);
        Assert.AreEqual(fromStb.X, fromPort.X, 1e-6);
        // Свой ход — встречный относительный ветер
        WindForce moving = WindLoad.Compute(a, new Wind(0, 0), 0, 5.0, 0);
        Assert.AreEqual(5.0, moving.RelativeSpeed, 1e-9);
        Assert.Less(moving.X, 0.0);
        // Величина: ½ρ_a·V²·A_L·C_Y
        double v = 20 * ShipParameters.KnotsToMs;
        Assert.AreEqual(0.5 * WindLoad.AirDensity * v * v * a.LateralM2 * expCy, -fromStb.Y, 1e-6 * Math.Abs(fromStb.Y));
    }

    [Test]
    public void Wind_SteadyDriftBalancesCrossFlowDrag()
    {
        TowingTrials.Run r = TowingTrials.WindDrift(Ship(), 20.0, 400);
        Manoeuvring3DofCoefficients c = new ShipDynamicsModel(Ship()) { MotionOverride = MotionModel.Manoeuvring3Dof }.Coefficients3Dof;
        // Без хода и вращения: |Y_ветра| = K_пс·Σ v² по полосам = CrossFlowK·20·v²
        double vExpected = Math.Sqrt(Math.Abs(r.ForceKn) * 1e3 / (c.CrossFlowK * 20));
        TestContext.Out.WriteLine($"Ветер 20 уз с ПБ: Y = {r.ForceKn:F1} кН, дрейф {r.SwayKn:F3} уз (равновесие без вращения {vExpected / ShipParameters.KnotsToMs:F3} уз), Δψ {r.DeltaPsiDeg:F1}°");
        Assert.Less(r.SwayKn, 0.0, "дрейф на ЛБ");
        Assert.AreEqual(vExpected / ShipParameters.KnotsToMs, Math.Abs(r.SwayKn), 0.1 * vExpected / ShipParameters.KnotsToMs);
    }

    [Test]
    public void Allocation_ReproducesForcesAndScalesWhenSaturated()
    {
        double xa = -8.8, ya = 2.0, max = 98e3;
        double k = AzimuthAllocation.Solve(30e3, -20e3, 0.1e6, xa, ya, max, out double fx1, out double fy1, out double fx2, out double fy2);
        Assert.AreEqual(1.0, k);
        AzimuthAllocation.Resultant(fx1, fy1, fx2, fy2, xa, ya, out double X, out double Y, out double N);
        Assert.AreEqual(30e3, X, 1e-6); Assert.AreEqual(-20e3, Y, 1e-6); Assert.AreEqual(0.1e6, N, 1e-6);
        k = AzimuthAllocation.Solve(400e3, 0, 0, xa, ya, max, out fx1, out fy1, out fx2, out fy2);
        Assert.AreEqual(98e3 / 200e3, k, 1e-12, "упор колонки ≤ T_гак/2");
        Assert.AreEqual(max, Math.Sqrt(fx1 * fx1 + fy1 * fy1), 1e-6);
    }

    [Test]
    public void TugHull_StableAndOldShipsUnchanged()
    {
        ShipParameters tug = TowingTrials.TugParameters();
        Manoeuvring3DofCoefficients c = new ShipDynamicsModel(tug) { MotionOverride = MotionModel.Manoeuvring3Dof }.Coefficients3Dof;
        Assert.Greater(c.M22 * c.M33 - c.M23 * c.M32, 0.0, "матрица масс положительна");
        Assert.AreEqual(1.0, c.YawDampingFactor);
        var m = new ShipDynamicsModel(tug) { MotionOverride = MotionModel.Manoeuvring3Dof };
        m.Teleport(0, 0, 0, -1.0, 0);
        m.V = 0.3; m.R = 0.05;
        for (int i = 0; i < 1500; i++) m.Step(0.02);
        Assert.IsFalse(double.IsNaN(m.R));
        Assert.Less(Math.Abs(m.R), 0.05, "возмущение затухает (на заднем ходу)");
        Assert.Less(Math.Abs(m.V), 0.3);

        // Прежние суда: у Model45m перекрёстные члены Clarke сохранились, подбор по K', T' — как был
        Manoeuvring3DofCoefficients s = new ShipDynamicsModel(Ship()) { MotionOverride = MotionModel.Manoeuvring3Dof }.Coefficients3Dof;
        Assert.AreNotEqual(0.0, s.M23);
        Assert.AreEqual(Ship().nomotoKPrime, s.LinearKPrime, 1e-3);
    }

    [Test]
    public void BollardPull_SteadyTensionEqualsOrder()
    {
        ShipParameters tug = TowingTrials.TugParameters();
        foreach (double p in new[] { 0.5, 1.0 })
        {
            TowingTrials.Run r = TowingTrials.BollardPull(Ship(), tug, p);
            double bp = TugSpec.DefaultBollardPullTonnes * 9.80665;
            TestContext.Out.WriteLine($"Тяга на гаке {p * 100:0} %: {r.ForceKn:F1} кН (заданная {p * bp:F1}), наибольшая {r.MaxForceKn:F1}");
            Assert.AreEqual(p * bp, r.ForceKn, 0.02 * p * bp);
        }
        Assert.AreEqual(12.0, TowingTrials.FreeRun(tug), 1.0, "свободный ход ≈ 12 уз");
    }

    [Test]
    public void PushAtMidship_ShipMovesSidewaysTugPerpendicular()
    {
        ShipParameters ship = Ship(), tug = TowingTrials.TugParameters();
        var m = new ShipDynamicsModel(ship) { MotionOverride = MotionModel.Manoeuvring3Dof };
        m.Teleport(0, 0, 0, 0, 0);
        var sys = new TowingSystem(ship, tug) { PushPointX = 0 };
        sys.PlaceTug(m, TugCommand.Push(270, 0.5), 30);
        bool contact = false;
        for (int i = 0; i < 6000; i++) { sys.Apply(m, 0.02, i * 0.02); m.Step(0.02); }
        foreach (TowEvent e in sys.Events) contact |= e.Type == TowEventType.PushContact;
        double rel = Math.IEEERemainder(sys.Tug.Model.Psi - m.Psi, 2 * Math.PI) * 180 / Math.PI;
        TestContext.Out.WriteLine($"Толкает у миделя 50 %: лагом {m.V / ShipParameters.KnotsToMs:F2} уз, курс судна {m.Psi * 180 / Math.PI:F1}°, буксир к ДП {rel:F1}°, упор {sys.PushForce / 1e3:F0} кН");
        Assert.IsTrue(contact);
        Assert.Less(m.V, -0.5 * ShipParameters.KnotsToMs, "судно идёт на ЛБ");
        Assert.Less(Math.Abs(Math.IEEERemainder(m.Psi, 2 * Math.PI)) * 180 / Math.PI, 15.0, "без разворота");
        Assert.AreEqual(-90.0, rel, 5.0, "буксир перпендикулярно борту (с ПБ, носом на ЛБ)");
        Assert.Greater(sys.PushForce, 0.0, "в касании");
    }

    [Test]
    public void PullFromStern_SternToPortBowToStarboard()
    {
        TowingTrials.Run r = TowingTrials.Lateral(Ship(), TowingTrials.TugParameters(), TugMode.Pull, -0.45, 0.5, 0.0, 30);
        TestContext.Out.WriteLine($"Тянет от кормы 50 %, 30 с: Δψ {r.DeltaPsiDeg:F1}°, натяжение наибольшее {r.MaxForceKn:F1} кН");
        Assert.Greater(r.DeltaPsiDeg, 5.0, "корма на ЛБ — нос вправо");
        Assert.Greater(r.MaxForceKn, 20.0);
    }

    [Test]
    public void TowLine_OverloadAndBreak()
    {
        var line = new TowLine(100e3);
        line.MakeFast(30);
        Assert.AreEqual(MooringLineEvent.None, line.Update(30.0, 0.02, 3e5));
        Assert.AreEqual(0.0, line.Tension);
        MooringLineEvent ev = MooringLineEvent.None;
        double len = 30;
        while (ev == MooringLineEvent.None && len < 40) { len += 0.05; ev = line.Update(len, 0.02, 3e5); }
        Assert.AreEqual(MooringLineEvent.Overload, ev);
        while (ev != MooringLineEvent.Broken && len < 45) { len += 0.05; ev = line.Update(len, 0.02, 3e5); }
        Assert.AreEqual(MooringLineEvent.Broken, ev);
        Assert.IsTrue(line.Broken);
        Assert.IsFalse(line.Fast);
        Assert.AreEqual("тянуть на ЛБ, 50 %", TugCommand.Pull(270, 0.5).Describe());
        Assert.AreEqual("толкать вперёд-ПБ, 25 %", TugCommand.Push(44, 0.25).Describe());
    }
}
