// ============================================================
// ScenarioFile.cs — Сценарий и акватория в файле JSON (запрос 21.1)
//
// Сценарий создаётся и переносится без Unity: один файл .json (флешкой между станциями).
//   {
//     "format": "VRBridgeTrainer.Scenario", "version": 1,
//     "scenario": { …все поля ScenarioDefinition, как в ассете (FieldJson)… },
//     "areas": [ { "name": "…", "area": { …поля WaterArea… } } ]   ← только если акватория своя (не встроенная)
//   }
// Ссылки на ассеты — именами встроенных: судно ("ShipParameters_Mariner"), набор оценки ("Assessment_TugWork"),
// акватория ("WaterArea_Demo"). Своя акватория вписана в файл сценария — файл самодостаточен; кроме того,
// акватория может лежать отдельным файлом (формат "VRBridgeTrainer.Area") в папке «Мои акватории».
// Встроенные сценарии (ассеты) не меняются: файл — всегда отдельный сценарий.
// Чтение: ошибки (не JSON, неизвестное судно, неверный тип поля) — сценарий не загружается;
// замечания ScenarioDefinition.Validate() и неизвестные поля — предупреждения, сценарий загружается.
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Встроенные ассеты, на которые файлы ссылаются по имени.</summary>
public sealed class ScenarioCatalog
{
    public readonly List<ShipParameters> Ships = new List<ShipParameters>();
    public readonly List<WaterArea> Areas = new List<WaterArea>();
    public readonly List<AssessmentCriteria> Criteria = new List<AssessmentCriteria>();
    /// <summary>Встроенные сценарии (ассеты библиотеки) — образцы и проверка повторов идентификаторов.</summary>
    public readonly List<ScenarioDefinition> Scenarios = new List<ScenarioDefinition>();

    /// <summary>Каталог по встроенным сценариям: всё, на что они ссылаются, + дополнительные суда (типы своего судна в сцене).</summary>
    public static ScenarioCatalog From(IEnumerable<ScenarioDefinition> builtIn, params ShipParameters[] extraShips)
    {
        var c = new ScenarioCatalog();
        if (builtIn != null)
            foreach (ScenarioDefinition s in builtIn)
            {
                if (s == null || s.IsFromFile) continue;
                c.Scenarios.Add(s);
                c.Add(s.shipParameters);
                c.Add(s.tugWorkClient);
                c.Add(s.waterArea);
                c.Add(s.assessment);
                if (s.traffic != null) foreach (TrafficShipDefinition t in s.traffic) if (t != null) c.Add(t.parameters);
            }
        if (extraShips != null) foreach (ShipParameters p in extraShips) c.Add(p);
        return c;
    }

    public void Add(UnityEngine.Object o)
    {
        if (o == null || string.IsNullOrEmpty(o.name)) return;
        if (o is ShipParameters p) { if (Ships.Find(x => x.name == p.name) == null) Ships.Add(p); }
        else if (o is WaterArea a) { if (Areas.Find(x => x.name == a.name) == null) Areas.Add(a); }
        else if (o is AssessmentCriteria k) { if (Criteria.Find(x => x.name == k.name) == null) Criteria.Add(k); }
    }

    public UnityEngine.Object Find(Type t, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        if (t == typeof(ShipParameters)) return Ships.Find(x => x.name == name);
        if (t == typeof(WaterArea)) return Areas.Find(x => x.name == name);
        if (t == typeof(AssessmentCriteria)) return Criteria.Find(x => x.name == name);
        return null;
    }

    /// <summary>Ассет встроенный (есть в каталоге этим же объектом).</summary>
    public bool IsBuiltIn(UnityEngine.Object o) => o != null && Find(o.GetType(), o.name) == o;

