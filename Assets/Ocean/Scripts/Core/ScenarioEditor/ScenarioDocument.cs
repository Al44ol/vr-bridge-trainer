// ============================================================
// ScenarioDocument.cs — Сценарий, открытый в редакторе (запрос 21.4а)
//
// Как AreaDocument, но для сценария: сценарий + файл + «изменено» + история отмены.
//   • New — «с нуля» на выбранной акватории (встроенной или своей): старт в середине района, маршрута нет;
//     BasedOn — «на основе» встроенного (или любого) сценария: копия через файл (ScenarioFile), новый
//     идентификатор «МОЙ-NN» и название «… (копия)»; встроенный ассет не меняется никогда;
//     Open — файл из «Мои сценарии».
//   • Правка: изменить Scenario → Commit() (снимок — текст файла сценария, своя акватория вписана в него);
//     Undo/Redo — снимки.
//   • Save — «Мои сценарии\<id> <название>.json» через временный файл; прежний вариант — в
//     «История\Сценарии\<имя>\<дата_время>.json» (последние HistoryKeep). Идентификатор встроенного
//     сценария занимать нельзя; занятое имя файла — NameTaken («Заменить / Отмена»).
//   • Черновик — «Черновики\Сценарий.json» раз в DraftPeriodS при изменениях.
// Тип своего судна — два варианта: модель 45 м и свой буксир (катер — переключатель тренажёра, вариант «-FB»).
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class ScenarioDocument
{
    public const string ScenarioDraftFile = "Сценарий.json";
    public const string IdPrefix = "МОЙ-";

    public ScenarioDefinition Scenario { get; private set; }
    /// <summary>Файл в «Мои сценарии»; пусто — не сохранён.</summary>
    public string FilePath { get; private set; } = "";
    /// <summary>Откуда открыт: «новый», «на основе «B2-01 …»», имя файла.</summary>
    public string Origin { get; private set; } = "";
    public bool Dirty { get; private set; }
    public readonly EditHistory History = new EditHistory();
    public DateTime? SavedAt { get; private set; }
    public string LastMessage { get; private set; } = "";

    private readonly ScenarioCatalog catalog;
    private readonly IList<WaterArea> folderAreas;

    private ScenarioDocument(ScenarioDefinition s, string origin, ScenarioCatalog catalog, IList<WaterArea> folderAreas)
    {
        Scenario = s;
        Origin = origin;
        this.catalog = catalog;
        this.folderAreas = folderAreas;
        History.Reset(Snapshot());
    }

    // ─────────── Создание ───────────
    /// <summary>Следующий свободный идентификатор «МОЙ-01», «МОЙ-02»… (taken — встроенные и свои).</summary>
    public static string NextId(IEnumerable<string> taken)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (taken != null) foreach (string t in taken) if (!string.IsNullOrEmpty(t)) used.Add(t.Trim());
        for (int i = 1; ; i++)
        {
            string id = IdPrefix + i.ToString("00");
            if (!used.Contains(id)) return id;
        }
    }

    /// <summary>Новый сценарий на акватории area: старт в середине района (курс 000°, 10 уз, «Средний вперёд»), маршрута нет.</summary>
    public static ScenarioDocument New(WaterArea area, ShipParameters ownShip, string id, string title,
                                       ScenarioCatalog catalog, IList<WaterArea> folderAreas)
    {
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        s.name = id;
        s.id = id;
        s.title = string.IsNullOrWhiteSpace(title) ? "Новый сценарий" : title.Trim();
        s.briefing = "";
        s.waterArea = area;
        s.shipParameters = ownShip;
        s.route = new List<RouteWaypoint>();
        if (area != null)
            s.initial.position = new WorldPosition(0.5 * (area.boundsMin.East + area.boundsMax.East),
                                                   0.5 * (area.boundsMin.North + area.boundsMax.North));
        s.initial.headingDeg = 0f;
        s.initial.speedKnots = 10f;
        s.initial.telegraph = TelegraphOrder.HalfAhead;
        s.assessment = catalog?.Find(typeof(AssessmentCriteria), "Assessment_Default") as AssessmentCriteria;
        return new ScenarioDocument(s, "новый", catalog, folderAreas) { Dirty = true };
    }

    /// <summary>Копия сценария source под новым идентификатором (исходный не меняется).</summary>
    public static ScenarioDocument BasedOn(ScenarioDefinition source, string id, ScenarioCatalog catalog, IList<WaterArea> folderAreas)
    {
        ScenarioLoadResult r = ScenarioFile.Read(ScenarioFile.Write(source, catalog), catalog, folderAreas, id);
        ScenarioDefinition s = r.Scenario;
        if (s == null) throw new InvalidOperationException("копия не получилась: " + string.Join("; ", r.Errors));
        s.name = id;
        s.id = id;
        s.title = (source.title ?? "Сценарий") + " (копия)";
        s.demoVersion = 0;   // версия встроенного демо-сценария к копии не относится
        return new ScenarioDocument(s, $"на основе «{source.id} {source.title}»", catalog, folderAreas) { Dirty = true };
    }

    /// <summary>Открыть файл сценария; null — ошибки (в errors).</summary>
    public static ScenarioDocument Open(string path, ScenarioCatalog catalog, IList<WaterArea> folderAreas, List<string> errors)
    {
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); }
        catch (Exception e) { errors?.Add($"{Path.GetFileName(path)}: не читается — {e.Message}"); return null; }
        ScenarioLoadResult r = ScenarioFile.Read(text, catalog, folderAreas, Path.GetFileNameWithoutExtension(path));
        if (!r.Ok) { errors?.AddRange(r.Errors); return null; }
        return new ScenarioDocument(r.Scenario, Path.GetFileName(path), catalog, folderAreas) { FilePath = path };
    }

    // ─────────── Правка, отмена ───────────
    public string Snapshot() => ScenarioFile.Write(Scenario, catalog);

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
        ScenarioLoadResult r = ScenarioFile.Read(state, catalog, folderAreas, Scenario.name);
        if (r.Scenario == null) return false;
        Scenario = r.Scenario;
        Dirty = true;
        return true;
    }

    /// <summary>Своё судно: модель 45 м или свой буксир (буксир — только в модели 3 СС).</summary>
    public void SetOwnShip(ScenarioShipType type, ShipParameters ship)
    {
        Scenario.ownShipType = type;
        if (ship != null) Scenario.shipParameters = ship;
        if (type == ScenarioShipType.Tug) Scenario.motionModel = ScenarioMotionModel.Manoeuvring3Dof;
        Commit();
    }

    // ─────────── Сохранение ───────────
    public static string ScenariosFolder(string root) => Path.Combine(root, ScenarioFolder.ScenariosDir);
    public string FileName => ScenarioFolder.SafeName($"{Scenario.id} {Scenario.title}") + ScenarioFile.Extension;
    public string TargetPath(string root) => Path.Combine(ScenariosFolder(root), FileName);

    /// <summary>Сохранить в «Мои сценарии» под «id название». overwrite — заменить другой файл с тем же именем;
    /// keepPrevious («Сохранить как») — прежний файл документа остаётся, иначе при смене имени он уходит в историю.</summary>
    public AreaSaveStatus Save(string root, bool overwrite, DateTime now, bool keepPrevious = false)
    {
        string id = (Scenario.id ?? "").Trim();
        if (id.Length == 0) { LastMessage = "Не задан идентификатор сценария."; return AreaSaveStatus.Invalid; }
        if (catalog != null && catalog.Scenarios.Exists(x => x != null && string.Equals(x.id, id, StringComparison.OrdinalIgnoreCase)))
        {
            LastMessage = $"Идентификатор «{id}» — у встроенного сценария; встроенные не перезаписываются, задайте другой.";
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
            Directory.CreateDirectory(ScenariosFolder(root));
            if (File.Exists(target)) KeepHistory(root, target, now);
            ScenarioFolder.SafeWrite(target, Snapshot());
            if (FilePath.Length > 0 && !same && !keepPrevious && File.Exists(FilePath))
            {
                KeepHistory(root, FilePath, now);
                File.Delete(FilePath);
            }
            FilePath = target;
            Dirty = false;
            SavedAt = now;
            DeleteDraft(root);
            int n = Scenario.Validate().Count;
            LastMessage = $"Сохранено: {Path.Combine(ScenarioFolder.ScenariosDir, Path.GetFileName(target))}" +
                          (n > 0 ? $" (замечаний проверки: {n})" : "");
            return AreaSaveStatus.Saved;
        }
        catch (Exception e)
        {
            LastMessage = "Не сохранено: " + e.Message;
            return AreaSaveStatus.Failed;
        }
    }

    public static string KeepHistory(string root, string file, DateTime now) => AreaDocument.KeepHistoryIn(root, "Сценарии", file, now);

    // ─────────── Черновик ───────────
    public static string DraftPath(string root) => Path.Combine(root, AreaDocument.DraftsDir, ScenarioDraftFile);

    public void WriteDraft(string root)
    {
        JsonValue v = JsonValue.Parse(Snapshot());
        v.Add("draftOf", JsonValue.Of(FilePath));
        v.Add("draftOrigin", JsonValue.Of(Origin));
        Directory.CreateDirectory(Path.Combine(root, AreaDocument.DraftsDir));
        ScenarioFolder.SafeWrite(DraftPath(root), v + "\n");
    }

    public static ScenarioDocument ReadDraft(string root, ScenarioCatalog catalog, IList<WaterArea> folderAreas, out DateTime written)
    {
        written = DateTime.MinValue;
        string path = DraftPath(root);
        if (!File.Exists(path)) return null;
        try
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            JsonValue j = JsonValue.Parse(text);
            ScenarioLoadResult r = ScenarioFile.Read(text, catalog, folderAreas, "черновик");
            if (r.Scenario == null) return null;
            r.Scenario.name = r.Scenario.id;
            written = File.GetLastWriteTime(path);
            JsonValue of = j["draftOf"], origin = j["draftOrigin"];
            return new ScenarioDocument(r.Scenario, origin != null && origin.Kind == JsonKind.String ? origin.Text : "черновик", catalog, folderAreas)
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
