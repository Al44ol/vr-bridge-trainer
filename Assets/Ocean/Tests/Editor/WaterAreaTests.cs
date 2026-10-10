// ============================================================
// WaterAreaTests.cs — EditMode-тесты учебной акватории и посадки на мель (B1)
// Window → General → Test Runner → EditMode → Run All.
// ============================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class WaterAreaTests
{
    private const double Dt = 0.02;
    private const double Deg = 180.0 / Math.PI;

    // Судно сцены (ShipParameters_Model45m)
    private static ShipParameters MakeShip()
    {
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        p.length = 45f; p.serviceSpeedKnots = 15f;
        p.accelerationTimeConstant = 30f; p.decelerationTimeConstant = 60f; p.asternFactor = 0.6f;
        p.nomotoKPrime = 0.8f; p.nomotoTPrime = 2f; p.minCalcSpeed = 0.05f;
        p.maxRudderAngle = 35f; p.rudderRate = 65f / 28f;
        p.beam = 8.71f; p.draft = 3f;
        return p;
    }

    private static List<WorldPosition> Poly(params double[] en)
    {
        var l = new List<WorldPosition>();
        for (int i = 0; i + 1 < en.Length; i += 2) l.Add(new WorldPosition(en[i], en[i + 1]));
        return l;
    }

    private static WaterArea AreaWith(params ShoalArea[] shoals)
    {
        var a = ScriptableObject.CreateInstance<WaterArea>();
        a.shoals = new List<ShoalArea>(shoals);
        return a;
    }

    private static GroundingDetector Detector(WaterArea area, ShipParameters p) => new GroundingDetector
    {
        Area = area, Draft = p.draft, UnderKeelClearance = 0.5, WarningDistance = WaterArea.CableMeters
    };

    private static HullFootprint Hull(ShipDynamicsModel m, ShipParameters p) =>
        HullFootprint.FromShip(m.East, m.North, m.Psi, 0, 0, p.length, p.beam);

    // ─────────── Геометрия ───────────
    [Test]
    public void Contains_ConcavePolygon()
    {
        // «Г»: квадрат 100×100 без правого верхнего квадранта
        var g = Poly(0, 0, 100, 0, 100, 50, 50, 50, 50, 100, 0, 100);
        Assert.IsTrue(Polygon2D.Contains(g, new WorldPosition(25, 75)));
        Assert.IsTrue(Polygon2D.Contains(g, new WorldPosition(75, 25)));
        Assert.IsFalse(Polygon2D.Contains(g, new WorldPosition(75, 75)), "вырез");
        Assert.IsFalse(Polygon2D.Contains(g, new WorldPosition(150, 25)));
    }

    [Test]
    public void HullFootprint_RotatesWithHeading()
    {
        var corners = new WorldPosition[4];
        // Курс 090°: корпус 100 × 10 вытянут по оси восток–запад; нос — на восток
        HullFootprint.FromShip(0, 0, Math.PI / 2, 0, 0, 100, 10).GetCorners(corners);
        Assert.AreEqual(50, corners[0].East, 1e-9);   // нос-ПБ
        Assert.AreEqual(-5, corners[0].North, 1e-9);  // ПБ на курсе 090° — к югу
        Assert.AreEqual(-50, corners[2].East, 1e-9);  // корма-ЛБ
        Assert.AreEqual(5, corners[2].North, 1e-9);
        // Смещение центра к носу на 10 м при курсе 000°
        HullFootprint h = HullFootprint.FromShip(0, 0, 0, 10, 2, 100, 10);
        Assert.AreEqual(2, h.Center.East, 1e-9);
        Assert.AreEqual(10, h.Center.North, 1e-9);
    }

    [Test]
    public void PolygonDistance_EdgeCrossingWithoutVerticesInside_IsZero()
    {
        // Корпус 100 × 10 вдоль N; узкая длинная мель поперёк — ни одна вершина не внутри другого
        var corners = new WorldPosition[4];
        HullFootprint.FromShip(0, 0, 0, 0, 0, 100, 10).GetCorners(corners);
        var strip = Poly(-200, -2, 200, -2, 200, 2, -200, 2);
        foreach (WorldPosition c in corners) Assert.IsFalse(Polygon2D.Contains(strip, c));
        foreach (WorldPosition v in strip) Assert.IsFalse(Polygon2D.Contains(corners, v));
        Assert.AreEqual(0.0, Polygon2D.PolygonDistance(corners, strip));
    }

    [Test]
    public void PolygonDistance_MatchesManualCalculation()
    {
        var corners = new WorldPosition[4];
        var square = Poly(100, -20, 140, -20, 140, 20, 100, 20);
        // Курс 000°: правый борт на E = 5 → до мели 95 м
        HullFootprint.FromShip(0, 0, 0, 0, 0, 100, 10).GetCorners(corners);
        Assert.AreEqual(95.0, Polygon2D.PolygonDistance(corners, square), 1e-9);
        // Курс 090°: нос на E = 50 → 50 м
        HullFootprint.FromShip(0, 0, Math.PI / 2, 0, 0, 100, 10).GetCorners(corners);
        Assert.AreEqual(50.0, Polygon2D.PolygonDistance(corners, square), 1e-9);
        // Корпус целиком внутри большой мели — 0
        var big = Poly(-500, -500, 500, -500, 500, 500, -500, 500);
        Assert.AreEqual(0.0, Polygon2D.PolygonDistance(corners, big));
        // Диагональ: угол мели (100, 100), угол корпуса на курсе 000° — (5, 50)
        var diag = Poly(100, 100, 150, 100, 150, 150, 100, 150);
        HullFootprint.FromShip(0, 0, 0, 0, 0, 100, 10).GetCorners(corners);
        Assert.AreEqual(Math.Sqrt(95 * 95 + 50 * 50), Polygon2D.PolygonDistance(corners, diag), 1e-9);
    }

    [Test]
    public void Triangulate_AreaEqualsPolygonArea()
    {
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        var polys = new List<List<WorldPosition>> { Poly(0, 0, 100, 0, 100, 50, 50, 50, 50, 100, 0, 100) };
        foreach (LandArea l in area.land) polys.Add(l.vertices);
        foreach (ShoalArea s in area.shoals) polys.Add(s.vertices);
        foreach (List<WorldPosition> p in polys)
        {
            List<int> t = Polygon2D.Triangulate(p);
            Assert.AreEqual((p.Count - 2) * 3, t.Count, "n − 2 треугольника");
            double sum = 0;
            for (int i = 0; i < t.Count; i += 3)
            {
                double o = Polygon2D.Orientation(p[t[i]], p[t[i + 1]], p[t[i + 2]]);
                Assert.Greater(o, 0.0, "обход треугольников против часовой");
                sum += 0.5 * o;
            }
            Assert.AreEqual(Math.Abs(Polygon2D.SignedArea(p)), sum, 1e-6 * Math.Abs(sum) + 1e-6);
        }
    }

    [Test]
    public void DepthAt_ShoalLandDefault()
    {
        var area = ScriptableObject.CreateInstance<WaterArea>();
        area.defaultDepth = 30f;
        area.shoals.Add(new ShoalArea { depth = 6f, vertices = Poly(0, 0, 100, 0, 100, 100, 0, 100) });
        area.shoals.Add(new ShoalArea { depth = 2f, vertices = Poly(40, 40, 60, 40, 60, 60, 40, 60) });
        area.land.Add(new LandArea { vertices = Poly(200, 0, 300, 0, 300, 100, 200, 100) });
        Assert.AreEqual(6f, area.DepthAt(new WorldPosition(10, 10)));
        Assert.AreEqual(2f, area.DepthAt(new WorldPosition(50, 50)), "наименьшая из вложенных");
        Assert.AreEqual(0f, area.DepthAt(new WorldPosition(250, 50)), "суша");
        Assert.AreEqual(30f, area.DepthAt(new WorldPosition(-50, -50)));
    }

    // ─────────── Посадка на мель (модель + детектор) ───────────
    private struct RunResult
    {
        public double WarningTime, WarningDistance, GroundTime, GroundSpeed, RefloatTime;
        public WorldPosition GroundPos;
        public int Warnings, Groundings, Refloats;
    }

    // Прямым курсом 000° при телеграфе sf; после посадки — телеграф sfAfter
    private static RunResult Run(ShipDynamicsModel m, ShipParameters p, GroundingDetector det,
                                 double duration, double sf, double sfAfter, double t0 = 0)
    {
        var r = new RunResult { WarningTime = double.NaN, GroundTime = double.NaN, RefloatTime = double.NaN };
        double t = t0;
        m.SpeedFactor = sf;
        while (t < duration)
        {
            m.Step(Dt);
            t += Dt;
            GroundingEvents ev = det.Update(Hull(m, p));
            if ((ev & GroundingEvents.WarningStarted) != 0 && r.Warnings++ == 0)
            {
                r.WarningTime = t; r.WarningDistance = det.Nearest.Distance;
            }
            if ((ev & GroundingEvents.Grounded) != 0)
            {
                if (r.Groundings++ == 0) { r.GroundTime = t; r.GroundSpeed = m.U; r.GroundPos = new WorldPosition(m.East, m.North); }
                m.SetGrounded(true);
                m.SpeedFactor = sfAfter;
            }
            if ((ev & GroundingEvents.Refloated) != 0)
            {
                if (r.Refloats++ == 0) r.RefloatTime = t;
                m.SetGrounded(false);
            }
        }
        return r;
    }

    [Test]
    public void FullAhead_ToShoal_WarningThenGrounding_ShipStops()
    {
        ShipParameters p = MakeShip();
        var shoal = new ShoalArea { name = "Тест", depth = 2f, vertices = Poly(-300, 1000, 300, 1000, 300, 1200, -300, 1200) };
        GroundingDetector det = Detector(AreaWith(shoal), p);
        var m = new ShipDynamicsModel(p) { U = p.ServiceSpeedMs, GroundingStopTime = 1.0 };

        // Полный вперёд, после посадки машина продолжает работать «в мель»
        RunResult r = Run(m, p, det, 400, 1.0, 1.0);

        Assert.AreEqual(1, r.Warnings, "одно предупреждение");
        Assert.AreEqual(1, r.Groundings, "одна посадка");
        Assert.AreEqual(0, r.Refloats, "вперёд с мели не сняться");
        Assert.Less(r.WarningTime, r.GroundTime, "сначала предупреждение");
        Assert.LessOrEqual(r.WarningDistance, WaterArea.CableMeters);
        Assert.Greater(r.WarningDistance, WaterArea.CableMeters - 1.0, "на дистанции D_пред (шаг 0,15 м)");
        Assert.AreEqual(GroundingState.Grounded, det.State);
        Assert.AreEqual(0.0, m.U, "u = 0");
        Assert.AreEqual(0.0, m.R, "r = 0");

        // Наползание ≈ u₀·τ_ост (Эйлер по шагу: u₀·dt / (1 − e^(−dt/τ)) ≈ u₀·(τ + dt/2))
        double crawl = m.North - r.GroundPos.North;
        double expected = r.GroundSpeed * 1.0;
        Assert.AreEqual(expected, crawl, 0.05 * expected, $"путь наползания {crawl:F2} м, u₀·τ = {expected:F2} м");

        // Машина «вперёд» на мели судно не сдвигает
        double n0 = m.North;
        for (int i = 0; i < 3000; i++) m.Step(Dt);
        Assert.AreEqual(n0, m.North, 1e-9);
    }

    [Test]
    public void DeepShoal_NoEventsForShallowDraftShip()
    {
        ShipParameters p = MakeShip();   // d = 3 м, Δ = 0,5 м → опасно < 3,5 м
        var shoal = new ShoalArea { name = "Банка", depth = 6f, vertices = Poly(-300, 1000, 300, 1000, 300, 1200, -300, 1200) };
        GroundingDetector det = Detector(AreaWith(shoal), p);
        var m = new ShipDynamicsModel(p) { U = p.ServiceSpeedMs };
        RunResult r = Run(m, p, det, 300, 1.0, 1.0);
        Assert.AreEqual(0, r.Warnings + r.Groundings);
        Assert.Greater(m.North, 1500.0, "судно прошло над банкой");

        // Для глубокосидящего (d = 8 м) та же банка опасна
        p.draft = 8f;
        det = Detector(AreaWith(shoal), p);
        m = new ShipDynamicsModel(p) { U = p.ServiceSpeedMs };
        r = Run(m, p, det, 300, 1.0, 1.0);
        Assert.AreEqual(1, r.Groundings);
    }

    [Test]
    public void Grounded_AsternRefloats()
    {
        ShipParameters p = MakeShip();
        var shoal = new ShoalArea { name = "Тест", depth = 2f, vertices = Poly(-300, 1000, 300, 1000, 300, 1200, -300, 1200) };
        GroundingDetector det = Detector(AreaWith(shoal), p);
        var m = new ShipDynamicsModel(p) { U = p.ServiceSpeedMs, RefloatSpeedFactor = 0.3 };

        // Сели на полном переднем, затем — полный назад
        RunResult r = Run(m, p, det, 600, 1.0, -1.0);
        Assert.AreEqual(1, r.Groundings);
        Assert.AreEqual(1, r.Refloats, "задним ходом снялись");
        Assert.Less(r.RefloatTime - r.GroundTime, 120.0, $"снялись за {r.RefloatTime - r.GroundTime:F0} с");
        Assert.Less(m.U, 0.0, "идёт назад");
        Assert.AreNotEqual(GroundingState.Grounded, det.State);
        Assert.Less(m.North, r.GroundPos.North, "отошли от мели");
    }

    [Test]
    public void GroundedOnSternway_AheadRefloats()
    {
        ShipParameters p = MakeShip();
        // Мель к югу; судно курсом 000° идёт задним ходом
        var shoal = new ShoalArea { name = "Тест", depth = 2f, vertices = Poly(-300, -600, 300, -600, 300, -400, -300, -400) };
        GroundingDetector det = Detector(AreaWith(shoal), p);
        var m = new ShipDynamicsModel(p) { U = -3.0 };
        RunResult r = Run(m, p, det, 600, -1.0, 1.0);
        Assert.AreEqual(1, r.Groundings);
        Assert.AreEqual(-1, m.GroundedDirection, "сели кормой");
        Assert.AreEqual(1, r.Refloats, "передним ходом снялись");
        Assert.Greater(m.U, 0.0);
    }

    [Test]
    public void Grounded_RudderDoesNotTurnShip()
    {
        ShipParameters p = MakeShip();
        var m = new ShipDynamicsModel(p) { U = 5.0, R = 0.02 };
        m.SetGrounded(true);
        m.RudderCommandDeg = 35;
        m.SpeedFactor = 1;
        for (int i = 0; i < 1000; i++) m.Step(Dt);
        Assert.AreEqual(0.0, m.R);
        double psi = m.Psi;
        for (int i = 0; i < 1000; i++) m.Step(Dt);
        Assert.AreEqual(psi, m.Psi, 1e-12);
        Assert.AreEqual(35.0, m.Delta, 1e-9, "рулевая машина работает");
    }

    [Test]
    public void Warning_Hysteresis_NoChatter()
    {
        ShipParameters p = MakeShip();
        var shoal = new ShoalArea { depth = 2f, vertices = Poly(-300, 1000, 300, 1000, 300, 1200, -300, 1200) };
        GroundingDetector det = Detector(AreaWith(shoal), p);
        int events = 0;
        // Нос колеблется около границы D_пред (±5 м)
        for (int i = 0; i < 1000; i++)
        {
            double bowGap = WaterArea.CableMeters + 5.0 * Math.Sin(i * 0.1);
            double north = 1000.0 - bowGap - p.length * 0.5;
            if (det.Update(HullFootprint.FromShip(0, north, 0, 0, 0, p.length, p.beam)) != GroundingEvents.None) events++;
        }
        Assert.AreEqual(1, events, "одно предупреждение, без «дребезга»");
    }

    // ─────────── Демонстрационная акватория ───────────
    private static WaterArea Demo()
    {
        var a = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(a);
        return a;
    }

    // Боковое отклонение от оси (> 0 — влево по ходу) и расстояние до ближайшего колена
    private static double CrossTrack(IList<WorldPosition> axis, WorldPosition p, out double distance)
    {
        distance = double.MaxValue;
        double side = 0;
        for (int i = 0; i + 1 < axis.Count; i++)
        {
            double d = Polygon2D.PointSegmentDistance(p, axis[i], axis[i + 1]);
            if (d < distance)
            {
                distance = d;
                side = Polygon2D.Orientation(axis[i], axis[i + 1], p) > 0 ? 1 : -1;
            }
        }
        return side * distance;
    }

    [Test]
    public void Demo_LateralMarks_RedLeftGreenRight_OnEdges()
    {
        WaterArea a = Demo();
        WorldPosition[] axis = DemoWaterArea.Axis();
        int red = 0, green = 0;
        foreach (AidToNavigation aid in a.aids)
        {
            if (aid.type != AidType.LateralPort && aid.type != AidType.LateralStarboard) continue;
            double xt = CrossTrack(axis, aid.position, out double dist);
            if (aid.type == AidType.LateralPort) { red++; Assert.Greater(xt, 0, aid.name + " — слева"); }
            else { green++; Assert.Less(xt, 0, aid.name + " — справа"); }
            // На полуширине (поворотные — на пересечении кромок, до конца колена чуть дальше)
            Assert.GreaterOrEqual(dist, DemoWaterArea.FairwayWidth * 0.5 - 1e-6, aid.name);
            Assert.LessOrEqual(dist, DemoWaterArea.FairwayWidth * 0.5 / Math.Cos(30 * Math.PI / 180) + 1.0, aid.name);
        }
        Assert.AreEqual(red, green, "попарно");
        Assert.GreaterOrEqual(red, 10);

        // Расстояние между соседними буями одной стороны ≤ 800 м
        WorldPosition? prev = null;
        foreach (AidToNavigation aid in a.aids)
        {
            if (aid.type != AidType.LateralPort) continue;
            if (prev.HasValue) Assert.LessOrEqual((aid.position - prev.Value).Magnitude, DemoWaterArea.MaxBuoySpacing + 1e-6);
            prev = aid.position;
        }
    }

    [Test]
    public void Demo_ShoalsOutsideFairwayCorridor()
    {
        WaterArea a = Demo();
        List<WorldPosition> corridor = DemoWaterArea.Corridor(DemoWaterArea.Axis(), DemoWaterArea.FairwayWidth);
        foreach (ShoalArea s in a.shoals)
            Assert.Greater(Polygon2D.PolygonDistance(corridor, s.vertices), 100.0, s.name);
        foreach (LandArea l in a.land)
            Assert.Greater(Polygon2D.PolygonDistance(corridor, l.vertices), 1000.0, l.name);
        foreach (AidToNavigation aid in a.aids)
            Assert.IsFalse(aid.type != AidType.LateralPort && aid.type != AidType.LateralStarboard && Polygon2D.Contains(corridor, aid.position),
                           aid.name + " не в коридоре");
    }

    [Test]
    public void Demo_CardinalMarks_OnNamedSideOfTheirShoal()
    {
        WaterArea a = Demo();
        int checkedMarks = 0;
        foreach (AidToNavigation aid in a.aids)
        {
            if (aid.type < AidType.CardinalNorth || aid.type > AidType.CardinalWest) continue;
            // Мель знака — ближайшая
            ShoalArea best = null; double bestD = double.MaxValue;
            foreach (ShoalArea s in a.shoals)
            {
                double d = Polygon2D.PolygonDistance(new List<WorldPosition> { aid.position, aid.position }, s.vertices);
                if (d < bestD) { bestD = d; best = s; }
            }
            Assert.IsNotNull(best);
            Assert.Less(bestD, 200.0, aid.name + " — у своей мели");
            Assert.IsFalse(Polygon2D.Contains(best.vertices, aid.position), aid.name + " — на безопасной воде");
            Polygon2D.Bounds(best.vertices, out WorldPosition min, out WorldPosition max);
            switch (aid.type)
            {
                case AidType.CardinalNorth: Assert.Greater(aid.position.North, max.North, aid.name); break;
                case AidType.CardinalSouth: Assert.Less(aid.position.North, min.North, aid.name); break;
                case AidType.CardinalEast: Assert.Greater(aid.position.East, max.East, aid.name); break;
                case AidType.CardinalWest: Assert.Less(aid.position.East, min.East, aid.name); break;
            }
            checkedMarks++;
        }
        Assert.AreEqual(4, checkedMarks, "все четыре кардинальных знака");
    }

    [Test]
    public void Demo_HasIsolatedDangerAndSafeWaterMarks()
    {
        WaterArea a = Demo();
        AidToNavigation isolated = a.aids.Find(x => x.type == AidType.IsolatedDanger);
        AidToNavigation safe = a.aids.Find(x => x.type == AidType.SafeWater);
        Assert.IsNotNull(isolated);
        Assert.IsNotNull(safe);
        Assert.Less(a.DepthAt(isolated.position), 2f, "знак отдельной опасности — на опасности");
        Assert.AreEqual(a.defaultDepth, a.DepthAt(safe.position), "осевой — на безопасной воде");
        Assert.AreEqual(a.defaultDepth, a.DepthAt(DemoWaterArea.StartPosition));
    }

    [Test]
    public void Demo_FollowingFairway_NoWarnings()
    {
        // Проход по оси фарватера простым авторулевым — ни предупреждений, ни посадки
        ShipParameters p = MakeShip();
        WaterArea a = Demo();
        GroundingDetector det = Detector(a, p);
        var m = new ShipDynamicsModel(p)
        {
            East = DemoWaterArea.StartPosition.East, North = DemoWaterArea.StartPosition.North,
            Psi = DemoWaterArea.StartHeadingDeg / Deg, U = p.ServiceSpeedMs, SpeedFactor = 1.0
        };
        WorldPosition[] wp = DemoWaterArea.Axis();
        int next = 0, events = 0;
        for (double t = 0; t < 2000 && next < wp.Length; t += Dt)
        {
            WorldPosition target = wp[next];
            double de = target.East - m.East, dn = target.North - m.North;
            if (Math.Sqrt(de * de + dn * dn) < 150) { next++; continue; }
            double err = Math.IEEERemainder(Math.Atan2(de, dn) - m.Psi, 2 * Math.PI) * Deg;
            // ПД-регулятор по курсу: δ = 1,5·Δψ − 8 с·r (°)
            m.RudderCommandDeg = Math.Max(-35, Math.Min(35, 1.5 * err - 8.0 * m.R * Deg));
            m.Step(Dt);
            if (det.Update(Hull(m, p)) != GroundingEvents.None) events++;
        }
        Assert.AreEqual(wp.Length, next, "все точки пройдены");
        Assert.AreEqual(0, events, det.Nearest.Valid ? $"ближе всего «{det.Nearest.Name}» {det.Nearest.Distance:F0} м" : "");
    }

    [Test]
    public void Demo_MissingTheTurn_GroundsOnNorthShoal()
    {
        // Не довернул на поворот: курс 000° дальше точки поворота → предупреждение, затем «Отмель Северная»
        ShipParameters p = MakeShip();
        GroundingDetector det = Detector(Demo(), p);
        var m = new ShipDynamicsModel(p) { East = 0, North = 2500, U = p.ServiceSpeedMs };
        RunResult r = Run(m, p, det, 400, 1.0, 1.0);
        Assert.AreEqual(1, r.Groundings);
        Assert.Less(r.WarningTime, r.GroundTime);
        Assert.AreEqual("Отмель Северная", det.GroundedOn.Name);
    }
}
