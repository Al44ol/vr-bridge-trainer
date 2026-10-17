// ============================================================
// OwnTugTests.cs — Свой буксир: джойстик и две азимутальные колонки (запрос 19.4а, Core)
//
// Мёртвая зона и рукоятка; пределы осей X_max, Y_max, N_max — колонки ровно на пределе, без лишних сил;
// насыщение сохраняет направление; модель 3 СС буксира: полный вперёд/назад ≈ 11–13 уз, лагом — без разворота,
// разворот — на месте; джойстик в ноль — буксир останавливается. Событие JoystickChanged — текст.
// 19.4а-2: удержание курса — без него корпус на ходу уходит в циркуляцию, с ним курс держится на всех ходах;
// ось разворота — угловая скорость, отпустил — новый курс; при нехватке упора — приоритет курса.
// ============================================================
using System;
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
        // 19.4а-4: полный наклон 35°, полный поворот 45° (было 20° / 30°)
        JoystickState j = TugJoystick.FromHandle(40, -17.5, 60);
        Assert.AreEqual(1.0, j.Surge, 1e-12, "наклон больше предела — полный ход");
        Assert.AreEqual(-0.5, j.Sway, 1e-12);
        Assert.AreEqual(1.0, j.Yaw, 1e-12);
        Assert.AreEqual(0.5, TugJoystick.FromHandle(0, 0, 22.5).Yaw, 1e-12);
        // Меню — ступени упора: с 50 % упора на 75 %; с полного назад дальше некуда
        Assert.AreEqual(0.75, TugJoystick.Shape(TugJoystick.Step(TugJoystick.StepPosition(2), +1)), 1e-12);
        Assert.AreEqual(-1.0, TugJoystick.Step(-1.0, -1), 1e-12);
        // Подписи — в процентах упора (после мёртвой зоны), ход — с названием ступени
        Assert.AreEqual("ход вперёд малый 50 %, лагом на ЛБ 25 %",
            TugJoystick.Describe(new JoystickState(TugJoystick.StepPosition(2), TugJoystick.StepPosition(-1), 0)));
        Assert.AreEqual("ход вперёд 44 %", TugJoystick.Describe(new JoystickState(0.5, 0, 0)), "рукоятка на 50 % хода — 44 % упора");
        Assert.AreEqual("в нуле", TugJoystick.Describe(JoystickState.Zero));
    }

    // ─────────── 19.4а-4: ступени рукоятки ───────────

    [Test]
    public void Steps_GiveExactThrust_25_50_75_100()
    {
        string[] names = { "стоп", "самый малый", "малый", "средний", "полный" };
        for (int k = -TugJoystick.StepCount; k <= TugJoystick.StepCount; k++)
        {
            double pos = TugJoystick.StepPosition(k);
            Assert.AreEqual(0.25 * k, TugJoystick.Shape(pos), 1e-12, $"ступень {k}: упор ровно {25 * k} %");
            Assert.AreEqual(k, TugJoystick.NearestStep(pos));
            Assert.AreEqual(names[System.Math.Abs(k)], TugJoystick.StepName(k));
        }
        Assert.AreEqual(0.0, TugJoystick.StepPosition(0));
        Assert.AreEqual(1.0, TugJoystick.StepPosition(4), 1e-12, "полный — рукоятка до упора");
        Assert.AreEqual(0.325, TugJoystick.StepPosition(1), 1e-12, "самый малый — за мёртвой зоной");
        // Наклоны ступеней хода при 35°: ≈ 11,4° / 19,3° / 27,1° / 35°
        Assert.AreEqual(11.375, TugJoystick.StepPosition(1) * TugJoystick.MaxTiltDeg, 1e-9);
        Assert.AreEqual(27.125, TugJoystick.StepPosition(3) * TugJoystick.MaxTiltDeg, 1e-9);
        // Ближайшая ступень — по положению рукоятки; середина между нулём и самым малым — ещё «стоп»
        Assert.AreEqual(0, TugJoystick.NearestStep(0.16));
        Assert.AreEqual(1, TugJoystick.NearestStep(0.17));
        Assert.AreEqual(-2, TugJoystick.NearestStep(-0.5));
        JoystickState s = TugJoystick.Snap(new JoystickState(0.6, -0.05, 0.8));
        Assert.AreEqual(0.5, TugJoystick.Shape(s.Surge), 1e-12);
        Assert.AreEqual(0.0, s.Sway);
        Assert.AreEqual(0.75, TugJoystick.Shape(s.Yaw), 1e-12);
        // Автомат буксира 19.4б: Unshape — тот же, что у ступеней (обратное Shape)
        Assert.AreEqual(TugJoystick.Unshape(0.12), TugWorkAutopilot.Unshape(0.12));
        Assert.AreEqual(0.12, TugJoystick.Shape(TugJoystick.Unshape(0.12)), 1e-12);
    }

    [Test]
    public void ThrustTitles_NamedStepsOnlyForSurge()
    {
        Assert.AreEqual("вперёд самый малый 25 %", TugJoystick.ThrustTitle(0.25, "вперёд", "назад", true));
        Assert.AreEqual("назад полный 100 %", TugJoystick.ThrustTitle(-1.0, "вперёд", "назад", true));
        Assert.AreEqual("вперёд 37 %", TugJoystick.ThrustTitle(0.37, "вперёд", "назад", true), "между ступенями — без названия");
        Assert.AreEqual("на ПБ 75 %", TugJoystick.ThrustTitle(0.75, "на ПБ", "на ЛБ", false));
        Assert.AreEqual("0", TugJoystick.ThrustTitle(0.001, "вперёд", "назад", true));
        Assert.AreEqual("вперёд средний 75 %", TugJoystick.SurgeTitle(TugJoystick.StepPosition(3)));
        Assert.AreEqual("нос влево 50 %", TugJoystick.YawTitle(TugJoystick.StepPosition(-2)));
        Assert.AreEqual("ход назад самый малый 25 %, разворот нос вправо 100 %", TugJoystick.DescribeThrust(-0.25, 0, 1.0));
        // Индикатор у рукоятки — движение буксира
        double kn = ShipParameters.KnotsToMs;
        Assert.AreEqual("вперёд 5,8 уз · лагом 0,3 уз на ПБ · 1,2 °/с вправо", TugJoystick.MotionTitle(5.8 * kn, 0.3 * kn, 1.2 * System.Math.PI / 180.0));
        Assert.AreEqual("назад 2,0 уз · лагом 0 · 0,5 °/с влево", TugJoystick.MotionTitle(-2.0 * kn, 0.01, -0.5 * System.Math.PI / 180.0));
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
        Assert.AreEqual("Джойстик: ход вперёд малый 50 %, разворот нос влево 25 % (рукой)", e.Describe());
    }

    // ─────────── 19.4а-2: удержание курса ───────────

    private static ShipDynamicsModel Kicked(ShipParameters p, AzimuthDrive d, double surge)
    {
        ShipDynamicsModel m = Run(p, d, 90, null, new JoystickState(surge, 0, 0));
        Run(p, d, 2, m, new JoystickState(surge, 0, 0.4));   // толчок рукоятки на разворот
        return Run(p, d, 30, m, new JoystickState(surge, 0, 0));
    }

    private static ShipDynamicsModel Run(ShipParameters p, AzimuthDrive d, double seconds, ShipDynamicsModel m, JoystickState j)
    {
        d.Joystick = j;
        return Run(p, d, seconds, m);
    }

    [Test]
    public void HeadingHold_KickAtSpeed_CourseHeld()
    {
        ShipParameters p = Tug();
        var raw = new AzimuthDrive(TugSpec.For(p)) { HeadingHold = false };
        ShipDynamicsModel mr = Kicked(p, raw, 0.55);
        Assert.Greater(Math.Abs(mr.R * 180 / Math.PI), 3.0, "без удержания корпус на ходу сам уходит в циркуляцию (ThesisNotes 19.29.6)");

        var hold = new AzimuthDrive(TugSpec.For(p));
        Assert.IsTrue(hold.HeadingHold, "по умолчанию — включено");
        ShipDynamicsModel m = Kicked(p, hold, 0.55);
        double psi0 = m.Psi;
        double maxR = 0;
        for (int i = 0; i < 30; i++) { Run(p, hold, 1.0, m); maxR = Math.Max(maxR, Math.Abs(m.R * 180 / Math.PI)); }
        Assert.Less(maxR, 0.5, "°/с");
        Assert.Less(Math.Abs(Math.IEEERemainder(m.Psi - psi0, 2 * Math.PI)) * 180 / Math.PI, 1.0, "курс держится");
        Assert.That(m.U / Kn, Is.InRange(5.0, 7.5));
    }

    [Test]
    public void HeadingHold_YawAxisIsRate_ReleaseHoldsNewCourse()
    {
        ShipParameters p = Tug();
        var d = new AzimuthDrive(TugSpec.For(p));
        ShipDynamicsModel m = Run(p, d, 60, null, new JoystickState(0.55, 0, 0));
        Run(p, d, 10, m, new JoystickState(0.55, 0, 1));
        Assert.That(m.R * 180 / Math.PI, Is.InRange(8.0, 11.0), "полный разворот — около RateMaxDegS");
        Assert.IsTrue(double.IsNaN(d.HoldHeadingDeg), "пока разворот — курс не держится");
        Run(p, d, 20, m, new JoystickState(0.55, 0, 0));
        Assert.Less(Math.Abs(m.R * 180 / Math.PI), 0.2, "отпустил — поворот погашен");
        Assert.IsFalse(double.IsNaN(d.HoldHeadingDeg), "новый курс запомнен");
        double psi0 = m.Psi;
        Run(p, d, 30, m);
        Assert.Less(Math.Abs(Math.IEEERemainder(m.Psi - psi0, 2 * Math.PI)) * 180 / Math.PI, 0.5, "и держится");

        var spin = new AzimuthDrive(TugSpec.For(p));
        ShipDynamicsModel ms = Run(p, spin, 40, null, new JoystickState(0, 0, 1));
        Assert.Less(Math.Sqrt(ms.East * ms.East + ms.North * ms.North), 2.0, "на месте — разворот без сноса");
    }

    [Test]
    public void HeadingHold_FullAheadAndAstern_PriorityToHeading()
    {
        ShipParameters p = Tug();
        foreach (double surge in new[] { 1.0, -1.0 })
        {
            var d = new AzimuthDrive(TugSpec.For(p));
            ShipDynamicsModel m = Kicked(p, d, surge);
            double psi0 = m.Psi;
            Run(p, d, 30, m);
            Assert.Less(Math.Abs(Math.IEEERemainder(m.Psi - psi0, 2 * Math.PI)) * 180 / Math.PI, 2.0, $"ход {surge}: курс держится");
            Assert.Greater(Math.Abs(m.U / Kn), 10.0, $"ход {surge}: почти полный");
        }
        // Нехватка упора: ход уменьшается первым, момент и поперечная сила — сохраняются
        TugSpec s = TugSpec.For(p);
        var mm = new ShipDynamicsModel(p) { MotionOverride = MotionModel.Manoeuvring3Dof };
        double n = 0.8 * TugJoystick.MaxYaw(s);
        AzimuthAllocation.ApplyToHeadingFirst(mm, s, s.BollardPull, 0.0, n, out double fx1, out double fy1, out double fx2, out double fy2);
        AzimuthAllocation.Resultant(fx1, fy1, fx2, fy2, s.ThrusterX, s.ThrusterY, out double rx, out _, out double rn);
        Assert.AreEqual(n, rn, 1e-3 * n, "момент — полностью");
        Assert.Less(rx, s.BollardPull, "ход уменьшен");
        Assert.Greater(rx, 0.0);
    }

    // ─────────── 19.4а-5: пульт буксира ───────────

    [Test]
    public void ConsoleMonitor_Lines()
    {
        var d = new TugMonitorData
        {
            HeadingDeg = 329.94, SpeedKn = 5.83, SwayKn = -0.22, RotDegMin = 72.4, HeadingHold = true, HoldHeadingDeg = double.NaN,
            DepthM = 30.0, DraftM = 3.5, WindKn = 20.0, WindFromDeg = 330.0, Berth = "", Pilot = "Лоцман: «Толкайте к причалу»", Failure = ""
        };
        string m = TugConsoleText.Monitor(d);
        StringAssert.Contains("ГЛУБИНА 30 м <size=75%>под килем 26,5 м", m);
        StringAssert.Contains("<b>КУРС</b> 329,9°", m.Replace('.', ','));
        StringAssert.Contains("автомат: разворот", m);
        StringAssert.Contains("<b>ХОД</b> 5,8 уз вперёд", m);
        StringAssert.Contains("<b>ЛАГОМ</b> 0,22 уз на ЛБ", m);
        StringAssert.Contains("<b>ПОВОРОТ</b> 72 °/мин вправо", m);
        StringAssert.Contains("ветер 20 уз, откуда 330°", m);
        StringAssert.Contains("Лоцман: «Толкайте к причалу»", m);
        StringAssert.DoesNotContain("РУЛЬ", m, "у буксира руля и телеграфа нет");

        d.HoldHeadingDeg = -30.0; d.SpeedKn = 0.01; d.SwayKn = 0.001; d.RotDegMin = 0.2; d.WindKn = 0; d.Alarm = "НА МЕЛИ"; d.Failure = "ОТКАЗ ГД";
        m = TugConsoleText.Monitor(d);
        StringAssert.Contains("НА МЕЛИ", m);
        StringAssert.DoesNotContain("ГЛУБИНА", m, "сигнализация вместо глубины");
        StringAssert.Contains("автомат 330°", m);
        StringAssert.Contains("<b>ХОД</b> 0", m);
        StringAssert.Contains("<b>ЛАГОМ</b> 0", m);
        StringAssert.Contains("<b>ПОВОРОТ</b> 0", m);
        StringAssert.Contains("ветра нет", m);
        StringAssert.Contains("ОТКАЗ ГД", m);
        d.Alarm = ""; d.DepthM = double.NaN; d.HeadingHold = false;
        m = TugConsoleText.Monitor(d);
        StringAssert.Contains("ГЛУБИНА —", m);
        StringAssert.Contains("автомат выкл", m);
    }

    [Test]
    public void ConsolePods_DirectionAndLoad()
    {
        Assert.AreEqual("ПБ 045° · 50 %", TugConsoleText.Pod("ПБ", 45.2, 0.5));
        Assert.AreEqual("ЛБ 315° · 100 %", TugConsoleText.Pod("ЛБ", -45.0, 1.3));
        Assert.AreEqual("ПБ — · 0 %", TugConsoleText.Pod("ПБ", 180.0, 0.001));
        // Колонки по джойстику: ход вперёд — обе вперёд (0°), загрузка — доля T_гак/2
        var d = new AzimuthDrive(TugSpec.For(Tug())) { HeadingHold = false };
        d.Joystick = new JoystickState(TugJoystick.StepPosition(2), 0, 0);
        var m = new ShipDynamicsModel(Tug()) { MotionOverride = MotionModel.Manoeuvring3Dof };
        for (int i = 0; i < 1000; i++) { d.Apply(m, 0.02); m.Step(0.02); }
        StringAssert.StartsWith("ПБ 000° · 50 %", TugConsoleText.Pod("ПБ", d.UnitDirectionDeg(1), d.UnitLoad(1)));
        StringAssert.StartsWith("ЛБ 000° · 50 %", TugConsoleText.Pod("ЛБ", d.UnitDirectionDeg(2), d.UnitLoad(2)));
    }
}
