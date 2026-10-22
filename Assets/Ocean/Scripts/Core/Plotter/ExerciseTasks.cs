// ============================================================
// ExerciseTasks.cs — Вкладка «Задание» навигационного планшета (D2)
//
// Что обучающийся должен сделать и как идёт упражнение — только то, что он и так
// знает из брифинга, без подсказок решения:
//   • название, ход упражнения (время и лимит, пройденные точки, следующая точка);
//   • условия — тот же текст, что на панели брифинга (BriefingFacts — единственный
//     источник; К1: ScenarioPanel берёт его части — BriefingShip и BriefingConditions — на боковые панели);
//   • полный текст брифинга.
// Не показывается (это проверяют критерии оценки C2/C4 — обучающийся должен решить сам):
// ситуация сближения и обязанность по МППСС, безопасная скорость сценария числом,
// время до следующего туманного сигнала. Проверяет PlotterTests.
// 19.2б-3: в сценарии швартовки вместо маршрута — причал, борт и четыре задания с условиями
// (MooringTasks); во время упражнения — отметка «выполнено (мм:сс)» у каждого задания
// (условия заданий — часть задания, а не подсказка; отметки показываются во всех режимах).
// 19.4б-2: упражнение на своём буксире — клиент, причал, четыре задания буксира с условиями и отметками,
// последняя команда лоцмана (TugWorkSystem).
// ============================================================
using System;
using System.Collections.Generic;
using System.Text;

public enum TaskPhase { Free, Briefing, Running, Finished }

public static class ExerciseTasks
{
    /// <summary>
    /// Условия упражнения — вкладка «Задание» планшета: «судно и задание» + «условия»
    /// (К1: на брифинге в шлеме это две боковые панели — BriefingShip и BriefingConditions).
    /// </summary>
    public static string BriefingFacts(ScenarioDefinition s) =>
        "<b>Условия</b>\n" + BriefingShip(s) + "\n" + BriefingConditions(s);

    /// <summary>К1: левая панель брифинга — начальное состояние своего судна и задание (маршрут, швартовка или работа буксира).</summary>
    public static string BriefingShip(ScenarioDefinition s)
    {
        var b = new StringBuilder();
        OwnShipInitialState i = s.initial;
        if (i != null)
            b.AppendLine($"Начало: курс {i.headingDeg:000}°, {ScenarioText.Num(i.speedKnots, 1)} уз, телеграф «{ScenarioText.Telegraph(i.telegraph)}», руль {ScenarioText.Num(i.rudderDeg)}°");
        if (s.IsMooring) MooringFacts(s, b);
        else if (s.IsTugWork) TugWorkFacts(s, b);
        else b.AppendLine($"Путевых точек: {Math.Max(0, s.route.Count - 1)}, маршрут {ScenarioText.Num(s.RouteLength / 1852.0, 1)} мили");
        if (s.traffic != null && s.traffic.Count > 0) b.AppendLine($"Другие суда в районе: {s.traffic.Count}");
        if (s.tugAvailable) b.AppendLine("Буксир: по вызову (меню B → «Буксир»)");
        return b.ToString().TrimEnd('\n', '\r');
    }

    /// <summary>К1: правая панель брифинга — время суток, погода, видимость, ветер, лимит и завершение.</summary>
    public static string BriefingConditions(ScenarioDefinition s)
    {
        var b = new StringBuilder();
        b.AppendLine($"Время суток: {ScenarioText.TimeOfDayTitle(s.timeOfDay)}; погода: {ScenarioText.Weather(s.weather)}");
        if (s.visibilityMeters > 0f)   // К1: видимость, заданная сценарием (иначе — по погоде, она уже в строке выше)
            b.AppendLine($"Видимость: {ScenarioText.Num(s.visibilityMeters / 1852.0, 1)} мили");
        if (s.windKnots > 0f)   // 19.3б; 19.3б-2 — случайное направление: в брифинге без числа (его видно в обстановке)
            b.AppendLine(s.windRandomSectorDeg > 0f ? $"Ветер: {ScenarioText.Num(s.windKnots)} уз, направление — {(s.windRandomSectorDeg >= 360f ? "случайное" : $"около {ScenarioText.Bearing(s.windFromDeg)}")}"
                                                  : $"Ветер: {ScenarioText.Wind(s.windKnots, s.windFromDeg)}");
        b.AppendLine(s.timeLimitSeconds > 0 ? $"Лимит времени: {ScenarioText.Clock(s.timeLimitSeconds)}" : "Без лимита времени");
        b.Append($"Завершение: {ScenarioText.EndConditions(s)}");
        return b.ToString();
    }

