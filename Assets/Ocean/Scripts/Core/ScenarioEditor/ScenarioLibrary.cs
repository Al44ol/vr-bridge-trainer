// ============================================================
// ScenarioLibrary.cs — Библиотека своих сценариев и акваторий (запрос 21.6в)
//
// Работает с папками ScenarioFolder (корень — Документы\VRBridgeTrainer) и с флешкой; встроенные сценарии
// и акватории (ассеты) не трогает никогда.
//   • Scan — свои файлы «Мои сценарии» и «Мои акватории» (тем же ScenarioFolder.Load, что и тренажёр), в том числе
//     с ошибками (их можно удалить или откатить на прежнюю версию); у акватории — сколько своих сценариев на ней.
//   • Copy — копия: сценарий с новым идентификатором «МОЙ-NN» и названием «… (копия)», акватория — «… (копия)».
//   • RenameScenario / RenameArea — новый id и название / имя: прежний вариант — в «История», папка истории
//     переезжает под новое имя. Сценарии на переименованной акватории не ломаются: её копия вписана в их файлы.
//   • Trash — не удаление, а перенос в «Корзина\Сценарии|Акватории\<имя>~<дата_время>.json»; Restore — на прежнее
//     место (занято — NameTaken: «Заменить» отправляет занявший файл в историю, или RestoreAsCopy); EmptyTrash —
//     единственное настоящее удаление (в редакторе — с подтверждением).
//   • Versions / RestoreVersion — версии из «История\…\<имя>\»; восстановленная версия встаёт на место файла
//     с его идентификатором и названием (имя файла не меняется), текущий вариант уходит в историю.
//   • Флешка: RemovableDrives (съёмные диски), Export — копии в «<диск>\VRBridgeTrainer\Мои сценарии|Мои акватории»
//     (та же раскладка, что на станции); ImportScan + Import — файлы из корня флешки и из этих папок: проверка
//     перед копированием, конфликты — «Заменить / Копией / Пропустить», id встроенного — только копией.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public enum LibraryKind { Scenario, Area }
public enum LibraryStatus { Done, NameTaken, Invalid, Failed }
public enum ImportConflict { None, FileExists, IdTaken, BuiltInId, AreaNameTaken }
public enum ImportMode { Normal, Replace, Copy }

/// <summary>Свой файл сценария или акватории.</summary>
public sealed class LibraryEntry
{
    public LibraryKind Kind;
    public string FilePath = "";
    public string Id = "", Title = "", AreaName = "";
    public DateTime Modified;
    public long Bytes;
    /// <summary>Загруженный сценарий / акватория; null — файл с ошибками.</summary>
    public ScenarioDefinition Scenario;
    public WaterArea Area;
    public readonly List<string> Errors = new List<string>();
    public readonly List<string> Warnings = new List<string>();
    /// <summary>Акватория: своих сценариев на ней (по имени).</summary>
    public int UsedBy;
    public bool Ok => Errors.Count == 0 && (Scenario != null || Area != null);
    public string FileName => Path.GetFileName(FilePath);
    /// <summary>«МОЙ-02 Проход…» / «Бухта»; у файла с ошибками — имя файла.</summary>
    public string Name => !Ok ? Path.GetFileNameWithoutExtension(FilePath) : Kind == LibraryKind.Scenario ? $"{Id} {Title}" : AreaName;
}

public sealed class LibraryScan
{
    public readonly List<LibraryEntry> Scenarios = new List<LibraryEntry>();
    public readonly List<LibraryEntry> Areas = new List<LibraryEntry>();
    public List<LibraryEntry> Of(LibraryKind k) => k == LibraryKind.Scenario ? Scenarios : Areas;

    /// <summary>Загруженные свои акватории (для чтения сценариев).</summary>
    public List<WaterArea> FolderAreas()
    {
        var list = new List<WaterArea>();
        foreach (LibraryEntry e in Areas) if (e.Area != null) list.Add(e.Area);
        return list;
    }
}

public sealed class TrashEntry
{
    public LibraryKind Kind;
    public string FilePath = "";
    /// <summary>Имя файла на прежнем месте («МОЙ-02 Проход….json»).</summary>
    public string OriginalName = "";
    public DateTime DeletedAt;
    public string Name => Path.GetFileNameWithoutExtension(OriginalName);
}

public sealed class HistoryVersion
{
    public string FilePath = "";
    public DateTime When;
    public long Bytes;
}

public sealed class LibraryResult
{
    public LibraryStatus Status;
    /// <summary>Файл, который получился (новое имя, копия, восстановленный).</summary>
    public string Path = "";
    public string Message = "";
    public bool Done => Status == LibraryStatus.Done;
    public static LibraryResult Of(LibraryStatus s, string message, string path = "") => new LibraryResult { Status = s, Message = message, Path = path ?? "" };
}

