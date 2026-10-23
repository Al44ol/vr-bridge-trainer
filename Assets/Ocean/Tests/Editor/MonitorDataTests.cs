// ============================================================
// MonitorDataTests.cs — Данные мониторов M1, M5–M8 (запрос 16б, Core): масштаб карты, таблица целей САРП,
// следующая точка маршрута, журнал сообщений и подписи.
// ============================================================
using System.Collections.Generic;
using NUnit.Framework;

public class MonitorDataTests
{
    [Test]
    public void MapRange_SmallestRangeWithMargin()
    {
        Assert.AreEqual(0.25, MapRange.Choose(0));
        Assert.AreEqual(0.25, MapRange.Choose(400), "0,25 мили = 463 м ≥ 400·1,15");
        Assert.AreEqual(0.5, MapRange.Choose(500), "575 м > 463 м");
        Assert.AreEqual(2.0, MapRange.Choose(1852 * 1.5));
        Assert.AreEqual(8.0, MapRange.Choose(1852 * 30), "дальше 8 миль — 8");
    }

    [Test]
    public void TargetTable_SortDangerExclude()
    {
        var own = new WorldPosition(0, 0);
        var targets = new List<TargetInput>
        {
            new TargetInput("A", new WorldPosition(0, 3704), 180, 10),       // встречный по носу, 2 мили: CPA 0
            new TargetInput("B", new WorldPosition(1852, 3704), 180, 10),    // встречный, 1 миля правее: CPA 1 миля
            new TargetInput("C", new WorldPosition(0, -1852), 180, 10),      // позади, уходит: расходящаяся
            new TargetInput("TUG", new WorldPosition(50, 0), 0, 10)          // рабочее судно — не цель
        };
        List<TargetRow> rows = TargetTable.Build(own, 0, 10, 0, targets, exclude: id => id == "TUG");
        Assert.AreEqual(3, rows.Count, "буксир исключён");
        Assert.AreEqual("A", rows[0].Id);
        Assert.AreEqual("B", rows[1].Id);
        Assert.AreEqual("C", rows[2].Id, "расходящиеся — после сближающихся");
        Assert.AreEqual(0.0, rows[0].CpaNm, 1e-6);
        Assert.AreEqual(6.0, rows[0].TcpaMin, 0.01, "2 мили при сближении 20 уз — 6 мин (узел → м/с — float)");
        Assert.IsTrue(rows[0].Dangerous);
        Assert.IsFalse(rows[1].Dangerous, "CPA 1 миля ≥ 0,5");
        Assert.IsFalse(rows[2].Approaching);
        Assert.AreEqual(180.0, rows[2].BearingDeg, 1e-6);

        // Снос лагом (3 СС): 2 уз к ПБ при курсе 0 — вектор на восток
        TargetTable.OwnVelocity(0, 0, 2, out double ve, out double vn);
        Assert.AreEqual(2 * ShipParameters.KnotsToMs, ve, 1e-6);
        Assert.AreEqual(0.0, vn, 1e-9);
    }

    [Test]
    public void RouteReadout_NextPointDistanceXte()
    {
        var route = new List<RouteWaypoint>
        {
            new RouteWaypoint { name = "Начало", position = new WorldPosition(0, 0) },
            new RouteWaypoint { name = "Буй 1", position = new WorldPosition(0, 1000), xteLimit = 100 },
            new RouteWaypoint { name = "Буй 2", position = new WorldPosition(1000, 1000), xteLimit = 50 }
        };
        RouteReadout r = RouteReadout.For(route, 0, new WorldPosition(30, 600));
        Assert.IsTrue(r.Valid);
        Assert.AreEqual(1, r.Next);
        Assert.AreEqual(3, r.Count);
        Assert.AreEqual("Буй 1", r.Name);
        Assert.AreEqual(0.0, r.LegCourseDeg, 1e-9);
        Assert.AreEqual(30.0, r.Xte, 1e-9, "правее линии — плюс");
        Assert.IsFalse(r.Outside);
        Assert.AreEqual(System.Math.Sqrt(30 * 30 + 400 * 400), r.DistanceM, 1e-9);
        Assert.Greater(r.BearingDeg, 355.0, "точка чуть левее носа");

        RouteReadout r2 = RouteReadout.For(route, 5, new WorldPosition(500, 1080));
        Assert.AreEqual(2, r2.Next, "участок за концом — последний");
        Assert.AreEqual(90.0, r2.LegCourseDeg, 1e-9);
        Assert.AreEqual(-80.0, r2.Xte, 1e-9, "севернее линии на восток — левее");
        Assert.IsTrue(r2.Outside, "80 м > 50 м");
        Assert.IsFalse(RouteReadout.For(route.GetRange(0, 1), 0, default).Valid);
    }

    [Test]
    public void Messages_And_Texts()
    {
        Assert.AreEqual(MessageKind.Hidden, MonitorMessages.Kind(ExerciseEventType.HelmStarted));
        Assert.AreEqual(MessageKind.Hidden, MonitorMessages.Kind(ExerciseEventType.EncounterStarted), "в «экзамене» — подсказка");
        Assert.AreEqual(MessageKind.Alarm, MonitorMessages.Kind(ExerciseEventType.Grounded));
        Assert.AreEqual(MessageKind.Normal, MonitorMessages.Kind(ExerciseEventType.WaypointPassed));
        Assert.AreEqual(MessageKind.Normal, MonitorMessages.Kind(ExerciseEventType.ScenarioMessage));
        Assert.IsTrue(MonitorMessages.IsFailure(ExerciseEventType.SteeringRestored));

        Assert.AreEqual("12,4", MonitorText.Speed(12.38));
        Assert.AreEqual("−3,2", MonitorText.Speed(-3.2));
        Assert.AreEqual("0,0", MonitorText.Speed(-0.01));
        Assert.AreEqual("—", MonitorText.Tcpa(-2));
        Assert.AreEqual("0,82", MonitorText.Nm(0.8213));
        Assert.AreEqual("12,5", MonitorText.Nm(12.47));
        Assert.AreEqual("С", MonitorText.Point8(350));
        Assert.AreEqual("СВ", MonitorText.Point8(45));
        Assert.AreEqual("ЮЗ", MonitorText.Point8(-135));
        Assert.AreEqual("12 м ПБ", MonitorText.Xte(12.2));
        Assert.AreEqual("7 м ЛБ", MonitorText.Xte(-7));
        Assert.AreEqual("умеренное", MonitorText.Sea(WeatherPresetId.Moderate));
    }
}
