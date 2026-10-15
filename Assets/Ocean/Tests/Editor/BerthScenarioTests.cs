// ============================================================
// BerthScenarioTests.cs — Сценарий швартовки (запрос 19.2б-3, часть «а»)
//
// Этапы задания (MooringTaskTracker): независимые отметки, «ошвартован» — после непрерывного
// удержания и отмечает все предыдущие; демо-сценарий 19-01 корректен (3 СС, левый борт, финиш
// «ошвартован», маршрутные критерии выключены); прежние сценарии не изменились; задание — в
// session.json и контексте оценки; критерий M1 по событиям MooringStage; вкладка «Задание»;
// сквозная швартовка на модели (концы, лебёдки, кранцы) доходит до «ошвартован».
// ============================================================
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class BerthScenarioTests
{
    private static ShipParameters Model45m() => LowSpeedModelTests.Model45m();

    private static WaterArea DemoArea()
    {
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        return area;
    }

    private static ScenarioDefinition Demo(out WaterArea area, out ShipParameters p)
    {
        area = DemoArea();
        p = Model45m();
        var crit = ScriptableObject.CreateInstance<AssessmentCriteria>();
        crit.settings = DemoBerthScenario.Assessment();
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoBerthScenario.Fill(s, area, p, crit);
        return s;
    }

    private static MooringTaskInput Input(double bowGap, double sternGap, double angle, double sog, bool contact = false, bool bow = false, bool stern = false) =>
        new MooringTaskInput
        {
            Readout = new BerthReadout { Valid = true, BowGapM = bowGap, SternGapM = sternGap, AngleDeg = angle },
            SpeedOverGroundKn = sog, FenderContact = contact, BowLineFast = bow, SternLineFast = stern
        };

    [Test]
    public void Tracker_StagesIndependentMooredAfterHold()
    {
        var t = new MooringTaskTracker(new MooringTaskDefinition { enabled = true });
        Assert.AreEqual(0, t.Update(Input(40, 40, 0, 2.0), 0, 0.02).Count, "далеко — ничего");
        Assert.AreEqual(0, t.Update(Input(8, 8, 15, 1.0), 1, 0.02).Count, "близко, но под углом 15° — не лагом");
        CollectionAssert.AreEqual(new[] { MooringStage.Alongside }, t.Update(Input(8, 9, 5, 1.0), 2, 0.02));
        // Концы поданы раньше, чем судно остановилось: этап 3 отмечается сам, этап 2 — нет
        CollectionAssert.AreEqual(new[] { MooringStage.LinesFast }, t.Update(Input(5, 6, 3, 0.5, bow: true, stern: true), 3, 0.02));
        Assert.IsFalse(t.Done(MooringStage.AtFenders));
        CollectionAssert.AreEqual(new[] { MooringStage.AtFenders }, t.Update(Input(0.4, 0.5, 0, 0.15, contact: true, bow: true, stern: true), 4, 0.02));
        Assert.AreEqual(3, t.DoneCount);

        // «Ошвартован»: условие должно держаться 30 с; рывок хода сбрасывает отсчёт
        double time = 4;
        for (int i = 0; i < 1000; i++) { time += 0.02; Assert.AreEqual(0, t.Update(Input(0.3, 0.3, 0, 0.05, true, true, true), time, 0.02).Count); }
        Assert.AreEqual(20.0, t.HoldTime, 1e-6);
        t.Update(Input(0.3, 0.3, 0, 0.2, true, true, true), time += 0.02, 0.02);
        Assert.AreEqual(0.0, t.HoldTime, "ход 0,2 уз — отсчёт сначала");
        IReadOnlyList<MooringStage> last = null;
        double holdFrom = time;
        for (int i = 0; i < 1600 && !t.Moored; i++) last = t.Update(Input(0.3, 0.3, 0, 0.05, true, true, true), time += 0.02, 0.02);
        Assert.IsTrue(t.Moored);
        CollectionAssert.AreEqual(new[] { MooringStage.Moored }, last);
        Assert.AreEqual(30.0, t.DoneAt(MooringStage.Moored) - holdFrom, 0.05, "ошвартован через 30 с удержания");

        // Сразу ошвартован (концы и кранцы с начала) — все этапы одним шагом, по порядку
        var u = new MooringTaskTracker(new MooringTaskDefinition { enabled = true, mooredHoldS = 0f });
        CollectionAssert.AreEqual(new[] { MooringStage.Alongside, MooringStage.AtFenders, MooringStage.LinesFast, MooringStage.Moored },
            u.Update(Input(0.3, 0.3, 0, 0.0, true, true, true), 0, 0.02));
        // Без причала (Readout не считан) — только «концы поданы» (по состоянию концов)
        CollectionAssert.AreEqual(new[] { MooringStage.LinesFast },
            new MooringTaskTracker(new MooringTaskDefinition()).Update(new MooringTaskInput { BowLineFast = true, SternLineFast = true }, 0, 0.02));
    }

    [Test]
    public void DemoScenario_ValidPortSideMooredEnd()
    {
        ScenarioDefinition s = Demo(out WaterArea area, out ShipParameters p);
        CollectionAssert.IsEmpty(s.Validate(), string.Join("; ", s.Validate()));
        Assert.IsTrue(s.IsMooring);
        Assert.AreEqual(ScenarioMotionModel.Manoeuvring3Dof, s.motionModel);
        Assert.IsTrue(s.Ends(ScenarioEndConditions.Moored));
        Assert.IsFalse(s.Ends(ScenarioEndConditions.Arrival), "прибытие по линии подхода не завершает");
        BerthArea b = area.berths[0];
        Assert.AreSame(b, s.mooring.BerthIn(s.waterArea));
        Assert.AreEqual("левым", MooringTasks.SideTitle(b, s.initial.headingDeg));
        Assert.AreEqual(b.BearingDeg, s.initial.headingDeg, 0.01, "курс — по кромке");

        // Начало: 30 м от кромки, 200 м до середины
        var m = new ShipDynamicsModel(p) { MotionOverride = MotionModel.Manoeuvring3Dof };
        m.Teleport(s.initial.position.East, s.initial.position.North, s.initial.headingDeg * System.Math.PI / 180.0, 0.0, 0.0);
        BerthReadout r = BerthReadout.Compute(b, p, m);
        Assert.AreEqual(DemoBerthScenario.StartGapM, r.BowGapM, 1e-3);
        Assert.AreEqual(DemoBerthScenario.StartGapM, r.SternGapM, 1e-3);
        b.ToLocal(s.initial.position, out double tStart, out _);
        Assert.AreEqual(0.5 * b.Length - DemoBerthScenario.StartBeforeMiddleM, tStart, 1e-6);
        Assert.AreEqual(ScenarioText.SpeedFactor(TelegraphOrder.DeadSlowAhead), ScenarioText.SpeedFactor(s.initial.telegraph));
        Assert.AreEqual(ShipDynamicsModel.SteadySpeed(p, 0.2) / ShipParameters.KnotsToMs, s.initial.speedKnots, 0.01);

        // Набор критериев: маршрут выключен, M1 включён и обязателен
        AssessmentSettings a = s.assessment.settings;
        foreach (string code in new[] { AssessmentCatalog.R1, AssessmentCatalog.R4, AssessmentCatalog.R5, AssessmentCatalog.R8a, AssessmentCatalog.R8b })
            Assert.IsFalse(a.Get(code).enabled, code);
        Assert.IsTrue(a.Get(AssessmentCatalog.M1).enabled);
        Assert.IsTrue(a.Get(AssessmentCatalog.M1).mandatory);
        Assert.AreEqual(1.0, a.GroupWeight(AssessmentGroup.Mooring));

        // Ошибки описания ловятся
        s.motionModel = ScenarioMotionModel.AsSelected;
        s.mooring.berthIndex = 5;
        s.endConditions |= ScenarioEndConditions.Arrival;
        List<string> problems = s.Validate();
        Assert.AreEqual(3, problems.Count, string.Join("; ", problems));
    }

    [Test]
    public void OldScenarios_Unchanged()
    {
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoScenario.Fill(s, DemoArea(), Model45m());
        Assert.IsFalse(s.IsMooring);
        Assert.AreEqual(ScenarioMotionModel.AsSelected, s.motionModel, "старые сценарии — модель, выбранная в тренажёре");
        CollectionAssert.IsEmpty(s.Validate());
        SessionScenario ss = SessionScenario.From(s);
        Assert.IsFalse(ss.mooring.enabled);
        Assert.IsNull(AssessmentContext.FromScenario(s, Model45m()).Mooring);
        StringAssert.Contains("Путевых точек: 3", ExerciseTasks.BriefingFacts(s));
        // Запись до 19.2б-3 (нет поля mooring) — не швартовка
        var old = JsonUtility.FromJson<SessionMeta>("{\"scenario\":{\"id\":\"B2-01\"}}");
        Assert.IsNull(AssessmentContext.From(old).Mooring);
    }

    [Test]
    public void SessionJson_MooringTaskRoundTrip()
    {
        ScenarioDefinition s = Demo(out _, out ShipParameters p);
        s.mooring.mooredHoldS = 45f;
        AssessmentContext c = AssessmentContext.FromScenario(s, p);   // через JSON, как в шлеме
        Assert.IsNotNull(c.Mooring);
        Assert.AreEqual(0, c.Mooring.berthIndex);
        Assert.AreEqual(45f, c.Mooring.mooredHoldS);
        Assert.AreEqual(s.mooring.stopGapM, c.Mooring.stopGapM);
        SessionMeta meta = SessionMeta.Create(s, p, "7", new System.DateTime(2026, 10, 16));
        Assert.AreEqual("Причал №1", meta.scenario.mooring.berth_name);
        Assert.AreEqual(DemoBerthScenario.TargetTimeSeconds, meta.scenario.mooring.target_time_s, 1e-9);
        Assert.AreEqual("Manoeuvring3Dof", meta.scenario.motion_model);
    }

    private static AssessmentResult AssessMooring(ScenarioDefinition s, ShipParameters p, int stages, ScenarioEndReason end)
    {
        var a = new ExerciseAssessor(AssessmentContext.FromScenario(s, p));
        double t = 0;
        for (int i = 1; i <= stages; i++)
            a.AddEvent(new ExerciseEvent(ExerciseEventType.MooringStage) { Index = i, Time = t += 60, Subject = MooringTasks.Title((MooringStage)i) });
        a.AddEvent(new ExerciseEvent(ExerciseEventType.ExerciseEnded) { EndReason = end, Time = t += 1 });
        a.AddSample(new TrackSample { Time = t, XteM = double.NaN }, null);
        return a.Finish();
    }

    [Test]
    public void Assessment_M1FromMooringStages()
    {
        ScenarioDefinition s = Demo(out _, out ShipParameters p);
        AssessmentResult ok = AssessMooring(s, p, 4, ScenarioEndReason.Moored);
        CriterionResult m1 = ok.Items.Find(x => x.Code == AssessmentCatalog.M1);
        Assert.IsNotNull(m1);
        Assert.AreEqual(1.0, m1.Value);
        Assert.IsTrue(m1.Passed);
        Assert.IsTrue(ok.Passed, "только группа «Швартовка», всё выполнено — зачёт");
        Assert.AreEqual(100.0, ok.Score, 1e-9);
        Assert.IsNull(ok.Items.Find(x => x.Code == AssessmentCatalog.R1), "маршрутные критерии выключены");

        AssessmentResult half = AssessMooring(s, p, 2, ScenarioEndReason.TimeLimit);
        CriterionResult h = half.Items.Find(x => x.Code == AssessmentCatalog.M1);
        Assert.AreEqual(0.5, h.Value);
        Assert.IsFalse(h.Passed);
        CollectionAssert.Contains(half.MandatoryFailed, AssessmentCatalog.M1);
        Assert.IsFalse(half.Passed);
        StringAssert.Contains("подать концы", h.Note);

        CriterionResult ab = AssessMooring(s, p, 3, ScenarioEndReason.Aborted).Items.Find(x => x.Code == AssessmentCatalog.M1);
        Assert.IsFalse(ab.Evaluated, "досрочно — не оценивается, как R4");

        var b2 = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoScenario.Fill(b2, DemoArea(), p);
        Assert.IsNull(AssessMooring(b2, p, 4, ScenarioEndReason.Arrived).Items.Find(x => x.Code == AssessmentCatalog.M1), "не швартовка — M1 нет");
        Assert.IsTrue(DebriefText.HasFailureText(AssessmentCatalog.M1));
    }

    [Test]
    public void Tasks_BriefingAndProgressText()
    {
        ScenarioDefinition s = Demo(out _, out _);
        string facts = ExerciseTasks.BriefingFacts(s);
        StringAssert.Contains("швартовка левым бортом", facts);
        StringAssert.Contains("Ошвартоваться", facts);
        StringAssert.DoesNotContain("Путевых точек", facts);
        StringAssert.Contains("ошвартован", ScenarioText.EndConditions(s));

        var tr = new MooringTaskTracker(s.mooring);
        tr.Update(Input(5, 6, 2, 1.0), 75, 0.02);
        string page = ExerciseTasks.Page(s, TaskPhase.Running, 80, null, tr);
        StringAssert.Contains("Заданий выполнено: 1 из 4", page);
        StringAssert.Contains("выполнено 01:15", page);
        StringAssert.DoesNotContain("Пройдено точек", page);

        var ev = new ExerciseEvent(ExerciseEventType.MooringStage) { Index = 4, Subject = "Ошвартоваться", Value = 0.31, Value2 = 0.04 };
        StringAssert.Contains("выполнено «Ошвартоваться»", ev.Describe());
        Assert.AreEqual("ошвартован", ScenarioText.EndReason(ScenarioEndReason.Moored));
        var log = new List<ExerciseEvent> { ev, new ExerciseEvent(ExerciseEventType.ExerciseEnded) { EndReason = ScenarioEndReason.Moored, Time = 300 } };
        ExerciseSummary sum = ExerciseSummary.From(s, log, null);
        Assert.AreEqual("заданий 1 из 4", sum.Progress);
    }

    /// <summary>
    /// Сквозная швартовка на модели: лагом в 3 м без хода, поданы носовой продольный и прижимной,
    /// кормовой прижимной и продольный. Концы выбираются «понемногу» (как командует швартовной
    /// команде судоводитель): каждые 0,2 с — «держать», если нос или корма идут к причалу быстрее
    /// 0,1 уз, и снова «выбирать», когда медленнее 0,05 уз; у кранцев — выбирать до упора лебёдки (0,15·MBL).
    /// Задание доходит до «ошвартован» без обрыва и удара о кромку. (Выборка всех концов сразу
    /// вдёргивает судно в кранцы на ≈ 0,5 уз — 19.13.)
    /// </summary>
    [Test]
    public void EndToEnd_LinesHauledToMoored()
    {
        ScenarioDefinition s = Demo(out WaterArea area, out ShipParameters p);
        BerthArea b = area.berths[0];
        WorldPosition pos = b.AlongsidePosition(0.5 * b.Length, 3.0, p.beam, out double hdg);
        var m = new ShipDynamicsModel(p) { MotionOverride = MotionModel.Manoeuvring3Dof };
        m.Teleport(pos.East, pos.North, hdg * System.Math.PI / 180.0, 0.0, 0.0);
        var sys = new MooringSystem(b, p) { FairleadHeightM = 4.5 };
        var tr = new MooringTaskTracker(s.mooring);
        foreach (int i in new[] { 0, 1, 4, 5 })
        {
            Assert.IsTrue(sys.Order(i, LineOrder.Give, m, 0, out string why), why);
            Assert.IsTrue(sys.Order(i, LineOrder.HaulIn, m, 0, out _));
        }
        const double dt = 0.02;
        double t = 0, maxImpactKn = 0;
        bool broken = false, quay = false, hauling = true;
        for (int k = 0; k < 30000 && !tr.Moored; k++)   // ≤ 10 мин
        {
            if (k % 10 == 0)
            {
                BerthReadout br = BerthReadout.Compute(b, p, m);
                double v = System.Math.Max(br.BowSpeedKn, br.SternSpeedKn);
                bool want = sys.InContact || (hauling ? v < 0.1 : v < 0.05);
                if (want != hauling)
                    foreach (int i in new[] { 0, 1, 4, 5 }) sys.Order(i, want ? LineOrder.HaulIn : LineOrder.Hold, m, t, out _);
                hauling = want;
            }
            sys.Apply(m, dt, t);
            m.Step(dt);
            t += dt;
            foreach (MooringEvent e in sys.Events)
            {
                broken |= e.Type == MooringEventType.LineBroken;
                quay |= e.Type == MooringEventType.QuayImpact;
                if (e.Type == MooringEventType.BerthingImpact) maxImpactKn = System.Math.Max(maxImpactKn, e.Value / ShipParameters.KnotsToMs);
            }
            sys.Events.Clear();
            var x = new MooringTaskInput
            {
                Readout = BerthReadout.Compute(b, p, m), FenderContact = sys.InContact,
                SpeedOverGroundKn = System.Math.Sqrt(m.VelocityEast * m.VelocityEast + m.VelocityNorth * m.VelocityNorth) / ShipParameters.KnotsToMs
            };
            MooringTasks.LinesFast(sys.Lines, out x.BowLineFast, out x.SternLineFast);
            tr.Update(x, t, dt);
        }
        Assert.IsTrue(tr.Moored, $"не ошвартован за {t:F0} с: выполнено {tr.DoneCount}");
        Assert.AreEqual(4, tr.DoneCount);
        Assert.IsFalse(broken, "обрыва нет");
        Assert.IsFalse(quay, "удара о кромку нет");
        Assert.Less(maxImpactKn, 0.5, "кранцы не на номинале (≈ 0,5 уз, табл. 19.5): натянутые концы и на «держать» продолжают тянуть судно");
        TestContext.Out.WriteLine($"Швартовка концами с 3 м: лагом {tr.DoneAt(MooringStage.Alongside):F1} с, у кранцев {tr.DoneAt(MooringStage.AtFenders):F1} с, " +
                                  $"ошвартован {tr.DoneAt(MooringStage.Moored):F1} с; навал {maxImpactKn:F2} уз");
    }
}
