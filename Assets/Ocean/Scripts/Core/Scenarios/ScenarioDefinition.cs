// ============================================================
// ScenarioDefinition.cs — Описание учебного сценария (B2)
//
// Новый сценарий создаётся без программирования: Create → VR Bridge → Scenario
// и заполнение полей в инспекторе (маршрут можно взять по оси фарватера
// кнопкой инспектора и двигать ручками в Scene View — объект Scenario).
//
// Содержимое:
//   • идентификатор, название, текст брифинга;
//   • акватория (WaterArea) и параметры своего судна (ShipParameters);
//   • начальные условия своего судна: позиция, курс, скорость, телеграф, руль;
//   • погода (пресет WeatherController) и время суток (B4) — независимо; видимость (C4) — пока поле;
//   • маршрут: путевые точки, у каждой — допустимое боковое отклонение (XTE)
//     на участке, ведущем К этой точке (у первой точки не используется);
//   • другие суда (B3): тип, параметры, начальная позиция (абсолютная, от своего
//     судна или «точка встречи» по CPA/TCPA), курс и скорость или маршрут;
//   • лимит времени и условия завершения;
//   • оценка (C2): набор критериев (AssessmentCriteria; пусто — значения по
//     умолчанию), дистанция обнаружения целей (0 — по видимости и времени суток),
//     у путевой точки — признак «участок — узкость» (правило 9 МППСС),
//     у цели — допустимая дистанция расхождения CPA_доп;
//   • 19.2б-3: модель движения на время сценария (по умолчанию — как выбрано в тренажёре)
//     и задание швартовки (MooringTaskDefinition: причал, условия этапов, финиш «ошвартован»);
//   • 19.3б: ветер (скорость и «откуда»; только в модели 3 СС) и разрешение вызвать буксир в упражнении.
// Досрочное завершение инструктором доступно всегда и в условиях не задаётся.
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Положение машинного телеграфа (как в EngineTelegraph, по умолчанию).</summary>
public enum TelegraphOrder
{
    [InspectorName("Полный вперёд")] FullAhead,
    [InspectorName("Средний вперёд")] HalfAhead,
    [InspectorName("Малый вперёд")] SlowAhead,
    [InspectorName("Самый малый вперёд")] DeadSlowAhead,
    [InspectorName("Стоп")] Stop,
    [InspectorName("Самый малый назад")] DeadSlowAstern,
    [InspectorName("Малый назад")] SlowAstern,
    [InspectorName("Средний назад")] HalfAstern,
    [InspectorName("Полный назад")] FullAstern
}

/// <summary>Пресет погоды WeatherController.</summary>
public enum WeatherPresetId
{
    [InspectorName("Штиль")] Calm,
    [InspectorName("Умеренное волнение")] Moderate,
    [InspectorName("Шторм")] Storm,
    [InspectorName("Слабое волнение, видимость 6,2 мили (бывш. «Ночь»)")] Night
}

/// <summary>Время суток (B4): независимо от погоды; освещение — EnvironmentModel.</summary>
public enum TimeOfDay
{
    [InspectorName("День")] Day,
    [InspectorName("Сумерки")] Twilight,
    [InspectorName("Ночь")] Night
}

/// <summary>Условия завершения упражнения (досрочное завершение инструктором — всегда).</summary>
[Flags]
public enum ScenarioEndConditions
{
    None = 0,
    [InspectorName("Прибытие в последнюю точку")] Arrival = 1,
    [InspectorName("Посадка на мель")] Grounding = 2,
    [InspectorName("Столкновение (B3)")] Collision = 4,
    [InspectorName("Истечение времени")] TimeLimit = 8,
    [InspectorName("Ошвартован (сценарий швартовки)")] Moored = 16,
    [InspectorName("Задание буксира выполнено (19.4б)")] TugWorkDone = 32,
    All = Arrival | Grounding | Collision | TimeLimit
}

/// <summary>Модель движения своего судна на время сценария (19.2б-3).</summary>
public enum ScenarioMotionModel
{
    [InspectorName("Как выбрано в тренажёре")] AsSelected,
    [InspectorName("Номото (курс)")] Nomoto,
    [InspectorName("3 СС (малые скорости, швартовка)")] Manoeuvring3Dof
}

