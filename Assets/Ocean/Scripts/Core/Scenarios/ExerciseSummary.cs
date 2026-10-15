// ============================================================
// ExerciseSummary.cs — Предварительный итог упражнения (B2)
//
// Собирается из журнала шины ExerciseEvents и статистики RouteTracker.
// Сейчас — экран результатов-заглушка; в C2/C3 из тех же данных строятся
// оценка и отчёт (в том числе офлайн по записанной сессии).
// ============================================================
using System.Collections.Generic;
using System.Text;

public sealed class ExerciseSummary
{
    public string ScenarioId = "";
    public string Title = "";
    public ScenarioEndReason Reason;
    public bool ByInstructor;
    /// <summary>Длительность упражнения (симуляционная), с.</summary>
    public double Duration;
    public int WaypointsPassed, WaypointsTotal;
    public int CorridorExits;
    public double MaxAbsXte;
    public double TimeOutsideCorridor;
    public int ShoalWarnings, Groundings;
    /// <summary>Столкновения (B3) и с чем было первое.</summary>
    public int Collisions;
    public string FirstCollisionWith = "";
    public string FirstGroundingOn = "";
    /// <summary>Провал упражнения: посадка на мель или столкновение (C2 — независимо от остальных баллов).</summary>
    public bool Failed => Groundings > 0 || Collisions > 0;
    /// <summary>Действия обучающегося (C1): перекладки руля, команды телеграфу, гудки, смены погоды.</summary>
    public int HelmActions, TelegraphOrders, HornSignals, WeatherChanges;

    /// <summary>Режимы тренажёра за упражнение (перед 19.2, п. B): был ли режим обучения или инструктора.</summary>
    public TrainerModeUsage Modes = new TrainerModeUsage();

    /// <summary>19.2б-3: сценарий швартовки — выполнено этапов задания (из 4) и время «ошвартован», с (NaN — нет).</summary>
    public bool MooringTask;
    public int MooringStagesDone;
    public double MooredAt = double.NaN;

    public static ExerciseSummary From(ScenarioDefinition scenario, IReadOnlyList<ExerciseEvent> log, RouteTracker route)
    {
        var s = new ExerciseSummary
        {
            ScenarioId = scenario != null ? scenario.id : "",
            Title = scenario != null ? scenario.title : "",
            WaypointsTotal = route != null && route.Valid ? route.WaypointCount - 1 : 0,
            MooringTask = scenario != null && scenario.IsMooring
        };
        if (route != null)
        {
            s.WaypointsPassed = route.PassedCount;
            s.CorridorExits = route.CorridorExits;
            s.MaxAbsXte = route.MaxAbsXte;
            s.TimeOutsideCorridor = route.TimeOutside;
        }
        if (log != null)
            foreach (ExerciseEvent e in log)
            {
                switch (e.Type)
                {
                    case ExerciseEventType.ShoalWarningStarted: s.ShoalWarnings++; break;
                    case ExerciseEventType.Grounded:
                        if (s.Groundings++ == 0) s.FirstGroundingOn = e.Subject ?? "";
                        break;
                    case ExerciseEventType.Collision:
                        if (s.Collisions++ == 0) s.FirstCollisionWith = e.Subject ?? "";
                        break;
                    case ExerciseEventType.HelmEnded: s.HelmActions++; break;
                    case ExerciseEventType.TelegraphChanged: if (!e.ByInstructor) s.TelegraphOrders++; break;
                    case ExerciseEventType.HornStarted: s.HornSignals++; break;
                    case ExerciseEventType.WeatherChanged: s.WeatherChanges++; break;
                    case ExerciseEventType.MooringStage:
                        s.MooringStagesDone++;
                        if (e.Index == (int)MooringStage.Moored) s.MooredAt = e.Time;
                        break;
                    case ExerciseEventType.ExerciseEnded:
                        s.Reason = e.EndReason; s.ByInstructor = e.ByInstructor; s.Duration = e.Time; break;
                }
            }
        s.Modes = TrainerModeUsage.From(log);
        return s;
    }

    /// <summary>Ход выполнения: путевые точки или (сценарий швартовки) задания.</summary>
    public string Progress => MooringTask ? $"заданий {MooringStagesDone} из {MooringTaskTracker.StageCount}" : $"точек {WaypointsPassed} из {WaypointsTotal}";

    /// <summary>Причина провала словами (пусто — не провал).</summary>
    public string FailureText()
    {
        if (Collisions > 0) return $"столкновение с «{FirstCollisionWith}»";
        if (Groundings > 0) return $"посадка на мель «{FirstGroundingOn}»";
        return "";
    }

    /// <summary>Короткий итог (вместе с оценкой C2 на панели результатов).</summary>
    public string ToPanelTextShort(bool markFailure = false)
    {
        var b = new StringBuilder();
        if (markFailure && Failed)
            b.AppendLine($"<color=#FF5A4A><b>УПРАЖНЕНИЕ ПРОВАЛЕНО:</b> {FailureText()}</color>");
        b.AppendLine($"<b>{ScenarioText.EndReason(Reason)}</b>{(ByInstructor ? " (инструктор)" : "")} · {ScenarioText.Clock(Duration)} · {Progress}");
        string route = MooringTask ? "" : $"max XTE {ScenarioText.Num(MaxAbsXte)} м, вне коридора {ScenarioText.Num(TimeOutsideCorridor)} с · ";
        b.Append($"<size=85%><color=#9AA6B2>{route}посадок {Groundings}, столкновений {Collisions} · перекладок {HelmActions}, команд телеграфу {TelegraphOrders}, гудков {HornSignals}</color></size>");
        return b.ToString();
    }

    /// <summary>Текст для VR-панели (TextMeshPro, rich text). markFailure — выделить провал красным.</summary>
    public string ToPanelText(bool markFailure = false)
    {
        var b = new StringBuilder();
        if (markFailure && Failed)
            b.AppendLine($"<color=#FF5A4A><b>УПРАЖНЕНИЕ ПРОВАЛЕНО:</b> {FailureText()}</color>");
        b.AppendLine($"<b>{ScenarioText.EndReason(Reason)}</b>{(ByInstructor ? " (инструктор)" : "")}");
        b.AppendLine();
        b.AppendLine($"Время упражнения: {ScenarioText.Clock(Duration)}");
        if (MooringTask) b.AppendLine($"Выполнено заданий швартовки: {MooringStagesDone} из {MooringTaskTracker.StageCount}");
        else b.AppendLine($"Пройдено путевых точек: {WaypointsPassed} из {WaypointsTotal}");
        b.AppendLine($"Наибольшее боковое отклонение: {ScenarioText.Num(MaxAbsXte)} м");
        b.AppendLine($"Выходов из коридора: {CorridorExits}, вне коридора {ScenarioText.Num(TimeOutsideCorridor)} с");
        b.AppendLine($"Опасных приближений к мели: {ShoalWarnings}");
        b.AppendLine($"Посадок на мель: {Groundings}");
        b.AppendLine($"Столкновений: {Collisions}");
        b.AppendLine($"Действия: перекладок руля {HelmActions}, команд телеграфу {TelegraphOrders}, гудков {HornSignals}");
        b.AppendLine();
        b.Append("<size=80%><color=#9AA6B2>Предварительный итог. Оценка по критериям и разбор — в следующих версиях.</color></size>");
        return b.ToString();
    }
}
