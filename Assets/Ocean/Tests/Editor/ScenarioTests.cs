// ============================================================
// ScenarioTests.cs — EditMode-тесты движка сценариев (B2)
// Window → General → Test Runner → EditMode → Run All.
// ============================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class ScenarioTests
{
    private const double Dt = 0.02;
    private const double Deg = 180.0 / Math.PI;

    // Судно сцены (ShipParameters_Model45m), как в WaterAreaTests
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

    private static List<RouteWaypoint> Route(float xte, params double[] en)
    {
        var r = new List<RouteWaypoint>();
        for (int i = 0; i + 1 < en.Length; i += 2) r.Add(new RouteWaypoint("WP" + i / 2, new WorldPosition(en[i], en[i + 1]), xte));
        return r;
    }

    private static ScenarioDefinition DemoScenarioAsset(out WaterArea area, out ShipParameters ship)
    {
        area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        ship = MakeShip();
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoScenario.Fill(s, area, ship);
        return s;
    }

    // ─────────── Геометрия маршрута ───────────
    [Test]
    public void Xte_MatchesManualCalculation()
    {
        // Участок на север: правее — восток
        var t = new RouteTracker();
        t.Reset(Route(100f, 0, 0, 0, 1000));
        t.Update(new WorldPosition(30, 400), 0, 0);
        Assert.AreEqual(30.0, t.Xte, 1e-9);
        Assert.AreEqual(400.0, t.Atd, 1e-9);
        Assert.AreEqual(0.0, t.LegCourseDeg, 1e-9);

        // Участок 045°: точка (1000; 0) — правее на 1000·cos45° = 707,1 м, вдоль — тоже 707,1 м
        t.Reset(Route(1000f, 0, 0, 1000, 1000, 2000, 2000));
        t.Update(new WorldPosition(1000, 0), 0, 0);
        Assert.AreEqual(1000.0 / Math.Sqrt(2.0), t.Xte, 1e-9);
        Assert.AreEqual(1000.0 / Math.Sqrt(2.0), t.Atd, 1e-9);
        Assert.AreEqual(45.0, t.LegCourseDeg, 1e-9);

        // Левее — отрицательное
        Assert.AreEqual(-50.0, RouteTracker.CrossTrack(new WorldPosition(0, 0), new WorldPosition(0, 100), new WorldPosition(-50, 10)), 1e-9);
    }

    [Test]
    public void Waypoint_PassedOnBisectorOfTurn()
    {
        // Поворот вправо на 90° в (0; 1000): биссектриса — нормаль (1; 1)
        var t = new RouteTracker();
        t.Reset(Route(100f, 0, 0, 0, 1000, 1000, 1000));
        t.Update(new WorldPosition(-10, 995), 0, 0.02);
        Assert.AreEqual(0, t.Leg, "(−10; 995): (P − W)·(t₁ + t₂) = −15 < 0 — не пройдена");
        Assert.AreEqual(0, t.PassedCount);

        IReadOnlyList<RouteEvent> ev = t.Update(new WorldPosition(10, 995), 1, 0.02);
        Assert.AreEqual(1, t.Leg, "(10; 995): +5 ≥ 0 — пройдена");
        Assert.AreEqual(1, t.PassedCount);
        Assert.AreEqual(RouteEventKind.WaypointPassed, ev[0].Kind);
        Assert.AreEqual(1, ev[0].Index);
        Assert.AreEqual(10.0, ev[0].Xte, 1e-9, "XTE в момент прохождения — по входящему участку");
        // Новый участок — на восток: правее — юг, P на 5 м южнее линии пути
        Assert.AreEqual(5.0, t.Xte, 1e-9);
        Assert.AreEqual(90.0, t.LegCourseDeg, 1e-9);
    }

    [Test]
    public void Waypoints_PassedInOrder_ThenArrivedOnce()
    {
        var t = new RouteTracker();
        t.Reset(Route(100f, 0, 0, 0, 500, 0, 1000));
        var passed = new List<int>();
        int arrived = 0;
        for (double n = 0, time = 0; n <= 1100; n += 5, time += 1)
            foreach (RouteEvent e in t.Update(new WorldPosition(3, n), time, 1))
            {
                if (e.Kind == RouteEventKind.WaypointPassed) passed.Add(e.Index);
                if (e.Kind == RouteEventKind.Arrived) arrived++;
            }
        CollectionAssert.AreEqual(new[] { 1, 2 }, passed);
        Assert.AreEqual(1, arrived);
        Assert.IsTrue(t.Finished);
        Assert.AreEqual(0, t.CorridorExits);
        Assert.AreEqual(3.0, t.MaxAbsXte, 1e-9);
    }

    [Test]
    public void Corridor_ExitAndReturn_WithHysteresis_NoChatter()
    {
        // XTE доп. 100 м, η = 0,05: выход при |XTE| > 100, возврат при |XTE| < 95
        var t = new RouteTracker();
        t.Reset(Route(100f, 0, 0, 0, 10000));
        double[] xte = { 50, 101, 97, 102, 99, 96, 94, 98, 30 };
        var kinds = new List<RouteEventKind>();
        RouteEvent ret = default;
        for (int i = 0; i < xte.Length; i++)
            foreach (RouteEvent e in t.Update(new WorldPosition(xte[i], 100 + i), i * 10.0, 10.0))
            {
                kinds.Add(e.Kind);
                if (e.Kind == RouteEventKind.CorridorReturn) ret = e;
            }
        CollectionAssert.AreEqual(new[] { RouteEventKind.CorridorExit, RouteEventKind.CorridorReturn }, kinds);
        Assert.AreEqual(1, t.CorridorExits);
        Assert.AreEqual(102.0, ret.Xte, 1e-9, "max |XTE| за выход");
        Assert.AreEqual(50.0, ret.Value2, 1e-9, "длительность: вышел при t = 10 с, вернулся при t = 60 с");
        Assert.AreEqual(50.0, t.TimeOutside, 1e-9);
        Assert.AreEqual(102.0, t.MaxAbsXte, 1e-9);
    }

    // ─────────── Шина событий ───────────
    [Test]
    public void Bus_StampsExerciseTime_LogsOnlyDuringExercise()
    {
        ExerciseEvents.Reset();
        var received = new List<ExerciseEvent>();
        ExerciseEvents.Raised += e => received.Add(e);
        try
        {
            ExerciseEvents.Publish(new ExerciseEvent(ExerciseEventType.ShoalWarningStarted));   // свободное плавание
            ExerciseEvents.BeginExercise("T-01");
            ExerciseEvents.Time = 12.5;
            ExerciseEvents.Publish(new ExerciseEvent(ExerciseEventType.WaypointPassed) { Index = 1, Value = -3 });
            ExerciseEvents.Time = 20.0;
            ExerciseEvents.Publish(new ExerciseEvent(ExerciseEventType.ExerciseEnded) { EndReason = ScenarioEndReason.Arrived });
            ExerciseEvents.EndExercise();
            ExerciseEvents.Publish(new ExerciseEvent(ExerciseEventType.Grounded));

            Assert.AreEqual(4, received.Count, "все события доставлены, в том числе вне упражнения");
            Assert.IsFalse(received[0].InExercise);
            Assert.AreEqual(2, ExerciseEvents.Log.Count, "в журнал — только события упражнения");
            Assert.AreEqual(12.5, ExerciseEvents.Log[0].Time);
            Assert.IsTrue(ExerciseEvents.Log[0].InExercise);
            Assert.AreEqual(1, ExerciseEvents.Log[0].Index);
            Assert.AreEqual(ScenarioEndReason.Arrived, ExerciseEvents.Log[1].EndReason);
            Assert.AreEqual("T-01", ExerciseEvents.ScenarioId);

            ExerciseEvents.BeginExercise("T-02");
            Assert.AreEqual(0, ExerciseEvents.Log.Count, "новое упражнение — новый журнал");
            Assert.AreEqual(0.0, ExerciseEvents.Time);
        }
        finally { ExerciseEvents.Reset(); }
    }

    // ─────────── Описание сценария ───────────
    [Test]
    public void Validate_FindsProblems_DemoIsValid()
    {
        var bad = ScriptableObject.CreateInstance<ScenarioDefinition>();
        bad.route = Route(0f, 0, 0);
        bad.briefing = "";
        List<string> problems = bad.Validate();
        Assert.IsTrue(problems.Exists(p => p.Contains("меньше двух")), string.Join("\n", problems));
        Assert.IsTrue(problems.Exists(p => p.Contains("брифинга")));
        Assert.IsTrue(problems.Exists(p => p.Contains("акватория")));

        bad.route = Route(0f, 0, 0, 0, 100);
        Assert.IsTrue(bad.Validate().Exists(p => p.Contains("XTE")));

        ScenarioDefinition demo = DemoScenarioAsset(out _, out _);
        CollectionAssert.IsEmpty(demo.Validate());
    }

    [Test]
    public void Demo_RouteAlongFairwayAxis()
    {
        ScenarioDefinition s = DemoScenarioAsset(out WaterArea area, out _);
        WorldPosition[] axis = DemoWaterArea.Axis();
        Assert.AreEqual(axis.Length + 1, s.route.Count);
        Assert.AreEqual(DemoWaterArea.StartPosition.East, s.route[0].position.East);
        Assert.AreEqual(DemoWaterArea.StartPosition.North, s.route[0].position.North);
        for (int i = 0; i < axis.Length; i++)
        {
            Assert.AreEqual(axis[i].East, s.route[i + 1].position.East, 1e-9);
            Assert.AreEqual(axis[i].North, s.route[i + 1].position.North, 1e-9);
        }
        Assert.AreEqual(DemoWaterArea.StartPosition.East, s.initial.position.East);
        Assert.AreEqual(DemoWaterArea.StartPosition.North, s.initial.position.North);
        Assert.AreEqual(0f, s.initial.headingDeg);
        Assert.AreEqual(WeatherPresetId.Moderate, s.weather);
        Assert.AreEqual(TimeOfDay.Day, s.timeOfDay);
        Assert.AreEqual(9200.0, s.RouteLength, 1.0, "2,2 + 3,5 + 3,5 км");
        // Коридор фарватера целиком внутри фарватера: XTE доп. < полуширины
        Assert.Less(DemoScenario.FairwayXteLimit, DemoWaterArea.FairwayWidth * 0.5);
        Assert.AreEqual(area.defaultDepth, area.DepthAt(s.initial.position));
    }

    // Прогон демо-сценария: модель + детектор мели + контроль маршрута.
    // Авторулевой по линии пути: χ_зад = χ_уч − atan(XTE / Δ), Δ = 150 м;
    // δ = 1,5·(χ_зад − ψ) − 8 с·r (°), |δ| ≤ 35°.
    // Упреждение поворота (wheel-over): за wheelOver метров до точки поворота
    // авторулевой уже ведёт судно по следующему участку, как судоводитель,
    // начинающий перекладку руля до точки. wheelOver = NaN — держит курс 000° (не поворачивает).
    private struct DemoRun
    {
        public double ArrivalTime, FirstExitTime, WarningTime, GroundTime;
        public int Passed, Exits, Warnings, Groundings;
        public double MaxAbsXte;
        public string GroundedOn;
    }

    private const double WheelOver = 150.0;   // м до точки поворота (подобрано: наименьший max |XTE|)

    private static DemoRun RunDemo(double wheelOver, double maxTime)
    {
        ScenarioDefinition s = DemoScenarioAsset(out WaterArea area, out ShipParameters p);
        var det = new GroundingDetector { Area = area, Draft = p.draft, UnderKeelClearance = 0.5, WarningDistance = WaterArea.CableMeters };
        var m = new ShipDynamicsModel(p)
        {
            East = s.initial.position.East, North = s.initial.position.North, Psi = s.initial.headingDeg / Deg,
            U = s.initial.speedKnots * ShipParameters.KnotsToMs, SpeedFactor = ScenarioText.SpeedFactor(s.initial.telegraph)
        };
        var route = new RouteTracker();
        route.Reset(s.route);
        var r = new DemoRun { ArrivalTime = double.NaN, FirstExitTime = double.NaN, WarningTime = double.NaN, GroundTime = double.NaN };

        double t = 0;
        route.Update(new WorldPosition(m.East, m.North), t, 0);
        while (t < maxTime && !route.Finished && r.Groundings == 0)
        {
            double courseDeg = 0.0;
            if (!double.IsNaN(wheelOver))
            {
                int leg = route.Leg;
                double legCourse = route.LegCourseDeg, xte = route.Xte;
                if (route.LegLength - route.Atd < wheelOver && leg + 2 < s.route.Count)
                {
                    WorldPosition a = s.route[leg + 1].position, b = s.route[leg + 2].position;
                    legCourse = Math.Atan2(b.East - a.East, b.North - a.North) * Deg;
                    xte = RouteTracker.CrossTrack(a, b, new WorldPosition(m.East, m.North));
                }
                courseDeg = legCourse - Math.Atan(xte / 150.0) * Deg;
            }
            double err = Math.IEEERemainder(courseDeg / Deg - m.Psi, 2 * Math.PI) * Deg;
            m.RudderCommandDeg = Math.Max(-35, Math.Min(35, 1.5 * err - 8.0 * m.R * Deg));
            m.Step(Dt);
            t += Dt;

            foreach (RouteEvent e in route.Update(new WorldPosition(m.East, m.North), t, Dt))
            {
                if (e.Kind == RouteEventKind.CorridorExit && double.IsNaN(r.FirstExitTime)) r.FirstExitTime = t;
                if (e.Kind == RouteEventKind.Arrived) r.ArrivalTime = t;
            }
            GroundingEvents g = det.Update(HullFootprint.FromShip(m.East, m.North, m.Psi, 0, 0, p.length, p.beam));
            if ((g & GroundingEvents.WarningStarted) != 0) { r.Warnings++; if (double.IsNaN(r.WarningTime)) r.WarningTime = t; }
            if ((g & GroundingEvents.Grounded) != 0) { r.Groundings++; r.GroundTime = t; r.GroundedOn = det.GroundedOn.Name; }
        }
        r.Passed = route.PassedCount;
        r.Exits = route.CorridorExits;
        r.MaxAbsXte = route.MaxAbsXte;
        return r;
    }

    [Test]
    public void Demo_FollowingRoute_AllWaypointsNoCorridorExit()
    {
        DemoRun r = RunDemo(WheelOver, DemoScenario.TimeLimitSeconds);
        TestContext.WriteLine($"Прибытие {r.ArrivalTime:F1} с, max |XTE| {r.MaxAbsXte:F1} м, выходов {r.Exits}, предупреждений {r.Warnings}");
        Assert.AreEqual(3, r.Passed, "все три точки");
        Assert.IsFalse(double.IsNaN(r.ArrivalTime), "прибытие");
        Assert.Less(r.ArrivalTime, DemoScenario.TimeLimitSeconds, "в пределах лимита");
        Assert.AreEqual(0, r.Exits, $"без выхода из коридора (max |XTE| {r.MaxAbsXte:F1} м)");
        Assert.AreEqual(0, r.Warnings);
        Assert.AreEqual(0, r.Groundings);
    }

    [Test]
    public void Demo_MissingTheTurn_CorridorExitThenWarningThenGrounding()
    {
        DemoRun r = RunDemo(double.NaN, DemoScenario.TimeLimitSeconds);
        TestContext.WriteLine($"Выход из коридора {r.FirstExitTime:F1} с, предупреждение {r.WarningTime:F1} с, посадка {r.GroundTime:F1} с («{r.GroundedOn}»), точек {r.Passed}");
        Assert.AreEqual(2, r.Passed, "вход и поворот пройдены (биссектриса), выход — нет");
        Assert.AreEqual(1, r.Groundings);
        Assert.AreEqual("Отмель Северная", r.GroundedOn);
        Assert.Less(r.FirstExitTime, r.WarningTime, "сначала выход из коридора");
        Assert.Less(r.WarningTime, r.GroundTime, "затем опасное приближение и посадка");
    }

    [Test]
    public void Demo_TurnWithoutWheelOver_LargerOvershoot()
    {
        // Поворот только по прохождении точки (на биссектрисе): из-за запаздывания рулевой
        // машины (2,32 °/с) судно выносит наружу поворота намного дальше, чем с упреждением
        DemoRun late = RunDemo(0.0, DemoScenario.TimeLimitSeconds);
        DemoRun ok = RunDemo(WheelOver, DemoScenario.TimeLimitSeconds);
        TestContext.WriteLine($"max |XTE|: без упреждения {late.MaxAbsXte:F1} м, с упреждением {WheelOver:F0} м — {ok.MaxAbsXte:F1} м");
        Assert.AreEqual(3, late.Passed);
        Assert.Greater(late.MaxAbsXte, 4.0 * ok.MaxAbsXte);
    }
}