/// <summary>19.4б-2: тип своего судна, нужный сценарию (ShipType — в коде сцены, Core на него не ссылается).</summary>
public enum ScenarioShipType
{
    [InspectorName("Как обычно (модель 45 м; на катере — вариант)")] Default,
    [InspectorName("Свой буксир")] Tug
}

/// <summary>Начальные условия своего судна.</summary>
[Serializable]
public class OwnShipInitialState
{
    [Tooltip("Позиция: восток, север, м")]
    public WorldPosition position;
    [Tooltip("Курс, градусы (0 = N, по часовой)")]
    public float headingDeg;
    [Tooltip("Скорость хода, уз (судно разгоняется/тормозит к скорости телеграфа)")]
    public float speedKnots;
    public TelegraphOrder telegraph = TelegraphOrder.Stop;
    [Tooltip("Руль, градусы (> 0 — правый борт)")]
    public float rudderDeg;
}

/// <summary>Путевая точка маршрута.</summary>
[Serializable]
public class RouteWaypoint
{
    public string name = "Точка";
    public WorldPosition position;
    [Tooltip("Допустимое боковое отклонение XTE на участке, ведущем к этой точке, м (у первой точки не используется)")]
    public float xteLimit = 150f;
    [Tooltip("Участок, ведущий к этой точке, — узкость или фарватер (правило 9 МППСС: держаться правой стороны). Маршрут — по оси узкости (C2)")]
    public bool narrowChannel;

    public RouteWaypoint() { }

    public RouteWaypoint(string name, WorldPosition position, float xteLimit, bool narrowChannel = false)
    {
        this.name = name;
        this.position = position;
        this.xteLimit = xteLimit;
        this.narrowChannel = narrowChannel;
    }
}

// Другое судно — TrafficShipDefinition (Core/Traffic/TrafficDefinition.cs, B3).

[CreateAssetMenu(menuName = "VR Bridge/Scenario", fileName = "Scenario", order = 10)]
public class ScenarioDefinition : ScriptableObject
{
    [Header("Сценарий")]
    [Tooltip("Короткий идентификатор (для записи и отчётов), например B2-01")]
    public string id = "S-00";
    public string title = "Новый сценарий";
    [TextArea(6, 20)] public string briefing = "";
    [Tooltip("19.4б-3: версия демо-сценария, созданного кодом (Demo…Scenario.Version); ассет с меньшей версией заполняется " +
             "заново при входе в Play Mode. 0 — сценарий не из кода или до 19.4б-3")]
    public int demoVersion;

    [Header("Акватория и судно")]
    public WaterArea waterArea;
    [Tooltip("Пусто — параметры, назначенные в сцене")]
    public ShipParameters shipParameters;
    [Tooltip("Модель движения на время сценария (19.2б-3); «как выбрано» — прежнее поведение. Сценарий швартовки — 3 СС (силы причала только в ней)")]
    public ScenarioMotionModel motionModel = ScenarioMotionModel.AsSelected;

    [Header("Начальные условия своего судна")]
    public OwnShipInitialState initial = new OwnShipInitialState();

    [Header("Условия плавания")]
    public WeatherPresetId weather = WeatherPresetId.Moderate;
    [Tooltip("День / сумерки / ночь — независимо от погоды (B4)")]
    public TimeOfDay timeOfDay = TimeOfDay.Day;
    [Tooltip("Видимость, м; 0 — по пресету погоды. Плотность тумана сцены пересчитывается: ρ = 1,73/V (C4)")]
    public float visibilityMeters;
    [Tooltip("Безопасная скорость в ограниченную видимость, уз (правила 6, 19 б; критерии F3, F4); 0 — не оценивается")]
    public float safeSpeedKnots;
    [Tooltip("Не показывать цели на навигационном планшете и в «Текущем балле» (обнаружение — только глазами и на слух; C4, D2)")]
    public bool hideTargetsOnPlotter;

    [Header("Маршрут")]
    public List<RouteWaypoint> route = new List<RouteWaypoint>();

    [Header("Другие суда (B3)")]
    public List<TrafficShipDefinition> traffic = new List<TrafficShipDefinition>();

    [Header("Завершение")]
    [Tooltip("Лимит времени, с; 0 — без лимита")]
    public float timeLimitSeconds = 1800f;
    public ScenarioEndConditions endConditions = ScenarioEndConditions.All;

