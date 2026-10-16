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
// 19.2б-3: в сценарии швартовки вместо маршрута — причал, борт и четыре задания с условиями
// (MooringTasks); во время упражнения — отметка «выполнено (мм:сс)» у каждого задания
// (условия заданий — часть задания, а не подсказка; отметки показываются во всех режимах).
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
        if (s.IsMooring) MooringFacts(s, b);
        else b.AppendLine($"Путевых точек: {Math.Max(0, s.route.Count - 1)}, маршрут {ScenarioText.Num(s.RouteLength / 1852.0, 1)} мили");
        if (s.traffic != null && s.traffic.Count > 0) b.AppendLine($"Другие суда в районе: {s.traffic.Count}");
        if (s.windKnots > 0f) b.AppendLine($"Ветер: {ScenarioText.Wind(s.windKnots, s.windFromDeg)}");   // 19.3б
        if (s.tugAvailable) b.AppendLine("Буксир: по вызову (меню B → «Буксир»)");
        b.AppendLine(s.timeLimitSeconds > 0 ? $"Лимит времени: {ScenarioText.Clock(s.timeLimitSeconds)}" : "Без лимита времени");
        b.Append($"Завершение: {ScenarioText.EndConditions(s)}");
        return b.ToString();
    }

    /// <summary>
    /// Ход упражнения: фаза, время, маршрут (route — RouteTracker упражнения, может быть null);
    /// в сценарии швартовки — задания с отметками (mooring — этапы упражнения, может быть null).
    /// </summary>
    public static string Status(ScenarioDefinition s, TaskPhase phase, double exerciseTimeS, RouteTracker route, MooringTaskTracker mooring = null)
    {
        var b = new StringBuilder();
        switch (phase)
        {
            case TaskPhase.Briefing:
                b.Append("Брифинг — упражнение не начато");
                break;
            case TaskPhase.Finished:
                b.Append($"Упражнение завершено, {ScenarioText.Clock(exerciseTimeS)}");
                MooringProgress(mooring, b);
                break;
            case TaskPhase.Running:
                b.Append($"Время {ScenarioText.Clock(exerciseTimeS)}");
                if (s.timeLimitSeconds > 0)
                    b.Append($" из {ScenarioText.Clock(s.timeLimitSeconds)}, осталось {ScenarioText.Clock(Math.Max(0.0, s.timeLimitSeconds - exerciseTimeS))}");
                if (s.IsMooring) MooringProgress(mooring, b);
                else if (route != null && route.Valid)
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
    public static string Page(ScenarioDefinition s, TaskPhase phase, double exerciseTimeS, RouteTracker route, MooringTaskTracker mooring = null)
    {
        if (s == null) return "<b>Задание</b>\n" + Status(null, TaskPhase.Free, 0, null);
        var b = new StringBuilder();
        b.AppendLine($"<b>{s.id} «{s.title}»</b>");
        b.AppendLine(Status(s, phase, exerciseTimeS, route, mooring));
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

    // ─────────── Сценарий швартовки (19.2б-3) ───────────

    /// <summary>Причал, борт и задания с условиями (брифинг и «Задание»).</summary>
    private static void MooringFacts(ScenarioDefinition s, StringBuilder b)
    {
        MooringTaskDefinition t = s.mooring;
        BerthArea berth = t.BerthIn(s.waterArea);
        string side = berth != null && s.initial != null ? MooringTasks.SideTitle(berth, s.initial.headingDeg) : "";
        b.AppendLine(berth != null
            ? $"Причал «{berth.name}»: кромка {ScenarioText.Num(berth.Length)} м, курс {berth.BearingDeg:000}°" + (side.Length > 0 ? $", швартовка {side} бортом" : "")
            : $"Причал № {t.berthIndex + 1} — нет в акватории");
        b.AppendLine("<b>Задания</b>");
        for (int i = 1; i <= MooringTaskTracker.StageCount; i++)
            b.AppendLine($"{i}. {MooringTasks.Title((MooringStage)i)}: <size=90%>{MooringTasks.Condition(t, (MooringStage)i)}</size>");
    }

    /// <summary>Отметки заданий: «1. Подойти лагом — выполнено 03:12».</summary>
    private static void MooringProgress(MooringTaskTracker m, StringBuilder b)
    {
        if (m == null) return;
        b.Append($"\nЗаданий выполнено: {m.DoneCount} из {MooringTaskTracker.StageCount}");
        for (int i = 1; i <= MooringTaskTracker.StageCount; i++)
        {
            var st = (MooringStage)i;
            b.Append($"\n  {i}. {MooringTasks.Title(st)} — ");
            b.Append(m.Done(st) ? $"<color=#6FD08C>выполнено {ScenarioText.Clock(m.DoneAt(st))}</color>" : "нет");
        }
        if (!m.Moored && m.HoldTime > 0.0)
            b.Append($"\n  ошвартован через {ScenarioText.Num(Math.Max(0.0, m.Task.mooredHoldS - m.HoldTime))} с");
    }
}
