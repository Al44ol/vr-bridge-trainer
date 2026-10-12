// ============================================================
// TrafficController.cs — Другие суда в сцене (B3)
//
// Объект Traffic (корень сцены). По сценарию ScenarioRunner (подписка на
// StateChanged — правок в runner не нужно):
//   • загрузка (брифинг) — цели создаются и ставятся в начальные позиции
//     (TrafficPlanner: абсолютная, от своего судна, «точка встречи»); стоят, пока
//     стоит своё судно (ShipNavigation.Paused);
//   • «Начать» — начальные позиции заново; выполнение и свободное плавание
//     после завершения — цели идут (TrafficVessel.Step в FixedUpdate);
//   • свободное плавание без сценария — цели убираются.
// Порядок −86: после шага своего судна (−90), времени упражнения (−88) и
// действий обучающегося (−87); до контроля мели (−85) и записи (−84) — строка
// targets.csv и события столкновения получают время этого шага.
// Каждая цель регистрируется в ExerciseTargets (C1) — ExerciseRecorder пишет
// targets.csv сам.
// Столкновение: пересечение контура своего судна (GroundingMonitor.TryGetFootprint —
// тот же контур L × B, что для мели) с контуром цели (HullContact) →
// ExerciseEventType.Collision в шину (Subject — id цели); ScenarioRunner
// завершает упражнение, если условие включено. Цель ставит «Стоп», звук удара
// и тряска камеры — как при посадке на мель. Физики удара нет.
// Отладка (меню A, «Отладка», вне упражнения): «Проверка огней: облёт цели» —
// неподвижная цель в 0,3 мили, своё судно на паузе переставляется по кругу
// шагами 15° раз в 3 с носом на цель (в консоли — какие огни должны быть видны);
// «Огни, цель: < 0,25 / 0,5 / 1 / 3 / 6 миль >» (стрелки — ближе/дальше) — неподвижная цель прямо по носу,
// идущая навстречу (видны топовые и оба бортовых; с C4 0,25 и 0,5 мили — проверка, где корпус теряется в тумане).
// C4 — туманные сигналы целей (FogSignalSystem, Core; тот же класс — в прогонах ExerciseSimulation):
// при ограниченной видимости (WeatherController.VisibilityM < 2 миль) каждая цель подаёт «—» на ходу
// или «— —» без хода раз в 110 с; звук доходит с задержкой d/340 с — в этот момент он звучит с мачты цели
// (TrafficShipView.PlaySignal: громкость и фильтр по дистанции) и публикуется TargetSoundSignal.
// Часы сигналов идут, только пока судно не на паузе (брифинг).
// ============================================================
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(-86)]
public class TrafficController : MonoBehaviour
{
    [Header("Сцена")]
    public ScenarioRunner runner;
    public ShipNavigation navigation;
    public GroundingMonitor grounding;
    public GroundingCameraShake cameraShake;

    [Header("Параметры по умолчанию для типов (если в сценарии не заданы)")]
    public ShipParameters cargoParameters;
    public ShipParameters tankerParameters;
    public ShipParameters smallParameters;

    [Header("Материалы: борт по типам и общие (подводная часть, палуба, надстройка, тёмные, акцент)")]
    public Material cargoHull;
    public Material tankerHull;
    public Material smallHull;
    public Material bootMaterial, deckMaterial, houseMaterial, darkMaterial, accentMaterial;

    [Header("Столкновение")]
    [Tooltip("Пусто — процедурный удар (GroundingSounds)")]
    public AudioClip collisionClip;
    [Range(0f, 1f)] public float collisionVolume = 1f;
    [Tooltip("После контакта цель ставит «Стоп»")]
    public bool stopTargetOnCollision = true;

    [Header("Туманные сигналы целей (C4)")]
    [Tooltip("Подавать туманные сигналы при ограниченной видимости (правило 35)")]
    public bool fogSignals = true;
    [Tooltip("Ограниченная видимость: меньше, мили")]
    public float restrictedVisibilityNm = 2f;
    [Range(0f, 1f)] public float fogSignalVolume = 1f;

    [Header("Отладка: проверка огней")]
    public float orbitRadiusNm = 0.3f;
    public float orbitStepDeg = 15f;
    public float orbitStepSeconds = 3f;

    [Header("Состояние (только чтение)")]
    [SerializeField] private int targetCount;
    [SerializeField] private float stepTimeUs;

