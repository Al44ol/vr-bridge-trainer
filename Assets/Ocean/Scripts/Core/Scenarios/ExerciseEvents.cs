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
//   HornStarted / HornEnded   Value (у HornEnded) — длительность звучания, с
//   WeatherChanged       Index — новый пресет (WeatherPresetId), Value — прежний пресет
//   TimeOfDayChanged (B4) Index — новое время суток (TimeOfDay), Value — прежнее
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
    TimeOfDayChanged    // B4
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
            case ExerciseEventType.HornEnded: return $"Гудок окончен, {ScenarioText.Num(Value, 1)} с";
            case ExerciseEventType.WeatherChanged: return $"Погода: {Subject}";
            case ExerciseEventType.TimeOfDayChanged: return $"Время суток: {Subject}";
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
