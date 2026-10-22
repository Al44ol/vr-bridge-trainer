// ============================================================
// ScenarioTriggers.cs — Триггеры сценария «если — то» в тренажёре (запрос 21.6а)
//
// Ставится сам на объект Scenario (RuntimeInitializeOnLoadMethod; при повторной загрузке сцены — TrainerScenes).
// Исполнитель — Core TriggerRunner: сброс при загрузке сценария и на «Начать»; шаг — в FixedUpdate упражнения
// (порядок −87: после ScenarioRunner −88 — время и события маршрута этого шага уже есть; до TrafficController −86 —
// приказ цели действует в том же шаге). Пауза (брифинг, инструктор) — триггеры стоят.
// Факты из шины: WaypointPassed (имя точки), MooringStage / TugWorkStage (номер этапа).
// Действия — теми же путями, что вводные инструктора:
//   цель — TrafficVessel.Order (+ TargetOrder в шину); погода — WeatherController.SetWeather; видимость —
//   InstructorInputs.SetVisibility (VisibilityChanged пишет BridgeActionMonitor); ветер — TowingController.SetWind
//   («триггер»); отказы — InstructorInputs (ByInstructor = false — «по сценарию»); сообщение — ScenarioMessage в шину
//   (табличку в шлеме показывает PilotOrderNotice).
// Окно инструктора — список (Runner, StateText) и «Сработать» (Fire: сразу, без задержки, TriggerFired с ByInstructor).
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-87)]
public class ScenarioTriggers : MonoBehaviour
{
    public static ScenarioTriggers Instance { get; private set; }

    /// <summary>Триггеры загруженного сценария и их состояние.</summary>
    public TriggerRunner Runner { get; } = new TriggerRunner();
    /// <summary>Сценарий, для которого заведены триггеры.</summary>
    public ScenarioDefinition Scenario { get; private set; }

    private ScenarioRunner runner;
    private ShipNavigation navigation;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    internal static void Ensure()   // и при повторной загрузке сцены — TrainerScenes
    {
        if (FindAnyObjectByType<ScenarioTriggers>() != null) return;
        ScenarioRunner r = FindAnyObjectByType<ScenarioRunner>();
        if (r == null) return;
        r.gameObject.AddComponent<ScenarioTriggers>();
    }

    void Awake() => Instance = this;

    void OnEnable() => ExerciseEvents.Raised += OnEvent;