    public IReadOnlyList<TrafficVessel> Vessels => vessels;
    public static TrafficController Instance { get; private set; }

    private readonly List<TrafficVessel> vessels = new List<TrafficVessel>();
    private readonly List<TrafficShipView> views = new List<TrafficShipView>();
    private readonly List<HullContact> contacts = new List<HullContact>();
    private ScenarioDefinition spawnedFor;
    private ScenarioRunner.Phase lastPhase = ScenarioRunner.Phase.Idle;
    private ScenarioRunner subscribed;
    private AudioSource impactSource;
    private readonly List<BridgeMenuCommand> menuCommands = new List<BridgeMenuCommand>();
    private TrafficVessel debugTarget;
    private TrafficShipView debugView;
    private Coroutine orbit;
    private int debugDistanceIndex = -1;
    private static readonly float[] DebugDistancesNm = { 0.25f, 0.5f, 1f, 3f, 6f };
    private static readonly string[] DebugDistanceTitles = { "0,25 мили", "0,5 мили", "1 милю", "3 мили", "6 миль" };
    // C4: туманные сигналы
    private readonly FogSignalSystem fogSystem = new FogSignalSystem();
    private readonly List<FogSignalSource> fogSources = new List<FogSignalSource>();
    private readonly List<HeardSignal> fogHeard = new List<HeardSignal>();
    private double fogClock;

    void Awake() => Instance = this;

    void OnEnable()
    {
        menuCommands.Add(BridgeMenu.Register("Отладка",
            () => orbit != null ? "Проверка огней: остановить облёт" : "Проверка огней: облёт цели",
            ToggleOrbit, () => CanDebug, instructorOnly: true));
        // «< цель: 3 мили >» — стрелками ближе/дальше; левее 1 мили — цели нет
        menuCommands.Add(BridgeMenu.RegisterStepper("Отладка",
            () => debugDistanceIndex < 0 ? "Огни, цель: нет" : $"Огни, цель: {DebugDistanceTitles[debugDistanceIndex]}",
            () => SetDebugDistance(debugDistanceIndex - 1), () => SetDebugDistance(debugDistanceIndex + 1),
            () => CanDebug && orbit == null, instructorOnly: true));
        Subscribe();
    }

    void OnDisable()
    {
        foreach (BridgeMenuCommand c in menuCommands) BridgeMenu.Unregister(c);
        menuCommands.Clear();
        if (subscribed != null) subscribed.StateChanged -= OnStateChanged;
        subscribed = null;
        Clear();
        RemoveDebugTarget();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (runner == null) runner = ScenarioRunner.Instance != null ? ScenarioRunner.Instance : FindAnyObjectByType<ScenarioRunner>();
        if (navigation == null) navigation = FindAnyObjectByType<ShipNavigation>();
        if (grounding == null && navigation != null) grounding = navigation.GetComponent<GroundingMonitor>();
        if (cameraShake == null) cameraShake = FindAnyObjectByType<GroundingCameraShake>();
        impactSource = gameObject.AddComponent<AudioSource>();
        impactSource.playOnAwake = false;
        impactSource.spatialBlend = 0f;
        if (collisionClip == null) collisionClip = GroundingSounds.Impact();
        Subscribe();
        OnStateChanged();
    }

    private void Subscribe()
    {
        if (runner == null || subscribed == runner) return;
        if (subscribed != null) subscribed.StateChanged -= OnStateChanged;
        runner.StateChanged += OnStateChanged;
        subscribed = runner;
    }

    private bool CanDebug => navigation != null && navigation.Model != null &&
                             (runner == null || runner.CurrentPhase != ScenarioRunner.Phase.Running);

    // ─────────── Жизненный цикл по сценарию ───────────
    private void OnStateChanged()
    {
        if (runner == null) return;
        ScenarioRunner.Phase phase = runner.CurrentPhase;
        ScenarioDefinition s = runner.Current;
        if (phase == ScenarioRunner.Phase.Idle || s == null) Clear();
        else if (phase == ScenarioRunner.Phase.Briefing || s != spawnedFor) Spawn(s);
        else if (phase == ScenarioRunner.Phase.Running && lastPhase == ScenarioRunner.Phase.Briefing) ResetAll();
        lastPhase = phase;
    }

