// ============================================================
// DemoBerthScenario.cs — Сценарий 19-01 «Швартовка к причалу №1 лагом» (запрос 19.2б-3)
//
// Демонстрационная акватория (B1), причал №1 (DemoWaterArea.Berth: кромка 80 м курсом 060°,
// вода — к юго-востоку, швартовка левым бортом). Модель движения — 3 СС (силы причала, концов
// и подруливающего действуют только в ней). День, штиль.
// Начало: курс по кромке, мидель за StartBeforeMiddleM до середины причала, борт в StartGapM
// от кромки, «Самый малый вперёд» с установившейся скоростью этого положения (≈ 3 уз у Model45m).
// Дистанции сокращены против операции «Судно к причалу: подход» (300 м, 60 м): реальная
// швартовка медленная — последний подход лагом идёт со скоростью 0,1–0,3 уз, 30 м — это 3–10 мин.
// Задания — MooringTaskDefinition (подойти лагом, остановиться у кранцев, подать концы,
// ошвартоваться); упражнение завершается «ошвартован». Маршрут — только линия подхода (две
// точки: старт → борт у середины кромки) для карт и планшета; прибытие упражнение не завершает,
// маршрутные критерии выключены набором Assessment_Berthing (Setup 19.2 Berth Scenario).
// Значения — предварительные.
// ============================================================
using System.Collections.Generic;

public static class DemoBerthScenario
{
    public const string Id = "05";   // до 22.10.2026 — «19-01» (ScenarioNumbers)
    public const string Title = "Швартовка к причалу лагом";
    public const double StartBeforeMiddleM = 200.0;
    public const double StartGapM = 30.0;
    public const TelegraphOrder StartTelegraph = TelegraphOrder.DeadSlowAhead;
    public const float ApproachXteLimit = 100f;
    public const float TimeLimitSeconds = 1200f;    // 20 мин
    public const float TargetTimeSeconds = 600f;    // контрольное время, 10 мин
    public const int BerthIndex = 0;

    public const string Briefing =
        "День, штиль. Своё судно идёт самым малым ходом курсом вдоль причала №1, в 200 м до его середины " +
        "и в 30 м от кромки. Причал — по левому борту.\n\n" +
        "Задача: ошвартоваться левым бортом к причалу №1:\n" +
        "  1. подойти лагом;\n" +
        "  2. погасить ход и остановиться у кранцев;\n" +
        "  3. подать концы с носа и кормы;\n" +
        "  4. обтянуть концы и стоять у кранцев без хода.\n\n" +
        "Подход к кранцам — с малой скоростью: сильный навал перегружает кранцы, удар корпусом о кромку — авария. " +
        "Без хода руль не действует: разворачивайте нос подруливающим, корму — «толчком» машины при переложенном руле.\n\n" +
        "Команды — меню «Управление» (кнопка B): руль, машина, подруливающее, концы, переход на крыло. " +
        "Подруливающее — также рычаг на пульте слева от телеграфа. Расстояния до кромки и скорость к причалу — " +
        "строка «ПРИЧАЛ» на навигационном дисплее.\n\n" +
        "Лимит времени — 20 минут.";

    /// <summary>Начальное положение: мидель за StartBeforeMiddleM до середины, борт в StartGapM от кромки, курс по кромке.</summary>
    public static WorldPosition StartPosition(BerthArea b, float beam, out double headingDeg) =>
        b.AlongsidePosition(0.5 * b.Length - StartBeforeMiddleM, StartGapM, beam, out headingDeg);

    /// <summary>Конец линии подхода: борт у кромки (вылет кранца) против её середины.</summary>
    public static WorldPosition BerthPoint(BerthArea b, float beam) =>
        b.AlongsidePosition(0.5 * b.Length, b.fenderOuterDiameterM, beam, out _);

    /// <summary>Заполнить сценарий (прежнее содержимое заменяется; набор критериев — assessment).</summary>
    public static void Fill(ScenarioDefinition s, WaterArea area, ShipParameters own, AssessmentCriteria assessment)
    {
        BerthArea b = area != null && area.berths != null && area.berths.Count > BerthIndex ? area.berths[BerthIndex] : DemoWaterArea.Berth();
        float beam = own != null ? own.beam : (float)DemoWaterArea.BerthShipBeam;
        WorldPosition start = StartPosition(b, beam, out double hdg);
        s.id = Id;
        s.title = Title;
        s.briefing = Briefing;
        s.waterArea = area;
        s.shipParameters = own;
        s.motionModel = ScenarioMotionModel.Manoeuvring3Dof;
        s.initial = new OwnShipInitialState
        {
            position = start, headingDeg = (float)System.Math.Round(hdg, 2),
            speedKnots = own != null ? (float)System.Math.Round(ShipDynamicsModel.SteadySpeed(own, ScenarioText.SpeedFactor(StartTelegraph)) / ShipParameters.KnotsToMs, 2) : 3f,
            telegraph = StartTelegraph, rudderDeg = 0f
        };
        s.weather = WeatherPresetId.Calm;
        s.timeOfDay = TimeOfDay.Day;
        s.visibilityMeters = 0f;
        s.safeSpeedKnots = 0f;
        s.hideTargetsOnPlotter = false;
        s.route = new List<RouteWaypoint>
        {
            new RouteWaypoint("Старт", start, ApproachXteLimit),
            new RouteWaypoint(b.name, BerthPoint(b, beam), ApproachXteLimit)
        };
        s.traffic = new List<TrafficShipDefinition>();
        s.timeLimitSeconds = TimeLimitSeconds;
        s.endConditions = ScenarioEndConditions.Moored | ScenarioEndConditions.Grounding | ScenarioEndConditions.Collision | ScenarioEndConditions.TimeLimit;
        s.detectionRangeNm = 0f;
        s.mooring = new MooringTaskDefinition { enabled = true, berthIndex = BerthIndex, targetTimeS = TargetTimeSeconds };
        s.assessment = assessment;
    }

    /// <summary>
    /// Набор критериев сценария швартовки: значения по умолчанию, маршрутные критерии (R1–R8b) выключены —
    /// линия подхода только для карты. Расхождение и туман без целей и тумана не оцениваются и так.
    /// </summary>
    public static AssessmentSettings Assessment()
    {
        AssessmentSettings a = AssessmentSettings.Default();
        foreach (CriterionSetting c in a.criteria)
        {
            CriterionInfo info = AssessmentCatalog.Find(c.code);
            if (info != null && (info.Group == AssessmentGroup.Route || c.code == AssessmentCatalog.R8b)) c.enabled = false;
        }
        return a;
    }
}