public sealed class FlashDrive
{
    public string Root = "";
    public string Label = "";
    public long FreeBytes;
    public string Title => $"{Root}{(string.IsNullOrEmpty(Label) ? "" : $" «{Label}»")} · свободно {ScenarioLibrary.SizeText(FreeBytes)}";
}

/// <summary>Файл, найденный на флешке.</summary>
public sealed class ImportItem
{
    public string SourcePath = "";
    public LibraryKind Kind;
    public string Name = "", Id = "", AreaName = "";
    public string TargetPath = "";
    public ImportConflict Conflict;
    /// <summary>На станции уже есть точно такой же файл — импортировать нечего.</summary>
    public bool Same;
    public readonly List<string> Errors = new List<string>();
    public bool Ok => Errors.Count == 0;
    public string FileName => Path.GetFileName(SourcePath);
}

public sealed class ImportScanResult
{
    public string Source = "";
    public string Error = "";
    public readonly List<ImportItem> Items = new List<ImportItem>();
}

public static class ScenarioLibrary
{
    public const string TrashDir = "Корзина";
    /// <summary>Папка на флешке: «&lt;диск&gt;\VRBridgeTrainer\Мои сценарии|Мои акватории».</summary>
    public const string FlashFolder = "VRBridgeTrainer";
    public const string CopySuffix = " (копия)";
    private const string StampFormat = "yyyyMMdd_HHmmss";

    public static string Section(LibraryKind k) => k == LibraryKind.Scenario ? "Сценарии" : "Акватории";
    public static string Folder(string root, LibraryKind k) => Path.Combine(root, k == LibraryKind.Scenario ? ScenarioFolder.ScenariosDir : ScenarioFolder.AreasDir);
    public static string TrashFolder(string root, LibraryKind k) => Path.Combine(root, TrashDir, Section(k));
    public static string HistoryFolder(string root, LibraryKind k, string file) =>
        Path.Combine(root, AreaDocument.HistoryDir, Section(k), Path.GetFileNameWithoutExtension(file));
    public static string KindTitle(LibraryKind k) => k == LibraryKind.Scenario ? "сценарий" : "акватория";

    // ─────────── Список ───────────
    /// <summary>Свои сценарии и акватории (по имени файла), в том числе с ошибками.</summary>
    public static LibraryScan Scan(string root, ScenarioCatalog catalog)
    {
        var scan = new LibraryScan();
        ScenarioLibraryReport rep = ScenarioFolder.Load(root, catalog ?? new ScenarioCatalog());
        foreach (ScenarioLoadResult r in rep.Files)
        {
            bool area = r.File.StartsWith(ScenarioFolder.AreasDir + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            var e = new LibraryEntry { Kind = area ? LibraryKind.Area : LibraryKind.Scenario, FilePath = Path.Combine(root, r.File) };
            e.Errors.AddRange(r.Errors);
            e.Warnings.AddRange(r.Warnings);
            if (area && r.Area != null) { e.Area = r.Area; e.AreaName = r.Area.name; }
            if (!area && r.Scenario != null)
            {
                e.Scenario = r.Scenario;
                e.Id = r.Scenario.id ?? "";
                e.Title = r.Scenario.title ?? "";
                e.AreaName = r.Scenario.waterArea != null ? r.Scenario.waterArea.name : "";
            }
            try
            {
                var fi = new FileInfo(e.FilePath);
                e.Modified = fi.LastWriteTime;
                e.Bytes = fi.Length;
            }
            catch (Exception) { }
            scan.Of(e.Kind).Add(e);
        }
        foreach (LibraryEntry a in scan.Areas)
            if (a.Area != null) a.UsedBy = scan.Scenarios.FindAll(s => s.Scenario != null && s.AreaName == a.AreaName).Count;
        return scan;
    }

    private static HashSet<string> TakenIds(ScenarioCatalog catalog, LibraryScan scan)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (catalog != null) foreach (ScenarioDefinition s in catalog.Scenarios) if (s != null && !string.IsNullOrEmpty(s.id)) ids.Add(s.id);
        if (scan != null) foreach (LibraryEntry e in scan.Scenarios) if (e.Id.Length > 0) ids.Add(e.Id);
        return ids;
    }

    private static bool IsBuiltInId(ScenarioCatalog catalog, string id) =>
        catalog != null && catalog.Scenarios.Exists(x => x != null && string.Equals(x.id, id, StringComparison.OrdinalIgnoreCase));

    private static bool AreaNameTaken(ScenarioCatalog catalog, LibraryScan scan, string name, LibraryEntry except = null) =>
        catalog != null && catalog.Find(typeof(WaterArea), name) != null ||
        scan != null && scan.Areas.Exists(a => a != except && a.Area != null && a.AreaName == name);

