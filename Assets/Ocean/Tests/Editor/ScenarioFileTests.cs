// ============================================================
// ScenarioFileTests.cs — Сценарии и акватории в файлах JSON (запрос 21.1, Core)
//
// Семь встроенных сценариев (с 21.6а — и 21-01 с триггерами) и WaterArea_Demo: ассет → файл → ассет бит в бит (тот же текст файла
// и тот же JsonUtility-снимок); своя акватория вписывается в файл сценария; ошибки и предупреждения
// чтения; папки: образцы, загрузка, повтор id, битый файл, акватория из «Мои акватории»; источник в session.json.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class ScenarioFileTests
{
    private static readonly List<string> dirs = new List<string>();

    [OneTimeTearDown]
    public void Cleanup()
    {
        foreach (string d in dirs) { try { Directory.Delete(d, true); } catch (Exception) { } }
        dirs.Clear();
    }

    private static string TempRoot()
    {
        string d = Path.Combine(Path.GetTempPath(), "VRBridgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        dirs.Add(d);
        return d;
    }

    // Встроенная библиотека, как в сцене: общие ассеты — одними объектами
    private static List<ScenarioDefinition> BuiltIn(out WaterArea area)
    {
        area = ScriptableObject.CreateInstance<WaterArea>();
        area.name = "WaterArea_Demo";
        DemoWaterArea.Fill(area);
        ShipParameters own = LowSpeedModelTests.Model45m(), mariner = LowSpeedModelTests.Mariner();
        var tug = ScriptableObject.CreateInstance<ShipParameters>();
        tug.name = "ShipParameters_Tug";
        ShipPresets.FillTug(tug);
        AssessmentCriteria Crit(string name, AssessmentSettings st)
        {
            var c = ScriptableObject.CreateInstance<AssessmentCriteria>();
            c.name = name;
            c.settings = st;
            return c;
        }
        ScenarioDefinition New(string name)
        {
            var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
            s.name = name;
            return s;
        }
        var list = new List<ScenarioDefinition>();
        ScenarioDefinition b2 = New("Scenario_B2-01_Fairway"); DemoScenario.Fill(b2, area, own); list.Add(b2);
        ScenarioDefinition b3 = New("Scenario_B3-01_Traffic"); DemoTrafficScenario.Fill(b3, area, own, mariner, mariner, own); list.Add(b3);
        ScenarioDefinition c4 = New("Scenario_C4-01_Fog"); DemoFogScenario.Fill(c4, area, own, mariner); list.Add(c4);
        ScenarioDefinition m1 = New("Scenario_19-01_Berth"); DemoBerthScenario.Fill(m1, area, own, Crit("Assessment_Berthing", DemoBerthScenario.Assessment())); list.Add(m1);
        ScenarioDefinition m2 = New("Scenario_19-02_TugBerth"); DemoTugBerthScenario.Fill(m2, area, own, Crit("Assessment_TugBerthing", DemoTugBerthScenario.Assessment())); list.Add(m2);
        ScenarioDefinition m3 = New("Scenario_19-03_TugWork"); DemoTugWorkScenario.Fill(m3, area, tug, mariner, Crit("Assessment_TugWork", DemoTugWorkScenario.Assessment())); list.Add(m3);
        ScenarioDefinition t1 = New("Scenario_21-01_Triggers"); DemoTriggerScenario.Fill(t1, area, own, mariner, null); list.Add(t1);   // 21.6а
        return list;
    }

    private static ScenarioLoadResult ReadOk(string text, ScenarioCatalog cat, string name = "файл")
    {
        ScenarioLoadResult r = ScenarioFile.Read(text, cat, null, name);
        Assert.IsTrue(r.Ok, $"{name}: " + string.Join("; ", r.Errors));
        return r;
    }

    [Test]
    public void BuiltInScenarios_FileRoundTrip_BitForBit()
    {
        List<ScenarioDefinition> lib = BuiltIn(out WaterArea area);
        ScenarioCatalog cat = ScenarioCatalog.From(lib);
        Assert.AreEqual(7, cat.Scenarios.Count);   // 21.6а: + 21-01 с триггерами
        foreach (ScenarioDefinition s in lib)
        {
            string text = ScenarioFile.Write(s, cat);
            ScenarioLoadResult r = ReadOk(text, cat, s.name);
            ScenarioDefinition back = r.Scenario;
            Assert.AreEqual(text, ScenarioFile.Write(back, cat), $"{s.id}: файл → сценарий → файл");
            Assert.AreEqual(JsonUtility.ToJson(s), JsonUtility.ToJson(back), $"{s.id}: все поля ассета совпадают");
            Assert.AreSame(area, back.waterArea, $"{s.id}: встроенная акватория — по имени, тем же ассетом");
            Assert.AreSame(s.shipParameters, back.shipParameters);
            Assert.AreSame(s.assessment, back.assessment);
            Assert.IsNull(text.Contains("\"areas\"") ? "есть" : null, $"{s.id}: встроенная акватория не вписывается");
            Assert.AreEqual(s.Validate().Count, r.Warnings.Count, $"{s.id}: предупреждения — только замечания Validate");
            TestContext.Out.WriteLine($"{s.id}: {text.Length / 1024.0:0.0} КБ, предупреждений {r.Warnings.Count}");
        }
    }

    [Test]
    public void WaterArea_FileRoundTrip_BitForBit()
    {
        BuiltIn(out WaterArea area);
        string text = ScenarioFile.WriteArea(area);
        ScenarioLoadResult r = ScenarioFile.ReadArea(text, "файл");
        Assert.IsTrue(r.Ok, string.Join("; ", r.Errors));
        Assert.AreEqual("WaterArea_Demo", r.Area.name);
        Assert.AreEqual(text, ScenarioFile.WriteArea(r.Area));
        Assert.AreEqual(JsonUtility.ToJson(area), JsonUtility.ToJson(r.Area));
        Assert.Greater(r.Area.berths.Count, 1, "причалы — тоже");
    }

    [Test]
    public void File_IsReadable_EnumsWordsNamesPositions()
    {
        List<ScenarioDefinition> lib = BuiltIn(out _);
        ScenarioCatalog cat = ScenarioCatalog.From(lib);
        string b2 = ScenarioFile.Write(lib[0], cat), m3 = ScenarioFile.Write(lib[5], cat);
        StringAssert.StartsWith("{\n  \"format\": \"VRBridgeTrainer.Scenario\",\n  \"version\": 1,", b2);
        StringAssert.Contains("\"weather\": \"Moderate\"", b2);
        StringAssert.Contains("\"telegraph\": \"FullAhead\"", b2);
        StringAssert.Contains("\"endConditions\": \"All\"", b2);
        StringAssert.Contains("\"endConditions\": \"Grounding, TimeLimit, TugWorkDone\"", m3);
        StringAssert.Contains("\"waterArea\": \"WaterArea_Demo\"", b2);
        StringAssert.Contains("\"shipParameters\": \"ShipParameters_Model45m\"", b2);
        StringAssert.Contains("\"position\": [0, -2500]", b2);
        StringAssert.Contains("\"tugWorkClient\": \"ShipParameters_Mariner\"", m3);
        StringAssert.Contains("\"assessment\": \"Assessment_TugWork\"", m3);
        StringAssert.Contains("\"ownShipType\": \"Tug\"", m3);
        StringAssert.DoesNotContain("sourceFile", m3);
    }

    [Test]
    public void CustomArea_EmbeddedInScenarioFile_SelfContained()
    {
        List<ScenarioDefinition> lib = BuiltIn(out WaterArea demo);
        ScenarioCatalog cat = ScenarioCatalog.From(lib);
        WaterArea mine = UnityEngine.Object.Instantiate(demo);
        mine.name = "Моя бухта";
        mine.aids.Add(new AidToNavigation("Новый буй", AidType.IsolatedDanger, 500, 700));
        ScenarioDefinition s = UnityEngine.Object.Instantiate(lib[0]);
        s.id = "МОЙ-01";
        s.waterArea = mine;

        string text = ScenarioFile.Write(s, cat);
        StringAssert.Contains("\"waterArea\": \"Моя бухта\"", text);
        StringAssert.Contains("\"areas\": [", text);
        ScenarioLoadResult r = ReadOk(text, cat);
        Assert.AreNotSame(mine, r.Scenario.waterArea);
        Assert.AreEqual("Моя бухта", r.Scenario.waterArea.name);
        Assert.AreEqual(ScenarioFile.WriteArea(mine), ScenarioFile.WriteArea(r.Scenario.waterArea), "акватория из файла — та же");
        Assert.AreEqual(text, ScenarioFile.Write(r.Scenario, cat));
        Assert.IsFalse(cat.IsBuiltIn(r.Scenario.waterArea));
    }

    [Test]
    public void Errors_NotLoaded_WithLineAndKnownNames()
    {
        List<ScenarioDefinition> lib = BuiltIn(out _);
        ScenarioCatalog cat = ScenarioCatalog.From(lib);
        string ok = ScenarioFile.Write(lib[0], cat);

        ScenarioLoadResult broken = ScenarioFile.Read(ok.Replace("\"version\": 1,", "\"version\": 1"), cat, null, "битый");
        Assert.IsNull(broken.Scenario);
        StringAssert.Contains("строка 4", broken.Errors[0]);
        StringAssert.Contains("запятая", broken.Errors[0]);

        ScenarioLoadResult ship = ScenarioFile.Read(ok.Replace("\"ShipParameters_Model45m\"", "\"Титаник\""), cat, null, "судно");
        Assert.IsNull(ship.Scenario);
        StringAssert.Contains("«Титаник» — нет такого судна", ship.Errors[0]);
        StringAssert.Contains("ShipParameters_Mariner", ship.Errors[0]);

        ScenarioLoadResult type = ScenarioFile.Read(ok.Replace("\"timeLimitSeconds\": 1800", "\"timeLimitSeconds\": \"долго\""), cat, null, "тип");
        Assert.IsNull(type.Scenario);
        StringAssert.Contains("scenario.timeLimitSeconds: нужно число", type.Errors[0]);

        ScenarioLoadResult en = ScenarioFile.Read(ok.Replace("\"weather\": \"Moderate\"", "\"weather\": \"Туман\""), cat, null, "погода");
        Assert.IsNull(en.Scenario);
        StringAssert.Contains("допустимо: Calm, Moderate, Storm, Night", en.Errors[0]);

        ScenarioLoadResult ver = ScenarioFile.Read(ok.Replace("\"version\": 1,", "\"version\": 7,"), cat, null, "версия");
        StringAssert.Contains("новее этой программы", ver.Errors[0]);

        ScenarioLoadResult area = ScenarioFile.Read(ScenarioFile.WriteArea(lib[0].waterArea), cat, null, "акватория");
        StringAssert.Contains("это файл акватории", area.Errors[0]);

        // Неизвестное поле — предупреждение, сценарий загружается
        ScenarioLoadResult extra = ScenarioFile.Read(ok.Replace("\"title\":", "\"автор\": \"Иванов\",\n    \"title\":"), cat, null, "лишнее");
        Assert.IsTrue(extra.Ok);
        Assert.IsTrue(extra.Warnings.Exists(w => w.Contains("неизвестное поле «автор»")));
        TestContext.Out.WriteLine(string.Join("\n", broken.Errors[0], ship.Errors[0], type.Errors[0], en.Errors[0]));
    }

    [Test]
    public void Numbers_OrderedRenamed_LegacySamplesRemoved()
    {
        // 22.10.2026: порядковые номера 01…07 — по порядку, без повторов; прежние id находят нынешние
        for (int i = 0; i < ScenarioNumbers.Renamed.Length; i++)
            Assert.AreEqual((i + 1).ToString("00"), ScenarioNumbers.Renamed[i].Now);
        Assert.AreEqual(DemoScenario.Id, ScenarioNumbers.Current("B2-01"));
        Assert.AreEqual("07", ScenarioNumbers.Current("19-03"));
        Assert.AreEqual("01-FB", ScenarioNumbers.Current("B2-01-FB"), "вариант на катере");
        Assert.AreEqual("МОЙ-01", ScenarioNumbers.Current("МОЙ-01"));
        Assert.AreEqual("01", ScenarioNumbers.Current("01"));

        List<ScenarioDefinition> lib = BuiltIn(out _);
        ScenarioCatalog cat = ScenarioCatalog.From(lib);
        string root = TempRoot();
        string samples = Path.Combine(root, ScenarioFolder.SamplesDir);
        Directory.CreateDirectory(samples);
        File.WriteAllText(Path.Combine(samples, "B2-01 Проход фарватером по путевым точкам.json"), "{}");
        File.WriteAllText(Path.Combine(samples, "Заметки.json"), "{}");
        ScenarioFolder.ExportSamples(root, cat);
        Assert.IsFalse(File.Exists(Path.Combine(samples, "B2-01 Проход фарватером по путевым точкам.json")), "образец с прежним id убран");
        Assert.IsTrue(File.Exists(Path.Combine(samples, "Заметки.json")), "чужой файл не трогается");
        Assert.AreEqual(1, Directory.GetFiles(samples, DemoScenario.Id + " *").Length);
    }

    [Test]
    public void Folder_SamplesLoadDuplicatesBrokenAreas()
    {
        List<ScenarioDefinition> lib = BuiltIn(out WaterArea demo);
        ScenarioCatalog cat = ScenarioCatalog.From(lib);
        string root = TempRoot();
        ScenarioFolder.Ensure(root);
        Assert.AreEqual(8, ScenarioFolder.ExportSamples(root, cat), "7 сценариев + акватория");
        Assert.AreEqual(0, ScenarioFolder.ExportSamples(root, cat), "без изменений — не переписываются");
        Assert.IsTrue(File.Exists(Path.Combine(root, ScenarioFolder.ReadmeFile)));
        Assert.AreEqual(0, ScenarioFolder.Load(root, cat).Scenarios.Count, "образцы сами не загружаются");

        string samples = Path.Combine(root, ScenarioFolder.SamplesDir), mine = Path.Combine(root, ScenarioFolder.ScenariosDir);
        string sample = Directory.GetFiles(samples, DemoScenario.Id + " *")[0];
        // 1) копия образца с новым id — свой сценарий
        File.WriteAllText(Path.Combine(mine, "Мой фарватер.json"), File.ReadAllText(sample).Replace($"\"id\": \"{DemoScenario.Id}\"", "\"id\": \"МОЙ-01\""));
        // 2) копия без смены id — загружается с предупреждением
        File.Copy(sample, Path.Combine(mine, "копия.json"));
        // 3) битый файл — не загружается
        File.WriteAllText(Path.Combine(mine, "битый.json"), "{ \"format\": ");
        // 4) своя акватория отдельным файлом и сценарий на неё
        WaterArea bay = UnityEngine.Object.Instantiate(demo);
        bay.name = "Бухта";
        File.WriteAllText(Path.Combine(root, ScenarioFolder.AreasDir, "Бухта.json"), ScenarioFile.WriteArea(bay));
        File.WriteAllText(Path.Combine(mine, "В бухте.json"), File.ReadAllText(sample)
            .Replace($"\"id\": \"{DemoScenario.Id}\"", "\"id\": \"БУХ-01\"").Replace("\"waterArea\": \"WaterArea_Demo\"", "\"waterArea\": \"Бухта\""));
        // 5) временный файл записи — не сценарий
        File.WriteAllText(Path.Combine(mine, "x.json.tmp"), "мусор");

        ScenarioLibraryReport rep = ScenarioFolder.Load(root, cat);
        TestContext.Out.WriteLine(rep.Text(new DateTime(2026, 10, 17, 21, 0, 0)));
        Assert.AreEqual(1, rep.Areas.Count);
        Assert.AreEqual(3, rep.Scenarios.Count, "мой, копия, в бухте");
        Assert.AreEqual(1, rep.ErrorFiles);
        ScenarioDefinition my = rep.Scenarios.Find(s => s.id == "МОЙ-01");
        Assert.IsTrue(my.IsFromFile && my.sourceFile.EndsWith("Мой фарватер.json"));
        Assert.AreEqual("Мой фарватер", my.name);
        Assert.AreSame(demo, my.waterArea);
        Assert.AreEqual("Бухта", rep.Scenarios.Find(s => s.id == "БУХ-01").waterArea.name);
        ScenarioLoadResult copy = rep.Files.Find(f => f.File.EndsWith("копия.json"));
        StringAssert.Contains($"«{DemoScenario.Id}» уже есть у встроенного сценария", copy.Warnings[0]);
        StringAssert.Contains("✗ НЕ ЗАГРУЖЕН", rep.Text(DateTime.Now));
        StringAssert.Contains("сценариев 3, акваторий 1; не загружено (ошибки) — 1", rep.Summary());
        Assert.IsFalse(cat.Scenarios.Contains(my), "встроенные не меняются");
    }

    [Test]
    public void Session_RecordsSourceFile_AndVariantKeepsIt()
    {
        List<ScenarioDefinition> lib = BuiltIn(out _);
        ScenarioCatalog cat = ScenarioCatalog.From(lib);
        ScenarioDefinition s = ReadOk(ScenarioFile.Write(lib[0], cat), cat).Scenario;
        s.sourceFile = Path.Combine("C:\\Users\\кто-то\\Documents\\VRBridgeTrainer", ScenarioFolder.ScenariosDir, "Мой.json");
        Assert.AreEqual("Мой.json", SessionScenario.From(s).source_file, "только имя файла, без пути пользователя");
        Assert.AreEqual("", SessionScenario.From(lib[0]).source_file, "встроенный");
        var boat = ScriptableObject.CreateInstance<ShipParameters>();
        ShipPresets.FillFastBoat(boat);
        ScenarioDefinition v = ScenarioVariants.WithShip(s, boat, s.shipParameters, "-FB", " (катер)");
        Assert.AreNotSame(s, v);
        Assert.AreEqual(s.sourceFile, v.sourceFile, "вариант на катере — тот же файл");
        Assert.IsFalse(ScenarioCatalog.From(new[] { s }).Scenarios.Contains(s), "сценарий из файла — не встроенный");
    }
}
