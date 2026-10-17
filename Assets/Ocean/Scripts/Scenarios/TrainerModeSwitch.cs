// ============================================================
// TrainerModeSwitch.cs — Режим тренажёра в сцене: меню, фильтр команд, журнал (перед 19.2, п. B)
//
// Режимы и права — Core/Scenarios/TrainerMode.cs. Здесь:
//   • меню Y «Общее» → «Режим: < экзамен / обучение / инструктор >» — в любой момент, и во время
//     упражнения (решение пользователя: без блокировки, но с записью); то же — кнопка в окне инструктора;
//   • фильтр меню (BridgeMenu.AccessFilter): группа «Отладка» — только «инструктор»; группа
//     «Подсказки» и команды «(инстр.)» вне группы «Сценарий» — не в «экзамене»
//     (перезапуск и досрочное завершение сценария остаются: без них автономная станция встанет);
//   • при переходе в «экзамен» подсказки выключаются (мели на воде, все цели на планшете);
//   • каждое переключение — событие шины TrainerModeChanged (консоль, events.csv, журнал
//     report.html, окно инструктора), на «Начать» — состояние на начало упражнения.
// Компонент ставится сам при загрузке сцены (на объект ScenarioRunner) — Setup не нужен.
// ============================================================
using System.Collections.Generic;
using UnityEngine;

public class TrainerModeSwitch : MonoBehaviour
{
    public const string DebugGroup = "Отладка";
    public const string HintsGroup = "Подсказки";
    private const string ScenarioGroup = "Сценарий";
    private const string MenuGroup = "Общее";

    [Tooltip("Режим при запуске (по умолчанию — инструктор: всё доступно, как до появления режимов)")]
    public TrainerMode startMode = TrainerModes.Default;

    public static TrainerModeSwitch Instance { get; private set; }

    private ShipNavigation navigation;
    private readonly List<BridgeMenuCommand> menuCommands = new List<BridgeMenuCommand>();

    // Сцена без компонента — поставить на объект сценария (или отдельный)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    internal static void Ensure()   // 21.2а: и при повторной загрузке сцены — TrainerScenes
    {
        if (FindAnyObjectByType<TrainerModeSwitch>() != null) return;
        ScenarioRunner runner = FindAnyObjectByType<ScenarioRunner>();
        if (runner == null) return;   // не сцена тренажёра
        runner.gameObject.AddComponent<TrainerModeSwitch>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("[Режим] В сцене несколько TrainerModeSwitch — используется первый.", this);
        else Instance = this;
        TrainerModes.Set(startMode);
    }

    void OnEnable()
    {
        BridgeMenu.AccessFilter = Allowed;
        TrainerModes.Changed += OnModeChanged;
        ExerciseEvents.Raised += OnExerciseEvent;
        BridgeMenu.SetGroupOrder(HintsGroup, 90);   // перед «Отладкой» (100)
        menuCommands.Add(BridgeMenu.RegisterStepper(MenuGroup,
            () => $"Режим: {TrainerModes.Title(TrainerModes.Current)}",
            () => Step(-1), () => Step(+1)).InMenu(BridgeMenu.SettingsMenuId));
        Debug.Log($"[Режим] Режим тренажёра: {TrainerModes.Title(TrainerModes.Current)} ({TrainerModes.Rights(TrainerModes.Current)}).", this);
    }

    void OnDisable()
    {
        if (BridgeMenu.AccessFilter == (System.Func<BridgeMenuCommand, bool>)Allowed) BridgeMenu.AccessFilter = null;
        TrainerModes.Changed -= OnModeChanged;
        ExerciseEvents.Raised -= OnExerciseEvent;
        foreach (BridgeMenuCommand c in menuCommands) BridgeMenu.Unregister(c);
        menuCommands.Clear();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Сменить режим; byInstructor — из окна инструктора (пишется в событие).</summary>
    public void SetMode(TrainerMode mode, bool byInstructor = false)
    {
        lastByInstructor = byInstructor;
        TrainerModes.Set(mode);
        lastByInstructor = false;
    }

    /// <summary>Следующий режим по кругу: экзамен → обучение → инструктор → экзамен.</summary>
    public void Cycle(bool byInstructor = false) =>
        SetMode((TrainerMode)(((int)TrainerModes.Current + 1) % 3), byInstructor);

    private bool lastByInstructor;

    private void Step(int delta)
    {
        int i = Mathf.Clamp((int)TrainerModes.Current + delta, (int)TrainerMode.Exam, (int)TrainerMode.Instructor);
        SetMode((TrainerMode)i);
    }

    // Что показывать в меню шлема при текущем режиме
    private static bool Allowed(BridgeMenuCommand c)
    {
        TrainerMode m = TrainerModes.Current;
        if (c.Group == DebugGroup) return TrainerModes.DebugAllowed(m);
        if (c.Group == HintsGroup) return TrainerModes.HintsAllowed(m);
        if (c.InstructorOnly && c.Group != ScenarioGroup) return TrainerModes.InstructorCommandsAllowed(m);
        return true;
    }

    private void OnModeChanged(TrainerMode old, TrainerMode mode)
    {
        Debug.Log($"[Режим] РЕЖИМ ТРЕНАЖЁРА: {TrainerModes.Title(mode)} (был: {TrainerModes.Title(old)}; {TrainerModes.Rights(mode)})" +
                  (ExerciseEvents.Active ? $", время упражнения {ScenarioText.Clock(ExerciseEvents.Time)}" : "") +
                  (lastByInstructor ? ", окно инструктора" : "") + ".", this);
        if (!TrainerModes.HintsAllowed(mode)) HideHints();
        Publish(mode, (int)old, lastByInstructor);
        BridgeMenu.RebuildOpen();
    }

    // Экзамен: подсказки, включённые раньше, выключаются
    private static void HideHints()
    {
        WaterAreaView view = FindAnyObjectByType<WaterAreaView>();
        if (view != null) view.showShoalsOnWater = false;
        NavPlotter plotter = FindAnyObjectByType<NavPlotter>();
        if (plotter != null && plotter.RevealAll) plotter.SetRevealAll(false);
    }

    // Журнал упражнения начинается с «Начать»: режим на начало — тоже в журнал
    private void OnExerciseEvent(ExerciseEvent e)
    {
        if (e.Type == ExerciseEventType.ExerciseStarted) Publish(TrainerModes.Current, -1, false);
    }

    private void Publish(TrainerMode mode, int previous, bool byInstructor)
    {
        var e = new ExerciseEvent(ExerciseEventType.TrainerModeChanged) { Index = (int)mode, Value = previous, ByInstructor = byInstructor };
        if (navigation == null) navigation = FindAnyObjectByType<ShipNavigation>();
        if (navigation != null && navigation.Model != null)
        {
            e.Position = navigation.PositionEastNorth;
            e.HeadingDeg = navigation.Heading;
            e.SpeedKnots = navigation.SpeedKnots;
        }
        ExerciseEvents.Publish(e);
    }
}
