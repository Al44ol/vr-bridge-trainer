// ============================================================
// TowingSceneTests.cs — Буксир и ветер в тренажёре (запрос 19.3б, Core)
//
// Команды меню → TugCommand (направления по месту буксира, «стоп»); автоматика: «стоп» держит место без силы
// на судно, смена борта толкания — обход оконечности (нос буксира не заходит в корпус), смена направления тяги —
// по дуге вокруг клюза; события TowingSystem → шина; столбцы буксира и ветра в mooring.csv (запись, чтение,
// прежние файлы без столбцов); оценка расхождения не видит буксир; ветер и буксир в сценарии, session.json, брифинге.
// ============================================================
using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

public class TowingSceneTests
{
    private const double Dt = 0.02;

    private static TowingSystem Make(TugStation station, out ShipDynamicsModel ship)
    {
        ShipParameters p = LowSpeedModelTests.Model45m();
        ship = new ShipDynamicsModel(p) { MotionOverride = MotionModel.Manoeuvring3Dof, SpeedFactor = 0.0 };
        ship.Teleport(0, 0, 0, 0, 0);
        var ts = new TowingSystem(p, TowingTrials.TugParameters());
        ts.SetStation(station);
        ts.SetGeometry(TugOrders.DefaultGeometry(station));
        return ts;
    }

    private static void Run(TowingSystem ts, ShipDynamicsModel ship, double seconds, ref double t, Action each = null)
    {
        int n = (int)Math.Round(seconds / Dt);
        for (int i = 0; i < n; i++)
        {
            ts.Apply(ship, Dt, t);
            ship.Step(Dt);
            t += Dt;
            each?.Invoke();
        }
    }

    // Насколько точка зашла внутрь контура корпуса судна, м (≤ 0 — снаружи)
    private static double Penetration(TowingSystem ts, ShipDynamicsModel ship, WorldPosition p)
    {
        TowingSystem.ShipAxes(ship, p, out double x, out double y);
        double half = MooringHull.HalfBeamAt(x, ts.ShipParams.length, ts.ShipParams.beam);
        return half <= 0.0 ? -1.0 : half - Math.Abs(y);
    }

    private static WorldPosition TugCenter(TowingSystem ts) => ts.Tug.Position;

    [Test]
    public void Orders_DirectionsByStation_StopCommand_Points()
    {
        CollectionAssert.DoesNotContain(TugOrders.Directions(TugStation.Stern, TugMode.Pull), 0.0, "у кормы «вперёд» — трос через палубу");
        CollectionAssert.DoesNotContain(TugOrders.Directions(TugStation.Bow, TugMode.Pull), 180.0, "у носа «назад» — трос через палубу");
        double[] mid = TugOrders.Directions(TugStation.Midship, TugMode.Pull);
        CollectionAssert.DoesNotContain(mid, 0.0);
        CollectionAssert.DoesNotContain(mid, 180.0);
        CollectionAssert.AreEqual(new[] { 270.0, 90.0 }, TugOrders.Directions(TugStation.Stern, TugMode.Push));
        Assert.AreEqual(180.0, TugOrders.DefaultDirection(TugStation.Stern));
        Assert.AreEqual(TugMode.Push, TugOrders.DefaultGeometry(TugStation.Midship));
        Assert.AreEqual(2, TugOrders.NearestIndex(TugOrders.Directions(TugStation.Stern, TugMode.Pull), 175.0));

        TugCommand stop = TugOrders.Command(TugMode.Stop, 270, 0.5);
        Assert.AreEqual(TugMode.Stop, stop.Mode);
        Assert.AreEqual(0.0, stop.Power);
        Assert.AreEqual(270.0, stop.DirectionDeg);
        Assert.AreEqual(0.25, TugOrders.Command(TugMode.Pull, 180, 0.25).Power);

        Assert.AreEqual(-0.45 * 45, TugOrders.TowPointX(TugStation.Stern, 45), 1e-9, "у кормы — как на стенде 19.3а");
        Assert.AreEqual(-0.3 * 45, TugOrders.PushPointX(TugStation.Stern, 45), 1e-9);
        Assert.AreEqual(0.45 * 45, TugOrders.TowPointX(TugStation.Bow, 45), 1e-9);
        Assert.AreEqual(0.0, TugOrders.PushPointX(TugStation.Midship, 45), 1e-9);
        TowingSystem ts = Make(TugStation.Bow, out _);
        Assert.AreEqual(0.45 * 45, ts.TowPointX, 1e-9);
        Assert.AreEqual(0.3 * 45, ts.PushPointX, 1e-9);
    }

