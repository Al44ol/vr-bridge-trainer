// ============================================================
// ScenarioRunner.cs — Выполнение учебного сценария (B2)
//
// Жизненный цикл:
//   Idle (свободное плавание) → Load(сценарий) → Briefing → StartExercise() →
//   Running → End(причина) → Finished → Restart() / Load(другой) / FreeSail().
//
// Load — применяет сценарий: параметры судна, акваторию, погоду и время суток (B4; сразу, без
//   перехода), ставит судно в начальную точку (ShipNavigation.TeleportTo +
//   GroundingMonitor.ResetState), телеграф и руль. На брифинге модель стоит
//   (ShipNavigation.Paused): судно с ходом 15 уз не уплывает, пока читают текст.
// StartExercise — снова применяет начальные условия (если на брифинге трогали
//   штурвал или телеграф), снимает паузу, начинает журнал шины, ExerciseStarted.
// Running — в FixedUpdate (порядок −88: после шага модели −90, до GroundingMonitor −85,
//   поэтому события маршрута и мели одного шага получают одно время):
//   время упражнения += Δt, контроль маршрута (RouteTracker) → события шины,
//   лимит времени. Посадку и столкновение runner узнаёт из шины, а не от модулей.
// End — ExerciseEnded в шину, итог (ExerciseSummary); дальше — свободное плавание.
//
// Runner не содержит интерфейса: VR-панель и команды меню — ScenarioPanel,
// вывод событий в консоль — ExerciseConsoleLog (отдельный подписчик шины).
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-88)]
public class ScenarioRunner : MonoBehaviour
{
    public enum Phase { Idle, Briefing, Running, Finished }

    [Header("Сценарии (в редакторе собираются сами при входе в Play Mode)")]
    public List<ScenarioDefinition> scenarios = new List<ScenarioDefinition>();
    [Tooltip("Сценарий для гизмо маршрута в Scene View вне Play Mode (номер в списке)")]
    public int previewIndex;

    [Header("Сцена")]
    public ShipNavigation navigation;
    public GroundingMonitor grounding;
    public WaterAreaView waterArea;
    public WeatherController weather;

    public static ScenarioRunner Instance { get; private set; }

    public Phase CurrentPhase { get; private set; } = Phase.Idle;
    /// <summary>Загруженный сценарий (брифинг, выполнение, результаты).</summary>
    public ScenarioDefinition Current { get; private set; }
    /// <summary>Время упражнения, с (симуляционное).</summary>
    public double ExerciseTime { get; private set; }
    public RouteTracker Route { get; } = new RouteTracker();
    /// <summary>Итог последнего упражнения.</summary>
    public ExerciseSummary LastSummary { get; private set; }

    /// <summary>Сменилась фаза или сценарий (для панели).</summary>
    public event Action StateChanged;

    /// <summary>Сценарий для гизмо: загруженный, иначе — выбранный для предпросмотра.</summary>
    public ScenarioDefinition PreviewScenario =>
        Current != null ? Current :
        scenarios != null && scenarios.Count > 0 ? scenarios[Mathf.Clamp(previewIndex, 0, scenarios.Count - 1)] : null;

