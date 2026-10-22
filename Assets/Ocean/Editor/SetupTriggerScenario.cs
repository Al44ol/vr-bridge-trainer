// ============================================================
// SetupTriggerScenario.cs — Ассет сценария 21-01 «Фарватер с триггерами» (запрос 21.6а)
//
// Сцену не правит. При входе в Play Mode (ScenarioLibrarySync → EnsureCreated) создаёт, если его нет,
// Assets/Ocean/Scenarios/Scenario_21-01_Triggers.asset (DemoTriggerScenario: маршрут B2-01, цель T1 — Mariner,
// четыре триггера; набор оценки — Assessment_Default, как у B2-01) — появляется в выборе без ручного Setup;
// ассет с demoVersion меньше DemoTriggerScenario.Version заполняется заново.
// Tools/VR Bridge/Rebuild Demo Trigger Scenario — заполнить 21-01 заново (правки будут заменены).
// ============================================================
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SetupTriggerScenario
{
    public const string ScenarioPath = "Assets/Ocean/Scenarios/Scenario_21-01_Triggers.asset";
    private const string DemoAreaPath = "Assets/Ocean/Settings/WaterArea_Demo.asset";
    private const string ShipPath = "Assets/Ocean/Settings/ShipParameters_Model45m.asset";
    private const string CargoPath = "Assets/Ocean/Settings/ShipParameters_Mariner.asset";
    private const string CriteriaPath = "Assets/Ocean/Scenarios/Assessment_Default.asset";

    /// <summary>Создать 21-01, если его нет, или обновить по версии (при входе в Play Mode).</summary>
    public static void EnsureCreated()
    {
        ScenarioDefinition s = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(ScenarioPath);
        if (s != null && s.demoVersion >= DemoTriggerScenario.Version) return;
        var log = new StringBuilder(s == null
            ? "[Setup 21.6а] Сценарий 21-01 создан автоматически (при входе в Play Mode):\n"
            : $"[Setup 21.6а] Сценарий 21-01 обновлён автоматически: версия {s.demoVersion} → {DemoTriggerScenario.Version} (правки в ассете заменены):\n");
        Create(log);
        Debug.Log(log.ToString().TrimEnd());
    }

    [MenuItem("Tools/VR Bridge/Rebuild Demo Trigger Scenario")]
    private static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup 21.6а] Выйдите из Play Mode и запустите снова.");
            return;
        }
        var log = new StringBuilder("[Setup 21.6а] Сценарий 21-01 заполнен заново:\n");
        Create(log);
        log.Append("Сцену сохранять не нужно.");
        Debug.Log(log.ToString());
    }

    private static void Create(StringBuilder log)
    {
        WaterArea area = AssetDatabase.LoadAssetAtPath<WaterArea>(DemoAreaPath);
        ShipParameters own = AssetDatabase.LoadAssetAtPath<ShipParameters>(ShipPath);
        ShipParameters cargo = AssetDatabase.LoadAssetAtPath<ShipParameters>(CargoPath);
        if (area == null || own == null || cargo == null)
        {
            log.AppendLine($"  ! нет {DemoAreaPath}, {ShipPath} или {CargoPath} — сценарий не создан");
            return;
        }
        AssessmentCriteria crit = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(CriteriaPath);
        if (crit == null) log.AppendLine($"  ! нет {CriteriaPath} — оценка по умолчанию");

        ScenarioDefinition s = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(ScenarioPath);
        bool created = s == null;
        if (created) s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        else Undo.RecordObject(s, "Rebuild 21-01");
        DemoTriggerScenario.Fill(s, area, own, cargo, crit);
        if (created) AssetDatabase.CreateAsset(s, ScenarioPath);
        else EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();
        log.AppendLine($"  + {ScenarioPath}: «{s.title}», маршрут B2-01, цель T1 ({cargo.name}) стоит у ({ScenarioText.Num(DemoTriggerScenario.TargetStart.East)}; " +
                       $"{ScenarioText.Num(DemoTriggerScenario.TargetStart.North)}), триггеров {s.triggers.Count}:");
        foreach (ScenarioTrigger t in s.triggers) log.AppendLine("    " + TriggerText.Line(t, s));
        foreach (string p in s.Validate()) log.AppendLine("  ! " + p);
    }
}