    [Test]
    public void Stop_HoldsStation_WithoutForceOnShip()
    {
        // У кормы на тросе: «стоп» — буксир держится на линии «назад» в длине троса, судно не движется
        TowingSystem ts = Make(TugStation.Stern, out ShipDynamicsModel ship);
        TugCommand stop = TugOrders.Command(TugMode.Stop, 180, 0.25);
        ts.PlaceTug(ship, stop, 30);
        ts.Order(stop, 0);
        ts.MakeFast(30, 0);
        double t = 0, maxT = 0;
        Run(ts, ship, 120, ref t, () => maxT = Math.Max(maxT, ts.Line.Tension));
        double sog = Math.Sqrt(ship.VelocityEast * ship.VelocityEast + ship.VelocityNorth * ship.VelocityNorth) / ShipParameters.KnotsToMs;
        WorldPosition f = ts.TowPoint(ship), b = ts.Tug.Bow;
        double dist = Math.Sqrt((b.East - f.East) * (b.East - f.East) + (b.North - f.North) * (b.North - f.North));
        TestContext.Out.WriteLine($"Стоп у кормы, 120 с: ход судна {sog:F3} уз, Δψ {ship.Psi * 180 / Math.PI:F2}°, нос буксира в {dist:F2} м от клюза, T_max {maxT / 1000:F1} кН");
        Assert.Less(sog, 0.05);
        Assert.Less(Math.Abs(ship.Psi * 180 / Math.PI), 1.0);
        Assert.AreEqual(30.0, dist, 1.0);
        Assert.Less(maxT, 0.05 * ts.Line.Mbl, "«стоп» на тросе не тянет");

        // У миделя: «стоп» — у борта в StandoffM без упора
        TowingSystem tm = Make(TugStation.Midship, out ShipDynamicsModel ship2);
        TugCommand s2 = TugOrders.Command(TugMode.Stop, 270, 0.5);
        tm.PlaceTug(ship2, s2, 30);
        tm.Order(s2, 0);
        double t2 = 0;
        bool contact = false;
        Run(tm, ship2, 60, ref t2, () => contact |= tm.PushContact);
        double pen = Penetration(tm, ship2, tm.Tug.Bow);
        TestContext.Out.WriteLine($"Стоп у миделя, 60 с: зазор носа буксира до борта {-pen:F2} м, касание {contact}, ход судна лагом {ship2.V / ShipParameters.KnotsToMs:F3} уз");
        Assert.IsFalse(contact);
        Assert.AreEqual(TowingSystem.StandoffM, -pen, 0.5);
        Assert.Less(Math.Abs(ship2.V) / ShipParameters.KnotsToMs, 0.02);
        StringAssert.Contains("держится у борта", TugOrders.State(tm, false));
    }

