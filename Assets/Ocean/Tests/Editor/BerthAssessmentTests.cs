// ============================================================
// BerthAssessmentTests.cs — Критерии группы «Швартовка», прогоны, раздел отчёта (запрос 19.2б-3в)
//
// M2–M9 по событиям и строкам mooring.csv (синтетические входы: навал, удар о кромку, обрыв и
// перегрузка концов, место обучающегося, время); прогоны «за обучающегося» (BerthingSimulation,
// пять вариантов) дают ожидаемый зачёт / незачёт; оценка в шлеме = по записи бит в бит (с mooring.csv);
// в сценарии без швартовки M2–M9 нет; report.html — раздел «Швартовка», графики у причала.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class BerthAssessmentTests
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

    private static ScenarioDefinition Berth()
    {
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        var crit = ScriptableObject.CreateInstance<AssessmentCriteria>();
        crit.settings = DemoBerthScenario.Assessment();
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoBerthScenario.Fill(s, area, LowSpeedModelTests.Model45m(), crit);
        return s;
    }

    private static BerthingSimulation Run(string variant, string folder = "")
    {
        var sim = new BerthingSimulation { Scenario = Berth(), RecordFolder = folder };
        DemoAssessmentRuns.ConfigureBerth(sim.Plan, variant);
        sim.Run();
        return sim;
    }

    private static CriterionResult Item(AssessmentResult r, string code) => r.Items.Find(c => c.Code == code);

    private static MooringSample Row(double t, bool contact, double ratio, double angle, BridgeStation st)
    {
        var s = new MooringSample
        {
            Time = t, Berth = 0, BowGapM = 0.4, SternGapM = 0.4, AngleDeg = angle, FenderContact = contact, FenderDeflectionRatio = ratio,
            Station = (int)st, StagesDone = 0, Lines = new MooringLineSample[MooringSample.LineCount]
        };
        for (int i = 0; i < MooringSample.LineCount; i++) s.Lines[i] = new MooringLineSample { Bollard = -1, LengthM = double.NaN };
        return s;
    }

    private static ExerciseEvent Ev(ExerciseEventType type, double t, int index = -1, double value = double.NaN, double value2 = double.NaN, string subject = "") =>
        new ExerciseEvent(type) { Time = t, InExercise = true, Index = index, Value = value, Value2 = value2, Subject = subject };

    // ─────────── Критерии на синтетических входах ───────────
    [Test]
    public void Criteria_FromEventsAndMooringRows()
    {
        ScenarioDefinition sc = Berth();
        var a = new ExerciseAssessor(AssessmentContext.FromScenario(sc, sc.shipParameters));
        double hdg = sc.initial.headingDeg;
        a.AddEvent(Ev(ExerciseEventType.ExerciseStarted, 0));
        for (int k = 0; k <= 50; k++)
        {
            double t = 10.0 * k;
            a.AddSample(new TrackSample { Time = t, HeadingDeg = hdg, Leg = -1 }, null);
            a.AddMooring(Row(t, t >= 100, t >= 100 ? 0.6 : 0.0, t >= 100 ? -4.0 : 0.0, BridgeStation.WingStarboard));
        }
        a.AddEvent(Ev(ExerciseEventType.LineMadeFast, 50, 0, 20, 3, "носовой продольный"));
        a.AddEvent(Ev(ExerciseEventType.BerthingImpact, 101, 72, 0.21, 3.4, "2"));
        a.AddEvent(Ev(ExerciseEventType.BerthingImpact, 150, 30, 0.08, 0.5, "1"));
        a.AddEvent(Ev(ExerciseEventType.QuayImpact, 160, -1, 0.12));
        a.AddEvent(Ev(ExerciseEventType.LineOverload, 200, 0, 140, 0.52, "носовой продольный"));
        a.AddEvent(Ev(ExerciseEventType.LineOverloadCleared, 212, 0));
        a.AddEvent(Ev(ExerciseEventType.LineOverload, 300, 4, 140, 0.52, "кормовой прижимной"));
        a.AddEvent(Ev(ExerciseEventType.LineBroken, 305, 4, 280, double.NaN, "кормовой прижимной"));
        for (int i = 1; i <= 4; i++) a.AddEvent(Ev(ExerciseEventType.MooringStage, 450, i));
        a.AddEvent(new ExerciseEvent(ExerciseEventType.ExerciseEnded) { Time = 500, InExercise = true, EndReason = ScenarioEndReason.Moored });
        AssessmentResult r = a.Finish();

        Assert.AreEqual(1.0, Item(r, AssessmentCatalog.M1).Value);
        Assert.AreEqual(0.21, Item(r, AssessmentCatalog.M2).Value, 1e-9, "наибольшая скорость навала");
        Assert.IsTrue(Item(r, AssessmentCatalog.M2).Passed);
        Assert.AreEqual(0.72, Item(r, AssessmentCatalog.M3).Value, 1e-9, "пик эпизода (72 %) выше строк 5 Гц (60 %)");
        Assert.AreEqual(1.0, Item(r, AssessmentCatalog.M4).Value);
        Assert.IsFalse(Item(r, AssessmentCatalog.M4).Passed);
        Assert.AreEqual(4.0, Item(r, AssessmentCatalog.M5).Value, 1e-9, "угол при первом касании (строка t = 100 с)");
        Assert.AreEqual(1.0, Item(r, AssessmentCatalog.M6).Value, "обрыв");
        Assert.AreEqual(17.0, Item(r, AssessmentCatalog.M7).Value, 1e-9, "12 с перегрузки + 5 с до обрыва");
        Assert.AreEqual(450.0 / sc.mooring.targetTimeS, Item(r, AssessmentCatalog.M8).Value, 1e-9);
        Assert.AreEqual(0.0, Item(r, AssessmentCatalog.M9).Value, "крыло ПБ, причал — по левому борту");
        CollectionAssert.IsSupersetOf(r.MandatoryFailed, new[] { AssessmentCatalog.M4, AssessmentCatalog.M6 });
        Assert.IsFalse(r.Passed);
        foreach (string c in new[] { "M2", "M3", "M4", "M5", "M6", "M7", "M8", "M9" })
            Assert.IsTrue(DebriefText.HasFailureText(c), $"текст нарушения {c}");
    }

    [Test]
    public void Criteria_NoContactNoLines_NotEvaluated_NotInOtherScenarios()
    {
        ScenarioDefinition sc = Berth();
        var a = new ExerciseAssessor(AssessmentContext.FromScenario(sc, sc.shipParameters));
        a.AddSample(new TrackSample { Time = 0, Leg = -1 }, null);
        a.AddMooring(Row(0, false, 0, 0, BridgeStation.Helm));
        a.AddEvent(new ExerciseEvent(ExerciseEventType.ExerciseEnded) { Time = 1200, InExercise = true, EndReason = ScenarioEndReason.TimeLimit });
        AssessmentResult r = a.Finish();
        foreach (string c in new[] { "M2", "M3", "M5", "M6", "M7", "M8", "M9" })
            Assert.IsFalse(Item(r, c).Evaluated, $"{c} — не оценивался");
        Assert.IsTrue(Item(r, AssessmentCatalog.M4).Evaluated, "удара не было — 0");

        // Сценарий без швартовки — группы нет
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        var b2 = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoScenario.Fill(b2, area, LowSpeedModelTests.Model45m());
        var b = new ExerciseAssessor(AssessmentContext.FromScenario(b2, b2.shipParameters));
        b.AddSample(new TrackSample { Time = 0, Leg = 0 }, null);
        b.AddMooring(Row(0, true, 1.2, 10, BridgeStation.Helm));
        b.AddEvent(new ExerciseEvent(ExerciseEventType.ExerciseEnded) { Time = 10, InExercise = true, EndReason = ScenarioEndReason.Aborted });
        Assert.IsFalse(b.Finish().Items.Exists(c => c.Info.Group == AssessmentGroup.Mooring));
    }

    // ─────────── Прогоны «за обучающегося» ───────────
    [Test]
    public void Runs_VariantsGiveExpectedVerdicts()
    {
        var results = new Dictionary<string, BerthingSimulation>();
        foreach (string v in DemoAssessmentRuns.BerthVariants) results[v] = Run(v);
        foreach (KeyValuePair<string, BerthingSimulation> kv in results)
        {
            AssessmentResult r = kv.Value.LiveResult;
            TestContext.Out.WriteLine($"{DemoAssessmentRuns.Title(kv.Key)}: {kv.Value.EndReason} {ScenarioText.Clock(kv.Value.EndTime)}, навал {kv.Value.MaxImpactKn:F2} уз, балл {r.Score:F1}, " +
                                      $"{(r.Passed ? "зачёт" : "незачёт")}; M2 {Item(r, "M2").Value:F2}, M3 {Item(r, "M3").Value:F2}, M5 {Item(r, "M5").Value:F1}, M8 {Item(r, "M8").Value:F2}, M9 {Item(r, "M9").Value:F0}");
            Assert.AreEqual(ScenarioEndReason.Moored, kv.Value.EndReason, kv.Key);
            Assert.AreEqual(1.0, Item(r, AssessmentCatalog.M1).Value, kv.Key);
        }
        AssessmentResult ok = results["berth_correct"].LiveResult;
        Assert.IsTrue(ok.Passed, "правильно — зачёт");
        Assert.LessOrEqual(Item(ok, "M2").Value, 0.3);
        Assert.IsTrue(Item(ok, "M5").Passed && Item(ok, "M8").Passed && Item(ok, "M9").Passed);

        AssessmentResult fast = results["berth_fast"].LiveResult;
        Assert.IsFalse(fast.Passed, "выбирать без остановок — незачёт");
        Assert.IsFalse(Item(fast, "M2").Passed); Assert.IsFalse(Item(fast, "M3").Passed);

        AssessmentResult angle = results["berth_angle"].LiveResult;
        Assert.IsFalse(Item(angle, "M5").Passed, "подход под углом — M5");
        Assert.IsFalse(angle.Passed);

        AssessmentResult helm = results["berth_helm"].LiveResult;
        Assert.AreEqual(0.0, Item(helm, "M9").Value, "у штурвала — M9");
        Assert.IsTrue(helm.Passed, "M9 не обязательный");
        Assert.Less(helm.Score, ok.Score);

        AssessmentResult slow = results["berth_slow"].LiveResult;
        Assert.Greater(Item(slow, "M8").Value, 1.0, "дольше контрольного времени");
        Assert.Less(Item(slow, "M8").Score, Item(ok, "M8").Score, "балл за время ниже (общий может быть выше: навал мягче — веса группы, ThesisNotes 19.23)");
    }

    [Test]
    public void Run_LiveEqualsOffline_ReportHasMooringSection()
    {
        string dir = NewDir();
        try
        {
            BerthingSimulation sim = Run("berth_correct", dir);
            RecordedSession s = SessionReader.Load(dir);
            Assert.Greater(s.Mooring.Count, 0, "mooring.csv записан");
            Assert.AreEqual(s.Track.Count, s.Mooring.Count);
            AssessmentResult offline = SessionAssessment.Assess(s);
            Assert.AreEqual(sim.LiveResult.ToCsv(), offline.ToCsv(), "оценка в шлеме = по записи (с mooring.csv)");
            Assert.AreEqual(File.ReadAllText(Path.Combine(dir, SessionFormat.AssessmentFile)).TrimStart('﻿'), offline.ToCsv());

            List<DebriefPlot> plots = DebriefPlots.Build(s, offline);
            Assert.AreEqual(6, plots.Count, "4 прежних + зазор и скорость к причалу");
            Assert.AreEqual("berth_gap", plots[4].Key);
            Assert.IsTrue(plots[5].Marks.Exists(m => m.Kind == PlotMarkKind.Berthing && m.Label == "ошвартован"));

            string html = SessionReport.Build(s, offline, DebriefMap.Build(s, offline));
            StringAssert.Contains("<h2>", html);
            StringAssert.Contains("Швартовка</h2>", html);
            StringAssert.Contains("навал на кранцы", html);
            StringAssert.Contains("носовой продольный", html);
            StringAssert.Contains("крыло ЛБ", html);
            StringAssert.Contains("Зазор борта до кромки причала", html);
        }
        finally { Delete(dir); }
    }

    [Test]
    public void OtherScenario_ReportUnchangedByMooringSection()
    {
        string dir = NewDir();
        try
        {
            ExerciseSimulation sim = AssessmentTests.B3Run("correct", dir);
            RecordedSession s = SessionReader.Load(dir);
            Assert.AreEqual(0, s.Mooring.Count, "без причала mooring.csv нет");
            Assert.AreEqual(4, DebriefPlots.Build(s, sim.LiveResult).Count);
            StringAssert.DoesNotContain("Швартовка</h2>", SessionReport.Build(s, sim.LiveResult, DebriefMap.Build(s, sim.LiveResult)));
        }
        finally { Delete(dir); }
    }
}
