// ============================================================
// InstructorTests.cs — Окно инструктора: вводные, пауза, карта (D1)
//
// Отказы в модели движения (отказ ГД — как «Стоп», руль заклинило; без отказов —
// прежний шаг); прогоны B2-01 с вводными и критерии N1, N2; оценка в реальном
// времени и по записи совпадают; вводные попадают в report.html (журнал, график,
// схема); новые события — в конце перечня и с текстом; окно карты инструктора
// (MapViewport): масштаб к курсору, сдвиг, следование с перецентровкой.
// ============================================================
using System;
using System.IO;
using NUnit.Framework;

public class InstructorTests
{
    private static ExerciseSimulation B2Run(string variant, string folder = "")
    {
        var sim = new ExerciseSimulation { Scenario = AssessmentTests.DemoB2(), RecordFolder = folder };
        DemoAssessmentRuns.Configure(sim, variant);
        sim.Run();
        return sim;
    }

    private static CriterionResult Of(AssessmentResult r, string code) => r.Items.Find(c => c.Code == code);

    // ─────────── Модель ───────────
    [Test]
    public void Model_EngineFailure_SameAsStop_SteeringFailure_RudderStays()
    {
        ShipParameters p = AssessmentTests.Model45m();
        var failed = new ShipDynamicsModel(p) { U = 7.0, SpeedFactor = 1.0, EngineFailed = true };
        var stop = new ShipDynamicsModel(p) { U = 7.0, SpeedFactor = 0.0 };
        for (int i = 0; i < 3000; i++) { failed.Step(0.02); stop.Step(0.02); }
        Assert.AreEqual(stop.U, failed.U, 0.0, "отказ ГД — тот же закон, что «Стоп» (бит в бит)");
        Assert.Less(failed.U, 7.0 * Math.Exp(-60.0 / p.decelerationTimeConstant) + 1e-6, "ход гаснет с τ_торм");

        var m = new ShipDynamicsModel(p) { U = 7.0, SpeedFactor = 1.0, RudderCommandDeg = 10 };
        for (int i = 0; i < 500; i++) m.Step(0.02);
        Assert.AreEqual(10.0, m.Delta, 1e-9);
        m.SteeringFailed = true;
        m.RudderCommandDeg = -35;
        for (int i = 0; i < 500; i++) m.Step(0.02);
        Assert.AreEqual(10.0, m.Delta, 1e-12, "руль заклинило");
        Assert.AreEqual(-35.0, m.Ordered, 1e-12, "заданный руль идёт от штурвала");
        m.SteeringFailed = false;
        m.Step(0.02);
        Assert.Less(m.Delta, 10.0, "после устранения рулевая машина снова отрабатывает");

        // Без отказов — прежний шаг
        var a = new ShipDynamicsModel(p) { U = 5.0, SpeedFactor = 0.6, RudderCommandDeg = 20 };
        var b = new ShipDynamicsModel(p) { U = 5.0, SpeedFactor = 0.6, RudderCommandDeg = 20, EngineFailed = false, SteeringFailed = false };
        for (int i = 0; i < 2000; i++) { a.Step(0.02); b.Step(0.02); }
        Assert.IsTrue(a.Psi.Equals(b.Psi) && a.U.Equals(b.U) && a.East.Equals(b.East));
    }

