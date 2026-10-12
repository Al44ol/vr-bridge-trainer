// ============================================================
// ExerciseTasks.cs — Вкладка «Задание» навигационного планшета (D2)
//
// Что обучающийся должен сделать и как идёт упражнение — только то, что он и так
// знает из брифинга, без подсказок решения:
//   • название, ход упражнения (время и лимит, пройденные точки, следующая точка);
//   • условия — тот же текст, что на панели брифинга (BriefingFacts — единственный
//     источник, его берёт и ScenarioPanel);
//   • полный текст брифинга.
// Не показывается (это проверяют критерии оценки C2/C4 — обучающийся должен решить сам):
// ситуация сближения и обязанность по МППСС, безопасная скорость сценария числом,
// время до следующего туманного сигнала. Проверяет PlotterTests.
// ============================================================
using System;
using System.Text;

public enum TaskPhase { Free, Briefing, Running, Finished }

public static class ExerciseTasks
{
    /// <summary>Условия упражнения — текст панели брифинга (ScenarioPanel).</summary>
    public static string BriefingFacts(ScenarioDefinition s)
    {
        var b = new StringBuilder();
        b.AppendLine("<b>Условия</b>");
        b.AppendLine($"Время суток: {ScenarioText.TimeOfDayTitle(s.timeOfDay)}; погода: {ScenarioText.Weather(s.weather)}");
        OwnShipInitialState i = s.initial;
        if (i != null)
            b.AppendLine($"Начало: курс {i.headingDeg:000}°, {ScenarioText.Num(i.speedKnots, 1)} уз, телеграф «{ScenarioText.Telegraph(i.telegraph)}», руль {ScenarioText.Num(i.rudderDeg)}°");
        b.AppendLine($"Путевых точек: {Math.Max(0, s.route.Count - 1)}, маршрут {ScenarioText.Num(s.RouteLength / 1852.0, 1)} мили");
        if (s.traffic != null && s.traffic.Count > 0) b.AppendLine($"Другие суда в районе: {s.traffic.Count}");
        b.AppendLine(s.timeLimitSeconds > 0 ? $"Лимит времени: {ScenarioText.Clock(s.timeLimitSeconds)}" : "Без лимита времени");
        b.Append($"Завершение: {ScenarioText.EndConditions(s)}");
        return b.ToString();
    }

    /// <summary>Ход упражнения: фаза, время, маршрут (route — RouteTracker упражнения, может быть null).</summary>
    public static string Status(ScenarioDefinition s, TaskPhase phase, double exerciseTimeS, RouteTracker route)
    {
        var b = new StringBuilder();
        switch (phase)
        {
            case TaskPhase.Briefing:
                b.Append("Брифинг — упражнение не начато");
                break;
            case TaskPhase.Finished:
                b.Append($"Упражнение завершено, {ScenarioText.Clock(exerciseTimeS)}");
                break;
            case TaskPhase.Running:
                b.Append($"Время {ScenarioText.Clock(exerciseTimeS)}");
                if (s.timeLimitSeconds > 0)
                    b.Append($" из {ScenarioText.Clock(s.timeLimitSeconds)}, осталось {ScenarioText.Clock(Math.Max(0.0, s.timeLimitSeconds - exerciseTimeS))}");
                if (route != null && route.Valid)
                {
                    int total = route.WaypointCount - 1;
                    b.Append($"\nПройдено точек: {route.PassedCount} из {total}");
                    if (!route.Finished)
                    {
                        int next = Math.Min(route.Leg + 1, route.WaypointCount - 1);
                        b.Append($"; следующая — {next} «{route.Route[next].name}»");
                    }
                    else b.Append(" — маршрут пройден");
                }
                break;
            default:
                b.Append("Свободное плавание — сценарий не выбран");
                break;
        }
        return b.ToString();
    }

    /// <summary>Страница «Задание»: заголовок, ход, условия, брифинг (TextMeshPro, разметка <b>, <size>).</summary>
    public static string Page(ScenarioDefinition s, TaskPhase phase, double exerciseTimeS, RouteTracker route)
    {
        if (s == null) return "<b>Задание</b>\n" + Status(null, TaskPhase.Free, 0, null);
        var b = new StringBuilder();
        b.AppendLine($"<b>{s.id} «{s.title}»</b>");
        b.AppendLine(Status(s, phase, exerciseTimeS, route));
        b.AppendLine();
        b.AppendLine(BriefingFacts(s));
        if (!string.IsNullOrWhiteSpace(s.briefing))
        {
            b.AppendLine();
            b.AppendLine("<b>Брифинг</b>");
            b.Append(s.briefing.Trim());
        }
        return b.ToString().TrimEnd();
    }
}
