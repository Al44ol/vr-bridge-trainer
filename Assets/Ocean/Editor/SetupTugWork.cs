// ============================================================
// SetupTugWork.cs — Ассеты упражнения на своём буксире 19-03 (запрос 19.4б-2)
//
// Сцену не правит. При входе в Play Mode (ScenarioLibrarySync → EnsureCreated, после SetupTowing — нужен ассет
// буксира Resources/ShipParameters_Tug) создаёт, если их нет:
//   • Assets/Ocean/Scenarios/Assessment_TugWork.asset — набор критериев 19-03 (DemoTugWorkScenario.Assessment:
//     маршрутные и «Швартовка» выключены; группа «Буксир» G1–G4 не оценивается — буксир не вызывается;
//     критерии работы буксира G5–G11 — запрос 19.4в);
//   • Assets/Ocean/Scenarios/Scenario_19-03_TugWork.asset «Буксир прижимает судно к причалу» (DemoTugWorkScenario:
//     своё судно — буксир, клиент — Model45m) — появляется в выборе без ручного Setup.
//   • 19.4б-3: причал №2 (под Mariner) дописывается в WaterArea_Demo.asset, если его нет; сценарий с demoVersion меньше
//     DemoTugWorkScenario.Version заполняется заново (клиент — Mariner, причал №2, 15 уз) — Rebuild вручную не нужен.
// Существующие ассеты не меняются (критерии, добавленные в каталог позже, дописываются — AddMissing).
// Tools/VR Bridge/Rebuild Demo Tug Work Scenario — заполнить 19-03 и набор критериев заново (правки будут заменены).
// ============================================================
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SetupTugWork
{
    public const string ScenarioPath = "Assets/Ocean/Scenarios/Scenario_19-03_TugWork.asset";
    public const string CriteriaPath = "Assets/Ocean/Scenarios/Assessment_TugWork.asset";
    private const string DemoAreaPath = "Assets/Ocean/Settings/WaterArea_Demo.asset";
    /// <summary>19.4б-3: судно-клиент — Mariner (было Model45m).</summary>
    private const string ClientPath = "Assets/Ocean/Settings/ShipParameters_Mariner.asset";

    /// <summary>Создать недостающие ассеты 19-03; 19.4б-3 — причал №2 в акватории и обновление сценария по версии (при входе в Play Mode).</summary>
    public static void EnsureCreated()
    {
        EnsureBerth2();
        ScenarioDefinition s = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(ScenarioPath);
        if (s != null && AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(CriteriaPath) != null)
        {
            AddMissingCriteria();
            if (s.demoVersion >= DemoTugWorkScenario.Version) return;
            var upd = new StringBuilder($"[Setup 19.4б] Сценарий 19-03 обновлён автоматически: версия {s.demoVersion} → {DemoTugWorkScenario.Version} " +
                                        "(правки в ассете заменены):\n");
            CreateScenario(upd, true);
            Debug.Log(upd.ToString().TrimEnd());
            return;
        }
        var log = new StringBuilder("[Setup 19.4б] Сценарий 19-03 создан автоматически (при входе в Play Mode):\n");
        CreateScenario(log, false);
        Debug.Log(log.ToString().TrimEnd());
    }

    // 19.4б-3: причал №2 (под Mariner) в ассете демо-акватории — если его нет; прочее содержимое не меняется
    private static void EnsureBerth2()
    {
        WaterArea area = AssetDatabase.LoadAssetAtPath<WaterArea>(DemoAreaPath);
        if (area == null || !DemoWaterArea.EnsureBerth2(area)) return;
        EditorUtility.SetDirty(area);
        AssetDatabase.SaveAssets();
        BerthArea b = area.berths[DemoWaterArea.Berth2Index];
        Debug.Log($"[Setup 19.4б] {DemoAreaPath}: добавлен «{b.name}» — кромка {b.Length:F0} м, кранцы ⌀ {b.fenderOuterDiameterM:F2} м через {b.fenderSpacingM:F0} м, глубина {b.depthM:F0} м (под Mariner).");
    }

    [MenuItem("Tools/VR Bridge/Rebuild Demo Tug Work Scenario")]
    private static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup 19.4б] Выйдите из Play Mode и запустите снова.");
            return;
        }
        var log = new StringBuilder("[Setup 19.4б] Сценарий 19-03 заполнен заново:\n");
        CreateScenario(log, true);
        log.Append("Сцену сохранять не нужно.");
        Debug.Log(log.ToString());
    }

    private static void AddMissingCriteria()
    {
        AssessmentCriteria c = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(CriteriaPath);
        if (c == null || c.settings == null) return;
        int added = c.settings.AddMissing();
        if (added == 0) return;
        EditorUtility.SetDirty(c);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Setup 19.4б] {CriteriaPath}: добавлено критериев {added}");
    }

    private static void CreateScenario(StringBuilder log, bool refill)
    {
        WaterArea area = AssetDatabase.LoadAssetAtPath<WaterArea>(DemoAreaPath);
        ShipParameters client = AssetDatabase.LoadAssetAtPath<ShipParameters>(ClientPath);
        ShipParameters tug = AssetDatabase.LoadAssetAtPath<ShipParameters>(SetupTowing.TugPath);
        if (area == null || client == null || tug == null)
        {
            log.AppendLine($"  ! нет {DemoAreaPath}, {ClientPath} или {SetupTowing.TugPath} — сценарий не создан");
            return;
        }
        AssessmentCriteria crit = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(CriteriaPath);
        if (crit == null)
        {
            crit = ScriptableObject.CreateInstance<AssessmentCriteria>();
            crit.settings = DemoTugWorkScenario.Assessment();
            AssetDatabase.CreateAsset(crit, CriteriaPath);
            log.AppendLine($"  + {CriteriaPath}: маршрутные и «Швартовка» выключены (критерии работы буксира — 19.4в)");
        }
        else if (refill)
        {
            Undo.RecordObject(crit, "Rebuild 19-03 criteria");
            crit.settings = DemoTugWorkScenario.Assessment();
            EditorUtility.SetDirty(crit);
            log.AppendLine($"  = {CriteriaPath} заполнен заново");
        }

        ScenarioDefinition s = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(ScenarioPath);
        bool created = s == null;
        if (created) s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        else if (!refill) { AssetDatabase.SaveAssets(); return; }
        else Undo.RecordObject(s, "Rebuild 19-03");
        DemoTugWorkScenario.Fill(s, area, tug, client, crit);
        if (created) AssetDatabase.CreateAsset(s, ScenarioPath);
        else EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();
        log.AppendLine($"  + {ScenarioPath}: «{s.title}», своё судно — буксир ({tug.name}), клиент — {client.name} в {ScenarioText.Num(s.tugWork.clientGapM)} м от причала, " +
                       $"ветер {ScenarioText.Num(s.windKnots)} уз откуда {ScenarioText.Bearing(s.windFromDeg)} ± {ScenarioText.Num(s.windRandomSectorDeg / 2f)}°, " +
                       $"лимит {s.timeLimitSeconds / 60f:F0} мин");
        foreach (string p in s.Validate()) log.AppendLine("  ! " + p);
    }
}
