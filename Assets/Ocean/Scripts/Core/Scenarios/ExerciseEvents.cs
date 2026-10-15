// ============================================================
// ExerciseEvents.cs — Шина событий упражнения (B2)
//
// Через шину работают модули, которые не должны ссылаться друг на друга:
// источники (ScenarioRunner — маршрут и жизненный цикл, GroundingMonitor —
// мели, B3 — столкновения, D1 — вводные) только публикуют события;
// потребители (консоль, запись C1, оценка C2, тактильный отклик D3) только
// подписываются. Шина — статическая, как реестр меню BridgeMenu.
// Рассылка синхронная; событие, опубликованное подписчиком во время рассылки,
// доставляется после текущего (порядок «причина → следствие» у всех подписчиков).
//
// Время события — время упражнения (симуляционное, с от «Начать»): его ведёт
// ScenarioRunner в FixedUpdate (ExerciseEvents.Time), шина ставит его в каждое
// событие сама — у всех источников одни часы. События вне упражнения
// (свободное плавание) рассылаются, но в журнал упражнения не попадают.
//
// Одна запись на все типы событий — строка events.csv (C1, SessionFormat).
// Значения Index / Value / Value2 по типам:
//   WaypointPassed       Index — номер точки,  Value — XTE при прохождении, м;  Value2 — XTE доп., м
//   CorridorExit         Index — номер участка, Value — XTE, м;                 Value2 — XTE доп., м
//   CorridorReturn       Index — номер участка, Value — max |XTE| за выход, м;   Value2 — длительность выхода, с
//   ShoalWarning*/Grounded/Refloated  Index — номер опасности, Value — расстояние до неё, м; Value2 — глубина, м
//   Collision (B3)       Subject — id цели, Index — номер цели в сценарии,
//                        Value — относительная скорость в момент контакта, уз;
//                        Value2 — курсовой угол цели со своего судна, ° (0…360, по часовой от носа)
//   ExerciseEnded        EndReason — причина, Value — пройдено точек, Value2 — всего точек
// Действия обучающегося (C1, источник — BridgeActionMonitor, ControlActionDetector):
//   HelmStarted          Value — исходный заданный руль, ° (> 0 — ПБ)
//   HelmEnded            Value — итоговый заданный руль, °;  Value2 — длительность перекладки, с
//   TelegraphChanged     Index — положение (TelegraphOrder), Value — новый SF; Value2 — прежний SF;
//                        ByInstructor — положение поставлено кодом (не рукой)
//   HornStarted / HornEnded   Value (у HornEnded) — длительность звучания, с;
//                        Index (у HornEnded, C4) — тип звука по правилу 32 (BlastKind)
//   WeatherChanged       Index — новый пресет (WeatherPresetId), Value — прежний пресет
//   TimeOfDayChanged (B4) Index — новое время суток (TimeOfDay), Value — прежнее
// Оценка (C2, источник — ExerciseAssessmentMonitor; публикуются через 0,2 с — на следующем срезе):
//   TargetDetected       Subject — id цели, Index — номер цели, Value — дистанция, мили; Value2 — дистанция обнаружения, мили
//   EncounterStarted     Subject — id цели, Index — ситуация (EncounterSituation), Value — CPA, мили; Value2 — TCPA, мин
// Ограниченная видимость и звуковые сигналы (C4):
//   VisibilityChanged    Value — видимость, заданная сценарием / инструктором, м (0 — по погоде); Value2 — прежняя
//   SoundSignal          свой сигнал (группа звуков, источник — ControlActionDetector): Index — SoundSignalKind,
//                        Subject — запись звуками («— —»), Value — число звуков, Value2 — длительность сигнала, с;
//                        офлайн-оценка его не читает, а пересчитывает по HornEnded
//   TargetSoundSignal    туманный сигнал цели дошёл до своего судна (источник — TrafficController):
//                        Subject — id цели, Index — SoundSignalKind, Value — дистанция в момент подачи, м;
//                        Value2 — курсовой угол цели, ° (0…360)
// Окно инструктора (D1; источники — ScenarioRunner и InstructorInputs, ByInstructor = true):
//   ExercisePaused       пауза: модель, цели и время упражнения стоят (время события — момент паузы)
//   ExerciseResumed      Value — длительность паузы по часам, с
//   EngineFailure        вводная «отказ главного двигателя»: Subject — положение телеграфа, Value — SF в момент отказа
//   EngineRestored       Value — сколько длился отказ, с (время упражнения)
//   SteeringFailure      вводная «отказ рулевого устройства»: Value — заклиненный фактический руль, ° (> 0 — ПБ);
//                        Value2 — заданный руль в момент отказа, °
//   SteeringRestored     Value — сколько длился отказ, с
// Навигационный планшет (D2; источник — NavPlotter, ByInstructor = true):
//   PlotterRevealTargets режим инструктора / теста «все цели»: Value — 1 включён (обучающийся видит на планшете
//                        все цели, в том числе не обнаруженные и скрытые флагом сценария), 0 — выключен
// Режим тренажёра (перед 19.2, п. B; источник — TrainerModeSwitch, TrainerMode.cs):
//   TrainerModeChanged   Index — новый режим (TrainerMode: 0 экзамен, 1 обучение, 2 инструктор);
//                        Value — прежний режим, −1 — состояние на начало упражнения (при ExerciseStarted);
//                        ByInstructor — переключено из окна инструктора
// Швартовка (19.2б; источники — MooringController по событиям MooringSystem, ShipControlOrders):
//   BerthingImpact       навал на кранцы: Value — скорость точки контакта к кромке, уз; Value2 — энергия,
//                        поглощённая кранцами, кДж; Index — наибольшее обжатие, % номинала; Subject — число кранцев
//   FenderOverload       кранец обжат сверх номинала: Index — кранец (с 0), Value — δ/δ_н
//   QuayImpact           удар корпусом о кромку причала: Value — скорость к кромке, уз
//   LineMadeFast         конец подан: Index — конец (MooringLineKind), Subject — название, Value — длина, м; Value2 — кнехт (с 0)
//   LineReleased         конец отдан: Index, Subject
//   LineOverload         натяжение > 0,5·MBL: Index, Subject, Value — T, кН; Value2 — T/MBL
//   LineOverloadCleared  натяжение < 0,45·MBL: Index, Subject
//   LineBroken           обрыв: Index, Subject, Value — наибольшее натяжение, кН
//   ThrusterChanged      положение рычага подруливающего: Index — ThrusterOrder (0 ЛБ 100 … 4 ПБ 100),
//                        Value — команда, % (> 0 — ПБ); Value2 — прежняя, %; Subject — «рычаг» / «меню»
//   ControlOrder         команда из меню «Управление» (кнопка B, «как рулевому»): Subject — орган
//                        («руль» / «машина» / «подруливающее»), Value — новое значение (°, SF, %), Value2 — прежнее
//   LineCommand (19.2б-2) команда концу из меню B: Index — конец, Value — LineOrder (0 подать, 1 выбирать, 2 травить,
//                        3 держать, 4 на кнехты, 5 отдать), Value2 — 1 выполнена / 0 нет; Subject — конец (при отказе — «: причина»)
//   StationChanged (19.2б-2) переход обучающегося: Index — BridgeStation (0 у штурвала, 1 крыло ЛБ, 2 крыло ПБ), Value — прежнее
// XTE > 0 — судно правее линии пути.
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

