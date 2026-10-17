// ============================================================
// AssessmentReport.cs — Данные C2 (оценка действий) для ВКР без Play Mode
// Меню: Tools/VR Bridge/Assessment Report → ThesisData/Assessment/<дата>/:
//   criteria.csv            — перечень критериев: код, правило, единица, пороги, вес, основание;
//   detection_range.csv/.svg — дистанция обнаружения цели по видимости V: ночью (огни,
//                             закон Алларда) с носа и с кормы, днём — V;
//   runs.csv                — итог прогонов (B3-01: без манёвра / правильно / влево / поздно;
//                             B2-01: с упреждением / без; C4-01 (туман, с C4): правильно / без сигналов /
//                             не снизил ход / влево; D1 — B2-01 с вводными: отказ руля «Стоп» через 10 с /
//                             через 45 с / без реакции, отказ ГД «Стоп» через 10 с): причина, время, балл, зачёт;
//   runs_criteria.csv       — по каждому прогону — все критерии (значение, порог, зачёт, балл);
//   b3_<вариант>_tracks.svg, c4_<вариант>_tracks.svg — траектории своего судна и целей;
//   tugberth_runs.csv, tugberth/ — 19-02 (19.3в); tugwork_runs.csv, tugwork/<вариант>/ — 19-03 на своём буксире
//                             (19.4в: G5–G11, report.html с реакцией на команды лоцмана и графиком упора);
//   summary.md              — сводка для вставки в ВКР, в том числе проверка «оценка в
//                             реальном времени = оценка по записи» на прогоне с записью.
// Сценарии строятся заново (DemoScenario / DemoTrafficScenario) с ассетами параметров
// судов и акватории; пороги — Assessment_Default (если нет — значения по умолчанию).
// Формат CSV — по CLAUDE.md, графики — SvgPlot (стиль раздела 2).
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class AssessmentReport
{
    private const double Nm = 1852.0;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);

    [MenuItem("Tools/VR Bridge/Assessment Report")]
    private static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[Отчёт C2] Выйдите из Play Mode.");
            return;
        }
        ShipParameters model45 = AssetDatabase.LoadAssetAtPath<ShipParameters>("Assets/Ocean/Settings/ShipParameters_Model45m.asset");
        ShipParameters mariner = AssetDatabase.LoadAssetAtPath<ShipParameters>("Assets/Ocean/Settings/ShipParameters_Mariner.asset");
        WaterArea area = AssetDatabase.LoadAssetAtPath<WaterArea>("Assets/Ocean/Settings/WaterArea_Demo.asset");
        if (model45 == null || mariner == null || area == null)
        {
            Debug.LogError("[Отчёт C2] Нужны ShipParameters_Model45m, ShipParameters_Mariner и WaterArea_Demo (Assets/Ocean/Settings).");
            return;
        }
        AssessmentCriteria criteria = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>("Assets/Ocean/Scenarios/Assessment_Default.asset");
        AssessmentSettings settings = AssessmentSettings.From(criteria);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ThesisData", "Assessment", DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv)));
        Directory.CreateDirectory(dir);
        var md = new StringBuilder("# C2: оценка действий обучающегося — данные стенда\n\n");
        md.AppendLine(criteria != null ? "Пороги — `Assessment_Default.asset`.\n" : "Пороги — значения по умолчанию (ассет не найден).\n");

        try
        {
            EditorUtility.DisplayProgressBar("Отчёт C2", "Критерии", 0f);
            Criteria(dir, md, settings);
            Detection(dir, md);
            Runs(dir, md, area, model45, mariner, criteria);
            BerthRuns(dir, md, area, model45);
            TugBerthRuns(dir, md, area, model45);
            TugWorkRuns(dir, md, area, mariner);
            LiveVsOffline(dir, md, area, model45, mariner, criteria);
        }
        finally { EditorUtility.ClearProgressBar(); }

        File.WriteAllText(Path.Combine(dir, "summary.md"), md.ToString(), Utf8Bom);
        Debug.Log($"[Отчёт C2] Сохранено: {dir}");
        EditorUtility.RevealInFinder(Path.Combine(dir, "summary.md"));
    }

    // ─────────── Перечень критериев ───────────
    private static void Criteria(string dir, StringBuilder md, AssessmentSettings s)
    {
        var csv = new StringBuilder("code;group;title;rule;unit;higher_is_better;enabled;mandatory;full;pass;zero;weight;basis\n");
        md.AppendLine("## Перечень критериев\n");
        md.AppendLine("| Код | Критерий | Правило | Единица | Полный балл | Зачёт | Ноль | Обяз. | Вес |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (CriterionInfo c in AssessmentCatalog.All)
        {
            CriterionSetting x = s.Get(c.Code);
            csv.Append(c.Code).Append(';').Append(c.Group).Append(';').Append(Q(c.Title)).Append(';').Append(Q(c.Rule)).Append(';')
               .Append(Q(c.Unit)).Append(';').Append(c.HigherIsBetter ? 1 : 0).Append(';').Append(x.enabled ? 1 : 0).Append(';')
               .Append(x.mandatory ? 1 : 0).Append(';').Append(F(x.full, 4)).Append(';').Append(F(x.pass, 4)).Append(';')
               .Append(F(x.zero, 4)).Append(';').Append(F(x.weight, 2)).Append(';').Append(Q(c.Basis)).Append('\n');
            string op = c.HigherIsBetter ? "≥" : "≤";
            md.AppendLine($"| {c.Code} | {c.Title} | {c.Rule} | {c.Unit} | {op} {G(x.full)} | {op} {G(x.pass)} | {G(x.zero)} | {(x.mandatory ? "да" : "")} | {G(x.weight)} |");
        }
        File.WriteAllText(Path.Combine(dir, "criteria.csv"), csv.ToString(), Utf8Bom);
        md.AppendLine($"\nВеса групп: расхождение {G(s.weightEncounters)}, реакция {G(s.weightReaction)}, маршрут {G(s.weightRoute)}; проходной балл {G(s.passScore)}.");
        md.AppendLine($"Параметры ситуаций: CPA_доп по умолчанию {G(s.defaultRequiredCpaNm)} мили, опасность — CPA < {G(s.riskCpaFactor)}·CPA_доп и 0 < TCPA ≤ {G(s.riskHorizonMin)} мин, " +
                      $"встречные — ±{G(s.headOnSectorDeg)}°, действие — руль ≥ {G(s.actionRudderDeg)}°, переклассификация — цель изменила курс > {G(s.targetTurnDeg)}°.\n");
    }

    // ─────────── Дистанция обнаружения ───────────
    private static void Detection(string dir, StringBuilder md)
    {
        var csv = new StringBuilder("visibility_nm;day_nm;night_L161_bow_nm;night_L161_stern_nm;night_L45_bow_nm;night_L45_stern_nm\n");
        var series = new List<Vector2>[5];
        for (int k = 0; k < 5; k++) series[k] = new List<Vector2>();
        for (double v = 0.5; v <= 12.0 + 1e-9; v += 0.25)
        {
            double vm = v * Nm;
            double day = DetectionModel.RangeNm(161, 0, TimeOfDay.Day, vm, 0);
            double b161 = DetectionModel.RangeNm(161, 0, TimeOfDay.Night, vm, 0);
            double s161 = DetectionModel.RangeNm(161, 180, TimeOfDay.Night, vm, 0);
            double b45 = DetectionModel.RangeNm(45, 0, TimeOfDay.Night, vm, 0);
            double s45 = DetectionModel.RangeNm(45, 180, TimeOfDay.Night, vm, 0);
            csv.Append(F(v, 2)).Append(';').Append(F(day, 3)).Append(';').Append(F(b161, 3)).Append(';').Append(F(s161, 3)).Append(';')
               .Append(F(b45, 3)).Append(';').Append(F(s45, 3)).Append('\n');
            series[0].Add(new Vector2((float)v, (float)day));
            series[1].Add(new Vector2((float)v, (float)b161));
            series[2].Add(new Vector2((float)v, (float)s161));
            series[3].Add(new Vector2((float)v, (float)b45));
            series[4].Add(new Vector2((float)v, (float)s45));
        }
        File.WriteAllText(Path.Combine(dir, "detection_range.csv"), csv.ToString(), Utf8Bom);
        var plot = new SvgPlot { Title = "Дистанция визуального обнаружения цели", XLabel = "Метеорологическая видимость V, мили", YLabel = "Дистанция обнаружения, мили", LegendBottomRight = true };
        plot.Add("день: D = V", SvgPlot.Dash.Solid, series[0]);
        plot.Add("ночь, L ≥ 50 м, с носа (топовый 6 миль)", SvgPlot.Dash.Dashed, series[1]);
        plot.Add("ночь, L ≥ 50 м, с кормы (кормовой 3 мили)", SvgPlot.Dash.DashDot, series[2]);
        plot.Add("ночь, L = 45 м, с кормы (кормовой 2 мили)", SvgPlot.Dash.Dotted, series[4]);
        File.WriteAllText(Path.Combine(dir, "detection_range.svg"), plot.Render(), new UTF8Encoding(false));

        md.AppendLine("## Дистанция обнаружения\n");
        md.AppendLine("| Условия | V, мили | Сухогруз (L ≥ 50 м) с носа | с кормы | Малое (L = 45 м) с носа | с кормы |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (WeatherPresetId w in new[] { WeatherPresetId.Calm, WeatherPresetId.Moderate, WeatherPresetId.Storm, WeatherPresetId.Night })
        {
            double vm = WeatherVisibility.VisibilityM(w);
            foreach (TimeOfDay t in new[] { TimeOfDay.Day, TimeOfDay.Night })
                md.AppendLine($"| {ScenarioText.WeatherTitle(w)}, {ScenarioText.TimeOfDayTitle(t).ToLowerInvariant()} | {N(vm / Nm, 2)} | " +
                              $"{N(DetectionModel.RangeNm(161, 0, t, vm, 0), 2)} | {N(DetectionModel.RangeNm(161, 180, t, vm, 0), 2)} | " +
                              $"{N(DetectionModel.RangeNm(45, 0, t, vm, 0), 2)} | {N(DetectionModel.RangeNm(45, 180, t, vm, 0), 2)} |");
        }
        md.AppendLine();
    }

    // ─────────── Прогоны ───────────
    internal static ScenarioDefinition B2(WaterArea area, ShipParameters own, AssessmentCriteria criteria)
    {
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoScenario.Fill(s, area, own);
        s.assessment = criteria;
        return s;
    }

    internal static ScenarioDefinition B3(WaterArea area, ShipParameters own, ShipParameters mariner, AssessmentCriteria criteria)
    {
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoTrafficScenario.Fill(s, area, own, mariner, mariner, own);
        s.assessment = criteria;
        return s;
    }

    internal static ScenarioDefinition C4(WaterArea area, ShipParameters own, ShipParameters mariner, AssessmentCriteria criteria)
    {
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoFogScenario.Fill(s, area, own, mariner);
        s.assessment = criteria;
        return s;
    }

    private static void Runs(string dir, StringBuilder md, WaterArea area, ShipParameters own, ShipParameters mariner, AssessmentCriteria criteria)
    {
        var runs = new StringBuilder("scenario;variant;title;end_reason;duration_s;score;passed;failed;failure;mandatory_failed;criteria_evaluated;criteria_failed\n");
        var items = new StringBuilder("scenario;variant;code;subject;evaluated;value;unit;pass;passed;score;mandatory;note\n");
        md.AppendLine("## Прогоны сценариев (своё судно — авторулевой и манёвры по плану)\n");
        md.AppendLine("| Сценарий | Вариант | Завершение | Время | Балл | Итог | Не выполнены обязательные |");
        md.AppendLine("|---|---|---|---|---|---|---|");

        var list = new List<(string sc, string v)>();
        foreach (string v in DemoAssessmentRuns.B3Variants) list.Add((DemoTrafficScenario.Id, v));
        foreach (string v in DemoAssessmentRuns.B2Variants) list.Add((DemoScenario.Id, v));
        foreach (string v in DemoAssessmentRuns.C4Variants) list.Add((DemoFogScenario.Id, v));
        foreach (string v in DemoAssessmentRuns.FailureVariants) list.Add((DemoScenario.Id, v));   // D1: вводные
        var details = new StringBuilder();
        for (int k = 0; k < list.Count; k++)
        {
            (string sc, string v) = list[k];
            EditorUtility.DisplayProgressBar("Отчёт C2", $"{sc}: {DemoAssessmentRuns.Title(v)}", 0.1f + 0.8f * k / list.Count);
            var sim = new ExerciseSimulation
            {
                Scenario = sc == DemoScenario.Id ? B2(area, own, criteria)
                         : sc == DemoFogScenario.Id ? C4(area, own, mariner, criteria) : B3(area, own, mariner, criteria)
            };
            DemoAssessmentRuns.Configure(sim, v);
            sim.Run();
            AssessmentResult r = sim.LiveResult;
            string verdict = r.VerdictText.ToLowerInvariant();
            runs.Append(sc).Append(';').Append(v).Append(';').Append(Q(DemoAssessmentRuns.Title(v))).Append(';').Append(sim.EndReason).Append(';')
                .Append(F(sim.EndTime, 1)).Append(';').Append(F(r.Score, 1)).Append(';').Append(r.Passed ? 1 : 0).Append(';').Append(r.Failed ? 1 : 0).Append(';')
                .Append(Q(r.FailureText)).Append(';').Append(Q(string.Join(" ", r.MandatoryFailed))).Append(';').Append(r.EvaluatedCount).Append(';').Append(r.FailedCount).Append('\n');
            foreach (CriterionResult c in r.Items)
                items.Append(sc).Append(';').Append(v).Append(';').Append(c.Code).Append(';').Append(Q(c.Subject)).Append(';').Append(c.Evaluated ? 1 : 0).Append(';')
                     .Append(c.Evaluated ? F(c.Value, 4) : "").Append(';').Append(Q(c.Info.Unit)).Append(';').Append(F(c.Setting.pass, 4)).Append(';')
                     .Append(c.Evaluated ? (c.Passed ? "1" : "0") : "").Append(';').Append(c.Evaluated ? F(c.Score, 3) : "").Append(';')
                     .Append(c.Mandatory ? 1 : 0).Append(';').Append(Q(c.Note)).Append('\n');
            md.AppendLine($"| {sc} | {DemoAssessmentRuns.Title(v)} | {ScenarioText.EndReason(sim.EndReason)} | {ScenarioText.Clock(sim.EndTime)} | {N(r.Score, 1)} | {verdict}{(r.Failed ? $" ({Md(r.FailureText)})" : "")} | {string.Join(", ", r.MandatoryFailed)} |");

            details.AppendLine($"### {sc} — {DemoAssessmentRuns.Title(v)}\n");
            foreach (EncounterSummary e in r.Encounters)
                details.AppendLine($"- {e.TargetId}: {EncounterClassifier.Title(e.Situation)}; обнаружена t = {N(e.DetectTime)} с на {N(e.DetectRangeNm, 2)} мили " +
                                   $"(дальность {N(e.DetectionRangeNm, 2)}); опасность t = {N(e.OnsetTime)} с, CPA {N(e.OnsetCpaNm, 2)} мили, TCPA {N(e.OnsetTcpaMin, 1)} мин, " +
                                   $"dП/dt {N(e.BearingRateDegMin, 2)} °/мин; действие: {(string.IsNullOrEmpty(e.Action) ? "нет" : $"{e.Action}, t = {N(e.ActionTime)} с")}; " +
                                   $"наименьшая дистанция {(e.Collided ? "0 (столкновение)" : $"{N(e.MinDistanceM)} м")}");
            details.AppendLine();
            details.AppendLine("| Критерий | Объект | Значение | Зачёт | Балл | Пояснение |");
            details.AppendLine("|---|---|---|---|---|---|");
            foreach (CriterionResult c in r.Items)
                details.AppendLine($"| {c.Code} {c.Info.Title} | {c.Subject} | {AssessmentResult.ValueText(c)} | {(c.Evaluated ? c.Passed ? "да" : "нет" : "—")} | {(c.Evaluated ? N(c.Score, 2) : "—")} | {Md(c.Note)} |");
            details.AppendLine();

            if (sc == DemoTrafficScenario.Id) Tracks(dir, "b3", "B3-01", v, sim);
            if (sc == DemoFogScenario.Id) Tracks(dir, "c4", "C4-01", v, sim);
        }
        File.WriteAllText(Path.Combine(dir, "runs.csv"), runs.ToString(), Utf8Bom);
        File.WriteAllText(Path.Combine(dir, "runs_criteria.csv"), items.ToString(), Utf8Bom);
        md.AppendLine();
        md.Append(details);
    }

    // ─────────── 19.2б-3в: прогоны сценария швартовки 19-01 ───────────
    // BerthingSimulation (модель 3 СС, MooringSystem, задания, запись с mooring.csv, оценка в реальном времени);
    // папка каждого прогона — berth/<вариант>/ с report.html; berth_runs.csv — значения M1–M9 по вариантам.
    private static void BerthRuns(string dir, StringBuilder md, WaterArea area, ShipParameters own)
    {
        AssessmentCriteria crit = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>("Assets/Ocean/Scenarios/Assessment_Berthing.asset");
        if (crit == null) { crit = ScriptableObject.CreateInstance<AssessmentCriteria>(); crit.settings = DemoBerthScenario.Assessment(); }
        RunBerthSet(dir, md, area, own, crit, DemoBerthScenario.Id, "berth", DemoAssessmentRuns.BerthVariants,
            s => DemoBerthScenario.Fill(s, area, own, crit), DemoAssessmentRuns.ConfigureBerth, false);
    }

    // ─────────── 19.3в: прогоны сценария 19-02 «Швартовка с буксиром при отжимном ветре» ───────────
    // Ветер — середина сектора сценария (330°); tugberth/<вариант>/ с report.html (раздел «Буксир и ветер», график буксира);
    // tugberth_runs.csv — значения M1–M9 и G1–G4.
    private static void TugBerthRuns(string dir, StringBuilder md, WaterArea area, ShipParameters own)
    {
        AssessmentCriteria crit = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(SetupTowing.CriteriaPath);
        if (crit == null) { crit = ScriptableObject.CreateInstance<AssessmentCriteria>(); crit.settings = DemoTugBerthScenario.Assessment(); }
        RunBerthSet(dir, md, area, own, crit, DemoTugBerthScenario.Id, "tugberth", DemoAssessmentRuns.TugBerthVariants,
            s => DemoTugBerthScenario.Fill(s, area, own, crit), DemoAssessmentRuns.ConfigureTugBerth, true);
    }

    // 19.4в: прогоны 19-03 (TugWorkSimulation, свой буксир, клиент Mariner у причала №2, ветер 15 уз — середина сектора) —
    // tugwork/<вариант>/ (сессия + report.html: «Работа буксира», реакция на команды, график упора); tugwork_runs.csv — G5–G11.
    private static void TugWorkRuns(string dir, StringBuilder md, WaterArea area, ShipParameters mariner)
    {
        ShipParameters tug = AssetDatabase.LoadAssetAtPath<ShipParameters>(SetupTowing.TugPath);
        if (tug == null) { tug = ScriptableObject.CreateInstance<ShipParameters>(); tug.name = "ShipParameters_Tug"; ShipPresets.FillTug(tug); }
        var probe = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(probe);
        WaterArea a = area.berths != null && area.berths.Count > DemoWaterArea.Berth2Index ? area : probe;   // причал №2 дописывается при входе в Play Mode
        AssessmentCriteria crit = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(SetupTugWork.CriteriaPath);
        if (crit == null) { crit = ScriptableObject.CreateInstance<AssessmentCriteria>(); crit.settings = DemoTugWorkScenario.Assessment(); }
        string[] codes = { AssessmentCatalog.G5, AssessmentCatalog.G6, AssessmentCatalog.G7, AssessmentCatalog.G8, AssessmentCatalog.G9, AssessmentCatalog.G10, AssessmentCatalog.G11 };
        var csv = new StringBuilder("variant;title;end_reason;duration_s;stages_done;max_client_gap_m;score;passed;mandatory_failed;live_equals_offline");
        foreach (string c in codes) csv.Append(';').Append(c.ToLowerInvariant());
        csv.Append('\n');
        md.AppendLine($"## Прогоны сценария {DemoTugWorkScenario.Id} (TugWorkSimulation, свой буксир, клиент Mariner, ветер 15 уз)\n");
        md.AppendLine("| Вариант | Завершение | Время | Заданий | Балл | Итог | Не выполнены обязательные | Прочие не выполнены |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        var log = new StringBuilder();
        string root = Path.Combine(dir, "tugwork");
        foreach (string v in DemoAssessmentRuns.TugWorkVariants)
        {
            EditorUtility.DisplayProgressBar("Отчёт C2", $"{DemoTugWorkScenario.Id}: {DemoAssessmentRuns.Title(v)}", 0.95f);
            var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
            DemoTugWorkScenario.Fill(s, a, tug, mariner, crit);
            string folder = Path.Combine(root, v);
            Directory.CreateDirectory(folder);
            var sim = new TugWorkSimulation { Scenario = s, RecordFolder = folder };
            DemoAssessmentRuns.ConfigureTugWork(sim.Plan, v);
            sim.Run();
            AssessmentResult r = sim.LiveResult;
            bool same = SessionAssessment.Assess(SessionReader.Load(folder)).ToCsv() == r.ToCsv();
            SessionReport.BuildForFolder(folder);
            var others = new List<string>();
            foreach (CriterionResult c in r.Items) if (c.Evaluated && !c.Passed && !c.Mandatory) others.Add(c.Code);
            csv.Append(v).Append(';').Append(Q(DemoAssessmentRuns.Title(v))).Append(';').Append(sim.EndReason).Append(';').Append(F(sim.EndTime, 1)).Append(';')
               .Append(sim.System.DoneCount).Append(';').Append(F(sim.MaxClientGapM, 1)).Append(';').Append(F(r.Score, 1)).Append(';').Append(r.Passed ? 1 : 0).Append(';')
               .Append(Q(string.Join(" ", r.MandatoryFailed))).Append(';').Append(same ? 1 : 0);
            foreach (string code in codes)
            {
                CriterionResult c = r.Items.Find(x => x.Code == code);
                csv.Append(';').Append(c != null && c.Evaluated ? F(c.Value, 4) : "");
            }
            csv.Append('\n');
            md.AppendLine($"| {DemoAssessmentRuns.Title(v)} | {ScenarioText.EndReason(sim.EndReason)} | {ScenarioText.Clock(sim.EndTime)} | {sim.System.DoneCount} из {TugWorkSystem.StageCount} | " +
                          $"{N(r.Score, 1)} | {r.VerdictText.ToLowerInvariant()} | {string.Join(", ", r.MandatoryFailed)} | {string.Join(", ", others)} |");
            log.AppendLine($"### {DemoTugWorkScenario.Id} — {DemoAssessmentRuns.Title(v)}{(same ? "" : " (оценка по записи НЕ совпала!)")}\n");
            foreach (CriterionResult c in r.Items)
                if (c.Info.Group == AssessmentGroup.Towing)
                    log.AppendLine($"- {c.Code} {c.Info.Title}: {AssessmentResult.ValueText(c)} — {(c.Evaluated ? c.Passed ? "да" : "нет" : "не оценивался")}; {Md(c.Note)}");
            log.AppendLine();
        }
        File.WriteAllText(Path.Combine(dir, "tugwork_runs.csv"), csv.ToString(), Utf8Bom);
        md.AppendLine();
        md.Append(log);
    }

    private static void RunBerthSet(string dir, StringBuilder md, WaterArea area, ShipParameters own, AssessmentCriteria crit, string id, string name,
                                    string[] variants, System.Action<ScenarioDefinition> fill, System.Action<BerthingPlan, string> configure, bool towing)
    {
        if (area.berths == null || area.berths.Count == 0) { md.AppendLine($"{id}: в акватории нет причала — прогоны пропущены.\n"); return; }
        var codeList = new List<string> { AssessmentCatalog.M1, AssessmentCatalog.M2, AssessmentCatalog.M3, AssessmentCatalog.M4, AssessmentCatalog.M5,
                                          AssessmentCatalog.M6, AssessmentCatalog.M7, AssessmentCatalog.M8, AssessmentCatalog.M9 };
        if (towing) codeList.AddRange(new[] { AssessmentCatalog.G1, AssessmentCatalog.G2, AssessmentCatalog.G3, AssessmentCatalog.G4 });
        string[] codes = codeList.ToArray();
        var csv = new StringBuilder("variant;title;end_reason;duration_s;moored_s;max_impact_kn;score;passed;mandatory_failed;live_equals_offline");
        foreach (string c in codes) csv.Append(';').Append(c.ToLowerInvariant());
        csv.Append('\n');
        md.AppendLine($"## Прогоны сценария {id} (BerthingSimulation)\n");
        md.AppendLine("| Вариант | Завершение | Время | Навал, уз | Балл | Итог | Не выполнены обязательные | Прочие не выполнены |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        var log = new StringBuilder();
        string root = Path.Combine(dir, name);
        foreach (string v in variants)
        {
            EditorUtility.DisplayProgressBar("Отчёт C2", $"{id}: {DemoAssessmentRuns.Title(v)}", 0.9f);
            var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
            fill(s);
            string folder = Path.Combine(root, v);
            Directory.CreateDirectory(folder);
            var sim = new BerthingSimulation { Scenario = s, RecordFolder = folder };
            configure(sim.Plan, v);
            sim.Run();
            AssessmentResult r = sim.LiveResult;
            RecordedSession rec = SessionReader.Load(folder);
            bool same = SessionAssessment.Assess(rec).ToCsv() == r.ToCsv();
            SessionReport.BuildForFolder(folder);
            var others = new List<string>();
            foreach (CriterionResult c in r.Items) if (c.Evaluated && !c.Passed && !c.Mandatory) others.Add(c.Code);
            double moored = sim.Task.DoneAt(MooringStage.Moored);
            csv.Append(v).Append(';').Append(Q(DemoAssessmentRuns.Title(v))).Append(';').Append(sim.EndReason).Append(';').Append(F(sim.EndTime, 1)).Append(';')
               .Append(F(moored, 1)).Append(';').Append(F(sim.MaxImpactKn, 3)).Append(';').Append(F(r.Score, 1)).Append(';').Append(r.Passed ? 1 : 0).Append(';')
               .Append(Q(string.Join(" ", r.MandatoryFailed))).Append(';').Append(same ? 1 : 0);
            foreach (string code in codes)
            {
                CriterionResult c = r.Items.Find(x => x.Code == code);
                csv.Append(';').Append(c != null && c.Evaluated ? F(c.Value, 4) : "");
            }
            csv.Append('\n');
            string verdict = r.VerdictText.ToLowerInvariant();
            md.AppendLine($"| {DemoAssessmentRuns.Title(v)} | {ScenarioText.EndReason(sim.EndReason)} | {ScenarioText.Clock(sim.EndTime)} | {N(sim.MaxImpactKn, 2)} | {N(r.Score, 1)} | {verdict} | {string.Join(", ", r.MandatoryFailed)} | {string.Join(", ", others)} |");
            log.AppendLine($"### {id} — {DemoAssessmentRuns.Title(v)}{(same ? "" : " (оценка по записи НЕ совпала!)")}\n");
            foreach (string l in sim.Log) log.AppendLine($"- {l}");
            log.AppendLine();
            foreach (CriterionResult c in r.Items)
                if (c.Info.Group == AssessmentGroup.Mooring || c.Info.Group == AssessmentGroup.Towing)
                    log.AppendLine($"- {c.Code} {c.Info.Title}: {AssessmentResult.ValueText(c)} — {(c.Evaluated ? c.Passed ? "да" : "нет" : "не оценивался")}; {Md(c.Note)}");
            log.AppendLine();
        }
        File.WriteAllText(Path.Combine(dir, name + "_runs.csv"), csv.ToString(), Utf8Bom);
        md.AppendLine();
        md.Append(log);
    }

    private static void Tracks(string dir, string prefix, string scenarioId, string variant, ExerciseSimulation sim)
    {
        var plot = new SvgPlot { Title = $"{scenarioId}: {DemoAssessmentRuns.Title(variant)}", XLabel = "Восток, м", YLabel = "Север, м", EqualAspect = true };
        SvgPlot.Dash[] dashes = { SvgPlot.Dash.Solid, SvgPlot.Dash.Dashed, SvgPlot.Dash.DashDot, SvgPlot.Dash.Dotted };
        for (int i = 0; i < sim.Tracks.Count; i++)
        {
            var pts = new List<Vector2>();
            foreach (WorldPosition p in sim.Tracks[i]) pts.Add(new Vector2((float)p.East, (float)p.North));
            plot.Add(sim.TrackNames[i], dashes[Math.Min(i, dashes.Length - 1)], pts);
        }
        File.WriteAllText(Path.Combine(dir, $"{prefix}_{variant}_tracks.svg"), plot.Render(), new UTF8Encoding(false));
    }

    // ─────────── Оценка в реальном времени = по записи ───────────
    private static void LiveVsOffline(string dir, StringBuilder md, WaterArea area, ShipParameters own, ShipParameters mariner, AssessmentCriteria criteria)
    {
        EditorUtility.DisplayProgressBar("Отчёт C2", "Оценка по записи", 0.95f);
        string session = Path.Combine(dir, "session_b3_correct");
        Directory.CreateDirectory(session);
        var sim = new ExerciseSimulation { Scenario = B3(area, own, mariner, criteria), RecordFolder = session };
        DemoAssessmentRuns.Configure(sim, "correct");
        sim.Run();
        RecordedSession rs = SessionReader.Load(session);
        AssessmentResult off = SessionAssessment.Assess(rs);
        AssessmentResult live = sim.LiveResult;
        int same = 0;
        for (int i = 0; i < Math.Min(live.Items.Count, off.Items.Count); i++)
        {
            CriterionResult a = live.Items[i], b = off.Items[i];
            if (a.Code == b.Code && a.Subject == b.Subject && a.Evaluated == b.Evaluated && a.Value.Equals(b.Value) && a.Passed == b.Passed && a.Score.Equals(b.Score)) same++;
        }
        bool csvSame = File.ReadAllText(Path.Combine(session, SessionFormat.AssessmentFile)).TrimStart('﻿') == off.ToCsv();
        md.AppendLine("## Оценка в реальном времени и по записи\n");
        md.AppendLine($"Прогон B3-01 «правильные действия» с записью (`session_b3_correct/`): {rs.Track.Count} строк траектории, {rs.Targets.Count} строк целей, {rs.Events.Count} событий.");
        md.AppendLine($"Совпало критериев: {same} из {live.Items.Count} (оценка по записи — {off.Items.Count}); балл {N(live.Score, 3)} / {N(off.Score, 3)}; " +
                      $"assessment.csv, записанный при прогоне, {(csvSame ? "совпадает" : "НЕ совпадает")} с пересчётом по файлам.");
    }

    private static string F(double v, int digits) => double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString("F" + digits, Inv);
    private static string N(double v, int digits = 0) => double.IsNaN(v) ? "—" : ScenarioText.Num(v, digits);
    // Порог, вес: до двух знаков без лишних нулей (0,5; 0,25; 70)
    private static string G(double v) => double.IsNaN(v) ? "—" : v.ToString("0.##", Inv).Replace('.', ',').Replace('-', '−');
    // Текст в ячейке markdown-таблицы: «|» (|XTE|) разбивает ячейку
    private static string Md(string s) => (s ?? "").Replace("|", "\\|");

    private static string Q(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var b = new StringBuilder();
        CsvFormat.Text(b, s);
        return b.ToString();
    }
}
