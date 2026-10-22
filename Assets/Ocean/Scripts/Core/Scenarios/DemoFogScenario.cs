// ============================================================
// DemoFogScenario.cs — Сценарий C4-01 «Плавание в тумане» (C4)
//
// Демонстрационная акватория (B1). День, штиль, видимость 0,5 мили (926 м; плотность
// тумана сцены ρ = 1,73/V = 0,00187). Своё судно — в 1,6 мили к югу от входа в фарватер,
// курс 000°, «Средний вперёд» (9,75 уз) — больше безопасной скорости 6 уз («Малый вперёд»),
// заданной сценарием (правила 6, 19 б; значение предварительное).
// Маршрут: старт (0; −3000) → вход в фарватер (0; −300), 2,7 км.
// T1 — сухогруз (Mariner, L 161 м, слышен на 1,5 мили), пересекает курс справа: «точка встречи»
// CPA 0, TCPA 6 мин при начальной скорости своего судна; затем уходит на юг западнее линии
// подхода. В начале он в 1,4 мили (курсовой угол около 45°): его слышно (туманный сигнал
// раз в 110 с), а увидеть можно только с 0,5 мили. Правильные действия: сразу сбавить ход
// до безопасного и подавать сигнал «—» не реже чем через 2 мин (правило 35 а); услышав
// сигнал впереди траверза — «Самый малый вперёд» (правило 19 д), не поворачивать влево (19 г).
// CPA_доп T1 — 0,2 мили (предварительно). Цели на планшете и в «Текущем балле» не показываются.
// ============================================================
using System.Collections.Generic;

public static class DemoFogScenario
{
    public const string Id = "03";   // до 22.10.2026 — «C4-01» (ScenarioNumbers)
    public const string Title = "Плавание в тумане";
    public const float VisibilityM = 926f;          // 0,5 мили
    public const float SafeSpeedKnots = 6f;         // «Малый вперёд» судна сцены (предварительно)
    public const float TargetRequiredCpaNm = 0.2f;  // предварительно
    public static readonly WorldPosition Start = new WorldPosition(0, -3000);
    public const TelegraphOrder StartTelegraph = TelegraphOrder.HalfAhead;
    public const float TimeLimitSeconds = 2100f;

    public const string Briefing =
        "День, штиль, ТУМАН: видимость около 0,5 мили (900 м). Своё судно — в 1,6 мили к югу от входа в учебный " +
        "фарватер, курс 000°, «Средний вперёд».\n\n" +
        "Задача: дойти до входа в фарватер (осевой знак, первая пара буёв).\n\n" +
        "В районе возможны другие суда; на планшете они не показываются — наблюдайте и слушайте. " +
        "Действуйте по МППСС-72 для ограниченной видимости: безопасная скорость (правила 6, 19 б), " +
        "туманные сигналы (правило 35), действия, услышав сигнал другого судна (правило 19 г, д).\n\n" +
        "Гудок (кнопка «ТИФОН») звучит, пока её держат: продолжительный звук — 4–6 с, короткий — около 1 с.\n\n" +
        "Столкновение и посадка на мель — провал упражнения. Лимит времени — 35 минут.";

    public static List<RouteWaypoint> Route() => new List<RouteWaypoint>
    {
        new RouteWaypoint("Старт", Start, DemoScenario.ApproachXteLimit),
        new RouteWaypoint("Вход в фарватер", DemoWaterArea.Axis()[0], DemoScenario.ApproachXteLimit)
    };

    /// <summary>Заполнить сценарий (прежнее содержимое заменяется; набор критериев не трогается).</summary>
    public static void Fill(ScenarioDefinition s, WaterArea area, ShipParameters own, ShipParameters cargo)
    {
        DemoScenario.Fill(s, area, own);
        s.id = Id;
        s.title = Title;
        s.briefing = Briefing;
        s.initial = new OwnShipInitialState
        {
            position = Start, headingDeg = 0f,
            speedKnots = own != null ? (float)System.Math.Round(ShipDynamicsModel.SteadySpeed(own, ScenarioText.SpeedFactor(StartTelegraph)) / ShipParameters.KnotsToMs, 2) : 9.75f,
            telegraph = StartTelegraph, rudderDeg = 0f
        };
        s.weather = WeatherPresetId.Calm;
        s.timeOfDay = TimeOfDay.Day;
        s.visibilityMeters = VisibilityM;
        s.safeSpeedKnots = SafeSpeedKnots;
        s.hideTargetsOnPlotter = true;
        s.route = Route();
        s.timeLimitSeconds = TimeLimitSeconds;
        s.traffic = Traffic(cargo);
    }

    public static List<TrafficShipDefinition> Traffic(ShipParameters cargo) => new List<TrafficShipDefinition>
    {
        new TrafficShipDefinition
        {
            id = "T1", name = "Сухогруз, пересекающий справа", type = TrafficVesselType.Cargo, parameters = cargo,
            placement = TrafficPlacement.MeetingPoint, cpaNm = 0f, tcpaMinutes = 6f, meetingSide = MeetingSide.Starboard,
            headingDeg = 270f, speedKnots = 10f, motion = TrafficMotion.Route,
            route = new List<WorldPosition> { new WorldPosition(-900, -1200), new WorldPosition(-900, -4500) },
            requiredCpaNm = TargetRequiredCpaNm
        }
    };
}
