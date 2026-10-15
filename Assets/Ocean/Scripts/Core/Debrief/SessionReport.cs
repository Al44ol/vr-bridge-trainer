// ============================================================
// SessionReport.cs — Отчёт по упражнению report.html (C3)
//
// Одна страница без внешних ресурсов (ни шрифтов, ни скриптов, ни ссылок
// в интернет): схемы и графики — встроенный SVG, стили — в <style>.
// Открывается в любом браузере без сети, печатается на A4 (@page, разрывы
// страниц не режут таблицы и рисунки, шапки таблиц повторяются).
// Разделы: сведения о сессии; итог (балл, вердикт, баллы групп); нарушения
// и замечания; схема траектории (весь маршрут и окна сближений); сближения;
// критерии (значение / порог / балл / результат / пояснение); графики курса,
// скорости, бокового отклонения и руля по времени; журнал событий.
// Строится по записи (RecordedSession) и итогу оценки:
//   • в шлеме — итог оценки в реальном времени (по C2 он бит в бит равен
//     оценке по записи), отчёт пишется в фоновом потоке (DebriefBuild);
//   • для старой сессии — офлайн-оценка SessionAssessment (BuildForFolder).
// Запрос 17: ряды графиков — DebriefPlots (общие с вкладкой «Графики» в шлеме),
// добавлена отметка паузы.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

public sealed class ReportOptions
{
    /// <summary>Откуда оценка: «оценка в шлеме», «оценка по записи» и т. п.</summary>
    public string AssessmentSource = "оценка по записи";
    /// <summary>Время построения (подпись внизу).</summary>
    public DateTime Generated = DateTime.Now;
    /// <summary>Сравнить с assessment.csv в папке сессии.</summary>
    public bool CompareWithRecorded = true;
}

