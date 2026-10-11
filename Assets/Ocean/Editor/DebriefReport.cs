// ============================================================
// DebriefReport.cs — Разбор и отчёты C3 в редакторе (Play Mode не нужен)
//
// Меню:
//   Tools/VR Bridge/Debrief Report — данные C3 для ВКР → ThesisData/Debrief/<дата>/:
//     sessions/<сценарий>_<вариант>/ — записанные прогоны (DemoAssessmentRuns,
//       как в Assessment Report) с report.html;
//     <сценарий>_<вариант>_map<k>.svg — схемы траектории (рисунки ВКР);
//     sessions_summary.csv, sessions_criteria.csv, sessions_log.txt — сводка по прогонам
//       (проверка сводной таблицы);
//     summary.md — что построено, время построения, объём, проверки.
//   Tools/VR Bridge/Sessions Summary… — сводная таблица по папке с сессиями
//     (педагогический эксперимент) → ThesisData/Summary/<дата>/; пороги — из записи
//     каждой сессии или текущего ассета Assessment_Default (анализ чувствительности).
// Отчёт для одной сессии (в том числе старой) — кнопка «Отчёт» в Session Replay
// (BuildReport: офлайн-оценка по записи, акватория — снимок или ассет по имени).
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class DebriefReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);

    private static string ThesisDir(string sub) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ThesisData", sub, DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv)));

    // ─────────── Одна сессия ───────────
    /// <summary>report.html для папки сессии (офлайн-оценка по записи) и открыть в браузере.</summary>
    public static string BuildReport(string folder, bool open = true)
    {
        RecordedSession s = SessionReader.Load(folder);
        SessionArea area = s.Meta.scenario.area != null && !s.Meta.scenario.area.IsEmpty ? null : SessionArea.From(FindArea(s.Meta));
        string path = SessionReport.BuildForFolder(folder, area);
        Debug.Log($"[Разбор] {path}");
        if (open) Application.OpenURL(new Uri(path).AbsoluteUri);
        return path;
    }

    /// <summary>Акватория сессии без снимка (до C3): ассет сценария с тем же id или ассет по имени.</summary>
    public static WaterArea FindArea(SessionMeta meta)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:ScenarioDefinition"))
        {
            var s = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (s != null && s.id == meta.scenario.id && s.waterArea != null) return s.waterArea;
        }
        if (string.IsNullOrEmpty(meta.scenario.water_area)) return null;
        foreach (string guid in AssetDatabase.FindAssets("t:WaterArea"))
        {
            var a = AssetDatabase.LoadAssetAtPath<WaterArea>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null && a.name == meta.scenario.water_area) return a;
        }
        return null;
    }

    // ─────────── Сводка по папке ───────────
    [MenuItem("Tools/VR Bridge/Sessions Summary…")]
    private static void SummaryMenu() => Summary(ExerciseRecorder.DefaultRoot);

    public static void Summary(string startFolder)
    {
        string root = EditorUtility.OpenFolderPanel("Папка с сессиями (или одна сессия)", startFolder, "");
        if (string.IsNullOrEmpty(root)) return;
        List<string> folders = ExperimentSummary.FindSessions(root);
        if (folders.Count == 0)
        {
            EditorUtility.DisplayDialog("Сводка по сессиям", $"В папке нет сессий (session.json):\n{root}", "OK");
            return;
        }
        int choice = EditorUtility.DisplayDialogComplex("Сводка по сессиям",
            $"Сессий: {folders.Count}.\n\nС какими порогами оценивать?\n• из записи — как оценивалось в шлеме (для сессий до C2 — по умолчанию);\n• текущий ассет Assessment_Default — анализ чувствительности.",
            "Из записи", "Отмена", "Текущий ассет");
        if (choice == 1) return;
        AssessmentSettings settings = null;
        if (choice == 2)
        {
            var asset = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>("Assets/Ocean/Scenarios/Assessment_Default.asset");
            settings = AssessmentSettings.From(asset);
        }
        ExperimentSummaryResult res;
        try
        {
            EditorUtility.DisplayProgressBar("Сводка по сессиям", $"{folders.Count} сессий", 0.5f);
            res = ExperimentSummary.Build(folders, settings);
        }
        finally { EditorUtility.ClearProgressBar(); }
        string dir = ThesisDir("Summary");
        res.Log.Insert(0, $"папка: {root}");
        ExperimentSummary.Write(dir, res);
        Debug.Log($"[Сводка] {res.Sessions} сессий → {dir}");
        EditorUtility.RevealInFinder(Path.Combine(dir, ExperimentSummary.WideFile));
    }

    // ─────────── Данные C3 для ВКР ───────────
    [MenuItem("Tools/VR Bridge/Debrief Report")]
    private static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[Отчёт C3] Выйдите из Play Mode.");
            return;
        }
        ShipParameters model45 = AssetDatabase.LoadAssetAtPath<ShipParameters>("Assets/Ocean/Settings/ShipParameters_Model45m.asset");
        ShipParameters mariner = AssetDatabase.LoadAssetAtPath<ShipParameters>("Assets/Ocean/Settings/ShipParameters_Mariner.asset");
        WaterArea area = AssetDatabase.LoadAssetAtPath<WaterArea>("Assets/Ocean/Settings/WaterArea_Demo.asset");
        if (model45 == null || mariner == null || area == null)
        {
            Debug.LogError("[Отчёт C3] Нужны ShipParameters_Model45m, ShipParameters_Mariner и WaterArea_Demo (Assets/Ocean/Settings).");
            return;
        }
        AssessmentCriteria criteria = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>("Assets/Ocean/Scenarios/Assessment_Default.asset");

        string dir = ThesisDir("Debrief");
        string sessionsDir = Path.Combine(dir, "sessions");
        Directory.CreateDirectory(sessionsDir);
        var md = new StringBuilder("# C3: разбор упражнения и отчёт — данные стенда\n\n");
        md.AppendLine("Прогоны — как в Assessment Report (`DemoAssessmentRuns`), с записью; отчёт строится тем же кодом, что в шлеме (`DebriefBuild`: файлы записи + итог оценки).\n");
        md.AppendLine("| Сценарий | Вариант | Балл | Итог | Нарушений | Видов схемы | report.html, КБ | Построение, мс |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");

        var list = new List<(string sc, string v)>();
        foreach (string v in DemoAssessmentRuns.B3Variants) list.Add((DemoTrafficScenario.Id, v));
        foreach (string v in DemoAssessmentRuns.B2Variants) list.Add((DemoScenario.Id, v));
        var folders = new List<string>();
        try
        {
            for (int k = 0; k < list.Count; k++)
            {
                (string sc, string v) = list[k];
                EditorUtility.DisplayProgressBar("Отчёт C3", $"{sc}: {DemoAssessmentRuns.Title(v)}", 0.05f + 0.85f * k / list.Count);
                string folder = Path.Combine(sessionsDir, $"{sc}_{v}");
                Directory.CreateDirectory(folder);
                var sim = new ExerciseSimulation
                {
                    Scenario = sc == DemoScenario.Id ? AssessmentReport.B2(area, model45, criteria) : AssessmentReport.B3(area, model45, mariner, criteria),
                    RecordFolder = folder
                };
                DemoAssessmentRuns.Configure(sim, v);
                sim.Run();
                var build = new DebriefBuild(folder, sim.LiveResult);
                build.Run();
                if (!string.IsNullOrEmpty(build.Error)) { Debug.LogError($"[Отчёт C3] {sc} {v}: {build.Error}"); continue; }
                folders.Add(folder);
                for (int i = 0; i < build.Map.Views.Count; i++)
                    File.WriteAllText(Path.Combine(dir, $"{sc}_{v}_map{i}.svg"), DebriefSvg.Map(build.Map, build.Map.Views[i], "map", 680, i == 0 ? 860 : 620), new UTF8Encoding(false));
                AssessmentResult r = sim.LiveResult;
                long kb = new FileInfo(build.ReportPath).Length / 1024;
                md.AppendLine($"| {sc} | {DemoAssessmentRuns.Title(v)} | {ScenarioText.Num(r.Score, 1)} | {DebriefText.Verdict(r).ToLowerInvariant()} | " +
                              $"{DebriefText.Violations(r).Count} | {build.Map.Views.Count} | {kb} | {ScenarioText.Num(build.ElapsedMs)} |");
            }

            EditorUtility.DisplayProgressBar("Отчёт C3", "Сводная таблица", 0.95f);
            ExperimentSummaryResult sum = ExperimentSummary.Build(folders);
            ExperimentSummary.Write(dir, sum);
            int wideCols = CsvFormat.Split(sum.WideCsv.Substring(0, sum.WideCsv.IndexOf('\n'))).Count;
            md.AppendLine();
            md.AppendLine("## Сводная таблица по прогонам\n");
            md.AppendLine($"`sessions_summary.csv`: {sum.Sessions} строк, {wideCols} столбцов; `sessions_criteria.csv`: {sum.LongCsv.Split('\n').Length - 2} строк критериев.\n");
            foreach (string line in sum.Log) md.AppendLine($"- {line}");

            md.AppendLine("\n## Нарушения (тексты разбора)\n");
            foreach (string f in folders)
            {
                RecordedSession s = SessionReader.Load(f);
                AssessmentResult r = SessionAssessment.Assess(s);
                md.AppendLine($"**{Path.GetFileName(f)}** — {r.Headline()}\n");
                List<DebriefViolation> v = DebriefText.Violations(r);
                if (v.Count == 0) md.AppendLine("- нарушений нет");
                foreach (DebriefViolation x in v) md.AppendLine($"- {(x.Severe ? "**" : "")}{x.Text.Replace("|", "\\|")}{(x.Severe ? "**" : "")}");
                md.AppendLine();
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        File.WriteAllText(Path.Combine(dir, "summary.md"), md.ToString(), Utf8Bom);
        Debug.Log($"[Отчёт C3] Сохранено: {dir}");
        EditorUtility.RevealInFinder(Path.Combine(dir, "summary.md"));
    }
}
