// ============================================================
// ExerciseRecorder.cs — Запись упражнения в файлы (C1)
//
// Потребитель шины ExerciseEvents. Сессия = упражнение ScenarioRunner:
// от ExerciseStarted до ExerciseEnded (причина — EndReason). Каждая сессия —
// отдельная папка (SessionFormat):
//   редактор — <проект>/ThesisData/Sessions/ (в git не попадает, .gitignore);
//   сборка   — Документы/VRBridgeTrainer/Sessions/, если «Документов» нет
//              (Quest автономно) — persistentDataPath/Sessions/.
// Перед новой записью — ротация (SessionStorage): не больше maxSessions
// папок, сессии с меткой «keep» не удаляются; 0 — без ротации.
//
// Данные:
//   track.csv / targets.csv — в FixedUpdate (порядок −84: после шага модели −90,
//     ScenarioRunner −88, GroundingMonitor −85) с частотой trackRateHz по
//     времени упражнения; XTE и участок — ScenarioRunner.Route (RouteTracker);
//   events.csv — каждое событие шины упражнения в момент доставки.
//   mooring.csv — 19.2б-3б: состояние у причала в моменты строк track.csv (BuildMooringSample);
//     в сценарии швартовки — всегда, иначе — с первого момента, когда причал считается
//     (19.3б: или вызван буксир); в конце строки — буксир и ветер (TowingController.Sample).
//     19.4б-2: в упражнении на буксире (19-03) — по судну-клиенту + свой буксир (TugWorkController.Sample).
//   assessment.csv и блок assessment в session.json — оценка по критериям (C2,
//     ExerciseAssessmentMonitor) в момент конца упражнения;
//   report.html — отчёт по упражнению (C3): после закрытия записи в фоновом
//     потоке (DebriefBuild) по файлам сессии; там же — схема для панели разбора.
// Срез состояния (BuildTrackSample / BuildTargetSamples) — общий для записи и оценки:
// оценка в шлеме получает те же строки, что окажутся в файлах.
// Без влияния на кадр: в шаге — только строка в StringBuilder; накопленное
// раз в flushIntervalSeconds (реальное время) уходит фоновому писателю.
// Сброс с ожиданием — в конце упражнения, при паузе приложения (Quest:
// системное меню), при выходе из Play Mode / закрытии (сессия помечается
// completed = false, end_reason = Interrupted).
//
// ID обучающегося — обезличенный код (без ФИО): поле traineeId в инспекторе
// (значение по умолчанию) или цифровая VR-клавиатура на брифинге (ScenarioPanel);
// в D1 — окно инструктора.
// ============================================================
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

[DefaultExecutionOrder(-84)]
public class ExerciseRecorder : MonoBehaviour
{
    [Header("Запись")]
    [Tooltip("Записывать упражнения (выключить — для замера частоты кадров без записи)")]
    public bool record = true;
    [Tooltip("Частота строк track.csv / targets.csv по времени упражнения, Гц")]
    [Range(1f, 50f)] public float trackRateHz = 5f;
    [Tooltip("Сброс накопленного в файлы не чаще, с (реальное время)")]
    [Range(0.2f, 10f)] public float flushIntervalSeconds = 1f;

    [Header("Обучающийся")]
    [Tooltip("Обезличенный код обучающегося (без ФИО). На брифинге вводится VR-клавиатурой")]
    public string traineeId = "";

    [Header("Хранение")]
    [Tooltip("Сколько последних сессий хранить; старые удаляются перед новой записью. Сессии с меткой «Сохранить» (окно Replay) не удаляются. 0 — без ротации (эксперимент)")]
    [Min(0)] public int maxSessions = 10;
    [Tooltip("После упражнения строить разбор: схему траектории для панели и report.html в папке сессии (фоновый поток, C3)")]
    public bool buildDebrief = true;
    [Tooltip("Папка сессий; пусто — по умолчанию (редактор: ThesisData/Sessions)")]
    public string customRoot = "";

    [Header("Сцена (пусто — найти)")]
    public ScenarioRunner runner;
    public ShipNavigation navigation;
    [Tooltip("19.2б-3б: место обучающегося (у штурвала / крыло) для mooring.csv")]
    public BridgeStationSwitch stations;