public enum ExerciseEventType
{
    ScenarioLoaded,
    ExerciseStarted,
    WaypointPassed,
    CorridorExit,
    CorridorReturn,
    ShoalWarningStarted,
    ShoalWarningCleared,
    Grounded,
    Refloated,
    Collision,          // B3
    ExerciseEnded,
    // Действия обучающегося (C1) — новые типы только в конец
    HelmStarted,
    HelmEnded,
    TelegraphChanged,
    HornStarted,
    HornEnded,
    WeatherChanged,
    TimeOfDayChanged,   // B4
    // Оценка (C2, источник — ExerciseAssessmentMonitor): для разбора и окна инструктора;
    // офлайн-оценка их не читает, а пересчитывает
    TargetDetected,
    EncounterStarted,
    // Ограниченная видимость и звуковые сигналы (C4)
    VisibilityChanged,
    SoundSignal,
    TargetSoundSignal,
    // Окно инструктора (D1): пауза и вводные
    ExercisePaused,
    ExerciseResumed,
    EngineFailure,
    EngineRestored,
    SteeringFailure,
    SteeringRestored,
    // Навигационный планшет (D2)
    PlotterRevealTargets,
    // Режим тренажёра (перед 19.2)
    TrainerModeChanged,
    // Швартовка (19.2б)
    BerthingImpact,
    FenderOverload,
    QuayImpact,
    LineMadeFast,
    LineReleased,
    LineOverload,
    LineOverloadCleared,
    LineBroken,
    ThrusterChanged,
    ControlOrder,
    // Швартовка, часть 2 (19.2б-2)
    LineCommand,
    StationChanged
}

