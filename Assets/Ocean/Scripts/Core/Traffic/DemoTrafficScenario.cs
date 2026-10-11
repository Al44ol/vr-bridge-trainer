// ============================================================
// DemoTrafficScenario.cs — Сценарий B3-01 «Расхождение с судами» (B3)
//
// Демонстрационная акватория (B1), маршрут своего судна — как в B2-01
// (старт (0; −2500) 000° 15 уз → вход в фарватер → поворот вправо на 60° → выход).
// Пресет погоды «Ночь» — проверка навигационных огней (настоящая ночь — B4).
// Три цели, по одной ситуации МППСС:
//   T1 — встречный сухогруз (Mariner, 12 уз) идёт фарватером навстречу, держась
//        правой для себя стороны в 90 м от оси (правило 9 а); с судном, идущим по
//        оси, расходится левыми бортами (CPA ≈ 90 м) — ситуация правил 9 и 14;
//   T2 — танкер (Mariner, 10 уз) пересекает курс справа на подходе: «точка встречи»
//        CPA 0 (курс столкновения), TCPA 4 мин — своё судно уступает дорогу (правило 15);
//        после встречи уходит на юго-запад;
//   T3 — малое судно (Model45m, 6 уз) идёт фарватером впереди в 80 м правее оси —
//        своё судно обгоняет его после поворота (правило 13).
// Без манёвра своего судна (авторулевой по маршруту) T2 приводит к столкновению
// около t = 4 мин — это проверяет тест; T1 и T3 проходят на десятках метров.
// Значения — демонстрационные (без обоснования как учебной нормы).
// ============================================================
using System.Collections.Generic;

public static class DemoTrafficScenario
{
    public const string Id = "B3-01";
    public const string Title = "Расхождение с судами на подходе и в фарватере";
    public const double OncomingOffset = 90.0;     // T1: от оси влево (по своему ходу — вправо), м
    public const double OvertakenOffset = 80.0;    // T3: от оси вправо, м
    public const double OncomingFromTurn = 1500.0; // T1: старт на колене 2 в 1500 м от точки поворота

    public const string Briefing =
        "Ночь. Своё судно — в 1,2 мили к югу от входа в учебный фарватер, курс 000°, полный ход 15 уз. " +
        "Маршрут — как в упражнении B2-01: вход в фарватер, поворот вправо на 60°, выход из фарватера.\n\n" +
        "В районе — другие суда. Наблюдайте за огнями, определяйте ситуацию сближения и действуйте " +
        "по МППСС-72 (правила 9, 13–17): на подходе возможна встреча с судном, пересекающим курс; " +
        "в фарватере — встречное и попутное суда.\n\n" +
        "Столкновение и посадка на мель — провал упражнения. Лимит времени — 30 минут.";

    /// <summary>Заполнить сценарий (прежнее содержимое заменяется).</summary>
    public static void Fill(ScenarioDefinition s, WaterArea area, ShipParameters own,
                            ShipParameters cargo, ShipParameters tanker, ShipParameters small)
    {
        DemoScenario.Fill(s, area, own);
        s.id = Id;
        s.title = Title;
        s.briefing = Briefing;
        s.weather = WeatherPresetId.Night;
        s.timeOfDay = TimeOfDay.Night;
        s.traffic = Traffic(cargo, tanker, small);
    }

    public static List<TrafficShipDefinition> Traffic(ShipParameters cargo, ShipParameters tanker, ShipParameters small)
    {
        WorldPosition[] axis = DemoWaterArea.Axis();
        List<WorldPosition> left = DemoWaterArea.OffsetPolyline(axis, OncomingOffset);
        List<WorldPosition> right = DemoWaterArea.OffsetPolyline(axis, -OvertakenOffset);

        // T1: старт на колене 2 (левая для своего судна сторона), идёт к повороту, по колену 1 на юг и дальше
        WorldPosition l1 = left[1], l2 = left[2];
        WorldPosition dir = (l2 - l1) * (1.0 / (l2 - l1).Magnitude);
        var t1 = new TrafficShipDefinition
        {
            id = "T1", name = "Встречный сухогруз", type = TrafficVesselType.Cargo, parameters = cargo,
            placement = TrafficPlacement.Absolute, position = l1 + dir * OncomingFromTurn,
            speedKnots = 12f, motion = TrafficMotion.Route,
            route = new List<WorldPosition> { l1, left[0], new WorldPosition(left[0].East, -3600) }
        };

        // T2: пересекает курс справа, курс столкновения через 4 мин, затем на юго-запад
        var t2 = new TrafficShipDefinition
        {
            id = "T2", name = "Танкер, пересекающий справа", type = TrafficVesselType.Tanker, parameters = tanker,
            placement = TrafficPlacement.MeetingPoint, cpaNm = 0f, tcpaMinutes = 4f, meetingSide = MeetingSide.Starboard,
            headingDeg = 270f, speedKnots = 10f, motion = TrafficMotion.Route,
            route = new List<WorldPosition> { new WorldPosition(-900, -1300), new WorldPosition(-900, -4500) }
        };

        // T3: малое судно впереди в фарватере, правее оси; после поворота — колено 2 и дальше
        WorldPosition r2 = right[2], r1 = right[1];
        WorldPosition dir2 = (r2 - r1) * (1.0 / (r2 - r1).Magnitude);
        var t3 = new TrafficShipDefinition
        {
            id = "T3", name = "Малое судно впереди", type = TrafficVesselType.Small, parameters = small,
            placement = TrafficPlacement.Absolute, position = new WorldPosition(right[0].East, 1200),
            speedKnots = 6f, motion = TrafficMotion.Route,
            route = new List<WorldPosition> { r1, r2, r2 + dir2 * 3000.0 }
        };
        return new List<TrafficShipDefinition> { t1, t2, t3 };
    }
}
