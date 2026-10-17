// ============================================================
// TugEventTests.cs — «Случайное событие» в 19-03: конец отходит, ближний на стопоре, буксир дожимает (запрос 19.4г, Core)
//
// Правильно: событие на заданном зазоре, концы ближнего конца поданы сразу, буксир у отошедшего конца, стопор, судно у
// кранцев, задание выполнено, G12/G13 зачтены, оценка в шлеме = по записи, отчёт и график; варианты с ошибкой не проходят
// G12/G13; без события G12/G13 не оцениваются; случайные конец, угол, зазор — в пределах задания; session.json.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class TugEventTests
{
    private static readonly List<string> dirs = new List<string>();

    private static ScenarioDefinition Scenario()
    {
        var tug = ScriptableObject.CreateInstance<ShipParameters>();
        tug.name = "ShipParameters_Tug";
        ShipPresets.FillTug(tug);
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        var crit = ScriptableObject.CreateInstance<AssessmentCriteria>();
        crit.settings = DemoTugWorkScenario.Assessment();
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoTugWorkScenario.Fill(s, area, tug, LowSpeedModelTests.Mariner(), crit);
        return s;
    }

    private static TugWorkSimulation Run(string variant, out RecordedSession session)
    {
        string dir = Path.Combine(Path.GetTempPath(), "VRBridgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        dirs.Add(dir);
        var sim = new TugWorkSimulation { Scenario = Scenario(), RecordFolder = dir };
        if (variant.StartsWith("tugevent")) DemoAssessmentRuns.ConfigureTugEvent(sim, variant);
        else DemoAssessmentRuns.ConfigureTugWork(sim.Plan, variant);
        sim.Run();
        session = SessionReader.Load(dir);
        Assert.AreEqual(sim.LiveResult.ToCsv(), SessionAssessment.Assess(session).ToCsv(), $"{variant}: оценка в шлеме = по записи");
        return sim;
    }

    private static CriterionResult Item(AssessmentResult r, string code) => r.Items.Find(c => c.Code == code);

    [OneTimeTearDown]
    public void Cleanup()
    {
        foreach (string d in dirs) { try { Directory.Delete(d, true); } catch (Exception) { } }
        dirs.Clear();
    }

    [Test]
    public void Correct_NearEndPinned_TugPushesOffEnd_Finishes()
    {
        TugWorkSimulation sim = Run("tugevent_correct", out RecordedSession s);
        TugWorkSystem sys = sim.System;
        Assert.AreEqual(ScenarioEndReason.TugWorkDone, sim.EndReason, ScenarioText.Clock(sim.EndTime));
        Assert.IsTrue(sys.EventFired && sys.TugAtEnd && sys.EventPinned && sys.EventDone);
        var phases = new List<int>();
        foreach (ExerciseEvent e in sim.Events) if (e.Type == ExerciseEventType.TugWorkEvent) phases.Add(e.Index);
        CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4 }, phases, "все четыре отметки события");
        Assert.AreEqual(1, phases[0], "сначала — «нос отходит»");
        Assert.AreEqual(4, phases[phases.Count - 1], "в конце — «судно у кранцев»");

        // В момент события: «стоп толкать», команда к носу, кормовые поданы сразу (стопор)
        ExerciseEvent start = sim.Events.Find(e => e.Type == ExerciseEventType.TugWorkEvent && e.Index == (int)TugEventPhase.ToEnd);
        Assert.AreEqual(6.0, start.Value, 1e-9);
        Assert.IsTrue(sim.Events.Exists(e => e.Type == ExerciseEventType.PilotOrder && e.Index == (int)PilotOrderKind.StopPush && Math.Abs(e.Time - start.Time) < 1e-6));
        Assert.IsTrue(sim.Events.Exists(e => e.Type == ExerciseEventType.PilotOrder && e.Index == (int)PilotOrderKind.ToEnd && e.Subject.Contains("к носу")));
        int sternLines = sim.Events.FindAll(e => e.Type == ExerciseEventType.LineMadeFast && e.Time - start.Time < 2.0 && e.Time >= start.Time).Count;
        Assert.AreEqual(2, sternLines, "кормовой прижимной и продольный — сразу");
        Assert.IsTrue(sys.Client.Secured, "концы закреплены");

        AssessmentResult r = sim.LiveResult;
        CriterionResult g12 = Item(r, "G12"), g13 = Item(r, "G13");
        Assert.IsTrue(g12.Evaluated && g12.Passed, g12.Note);
        Assert.IsTrue(g13.Evaluated && g13.Passed, g13.Note);
        Assert.IsTrue(r.Passed, r.Headline());
        TestContext.Out.WriteLine($"{r.Headline()}; G12 {g12.Note}; G13 {g13.Note}; финиш {ScenarioText.Clock(sim.EndTime)}");

        string html = SessionReport.Build(s, SessionAssessment.Assess(s), DebriefMap.Build(s, r));
        StringAssert.Contains("Случайное событие</h3>", html);
        StringAssert.Contains("Событие: нос отходит до 6", html);
        StringAssert.Contains("«Нос отходит, до 6°. Корма ложится. Буксир — к носу»", html);
        DebriefPlot p = DebriefPlots.Build(s, r).Find(x => x.Key == "tug_power");
        Assert.IsTrue(p.Marks.Exists(m => m.Label == "нос отходит"), "отметка события на графике упора");
    }

    [TestCase("tugevent_stay", false, false)]
    [TestCase("tugevent_wrong", false, false)]
    [TestCase("tugevent_late", false, true)]
    public void Variant_FailsTransfer(string variant, bool g13Passed, bool passed)
    {
        TugWorkSimulation sim = Run(variant, out RecordedSession _);
        AssessmentResult r = sim.LiveResult;
        CriterionResult g12 = Item(r, "G12"), g13 = Item(r, "G13");
        Assert.IsTrue(g12.Evaluated && !g12.Passed, $"{variant}: G12 — {g12.Note}");
        Assert.IsTrue(g13.Evaluated, $"{variant}: G13 оценён");
        Assert.AreEqual(g13Passed, g13.Passed, $"{variant}: G13 — {g13.Note}");
        Assert.AreEqual(passed, r.Passed, $"{variant}: {r.Headline()}; не выполнены {string.Join(", ", r.MandatoryFailed)}");
        TestContext.Out.WriteLine($"{variant}: {r.Headline()}; G12 {g12.Note}; G13 {g13.Note}");
    }

    [Test]
    public void NoEvent_G12G13NotAssessed()
    {
        TugWorkSimulation sim = Run("tugwork_correct", out RecordedSession _);
        Assert.IsFalse(sim.System.EventFired);
        Assert.IsFalse(Item(sim.LiveResult, "G12").Evaluated);
        Assert.IsFalse(Item(sim.LiveResult, "G13").Evaluated);
        Assert.IsTrue(sim.LiveResult.Passed, "19.4в — как прежде");
    }

    [Test]
    public void RandomEvent_WithinLimits_SessionJson()
    {
        ScenarioDefinition s = Scenario();
        Assert.IsTrue(s.tugWork.angleEvent, "19-03 — со случайным событием");
        Assert.GreaterOrEqual(DemoTugWorkScenario.Version, 4, "19.4г — версия 4 (ассет обновится сам)");
        StringAssert.Contains("событие", s.briefing);
        var tug = s.shipParameters;
        var sys = new TugWorkSystem(s.tugWork, s.tugWorkClient, s.tugWork.BerthIn(s.waterArea), TugSpec.ForOwnTug(tug));
        var own = new ShipDynamicsModel(tug) { MotionOverride = MotionModel.Manoeuvring3Dof };
        bool bow = false, stern = false;
        for (int seed = 0; seed < 40; seed++)
        {
            sys.Place(own);
            Assert.IsFalse(sys.EventArmed, "Place снимает событие — его взводят заново");
            sys.ArmEventRandom(new System.Random(seed));
            Assert.That(sys.EventAngleDeg, Is.InRange(4.0, 8.0));
            Assert.AreEqual(0.0, Math.IEEERemainder(sys.EventAngleDeg * 2.0, 1.0), 1e-9, "шаг 0,5°");
            Assert.That(sys.EventGapM, Is.InRange(4.0, 7.0));
            bow |= sys.EventOffEnd > 0;
            stern |= sys.EventOffEnd < 0;
        }
        Assert.IsTrue(bow && stern, "отходит и нос, и корма");
        Assert.AreEqual(0.3f, s.tugWork.endZoneX, 1e-6f, "зона конца — треть длины от миделя");

        TugWorkDefinition back = SessionTugWork.From(s).ToDefinition();
        Assert.IsTrue(back.angleEvent);
        Assert.AreEqual(4f, back.eventGapMinM, 1e-4f);
        Assert.AreEqual(7f, back.eventGapMaxM, 1e-4f);
        Assert.AreEqual(8f, back.eventAngleMaxDeg, 1e-4f);
        Assert.AreEqual(0.3f, back.endZoneX, 1e-4f);
        Assert.IsFalse(new SessionTugWork { enabled = true }.ToDefinition().angleEvent, "старые записи — без события");
    }
}
