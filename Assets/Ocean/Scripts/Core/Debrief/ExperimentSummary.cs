// ============================================================
// ExperimentSummary.cs — Сводная таблица по сессиям (C3)
//
// Для статистической обработки педагогического эксперимента: выбрать папку
// с сессиями → два CSV (формат CLAUDE.md: «;», точка, UTF-8 с BOM):
//   • sessions_summary.csv — строка на сессию: папка, ID обучающегося, сценарий,
//     дата, длительность, завершение, балл, зачёт/провал, баллы групп, аварии,
//     затем по каждому критерию и объекту — значение, выполнен, балл
//     (столбцы «E1_T2_value», «R6_Поворот_passed»…; набор столбцов —
//     объединение по всем сессиям, пусто — критерия в сессии нет);
//   • sessions_criteria.csv — то же в «длинном» виде: строка на сессию
//     и критерий (удобно для сводных таблиц Excel и пакетов статистики).
// Каждая сессия оценивается заново по записи: по умолчанию с порогами из
// её session.json (для сессий после C2 — тот же результат, что в шлеме,
// сверка — столбец recorded_match), или с другими порогами (анализ
// чувствительности, ThesisNotes 10.13). Сессии до C2 — пороги по умолчанию.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public sealed class SummaryItem
{
    public RecordedSession Session;
    public AssessmentResult Result;
    /// <summary>assessment.csv из шлема совпадает с пересчётом: 1 / 0 / null — нет файла.</summary>
    public bool? RecordedMatch;
}

public sealed class ExperimentSummaryResult
{
    public string WideCsv = "", LongCsv = "";
    public int Sessions;
    public readonly List<string> Log = new List<string>();
}

public static class ExperimentSummary
{
    public const string WideFile = "sessions_summary.csv", LongFile = "sessions_criteria.csv", LogFile = "sessions_log.txt";

    /// <summary>Папки сессий: сама папка (если в ней session.json) или все вложенные с session.json.</summary>
    public static List<string> FindSessions(string root)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return list;
        if (File.Exists(Path.Combine(root, SessionFormat.MetaFile))) { list.Add(root); return list; }
        foreach (string f in Directory.GetFiles(root, SessionFormat.MetaFile, SearchOption.AllDirectories))
            list.Add(Path.GetDirectoryName(f));
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    /// <summary>Чтение и оценка сессий (главный поток Unity). settings — другие пороги (null — из записи).</summary>
    public static ExperimentSummaryResult Build(IEnumerable<string> folders, AssessmentSettings settings = null)
    {
        var items = new List<SummaryItem>();
        var log = new List<string>();
        foreach (string f in folders)
        {
            RecordedSession s = SessionReader.Load(f);
            if (s.Track.Count == 0) { log.Add($"пропущена {Path.GetFileName(f)}: нет траектории"); continue; }
            AssessmentResult r = SessionAssessment.Assess(s, settings);
            bool? match = null;
            string file = Path.Combine(f, SessionFormat.AssessmentFile);
            if (File.Exists(file)) match = File.ReadAllText(file).TrimStart('﻿') == r.ToCsv();
            items.Add(new SummaryItem { Session = s, Result = r, RecordedMatch = match });
        }
        ExperimentSummaryResult res = Build(items);
        res.Log.InsertRange(0, log);
        res.Log.Insert(0, settings == null ? "пороги — из записи каждой сессии" : "пороги — заданные (анализ чувствительности)");
        return res;
    }

