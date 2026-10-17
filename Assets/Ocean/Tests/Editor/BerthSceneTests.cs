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
        Assert.AreEqual(2, area.berths.Count, "19.4б-3: причал №2 — под Mariner (19-03)");
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

    // ─────────── Часть 2 (19.2б-2) ───────────

    private static (BerthArea b, ShipParameters p, ShipDynamicsModel m, MooringSystem sys) Alongside(double gap)
    {
        BerthArea b = DemoArea().berths[0];
        ShipParameters p = Model45m();
        WorldPosition s = b.AlongsidePosition(0.5 * b.Length, gap, p.beam, out double hdg);
        var m = new ShipDynamicsModel(p) { MotionOverride = MotionModel.Manoeuvring3Dof };
        m.Teleport(s.East, s.North, hdg * System.Math.PI / 180.0, 0.0, 0.0);
        return (b, p, m, new MooringSystem(b, p));
    }

    [Test]
    public void LineOrders_GiveWinchBittsReleaseAndRefusals()
    {
        var (b, p, m, sys) = Alongside(1.0);
        Assert.IsFalse(sys.Order(0, LineOrder.HaulIn, m, 0, out string r0), "не подан — выбирать нельзя");
        StringAssert.Contains("не подан", r0);
        Assert.IsTrue(sys.Order(0, LineOrder.Give, m, 0, out _), "лагом в 1 м — подаётся");
        Assert.AreEqual(MooringLineState.Fast, sys.Lines[0].State);
        Assert.AreEqual(-1, sys.Lines[0].Side, "на левый борт — к причалу");
        Assert.IsTrue(sys.Order(0, LineOrder.HaulIn, m, 0, out _));
        Assert.AreEqual(WinchCommand.HaulIn, sys.Lines[0].Winch);
        Assert.IsTrue(sys.Order(0, LineOrder.OnBitts, m, 0, out _));
        Assert.IsTrue(sys.Lines[0].OnBitts, "на кнехтах судна");
        Assert.AreEqual(WinchCommand.Hold, sys.Lines[0].Winch);
        Assert.IsTrue(sys.Order(0, LineOrder.PayOut, m, 0, out _));
        Assert.IsFalse(sys.Lines[0].OnBitts, "травить — снова на лебёдке");
        Assert.IsTrue(sys.Order(0, LineOrder.Release, m, 0, out _));
        Assert.AreEqual(MooringLineState.Aboard, sys.Lines[0].State);
        StringAssert.Contains("на борту", LineOrders.State(sys.Lines[0], true));

        var far = Alongside(60.0);
        Assert.IsFalse(far.sys.Order(2, LineOrder.Give, far.m, 0, out string r1), "в 60 м от кромки — не достать");
        StringAssert.Contains("больше", r1);
    }

    [Test]
    public void LineShape_SagFromSlackAndTension()
    {
        double s = MooringLineShape.Sag(10.0, 10.5, 0.0, 270e3);
        Assert.AreEqual(System.Math.Sqrt(3.0 * 10.0 * 0.5 / 8.0), s, 1e-12);
        Assert.AreEqual(10.5, MooringLineShape.ParabolaLength(10.0, s), 1e-9, "длина параболы = L₀");
        Assert.AreEqual(0.1, MooringLineShape.Sag(10.0, 10.0, 0.0, 270e3), 1e-12, "обтянут без силы — 1 % хорды");
        Assert.AreEqual(0.0, MooringLineShape.Sag(10.0, 9.9, 0.3 * 270e3, 270e3), 1e-12, "под нагрузкой — прямой");
    }

    [Test]
    public void BerthReadout_GapsSpeedsAngle()
    {
        var (b, p, m, _) = Alongside(2.0);
        BerthReadout r = BerthReadout.Compute(b, p, m);
        Assert.IsTrue(r.Valid);
        Assert.AreEqual(2.0, r.BowGapM, 1e-6, "плечо — в цилиндрической вставке");
        Assert.AreEqual(2.0, r.SternGapM, 1e-6);
        Assert.AreEqual(0.0, r.AngleDeg, 1e-6);
        Assert.Less(r.DistanceM, BerthReadout.ShowWithinM);

        // Лагом к причалу 0,1 м/с (на левый борт — V < 0)
        m.V = -0.1;
        r = BerthReadout.Compute(b, p, m);
        Assert.AreEqual(0.1 / ShipParameters.KnotsToMs, r.BowSpeedKn, 1e-6);
        Assert.AreEqual(r.BowSpeedKn, r.SternSpeedKn, 1e-9);

        // Нос к причалу на 5°: нос ближе кормы
        m.V = 0.0;
        m.Psi -= 5.0 * System.Math.PI / 180.0;
        r = BerthReadout.Compute(b, p, m);
        Assert.Less(r.BowGapM, r.SternGapM);
        Assert.AreEqual(-5.0, r.AngleDeg, 1e-6);
        StringAssert.Contains("ПРИЧАЛ", r.Format());
    }

    [Test]
    public void LineCommandAndStation_Describe()
    {
        var e = new ExerciseEvent(ExerciseEventType.LineCommand) { Index = 2, Value = (int)LineOrder.OnBitts, Value2 = 1, Subject = "носовой шпринг" };
        Assert.AreEqual("Концы: «на кнехты» — носовой шпринг", e.Describe());
        e.Value2 = 0;
        StringAssert.Contains("НЕ ВЫПОЛНЕНО", e.Describe());
        var s = new ExerciseEvent(ExerciseEventType.StationChanged) { Index = (int)BridgeStation.WingPort, Value = (int)BridgeStation.Helm };
        Assert.AreEqual("Переход: крыло ЛБ (был: у штурвала)", s.Describe());
    }
}
