// ============================================================
// BridgeActionMonitor.cs — Действия обучающегося → шина упражнения (C1)
//
// Источник событий шины ExerciseEvents (как ScenarioRunner и GroundingMonitor):
// на каждом шаге модели читает органы управления — заданный руль
// (ShipNavigation.RudderOrdered, с учётом знака штурвала) и хват штурвала,
// положение телеграфа и хват рычага, звучание гудка, пресет погоды, время суток (B4) — и через
// ControlActionDetector (Core) публикует перекладки руля, команды телеграфу,
// гудки (C4: тип звука и сигнала), смены погоды, времени суток и видимости (C4). Органы управления о шине не знают.
//
// Порядок −87: после шага модели (−90) и ScenarioRunner (−88) — у действия
// то же время упражнения, что у событий маршрута этого шага.
// В начале упражнения детектор сбрасывается: начальные условия сценария —
// не действия обучающегося. Вне упражнения события тоже публикуются
// (в журнал и запись не попадают).
// ============================================================
using UnityEngine;

[DefaultExecutionOrder(-87)]
public class BridgeActionMonitor : MonoBehaviour
{
    [Header("Органы управления (пусто — найти)")]
    public ShipNavigation navigation;
    public SteeringWheel wheel;
    public EngineTelegraph telegraph;
    public ShipHorn horn;
    public WeatherController weather;

    [Header("Пороги (предварительные)")]
    [Tooltip("Начало перекладки: заданный руль ушёл от устоявшегося значения, градусы")]
    public float helmThresholdDeg = 0.5f;
    [Tooltip("Руль «стоит», если не уходит дальше, градусы")]
    public float helmStillDeg = 0.2f;
    [Tooltip("Конец перекладки: руль стоит столько секунд (или штурвал отпущен)")]
    public float helmSettleSeconds = 1f;
    [Tooltip("Команда телеграфу: положение стоит столько секунд (или рычаг отпущен)")]
    public float telegraphSettleSeconds = 0.5f;
    [Tooltip("Допуск длительности звука гудка (правило 32: продолжительный 4–6 с, короткий ≈ 1 с), с — C4")]
    public float blastToleranceS = (float)SoundSignals.DefaultToleranceS;

    private readonly ControlActionDetector detector = new ControlActionDetector();
    private double clock;
    private bool initialized;

    void OnEnable() => ExerciseEvents.Raised += OnExerciseEvent;
    void OnDisable() => ExerciseEvents.Raised -= OnExerciseEvent;

    void Start()
    {
        if (navigation == null) navigation = GetComponent<ShipNavigation>();
        if (navigation == null) navigation = FindAnyObjectByType<ShipNavigation>();
        if (navigation != null)
        {
            if (wheel == null) wheel = navigation.wheel;
            if (telegraph == null) telegraph = navigation.telegraph;
        }
        if (horn == null) horn = FindAnyObjectByType<ShipHorn>();
        if (weather == null) weather = FindAnyObjectByType<WeatherController>();
    }

    void FixedUpdate()
    {
        if (navigation == null || navigation.Model == null) return;
        clock += Time.fixedDeltaTime;
        ApplyThresholds();
        ControlState s = Read();
        if (!initialized)
        {
            detector.Reset(clock, s);
            initialized = true;
            return;
        }
        foreach (ExerciseEvent e in detector.Update(clock, s))
        {
            ExerciseEvent ev = e;
            ev.Position = navigation.PositionEastNorth;
            ev.HeadingDeg = navigation.Heading;
            ev.SpeedKnots = navigation.SpeedKnots;
            ExerciseEvents.Publish(ev);
        }
    }

    private void OnExerciseEvent(ExerciseEvent e)
    {
        if (e.Type != ExerciseEventType.ExerciseStarted || navigation == null || navigation.Model == null) return;
        detector.Reset(clock, Read());
        initialized = true;
    }

    private void ApplyThresholds()
    {
        detector.HelmThresholdDeg = helmThresholdDeg;
        detector.HelmStillDeg = helmStillDeg;
        detector.HelmSettleSeconds = helmSettleSeconds;
        detector.TelegraphSettleSeconds = telegraphSettleSeconds;
        detector.BlastToleranceS = blastToleranceS;
    }

    private ControlState Read()
    {
        double sf = telegraph != null ? telegraph.SpeedFactor : navigation.Model.SpeedFactor;
        return new ControlState
        {
            RudderOrderedDeg = navigation.RudderOrdered,
            WheelGrabbed = wheel != null && wheel.IsGrabbed,
            TelegraphSf = sf,
            Telegraph = (int)ScenarioText.OrderFromSpeedFactor(sf),
            TelegraphGrabbed = telegraph != null && telegraph.IsGrabbed,
            TelegraphByCode = telegraph != null && telegraph.LastChangeFromCode,
            HornSounding = horn != null && horn.IsSounding,
            WeatherPreset = weather != null ? (int)weather.CurrentPresetId : (int)WeatherPresetId.Moderate,
            TimeOfDay = weather != null ? (int)weather.CurrentTimeOfDay : (int)global::TimeOfDay.Day,
            VisibilityOverrideM = weather != null ? weather.VisibilityOverrideM : 0.0
        };
    }
}
