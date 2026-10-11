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
//   • набор критериев оценки — заготовка для C2.
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
    All = Arrival | Grounding | Collision | TimeLimit
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

    public RouteWaypoint() { }

    public RouteWaypoint(string name, WorldPosition position, float xteLimit)
    {
        this.name = name;
        this.position = position;
        this.xteLimit = xteLimit;
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

    [Header("Акватория и судно")]
    public WaterArea waterArea;
    [Tooltip("Пусто — параметры, назначенные в сцене")]
    public ShipParameters shipParameters;

    [Header("Начальные условия своего судна")]
    public OwnShipInitialState initial = new OwnShipInitialState();

    [Header("Условия плавания")]
    public WeatherPresetId weather = WeatherPresetId.Moderate;
    [Tooltip("День / сумерки / ночь — независимо от погоды (B4)")]
    public TimeOfDay timeOfDay = TimeOfDay.Day;
    [Tooltip("Видимость, м; 0 — по пресету погоды. Применяется в C4")]
    public float visibilityMeters;

    [Header("Маршрут")]
    public List<RouteWaypoint> route = new List<RouteWaypoint>();

    [Header("Другие суда (B3)")]
    public List<TrafficShipDefinition> traffic = new List<TrafficShipDefinition>();

    [Header("Завершение")]
    [Tooltip("Лимит времени, с; 0 — без лимита")]
    public float timeLimitSeconds = 1800f;
    public ScenarioEndConditions endConditions = ScenarioEndConditions.All;

    [Header("Оценка (C2)")]
    public AssessmentCriteria assessment;

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
        ValidateTraffic(problems);
        return problems;
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