    void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("[Сценарий] В сцене несколько ScenarioRunner — используется первый.", this);
        else Instance = this;
    }

    void OnEnable() => ExerciseEvents.Raised += OnExerciseEvent;

    void OnDisable()
    {
        ExerciseEvents.Raised -= OnExerciseEvent;
        if (navigation != null) navigation.Paused = false;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (navigation == null) navigation = FindAnyObjectByType<ShipNavigation>();
        if (grounding == null && navigation != null) grounding = navigation.GetComponent<GroundingMonitor>();
        if (waterArea == null) waterArea = FindAnyObjectByType<WaterAreaView>();
        if (weather == null) weather = FindAnyObjectByType<WeatherController>();
        scenarios.RemoveAll(s => s == null);
    }

    // ─────────── Управление ───────────
    /// <summary>Загрузить сценарий: начальные условия, погода, брифинг (судно стоит).</summary>
    public void Load(ScenarioDefinition scenario)
    {
        if (scenario == null) return;
        if (CurrentPhase == Phase.Running) End(ScenarioEndReason.Aborted, true);

        foreach (string p in scenario.Validate())
            Debug.LogWarning($"[Сценарий] «{scenario.title}»: {p}", scenario);

        Current = scenario;
        if (navigation != null) navigation.Paused = true;
        ApplyScenario(scenario);
        CurrentPhase = Phase.Briefing;
        ExerciseTime = 0;
        ExerciseEvents.Time = 0;
        Publish(new ExerciseEvent(ExerciseEventType.ScenarioLoaded) { Subject = scenario.title });
        StateChanged?.Invoke();
    }

    /// <summary>«Начать»: начальные условия заново, снять паузу, начало упражнения.</summary>
    public void StartExercise()
    {
        if (CurrentPhase != Phase.Briefing || Current == null) return;
        PlaceShip(Current);
        if (navigation != null) navigation.Paused = false;

        ExerciseTime = 0;
        ExerciseEvents.BeginExercise(Current.id);
        Route.Reset(Current.route);
        CurrentPhase = Phase.Running;
        LastSummary = null;
        Publish(new ExerciseEvent(ExerciseEventType.ExerciseStarted) { Subject = Current.title });
        TrackRoute(0.0);   // начальное положение относительно маршрута (может быть уже вне коридора)
        StateChanged?.Invoke();
    }

    /// <summary>Перезапуск текущего сценария (снова брифинг).</summary>
    public void Restart()
    {
        if (Current != null) Load(Current);
    }

    /// <summary>Досрочное завершение (инструктор).</summary>
    public void Abort()
    {
        if (CurrentPhase == Phase.Running) End(ScenarioEndReason.Aborted, true);
    }

    /// <summary>Свободное плавание: без сценария, судно на ходу.</summary>
    public void FreeSail()
    {
        if (CurrentPhase == Phase.Running) End(ScenarioEndReason.Aborted, true);
        if (navigation != null) navigation.Paused = false;
        Current = null;
        CurrentPhase = Phase.Idle;
        StateChanged?.Invoke();
    }

    // ─────────── Выполнение ───────────
    void FixedUpdate()
    {
        if (CurrentPhase != Phase.Running || navigation == null || navigation.Paused) return;
        double dt = Time.fixedDeltaTime;
        ExerciseTime += dt;
        ExerciseEvents.Time = ExerciseTime;

        TrackRoute(dt);
        if (CurrentPhase != Phase.Running) return;

        if (Current.Ends(ScenarioEndConditions.TimeLimit) && Current.timeLimitSeconds > 0f && ExerciseTime >= Current.timeLimitSeconds)
            End(ScenarioEndReason.TimeLimit, false);
    }

    private void TrackRoute(double dt)
    {
        ShipDynamicsModel m = navigation != null ? navigation.Model : null;
        if (m == null || !Route.Valid) return;

        foreach (RouteEvent e in Route.Update(new WorldPosition(m.East, m.North), ExerciseTime, dt))
        {
            switch (e.Kind)
            {
                case RouteEventKind.WaypointPassed:
                    Publish(new ExerciseEvent(ExerciseEventType.WaypointPassed) { Index = e.Index, Subject = e.Name, Value = e.Xte, Value2 = e.Value2 });
                    break;
                case RouteEventKind.CorridorExit:
                    Publish(new ExerciseEvent(ExerciseEventType.CorridorExit) { Index = e.Index, Subject = e.Name, Value = e.Xte, Value2 = e.Value2 });
                    break;
                case RouteEventKind.CorridorReturn:
                    Publish(new ExerciseEvent(ExerciseEventType.CorridorReturn) { Index = e.Index, Subject = e.Name, Value = e.Xte, Value2 = e.Value2 });
                    break;
                case RouteEventKind.Arrived:
                    if (Current.Ends(ScenarioEndConditions.Arrival)) End(ScenarioEndReason.Arrived, false);
                    return;
            }
        }
    }

    // Посадка и столкновение — из шины (источник — GroundingMonitor, в B3 — модуль целей)
    private void OnExerciseEvent(ExerciseEvent e)
    {
        if (CurrentPhase != Phase.Running || Current == null) return;
        if (e.Type == ExerciseEventType.Grounded && Current.Ends(ScenarioEndConditions.Grounding))
            End(ScenarioEndReason.Grounding, false);
        else if (e.Type == ExerciseEventType.Collision && Current.Ends(ScenarioEndConditions.Collision))
            End(ScenarioEndReason.Collision, false);
    }

    private void End(ScenarioEndReason reason, bool byInstructor)
    {
        if (CurrentPhase != Phase.Running) return;
        CurrentPhase = Phase.Finished;
        Publish(new ExerciseEvent(ExerciseEventType.ExerciseEnded)
        {
            EndReason = reason, ByInstructor = byInstructor, Subject = Current != null ? Current.title : "",
            Value = Route.PassedCount, Value2 = Route.Valid ? Route.WaypointCount - 1 : 0
        });
        ExerciseEvents.EndExercise();
        LastSummary = ExerciseSummary.From(Current, ExerciseEvents.Log, Route);
        StateChanged?.Invoke();
    }

    // ─────────── Начальные условия ───────────
    private void ApplyScenario(ScenarioDefinition s)
    {
        if (navigation != null && s.shipParameters != null) navigation.parameters = s.shipParameters;
        if (waterArea != null && s.waterArea != null && waterArea.area != s.waterArea)
        {
            waterArea.area = s.waterArea;
            waterArea.Rebuild();
        }
        // B4: погода и время суток — раздельно (ночь + штиль, ночь + шторм и т. д.)
        if (weather != null)
        {
            weather.SetWeather(s.weather, instant: true);
            weather.SetTimeOfDay(s.timeOfDay, instant: true);
        }
        if (s.visibilityMeters > 0f)
            Debug.Log($"[Сценарий] Видимость {s.visibilityMeters:F0} м будет применяться с C4; сейчас — по пресету погоды.");
        PlaceShip(s);
    }

    private void PlaceShip(ScenarioDefinition s)
    {
        if (navigation == null || s.initial == null) return;
        OwnShipInitialState init = s.initial;
        navigation.TeleportTo(init.position.East, init.position.North, init.headingDeg, init.speedKnots, init.rudderDeg);
        if (navigation.telegraph != null) navigation.telegraph.SetSpeedFactor((float)ScenarioText.SpeedFactor(init.telegraph));
        navigation.SetRudderOrder(init.rudderDeg);
        if (grounding != null) grounding.ResetState();
    }

    // Событие с состоянием своего судна
    private void Publish(ExerciseEvent e)
    {
        if (navigation != null && navigation.Model != null)
        {
            e.Position = navigation.PositionEastNorth;
            e.HeadingDeg = navigation.Heading;
            e.SpeedKnots = navigation.SpeedKnots;
        }
        ExerciseEvents.Publish(e);
    }
}
