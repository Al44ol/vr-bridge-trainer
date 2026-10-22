// ============================================================
// ScenarioFolder.cs — Папки сценариев на диске (запрос 21.1)
//
// Корень (на ПК — Документы\VRBridgeTrainer; путь задаёт сцена):
//   Мои сценарии\   — свои сценарии *.json: загружаются при запуске и кнопкой «Обновить»;
//   Мои акватории\  — свои акватории *.json (на них сценарии ссылаются по имени);
//   Образцы\        — встроенные сценарии и акватория в том же формате (пишутся при запуске,
//                     если изменились): скопировать в «Мои сценарии», поменять id и название — свой сценарий;
//   Загрузка сценариев.txt — что загрузилось, ошибки и предупреждения по файлам;
//   ПРОЧТИ.txt      — как пользоваться папками.
// Флешка: принести файл .json и положить в «Мои сценарии» — своя акватория уже внутри файла.
// Встроенные сценарии (ассеты) отсюда не меняются; файл с тем же id — отдельный сценарий (предупреждение).
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>Итог загрузки папок: сценарии и акватории, проблемы по файлам.</summary>
public sealed class ScenarioLibraryReport
{
    public readonly List<ScenarioDefinition> Scenarios = new List<ScenarioDefinition>();
    public readonly List<WaterArea> Areas = new List<WaterArea>();
    public readonly List<ScenarioLoadResult> Files = new List<ScenarioLoadResult>();
    public int ErrorFiles => Files.FindAll(f => f.Errors.Count > 0).Count;
    public int WarningFiles => Files.FindAll(f => f.Errors.Count == 0 && f.Warnings.Count > 0).Count;
    public string Root = "";

    /// <summary>Коротко: «Из файлов: 2 сценария, 1 акватория; с ошибками — 1 файл».</summary>
    public string Summary()
    {
        string s = $"Из файлов: сценариев {Scenarios.Count}, акваторий {Areas.Count}";
        if (ErrorFiles > 0) s += $"; не загружено (ошибки) — {ErrorFiles}";
        if (WarningFiles > 0) s += $"; с предупреждениями — {WarningFiles}";
        return s;
    }

    /// <summary>Подробно по файлам (Загрузка сценариев.txt, консоль, окно инструктора).</summary>
    public string Text(DateTime when)
    {
        var b = new StringBuilder();
        b.AppendLine($"Загрузка сценариев из файлов — {when:dd.MM.yyyy HH:mm:ss}");
        b.AppendLine($"Папка: {Root}");
        b.AppendLine(Summary());
        foreach (ScenarioLoadResult f in Files)
        {
            if (f.Errors.Count == 0 && f.Warnings.Count == 0) { b.AppendLine($"\n✓ {f.File}"); continue; }
            b.AppendLine($"\n{(f.Errors.Count > 0 ? "✗ НЕ ЗАГРУЖЕН" : "! загружен с предупреждениями")}: {f.File}");
            foreach (string e in f.Errors) b.AppendLine("   ошибка: " + e);
            foreach (string w in f.Warnings) b.AppendLine("   предупреждение: " + w);
        }
        return b.ToString();
    }
}

public static class ScenarioFolder
{
    public const string ScenariosDir = "Мои сценарии";
    public const string AreasDir = "Мои акватории";
    public const string SamplesDir = "Образцы";
    public const string ReportFile = "Загрузка сценариев.txt";
    public const string ReadmeFile = "ПРОЧТИ.txt";

    /// <summary>Корень по умолчанию на ПК: Документы\VRBridgeTrainer.</summary>
    public static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VRBridgeTrainer");

