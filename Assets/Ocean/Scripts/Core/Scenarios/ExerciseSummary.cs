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

    public static ExerciseSummary From(ScenarioDefinition scenario, IReadOnlyList<ExerciseEvent> log, RouteTracker route)
    {
        var s = new ExerciseSummary
        {
            ScenarioId = scenario != null ? scenario.id : "",
            Title = scenario != null ? scenario.title : "",
            WaypointsTotal = route != null && route.Valid ? route.WaypointCount - 1 : 0
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
                    case ExerciseEventType.Grounded: s.Groundings++; break;
                    case ExerciseEventType.ExerciseEnded:
                        s.Reason = e.EndReason; s.ByInstructor = e.ByInstructor; s.Duration = e.Time; break;
                }
            }
        return s;
    }

    /// <summary>Текст для VR-панели (TextMeshPro, rich text).</summary>
    public string ToPanelText()
    {
        var b = new StringBuilder();
        b.AppendLine($"<b>{ScenarioText.EndReason(Reason)}</b>{(ByInstructor ? " (инструктор)" : "")}");
        b.AppendLine();
        b.AppendLine($"Время упражнения: {ScenarioText.Clock(Duration)}");
        b.AppendLine($"Пройдено путевых точек: {WaypointsPassed} из {WaypointsTotal}");
        b.AppendLine($"Наибольшее боковое отклонение: {ScenarioText.Num(MaxAbsXte)} м");
        b.AppendLine($"Выходов из коридора: {CorridorExits}, вне коридора {ScenarioText.Num(TimeOutsideCorridor)} с");
        b.AppendLine($"Опасных приближений к мели: {ShoalWarnings}");
        b.AppendLine($"Посадок на мель: {Groundings}");
        b.AppendLine();
        b.Append("<size=80%><color=#9AA6B2>Предварительный итог. Оценка по критериям и разбор — в следующих версиях.</color></size>");
        return b.ToString();
    }
}
