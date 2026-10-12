// ============================================================
// FastBoatTests.cs — Быстроходный катер и варианты сценариев (запрос 15)
//
// Параметры катера (ShipPresets): 30+ уз на «Полном вперёд», циркуляция и периоды качки
// в правдоподобных пределах; вариант сценария — копия (исходный ассет не меняется),
// начальная скорость пересчитана, перезапуск берёт исходник, «точка встречи» целей B3 —
// по новой скорости; прогон B2-01 на катере доходит до конца маршрута и оценивается.
// ============================================================
using System;
using NUnit.Framework;
using UnityEngine;

public class FastBoatTests
{
    private static ShipParameters FastBoat()
    {
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        p.name = ShipPresets.FastBoatAssetName;
        ShipPresets.FillFastBoat(p);
        return p;
    }

    private static ScenarioDefinition OnBoat(ScenarioDefinition s, ShipParameters boat) =>
        ScenarioVariants.WithShip(s, boat, null, ShipTypeSuffix, " (катер)");

    private const string ShipTypeSuffix = "-FB";

    [Test]
    public void FastBoat_Parameters_Plausible()
    {
        ShipParameters p = FastBoat();
        double full = ShipDynamicsModel.SteadySpeed(p, 1.0) / ShipParameters.KnotsToMs;
        Assert.GreaterOrEqual(full, 30.0, "«Полный вперёд» — 30+ уз");
        double dL = p.TheoreticalCircleDiameter(35f) / p.length;
        Assert.That(dL, Is.InRange(2.0, 5.0), "диаметр циркуляции 35°, L");
        Assert.That(p.rollPeriod, Is.InRange(2.5f, 6f), "период крена, с");
        Assert.That(p.heavePeriod, Is.InRange(1f, 4f), "период вертикальной качки, с");
        Assert.Greater(p.rudderRate, 5f, "перекладка руля быстрее, чем у судна (СОЛАС — 2,3 °/с)");
    }

    [Test]
    public void Variant_IsCopy_SpeedScaled_SourceUntouched()
    {
        ScenarioDefinition s = AssessmentTests.DemoB2();
        ShipParameters own = s.shipParameters, boat = FastBoat();
        string id = s.id;
        float speed = s.initial.speedKnots;

        ScenarioDefinition v = OnBoat(s, boat);
        Assert.AreNotSame(s, v);
        Assert.AreSame(own, s.shipParameters, "исходный сценарий не меняется");
        Assert.AreEqual(id, s.id);
        Assert.AreEqual(speed, s.initial.speedKnots);
        Assert.AreEqual(id + ShipTypeSuffix, v.id);
        Assert.AreSame(boat, v.shipParameters);
        Assert.AreEqual(s.route.Count, v.route.Count);
        Assert.AreSame(s.waterArea, v.waterArea);
        // «Полный вперёд» 15 уз модели 45 м → установившаяся скорость катера
        Assert.AreEqual(ShipDynamicsModel.SteadySpeed(boat, 1.0) / ShipParameters.KnotsToMs, v.initial.speedKnots, 0.01);

        Assert.AreSame(s, ScenarioVariants.SourceOf(v), "перезапуск — от исходника");
        Assert.AreSame(v, OnBoat(v, boat), "копия копии не создаётся (кэш)");
        Assert.AreSame(s, ScenarioVariants.WithShip(s, own, null, ShipTypeSuffix, ""), "то же судно — без копии");
        Assert.IsTrue(ScenarioVariants.IsVariant(v));
        Assert.IsFalse(ScenarioVariants.IsVariant(s));
    }

    [Test]
    public void Variant_TrafficMeetingPoint_UsesBoatSpeed()
    {
        ScenarioDefinition s = AssessmentTests.DemoB3();
        ScenarioDefinition v = OnBoat(s, FastBoat());
        Assert.Greater(v.initial.speedKnots, s.initial.speedKnots + 10f);
        int meeting = 0;
        foreach (TrafficShipDefinition d in v.traffic)
        {
            if (d.placement != TrafficPlacement.MeetingPoint) continue;
            meeting++;
            TrafficStart start = TrafficPlanner.Resolve(d, v.initial);
            Assert.IsTrue(string.IsNullOrEmpty(start.Problem), start.Problem);
            CpaResult r = CpaCalculator.FromCourses(v.initial.position, v.initial.headingDeg, v.initial.speedKnots,
                                                    start.Position, start.HeadingDeg, start.SpeedKnots);
            Assert.AreEqual(d.cpaNm * CpaCalculator.MetersPerNm, r.Cpa, 5.0, $"{d.name}: CPA");
            Assert.AreEqual(d.tcpaMinutes * 60.0, r.Tcpa, 2.0, $"{d.name}: TCPA");
        }
        Assert.Greater(meeting, 0, "в B3-01 есть цель с «точкой встречи»");
    }

    [Test]
    public void B2Run_OnBoat_ArrivesAndIsAssessed()
    {
        ScenarioDefinition v = OnBoat(AssessmentTests.DemoB2(), FastBoat());
        var sim = new ExerciseSimulation { Scenario = v };
        sim.Run();
        Assert.AreEqual(ScenarioEndReason.Arrived, sim.EndReason, $"конец: {sim.EndReason} за {sim.EndTime:F0} с");
        Assert.Less(sim.EndTime, 11 * 60.0, "маршрут B2-01 на катере — около 9 мин");
        Assert.IsNotNull(sim.LiveResult);
        Assert.IsFalse(double.IsNaN(sim.LiveResult.Score), "оценка посчитана");
        TestContext.WriteLine($"B2-01 на катере: {sim.EndTime / 60.0:F1} мин, оценка {sim.LiveResult.Score:F1}, " +
                              (sim.LiveResult.Passed ? "зачёт" : "незачёт"));
    }
}
