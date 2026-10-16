// ============================================================
// TugWorkTests.cs — Упражнение на своём буксире 19-03: клиент, лоцман, контакт, задания (запрос 19.4б-1, Core)
//
// Сценарий 19-03 корректен; без буксира клиента сносит ветром, лоцман держит курс и место; касание кранцем —
// скорость по нормали, угол к борту, место; касание вне зоны — не «подход»; касание корпусом — флаг и скорость;
// расстояние носа до корпуса; автомат буксира проходит все четыре задания по командам лоцмана; тексты событий.
// ============================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class TugWorkTests
{
    private static ShipParameters Tug()
    {
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        p.name = "ShipParameters_Tug";
        ShipPresets.FillTug(p);
        return p;
    }

    private static ScenarioDefinition Scenario()
    {
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        var crit = ScriptableObject.CreateInstance<AssessmentCriteria>();
        crit.settings = DemoTugWorkScenario.Assessment();
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoTugWorkScenario.Fill(s, area, Tug(), LowSpeedModelTests.Model45m(), crit);
        return s;
    }

    private static TugWorkSystem System(ScenarioDefinition s, out ShipDynamicsModel own)
    {
        var sys = new TugWorkSystem(s.tugWork, s.tugWorkClient, s.tugWork.BerthIn(s.waterArea), TugSpec.For(s.shipParameters));
        own = new ShipDynamicsModel(s.shipParameters) { MotionOverride = MotionModel.Manoeuvring3Dof, SpeedFactor = 0.0 };
        sys.Place(own);
        return sys;
    }

    // Нос буксира в offset м от рабочего борта у точки x клиента, курс — поперёк борта ± angleDeg, ход u м/с
    private static void PutTug(TugWorkSystem sys, ShipDynamicsModel own, double x, double offset, double angleDeg, double u)
    {
        ShipDynamicsModel cm = sys.Client.Model;
        int side = sys.WorkSide;
        double nE = side * Math.Cos(cm.Psi), nN = -side * Math.Sin(cm.Psi);
        WorldPosition p = sys.ContactPoint(x);
        double psi = Math.Atan2(-nE, -nN) + angleDeg * Math.PI / 180.0;
        double bowE = p.East + nE * offset, bowN = p.North + nN * offset;
        double bx = sys.OwnSpec.BowX;
        own.Teleport(bowE - bx * Math.Sin(psi), bowN - bx * Math.Cos(psi), psi, u, 0.0);
    }

    [Test]
    public void Scenario_19_03_Valid()
    {
        ScenarioDefinition s = Scenario();
        CollectionAssert.IsEmpty(s.Validate());
        Assert.IsTrue(s.IsTugWork);
        Assert.IsFalse(s.IsMooring);
        Assert.AreEqual("19-03", s.id);
        Assert.AreEqual("ShipParameters_Tug", s.shipParameters.name, "своё судно — буксир");
        Assert.AreEqual(ScenarioMotionModel.Manoeuvring3Dof, s.motionModel);
        Assert.IsTrue(s.Ends(ScenarioEndConditions.TugWorkDone));
        Assert.IsFalse(s.Ends(ScenarioEndConditions.Arrival));
        Assert.AreEqual(20f, s.windKnots);
        Assert.AreEqual(60f, s.windRandomSectorDeg);
        Assert.IsFalse(s.tugAvailable, "буксир не вызывается — он свой");

        TugWorkSystem sys = System(s, out ShipDynamicsModel own);
        Assert.AreEqual(s.initial.position.East, own.East, 0.01, "начальные условия сценария = TugWorkSystem.Place");
        Assert.AreEqual(s.initial.position.North, own.North, 0.01);
        Assert.AreEqual(150.0, sys.BowDistance(own), 0.1, "нос буксира в 150 м от борта");
        Assert.AreEqual(30.0, sys.Client.GapM, 0.5, "клиент в 30 м от кромки");
        Assert.AreEqual(-1, sys.Client.SideToBerth, "причал у клиента по ЛБ");
        Assert.AreEqual(1, sys.WorkSide, "буксир — у ПБ");
        s.tugWork.enabled = false;
        Assert.IsTrue(s.Validate().Exists(p => p.Contains("задание буксира")), "условие без задания — замечание");
    }

    [Test]
    public void Client_WithoutTug_DriftsOff_PilotHoldsCourseAndPlace()
    {
        ScenarioDefinition s = Scenario();
        TugWorkSystem sys = System(s, out ShipDynamicsModel own);
        sys.Wind = Wind.Knots(s.windKnots, s.windFromDeg);
        double along0 = sys.Client.HoldAlongM;
        for (double t = 0; t < 300.0; t += 0.02)
        {
            sys.Apply(own, 0.02, t);
            own.Step(0.02);
        }
        ClientShip c = sys.Client;
        Assert.Greater(c.GapM, 80.0, "отжимной ветер сносит клиента от причала");
        Assert.Less(Math.Abs(Math.IEEERemainder(c.HeadingDeg - c.CourseDeg, 360.0)), 1.0, "лоцман держит курс по кромке");
        c.Berth.ToLocal(c.Position, out double along, out _);
        Assert.Less(Math.Abs(along - along0), 5.0, "и место вдоль причала");
        Assert.AreEqual(0, sys.DoneCount);
        Assert.AreEqual(PilotOrderKind.Approach, sys.Order);
    }

    [Test]
    public void Contact_ReportsSpeedAngleAndPlace()
    {
        ScenarioDefinition s = Scenario();
        TugWorkSystem sys = System(s, out ShipDynamicsModel own);
        PutTug(sys, own, 4.0, 0.5, 20.0, 0.5);
        var events = new List<ExerciseEvent>();
        for (int i = 0; i < 200 && sys.Contacts.Count == 0; i++)
        {
            sys.Apply(own, 0.02, i * 0.02);
            own.Step(0.02);
            events.AddRange(sys.Events);
            sys.Events.Clear();
        }
        Assert.AreEqual(1, sys.Contacts.Count);
        (double t, double kn, double angle, double x, bool inZone) = sys.Contacts[0];
        Assert.AreEqual(0.5 * Math.Cos(20.0 * Math.PI / 180.0) / ShipParameters.KnotsToMs, kn, 0.1, "скорость по нормали");
        Assert.AreEqual(20.0, angle, 1.0, "отклонение от перпендикуляра");
        Assert.AreEqual(4.0, x, 1.0, "место от миделя");
        Assert.IsTrue(inZone);
        Assert.IsTrue(sys.Done(TugWorkStage.Approach));
        ExerciseEvent ce = events.Find(e => e.Type == ExerciseEventType.TugWorkContact);
        Assert.AreEqual("правый борт", ce.Subject);
        Assert.AreEqual(4, ce.Index);
        Assert.IsTrue(events.Exists(e => e.Type == ExerciseEventType.PilotOrder && e.Index == (int)PilotOrderKind.Push), "после подхода — «толкайте»");
    }

    [Test]
    public void Contact_OutsideZone_IsNotApproach()
    {
        ScenarioDefinition s = Scenario();
        TugWorkSystem sys = System(s, out ShipDynamicsModel own);
        PutTug(sys, own, 15.0, 0.3, 0.0, 0.2);
        for (int i = 0; i < 200 && sys.Contacts.Count == 0; i++)
        {
            sys.Apply(own, 0.02, i * 0.02);
            own.Step(0.02);
        }
        Assert.AreEqual(1, sys.Contacts.Count);
        Assert.IsFalse(sys.Contacts[0].inZone, "15 м от миделя — вне зоны ± 10 м");
        Assert.IsFalse(sys.Done(TugWorkStage.Approach));
        Assert.AreEqual(PilotOrderKind.Approach, sys.Order);
    }

    [Test]
    public void HullContact_FlagSpeedAndBowDistance()
    {
        ScenarioDefinition s = Scenario();
        TugWorkSystem sys = System(s, out ShipDynamicsModel own);
        // Буксир лагом к борту клиента (параллельно), борт в 0,3 м; толчок к борту
        ShipDynamicsModel cm = sys.Client.Model;
        WorldPosition p = MooringHull.ToWorld(cm.East, cm.North, cm.Psi, 0.0, sys.WorkSide * (0.5 * cm.Parameters.beam + 0.5 * own.Parameters.beam + 0.3));
        own.Teleport(p.East, p.North, cm.Psi, 0.0, 0.0);
        double nE = sys.WorkSide * Math.Cos(cm.Psi), nN = -sys.WorkSide * Math.Sin(cm.Psi);
        int started = 0;
        double maxKn = 0;
        for (int i = 0; i < 400; i++)
        {
            if (i < 100) own.AddForce(0.0, -sys.WorkSide * 1e5, 0.0);   // толчок к борту клиента 2 с
            sys.Apply(own, 0.02, i * 0.02);
            own.Step(0.02);
            if (sys.Contact.HullContactStarted) started++;
            foreach (ExerciseEvent e in sys.Events) if (e.Type == ExerciseEventType.TugWorkHullContact) maxKn = Math.Max(maxKn, e.Value);
            sys.Events.Clear();
        }
        Assert.GreaterOrEqual(started, 1, "касание корпусом отмечено");
        Assert.AreEqual(started, sys.HullContacts.Count);
        Assert.Greater(maxKn, 0.0, "скорость сближения");
        Assert.AreEqual(0, sys.Contacts.Count, "лагом — не кранцем");

        // Расстояние носа до корпуса: напротив миделя в 5 м — 5; в 3 м за кормой сбоку (транец широкий) — 3
        PutTug(sys, own, 0.0, 5.0, 0.0, 0.0);
        Assert.AreEqual(5.0, sys.BowDistance(own), 0.05);
        PutTug(sys, own, -0.5 * cm.Parameters.length - 3.0, 4.0, 0.0, 0.0);
        Assert.AreEqual(3.0, sys.BowDistance(own), 0.05, "за кормой у транца — по длине");
    }

    [Test]
    public void Autopilot_CompletesAllStagesByPilotOrders()
    {
        var sim = new TugWorkSimulation { Scenario = Scenario() };
        sim.Run();
        Assert.AreEqual(ScenarioEndReason.TugWorkDone, sim.EndReason);
        Assert.Less(sim.EndTime, sim.Scenario.timeLimitSeconds);
        TugWorkSystem sys = sim.System;
        for (int i = 1; i <= TugWorkSystem.StageCount; i++)
        {
            Assert.IsTrue(sys.Done((TugWorkStage)i), ((TugWorkStage)i).ToString());
            if (i > 1) Assert.GreaterOrEqual(sys.DoneAt((TugWorkStage)i), sys.DoneAt((TugWorkStage)(i - 1)), "по порядку");
        }
        // Команды лоцмана — по одной, по порядку, после заданий
        List<ExerciseEvent> orders = sim.Events.FindAll(e => e.Type == ExerciseEventType.PilotOrder);
        Assert.AreEqual(4, orders.Count);
        for (int i = 0; i < 4; i++) Assert.AreEqual(i + 1, orders[i].Index);
        Assert.AreEqual(0.0, orders[0].Time, 0.1);
        Assert.AreEqual(sys.DoneAt(TugWorkStage.Approach), orders[1].Time, 1e-9);
        Assert.AreEqual(sys.DoneAt(TugWorkStage.Hold), orders[3].Time, 1e-9);
        // Качество работы автомата — основа «правильного» прогона 19.4в
        Assert.AreEqual(1, sys.Contacts.Count, "одно касание — без отскока");
        Assert.LessOrEqual(sys.Contacts[0].kn, 0.3, "касание ≤ 0,3 уз");
        Assert.LessOrEqual(sys.Contacts[0].angleDeg, 5.0, "носом поперёк борта");
        Assert.LessOrEqual(Math.Abs(sys.Contacts[0].x), 2.0, "у миделя");
        Assert.AreEqual(0, sys.HullContacts.Count, "без касаний корпусом");
        Assert.Greater(sys.HoldPushTime / sys.HoldTime, 0.9, "держал упор");
        double maxImpact = 0;
        foreach (ExerciseEvent e in sim.Events) if (e.Type == ExerciseEventType.BerthingImpact) maxImpact = Math.Max(maxImpact, e.Value);
        Assert.Greater(maxImpact, 0.0, "клиент лёг на кранцы");
        Assert.LessOrEqual(maxImpact, 0.3, "навал клиента ≤ 0,3 уз");
        Assert.IsTrue(sys.Client.Secured);
        Assert.Greater(sim.MaxClientGapM, 40.0, "пока буксир шёл, клиента сносило");
    }

    [Test]
    public void EventTexts_AndEndReason()
    {
        Assert.AreEqual("Лоцман: «Толкайте к причалу»",
            new ExerciseEvent(ExerciseEventType.PilotOrder) { Index = 2, Subject = TugWorkText.Order(PilotOrderKind.Push) }.Describe());
        Assert.AreEqual("Буксир, к правому борту на миделе, упирайтесь", TugWorkText.Order(PilotOrderKind.Approach, 1));
        Assert.AreEqual("Буксир, к левому борту на миделе, упирайтесь", TugWorkText.Order(PilotOrderKind.Approach, -1));
        Assert.AreEqual("Буксир: выполнено «Отойти от борта»",
            new ExerciseEvent(ExerciseEventType.TugWorkStage) { Index = 4, Subject = TugWorkText.Stage(TugWorkStage.Clear) }.Describe());
        Assert.AreEqual("Буксир упёрся в правый борт: 0,25 уз, 3° от перпендикуляра, −2 м от миделя",
            new ExerciseEvent(ExerciseEventType.TugWorkContact) { Value = 0.25, Value2 = 3, Index = -2, Subject = "правый борт" }.Describe());
        Assert.AreEqual("Касание корпусом буксира о судно: 0,15 уз",
            new ExerciseEvent(ExerciseEventType.TugWorkHullContact) { Value = 0.15 }.Describe());
        Assert.AreEqual("задание буксира выполнено", ScenarioText.EndReason(ScenarioEndReason.TugWorkDone));
        var d = new TugWorkDefinition();
        foreach (TugWorkStage st in new[] { TugWorkStage.Approach, TugWorkStage.Push, TugWorkStage.Hold, TugWorkStage.Clear })
            Assert.IsNotEmpty(TugWorkText.Condition(d, st));
    }
}
