// ============================================================
// ExerciseAssessmentMonitor.cs — Оценка упражнения в реальном времени (C2)
//
// Объект Scenario, порядок −83 (после шага модели −90, контроля маршрута −88,
// целей −86, мели −85 и записи −84). Сама оценка — чистый класс Core
// (ExerciseAssessor через LiveAssessment); здесь — только подача данных:
//   • события шины упражнения;
//   • срезы своего судна и целей с частотой записи (ExerciseRecorder.trackRateHz,
//     те же моменты и та же сборка строки, что у track.csv / targets.csv);
//   • контекст — из того же SessionMeta, что пишется в session.json.
// Поэтому оценка по записи (SessionAssessment, окно Session Replay) даёт тот же
// результат, что здесь (тест AssessmentTests.LiveAndOffline_SameSession_IdenticalResults).
// События самой оценки (обнаружение цели, начало сближения с ситуацией МППСС)
// публикуются в шину — их видят консоль, запись и (D1) окно инструктора.
// Итог — в конце упражнения (в том числе досрочном): панель результатов,
// assessment.csv и session.json (через ExerciseRecorder). Промежуточная оценка —
// Current() (меню «Параметры» на Y → «Текущий балл»).
// Каждый шаг модели (для окна инструктора и планшета, D1/D2) — CPA/TCPA по целям
// без округления: LiveTargets.
// ============================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

[DefaultExecutionOrder(-83)]
public class ExerciseAssessmentMonitor : MonoBehaviour
{
    /// <summary>Цель на текущем шаге (без округления; для отображения).</summary>
    public struct LiveTarget
    {
        public string Id;
        public double RangeNm, BearingDeg, CpaNm, TcpaMin;
    }

    [Tooltip("Оценивать упражнения")]
    public bool assess = true;

    [Header("Сцена (пусто — найти)")]
    public ScenarioRunner runner;
    public ShipNavigation navigation;
    public ExerciseRecorder recorder;

    [Header("Состояние (только чтение)")]
    [SerializeField] private string lastResult = "";
    [Tooltip("Время главного потока на шаг оценки (среднее за упражнение), мкс")]
    [SerializeField] private float stepTimeUs;

    public static ExerciseAssessmentMonitor Instance { get; private set; }

    public LiveAssessment Live { get; } = new LiveAssessment();
    /// <summary>Итог последнего упражнения.</summary>
    public AssessmentResult LastResult => Live.Result;
    public bool Running => Live.Running;
    public IReadOnlyList<LiveTarget> LiveTargets => liveTargets;

    /// <summary>Итог упражнения готов.</summary>
    public event Action<AssessmentResult> Finished;

    private readonly List<TargetSample> buffer = new List<TargetSample>();
    private readonly List<LiveTarget> liveTargets = new List<LiveTarget>();
    private readonly Stopwatch watch = new Stopwatch();
    private long ticks;
    private int steps;