public enum ScenarioEndReason
{
    None,
    Arrived,
    Grounding,
    Collision,
    TimeLimit,
    Aborted
}

/// <summary>Событие упражнения.</summary>
public struct ExerciseEvent
{
    public ExerciseEventType Type;
    /// <summary>Время упражнения (симуляционное), с — ставит шина.</summary>
    public double Time;
    /// <summary>Событие произошло во время упражнения (а не в свободном плавании).</summary>
    public bool InExercise;
    public WorldPosition Position;
    public float HeadingDeg;
    public float SpeedKnots;
    public int Index;
    public double Value;
    public double Value2;
    /// <summary>Имя точки, опасности, сценария.</summary>
    public string Subject;
    public ScenarioEndReason EndReason;
    public bool ByInstructor;

    public ExerciseEvent(ExerciseEventType type)
    {
        Type = type;
        Time = 0; InExercise = false;
        Position = default; HeadingDeg = 0f; SpeedKnots = 0f;
        Index = -1; Value = double.NaN; Value2 = double.NaN;
        Subject = ""; EndReason = ScenarioEndReason.None; ByInstructor = false;
    }

    public override string ToString()
    {
        string head = $"t = {ScenarioText.Clock(Time)}";
        string ship = $"{Position}, курс {Math.Round(HeadingDeg, 1) % 360.0:F1}°, {SpeedKnots:F1} уз";
        return $"{head}  {Describe()}. {ship}";
    }