    /// <summary>Свободное имя акватории: «Бухта (копия)», «Бухта (копия) (2)»…</summary>
    public static string UniqueAreaName(string name, ScenarioCatalog catalog, LibraryScan scan, string root)
    {
        string baseName = string.IsNullOrWhiteSpace(name) ? "Акватория" : name.Trim();
        for (int i = 1; ; i++)
        {
            string n = i == 1 ? baseName : $"{baseName} ({i})";
            if (!AreaNameTaken(catalog, scan, n) && !File.Exists(Path.Combine(Folder(root, LibraryKind.Area), ScenarioFolder.SafeName(n) + ScenarioFile.Extension)))
                return n;
        }
    }

    // Свободное имя файла в папке (на случай совпадения: «… (2).json»)
    private static string FreePath(string dir, string name)
    {
        string p = Path.Combine(dir, name + ScenarioFile.Extension);
        for (int i = 2; File.Exists(p); i++) p = Path.Combine(dir, $"{name} ({i}){ScenarioFile.Extension}");
        return p;
    }

    private static string ScenarioFileName(ScenarioDefinition s) => ScenarioFolder.SafeName($"{s.id} {s.title}");

    private static bool SamePath(string a, string b) =>
        a.Length > 0 && b.Length > 0 && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // ─────────── Копия ───────────
    /// <summary>Копия файла source (своего, из корзины или с флешки) под новым id / именем. suffix — к названию (« (копия)» или пусто).</summary>
    public static LibraryResult Copy(string root, ScenarioCatalog catalog, LibraryScan scan, LibraryKind kind, string source, string suffix = CopySuffix)
    {
        string text;
        try { text = File.ReadAllText(source, Encoding.UTF8); }
        catch (Exception e) { return LibraryResult.Of(LibraryStatus.Failed, "Не читается: " + e.Message); }
        try
        {
            string dir = Folder(root, kind);
            Directory.CreateDirectory(dir);
            if (kind == LibraryKind.Scenario)
            {
                ScenarioLoadResult r = ScenarioFile.Read(text, catalog, scan?.FolderAreas(), Path.GetFileNameWithoutExtension(source));
                if (r.Scenario == null) return LibraryResult.Of(LibraryStatus.Invalid, "Файл с ошибками — копию не сделать: " + string.Join("; ", r.Errors));
                ScenarioDefinition s = r.Scenario;
                string id = ScenarioDocument.NextId(TakenIds(catalog, scan));
                s.id = id;
                s.name = id;
                s.title = (string.IsNullOrWhiteSpace(s.title) ? "Сценарий" : s.title) + (suffix ?? "");
                s.demoVersion = 0;
                string path = FreePath(dir, ScenarioFileName(s));
                ScenarioFolder.SafeWrite(path, ScenarioFile.Write(s, catalog));
                return LibraryResult.Of(LibraryStatus.Done, $"Копия: «{s.id} {s.title}» — {Path.Combine(ScenarioFolder.ScenariosDir, Path.GetFileName(path))}", path);
            }
            ScenarioLoadResult ra = ScenarioFile.ReadArea(text, Path.GetFileNameWithoutExtension(source));
            if (ra.Area == null) return LibraryResult.Of(LibraryStatus.Invalid, "Файл с ошибками — копию не сделать: " + string.Join("; ", ra.Errors));
            string n = UniqueAreaName(ra.Area.name + (suffix ?? ""), catalog, scan, root);
            ra.Area.name = n;
            ra.Area.areaName = n;
            string apath = FreePath(dir, ScenarioFolder.SafeName(n));
            ScenarioFolder.SafeWrite(apath, ScenarioFile.WriteArea(ra.Area));
            return LibraryResult.Of(LibraryStatus.Done, $"Копия: акватория «{n}» — {Path.Combine(ScenarioFolder.AreasDir, Path.GetFileName(apath))}", apath);
        }
        catch (Exception e) { return LibraryResult.Of(LibraryStatus.Failed, "Копия не записана: " + e.Message); }
    }