    private void Spawn(ScenarioDefinition s)
    {
        Clear();
        if (orbit != null) { StopCoroutine(orbit); orbit = null; }   // судно уже поставлено сценарием — не возвращаем
        debugDistanceIndex = -1;
        RemoveDebugTarget();
        spawnedFor = s;
        if (s.traffic == null) return;
        for (int i = 0; i < s.traffic.Count; i++)
        {
            TrafficShipDefinition d = s.traffic[i];
            if (d == null) continue;
            ShipParameters p = d.parameters != null ? d.parameters : DefaultParameters(d.type);
            if (p == null)
            {
                Debug.LogWarning($"[Суда] {d.TargetId(i)} «{d.name}»: нет параметров судна (ни в сценарии, ни по умолчанию для типа) — пропущено.", this);
                continue;
            }
            TrafficStart st = TrafficPlanner.Resolve(d, s.initial);
            if (!string.IsNullOrEmpty(st.Problem)) Debug.LogWarning($"[Суда] {d.TargetId(i)}: {st.Problem}", this);
            var v = new TrafficVessel(i, d.TargetId(i), d, p, st);
            vessels.Add(v);
            contacts.Add(new HullContact());
            views.Add(CreateView(v, $"{v.TargetId} {d.name}"));
            ExerciseTargets.Register(v);
        }
        targetCount = vessels.Count;
        if (vessels.Count > 0)
            Debug.Log($"[Суда] «{s.title}»: {vessels.Count} — " + string.Join("; ", vessels.ConvertAll(Describe)));
    }

    private void ResetAll()
    {
        if (spawnedFor == null) return;
        foreach (TrafficVessel v in vessels)
            v.Reset(TrafficPlanner.Resolve(v.Definition, spawnedFor.initial));
        foreach (HullContact c in contacts) c.Reset();
        fogSystem.Reset();
        fogClock = 0;
    }

    private void Clear()
    {
        foreach (TrafficVessel v in vessels) ExerciseTargets.Unregister(v);
        foreach (TrafficShipView view in views) if (view != null) Destroy(view.gameObject);
        vessels.Clear();
        views.Clear();
        contacts.Clear();
        spawnedFor = null;
        targetCount = 0;
        fogSystem.Reset();
        fogClock = 0;
    }

