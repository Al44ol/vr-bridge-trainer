// ============================================================
// SetupPlotter.cs — Навигационный планшет (D2)
//
// Tools/VR Bridge/Setup D2 Plotter (идемпотентно):
//   1. NavPlotter (корень сцены) — компонент планшета; ссылки на ScenarioRunner,
//      ShipNavigation, ExerciseAssessmentMonitor, BridgeLighting;
//   2. левый контроллер XR Rig («Left Controller») — к нему планшет крепится в Play Mode
//      (холст строится в коде; префаб XR Rig не меняется);
//   3. кнопка X: у HeightAdjust прежняя привязка (панель роста) должна быть выключена.
// Положение в руке (handOffset, наклон) и прочие поля компонента повторный запуск
// не трогает. После — сохранить сцену (Ctrl+S).
// ============================================================
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SetupPlotter
{
    [MenuItem("Tools/VR Bridge/Setup D2 Plotter")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup D2] Выйдите из Play Mode и запустите снова.");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        ScenarioRunner runner = FindInScene<ScenarioRunner>(scene);
        ShipNavigation nav = FindInScene<ShipNavigation>(scene);
        if (runner == null || nav == null)
        {
            Debug.LogError("[Setup D2] Не найдены ScenarioRunner или ShipNavigation.");
            return;
        }
        var log = new StringBuilder($"[Setup D2] Сцена «{scene.name}»:\n");

        // ── 1. Компонент ──
        NavPlotter plotter = FindInScene<NavPlotter>(scene);
        if (plotter == null)
        {
            var go = new GameObject("NavPlotter");
            Undo.RegisterCreatedObjectUndo(go, "Create NavPlotter");
            SceneManager.MoveGameObjectToScene(go, scene);
            plotter = Undo.AddComponent<NavPlotter>(go);
            log.AppendLine("  + NavPlotter (навигационный планшет в левой руке; кнопка X, меню Y «Навигационный планшет»)");
        }
        else log.AppendLine("  = NavPlotter уже есть (положение в руке и настройки не меняются)");
        Undo.RecordObject(plotter, "Setup NavPlotter");
        plotter.runner = runner;
        plotter.navigation = nav;
        plotter.assessment = FindInScene<ExerciseAssessmentMonitor>(scene);
        plotter.lighting = FindInScene<BridgeLighting>(scene);
        if (plotter.assessment == null) log.AppendLine("  ! ExerciseAssessmentMonitor не найден (Setup C2) — в упражнении цели на планшете не появятся (нет обнаружения)");
        if (plotter.lighting == null) log.AppendLine("  ! BridgeLighting не найден (Setup B4) — планшет ночью не затемняется");

        // ── 2. Левый контроллер ──
        Transform left = null;
        foreach (GameObject r in scene.GetRootGameObjects())
        {
            foreach (Transform t in r.GetComponentsInChildren<Transform>(true))
                if (t.name == "Left Controller") { left = t; break; }
            if (left != null) break;
        }
        plotter.leftController = left;
        log.AppendLine(left != null ? $"  + левый контроллер: {Path(left)}"
                                    : "  ! «Left Controller» в XR Rig не найден — планшет появится перед игроком без руки; задайте NavPlotter.leftController вручную");
        EditorUtility.SetDirty(plotter);

        // ── 3. Кнопка X ──
        HeightAdjust height = FindInScene<HeightAdjust>(scene);
        if (height != null && height.controllerButton)
        {
            Undo.RecordObject(height, "Setup D2 X button");
            height.controllerButton = false;
            EditorUtility.SetDirty(height);
            log.AppendLine("  + HeightAdjust.controllerButton выключен: кнопка X — планшет (рост — меню Y «Рост игрока…»)");
        }
        else log.AppendLine("  = кнопка X свободна (HeightAdjust.controllerButton выкл)");

        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("Готово. Сохраните сцену (Ctrl+S). В Play Mode: кнопка X — планшет в левой руке; окно инструктора / меню A «Отладка» — «Планшет: все цели».");
        Debug.Log(log.ToString());
    }

    private static string Path(Transform t)
    {
        string p = t.name;
        for (Transform x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
        return p;
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
