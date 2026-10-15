// ============================================================
// SetupScenarios.cs — Движок сценариев в сцене (B2)
// Меню:
//   Tools/VR Bridge/Setup B2 Scenarios   — настройка сцены (идемпотентно);
//   Tools/VR Bridge/Rebuild Demo Scenario — заново заполнить первый сценарий (перезаписывает ассет).
//
// Setup B2 Scenarios:
//   1. ассет Assets/Ocean/Scenarios/Scenario_B2-01_Fairway.asset (если его нет —
//      DemoScenario.Fill: WaterArea_Demo, ShipParameters_Model45m);
//   2. объект Scenario в корне сцены: ScenarioRunner (ShipNavigation, GroundingMonitor,
//      WaterAreaView, WeatherController) + ExerciseConsoleLog; библиотека — все
//      ScenarioDefinition проекта;
//   3. ScenarioPanel (дочерний к Ship, рядом с BridgeMenu) — VR-панель и команды меню A.
// Требует Setup B1 Water Area. После запуска — Ctrl+S.
//
// Библиотека сценариев в редакторе собирается сама при входе в Play Mode
// (ScenarioLibrarySync): новый ассет Create → VR Bridge → Scenario сразу
// появляется в VR-меню выбора.
// ============================================================
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SetupScenarios
{
    private const string ScenarioDir = "Assets/Ocean/Scenarios";
    private const string DemoPath = ScenarioDir + "/Scenario_B2-01_Fairway.asset";
    private const string DemoAreaPath = "Assets/Ocean/Settings/WaterArea_Demo.asset";
    private const string ShipPath = "Assets/Ocean/Settings/ShipParameters_Model45m.asset";

    [MenuItem("Tools/VR Bridge/Setup B2 Scenarios")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup B2] Выйдите из Play Mode и запустите снова.");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        ShipNavigation nav = FindInScene<ShipNavigation>(scene);
        ShipMotion motion = FindInScene<ShipMotion>(scene);
        GroundingMonitor grounding = FindInScene<GroundingMonitor>(scene);
        WaterAreaView view = FindInScene<WaterAreaView>(scene);
        WeatherController weather = FindInScene<WeatherController>(scene);
        if (nav == null || motion == null || grounding == null || view == null)
        {
            Debug.LogError("[Setup B2] Не найдены ShipNavigation, ShipMotion, GroundingMonitor или WaterAreaView — сначала Setup B1 Water Area.");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Setup B2 Scenarios");
        var log = new StringBuilder($"[Setup B2] Сцена «{scene.name}»:\n");

        // ── 1. Первый сценарий ──
        ScenarioDefinition demo = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(DemoPath);
        if (demo == null)
        {
            demo = CreateDemo(log);
            if (demo == null) return;
        }
        else log.AppendLine($"  = {DemoPath} уже есть");

        // ── 2. ScenarioRunner + ExerciseConsoleLog ──
        ScenarioRunner runner = FindInScene<ScenarioRunner>(scene);
        if (runner == null)
        {
            var go = new GameObject("Scenario");
            Undo.RegisterCreatedObjectUndo(go, "Create Scenario");
            runner = Undo.AddComponent<ScenarioRunner>(go);
            log.AppendLine("  + объект Scenario (ScenarioRunner) в корне сцены");
        }
        else log.AppendLine("  = ScenarioRunner уже есть");
        Undo.RecordObject(runner, "Setup ScenarioRunner");
        runner.navigation = nav;
        runner.grounding = grounding;
        runner.waterArea = view;
        if (weather != null) runner.weather = weather;
        else log.AppendLine("  ! WeatherController не найден — погода сценария не применяется");
        List<ScenarioDefinition> all = ScenarioLibrarySync.FindAll();
        runner.scenarios = all;
        log.AppendLine($"  + библиотека: {all.Count} сценари(й/я): " + string.Join(", ", all.Select(s => s.id)));
        if (runner.GetComponent<ExerciseConsoleLog>() == null)
        {
            Undo.AddComponent<ExerciseConsoleLog>(runner.gameObject);
            log.AppendLine("  + ExerciseConsoleLog (события упражнения → консоль)");
        }
        EditorUtility.SetDirty(runner);

        // ── 3. ScenarioPanel ──
        ScenarioPanel panel = FindInScene<ScenarioPanel>(scene);
        if (panel == null)
        {
            var go = new GameObject("ScenarioPanel");
            Undo.RegisterCreatedObjectUndo(go, "Create ScenarioPanel");
            go.transform.SetParent(motion.transform, false);
            panel = Undo.AddComponent<ScenarioPanel>(go);
            log.AppendLine($"  + ScenarioPanel (дочерний к {motion.name}) — выбор, брифинг, результаты; команды меню A");
        }
        else log.AppendLine("  = ScenarioPanel уже есть");
        Undo.RecordObject(panel, "Setup ScenarioPanel");
        panel.runner = runner;
        EditorUtility.SetDirty(panel);

        if (FindInScene<BridgeMenu>(scene) == null)
            log.AppendLine("  ! BridgeMenu не найден — команды сценария недоступны (Setup B1 Water Area)");

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("Готово. Сохраните сцену (Ctrl+S). В Play Mode — панель выбора сценария; меню A → группа «Сценарий».");
        Debug.Log(log.ToString());
    }

    [MenuItem("Tools/VR Bridge/Rebuild Demo Scenario")]
    private static void RebuildDemo()
    {
        ScenarioDefinition s = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(DemoPath);
        if (s == null)
        {
            var log = new StringBuilder();
            CreateDemo(log);
            Debug.Log("[Сценарий] " + log);
            return;
        }
        if (!EditorUtility.DisplayDialog("Первый сценарий",
                $"Заполнить {DemoPath} заново? Все правки сценария (маршрут, брифинг, начальные условия) будут заменены исходными.",
                "Заполнить", "Отмена"))
            return;
        Undo.RecordObject(s, "Rebuild Demo Scenario");
        if (!FillDemo(s, null)) return;
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Сценарий] {DemoPath}: «{s.title}», {s.route.Count} точек, {s.RouteLength:F0} м.");
    }

    private static ScenarioDefinition CreateDemo(StringBuilder log)
    {
        Directory.CreateDirectory(ScenarioDir);
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        if (!FillDemo(s, log)) return null;
        AssetDatabase.CreateAsset(s, DemoPath);
        AssetDatabase.SaveAssets();
        log.AppendLine($"  + {DemoPath}: «{s.title}», {s.route.Count} точек, {s.RouteLength:F0} м");
        return s;
    }

    private static bool FillDemo(ScenarioDefinition s, StringBuilder log)
    {
        WaterArea area = AssetDatabase.LoadAssetAtPath<WaterArea>(DemoAreaPath);
        ShipParameters ship = AssetDatabase.LoadAssetAtPath<ShipParameters>(ShipPath);
        if (area == null)
        {
            Debug.LogError($"[Сценарий] Нет {DemoAreaPath} — сначала Setup B1 Water Area.");
            return false;
        }
        if (ship == null) log?.AppendLine($"  ! нет {ShipPath} — параметры судна в сценарии не заданы (берутся из сцены)");
        DemoScenario.Fill(s, area, ship);
        return true;
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T c = root.GetComponentInChildren<T>(true);
            if (c != null) return c;
        }
        return null;
    }
}

