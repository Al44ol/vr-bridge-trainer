// ============================================================
// ScenarioFiles.cs — Сценарии из файлов в библиотеке тренажёра (запрос 21.1)
//
// Ставится сам на объект Scenario (как TrainerModeSwitch). При запуске:
//   • папки Документы\VRBridgeTrainer\{Мои сценарии, Мои акватории, Образцы} (Quest — папка приложения);
//   • встроенные сценарии → «Образцы» в формате файла (только изменившиеся);
//   • сценарии из «Мои сценарии» — в конец ScenarioRunner.scenarios (их видят выбор в шлеме и окно инструктора),
//     отчёт — «Загрузка сценариев.txt» и консоль.
// Reload() («Обновить файлы» в окне инструктора) — заново: прежние файловые сценарии убираются из списка
// (загруженный сейчас упражнение не прерывается). Встроенные ассеты не меняются никогда.
// ToFileText — копия сценария для записи упражнения (scenario.json в папке сессии).
// 21.4б: «Пройти в тренажёре» из редактора (TrainerScenes.PlayOnLoad) — после загрузки этот сценарий открывается на брифинге.
// ============================================================
using System;
using System.IO;
using UnityEngine;

public class ScenarioFiles : MonoBehaviour
{
    [Tooltip("Папка сценариев; пусто — Документы\\VRBridgeTrainer (на Quest — папка приложения)")]
    public string rootOverride = "";
    [Tooltip("Писать встроенные сценарии в «Образцы» при запуске")]
    public bool exportSamples = true;

    public static ScenarioFiles Instance { get; private set; }
    public ScenarioCatalog Catalog { get; private set; }
    public ScenarioLibraryReport LastReport { get; private set; }
    public DateTime LastLoad { get; private set; }
    public string Root { get; private set; } = "";
    /// <summary>Список сценариев обновлён (окно инструктора — перерисовать).</summary>
    public event Action Reloaded;

    private ScenarioRunner runner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    internal static void Ensure()   // 21.2а: и при повторной загрузке сцены — TrainerScenes
    {
        if (FindAnyObjectByType<ScenarioFiles>() != null) return;
        ScenarioRunner r = FindAnyObjectByType<ScenarioRunner>();
        if (r == null) return;   // не сцена тренажёра
        r.gameObject.AddComponent<ScenarioFiles>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) Debug.LogWarning("[Сценарии] В сцене несколько ScenarioFiles — используется первый.", this);
        else Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        runner = GetComponent<ScenarioRunner>();
        if (runner == null) runner = FindAnyObjectByType<ScenarioRunner>();
        if (runner == null) return;
        Root = !string.IsNullOrWhiteSpace(rootOverride) ? rootOverride
             : Application.platform == RuntimePlatform.Android ? Path.Combine(Application.persistentDataPath, "VRBridgeTrainer")
             : ScenarioFolder.DefaultRoot();

        var nav = FindAnyObjectByType<ShipNavigation>();
        var sw = FindAnyObjectByType<ShipTypeSwitcher>();
        Catalog = ScenarioCatalog.From(runner.scenarios,
            nav != null ? nav.parameters : null,
            sw != null ? sw.bridgeParameters : null, sw != null ? sw.boatParameters : null, sw != null ? sw.TugParameters : null);
        try
        {
            ScenarioFolder.Ensure(Root);
            if (exportSamples)
            {
                int n = ScenarioFolder.ExportSamples(Root, Catalog);
                if (n > 0) Debug.Log($"[Сценарии] Образцы встроенных сценариев обновлены: {n} файл(ов) → {Path.Combine(Root, ScenarioFolder.SamplesDir)}");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Сценарии] Папка {Root} недоступна: {e.Message} — сценарии из файлов не загружаются.");
            return;
        }
        Reload();
    }

    /// <summary>Загрузить сценарии из папки заново (встроенные остаются, файловые заменяются).</summary>
    public void Reload()
    {
        if (runner == null || Catalog == null) return;
        ScenarioLibraryReport rep;
        try { rep = ScenarioFolder.Load(Root, Catalog); }
        catch (Exception e)
        {
            Debug.LogWarning($"[Сценарии] Не удалось прочитать {Root}: {e.Message}");
            return;
        }
        runner.scenarios.RemoveAll(s => s == null || s.IsFromFile);
        runner.scenarios.AddRange(rep.Scenarios);
        LastReport = rep;
        LastLoad = DateTime.Now;

        string text = rep.Text(LastLoad);
        try { ScenarioFolder.SafeWrite(Path.Combine(Root, ScenarioFolder.ReportFile), text); }
        catch (Exception e) { Debug.LogWarning($"[Сценарии] Отчёт загрузки не записан: {e.Message}"); }
        if (rep.ErrorFiles > 0 || rep.WarningFiles > 0) Debug.LogWarning("[Сценарии] " + text);
        else Debug.Log($"[Сценарии] {rep.Summary()} ({Root})");
        Reloaded?.Invoke();
        // 21.4б: «Пройти в тренажёре» из редактора — открыть этот сценарий на брифинге
        if (!string.IsNullOrEmpty(TrainerScenes.PlayOnLoad))
        {
            string path = TrainerScenes.PlayOnLoad;
            TrainerScenes.PlayOnLoad = "";
            StartCoroutine(PlayFromEditor(path));
        }
    }

    private System.Collections.IEnumerator PlayFromEditor(string path)
    {
        yield return null;   // Start остальных компонентов тренажёра (судно, погода, окно инструктора)
        yield return null;
        string full = Path.GetFullPath(path);
        ScenarioDefinition s = runner.scenarios.Find(x => x != null && x.IsFromFile &&
                                                     string.Equals(Path.GetFullPath(x.sourceFile), full, StringComparison.OrdinalIgnoreCase));
        if (s == null)
        {
            Debug.LogWarning($"[Сценарии] Из редактора: сценарий {Path.GetFileName(path)} не загрузился — см. «{ScenarioFolder.ReportFile}».");
            yield break;
        }
        runner.Load(s);
        InstructorWindow w = FindAnyObjectByType<InstructorWindow>();
        if (w != null) w.ShowScenario(s);
        Debug.Log($"[Сценарии] Из редактора: «{s.id} {s.title}» — брифинг ({Path.GetFileName(path)}).");
    }

    /// <summary>Сценарий в формате файла (копия в запись упражнения); null — не получилось.</summary>
    public static string ToFileText(ScenarioDefinition s)
    {
        if (s == null) return null;
        try { return ScenarioFile.Write(s, Instance != null ? Instance.Catalog : null); }   // как выполнялся (вариант на катере — с катером)
        catch (Exception e)
        {
            Debug.LogWarning($"[Сценарии] Копия сценария «{s.title}» для записи не сделана: {e.Message}");
            return null;
        }
    }
}