    private TrafficShipView CreateView(TrafficVessel v, string objectName)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        var view = go.AddComponent<TrafficShipView>();
        view.Init(v, Materials(v.Type));
        return view;
    }

    private ShipParameters DefaultParameters(TrafficVesselType t) =>
        t == TrafficVesselType.Tanker ? tankerParameters : t == TrafficVesselType.Small ? smallParameters : cargoParameters;

    private Material[] Materials(TrafficVesselType t)
    {
        Material hull = t == TrafficVesselType.Tanker ? tankerHull : t == TrafficVesselType.Small ? smallHull : cargoHull;
        return new[] { hull, bootMaterial, deckMaterial, houseMaterial, darkMaterial, accentMaterial };
    }

    private static string Describe(TrafficVessel v) =>
        $"{v.TargetId} {ScenarioText.VesselType(v.Type)} ({v.Model.Parameters.name}, L {v.Model.Parameters.length:F0} м) " +
        $"{v.Start.Position}, курс {v.Start.HeadingDeg:000}°, {v.Start.SpeedKnots:F1} уз{(v.Route != null ? $", маршрут {v.Route.Points.Count - 1} т." : "")}";

    // ─────────── Шаг ───────────
    void FixedUpdate()
    {
        if (vessels.Count == 0 && debugTarget == null) return;
        bool paused = navigation == null || navigation.Paused || navigation.Model == null;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        double dt = Time.fixedDeltaTime;

        foreach (TrafficVessel v in vessels)
        {
            if (paused) v.KeepPrevious();
            else v.Step(dt);
        }
        if (debugTarget != null) debugTarget.KeepPrevious();   // отладочная цель стоит

        if (!paused)
        {
            CheckCollisions();
            UpdateFogSignals(dt);
        }

        float us = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1e6 / System.Diagnostics.Stopwatch.Frequency);
        stepTimeUs = stepTimeUs <= 0f ? us : stepTimeUs + (us - stepTimeUs) * 0.02f;
    }

    private void CheckCollisions()
    {
        if (vessels.Count == 0) return;
        ShipDynamicsModel own = navigation.Model;
        HullFootprint ownHull;
        if (grounding == null || !grounding.TryGetFootprint(out ownHull))
        {
            ShipParameters p = navigation.parameters;
            ownHull = HullFootprint.FromShip(own.East, own.North, own.Psi, 0.0, 0.0, p.length, p.beam);
        }
        for (int i = 0; i < vessels.Count; i++)
        {
            TrafficVessel v = vessels[i];
            if (!contacts[i].Update(ownHull, v.Footprint)) continue;

            // Относительная скорость и курсовой угол цели в момент контакта
            v.Velocity(out double ve, out double vn);
            double oe = own.U * System.Math.Sin(own.Psi), on = own.U * System.Math.Cos(own.Psi);
            double rel = System.Math.Sqrt((ve - oe) * (ve - oe) + (vn - on) * (vn - on)) / ShipParameters.KnotsToMs;
            double bearing = CpaCalculator.Bearing(v.Model.East - own.East, v.Model.North - own.North);
            double q = CpaCalculator.RelativeBearing(bearing, own.Psi * Mathf.Rad2Deg);
            ExerciseEvents.Publish(new ExerciseEvent(ExerciseEventType.Collision)
            {
                Subject = v.TargetId, Index = v.Index, Value = rel, Value2 = q,
                Position = new WorldPosition(own.East, own.North),
                HeadingDeg = navigation.Heading, SpeedKnots = navigation.SpeedKnots
            });
            if (stopTargetOnCollision) v.EngineStopped = true;
            if (impactSource != null && collisionClip != null) impactSource.PlayOneShot(collisionClip, collisionVolume);
            if (cameraShake != null) cameraShake.Impact(Mathf.Clamp01((float)(0.4 + rel / 10.0)));
        }
    }

    // ─────────── Туманные сигналы целей (C4) ───────────
    private void UpdateFogSignals(double dt)
    {
        if (!fogSignals || vessels.Count == 0 || navigation == null || navigation.Model == null) return;
        fogClock += dt;
        WeatherController weather = WeatherController.Instance;
        bool restricted = weather != null && FogSignals.Restricted(weather.VisibilityM, restrictedVisibilityNm);
        fogSources.Clear();
        foreach (TrafficVessel v in vessels)
            fogSources.Add(new FogSignalSource { Id = v.TargetId, Index = v.Index, Position = v.Position, SpeedKnots = v.SpeedKnots, LengthM = v.Model.Parameters.length });
        ShipDynamicsModel own = navigation.Model;
        var ownPos = new WorldPosition(own.East, own.North);
        fogSystem.Update(fogClock, restricted, ownPos, navigation.Heading, fogSources, fogHeard);
        foreach (HeardSignal h in fogHeard)
        {
            int i = vessels.FindIndex(v => v.TargetId == h.Id);
            if (i >= 0 && i < views.Count && views[i] != null)
            {
                double range = FogSignals.AudibleRangeNm(h.LengthM) * NavigationLights.MetersPerNm;
                double d = (vessels[i].Position - ownPos).Magnitude;
                views[i].PlaySignal(h.Kind, (float)FogSignals.FrequencyHz(h.LengthM),
                                    (float)FogSignals.Loudness(d, range) * fogSignalVolume, (float)FogSignals.LowPassHz(d, range));
            }
            ExerciseEvents.Publish(new ExerciseEvent(ExerciseEventType.TargetSoundSignal)
            {
                Subject = h.Id, Index = (int)h.Kind, Value = h.DistanceM, Value2 = h.QOwnDeg,
                Position = ownPos, HeadingDeg = navigation.Heading, SpeedKnots = navigation.SpeedKnots
            });
        }
    }

    // ─────────── Отладка: проверка огней ───────────
    private void SetDebugDistance(int index)
    {
        index = Mathf.Clamp(index, -1, DebugDistancesNm.Length - 1);
        if (index == debugDistanceIndex) return;
        debugDistanceIndex = index;
        if (debugDistanceIndex < 0)
        {
            RemoveDebugTarget();
            return;
        }
        // Неподвижная цель прямо по носу, носом на своё судно (видны топовые и оба бортовых)
        ShipDynamicsModel own = navigation.Model;
        double d = DebugDistancesNm[debugDistanceIndex] * NavigationLights.MetersPerNm;
        var pos = new WorldPosition(own.East + d * System.Math.Sin(own.Psi), own.North + d * System.Math.Cos(own.Psi));
        double headingDeg = (own.Psi * Mathf.Rad2Deg + 180.0) % 360.0;
        SpawnDebugTarget(pos, headingDeg);
        Debug.Log($"[Огни] Проверка: сухогруз прямо по носу в {DebugDistanceTitles[debugDistanceIndex]}, идёт навстречу — " +
                  $"должны быть видны два топовых и оба бортовых огня (если позволяет видимость; K = {(NavLightField.Instance != null ? NavLightField.Instance.Transmissivity : NavigationLights.ClearTransmissivity):F3}).");
    }

    private void ToggleOrbit()
    {
        if (orbit != null)
        {
            StopCoroutine(orbit);
            EndOrbit();
            return;
        }
        orbit = StartCoroutine(Orbit());
    }

    private double orbitReturnE, orbitReturnN, orbitReturnPsi;
    private bool orbitWasPaused;

    private IEnumerator Orbit()
    {
        ShipDynamicsModel own = navigation.Model;
        orbitReturnE = own.East; orbitReturnN = own.North; orbitReturnPsi = own.Psi;
        orbitWasPaused = navigation.Paused;
        navigation.Paused = true;

        double r = orbitRadiusNm * NavigationLights.MetersPerNm;
        var center = new WorldPosition(own.East + r * System.Math.Sin(own.Psi), own.North + r * System.Math.Cos(own.Psi));
        double targetHeading = 0.0;
        SpawnDebugTarget(center, targetHeading);
        Debug.Log($"[Огни] Облёт: сухогруз курсом 000° в {orbitRadiusNm:F1} мили, шаг {orbitStepDeg:F0}° раз в {orbitStepSeconds:F0} с. Своё судно на паузе.");

        for (double q = 0.0; q < 360.0 - 1e-6; q += orbitStepDeg)
        {
            // Наблюдатель на курсовом угле q цели (от её носа), своё судно — носом на цель
            double az = (targetHeading + q) * Mathf.Deg2Rad;
            double e = center.East + r * System.Math.Sin(az), n = center.North + r * System.Math.Cos(az);
            double toTarget = (CpaCalculator.Bearing(center.East - e, center.North - n));
            navigation.TeleportTo(e, n, toTarget);
            if (grounding != null) grounding.ResetState();
            Debug.Log($"[Огни] Курсовой угол наблюдателя {ScenarioText.RelativeBearing(q)}: должны быть видны — {ExpectedLights(debugView, q)}");
            yield return new WaitForSeconds(orbitStepSeconds);
        }
        EndOrbit();
    }

    private void EndOrbit()
    {
        orbit = null;
        if (navigation != null && navigation.Model != null)
        {
            navigation.TeleportTo(orbitReturnE, orbitReturnN, orbitReturnPsi * Mathf.Rad2Deg);
            navigation.Paused = orbitWasPaused;
            if (grounding != null) grounding.ResetState();
        }
        RemoveDebugTarget();
        Debug.Log("[Огни] Облёт закончен, судно возвращено.");
    }

    private static string ExpectedLights(TrafficShipView view, double q)
    {
        if (view == null) return "—";
        var b = new StringBuilder();
        foreach (NavLight l in view.Lights)
        {
            double k = NavigationLights.SectorFactor(l, q);
            if (k <= 0.0) continue;
            if (b.Length > 0) b.Append(", ");
            b.Append(NavigationLights.Title(l.Kind));
            if (k < 0.999) b.Append($" ({k * 100:F0} %)");
        }
        return b.Length > 0 ? b.ToString() : "ничего";
    }

    private void SpawnDebugTarget(WorldPosition pos, double headingDeg)
    {
        RemoveDebugTarget();
        ShipParameters p = cargoParameters != null ? cargoParameters : navigation.parameters;
        var def = new TrafficShipDefinition { id = "DBG", name = "Проверка огней", type = TrafficVesselType.Cargo, parameters = p, speedKnots = 0f };
        var st = new TrafficStart { Position = pos, HeadingDeg = headingDeg, SpeedKnots = 0.0, Path = new List<WorldPosition>(), Problem = "" };
        debugTarget = new TrafficVessel(-1, "DBG", def, p, st) { EngineStopped = true };
        debugView = CreateView(debugTarget, "DBG Проверка огней");
    }

    private void RemoveDebugTarget()
    {
        if (debugView != null) Destroy(debugView.gameObject);
        debugView = null;
        debugTarget = null;
    }
}
