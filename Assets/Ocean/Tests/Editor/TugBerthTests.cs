// ============================================================
// TugBerthTests.cs — Сценарий 19-02 «Швартовка с буксиром при отжимном ветре» (запрос 19.3в, Core)
//
// Критерии группы «Буксир» G1–G4 на синтетических событиях (и «не оценивались» без буксира); сценарий 19-02
// (отжимной ветер со случайным сектором, буксир разрешён, 3 СС); прогоны «за обучающегося» дают ожидаемые итоги;
// оценка в шлеме = по записи; в отчёте — раздел «Буксир и ветер» и график буксира; буксир — «TUG» в targets.csv.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class TugBerthTests
{
    private static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "VRBridgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Delete(string dir)
    {
        try { Directory.Delete(dir, true); } catch (Exception) { }
    }

    private static ScenarioDefinition TugBerth()
    {
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        var crit = ScriptableObject.CreateInstance<AssessmentCriteria>();
        crit.settings = DemoTugBerthScenario.Assessment();
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoTugBerthScenario.Fill(s, area, LowSpeedModelTests.Model45m(), crit);
        return s;
    }

    private static BerthingSimulation Run(string variant, string folder = "")
    {
        var sim = new BerthingSimulation { Scenario = TugBerth(), RecordFolder = folder };
        DemoAssessmentRuns.ConfigureTugBerth(sim.Plan, variant);
        sim.Run();
        return sim;
    }

    private static CriterionResult Item(AssessmentResult r, string code) => r.Items.Find(c => c.Code == code);

    private static ExerciseEvent Ev(ExerciseEventType type, double t, int index = -1, double value = double.NaN, double value2 = double.NaN, string subject = "") =>
        new ExerciseEvent(type) { Time = t, InExercise = true, Index = index, Value = value, Value2 = value2, Subject = subject };

    [Test]
    public void Criteria_G1toG4_FromEvents_NotEvaluatedWithoutTug()
    {
        ScenarioDefinition sc = TugBerth();
        var a = new ExerciseAssessor(AssessmentContext.FromScenario(sc, sc.shipParameters));
        a.AddEvent(Ev(ExerciseEventType.ExerciseStarted, 0));
        for (int k = 0; k <= 10; k++) a.AddSample(new TrackSample { Time = 10.0 * k, HeadingDeg = sc.initial.headingDeg, Leg = -1 }, null);
        a.AddEvent(Ev(ExerciseEventType.TugCalled, 5, (int)TugStation.Stern, 196.1, double.NaN, "к корме"));
        a.AddEvent(Ev(ExerciseEventType.TowLineMadeFast, 6, -1, 30, 490.3));
        a.AddEvent(Ev(ExerciseEventType.TugOrder, 7, (int)TugMode.Pull, 25, 180, "тянуть назад, 25 %"));
        a.AddEvent(Ev(ExerciseEventType.TugOrder, 20, (int)TugMode.Pull, 100, 180, "тянуть назад, 100 %"));
        a.AddEvent(Ev(ExerciseEventType.TowLineOverload, 22, -1, 260, 0.53));
        a.AddEvent(Ev(ExerciseEventType.TowLineOverloadCleared, 34));
        a.AddEvent(Ev(ExerciseEventType.TowLineBroken, 60, -1, 495));
        a.AddEvent(Ev(ExerciseEventType.TugPushContact, 70, -1, 0.4));
        a.AddEvent(Ev(ExerciseEventType.TugPushContact, 80, -1, 0.8));
        a.AddEvent(new ExerciseEvent(ExerciseEventType.ExerciseEnded) { Time = 100, InExercise = true, EndReason = ScenarioEndReason.Aborted, ByInstructor = true });
        AssessmentResult r = a.Finish();
        foreach (CriterionResult c in r.Items) if (c.Info.Group == AssessmentGroup.Towing) TestContext.Out.WriteLine($"{c.Code} {AssessmentResult.ValueText(c)} {(c.Passed ? "+" : "×")} {c.Note}");
        Assert.AreEqual(1.0, Item(r, "G1").Value, "обрыв");
        Assert.IsFalse(Item(r, "G1").Passed);
        CollectionAssert.Contains(r.MandatoryFailed, "G1");
        Assert.AreEqual(12.0, Item(r, "G2").Value, 1e-9, "перегрузка 22…34 с");
        Assert.IsFalse(Item(r, "G2").Passed);
        Assert.AreEqual(75.0, Item(r, "G3").Value, 1e-9, "скачок 25 → 100 %");
        Assert.IsFalse(Item(r, "G3").Passed);
        Assert.AreEqual(0.8, Item(r, "G4").Value, 1e-9);
        Assert.IsFalse(Item(r, "G4").Passed);
        Assert.IsTrue(r.GroupScores.ContainsKey(AssessmentGroup.Towing));
        Assert.AreEqual(0.5, AssessmentSettings.Default().GroupWeight(AssessmentGroup.Towing));
        foreach (string code in new[] { "G1", "G2", "G3", "G4" }) Assert.IsTrue(DebriefText.HasFailureText(code), code);

        // Буксир вызван, но только «стоп» и без троса — G1–G3 не оценивались; без буксира — группы нет
        var b = new ExerciseAssessor(AssessmentContext.FromScenario(sc, sc.shipParameters));
        b.AddEvent(Ev(ExerciseEventType.ExerciseStarted, 0));
        b.AddSample(new TrackSample { Time = 0, Leg = -1 }, null);
        b.AddEvent(Ev(ExerciseEventType.TugCalled, 1, (int)TugStation.Midship, 196.1));
        b.AddEvent(new ExerciseEvent(ExerciseEventType.ExerciseEnded) { Time = 10, InExercise = true, EndReason = ScenarioEndReason.Aborted });
        AssessmentResult rb = b.Finish();
        foreach (string code in new[] { "G1", "G2", "G3", "G4" }) Assert.IsFalse(Item(rb, code).Evaluated, code);
        var c0 = new ExerciseAssessor(AssessmentContext.FromScenario(sc, sc.shipParameters));
        c0.AddEvent(Ev(ExerciseEventType.ExerciseStarted, 0));
        c0.AddSample(new TrackSample { Time = 0, Leg = -1 }, null);
        c0.AddEvent(new ExerciseEvent(ExerciseEventType.ExerciseEnded) { Time = 10, InExercise = true, EndReason = ScenarioEndReason.Aborted });
        Assert.IsFalse(c0.Finish().Items.Exists(c => c.Info.Group == AssessmentGroup.Towing));
    }

    [Test]
    public void Scenario_1902_OffBerthWindTugAvailable()
    {
        ScenarioDefinition s = TugBerth();
        Assert.AreEqual("19-02", s.id);
        Assert.AreEqual(330.0, s.windFromDeg, 0.5, "отжимной для причала №1 (кромка 060°, вода к ЮВ)");
        Assert.AreEqual(20f, s.windKnots);
        Assert.AreEqual(60f, s.windRandomSectorDeg);
        Assert.IsTrue(s.tugAvailable);
        Assert.AreEqual(ScenarioMotionModel.Manoeuvring3Dof, s.motionModel);
        Assert.AreEqual(720f, s.mooring.targetTimeS);
        Assert.IsTrue(s.IsMooring);
        List<string> p = s.Validate();
        Assert.IsFalse(p.Exists(x => x.Contains("3 СС") || x.Contains("Ветер") || x.Contains("Буксир") || x.StartsWith("Швартовка")), string.Join("\n", p));
        StringAssert.Contains("отжимной", s.briefing);
        StringAssert.Contains("Буксир: по вызову", ExerciseTasks.BriefingFacts(s));
    }

    [Test]
    public void Runs_TugVariantsGiveExpectedVerdicts()
    {
        var res = new Dictionary<string, BerthingSimulation>();
        foreach (string v in DemoAssessmentRuns.TugBerthVariants) res[v] = Run(v);
        foreach (KeyValuePair<string, BerthingSimulation> kv in res)
        {
            AssessmentResult r = kv.Value.LiveResult;
            CriterionResult g3 = Item(r, "G3"), m2 = Item(r, "M2");
            TestContext.Out.WriteLine($"{DemoAssessmentRuns.Title(kv.Key)}: {kv.Value.EndReason} {ScenarioText.Clock(kv.Value.EndTime)}, навал {kv.Value.MaxImpactKn:F2} уз, " +
                                      $"балл {r.Score:F1}, {(r.Passed ? "зачёт" : "незачёт")}; M2 {(m2 != null && m2.Evaluated ? m2.Value.ToString("F2") : "—")}, " +
                                      $"M8 {(Item(r, "M8")?.Evaluated == true ? Item(r, "M8").Value.ToString("F2") : "—")}, G3 {(g3 != null && g3.Evaluated ? g3.Value.ToString("F0") : "—")}, " +
                                      $"G4 {(Item(r, "G4")?.Evaluated == true ? Item(r, "G4").Value.ToString("F2") : "—")}");
        }
        AssessmentResult ok = res["tugberth_correct"].LiveResult;
        Assert.AreEqual(ScenarioEndReason.Moored, res["tugberth_correct"].EndReason);
        Assert.IsTrue(ok.Passed, "правильно — зачёт");
        Assert.LessOrEqual(Item(ok, "M2").Value, 0.3);
        Assert.AreEqual(25.0, Item(ok, "G3").Value, 1e-9, "тяга ступенями");
        Assert.IsTrue(Item(ok, "G4").Passed);

        BerthingSimulation none = res["tugberth_none"];
        Assert.IsFalse(none.LiveResult.Items.Exists(c => c.Info.Group == AssessmentGroup.Towing), "без буксира — группы нет");
        Assert.Greater(none.EndTime, res["tugberth_correct"].EndTime + 300.0, "без буксира — заметно дольше (концы издалека против ветра)");

        AssessmentResult full = res["tugberth_full"].LiveResult;
        Assert.IsFalse(full.Passed);
        Assert.AreEqual(100.0, Item(full, "G3").Value, 1e-9, "сразу полная тяга");
        Assert.IsFalse(Item(full, "G3").Passed);
        Assert.IsFalse(Item(full, "M2").Passed, "навал");

        BerthingSimulation late = res["tugberth_late"];
        Assert.Greater(late.EndTime, res["tugberth_correct"].EndTime + 300.0, "буксир поздно — судно снесло, дольше");
    }

    [Test]
    public void Run_LiveEqualsOffline_ReportHasTowingSectionAndPlot()
    {
        string dir = NewDir();
        try
        {
            BerthingSimulation sim = Run("tugberth_correct", dir);
            RecordedSession s = SessionReader.Load(dir);
            Assert.AreEqual(s.Track.Count, s.Mooring.Count);
            Assert.IsTrue(s.Mooring.Exists(m => m.Towing.Present && m.Towing.Mode == TugMode.Push), "столбцы буксира в mooring.csv");
            Assert.IsTrue(s.Mooring.TrueForAll(m => Math.Abs(m.Towing.WindKn - 20.0) < 1e-9), "ветер в каждой строке");
            Assert.IsTrue(s.Targets.Exists(t => t.Id == TugTarget.Id), "буксир — «TUG» в targets.csv");
            AssessmentResult offline = SessionAssessment.Assess(s);
            Assert.AreEqual(sim.LiveResult.ToCsv(), offline.ToCsv(), "оценка в шлеме = по записи (с буксиром и ветром)");
            Assert.IsFalse(offline.Encounters.Exists(e => e.TargetId.StartsWith(TugTarget.Id, StringComparison.Ordinal)), "буксир — не сближение");
            Assert.AreEqual(20.0, s.Meta.scenario.wind_kn);
            Assert.IsTrue(s.Meta.scenario.tug_available);

            List<DebriefPlot> plots = DebriefPlots.Build(s, offline);
            DebriefPlot tug = plots.Find(p => p.Key == "tug");
            Assert.IsNotNull(tug, "график буксира");
            Assert.IsTrue(tug.Marks.Exists(m => m.Label == "25 %"));
            string html = SessionReport.Build(s, offline, DebriefMap.Build(s, offline));
            StringAssert.Contains("Буксир и ветер</h2>", html);
            StringAssert.Contains("Ветер сценария: 20 уз, откуда 330°", html);
            StringAssert.Contains("Буксиру: «толкать на ЛБ, 25 %»", html);
            StringAssert.Contains("Буксир: заданная тяга, упор в борт, натяжение троса", html);
            StringAssert.Contains("<td>Буксир</td>", html);
        }
        finally { Delete(dir); }
    }
}
