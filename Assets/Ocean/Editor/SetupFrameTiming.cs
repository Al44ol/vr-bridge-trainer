// ============================================================
// SetupFrameTiming.cs — Замер производительности и режим рендеринга OpenXR
//
// Tools/VR Bridge/Setup Frame Timing (идемпотентно):
//   1. включает Player Settings → Frame Timing Stats (иначе GPU-время = 0);
//   2. добавляет объект PerfCapture с FrameTimingCapture (F8 — замер).
//
// Tools/VR Bridge/OpenXR Render Mode/Multi-pass | Single Pass Instanced:
//   переключает режим для PC (Standalone). Play Mode в редакторе использует
//   именно настройки Standalone, даже если активная платформа — Android.
//   Нужен, чтобы в любой момент снять замер в обоих режимах.
// После запуска сохраните сцену (Ctrl+S).
// ============================================================
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.OpenXR;

public static class SetupFrameTiming
{
    private const string MenuMulti = "Tools/VR Bridge/OpenXR Render Mode/Multi-pass";
    private const string MenuSpi = "Tools/VR Bridge/OpenXR Render Mode/Single Pass Instanced";

    [MenuItem("Tools/VR Bridge/Setup Frame Timing")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup Frame Timing] Выйдите из Play Mode и запустите снова.");
            return;
        }

        var log = new System.Text.StringBuilder("[Setup Frame Timing]\n");
        if (!PlayerSettings.enableFrameTimingStats)
        {
            PlayerSettings.enableFrameTimingStats = true;
            log.AppendLine("  + Player Settings: Frame Timing Stats включён");
        }
        else log.AppendLine("  = Frame Timing Stats уже включён");

        Scene scene = SceneManager.GetActiveScene();
        FrameTimingCapture capture = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            capture = root.GetComponentInChildren<FrameTimingCapture>(true);
            if (capture != null) break;
        }
        if (capture == null)
        {
            var go = new GameObject("PerfCapture");
            Undo.RegisterCreatedObjectUndo(go, "Setup Frame Timing");
            Undo.AddComponent<FrameTimingCapture>(go);
            EditorSceneManager.MarkSceneDirty(scene);
            log.AppendLine("  + PerfCapture (FrameTimingCapture, F8 — замер 30 с)");
        }
        else log.AppendLine("  = FrameTimingCapture уже есть на " + capture.name);

        var settings = StandaloneSettings();
        log.Append("  Режим OpenXR (PC): ").AppendLine(settings != null ? settings.renderMode.ToString() : "?");
        log.Append("Готово. Сохраните сцену (Ctrl+S).");
        Debug.Log(log.ToString());
    }

    [MenuItem(MenuMulti, false, 100)]
    private static void SetMultiPass() => SetRenderMode(OpenXRSettings.RenderMode.MultiPass);

    [MenuItem(MenuSpi, false, 101)]
    private static void SetSinglePassInstanced() => SetRenderMode(OpenXRSettings.RenderMode.SinglePassInstanced);

    [MenuItem(MenuMulti, true)]
    private static bool ValidateMultiPass() => Validate();

    [MenuItem(MenuSpi, true)]
    private static bool ValidateSpi() => Validate();

    private static bool Validate()
    {
        var s = StandaloneSettings();
        Menu.SetChecked(MenuMulti, s != null && s.renderMode == OpenXRSettings.RenderMode.MultiPass);
        Menu.SetChecked(MenuSpi, s != null && s.renderMode == OpenXRSettings.RenderMode.SinglePassInstanced);
        return s != null && !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static OpenXRSettings StandaloneSettings() =>
        OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);

    private static void SetRenderMode(OpenXRSettings.RenderMode mode)
    {
        var s = StandaloneSettings();
        if (s == null)
        {
            Debug.LogError("[OpenXR Render Mode] Настройки OpenXR для PC не найдены.");
            return;
        }
        if (s.renderMode == mode)
        {
            Debug.Log($"[OpenXR Render Mode] PC уже в режиме {mode}.");
            return;
        }
        Undo.RecordObject(s, "OpenXR Render Mode");
        s.renderMode = mode;
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();
        Debug.Log($"[OpenXR Render Mode] PC (Standalone): {mode}. Действует со следующего входа в Play Mode.");
    }
}
