// ============================================================
// BerthSceneTests.cs — Причал в учебной акватории и команды (запрос 19.2б, часть 1)
//
// Демо-причал: курс кромки 060°, кранцы — по PIANC под Model45m (как стенд), шаг 5 м;
// причал не суша — у кромки и на подходе детектор посадки молчит; судно, прошедшее
// финиш B2 прямо, проходит далеко от причала; положение «лагом» — левый борт, зазор;
// события MooringSystem → шина (единицы); положения подруливающего и ступени руля меню.
// ============================================================
using NUnit.Framework;
using UnityEngine;

public class BerthSceneTests
{
    private static ShipParameters Model45m() => LowSpeedModelTests.Model45m();

    private static WaterArea DemoArea()
    {
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        return area;
    }

    [Test]
    public void ForShip_DimsOverloadSameAsParameters()
    {
        ShipParameters p = Model45m();
        BerthArea a = BerthArea.ForShip(p), b = BerthArea.ForShip(p.length, p.beam, p.draft, p.blockCoefficient);
        Assert.AreEqual(a.fenderOuterDiameterM, b.fenderOuterDiameterM);
        Assert.AreEqual(a.fenderRatedReactionKn, b.fenderRatedReactionKn);
        Assert.AreEqual(a.fenderSpacingM, b.fenderSpacingM);
        Assert.AreEqual(a.bollardSpacingM, b.bollardSpacingM);
        Assert.AreEqual(a.b.East, b.b.East);
        Assert.AreEqual(BerthArea.ShipMass(p), BerthArea.ShipMass(p.length, p.beam, p.draft, p.blockCoefficient));
    }

    [Test]
    public void DemoBerth_GeometryAndFenders()
    {
        WaterArea area = DemoArea();
        Assert.AreEqual(1, area.berths.Count);
        BerthArea b = area.berths[0];
        Assert.AreEqual(60.0, b.BearingDeg, 1e-6, "кромка — вдоль второго колена фарватера");
        Assert.AreEqual(DemoWaterArea.BerthLength, b.Length, 1e-6);
        BerthArea stand = BerthArea.ForShip(Model45m());
        Assert.AreEqual(stand.fenderOuterDiameterM, b.fenderOuterDiameterM, 1e-4, "кранец — как у стенда (PIANC под Model45m)");
        Assert.AreEqual(stand.fenderRatedReactionKn, b.fenderRatedReactionKn, 1e-3);
        var f = b.FenderPositions();
        Assert.AreEqual(16, f.Count, "80 м, отступы 4 м, шаг ≤ 5 м: 72/5 → 15 промежутков по 4,8 м");
        for (int i = 1; i < f.Count; i++) Assert.LessOrEqual(f[i] - f[i - 1], DemoWaterArea.BerthFenderSpacing + 1e-6);
        Assert.AreEqual(0, area.NearestBerth(b.Middle, out double d0));
        Assert.AreEqual(0.0, d0, 1e-6);
    }

    [Test]
    public void DemoBerth_NotLand_NoGroundingWarningAtFace()
    {
        WaterArea area = DemoArea();
        BerthArea b = area.berths[0];
        ShipParameters p = Model45m();
        // Пирс (кромка и тыл) — не суша и не мель
        foreach (double t in new[] { 0.0, 0.5 * b.Length, b.Length })
            foreach (double n in new[] { 0.0, -b.structureWidthM })
                Assert.AreEqual(area.defaultDepth, area.DepthAt(b.ToWorld(t, n)), $"t {t}, n {n}");

        var det = new GroundingDetector { Area = area, Draft = p.draft };
        // Лагом у кранцев (зазор 0) и на подходе: 300 м до середины, 60 м от кромки
        for (int k = 0; k <= 30; k++)
        {
            double t = 0.5 * b.Length - 300.0 + 10.0 * k;
            WorldPosition s = b.AlongsidePosition(t, k == 30 ? 0.0 : 60.0 - 2.0 * k, p.beam, out double hdg);
            det.Update(HullFootprint.FromShip(s.East, s.North, hdg * System.Math.PI / 180.0, 0, 0, p.length, p.beam));
            Assert.AreEqual(GroundingState.Clear, det.State, $"шаг {k}: у причала нет «ОПАСНО: МЕЛЬ» и «НА МЕЛИ»");
        }
        Assert.Greater(det.Nearest.Distance, 3.0 * WaterArea.CableMeters, "до суши и мелей — больше 3 кбт");
    }