    void OnDisable() => ExerciseEvents.Raised -= OnEvent;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        runner = GetComponent<ScenarioRunner>();
        if (runner == null) runner = ScenarioRunner.Instance;
        navigation = runner != null && runner.navigation != null ? runner.navigation : FindAnyObjectByType<ShipNavigation>();
        if (runner != null && runner.Current != null) Load(runner.Current);
    }

    private void OnEvent(ExerciseEvent e)
    {
        switch (e.Type)
        {
            case ExerciseEventType.ScenarioLoaded:
            case ExerciseEventType.ExerciseStarted:
                if (runner == null) runner = ScenarioRunner.Instance;
                Load(runner != null ? runner.Current : null);
                break;
            case ExerciseEventType.WaypointPassed:
                if (e.InExercise) Runner.WaypointPassed(e.Subject);
                break;
            case ExerciseEventType.MooringStage:
            case ExerciseEventType.TugWorkStage:
                if (e.InExercise) Runner.StageReached(e.Index);
                break;
        }
    }

    private void Load(ScenarioDefinition s)
    {
        Scenario = s;
        Runner.Reset(s != null ? s.triggers : null);
        if (s == null || Runner.Count == 0 || runner == null || runner.CurrentPhase != ScenarioRunner.Phase.Briefing) return;
        var lines = new List<string>();
        foreach (ScenarioTrigger t in Runner.Triggers) lines.Add(TriggerText.Line(t, s));
        Debug.Log($"[Триггеры] «{s.title}»: {Runner.Count} — " + string.Join("; ", lines));
    }

    /// <summary>Идёт упражнение (триггеры работают и запускаются вручную).</summary>
    public bool Running => runner != null && runner.CurrentPhase == ScenarioRunner.Phase.Running && Scenario == runner.Current;

    void FixedUpdate()
    {
        if (!Running || Runner.Count == 0 || navigation == null || navigation.Model == null || navigation.Paused) return;
        List<int> fired = Runner.Step(runner.ExerciseTime, navigation.PositionEastNorth, RangeTo);
        if (fired.Count == 0) return;
        foreach (int i in fired.ToArray()) Apply(i, false);
    }

    /// <summary>Окно инструктора: сработать сейчас (без задержки); false — не идёт упражнение или уже сработал.</summary>
    public bool Fire(int index)
    {
        if (!Running || !Runner.Fire(index, runner.ExerciseTime)) return false;
        Apply(index, true);
        return true;
    }

    // Дистанция до цели по id, м (NaN — нет такой цели)
    private double RangeTo(string id)
    {
        TrafficVessel v = Target(id);
        return v != null ? (v.Position - navigation.PositionEastNorth).Magnitude : double.NaN;
    }

    private static TrafficVessel Target(string id)
    {
        TrafficController tc = TrafficController.Instance;
        if (tc == null || string.IsNullOrEmpty(id)) return null;
        foreach (TrafficVessel v in tc.Vessels) if (v.TargetId == id) return v;
        return null;
    }

    private void Apply(int index, bool manual)
    {
        ScenarioTrigger t = Runner.Triggers[index];
        Publish(new ExerciseEvent(ExerciseEventType.TriggerFired) { Index = index, Subject = t.Label, ByInstructor = manual });
        Debug.Log($"[Триггеры] t = {ScenarioText.Clock(runner.ExerciseTime)}  {TriggerText.Line(t, Scenario)}{(manual ? " — вручную" : "")}");
        if (t.actions == null) return;
        foreach (TriggerAction a in t.actions)
        {
            if (a == null) continue;
            try { Do(a); }
            catch (Exception ex) { Debug.LogException(ex, this); }
        }
    }

    private void Do(TriggerAction a)
    {
        InstructorInputs inputs = InstructorInputs.Instance;
        switch (a.kind)
        {
            case TriggerActionKind.TargetOrder:
            {
                TrafficVessel v = Target(a.target);
                if (v == null) { Debug.LogWarning($"[Триггеры] Нет цели «{a.target}» — приказ не выполнен.", this); return; }
                double course = a.ChangesCourse ? a.courseDeg : double.NaN;
                double speed = a.ChangesSpeed ? a.speedKnots : double.NaN;
                v.Order(course, speed);
                Publish(new ExerciseEvent(ExerciseEventType.TargetOrder) { Subject = v.TargetId, Index = v.Index, Value = course, Value2 = speed });
                return;
            }
            case TriggerActionKind.Weather:
                if (runner.weather != null) runner.weather.SetWeather(a.weather);
                return;
            case TriggerActionKind.Visibility:
                if (inputs != null) inputs.SetVisibility(a.visibilityNm * 1852f);
                else if (runner.weather != null) runner.weather.SetVisibility(a.visibilityNm * 1852f);
                return;
            case TriggerActionKind.Wind:
                if (TowingController.Instance != null) TowingController.Instance.SetWind(a.windKnots, a.windFromDeg, "триггер");
                return;
            case TriggerActionKind.EngineFailure: if (inputs != null) inputs.SetEngineFailure(true, false); return;
            case TriggerActionKind.EngineRestore: if (inputs != null) inputs.SetEngineFailure(false, false); return;
            case TriggerActionKind.SteeringFailure: if (inputs != null) inputs.SetSteeringFailure(true, false); return;
            case TriggerActionKind.SteeringRestore: if (inputs != null) inputs.SetSteeringFailure(false, false); return;
            case TriggerActionKind.Message:
                Publish(new ExerciseEvent(ExerciseEventType.ScenarioMessage) { Subject = a.text ?? "" });
                return;
        }
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