public static class SessionReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ─────────── Точки входа ───────────
    /// <summary>
    /// Отчёт для папки сессии: чтение, офлайн-оценка (settings — другие пороги, null — из записи),
    /// схема, report.html в папке. Только из главного потока Unity (оценка создаёт ShipParameters).
    /// area — акватория для сессий без снимка (до C3), null — только снимок.
    /// </summary>
    public static string BuildForFolder(string folder, SessionArea area = null, AssessmentSettings settings = null)
    {
        RecordedSession s = SessionReader.Load(folder);
        AssessmentResult r = s.Track.Count > 0 ? SessionAssessment.Assess(s, settings) : null;
        DebriefMapData map = DebriefMap.Build(s, r, area);
        var opt = new ReportOptions { AssessmentSource = settings == null ? "оценка по записи (пороги из записи)" : "оценка по записи с другими порогами" };
        return Write(folder, Build(s, r, map, opt));
    }

    public static string Write(string folder, string html)
    {
        string path = Path.Combine(folder, SessionFormat.ReportFile);
        File.WriteAllText(path, html, new UTF8Encoding(true));
        return path;
    }

    // ─────────── Страница ───────────
    public static string Build(RecordedSession s, AssessmentResult r, DebriefMapData map, ReportOptions opt = null)
    {
        opt = opt ?? new ReportOptions();
        SessionMeta m = s.Meta ?? new SessionMeta();
        SessionScenario sc = m.scenario ?? new SessionScenario();
        var h = new StringBuilder();
        h.Append("<!DOCTYPE html>\n<html lang=\"ru\">\n<head>\n<meta charset=\"utf-8\">\n");
        h.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        h.Append($"<title>Отчёт — {H(sc.id)} — {H(m.session_id)}</title>\n<style>\n{Css}</style>\n</head>\n<body>\n");

        h.Append($"<h1>Отчёт по упражнению {H(sc.id)} «{H(sc.title)}»</h1>\n");
        TrainerModeUsage modes = TrainerModeUsage.From(s.Events);   // перед 19.2 (п. B); в записях до п. B — пусто, отчёт прежний
        Meta(h, s, m, sc);
        Outcome(h, r, m, modes);
        Violations(h, r);
        Maps(h, map);
        Encounters(h, r);
        Criteria(h, r);
        Plots(h, s, r);
        Events(h, s);

        h.Append("<p class=\"foot\">Сформировано ").Append(H(opt.Generated.ToString("dd.MM.yyyy HH:mm", Inv))).Append(" · ")
         .Append(H(opt.AssessmentSource));
        if (opt.CompareWithRecorded && r != null) h.Append(" · ").Append(H(CompareText(s.Folder, r)));
        h.Append(" · VR-тренажёр судоводительского мостика</p>\n</body>\n</html>\n");
        return NumberSections(h.ToString());
    }

    /// <summary>Сквозные номера разделов: пустые разделы (нет сближений, траектории) пропускаются без дыр в нумерации.</summary>
    private static string NumberSections(string html)
    {
        int n = 0;
        return Regex.Replace(html, "<h2>", _ => $"<h2>{++n}. ");
    }

    /// <summary>Сверка с assessment.csv, записанным в шлеме.</summary>
    public static string CompareText(string folder, AssessmentResult r)
    {
        if (string.IsNullOrEmpty(folder)) return "";
        string file = Path.Combine(folder, SessionFormat.AssessmentFile);
        if (!File.Exists(file)) return "в записи нет оценки из шлема (сессия до C2 или прервана)";
        string recorded = File.ReadAllText(file).TrimStart('﻿');
        return recorded == r.ToCsv() ? "совпадает с оценкой в шлеме (assessment.csv)" : "НЕ совпадает с assessment.csv из шлема";
    }

    private const string Css =
        "@page { size: A4; margin: 14mm 12mm 14mm 16mm; }\n" +
        "body { font-family: \"Times New Roman\", Times, serif; font-size: 12pt; color: #000; background: #fff; max-width: 190mm; margin: 0 auto; padding: 6mm; }\n" +
        "h1 { font-size: 16pt; margin: 0 0 6pt; }\n" +
        "h2 { font-size: 13pt; margin: 14pt 0 5pt; border-bottom: 1px solid #000; break-after: avoid; page-break-after: avoid; }\n" +
        "table { border-collapse: collapse; width: 100%; font-size: 10pt; margin: 4pt 0; }\n" +
        "th, td { border: 1px solid #888; padding: 2pt 4pt; vertical-align: top; text-align: left; }\n" +
        "th { background: #eee; }\n" +
        "thead { display: table-header-group; }\n" +
        "tr, figure, .keep { break-inside: avoid; page-break-inside: avoid; }\n" +
        "table.meta td:first-child { width: 32%; color: #333; }\n" +
        "td.num { text-align: right; white-space: nowrap; }\n" +
        "td.mark { text-align: center; font-weight: bold; width: 1.5em; }\n" +
        "tr.fail td { font-weight: bold; }\n" +
        "tr.na td { color: #666; }\n" +
        ".verdict { display: inline-block; font-size: 14pt; font-weight: bold; border: 2px solid #000; padding: 3pt 10pt; margin: 2pt 0 6pt; }\n" +
        "figure { margin: 8pt 0; text-align: center; }\n" +
        "figure svg { max-width: 100%; height: auto; overflow: hidden; }\n" +
        "td { overflow-wrap: anywhere; }\n" +
        "figcaption { font-size: 10pt; margin-top: 2pt; }\n" +
        "ul.viol li { margin-bottom: 2pt; }\n" +
        "li.severe { font-weight: bold; }\n" +
        ".muted { color: #555; }\n" +
        ".foot { font-size: 9pt; color: #555; margin-top: 16pt; border-top: 1px solid #aaa; padding-top: 4pt; }\n" +
        "@media print { body { padding: 0; max-width: none; } }\n";

    // ─────────── Разделы ───────────
    private static void Meta(StringBuilder h, RecordedSession s, SessionMeta m, SessionScenario sc)
    {
        h.Append("<h2>Сведения о сессии</h2>\n<table class=\"meta\">\n");
        Row(h, "Обучающийся (ID)", string.IsNullOrEmpty(m.trainee_id) ? "не указан" : m.trainee_id);
        Row(h, "Сценарий", $"{sc.id} «{sc.title}»");
        Row(h, "Начало", Date(m.start_local));
        Row(h, "Длительность", ScenarioText.Clock(m.duration_s > 0 ? m.duration_s : s.Duration) + (m.wall_duration_s > 0 ? $" (по часам {ScenarioText.Clock(m.wall_duration_s)})" : ""));
        Row(h, "Завершение", (m.completed ? m.end_reason_ru : "запись прервана") + (m.by_instructor ? " (инструктор)" : ""));
        Enum.TryParse(sc.weather, out WeatherPresetId w);
        Enum.TryParse(sc.time_of_day, out TimeOfDay tod);
        Row(h, "Условия", ScenarioText.Conditions(w, tod));
        int n = sc.traffic != null ? sc.traffic.Count : 0;
        Row(h, "Другие суда", n == 0 ? "нет" : string.Join("; ", sc.traffic.ConvertAll(t => $"{t.id} — {t.name} (L {ScenarioText.Num(t.length_m)} м)")));
        if (m.ship != null && !string.IsNullOrEmpty(m.ship.asset))
            Row(h, "Своё судно", $"{m.ship.asset}: L {ScenarioText.Num(m.ship.length_m)} м, {ScenarioText.Num(m.ship.service_speed_kn, 1)} уз");
        if (m.summary != null)
            Row(h, "Маршрут (по всей записи)", $"пройдено точек {m.summary.waypoints_passed} из {m.summary.waypoints_total}; max |XTE| {ScenarioText.Num(m.summary.max_abs_xte_m)} м; " +
                              $"вне коридора {ScenarioText.Num(m.summary.time_outside_corridor_s)} с (с манёврами расхождения; в оценке маршрута они не учитываются)");
        if (m.summary != null)
            Row(h, "Действия", $"руль {m.summary.helm_actions}, телеграф {m.summary.telegraph_orders}, звуковые сигналы {m.summary.horn_signals}");
        Row(h, "Запись", $"{s.Name}; траектория {s.Track.Count} строк ({ScenarioText.Num(m.recording != null ? m.recording.track_rate_hz : 0)} Гц), целей {s.Targets.Count}, событий {s.Events.Count}");
        if (m.app != null && !string.IsNullOrEmpty(m.app.product))
            Row(h, "Программа", $"{m.app.product} {m.app.version}, Unity {m.app.unity_version}, {m.app.platform}" + (string.IsNullOrEmpty(m.app.git_commit) ? "" : $", коммит {m.app.git_commit}"));
        h.Append("</table>\n");
    }

    private static void Outcome(StringBuilder h, AssessmentResult r, SessionMeta m, TrainerModeUsage modes)
    {
        h.Append("<h2>Итог</h2>\n");
        // Режим тренажёра (перед 19.2, п. B): режим обучения / инструктора — жирным, экзамен — серым
        string mode = modes != null ? modes.Line() : "";
        if (mode.Length > 0)
            h.Append(modes.UsedTraining ? $"<p><b>{H(mode)}.</b></p>\n" : $"<p class=\"muted\">{H(mode)}.</p>\n");
        if (r == null)
        {
            h.Append("<p>Оценка не выполнялась: в записи нет траектории.</p>\n");
            return;
        }
        string score = double.IsNaN(r.Score) ? "—" : ScenarioText.Num(r.Score, 1);
        h.Append($"<div class=\"verdict\">{H(DebriefText.Verdict(r))} — {score} из 100</div>\n");
        string reason = DebriefText.VerdictReason(r);
        if (reason.Length > 0) h.Append($"<p><b>{H(char.ToUpperInvariant(reason[0]) + reason.Substring(1))}.</b></p>\n");
        h.Append("<table class=\"keep\"><thead><tr><th>Группа критериев</th><th>Вес группы</th><th>Балл группы</th></tr></thead><tbody>\n");
        AssessmentSettings set = m.assessment_settings ?? AssessmentSettings.Default();
        foreach (AssessmentGroup g in AssessmentCatalog.ScoredGroups)
        {
            bool has = r.GroupScores.TryGetValue(g, out double gs);
            h.Append($"<tr><td>{H(AssessmentCatalog.GroupTitle(g))}</td><td class=\"num\">{G(set.GroupWeight(g))}</td><td class=\"num\">{(has ? ScenarioText.Num(gs, 1) : "не оценивалась")}</td></tr>\n");
        }
        h.Append($"</tbody></table>\n<p class=\"muted\">Зачёт: нет аварии, выполнены все обязательные критерии и балл не ниже {G(r.PassScore)}. " +
                 $"Оценено критериев: {r.EvaluatedCount}, не выполнено: {r.FailedCount}. Пороги и веса — предварительные.</p>\n");
    }

    private static void Violations(StringBuilder h, AssessmentResult r)
    {
        if (r == null) return;
        h.Append("<h2>Нарушения и замечания</h2>\n");
        List<DebriefViolation> list = DebriefText.Violations(r);
        if (list.Count == 0) { h.Append("<p>Нарушений нет.</p>\n"); return; }
        h.Append("<ul class=\"viol\">\n");
        foreach (DebriefViolation v in list)
            h.Append($"<li{(v.Severe ? " class=\"severe\"" : "")}>{H(v.Text)}</li>\n");
        h.Append("</ul>\n");
    }

    private static void Maps(StringBuilder h, DebriefMapData map)
    {
        if (map == null || map.Views.Count == 0) return;
        h.Append("<h2>Схема траектории</h2>\n");
        for (int i = 0; i < map.Views.Count; i++)
        {
            DebriefMapView v = map.Views[i];
            h.Append("<figure>\n").Append(DebriefSvg.Map(map, v, "map" + i, 680, i == 0 ? 860 : 620));
            h.Append($"<figcaption>Рисунок {i + 1} — {H(v.Title)}</figcaption>\n</figure>\n");
        }
    }

    private static void Encounters(StringBuilder h, AssessmentResult r)
    {
        if (r == null || r.Encounters.Count == 0) return;
        h.Append("<h2>Сближения с судами</h2>\n<table>\n<thead><tr><th>Цель</th><th>Ситуация</th><th>Обнаружена</th><th>Опасность</th>" +
                 "<th>Манёвр</th><th>Наименьшая дистанция</th></tr></thead><tbody>\n");
        foreach (EncounterSummary e in r.Encounters)
        {
            string det = double.IsNaN(e.DetectTime) ? "—" : $"{ScenarioText.Clock(e.DetectTime)}, {ScenarioText.Num(e.DetectRangeNm, 2)} мили";
            string onset = double.IsNaN(e.OnsetTime) ? "—" : $"{ScenarioText.Clock(e.OnsetTime)}: CPA {ScenarioText.Num(e.OnsetCpaNm, 2)} мили, TCPA {ScenarioText.Num(e.OnsetTcpaMin, 1)} мин";
            string act = double.IsNaN(e.ActionTime) ? "нет" : $"{ScenarioText.Clock(e.ActionTime)}: {e.Action}";
            string min = e.Collided ? "<b>столкновение</b>" : !e.Complete ? "не завершено" :
                $"{ScenarioText.Num(e.MinDistanceM)} м при допустимой {ScenarioText.Num(e.RequiredCpaM)} м ({ScenarioText.Clock(e.MinTime)})";
            h.Append($"<tr><td>{H(e.TargetId)}</td><td>{H(EncounterClassifier.Title(e.Situation))}{(e.NarrowChannel ? ", в узкости" : "")}</td>" +
                     $"<td>{H(det)}</td><td>{H(onset)}</td><td>{H(act)}</td><td>{(e.Collided ? min : H(min))}</td></tr>\n");
        }
        h.Append("</tbody></table>\n");
    }

    private static void Criteria(StringBuilder h, AssessmentResult r)
    {
        if (r == null) return;
        h.Append("<h2>Критерии</h2>\n<table>\n<thead><tr><th></th><th>Код</th><th>Критерий</th><th>Объект</th><th>Значение</th><th>Порог зачёта</th>" +
                 "<th>Балл</th><th>Пояснение</th></tr></thead><tbody>\n");
        foreach (CriterionRow c in DebriefText.Rows(r))
        {
            string cls = c.State == CriterionState.FailedMandatory ? " class=\"fail\"" : c.State == CriterionState.NotEvaluated ? " class=\"na\"" : "";
            h.Append($"<tr{cls}><td class=\"mark\">{c.Mark}</td><td>{H(c.Code)}</td><td>{H(c.Title)}<br><span class=\"muted\">{H(c.Rule)}</span></td>" +
                     $"<td>{H(c.Subject)}</td><td class=\"num\">{H(c.Value)}</td><td class=\"num\">{H(c.Threshold)}</td>" +
                     $"<td class=\"num\">{(double.IsNaN(c.Score) ? "—" : ScenarioText.Num(c.Score, 2))}</td><td>{H(c.Note)}</td></tr>\n");
        }
        h.Append("</tbody></table>\n<p class=\"muted\">«+» — выполнен, «×» — не выполнен (жирным — обязательный), «–» — не оценивался. Балл критерия 0…1.</p>\n");
    }

    private static void Plots(StringBuilder h, RecordedSession s, AssessmentResult r)
    {
        // Ряды и отметки — DebriefPlots (запрос 17): те же графики на вкладке «Графики» в шлеме
        List<DebriefPlot> plots = DebriefPlots.Build(s, r);
        if (plots.Count == 0) return;
        h.Append("<h2>Графики</h2>\n");
        for (int i = 0; i < plots.Count; i++)
            h.Append("<figure>\n").Append(plots[i].ToSvg(PlotClipId(i), PlotHeight).Render())
             .Append($"<figcaption>{H(plots[i].Caption)}</figcaption>\n</figure>\n");
    }

    /// <summary>Высота графика в report.html, пикселей SVG (ширина — SvgPlot.Width).</summary>
    public const float PlotHeight = 300;
    public static string PlotClipId(int index) => "plot" + index;

    private static void Events(StringBuilder h, RecordedSession s)
    {
        if (s.Events.Count == 0) return;
        h.Append("<h2>Журнал событий</h2>\n<table>\n<thead><tr><th>Время</th><th>Событие</th><th>Курс, °</th><th>Скорость, уз</th></tr></thead><tbody>\n");
        foreach (ExerciseEvent e in s.Events)
            h.Append($"<tr><td class=\"num\">{ScenarioText.Clock(e.Time)}</td><td>{H(e.Describe())}</td>" +
                     $"<td class=\"num\">{ScenarioText.Num(e.HeadingDeg % 360.0, 1)}</td><td class=\"num\">{ScenarioText.Num(e.SpeedKnots, 1)}</td></tr>\n");
        h.Append("</tbody></table>\n");
    }

    // ─────────── Вспомогательное ───────────
    private static void Row(StringBuilder h, string k, string v) => h.Append($"<tr><td>{H(k)}</td><td>{H(v)}</td></tr>\n");

    private static string Date(string iso) =>
        DateTimeOffset.TryParse(iso, Inv, DateTimeStyles.None, out DateTimeOffset d) ? d.ToString("dd.MM.yyyy HH:mm:ss", Inv) : iso;

    private static string G(double v) => double.IsNaN(v) ? "—" : v.ToString("0.##", Inv).Replace('.', ',');

    /// <summary>Экранирование HTML.</summary>
    public static string H(string s) =>
        (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
