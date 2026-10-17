// ============================================================
// AreaDocument.cs — Акватория, открытая в редакторе (запрос 21.2а)
//
// Документ: акватория + файл (пусто — ещё не сохранена) + «изменено» + история отмены.
//   • New — пустая (район ±5 км, глубина 30 м); CopyOf — копия встроенной под новым именем
//     (встроенная не меняется никогда); Open — файл из «Мои акватории».
//   • Правка: изменить Area → Commit() (снимок в историю, «изменено»); Undo/Redo — снимки.
//   • Save — «Мои акватории\<имя>.json» через временный файл; прежний вариант этого файла — в
//     «История\Акватории\<имя>\<дата_время>.json» (последние HistoryKeep). Имя занято другим
//     файлом — NameTaken (спросить «Заменить / Копией»), занято встроенной — ошибка.
//   • Черновик — «Черновики\Акватория.json» раз в DraftPeriodS при изменениях: после сбоя
//     редактор предлагает восстановить. Сохранили — черновик удаляется.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public enum AreaSaveStatus { Saved, NameTaken, Invalid, Failed }

public sealed class AreaDocument
{
    public const string HistoryDir = "История";
    public const string DraftsDir = "Черновики";
    public const string AreaDraftFile = "Акватория.json";
    public const int HistoryKeep = 20;
    public const double DraftPeriodS = 120.0;

    public WaterArea Area { get; private set; }
    /// <summary>Файл в «Мои акватории»; пусто — не сохранена.</summary>
    public string FilePath { get; private set; } = "";
    /// <summary>Откуда открыта (для заголовка): «новая», «копия встроенной WaterArea_Demo», имя файла.</summary>
    public string Origin { get; private set; } = "";
    public bool Dirty { get; private set; }
    public readonly EditHistory History = new EditHistory();
    public DateTime? SavedAt { get; private set; }
    /// <summary>Сообщение последнего сохранения (ошибка или «сохранено …»).</summary>
    public string LastMessage { get; private set; } = "";

    private AreaDocument(WaterArea a, string origin)
    {
        Area = a;
        Origin = origin;
        History.Reset(Snapshot());
    }

    // ─────────── Создание ───────────
    public static AreaDocument New(string name = "Новая акватория")
    {
        var a = ScriptableObject.CreateInstance<WaterArea>();
        a.name = name;
        a.areaName = name;
        return new AreaDocument(a, "новая") { Dirty = true };
    }

    /// <summary>Копия встроенной (или любой) акватории под новым именем.</summary>
    public static AreaDocument CopyOf(WaterArea source, string newName)
    {
        ScenarioLoadResult r = ScenarioFile.ReadArea(ScenarioFile.WriteArea(source), newName);
        WaterArea a = r.Area;
        a.name = newName;
        return new AreaDocument(a, $"копия «{source.name}»") { Dirty = true };
    }