    [Test]
    public void PushSideChange_GoesAroundBow_NotThroughHull()
    {
        // У миделя с ПБ (толкать «на ЛБ») → «толкать на ПБ»: буксир обходит нос и упирается в ЛБ
        TowingSystem ts = Make(TugStation.Midship, out ShipDynamicsModel ship);
        TugCommand stop = TugOrders.Command(TugMode.Stop, 270, 0.25);
        ts.PlaceTug(ship, stop, 30);
        TowingSystem.ShipAxes(ship, ts.Tug.Bow, out _, out double y0);
        Assert.Greater(y0, 0.0, "толкать «на ЛБ» — буксир у ПБ");
        ts.Order(TugCommand.Push(90, 0.25), 0);
        double t = 0, maxPen = -10, maxCenterPen = -10, contactAt = double.NaN;
        bool around = false;
        Run(ts, ship, 360, ref t, () =>
        {
            around |= ts.GoingAround;
            if (!ts.PushContact) maxPen = Math.Max(maxPen, Penetration(ts, ship, ts.Tug.Bow));
            maxCenterPen = Math.Max(maxCenterPen, Penetration(ts, ship, TugCenter(ts)));
            if (ts.PushContact && double.IsNaN(contactAt)) contactAt = t;
        });
        TowingSystem.ShipAxes(ship, ts.Tug.Bow, out double x1, out double y1);
        TestContext.Out.WriteLine($"Смена борта: обход {around}, касание ЛБ через {contactAt:F0} с, нос буксира x {x1:F1} y {y1:F1} м, " +
                                  $"наибольший заход носа без касания {maxPen:F2} м, центра {maxCenterPen:F2} м; судно лагом {ship.V / ShipParameters.KnotsToMs:F2} уз");
        Assert.IsTrue(around);
        Assert.IsFalse(double.IsNaN(contactAt), "буксир дошёл до ЛБ");
        Assert.Less(y1, 0.0);
        Assert.Less(maxPen, 0.3, "нос буксира не проходит сквозь корпус");
        Assert.Less(maxCenterPen, 0.0, "корпус буксира не проходит сквозь судно");
        Assert.Greater(ship.V, 0.0, "толкает на ПБ");
    }

    [Test]
    public void PullDirectionJump_SwingsAroundFairlead()
    {
        // У кормы трос на «на ЛБ» → сразу «на ПБ»: буксир идёт по дуге за кормой, а не сквозь корму
        TowingSystem ts = Make(TugStation.Stern, out ShipDynamicsModel ship);
        TugCommand c = TugCommand.Pull(270, 0.1);
        ts.PlaceTug(ship, c, 30);
        ts.Order(c, 0);
        ts.MakeFast(30, 0);
        ts.Order(TugCommand.Pull(90, 0.1), 0);
        double t = 0, maxPen = -10, minBehind = double.MaxValue;
        Run(ts, ship, 240, ref t, () =>
        {
            maxPen = Math.Max(maxPen, Math.Max(Penetration(ts, ship, ts.Tug.Bow), Penetration(ts, ship, TugCenter(ts))));
            TowingSystem.ShipAxes(ship, ts.Tug.Bow, out double xb, out double yb);
            if (Math.Abs(yb) < 5.0) minBehind = Math.Min(minBehind, -0.5 * ts.ShipParams.length - xb);
        });
        WorldPosition f = ts.TowPoint(ship), b = ts.Tug.Bow;
        double rel = (Math.Atan2(b.East - f.East, b.North - f.North) - ship.Psi) * 180 / Math.PI;
        rel = ((rel % 360) + 360) % 360;
        TestContext.Out.WriteLine($"Тяга с ЛБ на ПБ: буксир на {rel:F0}° от ДП, проход за кормой не ближе {minBehind:F1} м, наибольший заход в корпус {maxPen:F2} м, трос {(ts.Line.Fast ? "подан" : "—")}");
        Assert.AreEqual(90.0, rel, 15.0);
        Assert.Less(maxPen, 0.0);
        Assert.Greater(minBehind, 5.0, "проходит за кормой");
        Assert.IsTrue(ts.Line.Fast);
    }

