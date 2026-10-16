// ============================================================
// OwnTugTests.cs — Свой буксир: джойстик и две азимутальные колонки (запрос 19.4а, Core)
//
// Мёртвая зона и рукоятка; пределы осей X_max, Y_max, N_max — колонки ровно на пределе, без лишних сил;
// насыщение сохраняет направление; модель 3 СС буксира: полный вперёд/назад ≈ 11–13 уз, лагом — без разворота,
// разворот — на месте; джойстик в ноль — буксир останавливается. Событие JoystickChanged — текст.
// ============================================================
using NUnit.Framework;
using UnityEngine;

public class OwnTugTests
{
    private static ShipParameters Tug()
    {
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        p.name = "ShipParameters_Tug";
        ShipPresets.FillTug(p);
        return p;
    }

    private static ShipDynamicsModel Run(ShipParameters p, AzimuthDrive d, double seconds, ShipDynamicsModel m = null)
    {
        m = m ?? new ShipDynamicsModel(p) { MotionOverride = MotionModel.Manoeuvring3Dof, SpeedFactor = 0.0 };
        const double dt = 0.02;
        for (int i = 0, n = (int)(seconds / dt); i < n; i++)
        {
            d.Apply(m, dt);
            m.Step(dt);
        }
        return m;
    }

    private const double Kn = ShipParameters.KnotsToMs;

    [Test]
    public void Joystick_DeadZoneAndHandle()
    {
        Assert.AreEqual(0.0, TugJoystick.Shape(0.05));
        Assert.AreEqual(0.0, TugJoystick.Shape(-0.1));
        Assert.AreEqual(0.5, TugJoystick.Shape(0.55), 1e-12);
        Assert.AreEqual(-1.0, TugJoystick.Shape(-1.0), 1e-12);
        JoystickState j = TugJoystick.FromHandle(25, -10, 45);
        Assert.AreEqual(1.0, j.Surge, 1e-12, "наклон больше предела — полный ход");
        Assert.AreEqual(-0.5, j.Sway, 1e-12);
        Assert.AreEqual(1.0, j.Yaw, 1e-12);
        Assert.AreEqual(0.75, TugJoystick.Step(0.5, +1), 1e-12);
        Assert.AreEqual(-1.0, TugJoystick.Step(-1.0, -1), 1e-12);
        Assert.AreEqual("ход вперёд 50 %, лагом на ЛБ 25 %", TugJoystick.Describe(new JoystickState(0.5, -0.25, 0)));
        Assert.AreEqual("в нуле", TugJoystick.Describe(JoystickState.Zero));
    }

    [Test]
    public void Allocation_AxisLimitsLoadUnitsExactly()
    {
        TugSpec s = TugSpec.For(Tug());
        double half = 0.5 * s.BollardPull;
        void Check(double x, double y, double n, string what)
        {
            double k = AzimuthAllocation.Solve(x, y, n, s.ThrusterX, s.ThrusterY, half, out double fx1, out double fy1, out double fx2, out double fy2);
            Assert.AreEqual(1.0, k, 1e-9, what + ": без насыщения");
            Assert.AreEqual(half, System.Math.Sqrt(fx1 * fx1 + fy1 * fy1), 1e-6 * half, what + ": колонка ПБ на пределе");
            Assert.AreEqual(half, System.Math.Sqrt(fx2 * fx2 + fy2 * fy2), 1e-6 * half, what + ": колонка ЛБ на пределе");
            AzimuthAllocation.Resultant(fx1, fy1, fx2, fy2, s.ThrusterX, s.ThrusterY, out double rx, out double ry, out double rn);
            Assert.AreEqual(x, rx, 1e-6 * s.BollardPull, what + ": X");
            Assert.AreEqual(y, ry, 1e-6 * s.BollardPull, what + ": Y");
            Assert.AreEqual(n, rn, 1e-6 * s.BollardPull, what + ": N");
        }
        Check(TugJoystick.MaxSurge(s), 0, 0, "вперёд");
        Check(-TugJoystick.MaxSurge(s), 0, 0, "назад");
        Check(0, TugJoystick.MaxSway(s), 0, "лагом");
        Check(0, 0, TugJoystick.MaxYaw(s), "разворот");
        Assert.That(TugJoystick.MaxSway(s) / s.BollardPull, Is.InRange(0.15, 0.3), "лагом ≈ 0,22 тяги на гаке (колонки в корме)");
    }

