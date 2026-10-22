// ============================================================
// ScenarioLibraryTests.cs — Библиотека своих сценариев и акваторий (запрос 21.6в, Core): список (в том числе файлы
// с ошибками), копия с новым id / именем, переименование с переездом истории, корзина (восстановить, копией,
// очистить), откат версии (текущий — в историю), экспорт на «флешку» и импорт с конфликтами. Флешка — временная папка.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

public class ScenarioLibraryTests
{
    private static readonly List<string> dirs = new List<string>();
    private static readonly DateTime T0 = new DateTime(2026, 10, 22, 20, 0, 0);

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

    // Станция: встроенный B2-01, своя акватория «Бухта» и сценарий МОЙ-01 на ней
    private static string Station(out ScenarioCatalog catalog, out ScenarioDefinition builtIn)
    {
        var demo = ScriptableObject.CreateInstance<WaterArea>();
        demo.name = "WaterArea_Demo";
        DemoWaterArea.Fill(demo);
        ShipParameters own = LowSpeedModelTests.Model45m();
        builtIn = ScriptableObject.CreateInstance<ScenarioDefinition>();
        builtIn.name = "Scenario_B2-01_Fairway";
        DemoScenario.Fill(builtIn, demo, own);
        catalog = ScenarioCatalog.From(new[] { builtIn }, own);

        string root = TempRoot();
        AreaDocument area = AreaDocument.New("Бухта");
        Assert.AreEqual(AreaSaveStatus.Saved, area.Save(root, catalog, false, T0));
        ScenarioDocument s = ScenarioDocument.New(area.Area, own, "МОЙ-01", "Проход бухты", catalog, new List<WaterArea> { area.Area });
        Assert.AreEqual(AreaSaveStatus.Saved, s.Save(root, false, T0));
        return root;
    }

    private static string ScenarioPath(string root, string name) => Path.Combine(root, ScenarioFolder.ScenariosDir, name + ".json");

    [Test]
    public void Scan_ListsOwnFiles_WithErrors_AndUsage()
    {
        string root = Station(out ScenarioCatalog cat, out _);
        File.WriteAllText(ScenarioPath(root, "Сломанный"), "{ \"format\": \"VRBridgeTrainer.Scenario\", ", new UTF8Encoding(false));
        LibraryScan scan = ScenarioLibrary.Scan(root, cat);
        Assert.AreEqual(2, scan.Scenarios.Count);
        LibraryEntry ok = scan.Scenarios.Find(e => e.Ok), bad = scan.Scenarios.Find(e => !e.Ok);
        Assert.AreEqual("МОЙ-01", ok.Id);
        Assert.AreEqual("Бухта", ok.AreaName);
        Assert.AreEqual("Сломанный", bad.Name, "файл с ошибками — в списке, по имени файла");
        Assert.IsNotEmpty(bad.Errors);
        Assert.AreEqual(1, scan.Areas.Count);
        Assert.AreEqual(1, scan.Areas[0].UsedBy, "на «Бухте» — один свой сценарий");
    }

    [Test]
    public void Copy_NewIdAndName()
    {
        string root = Station(out ScenarioCatalog cat, out _);
        LibraryScan scan = ScenarioLibrary.Scan(root, cat);
        LibraryResult c = ScenarioLibrary.Copy(root, cat, scan, LibraryKind.Scenario, scan.Scenarios[0].FilePath);
        Assert.IsTrue(c.Done, c.Message);
        LibraryResult a = ScenarioLibrary.Copy(root, cat, scan, LibraryKind.Area, scan.Areas[0].FilePath);
        Assert.IsTrue(a.Done, a.Message);

        scan = ScenarioLibrary.Scan(root, cat);
        LibraryEntry copy = scan.Scenarios.Find(e => e.Id == "МОЙ-02");
        Assert.IsNotNull(copy, "копия — со следующим свободным id");
        Assert.AreEqual("Проход бухты (копия)", copy.Title);
        Assert.IsTrue(scan.Areas.Exists(e => e.AreaName == "Бухта (копия)"));
        Assert.AreEqual(2, scan.Scenarios.Count);
        Assert.AreEqual(2, scan.Areas.Count);
    }

