// ============================================================
// InstructorInputs.cs — Вводные инструктора и пауза (D1)
//
// Вводные — события, которые инструктор подаёт во время упражнения:
//   • отказ главного двигателя — ShipNavigation.EngineFailed: u_зад = 0 при любом
//     положении телеграфа, ход гаснет (ShipDynamicsModel);
//   • отказ рулевого устройства — ShipNavigation.SteeringFailed: фактический руль
//     остаётся на месте, штурвал крутится вхолостую;
//   • ухудшение видимости — WeatherController.SetVisibility (событие VisibilityChanged
//     публикует BridgeActionMonitor, как при смене из меню «Обстановка»).
// Каждая вводная и её устранение — событие шины (EngineFailure / EngineRestored,
// SteeringFailure / SteeringRestored, ByInstructor = true; 21.6а — от триггера сценария false): попадает в запись, журнал
// report.html, на графики и схему; от неё оценка C2 отсчитывает время реакции (N1, N2).
// Вводные снимаются (без событий) при загрузке сценария, начале и конце упражнения.
//
// Меню A: подменю «Вводные» (отказы), «Пауза» — на главной (группа «Сценарий»), «Квитировать» —
// на главной, пока звучит сигнал. Оповещение — меню Y, подменю «Оповещения».
// Оповещение обучающегося (переключатель «Оповещение об отказах»):
//   вкл — звуковая сигнализация (прерывистый тон, до квитирования или устранения),
//   надпись перед обучающимся на showSeconds и строка «ОТКАЗ …» на NavDisplay;
//   выкл — молча: обучающийся сам замечает, что ход или руль не отвечают.
// Пауза инструктора — ScenarioRunner.Pause / Resume; пока пауза, перед обучающимся
// надпись «ПАУЗА». Те же команды — в окне инструктора (InstructorWindow) и в меню A
// (группы «Сценарий» и «Вводные»: проверка в шлеме без второго человека).
// ============================================================
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InstructorInputs : MonoBehaviour
{
    [Header("Сцена (пусто — найти)")]
    public ScenarioRunner runner;
    public ShipNavigation navigation;
    public WeatherController weather;

    [Header("Оповещение обучающегося об отказе")]
    [Tooltip("Сигнализация, надпись и строка на NavDisplay (переключается из меню A «Вводные»)")]
    public bool announceFailures = true;
    [Tooltip("Громкость сигнализации")]
    [Range(0f, 1f)] public float alarmVolume = 0.3f;
    [Tooltip("Частота тона сигнализации, Гц")]
    public float alarmFrequency = 1200f;
    [Tooltip("Сколько секунд видна надпись об отказе")]
    public float showSeconds = 6f;

    [Header("Надпись перед обучающимся")]
    public float distanceFromHead = 1.1f;
    public float upOffset = 0.22f;
    public float canvasScale = 0.001f;

    public static InstructorInputs Instance { get; private set; }

    public bool EngineFailed => navigation != null && navigation.EngineFailed;
    public bool SteeringFailed => navigation != null && navigation.SteeringFailed;
    /// <summary>Сигнализация звучит (не квитирована).</summary>
    public bool AlarmSounding => alarm != null && alarm.isPlaying;

    /// <summary>Строка для NavDisplay: «ОТКАЗ ГД», «ОТКАЗ РУЛЯ» (пусто — нет отказа или оповещение выключено).</summary>
    public string FailureLine
    {
        get
        {
            if (!announceFailures || (!EngineFailed && !SteeringFailed)) return "";
            var parts = new List<string>();
            if (EngineFailed) parts.Add("ОТКАЗ ГД");
            if (SteeringFailed) parts.Add("ОТКАЗ РУЛЯ");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Вводные можно подавать: идёт упражнение или свободное плавание.</summary>
    public bool CanApply => navigation != null && navigation.Model != null && runner != null &&
                            (runner.CurrentPhase == ScenarioRunner.Phase.Running || runner.CurrentPhase == ScenarioRunner.Phase.Idle);

    private const string MenuGroup = "Вводные";
    private readonly List<BridgeMenuCommand> menuCommands = new List<BridgeMenuCommand>();
    private double engineFailedAt, steeringFailedAt;
    private AudioSource alarm;
    private Canvas canvas;
    private TextMeshProUGUI label;
    private Image labelBackground;
    private CanvasGroup group;
    private float hideAt;
    private ScenarioRunner subscribed;

    private static readonly Color FailureBack = new Color(0.5f, 0.32f, 0.02f, 0.92f);
    private static readonly Color PauseBack = new Color(0.12f, 0.2f, 0.35f, 0.92f);

    void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("[Вводные] В сцене несколько InstructorInputs — используется первый.", this);
        else Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        ExerciseEvents.Raised += OnExerciseEvent;
        BridgeMenu.SetGroupOrder(MenuGroup, 50);
        menuCommands.Add(BridgeMenu.Register("Сценарий", () => runner != null && runner.IsPaused ? "Продолжить (снять паузу)" : "Пауза (инструктор)",
            () => runner.TogglePause(), () => runner != null && runner.CurrentPhase == ScenarioRunner.Phase.Running,
            instructorOnly: true, hideWhenUnavailable: true));
        menuCommands.Add(BridgeMenu.Register(MenuGroup, () => EngineFailed ? "Устранить отказ ГД" : "Отказ главного двигателя",
            () => SetEngineFailure(!EngineFailed), () => CanApply, instructorOnly: true));
        menuCommands.Add(BridgeMenu.Register(MenuGroup, () => SteeringFailed ? "Устранить отказ руля" : "Отказ рулевого устройства",
            () => SetSteeringFailure(!SteeringFailed), () => CanApply, instructorOnly: true));
        // Квитирование — на главной странице меню A (группа «Сигнализация», как у мели), пока звучит сигнал
        BridgeMenu.SetGroupOrder("Сигнализация", -90);
        BridgeMenu.SetGroupExpanded("Сигнализация", true);
        menuCommands.Add(BridgeMenu.Register("Сигнализация", "Квитировать сигнализацию отказа", Acknowledge,
            () => AlarmSounding, hideWhenUnavailable: true));
        // Переключатель оповещения — меню «Параметры» (Y), подменю «Оповещения»
        BridgeMenuCommand announce = BridgeMenu.Register("Оповещения", () => announceFailures ? "Оповещение об отказах: вкл" : "Оповещение об отказах: выкл",
            () => SetAnnounce(!announceFailures), instructorOnly: true).InMenu(BridgeMenu.SettingsMenuId);
        menuCommands.Add(announce);
    }

    void OnDisable()
    {
        ExerciseEvents.Raised -= OnExerciseEvent;
        foreach (BridgeMenuCommand c in menuCommands) BridgeMenu.Unregister(c);
        menuCommands.Clear();
        if (subscribed != null) subscribed.PauseChanged -= OnPauseChanged;
        subscribed = null;
        ClearFailures();
    }

    void Start()
    {
        if (runner == null) runner = ScenarioRunner.Instance != null ? ScenarioRunner.Instance : FindAnyObjectByType<ScenarioRunner>();
        if (navigation == null) navigation = runner != null && runner.navigation != null ? runner.navigation : FindAnyObjectByType<ShipNavigation>();
        if (weather == null) weather = FindAnyObjectByType<WeatherController>();
        if (runner != null && subscribed != runner)
        {
            runner.PauseChanged += OnPauseChanged;
            subscribed = runner;
        }
        BuildAlarm();
    }

    // ─────────── Вводные ───────────
    public void SetEngineFailure(bool failed, bool byInstructor = true)   // 21.6а: false — триггер сценария
    {
        if (navigation == null || navigation.EngineFailed == failed || (failed && !CanApply)) return;
        navigation.EngineFailed = failed;
        double now = ExerciseEvents.Time;
        if (failed)
        {
            engineFailedAt = now;
            double sf = navigation.telegraph != null ? navigation.telegraph.SpeedFactor : navigation.Model.SpeedFactor;
            Publish(new ExerciseEvent(ExerciseEventType.EngineFailure)
            {
                Value = sf, Subject = ScenarioText.Telegraph(ScenarioText.OrderFromSpeedFactor(sf)), ByInstructor = byInstructor
            });
            Announce("ОТКАЗ ГЛАВНОГО ДВИГАТЕЛЯ", "машина не отвечает на телеграф");
        }
        else
        {
            Publish(new ExerciseEvent(ExerciseEventType.EngineRestored) { Value = now - engineFailedAt, ByInstructor = byInstructor });
            AfterRestore();
        }
    }

    public void SetSteeringFailure(bool failed, bool byInstructor = true)
    {
        if (navigation == null || navigation.SteeringFailed == failed || (failed && !CanApply)) return;
        navigation.SteeringFailed = failed;
        double now = ExerciseEvents.Time;
        if (failed)
        {
            steeringFailedAt = now;
            Publish(new ExerciseEvent(ExerciseEventType.SteeringFailure)
            {
                Value = navigation.RudderActual, Value2 = navigation.RudderOrdered, ByInstructor = byInstructor
            });
            Announce("ОТКАЗ РУЛЕВОГО УСТРОЙСТВА", $"руль заклинило: {ScenarioText.Rudder(navigation.RudderActual)}");
        }
        else
        {
            Publish(new ExerciseEvent(ExerciseEventType.SteeringRestored) { Value = now - steeringFailedAt, ByInstructor = byInstructor });
            AfterRestore();
        }
    }

    /// <summary>Вводная «ухудшение видимости», м (0 — по погоде).</summary>
    public void SetVisibility(float meters)
    {
        if (weather != null) weather.SetVisibility(meters);
    }

    /// <summary>Квитировать сигнализацию (звук смолкает, строка на NavDisplay остаётся до устранения).</summary>
    public void Acknowledge()
    {
        if (alarm != null) alarm.Stop();
    }

    public void SetAnnounce(bool on)
    {
        announceFailures = on;
        if (!on) Acknowledge();
    }

    // Вводные — только на время упражнения: начальные условия сценария их снимают
    private void OnExerciseEvent(ExerciseEvent e)
    {
        if (e.Type == ExerciseEventType.ScenarioLoaded || e.Type == ExerciseEventType.ExerciseStarted || e.Type == ExerciseEventType.ExerciseEnded)
            ClearFailures();
    }

    private void ClearFailures()
    {
        if (navigation != null)
        {
            navigation.EngineFailed = false;
            navigation.SteeringFailed = false;
        }
        Acknowledge();
        if (canvas != null && (runner == null || !runner.IsPaused)) canvas.enabled = false;
    }

    private void AfterRestore()
    {
        if (!EngineFailed && !SteeringFailed) Acknowledge();
    }

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

    // ─────────── Оповещение ───────────
    private void Announce(string title, string detail)
    {
        if (!announceFailures) return;
        if (alarm != null)
        {
            alarm.volume = alarmVolume;
            if (!alarm.isPlaying) alarm.Play();
        }
        ShowNotice(title, detail, FailureBack, showSeconds);
    }

    private void OnPauseChanged()
    {
        if (runner != null && runner.IsPaused) ShowNotice("ПАУЗА", "упражнение остановлено инструктором", PauseBack, float.PositiveInfinity);
        else if (canvas != null) hideAt = Mathf.Min(hideAt, Time.unscaledTime + 0.8f);
    }

    private void BuildAlarm()
    {
        if (alarm != null) return;
        // Прерывистый тон: 0,4 с звук, 0,6 с пауза (огибающая 10 мс — без щелчков)
        const int rate = 44100;
        int n = rate;
        var data = new float[n];
        int on = (int)(0.4f * rate), fade = (int)(0.01f * rate);
        for (int i = 0; i < on; i++)
        {
            float env = Mathf.Min(1f, Mathf.Min(i, on - i) / (float)fade);
            data[i] = 0.8f * env * Mathf.Sin(2f * Mathf.PI * alarmFrequency * i / rate);
        }
        AudioClip clip = AudioClip.Create("FailureAlarm", n, 1, rate, false);
        clip.SetData(data, 0);
        alarm = gameObject.AddComponent<AudioSource>();
        alarm.clip = clip;
        alarm.loop = true;
        alarm.playOnAwake = false;
        alarm.spatialBlend = 0f;
        alarm.volume = alarmVolume;
    }

    private void ShowNotice(string title, string detail, Color back, float seconds)
    {
        BuildNotice();
        label.text = $"<b>{title}</b>\n<size=70%>{detail}</size>";
        labelBackground.color = back;
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 pos = cam.transform.position + fwd * distanceFromHead + Vector3.up * upOffset;
            canvas.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - cam.transform.position, Vector3.up));
        }
        canvas.enabled = true;
        group.alpha = 1f;
        hideAt = Time.unscaledTime + seconds;
    }

    void Update()
    {
        if (canvas == null || !canvas.enabled) return;
        float left = hideAt - Time.unscaledTime;
        group.alpha = Mathf.Clamp01(left / 0.8f);   // гаснет за последние 0,8 с
        if (left <= 0f) canvas.enabled = false;
    }

    private void BuildNotice()
    {
        if (canvas != null) return;
        // Надпись — дочерняя к этому объекту (Ship): качается вместе с мостиком
        var go = new GameObject("InstructorNoticeCanvas", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;
        go.transform.localScale = Vector3.one * canvasScale;
        group = go.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(620f, 130f);

        var bg = new GameObject("Background", typeof(RectTransform));
        bg.transform.SetParent(go.transform, false);
        labelBackground = bg.AddComponent<Image>();
        labelBackground.raycastTarget = false;
        Stretch(bg.GetComponent<RectTransform>());

        var text = new GameObject("Text", typeof(RectTransform));
        text.transform.SetParent(go.transform, false);
        label = text.AddComponent<TextMeshProUGUI>();
        label.fontSize = 40f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        Stretch(text.GetComponent<RectTransform>());
        canvas.enabled = false;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
