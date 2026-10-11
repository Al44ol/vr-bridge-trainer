// ============================================================
// SetupRecording.cs — Запись упражнения в сцене (C1)
// Меню: Tools/VR Bridge/Setup C1 Recording (идемпотентно).
//   1. ExerciseRecorder на объекте Scenario (рядом с ScenarioRunner и
//      ExerciseConsoleLog): ссылки на ScenarioRunner и ShipNavigation;
//   2. BridgeActionMonitor на ShipRoot (рядом с ShipNavigation): штурвал,
//      телеграф, гудок, погода → события действий обучающегося в шину;
//   3. ScenarioPanel — ссылка на ExerciseRecorder (ID обучающегося, папка записи).
// Требует Setup B2 Scenarios. После запуска — Ctrl+S.
// Воспроизведение записанных сессий — Tools/VR Bridge/Session Replay.
// ============================================================
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SetupRecording
{
    [MenuItem("Tools/VR Bridge/Setup C1 Recording")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup C1] Выйдите из Play Mode и запустите снова.");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        ScenarioRunner runner = FindInScene<ScenarioRunner>(scene);
        ShipNavigation nav = FindInScene<ShipNavigation>(scene);
        if (runner == null || nav == null)
        {
            Debug.LogError("[Setup C1] Не найдены ScenarioRunner или ShipNavigation — сначала Setup B2 Scenarios.");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Setup C1 Recording");
        var log = new StringBuilder($"[Setup C1] Сцена «{scene.name}»:\n");

        // ── 1. ExerciseRecorder ──
        ExerciseRecorder recorder = FindInScene<ExerciseRecorder>(scene);
        if (recorder == null)
        {
            recorder = Undo.AddComponent<ExerciseRecorder>(runner.gameObject);
            log.AppendLine($"  + ExerciseRecorder на «{runner.name}» (запись → {ExerciseRecorder.DefaultRoot})");
        }
        else log.AppendLine("  = ExerciseRecorder уже есть");
        Undo.RecordObject(recorder, "Setup ExerciseRecorder");
        recorder.runner = runner;
        recorder.navigation = nav;
        EditorUtility.SetDirty(recorder);

        // ── 2. BridgeActionMonitor ──
        BridgeActionMonitor monitor = FindInScene<BridgeActionMonitor>(scene);
        if (monitor == null)
        {
            monitor = Undo.AddComponent<BridgeActionMonitor>(nav.gameObject);
            log.AppendLine($"  + BridgeActionMonitor на «{nav.name}» (штурвал, телеграф, гудок, погода → шина)");
        }
        else log.AppendLine("  = BridgeActionMonitor уже есть");
        Undo.RecordObject(monitor, "Setup BridgeActionMonitor");
        monitor.navigation = nav;
        monitor.wheel = nav.wheel;
        monitor.telegraph = nav.telegraph;
        monitor.horn = FindInScene<ShipHorn>(scene);
        monitor.weather = FindInScene<WeatherController>(scene);
        if (monitor.wheel == null) log.AppendLine("  ! у ShipNavigation не назначен штурвал — перекладки не записываются");
        if (monitor.telegraph == null) log.AppendLine("  ! у ShipNavigation не назначен телеграф — команды телеграфу не записываются");
        if (monitor.horn == null) log.AppendLine("  ! ShipHorn не найден — гудки не записываются");
        if (monitor.weather == null) log.AppendLine("  ! WeatherController не найден — смена погоды не записывается");
        EditorUtility.SetDirty(monitor);

        // ── 3. ScenarioPanel ──
        ScenarioPanel panel = FindInScene<ScenarioPanel>(scene);
        if (panel != null)
        {
            Undo.RecordObject(panel, "Setup ScenarioPanel recorder");
            panel.recorder = recorder;
            EditorUtility.SetDirty(panel);
            log.AppendLine("  + ScenarioPanel → ExerciseRecorder (ID обучающегося на брифинге)");
        }
        else log.AppendLine("  ! ScenarioPanel не найден — ID вводится только в инспекторе ExerciseRecorder");

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("Готово. Сохраните сцену (Ctrl+S). Сессии — Tools/VR Bridge/Session Replay.");
        Debug.Log(log.ToString());
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
