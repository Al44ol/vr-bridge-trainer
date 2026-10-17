// ============================================================
// DemoTugWorkScenario.cs — Сценарий 19-03 «Буксир прижимает судно к причалу при отжимном ветре» (запросы 19.4б, 19.4б-3)
//
// Своё судно — буксир (ShipParameters_Tug, джойстик 19.4а), модель 3 СС. Демонстрационная акватория.
// 19.4б-3 (по проверке в шлеме — «сухогруз слишком мал на фоне буксира»): судно-клиент — Mariner 161 × 23 м
// (было Model45m — такое судно швартуется своим подруливающим, буксир ему не нужен) у причала №2 (под Mariner,
// DemoWaterArea.Berth2); ветер 15 уз (при 20 уз сила ветра на Mariner лагом 14,9 т — запас буксира 20 т всего 5 т;
// при 15 уз — 8,4 т, запас ≈ 12 т; ThesisNotes 19.35), отжимной, случайный в секторе ± 30°.
// Клиент стоит лагом в 30 м от кромки напротив середины причала, причал у него по левому борту; лоцман держит курс
// и место, поперёк — только ветер и буксир (TugWork.cs); лоцман командует упором буксира (19.4б-3).
// Буксир — в 150 м от правого борта клиента напротив миделя, носом к нему, без хода. Касание — в ± 15 м от миделя.
// Линия «старт → точка касания» — только для карты (маршрутные критерии выключены).
// Лимит 20 мин, контрольное время 12 мин. Значения — предварительные.
// Version — номер версии: ассет с меньшей версией заполняется заново при входе в Play Mode (SetupTugWork).
// ============================================================
using System.Collections.Generic;

public static class DemoTugWorkScenario
{
    public const string Id = "19-03";
    public const string Title = "Буксир прижимает судно к причалу";
    /// <summary>Версия сценария: 1 — 19.4б-2 (Model45m, причал №1, 20 уз); 2 — 19.4б-3 (Mariner, причал №2, 15 уз, команды упором).</summary>
    public const int Version = 2;
    public const float TimeLimitSeconds = 1200f;   // 20 мин
    public const float TargetTimeSeconds = 720f;   // контрольное время, 12 мин
    public const float WindKnots = 15f;
    public const float ContactZoneM = 15f;
    public const int BerthIndex = DemoWaterArea.Berth2Index;

    public const string Briefing =
        "День, волнения нет. Ветер 15 уз, отжимной — дует с причала и сносит судно от кромки. " +
        "Вы — на буксире (22 м, две поворотные колонки, тяга на гаке 20 т). Сухогруз 161 м стоит лагом в 30 м от причала №2, " +
        "причал у него по левому борту; машиной и подруливающим он держит только курс и место вдоль причала — к причалу его ведёте вы.\n\n" +
        "Работайте по командам лоцмана (УКВ):\n" +
        "  1. подойти к правому борту у миделя и упереться носовым кранцем — медленно, носом поперёк борта;\n" +
        "  2. прижать судно к причалу — упор держите таким, какой назовёт лоцман: «толкай самым малым / малым / средним / полным» " +
        "(ступени рукоятки 25 / 50 / 75 / 100 %), «стоп толкать»;\n" +
        "  3. держать упор, пока судно заводит концы;\n" +
        "  4. по команде «отходите» отойти от борта, не задев его корпусом.\n\n" +
        "Управление — джойстик на пульте: наклон вперёд-назад — ход, вбок — лагом, поворот рукоятки — разворот; " +
        "ступени упора — со щелчком. Монитор пульта — ход, лагом, команда лоцмана; колонки — на левом экране.\n\n" +
        "Лимит времени — 20 минут.";

    /// <summary>
    /// Заполнить сценарий: тип судна — буксир (tug), клиент — client (19.4б-3: Mariner), причал №2 акватории (нет — DemoWaterArea.Berth2;
    /// в ассет акватории его дописывает SetupTugWork). Начальное место буксира считается
    /// от места клиента (TugWorkSystem.TugStart) — то же, что поставит тренажёр.
    /// </summary>
    public static void Fill(ScenarioDefinition s, WaterArea area, ShipParameters tug, ShipParameters client, AssessmentCriteria assessment)
    {
        const int berthIndex = BerthIndex;
        BerthArea b = area != null && area.berths != null && area.berths.Count > berthIndex ? area.berths[berthIndex] : DemoWaterArea.Berth2();
        var task = new TugWorkDefinition { enabled = true, berthIndex = berthIndex, targetTimeS = TargetTimeSeconds, contactZoneM = ContactZoneM };

        // Начальные условия — как TugWorkSystem.Place
        var sys = new TugWorkSystem(task, client, b, TugSpec.For(tug));
        var own = new ShipDynamicsModel(tug) { MotionOverride = MotionModel.Manoeuvring3Dof };
        sys.Place(own);
        WorldPosition start = new WorldPosition(own.East, own.North);
        WorldPosition contact = sys.ContactPoint();

        s.id = Id;
        s.title = Title;
        s.demoVersion = Version;
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
        s.windKnots = WindKnots;
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
