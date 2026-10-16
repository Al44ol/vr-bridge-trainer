// ============================================================
// DemoTugWorkScenario.cs — Сценарий 19-03 «Буксир прижимает судно к причалу при отжимном ветре» (запрос 19.4б)
//
// Своё судно — буксир (ShipParameters_Tug, джойстик 19.4а), модель 3 СС. Демонстрационная акватория, причал №1
// (как 19-01, 19-02). Судно-клиент (Model45m) стоит лагом в 30 м от кромки напротив середины причала, причал
// у него по левому борту; лоцман держит курс и место, поперёк — только ветер и буксир (TugWork.cs).
// Ветер 20 уз, отжимной, случайный в секторе ± 30° (как 19-02): без буксира клиента сносит от причала.
// Буксир — в 150 м от правого борта клиента напротив миделя, носом к нему, без хода.
// Линия «старт → точка касания» — только для карты (маршрутные критерии выключены).
// Лимит 15 мин, контрольное время 8 мин. Значения — предварительные.
// ============================================================
using System.Collections.Generic;

public static class DemoTugWorkScenario
{
    public const string Id = "19-03";
    public const string Title = "Буксир прижимает судно к причалу";
    public const float TimeLimitSeconds = 900f;    // 15 мин
    public const float TargetTimeSeconds = 480f;   // контрольное время, 8 мин

    public const string Briefing =
        "День, волнения нет. Ветер 20 уз, отжимной — дует с причала и сносит судно от кромки. " +
        "Вы — на буксире (22 м, две поворотные колонки, тяга на гаке 20 т). Судно 45 м стоит лагом в 30 м от причала №1, " +
        "причал у него по левому борту; машиной и подруливающим оно держит только курс и место вдоль причала — к причалу его ведёте вы.\n\n" +
        "Работайте по командам лоцмана:\n" +
        "  1. подойти к правому борту у миделя и упереться носовым кранцем — медленно, носом поперёк борта;\n" +
        "  2. прижать судно к причалу — без навала на кранцы;\n" +
        "  3. держать упор, пока судно заводит концы;\n" +
        "  4. по команде «отходите» отойти от борта, не задев его корпусом.\n\n" +
        "Управление — джойстик на месте телеграфа: наклон вперёд-назад — ход, вбок — лагом, поворот рукоятки — разворот. " +
        "Отпущенная рукоятка остаётся на месте. То же — меню «Управление» (кнопка B) → «Джойстик».\n\n" +
        "Лимит времени — 15 минут.";

    /// <summary>
    /// Заполнить сценарий: тип судна — буксир (tug), клиент — client (Model45m). Начальное место буксира считается
    /// от места клиента (TugWorkSystem.TugStart) — то же, что поставит тренажёр.
    /// </summary>
    public static void Fill(ScenarioDefinition s, WaterArea area, ShipParameters tug, ShipParameters client, AssessmentCriteria assessment)
    {
        const int berthIndex = DemoBerthScenario.BerthIndex;
        BerthArea b = area != null && area.berths != null && area.berths.Count > berthIndex ? area.berths[berthIndex] : DemoWaterArea.Berth();
        var task = new TugWorkDefinition { enabled = true, berthIndex = berthIndex, targetTimeS = TargetTimeSeconds };

        // Начальные условия — как TugWorkSystem.Place
        var sys = new TugWorkSystem(task, client, b, TugSpec.For(tug));
        var own = new ShipDynamicsModel(tug) { MotionOverride = MotionModel.Manoeuvring3Dof };
        sys.Place(own);
        WorldPosition start = new WorldPosition(own.East, own.North);
        WorldPosition contact = sys.ContactPoint();

        s.id = Id;
        s.title = Title;
        s.briefing = Briefing;
        s.waterArea = area;
        s.shipParameters = tug;
        s.motionModel = ScenarioMotionModel.Manoeuvring3Dof;
        s.initial = new OwnShipInitialState
        {
            position = start, headingDeg = (float)System.Math.Round(own.Psi * 180.0 / System.Math.PI, 2),
            speedKnots = 0f, telegraph = TelegraphOrder.Stop, rudderDeg = 0f
        };
        s.weather = WeatherPresetId.Calm;
        s.timeOfDay = TimeOfDay.Day;
        s.visibilityMeters = 0f;
        s.safeSpeedKnots = 0f;
        s.hideTargetsOnPlotter = false;
        s.route = new List<RouteWaypoint>
        {
            new RouteWaypoint("Старт", start, DemoBerthScenario.ApproachXteLimit),
            new RouteWaypoint("Борт судна", contact, DemoBerthScenario.ApproachXteLimit)
        };
        s.traffic = new List<TrafficShipDefinition>();
        s.timeLimitSeconds = TimeLimitSeconds;
        s.endConditions = ScenarioEndConditions.TugWorkDone | ScenarioEndConditions.Grounding | ScenarioEndConditions.TimeLimit;
        s.detectionRangeNm = 0f;
        s.mooring = new MooringTaskDefinition { enabled = false, berthIndex = berthIndex };
        s.tugWork = task;
        s.tugWorkClient = client;
        s.ownShipType = ScenarioShipType.Tug;   // 19.4б-2: тип судна «буксир» ставится при загрузке
        s.windKnots = DemoTugBerthScenario.WindKnots;
        s.windFromDeg = (float)DemoTugBerthScenario.OffBerthWindFromDeg(b);
        s.windRandomSectorDeg = DemoTugBerthScenario.WindSectorDeg;
        s.tugAvailable = false;
        s.assessment = assessment;
    }

    /// <summary>Набор критериев 19-03: маршрутные и «Швартовка» выключены (критерии работы буксира — 19.4в).</summary>
    public static AssessmentSettings Assessment()
    {
        AssessmentSettings a = DemoBerthScenario.Assessment();
        foreach (CriterionSetting c in a.criteria)
        {
            CriterionInfo info = AssessmentCatalog.Find(c.code);
            if (info != null && info.Group == AssessmentGroup.Mooring) c.enabled = false;
        }
        return a;
    }
}
