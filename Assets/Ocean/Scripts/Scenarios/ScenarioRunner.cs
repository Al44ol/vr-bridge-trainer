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
// D1: Pause / Resume (инструктор) — во время упражнения модель стоит (ShipNavigation.Paused, как
//   на брифинге): своё судно, цели, время упражнения, запись и оценка; волны и качка идут.
//   События ExercisePaused / ExerciseResumed (длительность паузы по часам). Конец упражнения
//   на паузе сначала снимает паузу.
//
// Запрос 15: Load берёт сценарий для выбранного типа судна (ShipTypeSwitcher.VariantFor): на катере —
//   копия «-FB» с пересчитанной начальной скоростью; Restart загружает тот же исходник заново.
//
// 19.2б-3: сценарий швартовки (ScenarioDefinition.mooring) — Load ставит модель движения сценария
//   (ScenarioMotionModel; «как выбрано» возвращает модель, бывшую до сценария с заданной моделью);
//   в FixedUpdate этапы задания (MooringTaskTracker по BerthReadout и состоянию MooringController) →
//   события MooringStage; «ошвартован» завершает упражнение (ScenarioEndReason.Moored). На катере
//   сценарий швартовки грузится на модели 45 м (тип судна переключается сам).
// 19.4б-2: сценарий на своём буксире (ScenarioDefinition.ownShipType = Tug, 19-03) ставит тип «буксир» сам
//   (остальные сценарии на буксире — модель 45 м); финиш «задание буксира выполнено» — по TugWorkController.
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
    /// <summary>19.2б-3: этапы задания швартовки (null — сценарий не швартовка).</summary>
    public MooringTaskTracker MooringTask { get; private set; }

    /// <summary>Сменилась фаза или сценарий (для панели).</summary>
    public event Action StateChanged;

    /// <summary>Пауза инструктора (D1): модель, цели и время упражнения стоят.</summary>
    public bool IsPaused { get; private set; }
    /// <summary>Пауза поставлена или снята (D1; фаза при этом не меняется).</summary>
    public event Action PauseChanged;
    private float pausedAtRealtime;

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
        if (navigation != null) navigation.MotionModelChanged -= OnMotionModelChanged;
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (navigation == null) navigation = FindAnyObjectByType<ShipNavigation>();
        if (grounding == null && navigation != null) grounding = navigation.GetComponent<GroundingMonitor>();
        if (waterArea == null) waterArea = FindAnyObjectByType<WaterAreaView>();
        if (weather == null) weather = FindAnyObjectByType<WeatherController>();
        scenarios.RemoveAll(s => s == null);
        if (navigation != null) navigation.MotionModelChanged += OnMotionModelChanged;
    }

    // ─────────── Управление ───────────
    /// <summary>Загрузить сценарий: начальные условия, погода, брифинг (судно стоит).</summary>
    public void Load(ScenarioDefinition scenario)
    {
        if (scenario == null) return;
        if (CurrentPhase == Phase.Running) End(ScenarioEndReason.Aborted, true);
        ShipTypeSwitcher ship = ShipTypeSwitcher.Instance;
        // 19.2б-3: швартовка — только модель 45 м (на катере силы причала выключены): тип судна — сам.
        // Current сбрасывается, чтобы переключатель не перезагрузил прежний сценарий.
        if (ScenarioVariants.SourceOf(scenario).IsMooring && ship != null && ship.Type == ShipType.FastBoat)
        {
            Debug.Log($"[Сценарий] «{scenario.title}» — сценарий швартовки, только на модели 45 м: тип судна переключён.");
            Current = null;
            CurrentPhase = Phase.Idle;
            ship.SetType(ShipType.Model45m);
        }
        // 19.4б-2: сценарий на своём буксире (19-03) — тип судна «буксир» ставится сам
        bool needTug = ScenarioVariants.SourceOf(scenario).RequiresOwnTug;
        if (needTug && ship != null && ship.Type != ShipType.Tug)
        {
            if (!ship.TugReady)
            {
                Debug.LogWarning($"[Сценарий] «{scenario.title}» выполняется на своём буксире, а буксир не построить (нет стойки катера) — " +
                                 "запустите Tools/VR Bridge/Setup Fast Boat и сохраните сцену. Сценарий не загружен.");
                return;
            }
            Debug.Log($"[Сценарий] «{scenario.title}» — упражнение на своём буксире: тип судна переключён.");
            Current = null;
            CurrentPhase = Phase.Idle;
            ship.SetType(ShipType.Tug);
        }
        // 19.4а: на своём буксире остальные сценарии — на модели 45 м
        if (!needTug && ship != null && ship.Type == ShipType.Tug)
        {
            Debug.Log($"[Сценарий] «{scenario.title}» — сценарий для модели 45 м: тип судна переключён с буксира.");
            Current = null;
            CurrentPhase = Phase.Idle;
            ship.SetType(ShipType.Model45m);
        }
        // Запрос 15: на катере — вариант сценария «-FB» (исходный ассет не меняется)
        scenario = ship != null ? ship.VariantFor(scenario) : ScenarioVariants.SourceOf(scenario);

        foreach (string p in scenario.Validate())
            Debug.LogWarning($"[Сценарий] «{scenario.title}»: {p}", scenario);

        Current = scenario;
        MooringTask = scenario.IsMooring ? new MooringTaskTracker(scenario.mooring) : null;
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
        MooringTask?.Reset();
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

    /// <summary>Пауза инструктора (D1): только во время упражнения.</summary>
    public void Pause()
    {
        if (CurrentPhase != Phase.Running || IsPaused || navigation == null) return;
        IsPaused = true;
        navigation.Paused = true;
        pausedAtRealtime = Time.realtimeSinceStartup;
        Publish(new ExerciseEvent(ExerciseEventType.ExercisePaused) { ByInstructor = true });
        PauseChanged?.Invoke();
    }

    /// <summary>Снять паузу инструктора (D1).</summary>
    public void Resume()
    {
        if (!IsPaused) return;
        IsPaused = false;
        if (navigation != null) navigation.Paused = false;
        Publish(new ExerciseEvent(ExerciseEventType.ExerciseResumed) { ByInstructor = true, Value = Time.realtimeSinceStartup - pausedAtRealtime });
        PauseChanged?.Invoke();
    }

    public void TogglePause()
    {
        if (IsPaused) Resume(); else Pause();
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
        TrackMooring(dt);
        if (CurrentPhase != Phase.Running) return;
        // 19.4б-2: упражнение на буксире — все четыре задания (TugWorkController, шаг — до шага модели)
        TugWorkController tw = TugWorkController.Instance;
        if (Current.IsTugWork && tw != null && tw.Scenario == Current && tw.Work != null && tw.Work.Finished && Current.Ends(ScenarioEndConditions.TugWorkDone))
        {
            End(ScenarioEndReason.TugWorkDone, false);
            return;
        }

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

    // 19.2б-3: этапы задания швартовки — по причалу задания (зазоры, угол — BerthReadout) и состоянию
    // MooringController (касание кранцев, концы — только если считается именно этот причал)
    private void TrackMooring(double dt)
    {
        ShipDynamicsModel m = navigation != null ? navigation.Model : null;
        if (MooringTask == null || m == null) return;
        BerthArea berth = Current.mooring.BerthIn(Current.waterArea);
        var x = new MooringTaskInput
        {
            Readout = BerthReadout.Compute(berth, navigation.parameters, m),
            SpeedOverGroundKn = Math.Sqrt(m.VelocityEast * m.VelocityEast + m.VelocityNorth * m.VelocityNorth) / ShipParameters.KnotsToMs
        };
        MooringController mc = MooringController.Instance;
        if (mc != null && berth != null && mc.ActiveBerth == berth)
        {
            x.FenderContact = mc.System.InContact;
            MooringTasks.LinesFast(mc.System.Lines, out x.BowLineFast, out x.SternLineFast);
        }
        foreach (MooringStage st in MooringTask.Update(x, ExerciseTime, dt))
            Publish(new ExerciseEvent(ExerciseEventType.MooringStage)
            {
                Index = (int)st, Subject = MooringTasks.Title(st),
                Value = x.Readout.Valid ? Math.Max(x.Readout.BowGapM, x.Readout.SternGapM) : double.NaN, Value2 = x.SpeedOverGroundKn
            });
        if (MooringTask.Moored && Current.Ends(ScenarioEndConditions.Moored)) End(ScenarioEndReason.Moored, false);
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
        if (IsPaused) Resume();   // D1: завершение на паузе — сначала снять паузу (пара событий в журнале)
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
        // Судно сценария; без него — судно выбранного типа (запрос 15), иначе — назначенное в сцене
        ShipParameters ship = s.shipParameters != null ? s.shipParameters
            : ShipTypeSwitcher.Instance != null ? ShipTypeSwitcher.Instance.ActiveParameters : null;
        if (navigation != null && ship != null) navigation.parameters = ship;
        ApplyMotionModel(s);
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
        // C4: видимость сценария (0 — по погоде); плотность тумана сцены ρ = 1,73/V
        if (weather != null) weather.SetVisibility(s.visibilityMeters, instant: true);
        if (s.visibilityMeters > 0f)
            Debug.Log($"[Сценарий] Видимость {s.visibilityMeters:F0} м ({s.visibilityMeters / 1852f:F2} мили), плотность тумана {WeatherVisibility.DensityFor(s.visibilityMeters):F5}.");
        PlaceShip(s);
    }

    // 19.2б-3: модель движения сценария. Модель до первого сценария с заданной моделью запоминается
    // и возвращается при загрузке сценария «как выбрано»; если её сменили сами (меню, «Судно к причалу») —
    // остаётся выбранная.
    private ShipNavigation.MotionChoice? motionBeforeScenario;
    private bool settingMotion;

    private void ApplyMotionModel(ScenarioDefinition s)
    {
        if (navigation == null) return;
        settingMotion = true;
        try
        {
            if (s.motionModel == ScenarioMotionModel.AsSelected)
            {
                if (motionBeforeScenario.HasValue) navigation.SetMotionModel(motionBeforeScenario.Value);
                motionBeforeScenario = null;
                return;
            }
            if (!motionBeforeScenario.HasValue) motionBeforeScenario = navigation.motionModel;
            navigation.SetMotionModel(s.motionModel == ScenarioMotionModel.Nomoto
                ? ShipNavigation.MotionChoice.Course1Dof : ShipNavigation.MotionChoice.Manoeuvring3Dof);
        }
        finally { settingMotion = false; }
    }

    private void OnMotionModelChanged(bool to3Dof)
    {
        if (!settingMotion) motionBeforeScenario = null;
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
