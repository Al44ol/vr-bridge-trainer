// ============================================================
// TriggerTests.cs — Триггеры сценария «если — то» (запрос 21.6а, Core)
//
// TriggerRunner: условия (время, круг, точка маршрута, дистанция до цели, этап задания, другой триггер),
// задержка, однократность, цепочки в одном шаге, ручной запуск; проверка триггеров в Validate (ссылки, цикл);
// приказ цели TrafficVessel.Order (стоящая цель трогается, курс снимает маршрут); сценарий 21-01 и тексты событий.
// ============================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class TriggerTests
{
    private const double Dt = 0.02;

    private static ScenarioTrigger Tr(string id, TriggerCondition c, params TriggerAction[] actions)
    {
        var t = new ScenarioTrigger { id = id, condition = c };
        t.actions.AddRange(actions.Length > 0 ? actions : new[] { new TriggerAction { kind = TriggerActionKind.Message, text = id } });
        return t;
    }

    // Прогон по времени от 0 до tEnd: моменты срабатывания по id
    private static Dictionary<string, double> Run(TriggerRunner r, double tEnd, Func<double, WorldPosition> own = null,
                                                  Func<double, string, double> range = null, Action<double> before = null)
    {
        var at = new Dictionary<string, double>();
        for (int n = 0; n * Dt <= tEnd + 1e-9; n++)
        {
            double t = n * Dt;
            before?.Invoke(t);
            double tt = t;
            foreach (int i in r.Step(t, own != null ? own(t) : default, id => range != null ? range(tt, id) : double.NaN))
            {
                Assert.IsFalse(at.ContainsKey(r.Triggers[i].id), $"{r.Triggers[i].id}: сработал второй раз");
                at[r.Triggers[i].id] = t;
            }
        }
        return at;
    }

    [Test]
    public void Time_WithDelay_FiresOnce()
    {
        var r = new TriggerRunner();
        ScenarioTrigger a = Tr("Тр1", TriggerCondition.Time);
        a.timeSeconds = 60f;
        ScenarioTrigger b = Tr("Тр2", TriggerCondition.Time);
        b.timeSeconds = 60f;
        b.delaySeconds = 10f;
        r.Reset(new List<ScenarioTrigger> { a, b });
        Dictionary<string, double> at = Run(r, 120);
        Assert.AreEqual(60.0, at["Тр1"], 0.021);
        Assert.AreEqual(70.0, at["Тр2"], 0.021, "задержка — от момента условия");
        Assert.AreEqual(2, r.FiredCount);
        Assert.AreEqual(TriggerStatus.Fired, r.Status(1));

        r.Reset(new List<ScenarioTrigger> { a, b });   // «Начать» заново — снова ждут
        Assert.AreEqual(0, r.FiredCount);
        Assert.AreEqual(TriggerStatus.Waiting, r.Status(0));
        r.Step(65, default, null);
        Assert.AreEqual(TriggerStatus.Delay, r.Status(1));
        StringAssert.StartsWith("через 00:05", r.StateText(1, 65));
    }

    [Test]
    public void Circle_Waypoint_Range_Stage()
    {
        var r = new TriggerRunner();
        ScenarioTrigger circle = Tr("Тр1", TriggerCondition.EnterCircle);
        circle.center = new WorldPosition(0, 1000);
        circle.radiusM = 200f;
        ScenarioTrigger wp = Tr("Тр2", TriggerCondition.Waypoint);
        wp.waypoint = "Поворот";
        ScenarioTrigger range = Tr("Тр3", TriggerCondition.TargetRange);
        range.target = "T1";
        range.rangeNm = 1f;
        ScenarioTrigger stage = Tr("Тр4", TriggerCondition.TaskStage);
        stage.stage = 2;
        r.Reset(new List<ScenarioTrigger> { circle, wp, range, stage });

        // Своё судно идёт на север 10 м/с от N = 0; цель стоит в N = 3000 (до неё < 1852 м — с N > 1148)
        Dictionary<string, double> at = Run(r, 200, t => new WorldPosition(0, 10.0 * t),
            (t, id) => id == "T1" ? Math.Abs(3000.0 - 10.0 * t) : double.NaN,
            t =>
            {
                if (Math.Abs(t - 150.0) < Dt / 2) r.WaypointPassed("Поворот");
                if (Math.Abs(t - 170.0) < Dt / 2) r.StageReached(1);
                if (Math.Abs(t - 180.0) < Dt / 2) r.StageReached(2);
            });
        Assert.AreEqual(80.0, at["Тр1"], 0.021, "вход в круг R 200 м у N 1000");
        Assert.AreEqual(150.0, at["Тр2"], 0.021, "точка пройдена");
        Assert.AreEqual(114.8, at["Тр3"], 0.021, "дистанция < 1 мили");
        Assert.AreEqual(180.0, at["Тр4"], 0.021, "этап 2, не этап 1");
    }

    [Test]
    public void Chain_SameStep_DelayFromPrevious_AndManual()
    {
        var r = new TriggerRunner();
        ScenarioTrigger a = Tr("Тр1", TriggerCondition.Time);
        a.timeSeconds = 10f;
        ScenarioTrigger b = Tr("Тр2", TriggerCondition.AfterTrigger);
        b.afterTrigger = "Тр1";
        ScenarioTrigger c = Tr("Тр3", TriggerCondition.AfterTrigger);
        c.afterTrigger = "Тр2";
        c.delaySeconds = 5f;
        // Порядок в списке — обратный: цепочка всё равно срабатывает в одном шаге
        r.Reset(new List<ScenarioTrigger> { c, b, a });
        Dictionary<string, double> at = Run(r, 30);
        Assert.AreEqual(10.0, at["Тр1"], 0.021);
        Assert.AreEqual(at["Тр1"], at["Тр2"], 1e-9, "в том же шаге");
        Assert.AreEqual(15.0, at["Тр3"], 0.021, "задержка — от срабатывания предыдущего");

        // Вручную: сразу, без задержки и условия; повторно — нет; цепочка идёт от ручного
        r.Reset(new List<ScenarioTrigger> { a, b, c });
        Assert.IsTrue(r.Fire(0, 3.0));
        Assert.IsFalse(r.Fire(0, 4.0));
        Assert.IsTrue(r.Manual(0));
        Assert.AreEqual(3.0, r.FiredAt(0));
        StringAssert.Contains("вручную", r.StateText(0, 5.0));
        List<int> fired = r.Step(3.02, default, null);
        CollectionAssert.AreEqual(new[] { 1 }, fired, "Тр2 — после ручного Тр1");
        Assert.AreEqual(TriggerStatus.Delay, r.Status(2));
        Assert.AreEqual(8.02, r.DueAt(2), 1e-9);
        Assert.IsTrue(r.Fire(2, 4.0), "ручной — и во время задержки");
    }

    [Test]
    public void Validate_ReferencesCycleAndActions()
    {
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        DemoScenario.Fill(s, area, LowSpeedModelTests.Model45m());
        s.traffic.Add(new TrafficShipDefinition { id = "T1", placement = TrafficPlacement.Absolute, position = new WorldPosition(1500, 0), speedKnots = 0f });
        int baseCount = s.Validate().Count;

        ScenarioTrigger ok = Tr("Тр1", TriggerCondition.Waypoint, new TriggerAction { kind = TriggerActionKind.TargetOrder, target = "T1", speedKnots = 8f });
        ok.waypoint = "Поворот";
        s.triggers.Add(ok);
        Assert.AreEqual(baseCount, s.Validate().Count, "корректный триггер — без замечаний: " + string.Join("; ", s.Validate()));

        ScenarioTrigger badWp = Tr("Тр2", TriggerCondition.Waypoint, new TriggerAction { kind = TriggerActionKind.TargetOrder, target = "T9" });
        badWp.waypoint = "Старт";
        ScenarioTrigger loopA = Tr("Тр3", TriggerCondition.AfterTrigger);
        loopA.afterTrigger = "Тр4";
        ScenarioTrigger loopB = Tr("Тр4", TriggerCondition.AfterTrigger);
        loopB.afterTrigger = "Тр3";
        ScenarioTrigger stage = Tr("Тр5", TriggerCondition.TaskStage, new TriggerAction { kind = TriggerActionKind.Wind, windKnots = 20f });
        ScenarioTrigger empty = new ScenarioTrigger { id = "Тр1", condition = TriggerCondition.Time, timeSeconds = 2000f };
        s.triggers.AddRange(new[] { badWp, loopA, loopB, stage, empty });
        string all = string.Join("\n", s.Validate());
        StringAssert.Contains("Триггер Тр2: в маршруте нет точки «Старт»", all);
        StringAssert.Contains("Триггер Тр2: нет цели «T9»", all);
        StringAssert.Contains("Триггер Тр2: цели T9 не задан ни курс, ни скорость", all);
        StringAssert.Contains("замкнута по кругу (Тр3 → Тр4 → Тр3)", all);
        StringAssert.Contains("Триггер Тр5: «этап задания» — только в сценарии швартовки", all);
        StringAssert.Contains("Триггер Тр5: ветер действует только в модели 3 СС", all);
        StringAssert.Contains("Триггер Тр1: идентификатор повторяется", all);
        StringAssert.Contains("Триггер Тр1: нет действий", all);
        StringAssert.Contains("не раньше лимита 30:00", all);
        Assert.AreEqual("Тр6", TriggerText.NextId(s.triggers));
    }

    [Test]
    public void TargetOrder_StoppedTargetMoves_CourseDropsRoute()
    {
        ShipParameters mariner = LowSpeedModelTests.Mariner();
        var stopped = new TrafficShipDefinition { id = "T1", placement = TrafficPlacement.Absolute, position = new WorldPosition(0, 0), headingDeg = 330f, speedKnots = 0f };
        var v = new TrafficVessel(0, "T1", stopped, mariner, TrafficPlanner.Resolve(stopped, new OwnShipInitialState()));
        for (int i = 0; i < 3000; i++) v.Step(Dt);
        Assert.Less(v.SpeedKnots, 0.01, "без приказа стоит");

        v.Order(double.NaN, 8.0);
        for (int i = 0; i < 30000; i++) v.Step(Dt);   // 10 мин
        Assert.AreEqual(8.0, v.SpeedKnots, 0.4, "дал ход до заданной скорости");
        Assert.AreEqual(330.0, ((v.HeadingDeg % 360.0) + 360.0) % 360.0, 1.0, "курс не менялся");

        var routed = new TrafficShipDefinition
        {
            id = "T2", placement = TrafficPlacement.Absolute, position = new WorldPosition(0, 0), headingDeg = 0f, speedKnots = 10f,
            motion = TrafficMotion.Route, route = new List<WorldPosition> { new WorldPosition(0, 5000) }
        };
        var w = new TrafficVessel(1, "T2", routed, mariner, TrafficPlanner.Resolve(routed, new OwnShipInitialState()));
        Assert.IsNotNull(w.Route);
        w.Order(90.0, double.NaN);
        Assert.IsNull(w.Route, "курс снимает маршрут");
        Assert.AreEqual(90.0, w.CourseOrder, 1e-9);
        Assert.AreEqual(10.0, w.OrderedSpeedKnots, 1e-9, "скорость не менялась");
        w.Order(double.NaN, 0.0);
        Assert.AreEqual(0.0, w.SpeedFactor, 1e-9, "стоп");
    }

    [Test]
    public void Demo21_01_ValidCloseCrossing_AndEventTexts()
    {
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoTriggerScenario.Fill(s, area, LowSpeedModelTests.Model45m(), LowSpeedModelTests.Mariner(), null);
        CollectionAssert.IsEmpty(s.Validate(), string.Join("; ", s.Validate()));
        Assert.AreEqual(4, s.triggers.Count);
        Assert.AreEqual(DemoTriggerScenario.Version, s.demoVersion);

        // Файл сценария: триггеры — словами, туда и обратно без потерь
        area.name = "WaterArea_Demo";
        s.name = "21-01";   // имя ассета при чтении — имя файла
        ScenarioCatalog cat = ScenarioCatalog.From(new List<ScenarioDefinition> { s });
        string text = ScenarioFile.Write(s, cat);
        StringAssert.Contains("\"condition\": \"EnterCircle\"", text);
        StringAssert.Contains("\"kind\": \"TargetOrder\"", text);
        ScenarioLoadResult back = ScenarioFile.Read(text, cat, null, "21-01");
        Assert.IsTrue(back.Ok, string.Join("; ", back.Errors));
        Assert.AreEqual(JsonUtility.ToJson(s), JsonUtility.ToJson(back.Scenario));
        Assert.AreEqual("Тр3", back.Scenario.triggers[2].id);
        Assert.AreEqual(TriggerActionKind.Visibility, back.Scenario.triggers[2].actions[0].kind);

        // Кинематика без манёвра: своё судно 15 уз по маршруту от входа в круг Тр2 до пересечения второго колена,
        // T1 — 8 уз курсом 330° от стоянки; разница прихода — меньше минуты (курс пересечения)
        WorldPosition turn = s.route[2].position, exit = s.route[3].position;
        double ownEnter = DemoTriggerScenario.CircleCenter.North - DemoTriggerScenario.CircleRadiusM;
        double psi = 330.0 * Math.PI / 180.0;
        var d = new WorldPosition(Math.Sin(psi), Math.Cos(psi));
        WorldPosition leg = exit - turn;
        var u = new WorldPosition(leg.East / leg.Magnitude, leg.North / leg.Magnitude);
        // T0 + k·d = turn + q·u
        WorldPosition t0 = DemoTriggerScenario.TargetStart;
        double det = d.East * (-u.North) - d.North * (-u.East);
        double k = ((turn.East - t0.East) * (-u.North) - (turn.North - t0.North) * (-u.East)) / det;
        double q = (d.East * (turn.North - t0.North) - d.North * (turn.East - t0.East)) / det;
        double ownT = ((turn.North - ownEnter) + q) / (15.0 * ShipParameters.KnotsToMs);
        double tgtT = k / (8.0 * ShipParameters.KnotsToMs);
        TestContext.Out.WriteLine($"21-01: пересечение колена 2 в {q:F0} м от поворота; своё через {ownT:F0} с, T1 через {tgtT:F0} с");
        Assert.Less(Math.Abs(ownT - tgtT), 60.0);
        Assert.Greater(q, 0.0);

        Assert.AreEqual("ТРИГГЕР Тр2 «Сухогруз снимается» сработал", new ExerciseEvent(ExerciseEventType.TriggerFired) { Subject = s.triggers[1].Label }.Describe());
        Assert.AreEqual("Цель «T1» (по сценарию): курс 330°, 8,0 уз", new ExerciseEvent(ExerciseEventType.TargetOrder) { Subject = "T1", Value = 330, Value2 = 8 }.Describe());
        Assert.AreEqual("Цель «T1» (по сценарию): стоп", new ExerciseEvent(ExerciseEventType.TargetOrder) { Subject = "T1", Value2 = 0 }.Describe());
        StringAssert.EndsWith("(по сценарию)", new ExerciseEvent(ExerciseEventType.EngineFailure) { Subject = "Стоп" }.Describe());
        StringAssert.DoesNotEndWith("(по сценарию)", new ExerciseEvent(ExerciseEventType.EngineFailure) { Subject = "Стоп", ByInstructor = true }.Describe());
        StringAssert.Contains("в круге 400 м", TriggerText.Line(s.triggers[1], s));
        StringAssert.Contains("после Тр2 + 60 с → видимость 1,00 мили", TriggerText.Line(s.triggers[2], s));
    }
}