    /// <summary>Открыть файл акватории; null — ошибки (в errors).</summary>
    public static AreaDocument Open(string path, List<string> errors)
    {
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); }
        catch (Exception e) { errors?.Add($"{Path.GetFileName(path)}: не читается — {e.Message}"); return null; }
        ScenarioLoadResult r = ScenarioFile.ReadArea(text, Path.GetFileNameWithoutExtension(path));
        if (!r.Ok) { errors?.AddRange(r.Errors); return null; }
        return new AreaDocument(r.Area, Path.GetFileName(path)) { FilePath = path };
    }

    // ─────────── Правка, отмена ───────────
    public string Snapshot() => ScenarioFile.WriteArea(Area);

    /// <summary>Правка закончена: снимок в историю; true — что-то изменилось.</summary>
    public bool Commit()
    {
        if (!History.Push(Snapshot())) return false;
        Dirty = true;
        return true;
    }

    public bool Undo() => Restore(History.Undo());
    public bool Redo() => Restore(History.Redo());

    private bool Restore(string state)
    {
        if (state == null) return false;
        ScenarioLoadResult r = ScenarioFile.ReadArea(state, Area.name);
        if (r.Area == null) return false;
        Area = r.Area;
        Dirty = true;
        return true;
    }

    /// <summary>Переименовать (имя файла и акватории).</summary>
    public void Rename(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name == Area.name) return;
        Area.name = name;
        Area.areaName = name;   // одно имя: и ссылка из сценария, и подпись
        Commit();
    }

    // ─────────── Сохранение ───────────
    public static string AreasFolder(string root) => Path.Combine(root, ScenarioFolder.AreasDir);
    public string TargetPath(string root) => Path.Combine(AreasFolder(root), ScenarioFolder.SafeName(Area.name) + ScenarioFile.Extension);

    /// <summary>Сохранить в «Мои акватории» под именем акватории. overwrite — заменить чужой файл с тем же именем;
    /// keepPrevious («Сохранить как») — прежний файл документа остаётся, иначе при смене имени он уходит в историю.</summary>
    public AreaSaveStatus Save(string root, ScenarioCatalog catalog, bool overwrite, DateTime now, bool keepPrevious = false)
    {
        AreaCheckResult check = AreaChecks.Check(Area, catalog);
        if (string.IsNullOrWhiteSpace(Area.name) || catalog != null && catalog.Find(typeof(WaterArea), Area.name) != null)
        {
            LastMessage = check.Errors.Count > 0 ? check.Errors[0] : "Имя не подходит.";
            return AreaSaveStatus.Invalid;
        }
        string target = TargetPath(root);
        bool same = string.Equals(Path.GetFullPath(target), FilePath.Length > 0 ? Path.GetFullPath(FilePath) : "", StringComparison.OrdinalIgnoreCase);
        if (File.Exists(target) && !same && !overwrite)
        {
            LastMessage = $"Файл «{Path.GetFileName(target)}» уже есть.";
            return AreaSaveStatus.NameTaken;
        }
        try
        {
            Directory.CreateDirectory(AreasFolder(root));
            if (File.Exists(target)) KeepHistory(root, target, now);
            ScenarioFolder.SafeWrite(target, Snapshot());
            // Переименовали — прежний файл уходит в историю (а не остаётся вторым экземпляром)
            if (FilePath.Length > 0 && !same && !keepPrevious && File.Exists(FilePath))
            {
                KeepHistory(root, FilePath, now);
                File.Delete(FilePath);
            }
            FilePath = target;
            Dirty = false;
            SavedAt = now;
            DeleteDraft(root);
            LastMessage = $"Сохранено: {Path.Combine(ScenarioFolder.AreasDir, Path.GetFileName(target))}" +
                          (check.Errors.Count + check.Warnings.Count > 0 ? $" (замечаний проверки: {check.Errors.Count + check.Warnings.Count})" : "");
            return AreaSaveStatus.Saved;
        }
        catch (Exception e)
        {
            LastMessage = "Не сохранено: " + e.Message;
            return AreaSaveStatus.Failed;
        }
    }

    /// <summary>Копия файла в «История\Акватории\&lt;имя&gt;\&lt;дата_время&gt;.json», последних HistoryKeep.</summary>
    public static string KeepHistory(string root, string file, DateTime now) => KeepHistoryIn(root, "Акватории", file, now);

    /// <summary>21.4а: то же для любого раздела истории («Акватории», «Сценарии»).</summary>
    public static string KeepHistoryIn(string root, string section, string file, DateTime now)
    {
        string dir = Path.Combine(root, HistoryDir, section, Path.GetFileNameWithoutExtension(file));
        Directory.CreateDirectory(dir);
        string copy = Path.Combine(dir, now.ToString("yyyyMMdd_HHmmss") + ScenarioFile.Extension);
        for (int i = 2; File.Exists(copy); i++) copy = Path.Combine(dir, $"{now:yyyyMMdd_HHmmss}_{i}{ScenarioFile.Extension}");
        File.Copy(file, copy);
        var all = new List<string>(Directory.GetFiles(dir, "*" + ScenarioFile.Extension));
        all.Sort(StringComparer.Ordinal);
        for (int i = 0; i + HistoryKeep < all.Count; i++) File.Delete(all[i]);
        return copy;
    }

    // ─────────── Черновик ───────────
    public static string DraftPath(string root) => Path.Combine(root, DraftsDir, AreaDraftFile);

    /// <summary>Записать черновик (текст файла акватории + откуда открыта).</summary>
    public void WriteDraft(string root)
    {
        JsonValue v = JsonValue.Parse(Snapshot());
        v.Add("draftOf", JsonValue.Of(FilePath));
        v.Add("draftOrigin", JsonValue.Of(Origin));
        Directory.CreateDirectory(Path.Combine(root, DraftsDir));
        ScenarioFolder.SafeWrite(DraftPath(root), v + "\n");
    }

    /// <summary>Черновик, если есть (после сбоя); null — нет или не читается.</summary>
    public static AreaDocument ReadDraft(string root, out DateTime written)
    {
        written = DateTime.MinValue;
        string path = DraftPath(root);
        if (!File.Exists(path)) return null;
        try
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            JsonValue root2 = JsonValue.Parse(text);
            ScenarioLoadResult r = ScenarioFile.ReadArea(text, "черновик");
            if (r.Area == null) return null;
            written = File.GetLastWriteTime(path);
            JsonValue of = root2["draftOf"], origin = root2["draftOrigin"];
            return new AreaDocument(r.Area, origin != null && origin.Kind == JsonKind.String ? origin.Text : "черновик")
            {
                FilePath = of != null && of.Kind == JsonKind.String ? of.Text : "",
                Dirty = true
            };
        }
        catch (Exception) { return null; }
    }

    public static void DeleteDraft(string root)
    {
        string path = DraftPath(root);
        if (File.Exists(path)) File.Delete(path);
    }
}