    /// <summary>Создать папки и ПРОЧТИ.txt (если нет).</summary>
    public static void Ensure(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, ScenariosDir));
        Directory.CreateDirectory(Path.Combine(root, AreasDir));
        Directory.CreateDirectory(Path.Combine(root, SamplesDir));
        WriteIfChanged(Path.Combine(root, ReadmeFile), Readme);
    }

    public const string Readme =
        "VR-тренажёр мостика — свои сценарии и акватории\r\n\r\n" +
        "«Мои сценарии» — файлы сценариев (.json). Загружаются при запуске тренажёра и кнопкой «Обновить файлы» в окне инструктора.\r\n" +
        "«Мои акватории» — файлы акваторий (.json), на которые ссылаются сценарии.\r\n" +
        "«Образцы» — встроенные сценарии в том же формате; тренажёр переписывает их при запуске. Чтобы сделать свой сценарий на основе встроенного,\r\n" +
        "скопируйте файл в «Мои сценарии» и поменяйте \"id\" и \"title\" (встроенные сценарии при этом не меняются).\r\n\r\n" +
        "Перенос на другую станцию: файл сценария самодостаточен (своя акватория вписана в него) — скопируйте его в «Мои сценарии» на той станции.\r\n" +
        "Что загрузилось и ошибки по файлам — «Загрузка сценариев.txt».\r\n\r\n" +
        "Редактор сценариев: «История» — прежние версии файлов (20 последних), «Корзина» — удалённые в редакторе, «Черновики» — после сбоя.\r\n" +
        "Всё это — в редакторе: «Открыть…» → «Библиотека» (копия, переименование, корзина, история версий, импорт и экспорт флешкой —\r\n" +
        "на флешке файлы лежат в «VRBridgeTrainer\\Мои сценарии» и «VRBridgeTrainer\\Мои акватории»).\r\n";

    /// <summary>Записать встроенные сценарии и их акватории в «Образцы» (только изменившиеся файлы); вернуть число записанных.</summary>
    public static int ExportSamples(string root, ScenarioCatalog catalog)
    {
        string dir = Path.Combine(root, SamplesDir);
        Directory.CreateDirectory(dir);
        int n = 0;
        foreach (ScenarioDefinition s in catalog.Scenarios)
            if (WriteIfChanged(Path.Combine(dir, SafeName($"{s.id} {s.title}") + ScenarioFile.Extension), ScenarioFile.Write(s, catalog))) n++;
        foreach (WaterArea a in catalog.Areas)
            if (WriteIfChanged(Path.Combine(dir, SafeName("Акватория " + a.name) + ScenarioFile.Extension), ScenarioFile.WriteArea(a))) n++;
        return n;
    }

    /// <summary>Загрузить акватории и сценарии из папок; встроенные — в каталоге (повторы id — предупреждение).</summary>
    public static ScenarioLibraryReport Load(string root, ScenarioCatalog catalog)
    {
        var rep = new ScenarioLibraryReport { Root = root };
        foreach (string path in Files(Path.Combine(root, AreasDir)))
        {
            ScenarioLoadResult r = ReadFile(path, text => ScenarioFile.ReadArea(text, Path.GetFileNameWithoutExtension(path)));
            r.File = Path.Combine(AreasDir, Path.GetFileName(path));
            if (r.Area != null)
            {
                if (catalog.Find(typeof(WaterArea), r.Area.name) != null || rep.Areas.Exists(a => a.name == r.Area.name))
                {
                    r.Errors.Add($"акватория с именем «{r.Area.name}» уже есть — переименуйте (поле \"name\")");
                    r.Area = null;
                }
                else rep.Areas.Add(r.Area);
            }
            rep.Files.Add(r);
        }
        var ids = new Dictionary<string, string>();
        foreach (ScenarioDefinition s in catalog.Scenarios) ids[s.id] = "встроенного сценария";
        foreach (string path in Files(Path.Combine(root, ScenariosDir)))
        {
            ScenarioLoadResult r = ReadFile(path, text => ScenarioFile.Read(text, catalog, rep.Areas, Path.GetFileNameWithoutExtension(path)));
            r.File = Path.Combine(ScenariosDir, Path.GetFileName(path));
            if (r.Scenario != null)
            {
                r.Scenario.sourceFile = path;
                if (ids.TryGetValue(r.Scenario.id ?? "", out string who))
                    r.Warnings.Insert(0, $"идентификатор «{r.Scenario.id}» уже есть у {who} — в записях и отчётах их не отличить; поменяйте \"id\"");
                else ids[r.Scenario.id ?? ""] = $"файла {Path.GetFileName(path)}";
                rep.Scenarios.Add(r.Scenario);
            }
            rep.Files.Add(r);
        }
        return rep;
    }

    private static ScenarioLoadResult ReadFile(string path, Func<string, ScenarioLoadResult> read)
    {
        try { return read(File.ReadAllText(path, Encoding.UTF8)); }
        catch (IOException e) { var r = new ScenarioLoadResult(); r.Errors.Add("не читается: " + e.Message); return r; }
        catch (UnauthorizedAccessException e) { var r = new ScenarioLoadResult(); r.Errors.Add("нет доступа: " + e.Message); return r; }
    }

    private static List<string> Files(string dir)
    {
        var list = new List<string>();
        if (Directory.Exists(dir))
            foreach (string f in Directory.GetFiles(dir, "*" + ScenarioFile.Extension, SearchOption.TopDirectoryOnly))
                if (string.Equals(Path.GetExtension(f), ScenarioFile.Extension, StringComparison.OrdinalIgnoreCase)) list.Add(f);   // не *.json.tmp
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    /// <summary>Имя файла без запрещённых символов.</summary>
    public static string SafeName(string s)
    {
        var b = new StringBuilder();
        foreach (char c in s ?? "") b.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c == '«' || c == '»' ? '_' : c);
        string r = b.ToString().Trim().TrimEnd('.');
        return r.Length > 0 ? (r.Length > 80 ? r.Substring(0, 80).TrimEnd() : r) : "без имени";
    }

    /// <summary>Записать через временный файл (сбой посреди записи не портит прежний файл); false — содержимое то же, не писали.</summary>
    public static bool WriteIfChanged(string path, string text)
    {
        if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == text) return false;
        SafeWrite(path, text);
        return true;
    }

    public static void SafeWrite(string path, string text)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }
}
