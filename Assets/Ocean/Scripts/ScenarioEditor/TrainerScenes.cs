// ============================================================
// TrainerScenes.cs — Переход «тренажёр ↔ редактор сценариев» и начальное меню (запрос 21.2а)
//
// Редактор — отдельная сцена, создаётся кодом при переходе (SceneManager.CreateScene): ассета сцены и
// правки Build Settings не нужно. Тренажёр (BridgeScene) при этом выгружается целиком, обратно — загружается
// заново (как при запуске). Перед выгрузкой запоминаются каталог встроенных ассетов и папка сценариев
// (ScenarioFiles): ассеты держатся статической ссылкой и не выгружаются.
// Компоненты, которые «ставятся сами» по RuntimeInitializeOnLoadMethod (тот срабатывает один раз за запуск),
// при повторной загрузке тренажёра ставятся здесь — по SceneManager.sceneLoaded (их Ensure повторный вызов
// не дублирует).
// Начальное меню (StartMenu) — только при первом запуске сборки на ПК (на Quest и с ключом -training — сразу
// тренажёр); в Unity — по пункту Tools/VR Bridge/Start Menu in Play (по умолчанию выкл: Play — сразу тренажёр).
// ============================================================
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TrainerScenes
{
    public const string TrainingScene = "BridgeScene";
    public const string EditorScene = "ScenarioEditor";
#if UNITY_EDITOR
    public const string StartMenuPref = "VRBridge.StartMenuInPlay";
#endif

    /// <summary>Встроенные суда, акватории, наборы оценки и сценарии (запомнены при уходе из тренажёра).</summary>
    public static ScenarioCatalog Catalog { get; private set; }
    /// <summary>Папка сценариев (Документы\VRBridgeTrainer).</summary>
    public static string Root { get; private set; } = "";
    /// <summary>Выгрузка тренажёра идёт (редактор ждёт её, чтобы не было двух EventSystem).</summary>
    public static AsyncOperation Unloading { get; private set; }
    /// <summary>Тренажёр загружен не впервые за запуск (начальное меню больше не показывается).</summary>
    public static bool StartupDone { get; private set; }

    private static int trainingLoads;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        trainingLoads = 0;
        StartupDone = false;
        Unloading = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != TrainingScene) return;
        trainingLoads++;
        if (trainingLoads > 1) StartupDone = true;
        // Повторная загрузка — компоненты, которые ставятся сами (при первой — то же делает AfterSceneLoad; повтор безвреден)
        ScenarioFiles.Ensure();
        TrainerModeSwitch.Ensure();
        PilotOrderNotice.Ensure();
        TowingController.Ensure();
        TugWorkController.Ensure();
        StartMenu.Ensure();
    }

    /// <summary>Показывать начальное меню при этом запуске тренажёра.</summary>
    public static bool ShowStartMenu
    {
        get
        {
            if (StartupDone) return false;
            if (Application.platform == RuntimePlatform.Android) return false;   // автономный шлем — сразу тренажёр
            foreach (string a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "-training", StringComparison.OrdinalIgnoreCase)) return false;
#if UNITY_EDITOR
            return UnityEditor.EditorPrefs.GetBool(StartMenuPref, false);
#else
            return true;
#endif
        }
    }

    /// <summary>Из тренажёра — в редактор сценариев.</summary>
    public static void OpenEditor()
    {
        ScenarioFiles files = ScenarioFiles.Instance;
        if (files != null && files.Catalog != null)
        {
            Catalog = files.Catalog;
            Root = files.Root;
        }
        if (string.IsNullOrEmpty(Root)) Root = ScenarioFolder.DefaultRoot();
        if (Catalog == null) Catalog = new ScenarioCatalog();
        StartupDone = true;

        Scene from = SceneManager.GetActiveScene();
        Scene editor = SceneManager.CreateScene(EditorScene);
        SceneManager.SetActiveScene(editor);
        new GameObject("ScenarioEditor").AddComponent<ScenarioEditorApp>();
        Unloading = from.IsValid() && from.isLoaded && from != editor ? SceneManager.UnloadSceneAsync(from) : null;
        Debug.Log($"[Редактор] Открыт редактор сценариев; тренажёр выгружается. Папка: {Root}");
    }

    /// <summary>Из редактора — в тренажёр (загружается заново, как при запуске; новые файлы подхватятся).</summary>
    public static void OpenTraining()
    {
        StartupDone = true;
        Debug.Log("[Редактор] Возврат в тренажёр.");
        SceneManager.LoadScene(TrainingScene, LoadSceneMode.Single);
    }

    /// <summary>Выход из программы (в Unity — остановить Play).</summary>
    public static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