    /// <summary>Таблицы по оценённым сессиям (без чтения файлов).</summary>
    public static ExperimentSummaryResult Build(IList<SummaryItem> items)
    {
        var res = new ExperimentSummaryResult();
        var sorted = new List<SummaryItem>(items);
        sorted.Sort((a, b) => string.CompareOrdinal(a.Session.Meta.start_utc + a.Session.Name, b.Session.Meta.start_utc + b.Session.Name));

        // Объединение критериев (код + объект) по всем сессиям, в порядке каталога
        var keys = new List<(string code, string subject)>();
        foreach (SummaryItem it in sorted)
            foreach (CriterionResult c in it.Result.Items)
                if (!keys.Contains((c.Code, c.Subject))) keys.Add((c.Code, c.Subject));
        keys.Sort((a, b) =>
        {
            int ca = CatalogIndex(a.code), cb = CatalogIndex(b.code);
            return ca != cb ? ca.CompareTo(cb) : string.CompareOrdinal(a.subject, b.subject);
        });

        // Широкая таблица
        var w = new StringBuilder();
        w.Append("session_id;folder;trainee_id;scenario_id;scenario_title;start_local;duration_s;completed;end_reason;by_instructor;weather;time_of_day;" +
                 "score;passed;failed;failure;mandatory_failed;criteria_evaluated;criteria_failed;" +
                 "group_encounters;group_reaction;group_route;groundings;collisions;recorded_score;recorded_match");
        foreach ((string code, string subject) in keys)
        {
            string col = Column(code, subject);
            w.Append(';').Append(col).Append("_value;").Append(col).Append("_passed;").Append(col).Append("_score");
        }
        w.Append('\n');

        var l = new StringBuilder("session_id;folder;trainee_id;scenario_id;start_local;code;group;subject;evaluated;value;unit;full;pass;zero;passed;score;mandatory;weight;note\n");

        foreach (SummaryItem it in sorted)
        {
            SessionMeta m = it.Session.Meta;
            AssessmentResult r = it.Result;
            CsvFormat.Text(w, m.session_id ?? ""); Txt(w, it.Session.Name); Txt(w, m.trainee_id); Txt(w, m.scenario.id); Txt(w, m.scenario.title); Txt(w, m.start_local);
            NumC(w, m.duration_s > 0 ? m.duration_s : it.Session.Duration, 1); Bit(w, m.completed); Txt(w, m.completed ? m.end_reason : SessionMeta.Interrupted);
            Bit(w, m.by_instructor); Txt(w, m.scenario.weather); Txt(w, m.scenario.time_of_day);
            NumC(w, r.Score, 2); Bit(w, r.Passed); Bit(w, r.Failed); Txt(w, r.FailureText); Txt(w, string.Join(" ", r.MandatoryFailed));
            IntC(w, r.EvaluatedCount); IntC(w, r.FailedCount);
            NumC(w, Group(r, AssessmentGroup.Encounters), 2); NumC(w, Group(r, AssessmentGroup.Reaction), 2); NumC(w, Group(r, AssessmentGroup.Route), 2);
            IntC(w, Count(it.Session, ExerciseEventType.Grounded)); IntC(w, Count(it.Session, ExerciseEventType.Collision));
            if (m.assessment != null && m.assessment.assessed) NumC(w, m.assessment.score, 1); else w.Append(';');
            w.Append(';'); if (it.RecordedMatch.HasValue) w.Append(it.RecordedMatch.Value ? '1' : '0');
            foreach ((string code, string subject) in keys)
            {
                CriterionResult c = r.Items.Find(x => x.Code == code && x.Subject == subject);
                if (c == null || !c.Evaluated) { w.Append(";;;"); continue; }
                NumC(w, c.Value, 4); Bit(w, c.Passed); NumC(w, c.Score, 4);
            }
            w.Append('\n');

            foreach (CriterionResult c in r.Items)
            {
                CsvFormat.Text(l, m.session_id ?? ""); Txt(l, it.Session.Name); Txt(l, m.trainee_id); Txt(l, m.scenario.id); Txt(l, m.start_local);
                Txt(l, c.Code); Txt(l, c.Info.Group.ToString()); Txt(l, c.Subject); Bit(l, c.Evaluated);
                l.Append(';'); if (c.Evaluated) CsvFormat.Num(l, c.Value, 4);
                Txt(l, c.Info.Unit); NumC(l, c.Setting.full, 4); NumC(l, c.Setting.pass, 4); NumC(l, c.Setting.zero, 4);
                l.Append(';'); if (c.Evaluated) l.Append(c.Passed ? '1' : '0');
                l.Append(';'); if (c.Evaluated) CsvFormat.Num(l, c.Score, 4);
                Bit(l, c.Mandatory); NumC(l, c.Setting.weight, 2); Txt(l, c.Note);
                l.Append('\n');
            }
            res.Log.Add($"{m.session_id}: {m.scenario.id}, балл {ScenarioText.Num(r.Score, 1)}, {DebriefText.Verdict(r)}" +
                        (it.RecordedMatch.HasValue ? (it.RecordedMatch.Value ? ", совпадает с оценкой в шлеме" : ", НЕ совпадает с assessment.csv") : ""));
        }
        res.WideCsv = w.ToString();
        res.LongCsv = l.ToString();
        res.Sessions = sorted.Count;
        return res;
    }

    public static void Write(string dir, ExperimentSummaryResult r)
    {
        Directory.CreateDirectory(dir);
        var bom = new UTF8Encoding(true);
        File.WriteAllText(Path.Combine(dir, WideFile), r.WideCsv, bom);
        File.WriteAllText(Path.Combine(dir, LongFile), r.LongCsv, bom);
        File.WriteAllText(Path.Combine(dir, LogFile), string.Join("\n", r.Log) + "\n", bom);
    }

    /// <summary>Имя столбца: «E1», «E8_T2», «E8_T3-2» (второе сближение), «R6_Поворот».</summary>
    public static string Column(string code, string subject)
    {
        if (string.IsNullOrEmpty(subject)) return code;
        var b = new StringBuilder();
        foreach (char ch in subject.Replace(" (", "-").Replace("(", "-"))
        {
            if (ch == ')' || ch == '"' || ch == ';') continue;
            b.Append(char.IsWhiteSpace(ch) ? '_' : ch);
        }
        return code + "_" + b;
    }

    private static int CatalogIndex(string code)
    {
        for (int i = 0; i < AssessmentCatalog.All.Count; i++) if (AssessmentCatalog.All[i].Code == code) return i;
        return int.MaxValue;
    }

    private static double Group(AssessmentResult r, AssessmentGroup g) => r.GroupScores.TryGetValue(g, out double v) ? v : double.NaN;

    private static int Count(RecordedSession s, ExerciseEventType t)
    {
        int n = 0;
        foreach (ExerciseEvent e in s.Events) if (e.Type == t) n++;
        return n;
    }

    // Ячейки с разделителем перед значением (первая ячейка строки — CsvFormat.Text без «;»)
    private static void Txt(StringBuilder b, string s) { b.Append(';'); CsvFormat.Text(b, s ?? ""); }

    private static void NumC(StringBuilder b, double v, int d) { b.Append(';'); CsvFormat.Num(b, v, d); }
    private static void IntC(StringBuilder b, int v) { b.Append(';'); CsvFormat.Int(b, v); }
    private static void Bit(StringBuilder b, bool v) { b.Append(';').Append(v ? '1' : '0'); }
}