    [Test]
    public void Events_ToBus_UnitsAndTypes()
    {
        ExerciseEvent o = TowEventBus.ToExerciseEvent(new TowEvent { Type = TowEventType.CommandChanged, Subject = TugCommand.Pull(270, 0.25).Describe(), Value = 0.25, Value2 = 270 });
        Assert.AreEqual(ExerciseEventType.TugOrder, o.Type);
        Assert.AreEqual((int)TugMode.Pull, o.Index);
        Assert.AreEqual(25.0, o.Value, 1e-9);
        Assert.AreEqual(270.0, o.Value2, 1e-9);
        Assert.AreEqual((int)TugMode.Stop, TowEventBus.ToExerciseEvent(new TowEvent { Type = TowEventType.CommandChanged, Subject = "стоп" }).Index);
        Assert.AreEqual((int)TugMode.Push, TowEventBus.ToExerciseEvent(new TowEvent { Type = TowEventType.CommandChanged, Subject = TugCommand.Push(90, 1).Describe() }).Index);
        ExerciseEvent ov = TowEventBus.ToExerciseEvent(new TowEvent { Type = TowEventType.LineOverload, Value = 250e3, Value2 = 0.51 });
        Assert.AreEqual(ExerciseEventType.TowLineOverload, ov.Type);
        Assert.AreEqual(250.0, ov.Value, 1e-9, "кН");
        Assert.AreEqual(ExerciseEventType.TowLineBroken, TowEventBus.ToExerciseEvent(new TowEvent { Type = TowEventType.LineBroken, Value = 500e3 }).Type);
        Assert.AreEqual(ExerciseEventType.TowLineMadeFast, TowEventBus.ToExerciseEvent(new TowEvent { Type = TowEventType.LineMadeFast, Value = 30 }).Type);
        Assert.AreEqual(ExerciseEventType.TowLineReleased, TowEventBus.ToExerciseEvent(new TowEvent { Type = TowEventType.LineReleased }).Type);
        Assert.AreEqual(ExerciseEventType.TowLineOverloadCleared, TowEventBus.ToExerciseEvent(new TowEvent { Type = TowEventType.LineOverloadCleared }).Type);
        ExerciseEvent pc = TowEventBus.ToExerciseEvent(new TowEvent { Type = TowEventType.PushContact, Value = ShipParameters.KnotsToMs * 0.5 });
        Assert.AreEqual(ExerciseEventType.TugPushContact, pc.Type);
        Assert.AreEqual(0.5, pc.Value, 1e-9, "уз");

        // Тексты журнала
        Assert.AreEqual("Ветер 20 уз, откуда 090° (меню)", new ExerciseEvent(ExerciseEventType.WindChanged) { Value = 20, Value2 = 90, Subject = "меню" }.Describe());
        Assert.AreEqual("Ветра нет (сценарий)", new ExerciseEvent(ExerciseEventType.WindChanged) { Value = 0, Value2 = 0, Subject = "сценарий" }.Describe());
        Assert.AreEqual("Буксир вызван к корме", new ExerciseEvent(ExerciseEventType.TugCalled) { Index = 0, Subject = TugOrders.StationTarget(TugStation.Stern) }.Describe());
        foreach (ExerciseEventType et in new[] { ExerciseEventType.TugDismissed, ExerciseEventType.TugOrder, ExerciseEventType.TowLineMadeFast, ExerciseEventType.TowLineReleased,
                     ExerciseEventType.TowLineOverload, ExerciseEventType.TowLineOverloadCleared, ExerciseEventType.TowLineBroken, ExerciseEventType.TugPushContact })
            Assert.IsFalse(new ExerciseEvent(et).Describe().StartsWith(et.ToString(), StringComparison.Ordinal), $"{et}: нет текста");
    }

    private static Func<string, string> Row(string header, string line)
    {
        string[] h = header.Split(';'), v = line.TrimEnd('\n').Split(';');
        var map = new Dictionary<string, string>();
        for (int i = 0; i < h.Length; i++) map[h[i]] = i < v.Length ? v[i] : "";
        return n => map.TryGetValue(n, out string s) ? s : null;
    }