    [Header("Оценка (C2)")]
    [Tooltip("Набор критериев, порогов и весов; пусто — значения по умолчанию (AssessmentSettings.Default)")]
    public AssessmentCriteria assessment;
    [Tooltip("Дистанция обнаружения целей, мили; 0 — по видимости и времени суток (днём — видимость, ночью — дальность огней)")]
    public float detectionRangeNm;

    [Header("Швартовка (19.2б-3)")]
    public MooringTaskDefinition mooring = new MooringTaskDefinition();

    [Header("Ветер и буксир (19.3б)")]
    [Tooltip("Ветер, уз; 0 — нет. Действует только в модели 3 СС (Isherwood); волны не меняет")]
    public float windKnots;
    [Tooltip("Ветер — откуда, ° (0 = N, по часовой)")]
    public float windFromDeg;
    [Tooltip("19.3б-2: 0 — направление задано (windFromDeg); > 0 — случайное при каждой загрузке в секторе этой ширины вокруг windFromDeg, °; 360 — любое")]
    [Range(0f, 360f)] public float windRandomSectorDeg;
    [Tooltip("Буксир можно вызвать из меню B («Буксир») во время упражнения; в свободном плавании — всегда")]
    public bool tugAvailable;

    [Header("Упражнение на буксире (19.4б)")]
    [Tooltip("Своё судно — буксир: прижать судно-клиента к причалу (TugWork.cs)")]
    public TugWorkDefinition tugWork = new TugWorkDefinition();
    [Tooltip("Параметры судна-клиента; пусто — ShipParameters_Model45m")]
    public ShipParameters tugWorkClient;
    [Tooltip("19.4б-2: тип своего судна — при загрузке переключается сам (буксир — упражнение на буксире)")]
    public ScenarioShipType ownShipType = ScenarioShipType.Default;

    /// <summary>21.1: сценарий загружен из файла (путь); пусто — встроенный ассет. В ассет и в файл не пишется.</summary>
    [NonSerialized] public string sourceFile;

    /// <summary>21.1: сценарий из файла (не встроенный).</summary>
    public bool IsFromFile => !string.IsNullOrEmpty(sourceFile);

    /// <summary>19.3б-2:направление ветра при загрузке: заданное или случайное в секторе (random — источник случайных чисел).</summary>
    public double DrawWindFromDeg(System.Random random)
    {
        if (windRandomSectorDeg <= 0f || random == null) return windFromDeg;
        double sector = Math.Min(360.0, windRandomSectorDeg);
        double d = windFromDeg + (random.NextDouble() - 0.5) * sector;
        return Math.Round(((d % 360.0) + 360.0) % 360.0);
    }

    /// <summary>Сценарий швартовки (задания вместо маршрута).</summary>
    public bool IsMooring => mooring != null && mooring.enabled;

    /// <summary>19.4б: упражнение на своём буксире (судно-клиент, команды лоцмана, задания).</summary>
    public bool IsTugWork => tugWork != null && tugWork.enabled;

    /// <summary>19.4б-2: сценарий выполняется на своём буксире (тип судна переключается при загрузке).</summary>
    public bool RequiresOwnTug => ownShipType == ScenarioShipType.Tug;

    public bool Ends(ScenarioEndConditions c) => (endConditions & c) != 0;

    /// <summary>Длина маршрута по путевым точкам, м.</summary>
    public double RouteLength
    {
        get
        {
            double s = 0;
            for (int i = 0; i + 1 < route.Count; i++) s += (route[i + 1].position - route[i].position).Magnitude;
            return s;
        }
    }