    /// <summary>Описание события на русском, без времени и состояния судна (консоль, столбец description).</summary>
    public string Describe()
    {
        switch (Type)
        {
            case ExerciseEventType.ScenarioLoaded: return $"Сценарий «{Subject}» загружен, брифинг";
            case ExerciseEventType.ExerciseStarted: return $"НАЧАЛО упражнения «{Subject}»";
            case ExerciseEventType.WaypointPassed: return $"Пройдена точка {Index} «{Subject}», XTE {Value:+0;−0;0} м (доп. {Value2:F0} м)";
            case ExerciseEventType.CorridorExit: return $"ВЫХОД ИЗ КОРИДОРА на участке {Index}, XTE {Value:+0;−0;0} м (доп. {Value2:F0} м)";
            case ExerciseEventType.CorridorReturn: return $"Возврат в коридор на участке {Index}: max |XTE| {Value:F0} м, вне коридора {Value2:F0} с";
            case ExerciseEventType.ShoalWarningStarted: return $"Опасное приближение к «{Subject}» ({Value2:F1} м), D = {Value:F0} м";
            case ExerciseEventType.ShoalWarningCleared: return $"Опасность миновала, D = {Value:F0} м";
            case ExerciseEventType.Grounded: return $"ПОСАДКА НА МЕЛЬ «{Subject}» ({Value2:F1} м)";
            case ExerciseEventType.Refloated: return $"Снятие с мели «{Subject}»{(ByInstructor ? " (инструктор)" : " своим ходом")}";
            case ExerciseEventType.Collision:
                return double.IsNaN(Value)
                    ? $"СТОЛКНОВЕНИЕ с «{Subject}»"
                    : $"СТОЛКНОВЕНИЕ с «{Subject}»: относительная скорость {ScenarioText.Num(Value, 1)} уз, цель {ScenarioText.RelativeBearing(Value2)}";
            case ExerciseEventType.ExerciseEnded: return $"КОНЕЦ упражнения: {ScenarioText.EndReason(EndReason)}{(ByInstructor ? " (инструктор)" : "")}, точек {Value:F0}/{Value2:F0}";
            case ExerciseEventType.HelmStarted: return $"Перекладка руля начата (было {ScenarioText.Rudder(Value)})";
            case ExerciseEventType.HelmEnded: return $"Руль переложен на {ScenarioText.Rudder(Value)} за {ScenarioText.Num(Value2, 1)} с";
            case ExerciseEventType.TelegraphChanged: return $"Телеграф: «{Subject}»{(ByInstructor ? " (инструктор)" : "")}";
            case ExerciseEventType.HornStarted: return "Гудок";
            case ExerciseEventType.HornEnded:
                return $"Гудок окончен, {ScenarioText.Num(Value, 1)} с" + (Index >= 0 ? $" — {SoundSignals.BlastTitle((BlastKind)Index)} звук" : "");
            case ExerciseEventType.VisibilityChanged:
                return Value > 0 ? $"Видимость {ScenarioText.Num(Value / 1852.0, 2)} мили ({ScenarioText.Num(Value)} м)" : "Видимость — по погоде";
            case ExerciseEventType.SoundSignal:
                return $"Звуковой сигнал «{Subject}»: {SoundSignals.Title((SoundSignalKind)Index)}";
            case ExerciseEventType.TargetSoundSignal:
                return $"Слышен сигнал «{Subject}»: {SoundSignals.Title((SoundSignalKind)Index)}; {ScenarioText.Num(Value / 1852.0, 2)} мили, {ScenarioText.RelativeBearing(Value2)}";
            case ExerciseEventType.WeatherChanged: return $"Погода: {Subject}";
            case ExerciseEventType.TimeOfDayChanged: return $"Время суток: {Subject}";
            case ExerciseEventType.TargetDetected:
                return $"Обнаружена цель «{Subject}»: {ScenarioText.Num(Value, 2)} мили (дальность обнаружения {ScenarioText.Num(Value2, 1)} мили)";
            case ExerciseEventType.EncounterStarted:
                return $"«{Subject}»: {EncounterClassifier.Title((EncounterSituation)Index)} — {EncounterClassifier.Duty((EncounterSituation)Index)}; CPA {ScenarioText.Num(Value, 2)} мили, TCPA {ScenarioText.Num(Value2, 1)} мин";
            case ExerciseEventType.ExercisePaused: return "ПАУЗА (инструктор)";
            case ExerciseEventType.ExerciseResumed: return double.IsNaN(Value) ? "Продолжение после паузы" : $"Продолжение после паузы ({ScenarioText.Num(Value)} с по часам)";
            case ExerciseEventType.EngineFailure: return $"ВВОДНАЯ: отказ главного двигателя (телеграф «{Subject}»)";
            case ExerciseEventType.EngineRestored: return $"Главный двигатель восстановлен (отказ {ScenarioText.Num(Value)} с)";
            case ExerciseEventType.SteeringFailure: return $"ВВОДНАЯ: отказ рулевого устройства — руль заклинило на {ScenarioText.Rudder(Value)}";
            case ExerciseEventType.SteeringRestored: return $"Рулевое устройство восстановлено (отказ {ScenarioText.Num(Value)} с)";
            case ExerciseEventType.PlotterRevealTargets:
                return Value > 0 ? "Планшет: показаны ВСЕ цели (инструктор)" : "Планшет: все цели скрыты — только обнаруженные";
            case ExerciseEventType.TrainerModeChanged:
                return Value < 0
                    ? $"Режим тренажёра на начало: {TrainerModes.Title(TrainerModes.FromIndex(Index))}"
                    : $"РЕЖИМ ТРЕНАЖЁРА: {TrainerModes.Title(TrainerModes.FromIndex(Index))} (был: {TrainerModes.Title(TrainerModes.FromIndex((int)Value))})" +
                      (ByInstructor ? " (окно инструктора)" : "");
            case ExerciseEventType.BerthingImpact:
                return $"Навал на кранцы: {ScenarioText.Num(Value, 2)} уз, энергия {ScenarioText.Num(Value2, 1)} кДж, " +
                       $"обжатие {Index} % номинала, кранцев {Subject}";
            case ExerciseEventType.FenderOverload: return $"КРАНЕЦ {Index + 1} ПЕРЕГРУЖЕН: обжатие {ScenarioText.Num(Value * 100.0)} % номинала";
            case ExerciseEventType.QuayImpact: return $"УДАР О ПРИЧАЛ корпусом: {ScenarioText.Num(Value, 2)} уз";
            case ExerciseEventType.LineMadeFast: return $"Подан {Subject}: {ScenarioText.Num(Value, 1)} м, кнехт {ScenarioText.Num(Value2 + 1)}";
            case ExerciseEventType.LineReleased: return $"Отдан {Subject}";
            case ExerciseEventType.LineOverload: return $"ПЕРЕГРУЗКА: {Subject}, {ScenarioText.Num(Value)} кН ({ScenarioText.Num(Value2 * 100.0)} % MBL)";
            case ExerciseEventType.LineOverloadCleared: return $"Натяжение в норме: {Subject}";
            case ExerciseEventType.LineBroken: return $"ОБРЫВ: {Subject}, {ScenarioText.Num(Value)} кН";
            case ExerciseEventType.ThrusterChanged:
                return $"Подруливающее: {ThrusterOrders.TitleOfPercent(Value)} ({Subject})";
            case ExerciseEventType.ControlOrder:
                return $"Команда «{Subject}»: {ControlOrders.ValueText(Subject, Value)} (было {ControlOrders.ValueText(Subject, Value2)})";
            case ExerciseEventType.LineCommand:
                return $"Концы: «{LineOrders.Title((LineOrder)(int)Value)}» — {Subject}" + (Value2 > 0.5 ? "" : " (НЕ ВЫПОЛНЕНО)");
            case ExerciseEventType.StationChanged:
                return $"Переход: {BridgeStations.Title((BridgeStation)Index)} (был: {BridgeStations.Title((BridgeStation)(int)Value)})";
        }
        return $"{Type} {Subject}";
    }
}