    [Test]
    public void Sample_TowingColumns_WriteReadQuantize_OldFiles()
    {
        StringAssert.EndsWith(";tug_station;tug_mode;tug_dir_deg;tug_power_pct;towline_state;towline_length_m;towline_tension_kn;tug_push_kn;wind_kn;wind_from_deg", MooringRecording.Header);

        // Буксир у кормы тянет на ЛБ 25 % на тросе, ветер 20 уз с 090°
        TowingSystem ts = Make(TugStation.Stern, out ShipDynamicsModel ship);
        TugCommand c = TugCommand.Pull(270, 0.25);
        ts.PlaceTug(ship, c, 30);
        ts.Order(c, 0);
        ts.MakeFast(30, 0);
        double t = 0;
        Run(ts, ship, 20, ref t);
        MooringSample s = MooringSampler.Build(t, null, -1, ts.ShipParams, ship, null, ThrusterOrders.ZeroIndex, BridgeStation.Helm, null);
        Assert.IsFalse(s.Towing.Present, "без источника — буксира нет");
        s.Towing = TowingSample.Build(ts, Wind.Knots(20, 90));
        Assert.IsTrue(s.Towing.Present);
        Assert.AreEqual(TugMode.Pull, s.Towing.Mode);
        Assert.AreEqual(25.0, s.Towing.PowerPct, 1e-9);
        Assert.AreEqual(TowLineStatus.Fast, s.Towing.Line);
        Assert.Greater(s.Towing.LineTensionKn, 1.0);
        Assert.AreEqual(20.0, s.Towing.WindKn, 1e-9);

        var b = new StringBuilder();
        MooringRecording.Append(b, s);
        MooringSample back = MooringRecording.Parse(Row(MooringRecording.Header, b.ToString()));
        MooringSample q = MooringRecording.Quantize(s);
        TowingSample x = back.Towing, y = q.Towing;
        Assert.AreEqual(y.Present, x.Present); Assert.AreEqual(y.Station, x.Station); Assert.AreEqual(y.Mode, x.Mode);
        Assert.AreEqual(y.DirectionDeg, x.DirectionDeg); Assert.AreEqual(y.PowerPct, x.PowerPct); Assert.AreEqual(y.Line, x.Line);
        Assert.AreEqual(y.LineLengthM, x.LineLengthM); Assert.AreEqual(y.LineTensionKn, x.LineTensionKn); Assert.AreEqual(y.PushKn, x.PushKn);
        Assert.AreEqual(y.WindKn, x.WindKn); Assert.AreEqual(y.WindFromDeg, x.WindFromDeg);
        Assert.AreEqual(90.0, x.WindFromDeg);

        // Без буксира: пустые ячейки буксира, ветер 0; запись до 19.3б (столбцов нет) читается так же
        MooringSample none = MooringSampler.Build(0, null, -1, ts.ShipParams, ship, null, ThrusterOrders.ZeroIndex, BridgeStation.Helm, null);
        var nb = new StringBuilder();
        MooringRecording.Append(nb, none);
        StringAssert.EndsWith(";;;;;;;;;0.0;\n", nb.ToString());
        string oldHeader = MooringRecording.Header.Substring(0, MooringRecording.Header.IndexOf(";tug_station", StringComparison.Ordinal));
        string oldRow = nb.ToString().Substring(0, nb.ToString().Length - ";;;;;;;;;0.0;\n".Length);
        MooringSample old = MooringRecording.Parse(Row(oldHeader, oldRow));
        Assert.IsFalse(old.Towing.Present);
        Assert.AreEqual(0.0, old.Towing.WindKn);
        Assert.IsTrue(double.IsNaN(old.Towing.LineTensionKn));

        // Списки названий в session.json → recording
        var rec = new SessionRecording();
        CollectionAssert.AreEqual(new[] { "у кормы", "у носа", "у миделя" }, rec.tug_stations);
        CollectionAssert.AreEqual(new[] { "стоп", "тянуть", "толкать" }, rec.tug_modes);
        CollectionAssert.AreEqual(new[] { "не подан", "подан", "оборван" }, rec.towline_states);
    }

