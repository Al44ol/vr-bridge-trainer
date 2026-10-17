// ============================================================
// PilotPowerTests.cs — Упражнение 19-03 с клиентом Mariner: лоцман командует упором (запрос 19.4б-3, Core)
//
// Причал №2 под Mariner — вне фарватера, отдельно от №1, дописывается в старую акваторию; сценарий 19-03 — версия;
// правило лоцмана (заданная скорость по зазору, упор = ветер + поправка, ступени, тексты); в прогоне — команды упором
// не чаще 15 с, без повторов, обстановка по убыванию зазора, автомат держит названную ступень.
// ============================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class PilotPowerTests
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
        DemoTugWorkScenario.Fill(s, area, Tug(), LowSpeedModelTests.Mariner(), crit);
        return s;
    }

    [Test]
    public void Berth2_ForMariner_OffFairway_AddedToOldArea()
    {
        BerthArea b1 = DemoWaterArea.Berth(), b2 = DemoWaterArea.Berth2();
        Assert.AreEqual("Причал №2", b2.name);
        Assert.AreEqual(1.6 * DemoWaterArea.Berth2ShipLength, b2.Length, 1.0, "кромка — 1,6·L Mariner");
        Assert.AreEqual(11f, b2.depthM, 1e-3, "глубина d + 3 м");
        Assert.Greater(b2.fenderOuterDiameterM, b1.fenderOuterDiameterM, "кранцы крупнее — под Mariner");
        Assert.AreEqual(DemoWaterArea.Berth2Gap, (b2.a - b1.b).Magnitude, 1e-6, "продолжение кромки №1 через 300 м");
        Assert.AreEqual(b1.BearingDeg, b2.BearingDeg, 1e-6, "та же линия");

        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        Assert.AreEqual(2, area.berths.Count);
        // Причал №2 — за концом фарватера (как №1): проекция на последнее колено — дальше его конца
        WorldPosition[] axis = DemoWaterArea.Axis();
        WorldPosition seg = axis[2] - axis[1];
        double segLen = seg.Magnitude;
        foreach (WorldPosition p in new[] { b2.a, b2.b, b2.Middle })
        {
            WorldPosition d = p - axis[2];
            Assert.Greater((d.East * seg.East + d.North * seg.North) / segLen, 500.0, "кромка №2 — за концом фарватера");
        }
        ScenarioDefinition s = Scenario();
        Assert.AreEqual(30f, area.DepthAt(s.initial.position), 1e-3, "буксир на старте — на глубине акватории (не мель, не суша)");

        // Старая акватория (до 19.4б-3, один причал): причал №2 дописывается один раз
        var old = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(old);
        old.berths.RemoveAt(1);
        Assert.IsTrue(DemoWaterArea.EnsureBerth2(old));
        Assert.AreEqual(2, old.berths.Count);
        Assert.AreEqual(b2.a.East, old.berths[1].a.East, 1e-9);
        Assert.IsFalse(DemoWaterArea.EnsureBerth2(old), "второй раз — без изменений");
    }

    [Test]
    public void Scenario_Version_ClientMariner()
    {
        ScenarioDefinition s = Scenario();
        Assert.AreEqual(DemoTugWorkScenario.Version, s.demoVersion);
        Assert.GreaterOrEqual(DemoTugWorkScenario.Version, 2, "19.4б-3 — версия 2");
        Assert.AreEqual(0, ScriptableObject.CreateInstance<ScenarioDefinition>().demoVersion, "у прежних ассетов — 0 (обновятся)");
        Assert.AreEqual("ShipParameters_Mariner", s.tugWorkClient.name);
        Assert.AreEqual(15f, s.windKnots);
        Assert.AreEqual(15f, s.tugWork.contactZoneM);
        Assert.AreEqual(1200f, s.timeLimitSeconds);
        CollectionAssert.IsEmpty(s.Validate());
        StringAssert.Contains("161 м", s.briefing);
        StringAssert.Contains("толкай самым малым / малым / средним / полным", s.briefing);
    }

    [Test]
    public void PilotRule_TargetSpeed_Power_Texts()
    {
        Assert.AreEqual(0.3, TugWorkSystem.TargetSpeedKn(40.0));
        Assert.AreEqual(0.2, TugWorkSystem.TargetSpeedKn(15.0));
        Assert.AreEqual(0.15, TugWorkSystem.TargetSpeedKn(5.0));
        Assert.AreEqual(0.08, TugWorkSystem.TargetSpeedKn(1.0));

        ScenarioDefinition s = Scenario();
        var sys = new TugWorkSystem(s.tugWork, s.tugWorkClient, s.tugWork.BerthIn(s.waterArea), TugSpec.ForOwnTug(s.shipParameters));
        var own = new ShipDynamicsModel(s.shipParameters) { MotionOverride = MotionModel.Manoeuvring3Dof };
        sys.Place(own);
        Assert.AreEqual(0.0, sys.WindOffBerth(), "без ветра — 0");
        Assert.AreEqual(0.0, sys.PowerFor(40.0, 0.3, false), 1e-12, "без ветра на заданной скорости — стоп");
        sys.Wind = Wind.Knots(15.0, s.windFromDeg);
        double fw = sys.WindOffBerth();
        Assert.Greater(fw, 50e3, "отжимной ветер 15 уз на Mariner — больше 50 кН от причала");
        Assert.Less(fw, 100e3);
        double balance = fw / sys.OwnSpec.BollardPull;
        Assert.AreEqual(balance + TugWorkSystem.PowerKv * 0.3, sys.PowerFor(40.0, 0.0, false), 1e-9, "стоит далеко — ветер + разгон");
        Assert.AreEqual(balance, sys.PowerFor(40.0, 0.3, false), 1e-9, "идёт с заданной скоростью — только ветер");
        Assert.AreEqual(0.0, sys.PowerFor(1.0, 0.6, false), 1e-12, "у кранцев быстро — стоп");
        Assert.AreEqual(balance + TugWorkSystem.HoldMargin, sys.PowerFor(0.5, 0.0, true), 1e-9, "держать — ветер с запасом");
        Assert.AreEqual(1.0, sys.PowerFor(40.0, -1.0, false), 1e-12, "не больше полного");

        Assert.AreEqual("Толкай самым малым", TugWorkText.Power(1, false));
        Assert.AreEqual("Толкай полным", TugWorkText.Power(4, false));
        Assert.AreEqual("Держи малым", TugWorkText.Power(2, true));
        Assert.AreEqual("Стоп толкать", TugWorkText.Power(0, false));
        Assert.AreEqual("До причала 20 м, идёт 0,25 уз", TugWorkText.Info(20.04, 0.25));
        Assert.AreEqual("До причала 5 м, стоит", TugWorkText.Info(5.0, 0.0));
        Assert.AreEqual("До причала 30 м, отходит 0,10 уз", TugWorkText.Info(30.0, -0.1));
    }

    [Test]
    public void Run_PowerOrders_RateLimited_InfoDescending_AutopilotFollows()
    {
        var sim = new TugWorkSimulation { Scenario = Scenario() };
        sim.Run();
        Assert.AreEqual(ScenarioEndReason.TugWorkDone, sim.EndReason);
        List<ExerciseEvent> power = sim.Events.FindAll(e => e.Type == ExerciseEventType.PilotOrder &&
            (e.Index == (int)PilotOrderKind.PushStep || e.Index == (int)PilotOrderKind.StopPush));
        Assert.GreaterOrEqual(power.Count, 3, "лоцман ведёт судно упором");
        Assert.AreEqual(sim.System.DoneAt(TugWorkStage.Approach), power[0].Time, 1e-9, "первая команда упором — при «толкайте»");
        for (int i = 1; i < power.Count; i++)
        {
            Assert.GreaterOrEqual(power[i].Time - power[i - 1].Time, TugWorkSystem.PowerIntervalS - 1e-6, "не чаще 15 с");
            Assert.AreNotEqual(power[i].Value, power[i - 1].Value, "без повторов той же ступени");
        }
        Assert.IsTrue(power.TrueForAll(e => e.Time <= sim.System.DoneAt(TugWorkStage.Hold) + 1e-9), "после «концы закреплены» — без команд упором");
        Assert.IsTrue(power.Exists(e => e.Subject.StartsWith("Держи", StringComparison.Ordinal)), "на этапе «держать» — «Держи …»");

        List<ExerciseEvent> info = sim.Events.FindAll(e => e.Type == ExerciseEventType.PilotOrder && e.Index == (int)PilotOrderKind.Info);
        Assert.AreEqual(sim.System.DoneAt(TugWorkStage.Approach), info[0].Time, 1e-9, "первый доклад дистанции — вместе с «толкайте»");
        Assert.Greater(info[0].Value, 30.0, "клиента отнесло дальше 30 м, пока шёл буксир");
        for (int i = 1; i < info.Count; i++)
            Assert.LessOrEqual(info[i].Time - info[i - 1].Time, TugWorkSystem.InfoPeriodS + 0.05, "доклад не реже раза в 30 с");
        foreach (double mark in new[] { 30.0, 20.0, 10.0 })
            Assert.IsTrue(info.Exists(e => e.Value <= mark && e.Value > mark - 3.0), $"доклад у рубежа {mark} м");
        Assert.Less(info[info.Count - 1].Value, 3.0, "последний доклад — у кранцев");
        Assert.IsTrue(info.TrueForAll(e => e.Subject.StartsWith("До причала", StringComparison.Ordinal)));
        Assert.AreEqual(info[info.Count - 1].Subject, sim.System.LastInfo, "последний доклад — строка монитора");

        // Автомат держит названную ступень: после «Держи …» заданный упор — та же ступень
        Assert.GreaterOrEqual(sim.System.PowerStep, 1);
        Assert.Greater(sim.System.HoldPushTime / sim.System.HoldTime, 0.9, "держал упор");
        Assert.AreEqual(0, sim.System.HullContacts.Count);
        double maxImpact = 0;
        foreach (ExerciseEvent e in sim.Events) if (e.Type == ExerciseEventType.BerthingImpact) maxImpact = Math.Max(maxImpact, e.Value);
        Assert.Greater(maxImpact, 0.0);
        Assert.LessOrEqual(maxImpact, 0.2, "по командам лоцмана навал ≤ 0,2 уз");
    }
}
