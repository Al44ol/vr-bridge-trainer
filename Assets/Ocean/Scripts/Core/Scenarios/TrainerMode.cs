// ============================================================
// TrainerMode.cs — Режим тренажёра: экзамен / обучение / инструктор (перед запросом 19.2, п. B)
//
// Режим определяет, что доступно в меню шлема:
//   • Экзамен   — без подсказок, без команд инструктора «(инстр.)» и без отладки;
//   • Обучение  — подсказки (группа меню «Подсказки»: мели на воде, все цели на планшете,
//                 с 19.2 — натяжение концов цветом) и вводные «(инстр.)», без отладки;
//   • Инструктор — всё, включая группу «Отладка» (силы, телепорт, тест циркуляции).
// Решение пользователя: блокировки нет — режим переключается в любой момент (и во время
// упражнения), но каждое переключение — событие шины TrainerModeChanged (консоль, events.csv,
// журнал report.html), а в итогах упражнения пишется, был ли использован режим обучения или
// инструктора и с какого момента (TrainerModeUsage — из событий, одинаково в шлеме и по записи).
// На оценку режим не влияет.
// Событие: Index — новый режим (int TrainerMode), Value — прежний (−1 — состояние на начало
// упражнения: публикуется при ExerciseStarted). Источник — TrainerModeSwitch (сцена).
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Режим тренажёра; порядок — по возрастанию прав (сравнение «не ниже»).</summary>
public enum TrainerMode
{
    Exam,
    Training,
    Instructor
}

/// <summary>Текущий режим (общий для сцены: меню, подсказки, окно инструктора).</summary>
public static class TrainerModes
{
    /// <summary>Режим по умолчанию — инструктор: до появления режимов всё было доступно.</summary>
    public const TrainerMode Default = TrainerMode.Instructor;

    public static TrainerMode Current { get; private set; } = Default;

    /// <summary>Режим сменился: (прежний, новый).</summary>
    public static event Action<TrainerMode, TrainerMode> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        Current = Default;
        Changed = null;
    }

    /// <summary>Сменить режим; false — уже такой.</summary>
    public static bool Set(TrainerMode mode)
    {
        if (mode == Current) return false;
        TrainerMode old = Current;
        Current = mode;
        Changed?.Invoke(old, mode);
        return true;
    }

    /// <summary>Подсказки (группа «Подсказки»).</summary>
    public static bool HintsAllowed(TrainerMode m) => m >= TrainerMode.Training;
    /// <summary>Команды инструктора «(инстр.)» в меню шлема.</summary>
    public static bool InstructorCommandsAllowed(TrainerMode m) => m >= TrainerMode.Training;
    /// <summary>Группа «Отладка».</summary>
    public static bool DebugAllowed(TrainerMode m) => m == TrainerMode.Instructor;

    public static bool HintsAllowedNow => HintsAllowed(Current);
    public static bool DebugAllowedNow => DebugAllowed(Current);

    public static string Title(TrainerMode m)
    {
        switch (m)
        {
            case TrainerMode.Exam: return "экзамен";
            case TrainerMode.Training: return "обучение";
            default: return "инструктор";
        }
    }

    /// <summary>Что доступно в режиме (для подсказки в меню и ThesisNotes).</summary>
    public static string Rights(TrainerMode m)
    {
        switch (m)
        {
            case TrainerMode.Exam: return "без подсказок, вводных и отладки";
            case TrainerMode.Training: return "подсказки и вводные, без отладки";
            default: return "всё, включая отладку";
        }
    }

    /// <summary>Режим из числа события (Index); вне диапазона — по умолчанию.</summary>
    public static TrainerMode FromIndex(int i) =>
        i >= (int)TrainerMode.Exam && i <= (int)TrainerMode.Instructor ? (TrainerMode)i : Default;
}

/// <summary>
/// Как режимы использовались в упражнении — по событиям TrainerModeChanged (шина или events.csv).
/// Одинаково на панели результатов в шлеме и в report.html.
/// </summary>
public sealed class TrainerModeUsage
{
    /// <summary>В журнале есть события режима (в записях до п. B их нет — строка в итогах не выводится).</summary>
    public bool Recorded;
    public TrainerMode StartMode = TrainerModes.Default;
    /// <summary>Наибольший по правам режим за упражнение.</summary>
    public TrainerMode HighestMode = TrainerMode.Exam;
    /// <summary>С какого момента (время упражнения, с) впервые действовал режим не ниже «обучения» / «инструктора»; NaN — не было.</summary>
    public double TrainingFrom = double.NaN, InstructorFrom = double.NaN;
    /// <summary>Переключений во время упражнения (без состояния на начало).</summary>
    public int Changes;

    public bool UsedInstructor => !double.IsNaN(InstructorFrom);
    public bool UsedTraining => !double.IsNaN(TrainingFrom);

    public static TrainerModeUsage From(IEnumerable<ExerciseEvent> events)
    {
        var u = new TrainerModeUsage();
        if (events == null) return u;
        foreach (ExerciseEvent e in events)
        {
            if (e.Type != ExerciseEventType.TrainerModeChanged) continue;
            TrainerMode m = TrainerModes.FromIndex(e.Index);
            bool snapshot = e.Value < 0;
            if (!u.Recorded)
            {
                // Первое событие — состояние на начало или переключение (тогда начало — прежний режим)
                u.StartMode = snapshot ? m : TrainerModes.FromIndex((int)e.Value);
                u.HighestMode = u.StartMode;
                u.Recorded = true;
                if (!snapshot) u.Note(u.StartMode, 0.0);
            }
            if (!snapshot) u.Changes++;
            u.Note(m, e.Time);
        }
        return u;
    }

    private void Note(TrainerMode m, double t)
    {
        if (m > HighestMode) HighestMode = m;
        if (m >= TrainerMode.Training && double.IsNaN(TrainingFrom)) TrainingFrom = t;
        if (m == TrainerMode.Instructor && double.IsNaN(InstructorFrom)) InstructorFrom = t;
    }

    /// <summary>Строка итогов: «Был использован режим инструктора (с 03:12)…»; пусто — режимы не записаны.</summary>
    public string Line()
    {
        if (!Recorded) return "";
        string tail = Changes > 0 ? $"; начало — {TrainerModes.Title(StartMode)}, переключений {Changes}" : "";
        if (UsedInstructor) return $"Был использован режим инструктора (с {ScenarioText.Clock(InstructorFrom)}{tail})";
        if (UsedTraining) return $"Был использован режим обучения — подсказки доступны (с {ScenarioText.Clock(TrainingFrom)}{tail})";
        return "Режим тренажёра: экзамен (подсказки и отладка не использовались)";
    }
}