    // ─────────── Критерии N1, N2 ───────────
    [TestCase("steer_fast", "N1", 10.0, true, 1.0)]
    [TestCase("steer_late", "N1", 45.0, false, 1.0 / 3.0)]   // (60 − 45)/(60 − 15)
    [TestCase("steer_none", "N1", 120.0, false, 0.0)]
    [TestCase("engine_fast", "N2", 10.0, true, 1.0)]
    public void Failures_ReactionCriteria(string variant, string code, double value, bool passed, double score)
    {
        ExerciseSimulation sim = B2Run(variant);
        AssessmentResult r = sim.LiveResult;
        CriterionResult c = Of(r, code);
        TestContext.WriteLine($"{variant}: {r.Headline()}; {code} = {c?.Value} ({c?.Note}); конец {sim.EndReason} в {ScenarioText.Clock(sim.EndTime)}");
        Assert.IsNotNull(c, code);
        Assert.IsTrue(c.Evaluated);
        Assert.AreEqual(value, c.Value, 0.05);
        Assert.AreEqual(passed, c.Passed);
        Assert.AreEqual(score, c.Score, 0.01);
        Assert.AreEqual(AssessmentGroup.Reaction, c.Info.Group);
        Assert.IsNull(Of(r, code == "N1" ? "N2" : "N1"), "другого отказа не было");
        Assert.AreEqual(ScenarioEndReason.Arrived, sim.EndReason, "после устранения отказа маршрут пройден");
        Assert.IsTrue(sim.Events.Exists(e => e.Type == (code == "N1" ? ExerciseEventType.SteeringFailure : ExerciseEventType.EngineFailure) && e.ByInstructor));
        Assert.IsTrue(sim.Events.Exists(e => e.Type == (code == "N1" ? ExerciseEventType.SteeringRestored : ExerciseEventType.EngineRestored)));
        if (!passed) Assert.IsTrue(DebriefText.Violations(r).Exists(v => v.Code == code), "нарушение в разборе");
    }

    [Test]
    public void NoFailures_NoReactionToFailureCriteria()
    {
        AssessmentResult r = B2Run("wheelover").LiveResult;
        Assert.IsNull(Of(r, AssessmentCatalog.N1));
        Assert.IsNull(Of(r, AssessmentCatalog.N2));
        Assert.AreEqual(99.5, r.Score, 0.05, "B2-01 с упреждением — как в табл. 10.3");
    }