    /// <summary>
    /// К1: обязательные критерии (включённые и обязательные в наборе сценария с галочками — EffectiveAssessment),
    /// которые в этом сценарии оцениваются: расхождение и реакция — при других судах; маршрут — в упражнении по маршруту;
    /// видимость — при ограниченной видимости на начало; швартовка — в швартовке; G1–G4 — при буксире по вызову;
    /// G5–G13 — в работе своего буксира; аварии — всегда.
    /// </summary>
    public static List<CriterionInfo> BriefingMandatory(ScenarioDefinition s)
    {
        var list = new List<CriterionInfo>();
        if (s == null) return list;
        AssessmentSettings a = s.EffectiveAssessment();
        foreach (CriterionInfo c in AssessmentCatalog.All)
        {
            CriterionSetting set = a.Get(c.Code);
            if (set == null || !set.enabled || !set.mandatory) continue;
            if (Assessed(s, a, c)) list.Add(c);
        }
        return list;
    }

    /// <summary>К1: текст обязательных критериев для брифинга («нет» — если обязательных нет).</summary>
    public static string BriefingMandatoryText(ScenarioDefinition s)
    {
        List<CriterionInfo> list = BriefingMandatory(s);
        var b = new StringBuilder("<b>Обязательные критерии</b> (не выполнен — незачёт)");
        if (list.Count == 0) b.Append("\nнет");
        foreach (CriterionInfo c in list) b.Append($"\n{c.Code} — {c.Title}");
        return b.ToString();
    }

    private static bool Assessed(ScenarioDefinition s, AssessmentSettings a, CriterionInfo c)
    {
        bool traffic = s.traffic != null && s.traffic.Count > 0;
        switch (c.Group)
        {
            case AssessmentGroup.Encounters:
            case AssessmentGroup.Reaction: return traffic;
            case AssessmentGroup.Route: return !s.IsMooring && !s.IsTugWork && s.route != null && s.route.Count >= 2;
            case AssessmentGroup.Visibility:
                return FogSignals.Restricted(DetectionModel.Visibility(s.weather, s.visibilityMeters), a.RestrictedVisibilityNm);
            case AssessmentGroup.Mooring: return s.IsMooring;
            case AssessmentGroup.Towing:
                int n;
                bool own = c.Code.Length > 1 && int.TryParse(c.Code.Substring(1), out n) && n >= 5;   // G5–G13 — свой буксир
                return own ? s.IsTugWork : s.tugAvailable;
            default: return true;   // аварии
        }
    }