    [Header("Состояние (только чтение)")]
    [SerializeField] private string currentSession = "";
    [SerializeField] private int trackRows, eventRows;
    [Tooltip("Время главного потока на шаг записи (среднее за сессию), мкс")]
    [SerializeField] private float stepTimeUs;
    [Tooltip("Наибольшее время одной записи на диск (фоновый поток), мс")]
    [SerializeField] private float maxDiskWriteMs;
    [SerializeField] private string lastError = "";

    public static ExerciseRecorder Instance { get; private set; }

    /// <summary>Обезличенный код обучающегося для следующей сессии.</summary>
    public string TraineeId
    {
        get => traineeId;
        set => traineeId = value != null ? value.Trim() : "";
    }

    public bool IsRecording => session != null && session.IsOpen;
    /// <summary>Папка текущей или последней записанной сессии.</summary>
    public string LastSessionFolder { get; private set; } = "";
    /// <summary>Разбор последнего упражнения (C3): схема и report.html; null — не строился.</summary>
    public DebriefBuild LastDebrief { get; private set; }
    private bool debriefLogged = true;

    /// <summary>Корень папки сессий для текущей платформы.</summary>
    public string Root => !string.IsNullOrWhiteSpace(customRoot) ? customRoot : DefaultRoot;

    public static string DefaultRoot
    {
        get
        {
#if UNITY_EDITOR
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ThesisData", "Sessions"));
#else
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return !string.IsNullOrEmpty(docs)
                ? Path.Combine(docs, "VRBridgeTrainer", "Sessions")
                : Path.Combine(Application.persistentDataPath, "Sessions");
#endif
        }
    }

    private SessionRecorder session;
    private readonly TrackSampler sampler = new TrackSampler(5);
    private float nextFlush;
    private DateTime startLocal;
    private float startRealtime;
    private readonly Stopwatch watch = new Stopwatch();
    private long measuredTicks;
    private int measuredSteps;