/// <summary>
/// Библиотека сценариев: при входе в Play Mode список ScenarioRunner.scenarios
/// заполняется всеми ассетами ScenarioDefinition проекта (по идентификатору).
/// </summary>
[InitializeOnLoad]
public static class ScenarioLibrarySync
{
    static ScenarioLibrarySync()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) Sync();
        };
    }

    public static List<ScenarioDefinition> FindAll()
    {
        var list = new List<ScenarioDefinition>();
        foreach (string guid in AssetDatabase.FindAssets("t:ScenarioDefinition"))
        {
            var s = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (s != null) list.Add(s);
        }
        list.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.id, b.id);
            return c != 0 ? c : string.CompareOrdinal(a.title, b.title);
        });
        return list;
    }

    private static void Sync()
    {
        SetupBerthScenario.EnsureCreated();   // 19.2б-3: сценарий швартовки 19-01 — без ручного Setup
        List<ScenarioDefinition> all = FindAll();
        foreach (ScenarioRunner runner in Object.FindObjectsByType<ScenarioRunner>(FindObjectsInactive.Include))
        {
            if (runner.scenarios != null && runner.scenarios.SequenceEqual(all)) continue;
            Undo.RecordObject(runner, "Scenario Library");
            runner.scenarios = new List<ScenarioDefinition>(all);
            EditorUtility.SetDirty(runner);
            if (runner.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(runner.gameObject.scene);
            Debug.Log($"[Сценарий] Библиотека обновлена: {all.Count} — " + string.Join(", ", all.Select(s => s.id)));
        }
    }
}