    public string Names(Type t)
    {
        var n = new List<string>();
        if (t == typeof(ShipParameters)) foreach (ShipParameters x in Ships) n.Add(x.name);
        else if (t == typeof(WaterArea)) foreach (WaterArea x in Areas) n.Add(x.name);
        else if (t == typeof(AssessmentCriteria)) foreach (AssessmentCriteria x in Criteria) n.Add(x.name);
        return string.Join(", ", n);
    }
}

/// <summary>Итог чтения файла: сценарий (null — не загружен), ошибки, предупреждения.</summary>
public sealed class ScenarioLoadResult
{
    public string File = "";
    public ScenarioDefinition Scenario;
    public WaterArea Area;
    public readonly List<string> Errors = new List<string>();
    public readonly List<string> Warnings = new List<string>();
    public bool Ok => Errors.Count == 0 && (Scenario != null || Area != null);
}

public static class ScenarioFile
{
    public const string ScenarioFormat = "VRBridgeTrainer.Scenario";
    public const string AreaFormat = "VRBridgeTrainer.Area";
    public const int Version = 1;
    public const string Extension = ".json";

    // ─────────── Запись ───────────
    /// <summary>Сценарий → текст файла. Своя (не встроенная) акватория вписывается в файл.</summary>
    public static string Write(ScenarioDefinition s, ScenarioCatalog catalog)
    {
        var fj = new FieldJson();
        JsonValue root = JsonValue.NewObject()
            .Add("format", JsonValue.Of(ScenarioFormat))
            .Add("version", JsonValue.Of(Version))
            .Add("scenario", fj.Write(s));
        if (s.waterArea != null && (catalog == null || !catalog.IsBuiltIn(s.waterArea)))
            root.Add("areas", JsonValue.NewArray().Add(JsonValue.NewObject()
                .Add("name", JsonValue.Of(s.waterArea.name))
                .Add("area", fj.Write(s.waterArea))));
        return root + "\n";
    }

    /// <summary>Акватория → текст файла (папка «Мои акватории»).</summary>
    public static string WriteArea(WaterArea a)
    {
        JsonValue root = JsonValue.NewObject()
            .Add("format", JsonValue.Of(AreaFormat))
            .Add("version", JsonValue.Of(Version))
            .Add("name", JsonValue.Of(a.name))
            .Add("area", new FieldJson().Write(a));
        return root + "\n";
    }

    // ─────────── Чтение ───────────
    /// <summary>Текст файла → сценарий. areas — свои акватории из папки (могут быть null); name — имя для сообщений и Object.name.</summary>
    public static ScenarioLoadResult Read(string text, ScenarioCatalog catalog, IList<WaterArea> areas, string name)
    {
        var r = new ScenarioLoadResult { File = name ?? "" };
        JsonValue root = ParseRoot(text, ScenarioFormat, r);
        if (root == null) return r;

        // Свои акватории, вписанные в файл
        var embedded = new List<WaterArea>();
        JsonValue list = root["areas"];
        if (list != null && !list.IsNull)
        {
            if (list.Kind != JsonKind.Array) r.Errors.Add("areas: нужен массив [ … ]");
            else
                for (int i = 0; i < list.Items.Count; i++)
                {
                    WaterArea a = ReadAreaBody(list.Items[i], $"areas[{i}]", r);
                    if (a != null) embedded.Add(a);
                }
        }

        JsonValue body = root["scenario"];
        if (body == null || body.Kind != JsonKind.Object) { r.Errors.Add("нет раздела «scenario» { … }"); return r; }
        var fj = Mapper(catalog, embedded, areas);
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        s.name = name ?? "";
        fj.Read(body, s, "scenario");
        r.Errors.AddRange(fj.Errors);
        r.Warnings.AddRange(fj.Warnings);
        if (r.Errors.Count > 0) return r;
        if (embedded.Count > 0 && s.waterArea == null) r.Warnings.Add("в файле есть акватория, но сценарий на неё не ссылается (scenario.waterArea)");
        foreach (string p in s.Validate()) r.Warnings.Add(p);
        r.Scenario = s;
        return r;
    }

