// ============================================================
// ScenarioEditorTests.cs — Слой сценария в редакторе (запрос 21.4а, Core): документ сценария
// («с нуля», «на основе» встроенного, отмена, сохранение с историей, черновик), правка маршрута
// (имена, вставка, удаление, XTE от ручки, попадание мышью, курс старта), замечания редактора.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class ScenarioEditorTests
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

    // Встроенный B2-01 на WaterArea_Demo и каталог, как в сцене
    private static ScenarioDefinition BuiltInB2(out ScenarioCatalog catalog)
    {
        var area = ScriptableObject.CreateInstance<WaterArea>();
        area.name = "WaterArea_Demo";
        DemoWaterArea.Fill(area);
        ShipParameters own = LowSpeedModelTests.Model45m();
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        s.name = "Scenario_B2-01_Fairway";
        DemoScenario.Fill(s, area, own);
        catalog = ScenarioCatalog.From(new[] { s }, own);
        return s;
    }

    private static MapViewport View(double mpp)
    {
        var v = new MapViewport { Follow = false, MinMetersPerPixel = 0.1, MaxMetersPerPixel = 400.0 };
        v.SetSize(800, 600);
        v.Center = new WorldPosition(0, 0);
        v.MetersPerPixel = mpp;
        return v;
    }

    [Test]
    public void NextId_SkipsTaken()
    {
        Assert.AreEqual("МОЙ-01", ScenarioDocument.NextId(null));
        Assert.AreEqual("МОЙ-03", ScenarioDocument.NextId(new[] { "B2-01", "МОЙ-01", "мой-02" }));
    }

    [Test]
    public void BasedOn_NewIdAndTitle_BuiltInUntouched_UndoRedo()
    {
        ScenarioDefinition b2 = BuiltInB2(out ScenarioCatalog cat);
        string before = ScenarioFile.Write(b2, cat);
        ScenarioDocument doc = ScenarioDocument.BasedOn(b2, "МОЙ-01", cat, null);
        ScenarioDefinition s = doc.Scenario;
        Assert.AreNotSame(b2, s);
        Assert.AreEqual("МОЙ-01", s.id);
        StringAssert.EndsWith("(копия)", s.title);
        Assert.AreSame(b2.waterArea, s.waterArea, "встроенная акватория — по ссылке (именем)");
        Assert.AreEqual(b2.route.Count, s.route.Count);
        Assert.IsTrue(doc.Dirty);

        RouteEditing.Move(s.route, 1, new WorldPosition(100, -300));
        Assert.IsTrue(doc.Commit());
        s.initial.headingDeg = 45f;
        doc.Commit();
        Assert.AreEqual(before, ScenarioFile.Write(b2, cat), "встроенный не меняется");

        Assert.IsTrue(doc.Undo());
        Assert.AreEqual(b2.initial.headingDeg, doc.Scenario.initial.headingDeg);
        Assert.AreEqual(100.0, doc.Scenario.route[1].position.East, 1e-9);
        Assert.IsTrue(doc.Undo());
        Assert.AreEqual(b2.route[1].position.East, doc.Scenario.route[1].position.East, 1e-9);
        Assert.IsTrue(doc.Redo());
        Assert.AreEqual(100.0, doc.Scenario.route[1].position.East, 1e-9);
    }

    [Test]
    public void Route_AddInsertRemove_Names_Xte()
    {
        var r = new List<RouteWaypoint>();
        RouteEditing.Add(r, new WorldPosition(0, 0));
        RouteEditing.Add(r, new WorldPosition(0, 1000));
        r[1].xteLimit = 250f;
        r[1].narrowChannel = true;
        RouteEditing.Add(r, new WorldPosition(1000, 1000));
        Assert.AreEqual(new[] { "WP1", "WP2", "WP3" }, r.ConvertAll(w => w.name).ToArray());
        Assert.AreEqual(250f, r[2].xteLimit, "XTE — как у прежней последней");
        Assert.IsTrue(r[2].narrowChannel);

        int i = RouteEditing.Insert(r, 0, new WorldPosition(0, 500));
        Assert.AreEqual(1, i);
        Assert.AreEqual("WP4", r[1].name);
        Assert.AreEqual(250f, r[1].xteLimit, "XTE — участка");
        Assert.IsTrue(RouteEditing.Remove(r, 1));
        Assert.AreEqual("WP4", RouteEditing.NextName(r), "имя освободилось");
        Assert.AreEqual(-1, RouteEditing.Insert(r, 5, new WorldPosition(0, 0)));

        // XTE от ручки: расстояние от оси участка WP1→WP2 (по северу), шаг 10 м
        Assert.AreEqual(180f, RouteEditing.XteFrom(r, 0, new WorldPosition(183, 400)));
        Assert.AreEqual(180f, RouteEditing.XteFrom(r, 0, new WorldPosition(-178, 900)), "слева — так же");
        Assert.AreEqual(10f, RouteEditing.XteFrom(r, 0, new WorldPosition(1, 400)), "не меньше шага");
        WorldPosition h = RouteEditing.XteHandle(r, 0);
        Assert.AreEqual(250.0, h.East, 1e-9, "ручка — справа от участка на север");
        Assert.AreEqual(500.0, h.North, 1e-9);
        Assert.AreEqual(90.0, RouteEditing.LegCourse(r, 1), 1e-9);
    }

    [Test]
    public void Route_HitTests_StartHeading()
    {
        var r = new List<RouteWaypoint>();
        RouteEditing.Add(r, new WorldPosition(0, 0));
        RouteEditing.Add(r, new WorldPosition(0, 1000));
        MapViewport v = View(5.0);   // центр (0, 0) — пиксель (400, 300)
        Assert.AreEqual(1, RouteEditing.HitPoint(r, v, 400, 300 + 200 + 5));
        Assert.AreEqual(-1, RouteEditing.HitPoint(r, v, 420, 300));
        Assert.AreEqual(0, RouteEditing.HitLegMid(r, v, 403, 400));
        // XTE 150 м = 30 пикс. → ручка видна в (430, 400)
        Assert.AreEqual(0, RouteEditing.HitXte(r, v, 431, 401));
        MapViewport far = View(50.0);   // 150 м = 3 пикс. — ручка не показывается
        Assert.IsFalse(RouteEditing.XteHandleVisible(r, 0, far));
        Assert.AreEqual(-1, RouteEditing.HitXte(r, far, 403, 310));

        var st = new OwnShipInitialState { position = new WorldPosition(0, 0) };
        Assert.IsTrue(RouteEditing.HitStart(st, v, 410, 305));
        WorldPosition hh = RouteEditing.HeadingHandle(st.position, 90.0, 100.0);
        Assert.AreEqual(100.0, hh.East, 1e-9);
        Assert.AreEqual(0.0, hh.North, 1e-9);
        Assert.AreEqual(45f, RouteEditing.HeadingTo(st.position, new WorldPosition(10, 10.4), 5.0));
        Assert.AreEqual(270f, RouteEditing.HeadingTo(st.position, new WorldPosition(-10, 0)));
        Assert.AreEqual(0f, RouteEditing.HeadingTo(st.position, new WorldPosition(-0.01, 100), 5.0), "359,99° → 0°");
    }

    [Test]
    public void New_OnOwnArea_SaveHistoryLoad_DraftRestore()
    {
        ScenarioDefinition b2 = BuiltInB2(out ScenarioCatalog cat);
        string root = TempRoot();
        AreaDocument ad = AreaDocument.New("Бухта");
        Assert.AreEqual(AreaSaveStatus.Saved, ad.Save(root, cat, false, new DateTime(2026, 10, 18, 10, 0, 0)));

        ScenarioDocument doc = ScenarioDocument.New(ad.Area, b2.shipParameters, "МОЙ-01", "Проход бухты", cat, new List<WaterArea> { ad.Area });
        ScenarioDefinition s = doc.Scenario;
        Assert.AreEqual(0.0, s.initial.position.East, 1e-9, "старт — в середине района");
        Assert.AreEqual(0, s.route.Count);
        RouteEditing.Add(s.route, new WorldPosition(0, 0));
        RouteEditing.Add(s.route, new WorldPosition(0, 2000));
        s.briefing = "Пройти бухту.";
        doc.Commit();

        // Идентификатор встроенного занимать нельзя
        s.id = "B2-01";
        Assert.AreEqual(AreaSaveStatus.Invalid, doc.Save(root, false, DateTime.Now));
        s.id = "МОЙ-01";
        Assert.AreEqual(AreaSaveStatus.Saved, doc.Save(root, false, new DateTime(2026, 10, 18, 10, 1, 0)), doc.LastMessage);
        Assert.IsFalse(doc.Dirty);
        Assert.AreEqual("МОЙ-01 Проход бухты.json", Path.GetFileName(doc.FilePath));
        StringAssert.Contains("\"areas\"", File.ReadAllText(doc.FilePath), "своя акватория вписана в файл");

        // Пересохранение — прежний вариант в «История\Сценарии»; переименование — старый файл туда же
        s.title = "Проход бухты ночью";
        doc.Commit();
        Assert.AreEqual(AreaSaveStatus.Saved, doc.Save(root, false, new DateTime(2026, 10, 18, 10, 2, 0)));
        Assert.IsFalse(File.Exists(Path.Combine(root, ScenarioFolder.ScenariosDir, "МОЙ-01 Проход бухты.json")));
        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(root, AreaDocument.HistoryDir, "Сценарии", "МОЙ-01 Проход бухты")).Length);

        // Файл загружается тренажёром (ScenarioFolder.Load)
        ScenarioLibraryReport rep = ScenarioFolder.Load(root, cat);
        Assert.AreEqual(1, rep.Scenarios.Count, rep.Text(DateTime.Now));
        Assert.AreEqual("Проход бухты ночью", rep.Scenarios[0].title);
        Assert.AreEqual(2, rep.Scenarios[0].route.Count);

        // Другой документ с тем же именем файла — «имя занято»
        ScenarioDocument other = ScenarioDocument.New(ad.Area, b2.shipParameters, "МОЙ-01", "Проход бухты ночью", cat, null);
        Assert.AreEqual(AreaSaveStatus.NameTaken, other.Save(root, false, DateTime.Now));

        // Черновик
        s.initial.speedKnots = 7f;
        doc.Commit();
        doc.WriteDraft(root);
        ScenarioDocument d = ScenarioDocument.ReadDraft(root, cat, null, out DateTime _);
        Assert.IsNotNull(d);
        Assert.AreEqual(7f, d.Scenario.initial.speedKnots);
        Assert.AreEqual(doc.FilePath, d.FilePath);
        Assert.AreEqual("Бухта", d.Scenario.waterArea.name);
        ScenarioDocument.DeleteDraft(root);
        Assert.IsNull(ScenarioDocument.ReadDraft(root, cat, null, out DateTime _));
    }

    [Test]
    public void Check_StartFar_LegOverShoal_OwnShipTug()
    {
        ScenarioDefinition b2 = BuiltInB2(out ScenarioCatalog cat);
        AreaDocument ad = AreaDocument.New("Бухта");
        PolygonEditing.Add(ad.Area, AreaShapeKind.Shoal, new List<WorldPosition>
        {
            new WorldPosition(-100, 900), new WorldPosition(100, 900), new WorldPosition(100, 1100), new WorldPosition(-100, 1100)
        });
        ScenarioDocument doc = ScenarioDocument.New(ad.Area, b2.shipParameters, "МОЙ-01", "Мель", cat, null);
        ScenarioDefinition s = doc.Scenario;
        RouteEditing.Add(s.route, new WorldPosition(0, 0));
        RouteEditing.Add(s.route, new WorldPosition(0, 800));
        Assert.AreEqual(0, RouteEditing.Check(s).Count, "старт в первой точке; мели на пути ещё нет — " + string.Join("; ", RouteEditing.Check(s)));
        RouteEditing.Move(s.route, 1, new WorldPosition(0, 1000));   // в мель (2 м)
        s.initial.position = new WorldPosition(500, 0);
        List<string> w = RouteEditing.Check(s);
        Assert.AreEqual(2, w.Count, string.Join("; ", w));
        StringAssert.Contains("Старт в 500 м", w[0]);
        StringAssert.Contains("над глубиной 2", w[1]);

        // Свой буксир — модель 3 СС ставится сама
        s.motionModel = ScenarioMotionModel.Nomoto;
        doc.SetOwnShip(ScenarioShipType.Tug, b2.shipParameters);
        Assert.AreEqual(ScenarioShipType.Tug, doc.Scenario.ownShipType);
        Assert.AreEqual(ScenarioMotionModel.Manoeuvring3Dof, doc.Scenario.motionModel);
        Assert.IsTrue(doc.Undo());
        Assert.AreEqual(ScenarioShipType.Default, doc.Scenario.ownShipType);
    }
}
