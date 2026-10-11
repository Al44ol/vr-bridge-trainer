// ============================================================
// TrafficTests.cs — EditMode-тесты других судов и навигационных огней (B3)
// Window → General → Test Runner → EditMode → Run All.
// ============================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class TrafficTests
{
    private const double Dt = 0.02;
    private const double Deg = 180.0 / Math.PI;
    private const double Nm = 1852.0;

    // ─────────── Суда (значения ассетов проекта) ───────────
    private static ShipParameters Model45m()
    {
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        p.length = 45f; p.serviceSpeedKnots = 15f;
        p.accelerationTimeConstant = 30f; p.decelerationTimeConstant = 60f; p.asternFactor = 0.6f;
        p.nomotoKPrime = 0.8f; p.nomotoTPrime = 2f; p.minCalcSpeed = 0.05f;
        p.maxRudderAngle = 35f; p.rudderRate = 65f / 28f;
        p.beam = 8.71f; p.draft = 3f;
        return p;
    }

    // ShipParameters_Mariner (A3.2, Норрбин); B — MARAD, осадка — заглушка
    private static ShipParameters Mariner()
    {
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        p.length = 160.93f; p.serviceSpeedKnots = 15.001633f;
        p.accelerationTimeConstant = 95.196686f; p.decelerationTimeConstant = 150f; p.asternFactor = 0.6f;
        p.nomotoKPrime = 3.4845078f; p.nomotoTPrime = 3.969945f; p.minCalcSpeed = 0.05f;
        p.yawModel = YawModel.Norrbin; p.norrbinAlphaPrime = 53.740433f; p.turnSpeedLossCoeff = 0.8062914f;
        p.maxRudderAngle = 35f; p.rudderRate = 5f;
        p.beam = 23.16f; p.draft = 8f;
        return p;
    }

    // ShipParameters_KCS (A3.2, Норрбин); B, d — [7] раздела 3
    private static ShipParameters Kcs()
    {
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        p.length = 230f; p.serviceSpeedKnots = 24.002666f;
        p.accelerationTimeConstant = 90f; p.decelerationTimeConstant = 150f; p.asternFactor = 0.6f;
        p.nomotoKPrime = 4.578444f; p.nomotoTPrime = 3.182008f; p.minCalcSpeed = 0.05f;
        p.yawModel = YawModel.Norrbin; p.norrbinAlphaPrime = 5.819143f;
        p.maxRudderAngle = 35f; p.rudderRate = 2.32f;
        p.beam = 32.2f; p.draft = 10.8f;
        return p;
    }

    private static OwnShipInitialState Own(double e, double n, double course, double speedKn) =>
        new OwnShipInitialState { position = new WorldPosition(e, n), headingDeg = (float)course, speedKnots = (float)speedKn, telegraph = TelegraphOrder.FullAhead };

    // ─────────── CPA / TCPA ───────────
    [Test]
    public void Cpa_MatchesManualCalculation()
    {
        // Своё: (0; 0), 000°, 10 уз. Цель: (1000; 2000), 270°, 10 уз.
        // V_r = (−v, −v): линия относительного движения идёт под 45°,
        // CPA = |1000·(−1) − 2000·(−1)|/√2 = 707,1 м; TCPA = (3000/√2)/(v·√2) = 3000/(2v).
        CpaResult r = CpaCalculator.FromCourses(new WorldPosition(0, 0), 0.0, 10.0, new WorldPosition(1000, 2000), 270.0, 10.0);
        double v = 10.0 * ShipParameters.KnotsToMs;
        Assert.AreEqual(1000.0 / Math.Sqrt(2.0), r.Cpa, 1e-6);
        Assert.AreEqual(3000.0 / (2.0 * v), r.Tcpa, 1e-6);
        Assert.AreEqual(Math.Sqrt(5e6), r.Range, 1e-6);
        Assert.AreEqual(Math.Atan2(1000, 2000) * Deg, r.BearingDeg, 1e-9);

        // Одинаковые скорости: дистанция не меняется, TCPA = 0, CPA = дистанция
        r = CpaCalculator.FromCourses(new WorldPosition(0, 0), 45.0, 12.0, new WorldPosition(300, -400), 45.0, 12.0);
        Assert.AreEqual(0.0, r.Tcpa, 1e-9);
        Assert.AreEqual(500.0, r.Cpa, 1e-9);

        // Расходятся: TCPA < 0
        r = CpaCalculator.FromCourses(new WorldPosition(0, 0), 0.0, 10.0, new WorldPosition(0, -1000), 180.0, 10.0);
        Assert.Less(r.Tcpa, 0.0);
    }

    [TestCase(270.0, 10.0, 0.5, 12.0, MeetingSide.Ahead)]
    [TestCase(270.0, 10.0, 0.5, 12.0, MeetingSide.Astern)]
    [TestCase(315.0, 12.0, 0.3, 10.0, MeetingSide.Starboard)]
    [TestCase(180.0, 12.0, 0.3, 15.0, MeetingSide.Port)]
    [TestCase(180.0, 12.0, 0.3, 15.0, MeetingSide.Starboard)]
    [TestCase(0.0, 6.0, 0.2, 8.0, MeetingSide.Starboard)]
    [TestCase(90.0, 14.0, 0.0, 9.0, MeetingSide.Starboard)]
    public void MeetingStart_ForwardCheck_GivesRequestedCpaTcpaAndSide(double tgtCourse, double tgtSpeed, double cpaNm, double tcpaMin, MeetingSide side)
    {
        var own = new WorldPosition(100, -2500);
        const double ownCourse = 20.0, ownSpeed = 15.0;
        Assert.IsTrue(CpaCalculator.MeetingStart(own, ownCourse, ownSpeed, tgtCourse, tgtSpeed, cpaNm * Nm, tcpaMin * 60.0, side,
                                                 out WorldPosition start, out _));
        CpaResult r = CpaCalculator.FromCourses(own, ownCourse, ownSpeed, start, tgtCourse, tgtSpeed);
        Assert.AreEqual(cpaNm * Nm, r.Cpa, 1e-6);
        Assert.AreEqual(tcpaMin * 60.0, r.Tcpa, 1e-6);
        if (cpaNm <= 0.0) return;

        // Цель в момент TCPA — с заданной стороны своего судна
        CpaCalculator.Velocity(ownCourse, ownSpeed, out double oe, out double on);
        CpaCalculator.Velocity(tgtCourse, tgtSpeed, out double te, out double tn);
        double T = tcpaMin * 60.0;
        double re = start.East + te * T - (own.East + oe * T), rn = start.North + tn * T - (own.North + on * T);
        double psi = ownCourse / Deg;
        double fwd = re * Math.Sin(psi) + rn * Math.Cos(psi), stbd = re * Math.Cos(psi) - rn * Math.Sin(psi);
        switch (side)
        {
            case MeetingSide.Starboard: Assert.Greater(stbd, 0.0); break;
            case MeetingSide.Port: Assert.Less(stbd, 0.0); break;
            case MeetingSide.Ahead: Assert.Greater(fwd, 0.0); break;
            case MeetingSide.Astern: Assert.Less(fwd, 0.0); break;
        }
    }

    // «Точка встречи» на моделях движения: своё судно (Model45m) прямо, руль 0, 15 уз;
    // цель — TrafficVessel (тот же ShipDynamicsModel со своими параметрами + авторулевой).
    [TestCase("Mariner", 270.0, 12.0, 0.5, 12.0, MeetingSide.Ahead)]
    [TestCase("Mariner", 180.0, 12.0, 0.3, 10.0, MeetingSide.Port)]
    [TestCase("KCS", 300.0, 20.0, 1.0, 15.0, MeetingSide.Astern)]
    [TestCase("Model45m", 90.0, 10.0, 0.0, 8.0, MeetingSide.Starboard)]
    [TestCase("Model45m", 0.0, 6.0, 0.2, 10.0, MeetingSide.Starboard)]
    public void MeetingPoint_SimulatedWithModels_GivesRequestedCpa(string ship, double tgtCourse, double tgtSpeed, double cpaNm, double tcpaMin, MeetingSide side)
    {
        ShipParameters own = Model45m();
        ShipParameters tp = ship == "Mariner" ? Mariner() : ship == "KCS" ? Kcs() : Model45m();
        OwnShipInitialState init = Own(0, -2500, 0.0, 15.0);
        var def = new TrafficShipDefinition
        {
            placement = TrafficPlacement.MeetingPoint, cpaNm = (float)cpaNm, tcpaMinutes = (float)tcpaMin, meetingSide = side,
            headingDeg = (float)tgtCourse, speedKnots = (float)tgtSpeed, parameters = tp
        };
        TrafficStart st = TrafficPlanner.Resolve(def, init);
        var target = new TrafficVessel(0, "T1", def, tp, st);
        var m = new ShipDynamicsModel(own) { East = 0, North = -2500, Psi = 0, U = 15.0 * ShipParameters.KnotsToMs, SpeedFactor = 1.0 };

        double minD = double.MaxValue, tMin = 0, t = 0, T = tcpaMin * 60.0;
        while (t < 2.0 * T)
        {
            m.Step(Dt);
            target.Step(Dt);
            t += Dt;
            double d = (target.Position - new WorldPosition(m.East, m.North)).Magnitude;
            if (d < minD) { minD = d; tMin = t; }
        }
        TestContext.WriteLine($"{ship}: CPA модели {minD:F2} м (задано {cpaNm * Nm:F1}), t = {tMin:F2} с (TCPA {T:F0} с)");
        Assert.AreEqual(cpaNm * Nm, minD, 1.0);
        Assert.AreEqual(T, tMin, cpaNm > 0 ? 1.0 : 0.1);
    }

    // ─────────── Авторулевой ───────────
    [Test]
    public void Autopilot_Gains_Model45m_MatchTestAutopilotOfB2()
    {
        ShipParameters p = Model45m();
        HeadingAutopilot.Gains(p, 15.0 * ShipParameters.KnotsToMs, 0.75, 0.7, 0.25, out double kp, out double kd);
        TestContext.WriteLine($"Model45m: K_p = {kp:F3}, K_d = {kd:F2} с");
        Assert.AreEqual(1.41, kp, 0.02);
        Assert.AreEqual(8.0, kd, 0.1);
        HeadingAutopilot.Gains(Mariner(), 15.0 * ShipParameters.KnotsToMs, 0.75, 0.7, 0.25, out kp, out kd);
        TestContext.WriteLine($"Mariner: K_p = {kp:F3}, K_d = {kd:F2} с");
        HeadingAutopilot.Gains(Kcs(), 24.0 * ShipParameters.KnotsToMs, 0.75, 0.7, 0.25, out kp, out kd);
        TestContext.WriteLine($"KCS: K_p = {kp:F3}, K_d = {kd:F2} с");
    }

    // Смена курса на 30°: перерегулирование и время установления (±1°) в единицах L/U
    [TestCase("Model45m", 15.0)]
    [TestCase("Mariner", 15.0)]
    [TestCase("KCS", 24.0)]
    [TestCase("Mariner", 6.0)]
    public void Autopilot_CourseChange30_SmallOvershootAndSettles(string ship, double speedKn)
    {
        ShipParameters p = ship == "Mariner" ? Mariner() : ship == "KCS" ? Kcs() : Model45m();
        AutopilotStep(p, speedKn, 30.0, out double overshoot, out double settle, out double maxRudder);
        double lu = p.length / (speedKn * ShipParameters.KnotsToMs);
        TestContext.WriteLine($"{ship} {speedKn} уз: перерегулирование {overshoot:F2}°, установление ±1° за {settle:F0} с = {settle / lu:F1}·L/U, max |δ| {maxRudder:F1}°");
        Assert.Less(overshoot, 3.0);
        Assert.Less(settle / lu, 25.0);
    }

    public static void AutopilotStep(ShipParameters p, double speedKn, double change, out double overshoot, out double settle, out double maxRudder,
                                     List<Vector2> heading = null, List<Vector2> rudder = null)
    {
        var def = new TrafficShipDefinition { headingDeg = 0f, speedKnots = (float)speedKn, parameters = p };
        var v = new TrafficVessel(0, "T", def, p, TrafficPlanner.Resolve(def, null)) { OrderedCourseDeg = change };
        overshoot = 0; settle = 0; maxRudder = 0;
        double t = 0;
        for (int i = 0; i < 60000; i++)
        {
            v.Step(Dt);
            t += Dt;
            double h = Math.IEEERemainder(v.Model.Psi * Deg, 360.0);
            overshoot = Math.Max(overshoot, h - change);
            maxRudder = Math.Max(maxRudder, Math.Abs(v.Model.Delta));
            if (Math.Abs(h - change) > 1.0) settle = t;
            if (heading != null && i % 25 == 0) heading.Add(new Vector2((float)t, (float)h));
            if (rudder != null && i % 25 == 0) rudder.Add(new Vector2((float)t, (float)v.Model.Delta));
        }
    }

    [Test]
    public void WheelOver_Model45m_NearOptimumOfB2()
    {
        double d = RouteFollower.WheelOverDistance(Model45m(), 15.0 * ShipParameters.KnotsToMs, 60.0, 20.0);
        TestContext.WriteLine($"Model45m, поворот 60°, 15 уз: упреждение {d:F0} м (оптимум B2 — 150 м)");
        Assert.That(d, Is.InRange(120.0, 200.0));
    }

    // Маршрут с поворотом 60° (как фарватер): точки пройдены, отклонение от линии пути ограничено
    [TestCase("Model45m", 15.0, 60.0)]
    [TestCase("Mariner", 12.0, 60.0)]
    [TestCase("Model45m", 6.0, 60.0)]
    [TestCase("Mariner", 10.0, 90.0)]
    public void RouteFollower_TurnPassesWaypoints_LimitedXte(string ship, double speedKn, double turnDeg)
    {
        ShipParameters p = ship == "Mariner" ? Mariner() : Model45m();
        double turn = turnDeg / Deg;
        var path = new List<WorldPosition>
        {
            new WorldPosition(0, 3000),
            new WorldPosition(3000 * Math.Sin(turn), 3000 + 3000 * Math.Cos(turn))
        };
        double maxDev = RunRoute(p, speedKn, new WorldPosition(0, 0), path, out bool finished);
        TestContext.WriteLine($"{ship} {speedKn} уз, поворот {turnDeg}°: max отклонение от пути {maxDev:F1} м = {maxDev / p.length:F2}·L");
        Assert.IsTrue(finished);
        Assert.Less(maxDev, 1.5 * p.length + 30.0);
    }

    // Наибольшее расстояние от ломаной пути (по всем участкам), м
    public static double RunRoute(ShipParameters p, double speedKn, WorldPosition start, List<WorldPosition> path, out bool finished,
                                  List<Vector2> track = null)
    {
        var def = new TrafficShipDefinition { position = start, speedKnots = (float)speedKn, motion = TrafficMotion.Route, route = path, parameters = p };
        var v = new TrafficVessel(0, "T", def, p, TrafficPlanner.Resolve(def, null));
        var pts = new List<WorldPosition> { start };
        pts.AddRange(path);
        double maxDev = 0, t = 0;
        while (!v.Route.Finished && t < 7200)
        {
            v.Step(Dt);
            t += Dt;
            maxDev = Math.Max(maxDev, DistanceToPolyline(v.Position, pts));
            if (track != null && ((int)(t / Dt)) % 50 == 0) track.Add(new Vector2((float)v.Model.East, (float)v.Model.North));
        }
        finished = v.Route.Finished;
        return maxDev;
    }

    private static double DistanceToPolyline(WorldPosition p, List<WorldPosition> pts)
    {
        double best = double.MaxValue;
        for (int i = 0; i + 1 < pts.Count; i++) best = Math.Min(best, Polygon2D.PointSegmentDistance(p, pts[i], pts[i + 1]));
        return best;
    }

    // ─────────── Огни: правила 21, 22, 23, Приложение I ───────────
    [TestCase(160.93, 6, 3, 3)]
    [TestCase(50.0, 6, 3, 3)]
    [TestCase(45.0, 5, 2, 2)]
    [TestCase(15.0, 3, 2, 2)]
    [TestCase(10.0, 2, 1, 2)]
    public void Lights_Rule22_RangesByLength(double length, double masthead, double side, double stern)
    {
        NavigationLights.Ranges(length, out double m, out double s, out double st, out _);
        Assert.AreEqual(masthead, m);
        Assert.AreEqual(side, s);
        Assert.AreEqual(stern, st);
    }

    private static Dictionary<NavLightKind, double> Visible(List<NavLight> lights, double q)
    {
        var d = new Dictionary<NavLightKind, double>();
        foreach (NavLight l in lights) d[l.Kind] = NavigationLights.SectorFactor(l, q);
        return d;
    }

    [Test]
    public void Lights_Rule21_SectorsAroundVessel()
    {
        List<NavLight> lights = NavigationLights.PowerDrivenUnderway(HullProfile.For(TrafficVesselType.Cargo, 160.93, 23.16, 8));
        Assert.AreEqual(5, lights.Count, "≥ 50 м: два топовых, два бортовых, кормовой");

        // Прямо по носу: оба топовых и оба бортовых, кормовой не виден
        var v = Visible(lights, 0.0);
        Assert.AreEqual(1.0, v[NavLightKind.MastheadForward]); Assert.AreEqual(1.0, v[NavLightKind.MastheadAft]);
        Assert.AreEqual(1.0, v[NavLightKind.SidelightStarboard]); Assert.AreEqual(1.0, v[NavLightKind.SidelightPort]);
        Assert.AreEqual(0.0, v[NavLightKind.Sternlight]);

        // 45° и 90° ПБ: топовые и зелёный
        foreach (double q in new[] { 45.0, 90.0 })
        {
            v = Visible(lights, q);
            Assert.AreEqual(1.0, v[NavLightKind.MastheadForward]); Assert.AreEqual(1.0, v[NavLightKind.SidelightStarboard]);
            Assert.AreEqual(0.0, v[NavLightKind.SidelightPort]); Assert.AreEqual(0.0, v[NavLightKind.Sternlight]);
        }
        // 45° ЛБ: топовые и красный
        v = Visible(lights, 315.0);
        Assert.AreEqual(1.0, v[NavLightKind.SidelightPort]); Assert.AreEqual(0.0, v[NavLightKind.SidelightStarboard]);

        // Граница 112,5° (22,5° позади траверза): топовый, зелёный и кормовой — по 50 % (перекрытие)
        v = Visible(lights, 112.5);
        Assert.AreEqual(0.5, v[NavLightKind.MastheadForward], 1e-9);
        Assert.AreEqual(0.5, v[NavLightKind.SidelightStarboard], 1e-9);
        Assert.AreEqual(0.5, v[NavLightKind.Sternlight], 1e-9);
        // 120° ПБ: только кормовой; 180°: только кормовой
        foreach (double q in new[] { 120.0, 180.0, 240.0 })
        {
            v = Visible(lights, q);
            Assert.AreEqual(1.0, v[NavLightKind.Sternlight]);
            Assert.AreEqual(0.0, v[NavLightKind.MastheadForward]);
            Assert.AreEqual(0.0, v[NavLightKind.SidelightStarboard]); Assert.AreEqual(0.0, v[NavLightKind.SidelightPort]);
        }

        // Острая граница бортовых вперёд: зелёный виден до 1° слева от носа, гаснет к 3°
        Assert.AreEqual(1.0, NavigationLights.SectorFactor(lights[2], 359.0), 1e-9);
        Assert.AreEqual(0.5, NavigationLights.SectorFactor(lights[2], 358.0), 1e-9);
        Assert.AreEqual(0.0, NavigationLights.SectorFactor(lights[2], 357.0), 1e-9);

        // Слепых секторов нет: при облёте по кругу всегда виден хотя бы один огонь
        for (double q = 0.0; q < 360.0; q += 0.25)
        {
            double sum = 0;
            foreach (NavLight l in lights) sum += NavigationLights.SectorFactor(l, q);
            Assert.Greater(sum, 0.49, $"q = {q}");
        }
    }

    [Test]
    public void Lights_Annex1_Positions()
    {
        foreach (TrafficVesselType type in new[] { TrafficVesselType.Cargo, TrafficVesselType.Tanker })
        {
            HullProfile h = HullProfile.For(type, 160.93, 23.16, 8);
            List<NavLight> l = NavigationLights.PowerDrivenUnderway(h);
            NavLight fwd = l.Find(x => x.Kind == NavLightKind.MastheadForward);
            NavLight aft = l.Find(x => x.Kind == NavLightKind.MastheadAft);
            NavLight stbd = l.Find(x => x.Kind == NavLightKind.SidelightStarboard);
            NavLight stern = l.Find(x => x.Kind == NavLightKind.Sternlight);
            double h1 = fwd.Height - h.Freeboard;
            Assert.GreaterOrEqual(h1, Math.Min(Math.Max(6.0, h.Beam), 12.0) - 1e-9, "п. 2 а i");
            Assert.GreaterOrEqual(aft.Height - fwd.Height, 4.5 - 1e-9, "п. 2 а ii");
            Assert.GreaterOrEqual(fwd.Forward - aft.Forward, Math.Min(0.5 * h.Length, 100.0) - 1e-9, "п. 3 а");
            Assert.LessOrEqual(0.5 * h.Length - fwd.Forward, 0.25 * h.Length, "п. 3 а: не дальше L/4 от форштевня");
            Assert.LessOrEqual(stbd.Height - h.Freeboard, 0.75 * h1 + 1e-9, "п. 2 ж");
            Assert.LessOrEqual(stbd.Forward, fwd.Forward, "п. 3 б: бортовые не впереди топового");
            Assert.AreEqual(0.5 * h.Beam, stbd.Starboard, 1e-9);
            Assert.Less(stern.Forward, -0.45 * h.Length);
        }
        // Малое судно (< 50 м): один топовый огонь, над рубкой
        HullProfile s = HullProfile.For(TrafficVesselType.Small, 45, 8.71, 3);
        List<NavLight> sl = NavigationLights.PowerDrivenUnderway(s);
        Assert.AreEqual(4, sl.Count);
        Assert.IsFalse(sl.Exists(x => x.Kind == NavLightKind.MastheadAft));
        Assert.Greater(sl[0].Height, s.HouseTop);
        Assert.AreEqual(5.0, sl[0].NominalRangeNm);
    }

    [Test]
    public void Lights_Allard_NominalRangeAtThresholdAndFog()
    {
        foreach (double D in new[] { 1.0, 2.0, 3.0, 5.0, 6.0 })
        {
            double I = NavigationLights.IntensityForRange(D);
            Assert.AreEqual(NavigationLights.ThresholdLux, NavigationLights.Illuminance(I, D * Nm, 0.8), 1e-15);
            Assert.AreEqual(D, NavigationLights.LuminousRangeNm(I, 0.8), 1e-6);
            Assert.AreEqual(0.0, NavigationLights.DisplayLevel(NavigationLights.Illuminance(I, D * Nm * 1.001, 0.8)));
        }
        // Приложение I, п. 8: при D = 6 миль I ≈ 94 кд (таблица: 94), 3 мили — 12, 2 — 4,3
        Assert.AreEqual(94.0, NavigationLights.IntensityForRange(6.0), 0.5);
        Assert.AreEqual(12.0, NavigationLights.IntensityForRange(3.0), 0.1);
        Assert.AreEqual(4.3, NavigationLights.IntensityForRange(2.0), 0.05);
        // Кошмидер: K = 0,8 ⇔ видимость ≈ 13,4 мили
        Assert.AreEqual(0.8, NavigationLights.TransmissivityFromVisibility(13.425 * Nm), 1e-3);
        // В тумане дальность меньше номинальной и убывает с видимостью
        double I6 = NavigationLights.IntensityForRange(6.0);
        double r1 = NavigationLights.LuminousRangeNm(I6, NavigationLights.TransmissivityFromVisibility(11533));
        double r2 = NavigationLights.LuminousRangeNm(I6, NavigationLights.TransmissivityFromVisibility(4325));
        TestContext.WriteLine($"Топовый 6 миль: при V = 6,2 мили — {r1:F2} мили, при V = 2,3 мили — {r2:F2} мили");
        Assert.Less(r1, 6.0); Assert.Less(r2, r1);
    }

    [Test]
    public void LightCharacter_ParsesIalaCharacteristics()
    {
        LightCharacter c = LightCharacter.Parse("Fl R 4s");
        Assert.AreEqual(NavLightColor.Red, c.Color);
        Assert.AreEqual(4.0, c.Period, 1e-9);
        Assert.IsTrue(c.IsOn(0.2)); Assert.IsFalse(c.IsOn(1.0)); Assert.IsTrue(c.IsOn(4.2));

        c = LightCharacter.Parse("Q(6)+LFl 15s");
        Assert.AreEqual(NavLightColor.White, c.Color);
        Assert.AreEqual(7, c.Flashes.Count);
        Assert.AreEqual(6.0, c.Flashes[6].on, 1e-9);
        Assert.AreEqual(2.0, c.Flashes[6].off - c.Flashes[6].on, 1e-9);
        Assert.AreEqual(15.0, c.Period, 1e-9);

        c = LightCharacter.Parse("Q");
        Assert.AreEqual(1.0, c.Period, 1e-9);
        c = LightCharacter.Parse("Fl(2) 5s");
        Assert.IsTrue(c.IsOn(1.7)); Assert.IsFalse(c.IsOn(1.2)); Assert.IsFalse(c.IsOn(3.0));
        c = LightCharacter.Parse("Fl G 4s");
        Assert.AreEqual(NavLightColor.Green, c.Color);
        c = LightCharacter.Parse("Iso 4s");
        Assert.IsTrue(c.IsOn(1.9)); Assert.IsFalse(c.IsOn(2.1));

        // Все огни знаков МАМС по умолчанию (B1) разбираются и хоть когда-то горят
        foreach (AidType t in (AidType[])Enum.GetValues(typeof(AidType)))
        {
            LightCharacter lc = LightCharacter.Parse(IalaMarks.Get(t).DefaultLight);
            Assert.Greater(lc.Flashes.Count, 0, t.ToString());
            Assert.Greater(lc.Period, 0.0, t.ToString());
        }
    }

    // ─────────── Столкновение ───────────
    [Test]
    public void HullContact_OneEventPerContact_WithHysteresis()
    {
        var c = new HullContact();
        int events = 0;
        // Цель 100 × 20 м неподвижна в (0; 0), курс 090; своё 45 × 9 м идёт на неё с юга
        HullFootprint target = HullFootprint.FromShip(0, 0, 90.0 / Deg, 0, 0, 100, 20);
        for (double n = -200; n <= 0; n += 0.3)
            if (c.Update(HullFootprint.FromShip(0, n, 0.0, 0, 0, 45, 9), target)) events++;
        Assert.AreEqual(1, events, "одно событие на контакт");
        Assert.IsTrue(c.InContact);
        // Отошли на 3 м (меньше гистерезиса 5 м) и снова коснулись — то же столкновение
        for (double n = 0; n >= -36; n -= 0.3) if (c.Update(HullFootprint.FromShip(0, n, 0.0, 0, 0, 45, 9), target)) events++;
        for (double n = -36; n <= 0; n += 0.3) if (c.Update(HullFootprint.FromShip(0, n, 0.0, 0, 0, 45, 9), target)) events++;
        Assert.AreEqual(1, events);
        // Разошлись на 50 м — новое касание даёт новое событие
        for (double n = 0; n >= -90; n -= 0.3) c.Update(HullFootprint.FromShip(0, n, 0.0, 0, 0, 45, 9), target);
        Assert.IsFalse(c.InContact);
        for (double n = -90; n <= 0; n += 0.3) if (c.Update(HullFootprint.FromShip(0, n, 0.0, 0, 0, 45, 9), target)) events++;
        Assert.AreEqual(2, events);
    }

    // ─────────── Сценарий B3-01 ───────────
    private static ScenarioDefinition DemoTraffic(out WaterArea area)
    {
        area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoTrafficScenario.Fill(s, area, Model45m(), Mariner(), Mariner(), Model45m());
        return s;
    }

    [Test]
    public void Traffic_EmptyScenarioStillValid_DemoTrafficValid()
    {
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        var b2 = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoScenario.Fill(b2, area, Model45m());
        CollectionAssert.IsEmpty(b2.Validate(), "B2-01 без целей — как раньше");

        ScenarioDefinition s = DemoTraffic(out _);
        List<string> problems = s.Validate();
        CollectionAssert.IsEmpty(problems, string.Join("\n", problems));
        Assert.AreEqual(3, s.traffic.Count);

        // Ошибки описания целей находятся
        s.traffic.Add(new TrafficShipDefinition { id = "T1", placement = TrafficPlacement.MeetingPoint, tcpaMinutes = 0f, speedKnots = 30f, parameters = Model45m(), headingDeg = 0f });
        problems = s.Validate();
        Assert.IsTrue(problems.Exists(p => p.Contains("повторяется")));
        Assert.IsTrue(problems.Exists(p => p.Contains("TCPA")));
        Assert.IsTrue(problems.Exists(p => p.Contains("больше эксплуатационной")));
    }

    // session.json (C1): цели — с рассчитанным стартом и путём; чтение обратно
    [Test]
    public void Session_TrafficWrittenToSessionJson_RoundTrip()
    {
        ScenarioDefinition s = DemoTraffic(out _);
        SessionMeta meta = SessionMeta.Create(s, s.shipParameters, "17", new DateTime(2026, 10, 11, 12, 0, 0));
        string json = JsonUtility.ToJson(meta, true);
        var back = new SessionMeta();
        JsonUtility.FromJsonOverwrite(json, back);
        Assert.AreEqual(3, back.scenario.traffic.Count);
        TrafficStart t2 = TrafficPlanner.Resolve(s.traffic[1], s.initial);
        SessionTraffic r2 = back.scenario.traffic[1];
        Assert.AreEqual("T2", r2.id);
        Assert.AreEqual("MeetingPoint", r2.placement);
        Assert.AreEqual(t2.Position.East, r2.x_e_m, 1e-3);
        Assert.AreEqual(t2.Position.North, r2.y_n_m, 1e-3);
        Assert.AreEqual(t2.Path.Count, r2.path.Count);
        Assert.AreEqual(4.0, r2.tcpa_min, 1e-9);
        // Точка встречи T2 (CPA 0) — там, где через 4 мин будет своё судно: (0; −2500 + 15 уз · 240 с)
        Assert.AreEqual(0.0, r2.meeting_x_e_m, 0.01);
        Assert.AreEqual(-2500.0 + 15.0 * ShipParameters.KnotsToMs * 240.0, r2.meeting_y_n_m, 0.01);
    }

    // Прогон B3-01: своё судно — авторулевой по маршруту (как в B2), без манёвров расхождения
    public struct TrafficRun
    {
        public double[] MinDistance, MinTime;
        public string FirstCollision;
        public double CollisionTime;
        public double[] MinDepthUnderTarget;
    }

    public static TrafficRun RunDemoTraffic(double maxTime, bool stopAtCollision, List<List<Vector2>> tracks = null)
    {
        ScenarioDefinition s = DemoTraffic(out WaterArea area);
        ShipParameters p = s.shipParameters;
        var m = new ShipDynamicsModel(p)
        {
            East = s.initial.position.East, North = s.initial.position.North, Psi = s.initial.headingDeg / Deg,
            U = s.initial.speedKnots * ShipParameters.KnotsToMs, SpeedFactor = 1.0
        };
        var ownRoute = new List<WorldPosition>();
        for (int i = 1; i < s.route.Count; i++) ownRoute.Add(s.route[i].position);
        var ownFollower = new RouteFollower(s.initial.position, ownRoute);
        var ap = new HeadingAutopilot();

        var vessels = new List<TrafficVessel>();
        var contacts = new List<HullContact>();
        for (int i = 0; i < s.traffic.Count; i++)
        {
            TrafficShipDefinition d = s.traffic[i];
            vessels.Add(new TrafficVessel(i, d.TargetId(i), d, d.parameters, TrafficPlanner.Resolve(d, s.initial)));
            contacts.Add(new HullContact());
        }
        if (tracks != null) { tracks.Clear(); for (int i = 0; i <= vessels.Count; i++) tracks.Add(new List<Vector2>()); }

        var r = new TrafficRun
        {
            MinDistance = new double[vessels.Count], MinTime = new double[vessels.Count],
            MinDepthUnderTarget = new double[vessels.Count], FirstCollision = "", CollisionTime = double.NaN
        };
        for (int i = 0; i < vessels.Count; i++) { r.MinDistance[i] = double.MaxValue; r.MinDepthUnderTarget[i] = double.MaxValue; }

        double t = 0;
        int step = 0;
        while (t < maxTime)
        {
            m.RudderCommandDeg = ap.Rudder(m, ownFollower.Course(m));
            m.Step(Dt);
            foreach (TrafficVessel v in vessels) v.Step(Dt);
            t += Dt;
            step++;
            HullFootprint own = HullFootprint.FromShip(m.East, m.North, m.Psi, 0, 0, p.length, p.beam);
            for (int i = 0; i < vessels.Count; i++)
            {
                double d = (vessels[i].Position - new WorldPosition(m.East, m.North)).Magnitude;
                if (d < r.MinDistance[i]) { r.MinDistance[i] = d; r.MinTime[i] = t; }
                r.MinDepthUnderTarget[i] = Math.Min(r.MinDepthUnderTarget[i], area.DepthAt(vessels[i].Position));
                if (contacts[i].Update(own, vessels[i].Footprint) && string.IsNullOrEmpty(r.FirstCollision))
                {
                    r.FirstCollision = vessels[i].TargetId;
                    r.CollisionTime = t;
                    if (stopAtCollision) return r;
                }
            }
            if (tracks != null && step % 50 == 0)
            {
                tracks[0].Add(new Vector2((float)m.East, (float)m.North));
                for (int i = 0; i < vessels.Count; i++) tracks[i + 1].Add(new Vector2((float)vessels[i].Model.East, (float)vessels[i].Model.North));
            }
        }
        return r;
    }

    [Test]
    public void Demo_B3_NoManoeuvre_CollidesWithCrossingTanker_OthersPassClear()
    {
        TrafficRun r = RunDemoTraffic(1500.0, false);
        for (int i = 0; i < r.MinDistance.Length; i++)
            TestContext.WriteLine($"T{i + 1}: наименьшая дистанция {r.MinDistance[i]:F1} м в t = {r.MinTime[i]:F1} с; наименьшая глубина под целью {r.MinDepthUnderTarget[i]:F1} м");
        TestContext.WriteLine($"Первое столкновение: {r.FirstCollision} в t = {r.CollisionTime:F1} с");
        Assert.AreEqual("T2", r.FirstCollision, "курс столкновения с T2 (CPA 0, TCPA 4 мин)");
        Assert.AreEqual(240.0, r.CollisionTime, 30.0);
        Assert.Less(r.MinDistance[1], 20.0);
        // Встречный и обгоняемый проходят без касания, на десятках метров
        Assert.That(r.MinDistance[0], Is.InRange(50.0, 200.0), "T1 — встречный в фарватере");
        Assert.That(r.MinDistance[2], Is.InRange(40.0, 200.0), "T3 — обгоняемый");
        // Цели не заходят на мели и сушу (осадка Mariner 8 м, Model45m 3 м)
        Assert.Greater(r.MinDepthUnderTarget[0], 8.5);
        Assert.Greater(r.MinDepthUnderTarget[1], 8.5);
        Assert.Greater(r.MinDepthUnderTarget[2], 3.5);
    }
}
