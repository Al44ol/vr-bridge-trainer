// ============================================================
// SetupBerthScenario.cs — Сценарий швартовки (запрос 19.2б-3, часть «а»)
//
// Tools/VR Bridge/Setup 19.2 Berth Scenario (идемпотентно, СЦЕНУ НЕ ПРАВИТ — сохранять не нужно):
//   1. Assessment_Default.asset — добавить новый критерий M1 (группа «Швартовка»);
//   2. Assessment_Berthing.asset — набор критериев сценария швартовки: по умолчанию, маршрутные
//      критерии выключены (DemoBerthScenario.Assessment);
//   3. сценарий Assets/Ocean/Scenarios/Scenario_19-01_Berth.asset «Швартовка к причалу лагом»
//      (DemoBerthScenario: причал №1, 3 СС, штиль, задания, финиш «ошвартован»).
// С 16.10.2026 то же делается само при входе в Play Mode (ScenarioLibrarySync → EnsureCreated), если ассетов
// ещё нет: сценарий 19-01 появляется в выборе без ручного Setup. Существующие ассеты при этом не меняются.
// Библиотека сценариев ScenarioRunner собирается сама при входе в Play Mode (ScenarioLibrarySync).
// Tools/VR Bridge/Rebuild Demo Berth Scenario — заполнить 19-01 и Assessment_Berthing заново (правки будут заменены).
// ============================================================
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SetupBerthScenario
{
    private const string ScenarioPath = "Assets/Ocean/Scenarios/Scenario_19-01_Berth.asset";
    private const string CriteriaPath = "Assets/Ocean/Scenarios/Assessment_Berthing.asset";
    private const string DefaultCriteriaPath = "Assets/Ocean/Scenarios/Assessment_Default.asset";
    private const string DemoAreaPath = "Assets/Ocean/Settings/WaterArea_Demo.asset";
    private const string Model45mPath = "Assets/Ocean/Settings/ShipParameters_Model45m.asset";

    [MenuItem("Tools/VR Bridge/Setup 19.2 Berth Scenario")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup 19.2б-3] Выйдите из Play Mode и запустите снова.");
            return;
        }
        var log = new StringBuilder("[Setup 19.2б-3] Сценарий швартовки:\n");
        Create(log);
        log.Append("Готово. Сцену сохранять не нужно. В Play Mode: выбор сценария → 19-01.");
        Debug.Log(log.ToString());
    }

    /// <summary>
    /// При входе в Play Mode (ScenarioLibrarySync): создать сценарий 19-01 и набор критериев, если их нет,
    /// чтобы сценарий был в выборе без ручного Setup. Существующие ассеты не меняются.
    /// </summary>
    public static void EnsureCreated()
    {
        if (AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(ScenarioPath) != null &&
            AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(CriteriaPath) != null)
        {
            AddMissingCriteria();   // 19.2б-3в: новые критерии (M2–M9) — в существующие наборы
            return;
        }
        var log = new StringBuilder("[Setup 19.2б-3] Сценарий швартовки создан автоматически (при входе в Play Mode):\n");
        Create(log);
        Debug.Log(log.ToString().TrimEnd());
    }

    /// <summary>Дописать в Assessment_Default и Assessment_Berthing критерии каталога, которых там нет (значения по умолчанию).</summary>
    private static void AddMissingCriteria()
    {
        var log = new StringBuilder();
        foreach (string path in new[] { DefaultCriteriaPath, CriteriaPath })
        {
            AssessmentCriteria c = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(path);
            if (c == null || c.settings == null) continue;
            int added = c.settings.AddMissing();
            if (added == 0) continue;
            EditorUtility.SetDirty(c);
            log.AppendLine($"  + {path}: добавлено критериев {added}");
        }
        if (log.Length == 0) return;
        AssetDatabase.SaveAssets();
        Debug.Log("[Setup 19.2б-3] Новые критерии группы «Швартовка» добавлены в наборы (при входе в Play Mode):\n" + log.ToString().TrimEnd());
    }

    private static void Create(StringBuilder log)
    {
        // ── 1. M1 в наборе по умолчанию ──
        AssessmentCriteria def = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(DefaultCriteriaPath);
        if (def == null) log.AppendLine($"  ! {DefaultCriteriaPath} нет — сначала Tools/VR Bridge/Setup C2 Assessment");
        else
        {
            Undo.RecordObject(def, "Setup 19.2b-3 criteria");
            int added = def.settings.AddMissing();
            EditorUtility.SetDirty(def);
            log.AppendLine(added > 0 ? $"  + {DefaultCriteriaPath}: добавлено критериев {added} (M1 «Швартовка»)" : $"  = {DefaultCriteriaPath}: M1 уже есть");
        }

        // ── 2. Набор сценария швартовки ──
        AssessmentCriteria berthing = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(CriteriaPath);
        if (berthing == null)
        {
            berthing = ScriptableObject.CreateInstance<AssessmentCriteria>();
            FillCriteria(berthing);
            AssetDatabase.CreateAsset(berthing, CriteriaPath);
            log.AppendLine($"  + {CriteriaPath}: маршрутные критерии выключены, группа «Швартовка» (M1)");
        }
        else
        {
            Undo.RecordObject(berthing, "Setup 19.2b-3 berthing criteria");
            int added = berthing.settings.AddMissing();
            EditorUtility.SetDirty(berthing);
            log.AppendLine($"  = {CriteriaPath} уже есть" + (added > 0 ? $", добавлено критериев {added}" : ""));
        }

        // ── 3. Сценарий 19-01 ──
        ScenarioDefinition s = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(ScenarioPath);
        if (s == null)
        {
            s = ScriptableObject.CreateInstance<ScenarioDefinition>();
            if (Fill(s, berthing, log))
            {
                AssetDatabase.CreateAsset(s, ScenarioPath);
                log.AppendLine($"  + {ScenarioPath}: «{s.title}», старт {s.initial.position}, курс {s.initial.headingDeg:F1}°, {s.initial.speedKnots} уз, лимит {s.timeLimitSeconds / 60f:F0} мин");
            }
        }
        else log.AppendLine($"  = {ScenarioPath} уже есть (заполнить заново — Tools/VR Bridge/Rebuild Demo Berth Scenario)");
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/VR Bridge/Rebuild Demo Berth Scenario")]
    private static void Rebuild()
    {
        ScenarioDefinition s = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(ScenarioPath);
        AssessmentCriteria berthing = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(CriteriaPath);
        if (s == null || berthing == null)
        {
            Debug.LogWarning($"[Сценарий] Нет {ScenarioPath} или {CriteriaPath} — запустите Tools/VR Bridge/Setup 19.2 Berth Scenario.");
            return;
        }
        if (!EditorUtility.DisplayDialog("Сценарий 19-01",
                $"Заполнить {ScenarioPath} и {CriteriaPath} заново? Все правки будут заменены исходными.", "Заполнить", "Отмена"))
            return;
        Undo.RecordObject(s, "Rebuild Demo Berth Scenario");
        Undo.RecordObject(berthing, "Rebuild Demo Berth Scenario");
        FillCriteria(berthing);
        EditorUtility.SetDirty(berthing);
        var log = new StringBuilder();
        if (!Fill(s, berthing, log)) return;
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Сценарий] {ScenarioPath}: «{s.title}». {log}");
    }

    private static void FillCriteria(AssessmentCriteria c)
    {
        c.notes = "Сценарий швартовки (19.2б-3): значения по умолчанию, маршрутные критерии выключены — линия подхода только для карты. " +
                  "Значения предварительные.";
        c.settings = DemoBerthScenario.Assessment();
    }

    private static bool Fill(ScenarioDefinition s, AssessmentCriteria criteria, StringBuilder log)
    {
        WaterArea area = AssetDatabase.LoadAssetAtPath<WaterArea>(DemoAreaPath);
        ShipParameters own = AssetDatabase.LoadAssetAtPath<ShipParameters>(Model45mPath);
        if (area == null || own == null)
        {
            Debug.LogError($"[Сценарий] Нужны {DemoAreaPath} и {Model45mPath} (Setup B1, A2).");
            return false;
        }
        if (area.berths == null || area.berths.Count <= DemoBerthScenario.BerthIndex)
        {
            Debug.LogError($"[Сценарий] В {DemoAreaPath} нет причала №1 — запустите Tools/VR Bridge/Setup 19.2 Berth (или Rebuild Demo Water Area).");
            return false;
        }
        DemoBerthScenario.Fill(s, area, own, criteria);
        foreach (string p in s.Validate()) log.AppendLine($"  ! {p}");
        return true;
    }
}