    /// <summary>
    /// Ход упражнения: фаза, время, маршрут (route — RouteTracker упражнения, может быть null);
    /// в сценарии швартовки — задания с отметками (mooring — этапы упражнения, может быть null).
    /// </summary>
    public static string Status(ScenarioDefinition s, TaskPhase phase, double exerciseTimeS, RouteTracker route, MooringTaskTracker mooring = null,
                                TugWorkSystem work = null)
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
                if (s.IsTugWork) TugWorkProgress(work, b);
                break;
            case TaskPhase.Running:
                b.Append($"Время {ScenarioText.Clock(exerciseTimeS)}");
                if (s.timeLimitSeconds > 0)
                    b.Append($" из {ScenarioText.Clock(s.timeLimitSeconds)}, осталось {ScenarioText.Clock(Math.Max(0.0, s.timeLimitSeconds - exerciseTimeS))}");
                if (s.IsMooring) MooringProgress(mooring, b);
                else if (s.IsTugWork) TugWorkProgress(work, b);
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
    public static string Page(ScenarioDefinition s, TaskPhase phase, double exerciseTimeS, RouteTracker route, MooringTaskTracker mooring = null,
                              TugWorkSystem work = null)
    {
        if (s == null) return "<b>Задание</b>\n" + Status(null, TaskPhase.Free, 0, null);
        var b = new StringBuilder();
        b.AppendLine($"<b>{s.id} «{s.title}»</b>");
        b.AppendLine(Status(s, phase, exerciseTimeS, route, mooring, work));
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

    // ─────────── Упражнение на своём буксире (19.4б-2) ───────────

    /// <summary>Судно-клиент, причал и задания с условиями (брифинг и «Задание»).</summary>
    private static void TugWorkFacts(ScenarioDefinition s, StringBuilder b)
    {
        TugWorkDefinition t = s.tugWork;
        BerthArea berth = t.BerthIn(s.waterArea);
        string client = s.tugWorkClient != null ? $"судно {ScenarioText.Num(s.tugWorkClient.length)} м" : "судно";
        b.AppendLine(berth != null
            ? $"Своё судно — буксир; {client} лагом в {ScenarioText.Num(t.clientGapM)} м от причала «{berth.name}», буксир — в {ScenarioText.Num(t.tugDistanceM)} м от его борта"
            : $"Причал № {t.berthIndex + 1} — нет в акватории");
        b.AppendLine("<b>Задания (по командам лоцмана)</b>");
        for (int i = 1; i <= TugWorkSystem.StageCount; i++)
            b.AppendLine($"{i}. {TugWorkText.Stage((TugWorkStage)i)}: <size=90%>{TugWorkText.Condition(t, (TugWorkStage)i)}</size>");
    }

    /// <summary>Последняя команда лоцмана и отметки заданий буксира.</summary>
    private static void TugWorkProgress(TugWorkSystem w, StringBuilder b)
    {
        if (w == null) return;
        if (w.Order != PilotOrderKind.None)
            b.Append($"\nЛоцман: «{w.LastMessage}» ({ScenarioText.Clock(w.LastMessageTime)})");   // 19.4б-3: и команды упором
        if (w.LastInfo.Length > 0 && w.LastInfo != w.LastMessage)
            b.Append($"\n  {w.LastInfo} ({ScenarioText.Clock(w.LastInfoTime)})");             // и последний доклад дистанции
        b.Append($"\nЗаданий выполнено: {w.DoneCount} из {TugWorkSystem.StageCount}");
        for (int i = 1; i <= TugWorkSystem.StageCount; i++)
        {
            var st = (TugWorkStage)i;
            b.Append($"\n  {i}. {TugWorkText.Stage(st)} — ");
            b.Append(w.Done(st) ? $"<color=#6FD08C>выполнено {ScenarioText.Clock(w.DoneAt(st))}</color>" : "нет");
        }
        // 19.4г: «случайное событие» — только после того, как случилось (заранее не подсказываем)
        if (w.EventFired)
        {
            string end = TugWorkText.End(w.EventOffEnd);
            b.Append($"\nСобытие ({ScenarioText.Clock(w.EventTime)}): {end} отходит до {ScenarioText.Num(w.EventAngleDeg, 1)}°, угол сейчас {ScenarioText.Num(Math.Abs(w.Client.OffAngleDeg), 1)}°");
            b.Append($"\n  буксир у конца — {(w.TugAtEnd ? "да" : "нет")}; ближний конец на стопоре — {(w.EventPinned ? "да" : "нет")}{(w.EventDone ? "; судно у кранцев" : "")}");
        }
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
