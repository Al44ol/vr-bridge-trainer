// ============================================================
// AreaEditorTests.cs — Редактор акватории: документ, отмена, сохранение, черновик, сетка, проверка (запрос 21.2а, Core)
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class AreaEditorTests
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

    private static WaterArea Demo()
    {
        var a = ScriptableObject.CreateInstance<WaterArea>();
        a.name = "WaterArea_Demo";
        DemoWaterArea.Fill(a);
        return a;
    }

    private static ScenarioCatalog Catalog(WaterArea demo)
    {
        var c = new ScenarioCatalog();
        c.Add(demo);
        return c;
    }

    [Test]
    public void History_UndoRedo_SkipsSame_DropsRedoAfterNewEdit_Limit()
    {
        var h = new EditHistory { Limit = 3 };
        h.Reset("a");
        Assert.IsFalse(h.Push("a"), "то же состояние не пишется");
        h.Push("b"); h.Push("c");
        Assert.AreEqual("b", h.Undo());
        Assert.AreEqual("a", h.Undo());
        Assert.IsNull(h.Undo());
        Assert.AreEqual("b", h.Redo());
        h.Push("x");
        Assert.IsFalse(h.CanRedo, "новая правка после отмены — повтор теряется");
        h.Push("y"); h.Push("z");
        Assert.AreEqual(3, h.Count, "глубина ограничена");
        Assert.AreEqual("y", h.Undo());
    }

    [Test]
    public void CopyOfBuiltIn_EditUndo_BuiltInUntouched()
    {
        WaterArea demo = Demo();
        string before = ScenarioFile.WriteArea(demo);
        AreaDocument doc = AreaDocument.CopyOf(demo, "Моя бухта");
        Assert.AreNotSame(demo, doc.Area);
        Assert.AreEqual("Моя бухта", doc.Area.name);
        Assert.IsTrue(doc.Dirty);

        doc.Area.defaultDepth = 12f;
        Assert.IsTrue(doc.Commit());
        doc.Area.boundsMax = new WorldPosition(9000, 9000);
        doc.Commit();
        Assert.IsTrue(doc.Undo());
        Assert.AreEqual(12f, doc.Area.defaultDepth);
        Assert.AreEqual(demo.boundsMax.East, doc.Area.boundsMax.East, "границы — как до правки");
        Assert.IsTrue(doc.Undo());
        Assert.AreEqual(demo.defaultDepth, doc.Area.defaultDepth);
        Assert.IsTrue(doc.Redo());
        Assert.AreEqual(12f, doc.Area.defaultDepth);
        Assert.AreEqual(before, ScenarioFile.WriteArea(demo), "встроенная не изменилась");
    }

    [Test]
    public void Save_History_NameTaken_BuiltInName_Rename()
    {
        WaterArea demo = Demo();
        ScenarioCatalog cat = Catalog(demo);
        string root = TempRoot();
        var t0 = new DateTime(2026, 10, 18, 10, 0, 0);

        AreaDocument doc = AreaDocument.CopyOf(demo, "WaterArea_Demo");
        Assert.AreEqual(AreaSaveStatus.Invalid, doc.Save(root, cat, false, t0), "имя встроенной — нельзя");
        StringAssert.Contains("занято встроенной", doc.LastMessage);

        doc.Rename("Бухта");
        Assert.AreEqual(AreaSaveStatus.Saved, doc.Save(root, cat, false, t0), doc.LastMessage);
        string file = Path.Combine(root, ScenarioFolder.AreasDir, "Бухта.json");
        Assert.IsTrue(File.Exists(file));
        Assert.IsFalse(doc.Dirty);
        Assert.AreEqual(file, doc.FilePath);

        // Повторное сохранение того же файла — прежний вариант в историю
        doc.Area.defaultDepth = 15f; doc.Commit();
        Assert.AreEqual(AreaSaveStatus.Saved, doc.Save(root, cat, false, t0.AddMinutes(1)));
        string hist = Path.Combine(root, AreaDocument.HistoryDir, "Акватории", "Бухта");
        Assert.AreEqual(1, Directory.GetFiles(hist).Length);
        StringAssert.Contains("\"defaultDepth\": 30", File.ReadAllText(Directory.GetFiles(hist)[0]), "в истории — прежний вариант");

        // Другой документ с тем же именем — «уже есть», без overwrite не пишет
        AreaDocument other = AreaDocument.New("Бухта");
        Assert.AreEqual(AreaSaveStatus.NameTaken, other.Save(root, cat, false, t0.AddMinutes(2)));
        StringAssert.Contains("\"defaultDepth\": 15", File.ReadAllText(file), "файл не тронут");
        Assert.AreEqual(AreaSaveStatus.Saved, other.Save(root, cat, true, t0.AddMinutes(3)), "заменить");
        Assert.AreEqual(2, Directory.GetFiles(hist).Length, "заменённый — тоже в истории");

        // Переименование сохранённой: новый файл, прежний — в историю, не остаётся вторым
        doc = AreaDocument.Open(file, null);
        doc.Rename("Бухта Северная");
        Assert.AreEqual(AreaSaveStatus.Saved, doc.Save(root, cat, false, t0.AddMinutes(4)));
        Assert.IsFalse(File.Exists(file));
        Assert.IsTrue(File.Exists(Path.Combine(root, ScenarioFolder.AreasDir, "Бухта Северная.json")));

        // Загрузка тренажёром видит сохранённую акваторию
        ScenarioLibraryReport rep = ScenarioFolder.Load(root, cat);
        Assert.AreEqual(1, rep.Areas.Count);
        Assert.AreEqual("Бухта Северная", rep.Areas[0].name);
    }

    [Test]
    public void History_KeepsLast20()
    {
        string root = TempRoot();
        string f = Path.Combine(root, "a.json");
        File.WriteAllText(f, "{}");
        var t = new DateTime(2026, 10, 18, 10, 0, 0);
        for (int i = 0; i < 25; i++) AreaDocument.KeepHistory(root, f, t.AddSeconds(i));
        string[] left = Directory.GetFiles(Path.Combine(root, AreaDocument.HistoryDir, "Акватории", "a"));
        Assert.AreEqual(AreaDocument.HistoryKeep, left.Length);
        Array.Sort(left, StringComparer.Ordinal);
        StringAssert.EndsWith("20261018_100005.json", left[0], "старые удалены");
    }

    [Test]
    public void Draft_WriteRestore_DeletedOnSave()
    {
        WaterArea demo = Demo();
        string root = TempRoot();
        AreaDocument doc = AreaDocument.CopyOf(demo, "Черновая");
        doc.Area.defaultDepth = 7f; doc.Commit();
        doc.WriteDraft(root);
        AreaDocument back = AreaDocument.ReadDraft(root, out DateTime when);
        Assert.IsNotNull(back);
        Assert.AreEqual("Черновая", back.Area.name);
        Assert.AreEqual(7f, back.Area.defaultDepth);
        Assert.IsTrue(back.Dirty);
        StringAssert.Contains("копия", back.Origin);
        Assert.AreNotEqual(DateTime.MinValue, when);
        Assert.AreEqual(0, ScenarioFolder.Load(root, Catalog(demo)).Files.Count, "черновик тренажёр не загружает");
        Assert.AreEqual(AreaSaveStatus.Saved, back.Save(root, Catalog(demo), false, DateTime.Now));
        Assert.IsNull(AreaDocument.ReadDraft(root, out _), "сохранили — черновика нет");
    }

    [Test]
    public void Grid_SnapAndDisplayStep()
    {
        WorldPosition p = MapGrid.Snap(new WorldPosition(123.4, -77.0), 50.0);
        Assert.AreEqual(100.0, p.East, 1e-9);
        Assert.AreEqual(-100.0, p.North, 1e-9);
        Assert.AreEqual(185.2, MapGrid.Snap(new WorldPosition(200, 0), WaterArea.CableMeters).East, 1e-9);
        Assert.AreEqual(50.0, MapGrid.DisplayStep(50.0, 2.0), 1e-9, "25 пикс. — как есть");
        Assert.AreEqual(250.0, MapGrid.DisplayStep(50.0, 20.0), 1e-9, "2,5 пикс. → ×5 (12,5 пикс.)");
        Assert.AreEqual(new List<double> { -100, -50, 0, 50, 100 }, MapGrid.Lines(-120, 120, 50));
        Assert.AreEqual("1 кбт", MapGrid.StepTitle(WaterArea.CableMeters));
        Assert.AreEqual("1 км", MapGrid.StepTitle(1000));
    }

    [Test]
    public void Checks_ErrorsAndWarnings()
    {
        WaterArea demo = Demo();
        ScenarioCatalog cat = Catalog(demo);
        Assert.AreEqual(0, AreaChecks.Check(AreaDocument.CopyOf(demo, "Копия").Area, cat).Errors.Count, "демо без ошибок");
        AreaDocument doc = AreaDocument.New("Тест");
        WaterArea a = doc.Area;
        a.boundsMin = new WorldPosition(100, 0);
        a.boundsMax = new WorldPosition(0, 1000);
        a.defaultDepth = 0f;
        a.aids.Add(new AidToNavigation("Б", AidType.LateralPort, 50000, 0));
        a.aids.Add(new AidToNavigation("Б", AidType.LateralStarboard, 50, 50));
        AreaCheckResult r = AreaChecks.Check(a, cat);
        Assert.AreEqual(2, r.Errors.Count, string.Join("; ", r.Errors));
        Assert.IsTrue(r.Warnings.Exists(w => w.Contains("вне района")));
        Assert.IsTrue(r.Warnings.Exists(w => w.Contains("имя повторяется")));
        a.name = "WaterArea_Demo";
        Assert.IsTrue(AreaChecks.Check(a, cat).Errors.Exists(e => e.Contains("занято встроенной")));
    }
}