    // ─────────── Переименование ───────────
    /// <summary>Сценарий: новый идентификатор и название (файл «id название.json»). overwrite — заменить занявший имя файл.</summary>
    public static LibraryResult RenameScenario(string root, ScenarioCatalog catalog, LibraryScan scan, LibraryEntry e, string newId, string newTitle,
                                               bool overwrite, DateTime now)
    {
        if (e == null || e.Scenario == null) return LibraryResult.Of(LibraryStatus.Invalid, "Файл с ошибками — переименовать нельзя (откатите версию или исправьте файл).");
        string id = (newId ?? "").Trim(), title = (newTitle ?? "").Trim();
        if (id.Length == 0) return LibraryResult.Of(LibraryStatus.Invalid, "Не задан идентификатор.");
        if (title.Length == 0) title = e.Title;
        if (IsBuiltInId(catalog, id)) return LibraryResult.Of(LibraryStatus.Invalid, $"Идентификатор «{id}» — у встроенного сценария; задайте другой.");
        LibraryEntry other = scan?.Scenarios.Find(x => x != e && x.Scenario != null && string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (other != null) return LibraryResult.Of(LibraryStatus.Invalid, $"Идентификатор «{id}» уже у файла «{other.FileName}».");
        ScenarioDefinition s = e.Scenario;
        string oldId = s.id, oldName = s.name, oldTitle = s.title;
        s.id = id;
        s.name = id;
        s.title = title;
        string target = Path.Combine(Folder(root, LibraryKind.Scenario), ScenarioFileName(s) + ScenarioFile.Extension);
        string text = ScenarioFile.Write(s, catalog);
        s.id = oldId; s.name = oldName; s.title = oldTitle;   // объект списка — как в файле, пока не записано
        return Replace(root, LibraryKind.Scenario, e.FilePath, target, text, overwrite, now, $"Переименовано: «{id} {title}»");
    }

    /// <summary>Акватория: новое имя (и файла, и ссылки из сценариев). Свои сценарии на ней не меняются — копия вписана в их файлы.</summary>
    public static LibraryResult RenameArea(string root, ScenarioCatalog catalog, LibraryScan scan, LibraryEntry e, string newName, bool overwrite, DateTime now)
    {
        if (e == null || e.Area == null) return LibraryResult.Of(LibraryStatus.Invalid, "Файл с ошибками — переименовать нельзя (откатите версию или исправьте файл).");
        string n = (newName ?? "").Trim();
        if (n.Length == 0) return LibraryResult.Of(LibraryStatus.Invalid, "Не задано имя.");
        if (AreaNameTaken(catalog, scan, n, e)) return LibraryResult.Of(LibraryStatus.Invalid, $"Акватория «{n}» уже есть (встроенная или свой файл).");
        WaterArea a = e.Area;
        string oldName = a.name, oldAreaName = a.areaName;
        a.name = n;
        a.areaName = n;
        string text = ScenarioFile.WriteArea(a);
        a.name = oldName; a.areaName = oldAreaName;
        string target = Path.Combine(Folder(root, LibraryKind.Area), ScenarioFolder.SafeName(n) + ScenarioFile.Extension);
        string note = e.UsedBy > 0 ? $" Сценариев на ней: {e.UsedBy} — у них копия акватории вписана в файл, они не изменятся (прежнее имя)." : "";
        LibraryResult r = Replace(root, LibraryKind.Area, e.FilePath, target, text, overwrite, now, $"Переименовано: акватория «{n}».");
        if (r.Done) r.Message += note;
        return r;
    }

    // Записать text в target вместо файла from: прежние варианты — в историю, история переезжает под новое имя
    private static LibraryResult Replace(string root, LibraryKind kind, string from, string target, string text, bool overwrite, DateTime now, string done)
    {
        bool same = SamePath(from, target);
        if (File.Exists(target) && !same && !overwrite)
            return LibraryResult.Of(LibraryStatus.NameTaken, $"Файл «{Path.GetFileName(target)}» уже есть.");
        try
        {
            if (File.Exists(target) && !same) KeepHistory(root, kind, target, now);
            if (File.Exists(from)) KeepHistory(root, kind, from, now);
            ScenarioFolder.SafeWrite(target, text);
            if (!same)
            {
                if (File.Exists(from)) File.Delete(from);
                MoveHistory(root, kind, from, target);
            }
            return LibraryResult.Of(LibraryStatus.Done, $"{done} — {Path.GetFileName(target)}", target);
        }
        catch (Exception e) { return LibraryResult.Of(LibraryStatus.Failed, "Не переименовано: " + e.Message); }
    }

    private static string KeepHistory(string root, LibraryKind kind, string file, DateTime now) =>
        AreaDocument.KeepHistoryIn(root, Section(kind), file, now);

    /// <summary>Папку истории — под новое имя файла (слить, если она уже есть).</summary>
    public static void MoveHistory(string root, LibraryKind kind, string oldFile, string newFile)
    {
        string src = HistoryFolder(root, kind, oldFile), dst = HistoryFolder(root, kind, newFile);
        if (!Directory.Exists(src) || string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) return;
        if (!Directory.Exists(dst)) { Directory.CreateDirectory(Path.GetDirectoryName(dst)); Directory.Move(src, dst); return; }
        foreach (string f in Directory.GetFiles(src))
        {
            string name = Path.GetFileNameWithoutExtension(f), p = Path.Combine(dst, Path.GetFileName(f));
            for (int i = 2; File.Exists(p); i++) p = Path.Combine(dst, $"{name}_{i}{Path.GetExtension(f)}");
            File.Move(f, p);
        }
        Directory.Delete(src, true);
    }

    // ─────────── Корзина ───────────
    /// <summary>В корзину (не удаление): «Корзина\Сценарии\&lt;имя&gt;~&lt;дата_время&gt;.json».</summary>
    public static LibraryResult Trash(string root, LibraryEntry e, DateTime now)
    {
        if (e == null || !File.Exists(e.FilePath)) return LibraryResult.Of(LibraryStatus.Failed, "Файла уже нет.");
        try
        {
            string dir = TrashFolder(root, e.Kind);
            Directory.CreateDirectory(dir);
            string name = Path.GetFileNameWithoutExtension(e.FilePath) + "~" + now.ToString(StampFormat, CultureInfo.InvariantCulture);
            string p = Path.Combine(dir, name + ScenarioFile.Extension);
            for (int i = 2; File.Exists(p); i++) p = Path.Combine(dir, $"{name}_{i}{ScenarioFile.Extension}");
            File.Move(e.FilePath, p);
            return LibraryResult.Of(LibraryStatus.Done, $"В корзине: {e.Name} ({Path.Combine(TrashDir, Section(e.Kind))})", p);
        }
        catch (Exception x) { return LibraryResult.Of(LibraryStatus.Failed, "Не перенесено в корзину: " + x.Message); }
    }

    /// <summary>Содержимое корзины, последние удалённые — первыми.</summary>
    public static List<TrashEntry> TrashList(string root)
    {
        var list = new List<TrashEntry>();
        foreach (LibraryKind k in new[] { LibraryKind.Scenario, LibraryKind.Area })
        {
            string dir = TrashFolder(root, k);
            if (!Directory.Exists(dir)) continue;
            foreach (string f in Directory.GetFiles(dir, "*" + ScenarioFile.Extension))
            {
                if (!string.Equals(Path.GetExtension(f), ScenarioFile.Extension, StringComparison.OrdinalIgnoreCase)) continue;
                string n = Path.GetFileNameWithoutExtension(f);
                var t = new TrashEntry { Kind = k, FilePath = f, OriginalName = Path.GetFileName(f), DeletedAt = File.GetLastWriteTime(f) };
                int i = n.LastIndexOf('~');
                if (i > 0)
                {
                    string stamp = n.Substring(i + 1);
                    int u = stamp.IndexOf('_', 9);   // «_2» — второй файл в ту же секунду
                    if (u > 0) stamp = stamp.Substring(0, u);
                    if (DateTime.TryParseExact(stamp, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when))
                    {
                        t.OriginalName = n.Substring(0, i) + ScenarioFile.Extension;
                        t.DeletedAt = when;
                    }
                }
                list.Add(t);
            }
        }
        list.Sort((a, b) => b.DeletedAt.CompareTo(a.DeletedAt));
        return list;
    }

    /// <summary>Вернуть из корзины на прежнее место; занято — NameTaken, overwrite — занявший файл уходит в историю.</summary>
    public static LibraryResult Restore(string root, TrashEntry t, bool overwrite, DateTime now)
    {
        if (t == null || !File.Exists(t.FilePath)) return LibraryResult.Of(LibraryStatus.Failed, "Файла в корзине уже нет.");
        string dir = Folder(root, t.Kind), target = Path.Combine(dir, t.OriginalName);
        if (File.Exists(target) && !overwrite) return LibraryResult.Of(LibraryStatus.NameTaken, $"Файл «{t.OriginalName}» уже есть.");
        try
        {
            Directory.CreateDirectory(dir);
            if (File.Exists(target))
            {
                KeepHistory(root, t.Kind, target, now);
                ScenarioFolder.SafeWrite(target, File.ReadAllText(t.FilePath, Encoding.UTF8));
                File.Delete(t.FilePath);
            }
            else File.Move(t.FilePath, target);
            return LibraryResult.Of(LibraryStatus.Done, $"Восстановлено: {t.Name}", target);
        }
        catch (Exception e) { return LibraryResult.Of(LibraryStatus.Failed, "Не восстановлено: " + e.Message); }
    }

    /// <summary>Вернуть из корзины копией (новый id / имя), если прежнее место занято.</summary>
    public static LibraryResult RestoreAsCopy(string root, ScenarioCatalog catalog, LibraryScan scan, TrashEntry t)
    {
        if (t == null || !File.Exists(t.FilePath)) return LibraryResult.Of(LibraryStatus.Failed, "Файла в корзине уже нет.");
        LibraryResult r = Copy(root, catalog, scan, t.Kind, t.FilePath, "");
        if (r.Done)
        {
            try { File.Delete(t.FilePath); } catch (Exception) { }
            r.Message = "Восстановлено копией. " + r.Message;
        }
        return r;
    }

    /// <summary>Очистить корзину — файлы удаляются насовсем; вернуть число удалённых.</summary>
    public static int EmptyTrash(string root)
    {
        int n = 0;
        foreach (LibraryKind k in new[] { LibraryKind.Scenario, LibraryKind.Area })
        {
            string dir = TrashFolder(root, k);
            if (!Directory.Exists(dir)) continue;
            foreach (string f in Directory.GetFiles(dir)) { File.Delete(f); n++; }
        }
        return n;
    }

    // ─────────── История версий ───────────
    /// <summary>Прежние версии файла (последние — первыми).</summary>
    public static List<HistoryVersion> Versions(string root, LibraryKind kind, string file)
    {
        var list = new List<HistoryVersion>();
        string dir = HistoryFolder(root, kind, file);
        if (!Directory.Exists(dir)) return list;
        foreach (string f in Directory.GetFiles(dir, "*" + ScenarioFile.Extension))
        {
            string n = Path.GetFileNameWithoutExtension(f);
            var v = new HistoryVersion { FilePath = f, When = File.GetLastWriteTime(f) };
            if (n.Length >= 15 && DateTime.TryParseExact(n.Substring(0, 15), StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when))
                v.When = when;
            try { v.Bytes = new FileInfo(f).Length; } catch (Exception) { }
            list.Add(v);
        }
        list.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(b.FilePath), Path.GetFileName(a.FilePath)));
        return list;
    }

    /// <summary>Поставить версию на место файла (текущий вариант — в историю). Идентификатор и название / имя — текущие.</summary>
    public static LibraryResult RestoreVersion(string root, ScenarioCatalog catalog, LibraryScan scan, LibraryEntry e, HistoryVersion v, DateTime now)
    {
        if (e == null || v == null || !File.Exists(v.FilePath)) return LibraryResult.Of(LibraryStatus.Failed, "Версии уже нет.");
        string text;
        try { text = File.ReadAllText(v.FilePath, Encoding.UTF8); }   // прочитать до записи в историю (она обрезается до 20)
        catch (Exception x) { return LibraryResult.Of(LibraryStatus.Failed, "Версия не читается: " + x.Message); }
        if (e.Kind == LibraryKind.Scenario)
        {
            ScenarioLoadResult r = ScenarioFile.Read(text, catalog, scan?.FolderAreas(), Path.GetFileNameWithoutExtension(e.FilePath));
            if (r.Scenario == null) return LibraryResult.Of(LibraryStatus.Invalid, "Версия с ошибками: " + string.Join("; ", r.Errors));
            if (e.Scenario != null && (r.Scenario.id != e.Id || r.Scenario.title != e.Title))
            {
                r.Scenario.id = e.Id;
                r.Scenario.name = e.Id;
                r.Scenario.title = e.Title;
                text = ScenarioFile.Write(r.Scenario, catalog);
            }
        }
        else
        {
            ScenarioLoadResult r = ScenarioFile.ReadArea(text, Path.GetFileNameWithoutExtension(e.FilePath));
            if (r.Area == null) return LibraryResult.Of(LibraryStatus.Invalid, "Версия с ошибками: " + string.Join("; ", r.Errors));
            if (e.Area != null && r.Area.name != e.AreaName)
            {
                r.Area.name = e.AreaName;
                r.Area.areaName = e.AreaName;
                text = ScenarioFile.WriteArea(r.Area);
            }
        }
        try
        {
            if (File.Exists(e.FilePath)) KeepHistory(root, e.Kind, e.FilePath, now);
            ScenarioFolder.SafeWrite(e.FilePath, text);
            return LibraryResult.Of(LibraryStatus.Done, $"Восстановлена версия от {v.When:dd.MM.yyyy HH:mm:ss}: {e.Name} (прежний вариант — в «История»)", e.FilePath);
        }
        catch (Exception x) { return LibraryResult.Of(LibraryStatus.Failed, "Не восстановлено: " + x.Message); }
    }

    // ─────────── Флешка ───────────
    /// <summary>Съёмные диски, готовые к записи (флешки).</summary>
    public static List<FlashDrive> RemovableDrives()
    {
        var list = new List<FlashDrive>();
        try
        {
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Removable || !d.IsReady) continue;
                    list.Add(new FlashDrive { Root = d.RootDirectory.FullName, Label = d.VolumeLabel, FreeBytes = d.AvailableFreeSpace });
                }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
        return list;
    }

    public static string SizeText(long bytes) =>
        bytes >= 1L << 30 ? ScenarioText.Num(bytes / (double)(1L << 30), 1) + " ГБ"
        : bytes >= 1L << 20 ? ScenarioText.Num(bytes / (double)(1L << 20), 1) + " МБ"
        : ScenarioText.Num(Math.Max(1.0, bytes / 1024.0), 0) + " КБ";

    // Папка — сама станция (Документы\VRBridgeTrainer): туда не экспортировать и оттуда не импортировать
    private static bool IsStation(string folder, string root) =>
        SamePath(folder.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'));

    /// <summary>Записать копии файлов в «&lt;target&gt;\VRBridgeTrainer\Мои сценарии|Мои акватории».</summary>
    public static LibraryResult Export(string root, IEnumerable<LibraryEntry> entries, string target)
    {
        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target)) return LibraryResult.Of(LibraryStatus.Invalid, $"Нет папки «{target}».");
        string dst = Path.Combine(target, FlashFolder);
        if (IsStation(dst, root) || IsStation(target, root)) return LibraryResult.Of(LibraryStatus.Invalid, "Это папка самой станции — выберите флешку.");
        int written = 0, same = 0;
        var failed = new List<string>();
        foreach (LibraryEntry e in entries)
        {
            if (e == null) continue;
            try
            {
                string dir = Path.Combine(dst, e.Kind == LibraryKind.Scenario ? ScenarioFolder.ScenariosDir : ScenarioFolder.AreasDir);
                Directory.CreateDirectory(dir);
                if (ScenarioFolder.WriteIfChanged(Path.Combine(dir, e.FileName), File.ReadAllText(e.FilePath, Encoding.UTF8))) written++;
                else same++;
            }
            catch (Exception x) { failed.Add($"{e.FileName}: {x.Message}"); }
        }
        string msg = $"На флешку ({dst}): записано {written}" + (same > 0 ? $", уже такие же {same}" : "") +
                     (failed.Count > 0 ? $"; не записано {failed.Count}: " + string.Join("; ", failed) : "");
        return LibraryResult.Of(failed.Count > 0 && written + same == 0 ? LibraryStatus.Failed : LibraryStatus.Done, msg, dst);
    }

    /// <summary>Файлы тренажёра на флешке: корень, «VRBridgeTrainer\», «…\Мои сценарии», «…\Мои акватории» (не «Образцы»).</summary>
    public static ImportScanResult ImportScan(string source, string root, ScenarioCatalog catalog, LibraryScan local)
    {
        var res = new ImportScanResult { Source = source ?? "" };
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source)) { res.Error = $"Нет папки «{source}»."; return res; }
        if (IsStation(source, root) || IsStation(Path.Combine(source, FlashFolder), root)) { res.Error = "Это папка самой станции — выберите флешку."; return res; }
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dir in new[]
                 {
                     source, Path.Combine(source, FlashFolder),
                     Path.Combine(source, ScenarioFolder.AreasDir), Path.Combine(source, ScenarioFolder.ScenariosDir),
                     Path.Combine(source, FlashFolder, ScenarioFolder.AreasDir), Path.Combine(source, FlashFolder, ScenarioFolder.ScenariosDir)
                 })
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*" + ScenarioFile.Extension, SearchOption.TopDirectoryOnly))
                    if (string.Equals(Path.GetExtension(f), ScenarioFile.Extension, StringComparison.OrdinalIgnoreCase) && seen.Add(Path.GetFullPath(f))) files.Add(f);
            }
            catch (Exception) { }
        }

        // Сначала акватории (на них могут ссылаться сценарии с флешки), затем сценарии
        var texts = new Dictionary<string, string>();
        var srcAreas = new List<WaterArea>();
        var areaItems = new List<ImportItem>();
        var scenarioFiles = new List<string>();
        foreach (string f in files)
        {
            var item = new ImportItem { SourcePath = f };
            string text;
            try { text = File.ReadAllText(f, Encoding.UTF8); }
            catch (Exception e) { item.Errors.Add("не читается: " + e.Message); res.Items.Add(item); continue; }
            string format = FormatOf(text);
            if (format == ScenarioFile.ScenarioFormat) { texts[f] = text; scenarioFiles.Add(f); continue; }
            if (format != ScenarioFile.AreaFormat) { item.Errors.Add("не файл тренажёра (нет «format»: «VRBridgeTrainer.Scenario» / «…Area»)"); res.Items.Add(item); continue; }
            item.Kind = LibraryKind.Area;
            ScenarioLoadResult r = ScenarioFile.ReadArea(text, Path.GetFileNameWithoutExtension(f));
            item.Errors.AddRange(r.Errors);
            if (r.Area != null)
            {
                item.Name = item.AreaName = r.Area.name;
                item.TargetPath = Path.Combine(Folder(root, LibraryKind.Area), Path.GetFileName(f));
                LibraryEntry same = local?.Areas.Find(a => a.Area != null && a.AreaName == r.Area.name);
                if (catalog != null && catalog.Find(typeof(WaterArea), r.Area.name) != null) item.Conflict = ImportConflict.AreaNameTaken;
                else if (File.Exists(item.TargetPath)) item.Conflict = ImportConflict.FileExists;
                else if (same != null) item.Conflict = ImportConflict.AreaNameTaken;
                item.Same = File.Exists(item.TargetPath) && SameText(item.TargetPath, text);
                srcAreas.Add(r.Area);
            }
            areaItems.Add(item);
        }
        res.Items.AddRange(areaItems);
        var areas = new List<WaterArea>(local != null ? local.FolderAreas() : new List<WaterArea>());
        foreach (WaterArea a in srcAreas) if (!areas.Exists(x => x.name == a.name)) areas.Add(a);
        foreach (string f in scenarioFiles)
        {
            var item = new ImportItem { SourcePath = f, Kind = LibraryKind.Scenario };
            ScenarioLoadResult r = ScenarioFile.Read(texts[f], catalog, areas, Path.GetFileNameWithoutExtension(f));
            item.Errors.AddRange(r.Errors);
            if (r.Scenario != null)
            {
                item.Id = r.Scenario.id ?? "";
                item.Name = $"{item.Id} {r.Scenario.title}";
                item.AreaName = r.Scenario.waterArea != null ? r.Scenario.waterArea.name : "";
                item.TargetPath = Path.Combine(Folder(root, LibraryKind.Scenario), Path.GetFileName(f));
                if (IsBuiltInId(catalog, item.Id)) item.Conflict = ImportConflict.BuiltInId;
                else if (File.Exists(item.TargetPath)) item.Conflict = ImportConflict.FileExists;
                else if (local != null && local.Scenarios.Exists(x => x.Scenario != null && string.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase)))
                    item.Conflict = ImportConflict.IdTaken;
                item.Same = File.Exists(item.TargetPath) && SameText(item.TargetPath, texts[f]);
            }
            res.Items.Add(item);
        }
        return res;
    }

    private static string FormatOf(string text)
    {
        try
        {
            JsonValue root = JsonValue.Parse(text);
            JsonValue f = root.Kind == JsonKind.Object ? root["format"] : null;
            return f != null && f.Kind == JsonKind.String ? f.Text : "";
        }
        catch (FormatException) { return ""; }
    }

    private static bool SameText(string path, string text)
    {
        try { return File.ReadAllText(path, Encoding.UTF8) == text; }
        catch (Exception) { return false; }
    }

    /// <summary>Конфликт, при котором остаётся только копия (имя занято встроенным или другим файлом).</summary>
    public static bool CopyOnly(ImportConflict c) => c == ImportConflict.BuiltInId || c == ImportConflict.IdTaken || c == ImportConflict.AreaNameTaken;

    public static string ConflictText(ImportItem i)
    {
        if (!i.Ok) return "ошибки: " + string.Join("; ", i.Errors);
        if (i.Same) return "на станции уже есть такой же";
        switch (i.Conflict)
        {
            case ImportConflict.FileExists: return $"на станции уже есть файл «{Path.GetFileName(i.TargetPath)}»";
            case ImportConflict.IdTaken: return $"идентификатор «{i.Id}» уже у другого своего сценария";
            case ImportConflict.BuiltInId: return $"идентификатор «{i.Id}» — у встроенного сценария";
            case ImportConflict.AreaNameTaken: return $"акватория «{i.AreaName}» уже есть (встроенная или другой файл)";
            default: return "новый";
        }
    }

    /// <summary>Импортировать один файл. Normal — только без конфликта (иначе NameTaken); Replace — заменить файл с тем же
    /// именем (прежний — в историю); Copy — под новым id / именем.</summary>
    public static LibraryResult Import(string root, ScenarioCatalog catalog, LibraryScan local, ImportItem item, ImportMode mode, DateTime now)
    {
        if (item == null || !item.Ok) return LibraryResult.Of(LibraryStatus.Invalid, $"{item?.FileName}: с ошибками — не импортирован.");
        if (item.Same) return LibraryResult.Of(LibraryStatus.Done, $"{item.FileName}: уже есть такой же — пропущен.", item.TargetPath);
        if (mode == ImportMode.Copy || CopyOnly(item.Conflict) && mode == ImportMode.Replace)
        {
            LibraryResult c = Copy(root, catalog, local, item.Kind, item.SourcePath, "");
            if (c.Done) c.Message = $"{item.FileName}: импортирован копией. {c.Message}";
            return c;
        }
        if (item.Conflict != ImportConflict.None && mode == ImportMode.Normal)
            return LibraryResult.Of(LibraryStatus.NameTaken, $"{item.FileName}: {ConflictText(item)}.");
        try
        {
            string dir = Folder(root, item.Kind);
            Directory.CreateDirectory(dir);
            if (File.Exists(item.TargetPath)) KeepHistory(root, item.Kind, item.TargetPath, now);
            ScenarioFolder.SafeWrite(item.TargetPath, File.ReadAllText(item.SourcePath, Encoding.UTF8));
            return LibraryResult.Of(LibraryStatus.Done, $"{item.FileName}: импортирован{(mode == ImportMode.Replace ? " (заменён, прежний — в «История»)" : "")}.", item.TargetPath);
        }
        catch (Exception e) { return LibraryResult.Of(LibraryStatus.Failed, $"{item.FileName}: не импортирован — {e.Message}"); }
    }
}