    [Test]
    public void Allocation_SaturationKeepsDirection()
    {
        TugSpec s = TugSpec.For(Tug());
        TugJoystick.Demand(new JoystickState(1, 1, 1), s, out double x, out double y, out double n);
        double k = AzimuthAllocation.Solve(x, y, n, s.ThrusterX, s.ThrusterY, 0.5 * s.BollardPull, out double fx1, out double fy1, out double fx2, out double fy2);
        Assert.Less(k, 1.0, "три оси сразу — насыщение");
        AzimuthAllocation.Resultant(fx1, fy1, fx2, fy2, s.ThrusterX, s.ThrusterY, out double rx, out double ry, out double rn);
        Assert.AreEqual(k * x, rx, 1e-6 * s.BollardPull);
        Assert.AreEqual(k * y, ry, 1e-6 * s.BollardPull);
        Assert.AreEqual(k * n, rn, 1e-6 * s.BollardPull);
    }

    [Test]
    public void Drive_AheadAsternAndStop()
    {
        ShipParameters p = Tug();
        var d = new AzimuthDrive(TugSpec.For(p)) { Joystick = new JoystickState(1, 0, 0) };
        ShipDynamicsModel m = Run(p, d, 200);
        Assert.That(m.U / Kn, Is.InRange(10.0, 14.0), "полный вперёд — свободный ход буксира");
        Assert.AreEqual(0.0, m.Psi * 180 / System.Math.PI % 360, 0.1, "курс не уходит");
        Assert.AreEqual(1.0, d.UnitLoad(1), 1e-3);
        Assert.AreEqual(0.0, d.UnitDirectionDeg(1), 1e-6, "упор колонки вперёд");

        d.Joystick = new JoystickState(0.55, 0, 0);   // за мёртвой зоной — половина упора
        Run(p, d, 200, m);
        Assert.That(m.U / Kn, Is.InRange(5.0, 7.0), "половина упора — около половины хода (сопротивление линейное)");

        d.Joystick = JoystickState.Zero;
        Run(p, d, 120, m);
        Assert.Less(System.Math.Abs(m.U / Kn), 0.2, "джойстик в ноль — буксир останавливается");

        var back = new AzimuthDrive(TugSpec.For(p)) { Joystick = new JoystickState(-1, 0, 0) };
        ShipDynamicsModel mb = Run(p, back, 200);
        Assert.That(-mb.U / Kn, Is.InRange(10.0, 14.0), "полный назад — колонки развёрнуты на 180°");
        Assert.AreEqual(180.0, back.UnitDirectionDeg(2), 1e-6);
    }

    [Test]
    public void Drive_SidewaysWithoutTurn_SpinInPlace()
    {
        ShipParameters p = Tug();
        var d = new AzimuthDrive(TugSpec.For(p)) { Joystick = new JoystickState(0, 1, 0) };
        ShipDynamicsModel m = Run(p, d, 90);
        Assert.Greater(m.V / Kn, 1.5, "лагом на ПБ");
        Assert.Less(System.Math.Abs(m.U / Kn), 0.2, "без хода вперёд");
        double dpsi = System.Math.IEEERemainder(m.Psi * 180 / System.Math.PI, 360);
        Assert.Less(System.Math.Abs(dpsi), 2.0, "без разворота");
        Assert.Greater(m.East, 50.0, "сместился на восток (курс 0°, ПБ — восток)");

        var spin = new AzimuthDrive(TugSpec.For(p)) { Joystick = new JoystickState(0, 0, 1) };
        ShipDynamicsModel ms = Run(p, spin, 40);
        Assert.Greater(ms.R * 180 / System.Math.PI, 5.0, "разворот вправо, °/с");
        Assert.Less(System.Math.Sqrt(ms.East * ms.East + ms.North * ms.North), 2.0, "на месте");
    }

    [Test]
    public void Drive_SpoolAndEventText()
    {
        ShipParameters p = Tug();
        var d = new AzimuthDrive(TugSpec.For(p)) { Joystick = new JoystickState(1, 0, 0) };
        Run(p, d, d.Spec.SpoolTime);
        Assert.AreEqual(1.0 - System.Math.Exp(-1.0), d.ThrustX / d.OrderX, 0.01, "набор упора — звено 1-го порядка τ_к");
        d.Reset();
        Assert.IsTrue(d.Joystick.IsZero);
        Assert.AreEqual(0.0, d.ThrustX);

        var e = new ExerciseEvent(ExerciseEventType.JoystickChanged) { Value = 50, Value2 = 0, Index = -25, Subject = "рукой" };
        Assert.AreEqual("Джойстик: ход вперёд 50 %, разворот нос влево 25 % (рукой)", e.Describe());
    }
}
