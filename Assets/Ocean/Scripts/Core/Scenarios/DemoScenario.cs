// ============================================================
// DemoScenario.cs — Первый сценарий (B2): «Проход фарватером по путевым точкам»
//
// Акватория — демонстрационная (DemoWaterArea, B1). Маршрут — по оси
// фарватера: старт (0; −2500) → вход в фарватер (0; −300) → точка поворота
// (0; 3200), поворот вправо на 60° → выход из фарватера (3031; 4950); 9,2 км.
// Участки фарватера — узкость (правило 9, C2): оценивается, держится ли судно правее оси.
// XTE доп.: 250 м на подходе, 150 м в фарватере (полуширина 185 м минус запас
// ≈ 35 м на корпус и рыскание) — предварительные значения (глава 2 ВКР).
// День, умеренное волнение, полный ход 15 уз; номинальное время ≈ 20 мин,
// лимит 30 мин. Заполняет Tools/VR Bridge/Setup B2 Scenarios; используют тесты.
// ============================================================
using System.Collections.Generic;

public static class DemoScenario
{
    public const string Id = "B2-01";
    public const string Title = "Проход фарватером по путевым точкам";
    public const float ApproachXteLimit = 250f;
    public const float FairwayXteLimit = 150f;
    public const float TimeLimitSeconds = 1800f;
    public const TelegraphOrder StartTelegraph = TelegraphOrder.FullAhead;
    public const float StartSpeedKnots = 15f;

    /// <summary>Правило 9 (C2): в узкости — у правой стороны. Прежняя фраза брифинга — LegacyKeepAxisSentence.</summary>
    public const string KeepRightSentence = "В фарватере держитесь у оси с правой стороны (правило 9 МППСС).";
    public const string LegacyKeepAxisSentence = "Держитесь оси фарватера.";

    public const string Briefing =
        "Своё судно — в 1,2 мили (2,2 км) к югу от входа в учебный фарватер, курс 000°, полный ход 15 уз.\n\n" +
        "Задача: пройти фарватером по путевым точкам:\n" +
        "  1. Вход в фарватер — за осевым знаком, у первой пары буёв;\n" +
        "  2. Точка поворота — поворот вправо на 60°, на курс 060°;\n" +
        "  3. Выход из фарватера.\n\n" +
        KeepRightSentence + " Допустимое боковое отклонение — 250 м на подходе и 150 м в фарватере.\n\n" +
        "Фарватер шириной 2 кбт ограждён латеральными знаками МАМС (регион A): красные — слева, " +
        "зелёные — справа по ходу с моря. За поворотом и с внутренней стороны поворота — отмели, " +
        "обозначенные кардинальными знаками; к востоку от линии подхода — камень со знаком отдельной опасности.\n\n" +
        "Лимит времени — 30 минут.";

    /// <summary>Маршрут: старт + ось фарватера DemoWaterArea.Axis().</summary>
    public static List<RouteWaypoint> Route()
    {
        WorldPosition[] axis = DemoWaterArea.Axis();
        return new List<RouteWaypoint>
        {
            new RouteWaypoint("Старт", DemoWaterArea.StartPosition, ApproachXteLimit),
            new RouteWaypoint("Вход в фарватер", axis[0], ApproachXteLimit),
            new RouteWaypoint("Поворот", axis[1], FairwayXteLimit, narrowChannel: true),
            new RouteWaypoint("Выход из фарватера", axis[2], FairwayXteLimit, narrowChannel: true)
        };
    }

    /// <summary>Заполнить сценарий (прежнее содержимое заменяется).</summary>
    public static void Fill(ScenarioDefinition s, WaterArea area, ShipParameters ship)
    {
        s.id = Id;
        s.title = Title;
        s.briefing = Briefing;
        s.waterArea = area;
        s.shipParameters = ship;
        s.initial = new OwnShipInitialState
        {
            position = DemoWaterArea.StartPosition,
            headingDeg = (float)DemoWaterArea.StartHeadingDeg,
            speedKnots = StartSpeedKnots,
            telegraph = StartTelegraph,
            rudderDeg = 0f
        };
        s.weather = WeatherPresetId.Moderate;
        s.timeOfDay = TimeOfDay.Day;
        s.visibilityMeters = 0f;
        s.route = Route();
        s.traffic = new List<TrafficShipDefinition>();
        s.timeLimitSeconds = TimeLimitSeconds;
        s.endConditions = ScenarioEndConditions.All;
        s.detectionRangeNm = 0f;
        // s.assessment не трогается: набор критериев назначает Setup C2 Assessment
    }
}