    [Test]
    public void Rename_MovesFileAndHistory_BuiltInIdRefused()
    {
        string root = Station(out ScenarioCatalog cat, out ScenarioDefinition b2);
        string old = ScenarioPath(root, "МОЙ-01 Проход бухты");
        // Вторая запись — прежний вариант уходит в историю
        ScenarioDocument d = ScenarioDocument.Open(old, cat, ScenarioLibrary.Scan(root, cat).FolderAreas(), null);
        d.Scenario.briefing = "второй вариант";
        d.Commit();
        Assert.AreEqual(AreaSaveStatus.Saved, d.Save(root, false, T0.AddMinutes(1)));
        Assert.AreEqual(1, ScenarioLibrary.Versions(root, LibraryKind.Scenario, old).Count);

        LibraryScan scan = ScenarioLibrary.Scan(root, cat);
        LibraryResult bad = ScenarioLibrary.RenameScenario(root, cat, scan, scan.Scenarios[0], b2.id, "x", false, T0.AddMinutes(2));
        Assert.AreEqual(LibraryStatus.Invalid, bad.Status, "id встроенного занимать нельзя");

        LibraryResult r = ScenarioLibrary.RenameScenario(root, cat, scan, scan.Scenarios[0], "МОЙ-07", "Новое имя", false, T0.AddMinutes(2));
        Assert.IsTrue(r.Done, r.Message);
        Assert.IsFalse(File.Exists(old));
        string now = ScenarioPath(root, "МОЙ-07 Новое имя");
        Assert.AreEqual(now, r.Path);
        scan = ScenarioLibrary.Scan(root, cat);
        Assert.AreEqual("МОЙ-07", scan.Scenarios[0].Id);
        Assert.AreEqual("второй вариант", scan.Scenarios[0].Scenario.briefing);
        Assert.IsFalse(Directory.Exists(ScenarioLibrary.HistoryFolder(root, LibraryKind.Scenario, old)), "история переехала");
        Assert.AreEqual(2, ScenarioLibrary.Versions(root, LibraryKind.Scenario, now).Count, "прежняя версия + вариант до переименования");

        LibraryResult ar = ScenarioLibrary.RenameArea(root, cat, scan, scan.Areas[0], "Залив", false, T0.AddMinutes(3));
        Assert.IsTrue(ar.Done, ar.Message);
        StringAssert.Contains("Сценариев на ней: 1", ar.Message);
        scan = ScenarioLibrary.Scan(root, cat);
        Assert.AreEqual("Залив", scan.Areas[0].AreaName);
        Assert.IsTrue(scan.Scenarios[0].Ok, "сценарий на переименованной акватории читается (копия вписана в файл)");
    }