    /// <summary>Проблемы описания сценария (пусто — сценарий корректен).</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(id)) problems.Add("Не задан идентификатор.");
        if (string.IsNullOrWhiteSpace(title)) problems.Add("Не задано название.");
        if (string.IsNullOrWhiteSpace(briefing)) problems.Add("Пустой текст брифинга.");
        if (waterArea == null) problems.Add("Не назначена акватория (WaterArea).");
        if (route == null || route.Count < 2)
            problems.Add("В маршруте меньше двух путевых точек.");
        else
        {
            for (int i = 1; i < route.Count; i++)
            {
                if (route[i] == null) { problems.Add($"Путевая точка {i} пустая."); continue; }
                if (route[i].xteLimit <= 0f) problems.Add($"Точка {i} «{route[i].name}»: XTE должно быть больше 0.");
                if (route[i - 1] != null && (route[i].position - route[i - 1].position).Magnitude < 1.0)
                    problems.Add($"Точки {i - 1} и {i} совпадают.");
            }
            if (waterArea != null)
                for (int i = 0; i < route.Count; i++)
                    if (route[i] != null && !waterArea.InBounds(route[i].position))
                        problems.Add($"Точка {i} «{route[i].name}» вне района акватории.");
        }
        if (initial == null) problems.Add("Не заданы начальные условия.");
        else
        {
            if (waterArea != null && !waterArea.InBounds(initial.position)) problems.Add("Начальная позиция вне района акватории.");
            if (waterArea != null && shipParameters != null && waterArea.DepthAt(initial.position) < shipParameters.draft)
                problems.Add("Начальная позиция — на мели или на суше.");
            if (initial.speedKnots < 0f) problems.Add("Начальная скорость отрицательная (задний ход начальными условиями не задаётся).");
        }
        if (timeLimitSeconds < 0f) problems.Add("Лимит времени отрицательный.");
        if (timeLimitSeconds == 0f && Ends(ScenarioEndConditions.TimeLimit))
            problems.Add("Условие «истечение времени» включено, но лимит = 0 (без лимита).");
        if (visibilityMeters < 0f) problems.Add("Видимость отрицательная.");
        if (safeSpeedKnots < 0f) problems.Add("Безопасная скорость отрицательная.");
        if (detectionRangeNm < 0f) problems.Add("Дистанция обнаружения целей отрицательная.");
        ValidateTraffic(problems);
        ValidateMooring(problems);
        ValidateTugWork(problems);
        // 19.3б: ветер и буксир — только в модели 3 СС
        if (windKnots < 0f) problems.Add("Ветер отрицательный.");
        if (windKnots > 0f && motionModel != ScenarioMotionModel.Manoeuvring3Dof)
            problems.Add("Ветер действует только в модели 3 СС — выберите её в «Модель движения» (иначе ветра не будет).");
        if (tugAvailable && motionModel != ScenarioMotionModel.Manoeuvring3Dof)
            problems.Add("Буксир работает только в модели 3 СС — выберите её в «Модель движения».");
        return problems;
    }

    // Швартовка (19.2б-3)
    private void ValidateMooring(List<string> problems)
    {
        if (!IsMooring)
        {
            if (Ends(ScenarioEndConditions.Moored)) problems.Add("Условие «ошвартован» включено, но задание швартовки выключено.");
            return;
        }
        if (waterArea != null && mooring.BerthIn(waterArea) == null)
            problems.Add($"Швартовка: в акватории нет причала № {mooring.berthIndex} (причалов {(waterArea.berths != null ? waterArea.berths.Count : 0)}).");
        if (motionModel != ScenarioMotionModel.Manoeuvring3Dof)
            problems.Add("Швартовка: силы причала и концов действуют только в модели 3 СС — выберите её в «Модель движения».");
        if (!Ends(ScenarioEndConditions.Moored)) problems.Add("Швартовка: не включено условие завершения «ошвартован».");
        if (Ends(ScenarioEndConditions.Arrival)) problems.Add("Швартовка: условие «прибытие в последнюю точку» завершит упражнение раньше швартовки.");
        if (mooring.alongsideGapM <= 0f || mooring.stopGapM <= 0f || mooring.mooredHoldS < 0f || mooring.mooredSpeedKnots <= 0f || mooring.stopSpeedKnots <= 0f)
            problems.Add("Швартовка: пороги этапов должны быть больше 0.");
    }

    // Упражнение на буксире (19.4б)
    private void ValidateTugWork(List<string> problems)
    {
        if (!IsTugWork)
        {
            if (Ends(ScenarioEndConditions.TugWorkDone)) problems.Add("Условие «задание буксира выполнено» включено, но упражнение на буксире выключено.");
            if (RequiresOwnTug && IsMooring) problems.Add("Свой буксир: задание швартовки — для модели 45 м.");
            return;
        }
        if (waterArea != null && tugWork.BerthIn(waterArea) == null)
            problems.Add($"Буксир: в акватории нет причала № {tugWork.berthIndex}.");
        if (motionModel != ScenarioMotionModel.Manoeuvring3Dof)
            problems.Add("Буксир: колонки, причал и контакт с судном действуют только в модели 3 СС — выберите её в «Модель движения».");
        if (IsMooring) problems.Add("Буксир: задание швартовки своего судна и упражнение на буксире вместе не используются.");
        if (!RequiresOwnTug) problems.Add("Буксир: упражнение на буксире — выберите «Тип своего судна: свой буксир» (19.4б-2).");
        if (!Ends(ScenarioEndConditions.TugWorkDone)) problems.Add("Буксир: не включено условие завершения «задание буксира выполнено».");
        if (Ends(ScenarioEndConditions.Arrival)) problems.Add("Буксир: условие «прибытие в последнюю точку» завершит упражнение раньше задания.");
        if (tugWork.clientGapM <= 0f || tugWork.tugDistanceM <= 0f || tugWork.contactZoneM <= 0f || tugWork.atFendersGapM <= 0f ||
            tugWork.linesGapM <= 0f || tugWork.securedHoldS < 0f || tugWork.securedSpeedKnots <= 0f || tugWork.clearDistanceM <= 0f)
            problems.Add("Буксир: пороги задания должны быть больше 0.");
    }

    // Другие суда (B3)
    private void ValidateTraffic(List<string> problems)
    {
        if (traffic == null) return;
        var ids = new HashSet<string>();
        for (int i = 0; i < traffic.Count; i++)
        {
            TrafficShipDefinition d = traffic[i];
            if (d == null) { problems.Add($"Судно {i + 1}: пустая запись."); continue; }
            string tag = $"Судно {d.TargetId(i)} «{d.name}»";
            if (!ids.Add(d.TargetId(i))) problems.Add($"{tag}: идентификатор повторяется.");
            if (d.speedKnots < 0f) problems.Add($"{tag}: скорость отрицательная.");
            if (d.requiredCpaNm < 0f) problems.Add($"{tag}: CPA_доп отрицательная.");
            if (d.parameters != null && d.speedKnots > d.parameters.serviceSpeedKnots + 0.01f)
                problems.Add($"{tag}: скорость {d.speedKnots:F1} уз больше эксплуатационной {d.parameters.serviceSpeedKnots:F1} уз — будет ограничена.");
            if (d.motion == TrafficMotion.Route && (d.route == null || d.route.Count == 0))
                problems.Add($"{tag}: движение «по маршруту», но точек нет — пойдёт постоянным курсом.");

            TrafficStart st = TrafficPlanner.Resolve(d, initial);
            if (!string.IsNullOrEmpty(st.Problem)) problems.Add($"{tag}: {st.Problem}.");
            if (d.placement == TrafficPlacement.MeetingPoint)
            {
                if (d.tcpaMinutes <= 0f) problems.Add($"{tag}: TCPA должно быть больше 0.");
                if (d.cpaNm < 0f) problems.Add($"{tag}: CPA отрицательная.");
                if (st.SideAmbiguous && d.cpaNm > 0f)
                    problems.Add($"{tag}: сторона «{ScenarioText.Side(d.meetingSide)}» при таких курсах не определяется — выберите борт.");
                if (shipParameters != null && initial != null)
                {
                    double steady = ShipDynamicsModel.SteadySpeed(shipParameters, ScenarioText.SpeedFactor(initial.telegraph)) / ShipParameters.KnotsToMs;
                    if (Math.Abs(steady - initial.speedKnots) > 0.5)
                        problems.Add($"{tag}: точка встречи считается по начальной скорости своего судна {initial.speedKnots:F1} уз, а телеграф даёт {steady:F1} уз — CPA выполнится лишь приблизительно.");
                }
            }
            if (waterArea != null)
            {
                if (!waterArea.InBounds(st.Position)) problems.Add($"{tag}: начальная позиция вне района акватории.");
                float draft = d.parameters != null ? d.parameters.draft : 0f;
                if (waterArea.DepthAt(st.Position) <= draft) problems.Add($"{tag}: начальная позиция — на мели или на суше.");
            }
            if (initial != null)
            {
                double ownL = shipParameters != null ? shipParameters.length : 0.0;
                double tgtL = d.parameters != null ? d.parameters.length : 0.0;
                if ((st.Position - initial.position).Magnitude < 0.5 * (ownL + tgtL) + 50.0)
                    problems.Add($"{tag}: начальная позиция слишком близко к своему судну.");
            }
        }
    }
}
