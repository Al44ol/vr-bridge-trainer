// ============================================================
// TugWorkAssessmentTests.cs — Критерии работы своего буксира G5–G11, прогоны «за обучающегося», «без оценки» (запрос 19.4в)
//
// Каталог и набор 19-03 (обязательные G5, G8, G10, G11); реакция на команды лоцмана упором (TugWorkReaction) на
// искусственных событиях; прогон «правильно» — все G зачтены, ЗАЧЁТ, оценка в шлеме = по записи; каждый вариант с ошибкой
// не проходит свой критерий; «нечего оценивать» — «БЕЗ ОЦЕНКИ», а не «НЕЗАЧЁТ»; report.html — реакция и график упора.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class TugWorkAssessmentTests
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

    // Прогон варианта с записью; оценка по записи должна совпасть с оценкой «в шлеме»
    private static TugWorkSimulation Run(string variant, out RecordedSession session, double maxTime = 0.0)
    {
        string dir = Path.Combine(Path.GetTempPath(), "VRBridgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        dirs.Add(dir);
        var sim = new TugWorkSimulation { Scenario = Scenario(), RecordFolder = dir };
        DemoAssessmentRuns.ConfigureTugWork(sim.Plan, variant);
        sim.Run(maxTime);
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
    public void Catalog_G5toG11_InTugWorkSet_Mandatory()
    {
        string[] codes = { "G5", "G6", "G7", "G8", "G9", "G10", "G11" };
        var mandatory = new HashSet<string> { "G5", "G8", "G10", "G11" };
        AssessmentSettings set = DemoTugWorkScenario.Assessment();
        foreach (string code in codes)
        {
            CriterionInfo info = AssessmentCatalog.Find(code);
            Assert.IsNotNull(info, code);
            Assert.AreEqual(AssessmentGroup.Towing, info.Group, code);
            Assert.AreEqual(mandatory.Contains(code), info.Mandatory, $"{code}: обязательный");
            Assert.IsTrue(set.Get(code).enabled, $"{code} в наборе 19-03");
            Assert.IsTrue(DebriefText.HasFailureText(code), $"текст нарушения {code}");
        }
        Assert.AreEqual(30.0, AssessmentCatalog.Find("G11").Pass, 1e-9, "реакция — зачёт до 30 с");
        Assert.AreEqual(0.3, AssessmentCatalog.Find("G10").Pass, 1e-9, "навал — как M2");
        Assert.IsTrue(AssessmentCatalog.Find("G8").HigherIsBetter);

        // Старый набор (до 19.4в) — G5–G11 дописываются сами
        var old = DemoTugWorkScenario.Assessment();
        old.criteria.RemoveAll(c => Array.IndexOf(codes, c.code) >= 0);
        Assert.AreEqual(codes.Length, old.AddMissing());
    }

    [Test]
    public void Reaction_ToPowerOrders_FromSettledHandle()
    {
        ExerciseEvent Order(double t, PilotOrderKind k, double pct, string text) =>
            new ExerciseEvent(ExerciseEventType.PilotOrder) { Time = t, Index = (int)k, Value = pct, Subject = text };
        ExerciseEvent Handle(double t, double pct) => new ExerciseEvent(ExerciseEventType.JoystickChanged) { Time = t, Value = pct };
        var orders = new List<ExerciseEvent>
        {
            Order(5, PilotOrderKind.Info, 30, "До причала 30 м, идёт 0,30 уз"),   // обстановка — не команда упором
            Order(10, PilotOrderKind.PushStep, 50, "Толкай малым"),
            Order(40, PilotOrderKind.PushStep, 75, "Толкай средним"),
            Order(60, PilotOrderKind.StopPush, 0, "Стоп толкать"),
            Order(80, PilotOrderKind.PushStep, 25, "Держи самым малым")
        };
        var handle = new List<ExerciseEvent> { Handle(4, 25), Handle(18, 49), Handle(45, 60), Handle(61, -30), Handle(79, 25) };
        List<PilotPowerReaction> r = TugWorkReaction.Compute(orders, handle, 100.0);
        Assert.AreEqual(4, r.Count, "только команды упором");
        Assert.IsTrue(r[0].Executed); Assert.AreEqual(8.0, r[0].ReactionS, 1e-9, "малый через 8 с (49 % — ступень 2)");
        Assert.AreEqual(1, r[0].StepBefore);
        Assert.IsFalse(r[1].Executed, "60 % — ступень 2, не средний: не выполнена до «стоп»");
        Assert.AreEqual(20.0, TugWorkReaction.InForce(r[1]), 1e-9);
        Assert.IsTrue(r[2].Executed); Assert.AreEqual(1.0, r[2].ReactionS, 1e-9, "назад — тоже «не толкает»");
        Assert.IsTrue(r[3].Executed); Assert.AreEqual(0.0, r[3].ReactionS, 1e-9, "уже стояла на ступени");

        // Последняя команда ещё действует — итог неизвестен (во время упражнения не оценивается)
        List<PilotPowerReaction> live = TugWorkReaction.Compute(new List<ExerciseEvent> { Order(10, PilotOrderKind.PushStep, 100, "Толкай полным") }, handle, double.NaN);
        Assert.IsFalse(live[0].Resolved);
    }

    [Test]
    public void Correct_AllPassed_JoystickEvents_ReportAndPlot()
    {
        TugWorkSimulation sim = Run("tugwork_correct", out RecordedSession s);
        AssessmentResult r = sim.LiveResult;
        Assert.AreEqual(ScenarioEndReason.TugWorkDone, sim.EndReason);
        foreach (string code in new[] { "G5", "G6", "G7", "G8", "G9", "G10", "G11" })
        {
            CriterionResult c = Item(r, code);
            Assert.IsNotNull(c, code);
            Assert.IsTrue(c.Evaluated && c.Passed, $"{code}: {c.Note}");
        }
        Assert.IsTrue(r.Passed, r.Headline());
        Assert.Greater(r.Score, 99.0);
        Assert.Less(Item(r, "G11").Value, 2.0, "автомат ставит ступень сразу (0,5 с — установившееся положение)");
        Assert.Greater(s.Events.FindAll(e => e.Type == ExerciseEventType.JoystickChanged).Count, 5, "в записи — положения рукоятки");

        string html = SessionReport.Build(s, SessionAssessment.Assess(s), DebriefMap.Build(s, r));
        StringAssert.Contains("Команды лоцмана упором и реакция</h3>", html);
        StringAssert.Contains("«Толкай полным»", html);
        StringAssert.Contains("с упором в борт —", html);
        StringAssert.DoesNotContain("не выполнена (", html);
        StringAssert.Contains("Упор буксира: команда лоцмана и рукоятка", html);
        DebriefPlot p = DebriefPlots.Build(s, r).Find(x => x.Key == "tug_power");
        Assert.IsNotNull(p, "график упора");
        Assert.AreEqual(3, p.Series.Count, "команда, рукоятка, колонки");
        Assert.AreEqual(4, p.Marks.Count, "отметки — 4 задания");
    }

    [TestCase("tugwork_ignore", "G11", false)]
    [TestCase("tugwork_late", "G11", false)]
    [TestCase("tugwork_full", "G10", false)]
    [TestCase("tugwork_fast", "G6", true)]
    [TestCase("tugwork_angle", "G7", true)]
    [TestCase("tugwork_release", "G8", false)]
    public void Variant_FailsItsCriterion(string variant, string code, bool passed)
    {
        TugWorkSimulation sim = Run(variant, out RecordedSession _);
        AssessmentResult r = sim.LiveResult;
        CriterionResult c = Item(r, code);
        Assert.IsNotNull(c, code);
        Assert.IsTrue(c.Evaluated, $"{variant}: {code} оценён");
        Assert.IsFalse(c.Passed, $"{variant}: {code} не зачтён — {c.Note}");
        Assert.AreEqual(passed, r.Passed, $"{variant}: {r.Headline()}; не выполнены {string.Join(", ", r.MandatoryFailed)}");
        Assert.AreEqual(4, sim.System.DoneCount, $"{variant}: задание всё же выполнено ({ScenarioText.Clock(sim.EndTime)})");
        TestContext.Out.WriteLine($"{variant}: {r.Headline()}; {code} = {AssessmentResult.ValueText(c)} — {c.Note}");
    }

    [Test]
    public void NothingToAssess_IsNotAssessed_NotFail()
    {
        // Пустой итог: ни одного критерия с весом — «БЕЗ ОЦЕНКИ»
        var empty = new AssessmentResult { Final = true };
        empty.Aggregate(AssessmentSettings.Default());
        Assert.IsTrue(empty.NotAssessed);
        Assert.IsFalse(empty.Passed);
        Assert.AreEqual("БЕЗ ОЦЕНКИ", empty.VerdictText);
        StringAssert.Contains("— БЕЗ ОЦЕНКИ", empty.Headline());
        StringAssert.DoesNotContain("НЕЗАЧЁТ", empty.ToPanelText());
        StringAssert.Contains("без оценки: нечего оценивать", empty.ToCsv());
        Assert.IsTrue(SessionAssessmentSummary.From(empty).not_assessed);
        Assert.AreEqual("БЕЗ ОЦЕНКИ", DebriefText.Verdict(empty));
        Assert.IsFalse(new AssessmentResult { Final = false }.NotAssessed, "во время упражнения — не итог");

        // 19-03 прервано через 30 с: к борту не подходил — оценивать нечего
        TugWorkSimulation sim = Run("tugwork_correct", out RecordedSession _, 30.0);
        Assert.AreEqual(ScenarioEndReason.Aborted, sim.EndReason);
        Assert.IsTrue(sim.LiveResult.NotAssessed, sim.LiveResult.Headline());
        Assert.IsFalse(Item(sim.LiveResult, "G5").Evaluated, "задание — досрочно, не оценивается");
    }
}