    [Test]
    public void Trash_RestoreCopyEmpty()
    {
        string root = Station(out ScenarioCatalog cat, out _);
        LibraryScan scan = ScenarioLibrary.Scan(root, cat);
        string path = scan.Scenarios[0].FilePath;
        Assert.IsTrue(ScenarioLibrary.Trash(root, scan.Scenarios[0], T0).Done);
        Assert.IsFalse(File.Exists(path));
        Assert.AreEqual(0, ScenarioLibrary.Scan(root, cat).Scenarios.Count, "из корзины не загружается");
        List<TrashEntry> trash = ScenarioLibrary.TrashList(root);
        Assert.AreEqual(1, trash.Count);
        Assert.AreEqual(Path.GetFileName(path), trash[0].OriginalName);
        Assert.AreEqual(T0, trash[0].DeletedAt);

        Assert.IsTrue(ScenarioLibrary.Restore(root, trash[0], false, T0.AddMinutes(1)).Done);
        Assert.IsTrue(File.Exists(path));

        // Снова в корзину, а место заняли — NameTaken, затем копией
        scan = ScenarioLibrary.Scan(root, cat);
        string text = File.ReadAllText(path, Encoding.UTF8);
        ScenarioLibrary.Trash(root, scan.Scenarios[0], T0.AddMinutes(2));
        File.WriteAllText(path, text, new UTF8Encoding(false));
        trash = ScenarioLibrary.TrashList(root);
        Assert.AreEqual(LibraryStatus.NameTaken, ScenarioLibrary.Restore(root, trash[0], false, T0.AddMinutes(3)).Status);
        LibraryResult c = ScenarioLibrary.RestoreAsCopy(root, cat, ScenarioLibrary.Scan(root, cat), trash[0]);
        Assert.IsTrue(c.Done, c.Message);
        scan = ScenarioLibrary.Scan(root, cat);
        Assert.AreEqual(2, scan.Scenarios.Count);
        Assert.IsTrue(scan.Scenarios.Exists(e => e.Id == "МОЙ-02"));
        Assert.AreEqual(0, ScenarioLibrary.TrashList(root).Count);

        ScenarioLibrary.Trash(root, scan.Areas[0], T0.AddMinutes(4));
        ScenarioLibrary.Trash(root, scan.Scenarios[1], T0.AddMinutes(4));
        Assert.AreEqual(2, ScenarioLibrary.EmptyTrash(root));
        Assert.AreEqual(0, ScenarioLibrary.TrashList(root).Count);
    }

    [Test]
    public void RestoreVersion_CurrentGoesToHistory_IdentityKept()
    {
        string root = Station(out ScenarioCatalog cat, out _);
        string path = ScenarioPath(root, "МОЙ-01 Проход бухты");
        ScenarioDocument d = ScenarioDocument.Open(path, cat, ScenarioLibrary.Scan(root, cat).FolderAreas(), null);
        d.Scenario.briefing = "вариант 2";
        d.Commit();
        d.Save(root, false, T0.AddMinutes(1));

        LibraryScan scan = ScenarioLibrary.Scan(root, cat);
        List<HistoryVersion> v = ScenarioLibrary.Versions(root, LibraryKind.Scenario, path);
        Assert.AreEqual(1, v.Count);
        Assert.AreEqual(T0.AddMinutes(1), v[0].When);
        LibraryResult r = ScenarioLibrary.RestoreVersion(root, cat, scan, scan.Scenarios[0], v[0], T0.AddMinutes(2));
        Assert.IsTrue(r.Done, r.Message);

        scan = ScenarioLibrary.Scan(root, cat);
        Assert.AreEqual("", scan.Scenarios[0].Scenario.briefing ?? "", "версия на месте");
        Assert.AreEqual("МОЙ-01", scan.Scenarios[0].Id);
        v = ScenarioLibrary.Versions(root, LibraryKind.Scenario, path);
        Assert.AreEqual(2, v.Count, "текущий вариант — тоже в истории");
        Assert.AreEqual(T0.AddMinutes(2), v[0].When, "последние — первыми");
    }

