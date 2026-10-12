// ============================================================
// SetupFog.cs — Ограниченная видимость и туманные сигналы (C4)
//
// Tools/VR Bridge/Setup C4 Fog (идемпотентно, сцену не правит):
//   1. Assessment_Default.asset — добавить новые критерии F1–F6 (группа «Ограниченная видимость»);
//   2. сценарий Assets/Ocean/Scenarios/Scenario_C4-01_Fog.asset «Плавание в тумане»
//      (DemoFogScenario: видимость 0,5 мили, безопасная скорость 6 уз, T1 — сухогруз справа),
//      набор критериев — Assessment_Default.
// Библиотека сценариев ScenarioRunner собирается сама при входе в Play Mode (ScenarioLibrarySync).
// Tools/VR Bridge/Rebuild Demo Fog Scenario — заполнить C4-01 заново (правки будут заменены).
// Остальное C4 работает без настройки сцены: гудок «пока держат» (ShipHorn подписывается на
// отпускание кнопки сам), видимость сценария (WeatherController), сигналы целей (TrafficController).
// ============================================================
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SetupFog
{
    private const string ScenarioPath = "Assets/Ocean/Scenarios/Scenario_C4-01_Fog.asset";
    private const string CriteriaPath = "Assets/Ocean/Scenarios/Assessment_Default.asset";
    private const string DemoAreaPath = "Assets/Ocean/Settings/WaterArea_Demo.asset";
    private const string Model45mPath = "Assets/Ocean/Settings/ShipParameters_Model45m.asset";
    private const string MarinerPath = "Assets/Ocean/Settings/ShipParameters_Mariner.asset";

    [MenuItem("Tools/VR Bridge/Setup C4 Fog")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup C4] Выйдите из Play Mode и запустите снова.");
            return;
        }
        var log = new StringBuilder("[Setup C4] Ограниченная видимость и туманные сигналы:\n");

        // ── 1. Критерии F1–F6 в наборе по умолчанию ──
        AssessmentCriteria criteria = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(CriteriaPath);
        if (criteria == null) log.AppendLine($"  ! {CriteriaPath} нет — сначала Tools/VR Bridge/Setup C2 Assessment");
        else
        {
            Undo.RecordObject(criteria, "Setup C4 criteria");
            int added = criteria.settings.AddMissing();
            EditorUtility.SetDirty(criteria);
            log.AppendLine(added > 0
                ? $"  + {CriteriaPath}: добавлено критериев {added} (F1–F6), вес группы «Ограниченная видимость» {criteria.settings.weightVisibility}"
                : $"  = {CriteriaPath}: критерии F1–F6 уже есть");
        }

        // ── 2. Сценарий C4-01 ──
        ScenarioDefinition s = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(ScenarioPath);
        if (s == null)
        {
            s = ScriptableObject.CreateInstance<ScenarioDefinition>();
            if (Fill(s, criteria, log))
            {
                AssetDatabase.CreateAsset(s, ScenarioPath);
                log.AppendLine($"  + {ScenarioPath}: «{s.title}», видимость {s.visibilityMeters:F0} м, безопасная скорость {s.safeSpeedKnots} уз, судов {s.traffic.Count}");
            }
        }
        else log.AppendLine($"  = {ScenarioPath} уже есть (заполнить заново — Tools/VR Bridge/Rebuild Demo Fog Scenario)");
        AssetDatabase.SaveAssets();
        log.Append("Готово. Сцену сохранять не нужно. В Play Mode: выбор сценария → C4-01; меню A → «Обстановка» → «Видимость».");
        Debug.Log(log.ToString());
    }

    [MenuItem("Tools/VR Bridge/Rebuild Demo Fog Scenario")]
    private static void Rebuild()
    {
        ScenarioDefinition s = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(ScenarioPath);
        if (s == null)
        {
            Debug.LogWarning($"[Сценарий] Нет {ScenarioPath} — запустите Tools/VR Bridge/Setup C4 Fog.");
            return;
        }
        if (!EditorUtility.DisplayDialog("Сценарий C4-01",
                $"Заполнить {ScenarioPath} заново? Все правки будут заменены исходными.", "Заполнить", "Отмена"))
            return;
        Undo.RecordObject(s, "Rebuild Demo Fog Scenario");
        var log = new StringBuilder();
        if (!Fill(s, AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(CriteriaPath), log)) return;
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Сценарий] {ScenarioPath}: «{s.title}». {log}");
    }

    private static bool Fill(ScenarioDefinition s, AssessmentCriteria criteria, StringBuilder log)
    {
        WaterArea area = AssetDatabase.LoadAssetAtPath<WaterArea>(DemoAreaPath);
        ShipParameters own = AssetDatabase.LoadAssetAtPath<ShipParameters>(Model45mPath);
        ShipParameters mariner = AssetDatabase.LoadAssetAtPath<ShipParameters>(MarinerPath);
        if (area == null || own == null || mariner == null)
        {
            Debug.LogError($"[Сценарий] Нужны {DemoAreaPath}, {Model45mPath}, {MarinerPath} (Setup B1, калибровка A3.2).");
            return false;
        }
        DemoFogScenario.Fill(s, area, own, mariner);
        s.assessment = criteria;
        foreach (string p in s.Validate()) log.AppendLine($"  ! {p}");
        return true;
    }
}