public static class ExerciseEvents
{
    private static readonly List<ExerciseEvent> log = new List<ExerciseEvent>();

    /// <summary>Событие опубликовано (вызывается синхронно, в том же шаге).</summary>
    public static event Action<ExerciseEvent> Raised;

    /// <summary>Время упражнения, с (симуляционное) — ведёт ScenarioRunner.</summary>
    public static double Time;
    /// <summary>Идёт упражнение (между «Начать» и завершением).</summary>
    public static bool Active { get; private set; }
    /// <summary>Идентификатор сценария текущего / последнего упражнения.</summary>
    public static string ScenarioId { get; private set; } = "";
    /// <summary>Журнал текущего (или последнего завершённого) упражнения.</summary>
    public static IReadOnlyList<ExerciseEvent> Log => log;

    // Если в настройках Play Mode отключена перезагрузка домена — не тащить подписчиков прошлого запуска
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        Raised = null;
        log.Clear();
        Time = 0;
        Active = false;
        ScenarioId = "";
        pending.Clear();
        dispatching = false;
    }

    /// <summary>Начало упражнения: журнал очищается, время — с нуля.</summary>
    public static void BeginExercise(string scenarioId)
    {
        log.Clear();
        Time = 0;
        ScenarioId = scenarioId ?? "";
        Active = true;
    }

    /// <summary>Конец упражнения: журнал сохраняется до следующего начала.</summary>
    public static void EndExercise() => Active = false;

    // Событие, опубликованное подписчиком во время рассылки (Grounded → ScenarioRunner → ExerciseEnded),
    // доставляется ПОСЛЕ текущего — всем подписчикам в порядке причина → следствие
    // (найдено в шлеме: консоль получала «конец» раньше «посадки»).
    private static readonly Queue<ExerciseEvent> pending = new Queue<ExerciseEvent>();
    private static bool dispatching;

    public static void Publish(ExerciseEvent e)
    {
        e.Time = Time;
        e.InExercise = Active;
        if (Active) log.Add(e);

        pending.Enqueue(e);
        if (dispatching) return;
        dispatching = true;
        try
        {
            while (pending.Count > 0) Dispatch(pending.Dequeue());
        }
        finally { dispatching = false; }
    }

    private static void Dispatch(ExerciseEvent e)
    {
        Action<ExerciseEvent> handlers = Raised;
        if (handlers == null) return;
        // Ошибка одного подписчика не должна мешать остальным
        foreach (Delegate d in handlers.GetInvocationList())
        {
            try { ((Action<ExerciseEvent>)d)(e); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }
}