    [Test]
    public void ExportImport_Conflicts()
    {
        string root = Station(out ScenarioCatalog cat, out ScenarioDefinition b2);
        string flash = TempRoot();
        LibraryScan scan = ScenarioLibrary.Scan(root, cat);
        Assert.AreEqual(LibraryStatus.Invalid, ScenarioLibrary.Export(root, scan.Scenarios, root).Status, "в папку самой станции — нельзя");
        LibraryResult ex = ScenarioLibrary.Export(root, new List<LibraryEntry>(scan.Scenarios) { scan.Areas[0] }, flash);
        Assert.IsTrue(ex.Done, ex.Message);
        string flashScen = Path.Combine(flash, ScenarioLibrary.FlashFolder, ScenarioFolder.ScenariosDir);
        Assert.IsTrue(File.Exists(Path.Combine(flashScen, "МОЙ-01 Проход бухты.json")));
        Assert.IsTrue(File.Exists(Path.Combine(flash, ScenarioLibrary.FlashFolder, ScenarioFolder.AreasDir, "Бухта.json")));

        // На флешке: изменённый МОЙ-01 (файл уже есть), встроенный B2-01 (id встроенного), новый МОЙ-05, мусор
        string mine = Path.Combine(flashScen, "МОЙ-01 Проход бухты.json");
        ScenarioDefinition edited = ScenarioLibrary.Scan(root, cat).Scenarios[0].Scenario;
        edited.briefing = "с флешки";
        File.WriteAllText(mine, ScenarioFile.Write(edited, cat), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(flash, "B2-01.json"), ScenarioFile.Write(b2, cat), new UTF8Encoding(false));
        ScenarioDefinition fresh = ScenarioLibrary.Scan(root, cat).Scenarios[0].Scenario;
        fresh.id = "МОЙ-05";
        fresh.title = "С другой станции";
        File.WriteAllText(Path.Combine(flashScen, "МОЙ-05 С другой станции.json"), ScenarioFile.Write(fresh, cat), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(flash, "заметки.json"), "{ \"a\": 1 }", new UTF8Encoding(false));

        Assert.IsNotEmpty(ScenarioLibrary.ImportScan(root, root, cat, scan).Error, "из папки самой станции — нельзя");
        ImportScanResult found = ScenarioLibrary.ImportScan(flash, root, cat, scan);
        Assert.AreEqual("", found.Error);
        ImportItem area = found.Items.Find(i => i.Kind == LibraryKind.Area && i.Ok);
        ImportItem changed = found.Items.Find(i => i.Id == "МОЙ-01");
        ImportItem builtIn = found.Items.Find(i => i.Id == b2.id);
        ImportItem newOne = found.Items.Find(i => i.Id == "МОЙ-05");
        Assert.IsTrue(area.Same, "акватория та же — импортировать нечего");
        Assert.AreEqual(ImportConflict.FileExists, changed.Conflict);
        Assert.IsFalse(changed.Same);
        Assert.AreEqual(ImportConflict.BuiltInId, builtIn.Conflict);
        Assert.AreEqual(ImportConflict.None, newOne.Conflict);
        Assert.IsTrue(found.Items.Exists(i => !i.Ok && i.FileName == "заметки.json"));

        Assert.AreEqual(LibraryStatus.NameTaken, ScenarioLibrary.Import(root, cat, scan, changed, ImportMode.Normal, T0).Status);
        Assert.IsTrue(ScenarioLibrary.Import(root, cat, scan, newOne, ImportMode.Normal, T0).Done);
        Assert.IsTrue(ScenarioLibrary.Import(root, cat, ScenarioLibrary.Scan(root, cat), changed, ImportMode.Replace, T0).Done);
        LibraryResult bc = ScenarioLibrary.Import(root, cat, ScenarioLibrary.Scan(root, cat), builtIn, ImportMode.Copy, T0);
        Assert.IsTrue(bc.Done, bc.Message);

        scan = ScenarioLibrary.Scan(root, cat);
        Assert.AreEqual(3, scan.Scenarios.Count);
        Assert.AreEqual("с флешки", scan.Scenarios.Find(e => e.Id == "МОЙ-01").Scenario.briefing);
        Assert.AreEqual(1, ScenarioLibrary.Versions(root, LibraryKind.Scenario, ScenarioPath(root, "МОЙ-01 Проход бухты")).Count, "заменённый — в истории");
        LibraryEntry copy = scan.Scenarios.Find(e => e.Id != "МОЙ-01" && e.Id != "МОЙ-05");
        StringAssert.StartsWith(ScenarioDocument.IdPrefix, copy.Id, "id встроенного — только копией с новым id");
        Assert.AreEqual(b2.title, copy.Title);
    }
}