    [Test]
    public void Assessment_IgnoresTug_StateTextByMode()
    {
        var ctx = new AssessmentContext { Weather = WeatherPresetId.Calm, TimeOfDay = TimeOfDay.Day, DetectionRangeNm = 10 };
        var a = new ExerciseAssessor(ctx);
        for (int i = 0; i < 20; i++)
        {
            double t = i * 0.2;
            var own = new TrackSample { Time = t, East = 0, North = t * 1.5, HeadingDeg = 0, CogDeg = 0, SpeedKnots = 3, Leg = -1, XteM = double.NaN, XteLimitM = double.NaN };
            a.AddSample(own, new List<TargetSample>
            {
                new TargetSample { Time = t, Id = TugTarget.Id, East = 0, North = t * 1.5 - 30, HeadingDeg = 0, SpeedKnots = 3 },
                new TargetSample { Time = t, Id = "T1", East = 3000, North = 3000, HeadingDeg = 270, SpeedKnots = 10 }
            });
        }
        a.Finish();
        Assert.AreEqual(1, a.Targets.Count, "буксир — не цель расхождения");
        Assert.AreEqual("T1", a.Targets[0].Info.Id);

        // Состояние: в «экзамене» без кН, в «обучении» — с кН
        TowingSystem ts = Make(TugStation.Stern, out ShipDynamicsModel ship);
        TugCommand c = TugCommand.Pull(180, 0.25);
        ts.PlaceTug(ship, c, 30);
        ts.Order(c, 0);
        ts.MakeFast(30, 0);
        double tt = 0;
        Run(ts, ship, 30, ref tt);
        string exam = TugOrders.State(ts, false), hints = TugOrders.State(ts, true);
        TestContext.Out.WriteLine($"экзамен: {exam}\nобучение: {hints}");
        StringAssert.Contains("тянет назад, 25 %", exam);
        StringAssert.DoesNotContain("кН", exam);
        StringAssert.Contains("трос", exam);
        StringAssert.Contains("кН", hints);
    }

    [Test]
    public void Scenario_WindAndTug_ValidateMetaBriefing()
    {
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        s.windKnots = 20f; s.windFromDeg = 90f; s.tugAvailable = true;
        s.motionModel = ScenarioMotionModel.Nomoto;
        List<string> p = s.Validate();
        Assert.IsTrue(p.Exists(x => x.StartsWith("Ветер действует только", StringComparison.Ordinal)), string.Join("\n", p));
        Assert.IsTrue(p.Exists(x => x.StartsWith("Буксир работает только", StringComparison.Ordinal)));
        s.motionModel = ScenarioMotionModel.Manoeuvring3Dof;
        p = s.Validate();
        Assert.IsFalse(p.Exists(x => x.Contains("3 СС")), string.Join("\n", p));
        s.windKnots = -1f;
        Assert.IsTrue(s.Validate().Contains("Ветер отрицательный."));
        s.windKnots = 20f;

        SessionScenario m = SessionScenario.From(s);
        Assert.AreEqual(20.0, m.wind_kn);
        Assert.AreEqual(90.0, m.wind_from_deg);
        Assert.IsTrue(m.tug_available);
        SessionScenario back = JsonUtility.FromJson<SessionScenario>(JsonUtility.ToJson(m));
        Assert.AreEqual(20.0, back.wind_kn);
        Assert.IsTrue(back.tug_available);

        string facts = ExerciseTasks.BriefingFacts(s);
        StringAssert.Contains("Ветер: 20 уз, откуда 090°", facts);
        StringAssert.Contains("Буксир: по вызову", facts);
        var calm = ScriptableObject.CreateInstance<ScenarioDefinition>();
        StringAssert.DoesNotContain("Ветер", ExerciseTasks.BriefingFacts(calm), "у прежних сценариев строк нет");
        StringAssert.DoesNotContain("Буксир", ExerciseTasks.BriefingFacts(calm));
        Assert.AreEqual("нет", ScenarioText.Wind(0, 90));
        Assert.AreEqual("005°", ScenarioText.Bearing(365));
    }
}