    [Test]
    public void DemoBerth_ClearOfStraightRunPastFinish()
    {
        WaterArea area = DemoArea();
        BerthArea b = area.berths[0];
        WorldPosition[] axis = DemoWaterArea.Axis();
        WorldPosition end = axis[axis.Length - 1], d = end - axis[axis.Length - 2];
        double len = d.Magnitude;
        for (double s = 0; s <= 1500; s += 25)
        {
            var p = new WorldPosition(end.East + s * d.East / len, end.North + s * d.North / len);
            Assert.Greater(b.DistanceTo(p), 100.0, $"{s} м за концом фарватера");
        }
    }

    [Test]
    public void Alongside_PortSideGapAndAngle()
    {
        WaterArea area = DemoArea();
        BerthArea b = area.berths[0];
        ShipParameters p = Model45m();
        WorldPosition s = b.AlongsidePosition(0.5 * b.Length, 2.0, p.beam, out double hdg);
        var m = new ShipDynamicsModel(p);
        m.Teleport(s.East, s.North, hdg * System.Math.PI / 180.0, 0.0, 0.0);
        var sys = new MooringSystem(b, p);
        Assert.AreEqual(-1, sys.SideToBerth(m), "причал — по левому борту");
        Assert.AreEqual(2.0, sys.DistanceToFace(m, 0.0), 1e-6, "зазор борта у миделя");
        Assert.AreEqual(0.0, sys.AngleToBerthDeg(m), 1e-6);
    }

    [Test]
    public void EventBus_UnitsThrusterAndRudderOrders()
    {
        var impact = new MooringEvent { Type = MooringEventType.BerthingImpact, Value = 0.2 * ShipParameters.KnotsToMs, Value2 = 6500.0, Value3 = 0.454, Index = 2 };
        ExerciseEvent e = MooringEventBus.ToExerciseEvent(impact);
        Assert.AreEqual(ExerciseEventType.BerthingImpact, e.Type);
        Assert.AreEqual(0.2, e.Value, 1e-9, "уз");
        Assert.AreEqual(6.5, e.Value2, 1e-9, "кДж");
        Assert.AreEqual(45, e.Index, "обжатие, %");
        Assert.AreEqual("2", e.Subject, "кранцев");
        StringAssert.Contains("0,20 уз", e.Describe());

        var over = new MooringEvent { Type = MooringEventType.LineOverload, Index = 4, Value = 150e3, Value2 = 0.55, Subject = "кормовой прижимной" };
        ExerciseEvent o = MooringEventBus.ToExerciseEvent(over);
        Assert.AreEqual(150.0, o.Value, 1e-9, "кН");
        Assert.AreEqual(0.55, o.Value2, 1e-9);
        StringAssert.Contains("кормовой прижимной", o.Describe());

        Assert.AreEqual(-1.0, ThrusterOrders.Command(0));
        Assert.AreEqual(0.0, ThrusterOrders.Command(ThrusterOrders.ZeroIndex));
        Assert.AreEqual(1.0, ThrusterOrders.Command(4));
        for (int i = 0; i < ThrusterOrders.Count; i++) Assert.AreEqual(i, ThrusterOrders.IndexOf(ThrusterOrders.Command(i)));
        Assert.AreEqual("ПБ 100 %", ThrusterOrders.Title(4));
        Assert.AreEqual("ЛБ 50 %", ThrusterOrders.Title(1));
        Assert.AreEqual("0", ThrusterOrders.Title(2));

        Assert.AreEqual(10.0, ControlOrders.RudderSteps[ControlOrders.NearestRudderStep(12.0)]);
        Assert.AreEqual(0.0, ControlOrders.RudderSteps[ControlOrders.NearestRudderStep(-1.0)]);
        Assert.AreEqual("лево 10°", ControlOrders.RudderTitle(-10.0));
        Assert.AreEqual("прямо", ControlOrders.RudderTitle(0.0));
        Assert.AreEqual("Самый малый вперёд", ControlOrders.ValueText(ControlOrders.Telegraph, 0.2));
    }
}
