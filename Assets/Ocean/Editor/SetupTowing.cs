// ============================================================
// SetupTowing.cs — Ассеты буксировки (запрос 19.3)
//
// Сцену не правит. При входе в Play Mode (ScenarioLibrarySync → EnsureCreated) создаёт, если их нет:
//   • 19.3б: Assets/Ocean/Resources/ShipParameters_Tug.asset — буксир из ShipPresets.FillTug (единственный источник
//     значений): его берёт TowingController (Resources.Load), если в инспекторе не задан свой; правка значений —
//     в инспекторе ассета. Без ассета (сборка без него) TowingController строит те же параметры сам;
//   • 19.3в: Assets/Ocean/Scenarios/Assessment_TugBerthing.asset (набор критериев: как 19-01 + группа «Буксир») и
//     сценарий Scenario_19-02_TugBerth.asset «Швартовка с буксиром при отжимном ветре» (DemoTugBerthScenario) —
//     появляется в выборе без ручного Setup.
// Существующие ассеты не меняются. Tools/VR Bridge/Rebuild Demo Tug Berth Scenario — заполнить 19-02 и набор
// критериев заново (правки будут заменены).
// ============================================================
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SetupTowing
{
    public const string TugPath = "Assets/Ocean/Resources/ShipParameters_Tug.asset";
    public const string ScenarioPath = "Assets/Ocean/Scenarios/Scenario_19-02_TugBerth.asset";
    public const string CriteriaPath = "Assets/Ocean/Scenarios/Assessment_TugBerthing.asset";
    private const string DemoAreaPath = "Assets/Ocean/Settings/WaterArea_Demo.asset";
    private const string Model45mPath = "Assets/Ocean/Settings/ShipParameters_Model45m.asset";

    /// <summary>Создать недостающие ассеты буксировки (при входе в Play Mode).</summary>
    public static void EnsureCreated()
    {
        EnsureTug();
        if (AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(ScenarioPath) != null &&
            AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(CriteriaPath) != null)
        {
            AddMissingCriteria();
            return;
        }
        var log = new StringBuilder("[Setup 19.3в] Сценарий 19-02 создан автоматически (при входе в Play Mode):\n");
        CreateScenario(log, false);
        Debug.Log(log.ToString().TrimEnd());
    }

    [MenuItem("Tools/VR Bridge/Rebuild Demo Tug Berth Scenario")]
    private static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup 19.3в] Выйдите из Play Mode и запустите снова.");
            return;
        }
        var log = new StringBuilder("[Setup 19.3в] Сценарий 19-02 заполнен заново:\n");
        CreateScenario(log, true);
        log.Append("Сцену сохранять не нужно.");
        Debug.Log(log.ToString());
    }

    private static void EnsureTug()
    {
        if (AssetDatabase.LoadAssetAtPath<ShipParameters>(TugPath) != null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(TugPath)));
        AssetDatabase.Refresh();
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        ShipPresets.FillTug(p);
        AssetDatabase.CreateAsset(p, TugPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Setup 19.3] Буксир создан автоматически (при входе в Play Mode): {TugPath} — L {p.length} м, B {p.beam} м, d {p.draft} м, " +
                  $"тяга на гаке {TugSpec.DefaultBollardPullTonnes:0} т (TowingController.bollardPullTonnes).");
    }

    private static void AddMissingCriteria()
    {
        AssessmentCriteria c = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(CriteriaPath);
        if (c == null || c.settings == null) return;
        int added = c.settings.AddMissing();
        if (added == 0) return;
        EditorUtility.SetDirty(c);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Setup 19.3в] {CriteriaPath}: добавлено критериев {added}");
    }

    private static void CreateScenario(StringBuilder log, bool refill)
    {
        WaterArea area = AssetDatabase.LoadAssetAtPath<WaterArea>(DemoAreaPath);
        ShipParameters ship = AssetDatabase.LoadAssetAtPath<ShipParameters>(Model45mPath);
        if (area == null || ship == null)
        {
            log.AppendLine($"  ! нет {DemoAreaPath} или {Model45mPath} — сценарий не создан");
            return;
        }
        AssessmentCriteria crit = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(CriteriaPath);
        if (crit == null)
        {
            crit = ScriptableObject.CreateInstance<AssessmentCriteria>();
            crit.settings = DemoTugBerthScenario.Assessment();
            AssetDatabase.CreateAsset(crit, CriteriaPath);
            log.AppendLine($"  + {CriteriaPath}: как у 19-01 (маршрутные выключены) + группа «Буксир» (G1–G4)");
        }
        else if (refill)
        {
            Undo.RecordObject(crit, "Rebuild 19-02 criteria");
            crit.settings = DemoTugBerthScenario.Assessment();
            EditorUtility.SetDirty(crit);
            log.AppendLine($"  = {CriteriaPath} заполнен заново");
        }

        ScenarioDefinition s = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(ScenarioPath);
        bool created = s == null;
        if (created) s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        else if (!refill) { AssetDatabase.SaveAssets(); return; }
        else Undo.RecordObject(s, "Rebuild 19-02");
        DemoTugBerthScenario.Fill(s, area, ship, crit);
        if (created) AssetDatabase.CreateAsset(s, ScenarioPath);
        else EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();
        log.AppendLine($"  + {ScenarioPath}: «{s.title}», ветер {ScenarioText.Num(s.windKnots)} уз откуда {ScenarioText.Bearing(s.windFromDeg)} ± {ScenarioText.Num(s.windRandomSectorDeg / 2f)}°, " +
                       $"буксир по вызову, лимит {s.timeLimitSeconds / 60f:F0} мин");
        foreach (string p in s.Validate()) log.AppendLine("  ! " + p);
    }
}