    /// <summary>Текст файла акватории → акватория.</summary>
    public static ScenarioLoadResult ReadArea(string text, string name)
    {
        var r = new ScenarioLoadResult { File = name ?? "" };
        JsonValue root = ParseRoot(text, AreaFormat, r);
        if (root == null) return r;
        r.Area = ReadAreaBody(root, "", r);
        if (r.Errors.Count > 0) r.Area = null;
        return r;
    }

    private static JsonValue ParseRoot(string text, string format, ScenarioLoadResult r)
    {
        JsonValue root;
        try { root = JsonValue.Parse(text); }
        catch (FormatException e) { r.Errors.Add("не JSON: " + e.Message); return null; }
        if (root.Kind != JsonKind.Object) { r.Errors.Add("файл должен начинаться с «{»"); return null; }
        JsonValue f = root["format"], v = root["version"];
        string got = f != null && f.Kind == JsonKind.String ? f.Text : "";
        if (got != format)
        {
            r.Errors.Add(got == ScenarioFormat || got == AreaFormat
                ? $"это файл {(got == AreaFormat ? "акватории" : "сценария")}, а ожидался файл {(format == AreaFormat ? "акватории" : "сценария")}"
                : $"не файл тренажёра: «format» = «{got}», нужно «{format}»");
            return null;
        }
        if (v == null || v.Kind != JsonKind.Number || !int.TryParse(v.Text, out int ver)) { r.Errors.Add("нет номера версии формата («version»)"); return null; }
        if (ver > Version) { r.Errors.Add($"файл версии {ver} — новее этой программы (понимает до {Version}); обновите тренажёр"); return null; }
        return root;
    }

    // { "name": "…", "area": { … } } — и в файле акватории, и в списке areas сценария
    private static WaterArea ReadAreaBody(JsonValue o, string where, ScenarioLoadResult r)
    {
        string p = where.Length > 0 ? where + "." : "";
        JsonValue n = o != null ? o["name"] : null, body = o != null ? o["area"] : null;
        if (n == null || n.Kind != JsonKind.String || string.IsNullOrWhiteSpace(n.Text)) { r.Errors.Add($"{p}name: нужно имя акватории строкой"); return null; }
        if (body == null || body.Kind != JsonKind.Object) { r.Errors.Add($"{p}area: нет описания акватории {{ … }}"); return null; }
        var fj = new FieldJson();
        var a = ScriptableObject.CreateInstance<WaterArea>();
        a.name = n.Text.Trim();
        fj.Read(body, a, p + "area");
        r.Errors.AddRange(fj.Errors);
        r.Warnings.AddRange(fj.Warnings);
        return fj.Errors.Count == 0 ? a : null;
    }

    // Имена: акватория — вписанная в файл → встроенная → из папки «Мои акватории»; судно и набор оценки — встроенные
    private static FieldJson Mapper(ScenarioCatalog catalog, List<WaterArea> embedded, IList<WaterArea> folder)
    {
        return new FieldJson
        {
            Known = t =>
            {
                string s = catalog != null ? catalog.Names(t) : "";
                if (t == typeof(WaterArea))
                {
                    var own = new List<string>();
                    foreach (WaterArea x in embedded) own.Add(x.name);
                    if (folder != null) foreach (WaterArea x in folder) if (x != null) own.Add(x.name);
                    if (own.Count > 0) s = (s.Length > 0 ? s + ", " : "") + string.Join(", ", own);
                }
                return s;
            },
            Resolve = (t, name) =>
            {
                name = name.Trim();
                if (t == typeof(WaterArea))
                {
                    WaterArea a = embedded.Find(x => x.name == name);
                    if (a != null) return a;
                    UnityEngine.Object b = catalog?.Find(t, name);
                    if (b != null) return b;
                    if (folder != null) foreach (WaterArea x in folder) if (x != null && x.name == name) return x;
                    return null;
                }
                return catalog?.Find(t, name);
            }
        };
    }
}
