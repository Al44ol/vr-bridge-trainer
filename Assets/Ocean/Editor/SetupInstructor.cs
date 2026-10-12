// ============================================================
// SetupInstructor.cs — Окно инструктора (D1)
//
// Tools/VR Bridge/Setup D1 Instructor (идемпотентно):
//   1. Assessment_Default.asset — добавить критерии N1, N2 (реакция на отказы);
//   2. Ship/InstructorInputs — вводные инструктора (отказы, пауза, оповещение;
//      дочерний к Ship: надпись перед обучающимся качается с мостиком);
//   3. Instructor (корень сцены) — InstructorStation + InstructorWindow + InstructorPip:
//      окно инструктора на мониторе, F2 — зеркало из шлема;
//   4. EventSystem → XRUIInputModule: включить мышь (кнопки окна инструктора;
//      лучи и касания XR работают как раньше).
// После — сохранить сцену (Ctrl+S).
// ============================================================
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public static class SetupInstructor
{
    private const string CriteriaPath = "Assets/Ocean/Scenarios/Assessment_Default.asset";

    [MenuItem("Tools/VR Bridge/Setup D1 Instructor")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Setup D1] Выйдите из Play Mode и запустите снова.");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        ScenarioRunner runner = FindInScene<ScenarioRunner>(scene);
        ShipNavigation nav = FindInScene<ShipNavigation>(scene);
        ShipMotion motion = FindInScene<ShipMotion>(scene);
        if (runner == null || nav == null || motion == null)
        {
            Debug.LogError("[Setup D1] Не найдены ScenarioRunner, ShipNavigation или ShipMotion (Ship).");
            return;
        }
        var log = new StringBuilder($"[Setup D1] Сцена «{scene.name}»:\n");

        // ── 1. Критерии N1, N2 ──
        AssessmentCriteria criteria = AssetDatabase.LoadAssetAtPath<AssessmentCriteria>(CriteriaPath);
        if (criteria == null) log.AppendLine($"  ! {CriteriaPath} нет — сначала Tools/VR Bridge/Setup C2 Assessment");
        else
        {
            Undo.RecordObject(criteria, "Setup D1 criteria");
            int added = criteria.settings.AddMissing();
            EditorUtility.SetDirty(criteria);
            AssetDatabase.SaveAssets();
            log.AppendLine(added > 0 ? $"  + {CriteriaPath}: добавлено критериев {added} (N1, N2)" : $"  = {CriteriaPath}: критерии N1, N2 уже есть");
        }

        WeatherController weather = FindInScene<WeatherController>(scene);

        // ── 2. Вводные ──
        InstructorInputs inputs = FindInScene<InstructorInputs>(scene);
        if (inputs == null)
        {
            var go = new GameObject("InstructorInputs");
            Undo.RegisterCreatedObjectUndo(go, "Create InstructorInputs");
            go.transform.SetParent(motion.transform, false);
            inputs = Undo.AddComponent<InstructorInputs>(go);
            log.AppendLine($"  + {motion.name}/InstructorInputs (вводные, пауза, оповещение об отказах)");
        }
        else log.AppendLine("  = InstructorInputs уже есть");
        Undo.RecordObject(inputs, "Setup InstructorInputs");
        inputs.runner = runner;
        inputs.navigation = nav;
        inputs.weather = weather;
        EditorUtility.SetDirty(inputs);

        // ── 3. Окно инструктора ──
        InstructorStation station = FindInScene<InstructorStation>(scene);
        if (station == null)
        {
            var go = new GameObject("Instructor");
            Undo.RegisterCreatedObjectUndo(go, "Create Instructor");
            SceneManager.MoveGameObjectToScene(go, scene);
            station = Undo.AddComponent<InstructorStation>(go);
            log.AppendLine("  + Instructor (окно инструктора на мониторе; F2 — зеркало из шлема)");
        }
        else log.AppendLine("  = InstructorStation уже есть");
        InstructorWindow window = station.GetComponent<InstructorWindow>();
        if (window == null) window = Undo.AddComponent<InstructorWindow>(station.gameObject);
        if (station.GetComponent<InstructorPip>() == null) Undo.AddComponent<InstructorPip>(station.gameObject);
        Undo.RecordObject(window, "Setup InstructorWindow");
        window.runner = runner;
        window.recorder = FindInScene<ExerciseRecorder>(scene);
        window.assessment = FindInScene<ExerciseAssessmentMonitor>(scene);
        window.inputs = inputs;
        window.weather = weather;
        window.grounding = nav.GetComponent<GroundingMonitor>();
        EditorUtility.SetDirty(window);
        if (window.recorder == null) log.AppendLine("  ! ExerciseRecorder не найден (Setup C1) — ID обучающегося из окна не записывается");
        if (window.assessment == null) log.AppendLine("  ! ExerciseAssessmentMonitor не найден (Setup C2) — без ситуаций МППСС и балла");

        // ── 4. Мышь в EventSystem ──
        EventSystem es = FindInScene<EventSystem>(scene);
        BaseInputModule module = es != null ? es.GetComponent<BaseInputModule>() : null;
        if (module == null) log.AppendLine("  ! EventSystem / модуль ввода не найден — кнопки окна мышью не нажмутся");
        else
        {
            var so = new SerializedObject(module);
            SerializedProperty mouse = so.FindProperty("m_EnableMouseInput");
            if (mouse == null) log.AppendLine($"  = {module.GetType().Name}: свойства мыши нет (мышь работает по умолчанию)");
            else if (mouse.boolValue) log.AppendLine($"  = {module.GetType().Name}: мышь уже включена");
            else
            {
                mouse.boolValue = true;
                so.ApplyModifiedProperties();
                log.AppendLine($"  + {module.GetType().Name}: мышь включена (окно инструктора)");
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("Готово. Сохраните сцену (Ctrl+S). В Play Mode на мониторе — окно инструктора, F2 — зеркало из шлема; меню A → «Вводные».");
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