    void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("[Оценка] В сцене несколько ExerciseAssessmentMonitor — используется первый.", this);
        else Instance = this;
        Live.Output += PublishOwn;
    }

    void OnEnable() => ExerciseEvents.Raised += OnExerciseEvent;
    void OnDisable() => ExerciseEvents.Raised -= OnExerciseEvent;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (runner == null) runner = ScenarioRunner.Instance != null ? ScenarioRunner.Instance : FindAnyObjectByType<ScenarioRunner>();
        if (navigation == null) navigation = runner != null && runner.navigation != null ? runner.navigation : FindAnyObjectByType<ShipNavigation>();
        if (recorder == null) recorder = ExerciseRecorder.Instance != null ? ExerciseRecorder.Instance : FindAnyObjectByType<ExerciseRecorder>();
    }

    // ─────────── Шина ───────────
    private void OnExerciseEvent(ExerciseEvent e)
    {
        if (!assess || !e.InExercise) return;
        if (e.Type == ExerciseEventType.ExerciseStarted)
        {
            Begin();
            Live.OnEvent(e);
            if (Live.Due(e.Time)) Sample(e.Time);   // срез t = 0 — как первая строка track.csv
            return;
        }
        if (!Live.Running) return;
        Live.OnEvent(e);
        if (e.Type == ExerciseEventType.ExerciseEnded) Finish(e.Time);
    }

    private void Begin()
    {
        ScenarioDefinition s = runner != null ? runner.Current : null;
        ShipParameters p = navigation != null ? navigation.parameters : null;
        double rate = recorder != null ? recorder.trackRateHz : 5.0;
        Live.Begin(AssessmentContext.FromScenario(s, p), rate);
        ticks = 0; steps = 0;
        lastResult = "идёт упражнение";
    }

    /// <summary>
    /// Итог по событию конца упражнения (вызывает и ExerciseRecorder, чтобы записать оценку
    /// в файлы, — порядок подписчиков шины не важен). Повторный вызов возвращает тот же итог.
    /// </summary>
    public AssessmentResult FinishNow(in ExerciseEvent ended)
    {
        if (!assess) return null;
        if (Live.Running)
        {
            Live.OnEvent(ended);
            Finish(ended.Time);
        }
        return Live.Result;
    }

    private void Finish(double endTime)
    {
        if (!Live.Running) return;
        if (Live.NeedsFinalSample(endTime)) Sample(endTime);   // последний срез — в момент конца, как в track.csv
        AssessmentResult r = Live.End();
        lastResult = r.Headline();
        Debug.Log($"[Оценка] {r.Headline()}" +
                  (r.Failed ? $"; провал: {r.FailureText}" : "") +
                  (r.MandatoryFailed.Count > 0 ? $"; не выполнены обязательные: {string.Join(", ", r.MandatoryFailed)}" : "") +
                  $"; критериев оценено {r.EvaluatedCount}, не выполнено {r.FailedCount}", this);
        try { Finished?.Invoke(r); }
        catch (Exception ex) { Debug.LogException(ex, this); }
    }

    /// <summary>Промежуточная оценка во время упражнения (после — итог).</summary>
    public AssessmentResult Current() => Live.Current();

    // ─────────── Шаг ───────────
    void FixedUpdate()
    {
        if (!Live.Running || runner == null || runner.CurrentPhase != ScenarioRunner.Phase.Running) return;
        watch.Restart();
        double t = runner.ExerciseTime;
        if (Live.Due(t)) Sample(t);
        UpdateLiveTargets();
        watch.Stop();
        ticks += watch.ElapsedTicks;
        steps++;
        stepTimeUs = (float)(ticks * 1e6 / Stopwatch.Frequency / Math.Max(1, steps));
    }

    private void Sample(double t)
    {
        if (!ExerciseRecorder.BuildTrackSample(navigation, runner, t, out TrackSample own)) return;
        ExerciseRecorder.BuildTargetSamples(t, buffer);
        Live.AddSample(own, buffer);
    }

    private void UpdateLiveTargets()
    {
        liveTargets.Clear();
        if (navigation == null || navigation.Model == null) return;
        WorldPosition p = navigation.PositionEastNorth;
        foreach (ITrackedTarget t in ExerciseTargets.All)
        {
            CpaResult c = CpaCalculator.FromCourses(p, navigation.Heading, navigation.SpeedKnots, t.Position, t.HeadingDeg, t.SpeedKnots);
            liveTargets.Add(new LiveTarget
            {
                Id = t.TargetId, RangeNm = c.Range / CpaCalculator.MetersPerNm, BearingDeg = c.BearingDeg,
                CpaNm = c.Cpa / CpaCalculator.MetersPerNm, TcpaMin = c.Tcpa / 60.0
            });
        }
    }

    // События оценки — с состоянием своего судна, как у остальных источников
    private void PublishOwn(ExerciseEvent e)
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