    [Test]
    public void Failures_LiveAndOffline_IdenticalAndInReport()
    {
        string dir = Path.Combine(Path.GetTempPath(), "VRBridgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            AssessmentResult live = B2Run("steer_late", dir).LiveResult;
            RecordedSession rs = SessionReader.Load(dir);
            CollectionAssert.IsEmpty(rs.Warnings, string.Join("; ", rs.Warnings));
            AssessmentResult offline = SessionAssessment.Assess(rs);
            Assert.AreEqual(live.Items.Count, offline.Items.Count);
            for (int i = 0; i < live.Items.Count; i++)
            {
                CriterionResult a = live.Items[i], b = offline.Items[i];
                Assert.AreEqual(a.Code, b.Code);
                Assert.IsTrue(a.Value.Equals(b.Value), $"{a.Code}: {a.Value:R} ≠ {b.Value:R}");
                Assert.AreEqual(a.Note, b.Note, a.Code);
            }
            Assert.IsTrue(live.Score.Equals(offline.Score));
            Assert.IsTrue(rs.Events.Exists(e => e.Type == ExerciseEventType.SteeringFailure && e.ByInstructor), "вводная — в events.csv");

            DebriefMapData map = DebriefMap.Build(rs, offline);
            Assert.IsTrue(map.Markers.Exists(m => m.Layer == MapLayer.Failure && m.Label == "отказ руля"), "отметка на схеме");
            string html = SessionReport.Build(rs, offline, map);
            StringAssert.Contains("ВВОДНАЯ: отказ рулевого устройства", html, "журнал событий");
            StringAssert.Contains("отказ руля", html, "отметка на графиках и схеме");
            StringAssert.Contains("Запоздалая реакция на отказ рулевого устройства", html, "нарушение");
            StringAssert.Contains("<h2>1. Сведения о сессии</h2>", html);
            StringAssert.Contains("<h2>5. Критерии</h2>", html, "без сближений — нумерация без пропуска (было «6. Критерии»)");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }

    // ─────────── События ───────────
    [Test]
    public void Events_AppendedAtEnd_WithText()
    {
        Assert.Greater((int)ExerciseEventType.ExercisePaused, (int)ExerciseEventType.TargetSoundSignal, "новые типы — в конец перечня (номера прежних записей)");
        foreach (ExerciseEventType t in new[] { ExerciseEventType.ExercisePaused, ExerciseEventType.ExerciseResumed, ExerciseEventType.EngineFailure,
                                               ExerciseEventType.EngineRestored, ExerciseEventType.SteeringFailure, ExerciseEventType.SteeringRestored })
        {
            string d = new ExerciseEvent(t) { Value = 12, Subject = "Полный вперёд" }.Describe();
            Assert.IsFalse(d.StartsWith(t.ToString(), StringComparison.Ordinal), $"{t}: есть русский текст ({d})");
        }
        Assert.IsTrue(DebriefText.HasFailureText(AssessmentCatalog.N1) && DebriefText.HasFailureText(AssessmentCatalog.N2));
    }

    // ─────────── Карта ───────────
    [Test]
    public void Viewport_ZoomAtCursor_PanAndFollow()
    {
        var v = new MapViewport { WidthPx = 1000, HeightPx = 800, MetersPerPixel = 5, Center = new WorldPosition(0, 0) };
        WorldPosition before = v.ToWorld(700, 300);
        v.ZoomAt(700, 300, 2.0);
        Assert.AreEqual(2.5, v.MetersPerPixel, 1e-12);
        WorldPosition after = v.ToWorld(700, 300);
        Assert.AreEqual(before.East, after.East, 1e-9, "точка под курсором остаётся на месте");
        Assert.AreEqual(before.North, after.North, 1e-9);
        v.ToPixel(after, out double px, out double py);
        Assert.AreEqual(700, px, 1e-9);
        Assert.AreEqual(300, py, 1e-9);

        // Следование: внутри центральных 60 % окна — окно стоит, вышло — центр на судно
        v.FollowShip(new WorldPosition(0, 0));
        int ver = v.Version;
        Assert.IsFalse(v.Track(new WorldPosition(700, 0)), "0,7 км при полуширине 1,25 км × 0,6 = 750 м — внутри");
        Assert.AreEqual(ver, v.Version, "неподвижный слой не перестраивается");
        Assert.IsTrue(v.Track(new WorldPosition(800, 0)));
        Assert.AreEqual(800, v.Center.East, 1e-9);

        v.Pan(100, 0);
        Assert.IsFalse(v.Follow, "сдвиг рукой выключает следование");
        Assert.AreEqual(800 - 100 * 2.5, v.Center.East, 1e-9, "карта едет за мышью");
        Assert.IsFalse(v.Track(new WorldPosition(5000, 0)));

        var r = new MapRect(-500, -3000, 3100, 5000);
        v.Fit(r, 0.05);
        Assert.IsTrue(v.Bounds.Contains(new WorldPosition(-500, -3000)) && v.Bounds.Contains(new WorldPosition(3100, 5000)), "весь маршрут в окне");
        v.ZoomAt(500, 400, 1e6);
        Assert.AreEqual(v.MinMetersPerPixel, v.MetersPerPixel, "предел масштаба");
    }

    [Test]
    public void StaticMap_AreaAndRouteFromScenario()
    {
        ScenarioDefinition s = AssessmentTests.DemoB2();
        DebriefMapData d = DebriefMap.BuildStatic(SessionArea.From(s.waterArea), SessionScenario.From(s));
        Assert.IsTrue(d.HasArea && d.HasRoute);
        Assert.AreEqual(1, d.Views.Count);
        Assert.IsTrue(d.Lines.Exists(l => l.Layer == MapLayer.Corridor));
        Assert.IsFalse(d.Lines.Exists(l => l.Layer == MapLayer.OwnTrack || l.Layer == MapLayer.TargetTrack), "траектории — на подвижном слое");
        foreach (RouteWaypoint w in s.route) Assert.IsTrue(d.Views[0].Bounds.Contains(w.position), w.name);
        DebriefMapData empty = DebriefMap.BuildStatic(null, null);
        Assert.IsFalse(empty.HasArea || empty.HasRoute);
        Assert.IsFalse(empty.Views[0].Bounds.IsEmpty);
    }
}