    void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("[Запись] В сцене несколько ExerciseRecorder — используется первый.", this);
        else Instance = this;
    }

    void OnEnable() => ExerciseEvents.Raised += OnExerciseEvent;

    void OnDisable()
    {
        ExerciseEvents.Raised -= OnExerciseEvent;
        Close(null);   // выход из Play Mode / отключение компонента — запись прервана
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnApplicationQuit() => Close(null);

    void OnApplicationPause(bool paused)
    {
        if (paused && session != null) session.FlushAndWait();
    }

    void Start()
    {
        if (runner == null) runner = ScenarioRunner.Instance != null ? ScenarioRunner.Instance : FindAnyObjectByType<ScenarioRunner>();
        if (navigation == null) navigation = runner != null && runner.navigation != null ? runner.navigation : FindAnyObjectByType<ShipNavigation>();
        if (stations == null) stations = FindAnyObjectByType<BridgeStationSwitch>();
    }

    // ─────────── Шина ───────────
    private void OnExerciseEvent(ExerciseEvent e)
    {
        if (e.Type == ExerciseEventType.ExerciseStarted && record) Open();
        if (session == null || !e.InExercise) return;

        session.AddEvent(e);
        eventRows = session.EventRows;
        if (e.Type == ExerciseEventType.ExerciseStarted && sampler.Due(e.Time)) SampleTrack(session, e.Time);   // строка t = 0
        else if (e.Type == ExerciseEventType.ExerciseEnded) Close(e);
    }

    // ─────────── Траектория ───────────
    void FixedUpdate()
    {
        if (session == null) return;
        watch.Restart();
        if (runner != null && runner.CurrentPhase == ScenarioRunner.Phase.Running)
        {
            double t = runner.ExerciseTime;
            if (sampler.Due(t)) SampleTrack(session, t);
        }
        if (Time.unscaledTime >= nextFlush)
        {
            session.Flush();
            nextFlush = Time.unscaledTime + flushIntervalSeconds;
            if (session.Writer.LastError != null) lastError = session.Writer.LastError.Message;
            maxDiskWriteMs = (float)session.Writer.MaxWriteMs;
        }
        watch.Stop();
        measuredTicks += watch.ElapsedTicks;
        measuredSteps++;
        stepTimeUs = (float)(measuredTicks * 1e6 / Stopwatch.Frequency / Math.Max(1, measuredSteps));
    }

    private readonly System.Collections.Generic.List<TargetSample> targetBuffer = new System.Collections.Generic.List<TargetSample>();

    private void SampleTrack(SessionRecorder rec, double t)
    {
        if (!BuildTrackSample(navigation, runner, t, out TrackSample sample)) return;
        rec.AddTrack(sample);
        BuildTargetSamples(t, targetBuffer);
        foreach (TargetSample ts in targetBuffer) rec.AddTarget(ts);
        trackRows = rec.TrackRows;
        // 19.2б-3б: mooring.csv — с первого момента, когда причал понадобился, и до конца упражнения
        bool berthNear = BuildMooringSample(navigation, runner, stations, t, out MooringSample ms);
        if (ms.Lines != null && (berthNear || rec.MooringRows > 0)) rec.AddMooring(ms);
    }

    /// <summary>
    /// 19.2б-3б: срез у причала в момент t — строка mooring.csv (MooringSampler.Build, Core — тот же срез у прогона
    /// «за обучающегося»). Причал: в сценарии швартовки — причал задания, иначе — причал, который считает
    /// MooringController (ближе 600 м, модель 3 СС). true — строку надо писать (сценарий швартовки или причал рядом).
    /// </summary>
    public static bool BuildMooringSample(ShipNavigation navigation, ScenarioRunner runner, BridgeStationSwitch stations, double t, out MooringSample sample)
    {
        sample = default;
        ShipDynamicsModel m = navigation != null ? navigation.Model : null;
        if (m == null) return false;
        MooringController mc = MooringController.Instance;
        ScenarioDefinition sc = runner != null ? runner.Current : null;
        // 19.4б-2: упражнение на своём буксире — строка по клиенту у причала + столбцы своего буксира (каждую строку track.csv)
        TugWorkController tw = TugWorkController.Instance;
        if (sc != null && sc.IsTugWork && tw != null && tw.Work != null && tw.Scenario == sc)
        {
            sample = tw.Sample(t, stations != null ? stations.Current : BridgeStation.Helm);
            return true;
        }
        bool task = sc != null && sc.IsMooring;
        BerthArea berth;
        int index;
        if (task)
        {
            index = sc.mooring.berthIndex;
            berth = sc.mooring.BerthIn(sc.waterArea);
        }
        else
        {
            index = mc != null ? mc.ActiveBerthIndex : -1;
            berth = index >= 0 ? mc.ActiveBerth : null;
        }
        sample = MooringSampler.Build(t, berth, index, navigation.parameters, m, mc != null ? mc.System : null,
            mc != null ? mc.ThrusterIndex : ThrusterOrders.ZeroIndex, stations != null ? stations.Current : BridgeStation.Helm,
            task ? runner.MooringTask : null);
        // 19.3б: буксир и ветер — столбцы в конце строки; строки пишутся и тогда, когда вызван буксир
        TowingController tc = TowingController.Instance;
        if (tc != null) sample.Towing = tc.Sample();
        return task || berth != null || tc != null && tc.TugPresent;
    }

    /// <summary>Срез своего судна в момент t — строка track.csv; тот же срез получает оценка (C2).</summary>
    public static bool BuildTrackSample(ShipNavigation navigation, ScenarioRunner runner, double t, out TrackSample sample)
    {
        sample = default;
        ShipDynamicsModel m = navigation != null ? navigation.Model : null;
        if (m == null) return false;
        RouteTracker r = runner != null ? runner.Route : null;

        double heading = navigation.Heading;
        double cog = Math.Abs(m.U) < 0.01 ? heading : m.U >= 0 ? heading : (heading + 180.0) % 360.0;
        if (m.V != 0.0)
        {
            // Запрос 19.1, модель 3 СС: путевой угол — по скорости относительно грунта (с дрейфом)
            double ve = m.VelocityEast, vn = m.VelocityNorth;
            cog = ve * ve + vn * vn < 1e-4 ? heading : (Math.Atan2(ve, vn) * 180.0 / Math.PI + 360.0) % 360.0;
        }
        double xte = double.NaN, xteLimit = double.NaN;
        int leg = -1;
        if (r != null && r.Valid)
        {
            leg = r.Leg;
            xteLimit = r.XteLimit;
            // XTE по активному участку — по текущей позиции (в момент «Начать» RouteTracker ещё не обновлён,
            // после прибытия — не обновляется); на шаге модели совпадает с RouteTracker.Xte
            xte = RouteTracker.CrossTrack(r.Route[leg].position, r.Route[leg + 1].position, new WorldPosition(m.East, m.North));
        }

        sample = new TrackSample
        {
            Time = t,
            East = m.East, North = m.North,
            HeadingDeg = heading, CogDeg = cog,
            SpeedKnots = navigation.SpeedKnots,
            RotDegMin = navigation.RateOfTurn,
            RudderOrderedDeg = m.Ordered, RudderActualDeg = m.Delta,
            TelegraphSf = m.SpeedFactor,
            Telegraph = (int)ScenarioText.OrderFromSpeedFactor(m.SpeedFactor),
            XteM = xte, Leg = leg, XteLimitM = xteLimit,
            Grounded = m.Grounded
        };
        return true;
    }

    /// <summary>Срез других судов в момент t — строки targets.csv (B3); тот же срез получает оценка (C2).</summary>
    public static void BuildTargetSamples(double t, System.Collections.Generic.List<TargetSample> into)
    {
        into.Clear();
        foreach (ITrackedTarget target in ExerciseTargets.All)
            into.Add(new TargetSample
            {
                Time = t, Id = target.TargetId,
                East = target.Position.East, North = target.Position.North,
                HeadingDeg = target.HeadingDeg, SpeedKnots = target.SpeedKnots
            });
    }

    // ─────────── Сессия ───────────
    private void Open()
    {
        Close(null);
        ScenarioDefinition s = runner != null ? runner.Current : null;
        try
        {
            foreach (string removed in SessionStorage.Rotate(Root, maxSessions))
                Debug.Log($"[Запись] Ротация: удалена старая сессия {Path.GetFileName(removed)}");

            startLocal = DateTime.Now;
            startRealtime = Time.realtimeSinceStartup;
            ShipParameters p = navigation != null ? navigation.parameters : null;
            SessionMeta meta = SessionMeta.Create(s, p, TraineeId, startLocal);
            if (navigation != null && navigation.Model != null && meta.ship != null)
                meta.ship.motion_model = navigation.Model.ActiveMotionModel.ToString();   // запрос 19.1: переключатель в сцене
            ShipMotion motion = FindAnyObjectByType<ShipMotion>();
            if (motion != null && meta.ship != null) meta.ship.bridge_motion_pct = motion.ComfortPercent;   // К4: меню Y «Качка мостика»
            FillApp(meta.app);
            meta.recording.track_rate_hz = trackRateHz;
            meta.recording.fixed_dt_s = Math.Round(Time.fixedDeltaTime, 6);

            string folder = SessionStorage.CreateFolder(Root, meta.session_id);
            meta.session_id = Path.GetFileName(folder);
            session = new SessionRecorder(folder, meta);
            // 21.1: копия сценария, по которому идёт упражнение (встроенный или из файла) — его можно открыть и повторить
            string copy = ScenarioFiles.ToFileText(s);
            if (copy != null) session.Writer.Replace(SessionFormat.ScenarioCopyFile, copy);
        }
        catch (Exception ex)
        {
            session = null;
            lastError = ex.Message;
            Debug.LogError($"[Запись] Не удалось начать запись в {Root}: {ex.Message}", this);
            return;
        }

        sampler.SetRate(trackRateHz);
        sampler.Reset();
        nextFlush = Time.unscaledTime + flushIntervalSeconds;
        measuredTicks = 0; measuredSteps = 0;
        LastSessionFolder = session.Folder;
        currentSession = session.Meta.session_id;
        trackRows = eventRows = 0;
        lastError = "";
        Debug.Log($"[Запись] Сессия {currentSession} → {session.Folder}", this);
    }

    /// <summary>Завершить сессию: ended — событие ExerciseEnded (null — запись прервана).</summary>
    private void Close(ExerciseEvent? ended)
    {
        if (session == null) return;
        SessionRecorder s = session;
        session = null;
        AssessmentResult assessment = null;

        if (ended.HasValue)
        {
            ExerciseEvent e = ended.Value;
            if (double.IsNaN(s.LastTrackTime) || s.LastTrackTime < e.Time - 1e-6)
                SampleTrack(s, e.Time);   // последняя строка траектории — в момент завершения
            s.Meta.completed = true;
            s.Meta.end_reason = e.EndReason.ToString();
            s.Meta.end_reason_ru = ScenarioText.EndReason(e.EndReason);
            s.Meta.by_instructor = e.ByInstructor;
            s.Meta.duration_s = Math.Round(e.Time, 3);

            // Оценка (C2): итог в session.json, подробно — assessment.csv
            ExerciseAssessmentMonitor monitor = ExerciseAssessmentMonitor.Instance;
            assessment = monitor != null ? monitor.FinishNow(e) : null;
            if (assessment != null)
            {
                s.Meta.assessment = SessionAssessmentSummary.From(assessment);
                s.WriteAssessment(assessment.ToCsv());
            }
        }
        else
        {
            s.Meta.completed = false;
            s.Meta.end_reason = SessionMeta.Interrupted;
            s.Meta.end_reason_ru = "запись прервана (выход из Play Mode или закрытие приложения)";
            s.Meta.duration_s = Math.Round(double.IsNaN(s.LastTrackTime) ? 0 : s.LastTrackTime, 3);
        }

        DateTime end = DateTime.Now;
        s.Meta.end_local = end.ToString("yyyy-MM-ddTHH:mm:sszzz");
        s.Meta.end_utc = end.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        s.Meta.wall_duration_s = Math.Round(Time.realtimeSinceStartup - startRealtime, 3);
        if (runner != null)
            s.Meta.summary = SessionSummary.From(ExerciseSummary.From(runner.Current, ExerciseEvents.Log, runner.Route));

        bool written = s.Close();
        trackRows = s.TrackRows;
        eventRows = s.EventRows;
        if (s.Writer.LastError != null) lastError = s.Writer.LastError.Message;
        if (!written) Debug.LogWarning($"[Запись] Сессия {s.Meta.session_id}: запись на диск не завершилась за 2 с.", this);
        Debug.Log($"[Запись] Сессия {s.Meta.session_id} записана: {s.Meta.end_reason_ru}, строк траектории {s.TrackRows}, событий {s.EventRows}" +
                  (string.IsNullOrEmpty(lastError) ? "" : $" (ошибка: {lastError})"), this);

        // Разбор (C3): по записанным файлам, в фоновом потоке; оценка — готовая (в шлеме = по записи, C2)
        LastDebrief = null;
        if (ended.HasValue && written && buildDebrief && assessment != null)
        {
            LastDebrief = DebriefBuild.Start(s.Folder, assessment);
            debriefLogged = false;
        }
    }

    void Update()
    {
        if (debriefLogged || LastDebrief == null || !LastDebrief.Done) return;
        debriefLogged = true;
        if (string.IsNullOrEmpty(LastDebrief.Error))
            Debug.Log($"[Разбор] {LastDebrief.ReportPath} ({LastDebrief.ElapsedMs:F0} мс, фоновый поток)", this);
        else
            Debug.LogWarning($"[Разбор] Отчёт не построен: {LastDebrief.Error}", this);
    }

    private static void FillApp(SessionApp app)
    {
        app.product = Application.productName;
        app.version = Application.version;
        app.unity_version = Application.unityVersion;
        app.platform = Application.platform.ToString();
        app.is_editor = Application.isEditor;
#if UNITY_EDITOR
        app.git_commit = GitCommit();
#endif
    }

#if UNITY_EDITOR
    // Коммит проекта (.git/HEAD → ref → хеш) — для воспроизводимости записи
    private static string GitCommit()
    {
        try
        {
            string git = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".git"));
            string head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();
            if (!head.StartsWith("ref:")) return head;
            string refName = head.Substring(4).Trim();
            string refFile = Path.Combine(git, refName.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(refFile)) return File.ReadAllText(refFile).Trim();
            string packed = Path.Combine(git, "packed-refs");
            if (File.Exists(packed))
                foreach (string line in File.ReadAllLines(packed))
                    if (line.EndsWith(" " + refName)) return line.Substring(0, line.IndexOf(' '));
        }
        catch (Exception) { }
        return "";
    }
#endif
}
