// ============================================================
// ScenarioEditorTests.cs — Слой сценария в редакторе (запрос 21.4а, Core): документ сценария
// («с нуля», «на основе» встроенного, отмена, сохранение с историей, черновик), правка маршрута
// (имена, вставка, удаление, XTE от ручки, попадание мышью, курс старта), замечания редактора;
// 21.5а — другие суда (добавление, способ задания с сохранением места, встреча = TrafficPlanner, ромб TCPA, замечания, файл).
// 21.5б — блоки задания (швартовка вкл/выкл, работа буксира — расстановка как у 19-03, проверка причала, набор, файл).
// 21.6б — триггеры (добавить / копия / удалить, условия и действия по умолчанию, отмена, ручки круга, «мм:сс», ошибки
// и предупреждения «Проверки», ссылки за переименованием точки и целями).
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

    // ─────────── 21.4б ───────────
    [Test]
    public void CriteriaSwitches_OnlyDifferences_Effective_File_SessionMeta()
    {
        ScenarioDefinition b2 = BuiltInB2(out ScenarioCatalog cat);
        string root = TempRoot();
        ScenarioDocument doc = ScenarioDocument.BasedOn(b2, "МОЙ-01", cat, null);
        ScenarioDefinition s = doc.Scenario;
        Assert.AreEqual(0, s.criteriaSwitches.Count);

        Assert.IsTrue(CriteriaEditing.ToggleEnabled(s, "E5"));
        Assert.IsTrue(CriteriaEditing.Differs(s, "E5"));
        Assert.IsFalse(CriteriaEditing.Effective(s, "E5").enabled);
        Assert.IsTrue(CriteriaEditing.ToggleEnabled(s, "E5"));
        Assert.IsFalse(CriteriaEditing.Differs(s, "E5"), "вернули как в наборе — отличие убрано");

        Assert.IsTrue(CriteriaEditing.ToggleMandatory(s, "E2"));
        Assert.IsTrue(CriteriaEditing.Effective(s, "E2").mandatory);
        Assert.IsFalse(b2.EffectiveAssessment().Get("E2").mandatory, "встроенный не меняется");
        Assert.AreEqual(CriteriaEditing.Base(s, "E2").pass, CriteriaEditing.Effective(s, "E2").pass, "пороги — набора");
        Assert.IsTrue(CriteriaEditing.ToggleEnabled(s, "E2"));
        CriterionSetting e2 = CriteriaEditing.Effective(s, "E2");
        Assert.IsFalse(e2.enabled);
        Assert.IsFalse(e2.mandatory, "выключенный не бывает обязательным");
        Assert.IsFalse(CriteriaEditing.ToggleMandatory(s, "E2"));
        Assert.AreEqual(1, s.criteriaSwitches.Count);

        // Файл: только отличия; читается обратно; снимок session.json — с галочками
        RouteEditing.Move(s.route, 0, s.route[0].position);
        doc.Commit();
        string text = ScenarioFile.Write(s, cat);
        StringAssert.Contains("\"criteriaSwitches\"", text);
        ScenarioLoadResult r = ScenarioFile.Read(text, cat, null, "МОЙ-01");
        Assert.IsTrue(r.Ok, string.Join("; ", r.Errors));
        Assert.AreEqual(1, r.Scenario.criteriaSwitches.Count);
        Assert.IsFalse(r.Scenario.EffectiveAssessment().Get("E2").enabled);
        SessionMeta m = SessionMeta.Create(r.Scenario, b2.shipParameters, "", new DateTime(2026, 10, 18));
        Assert.IsFalse(m.assessment_settings.Get("E2").enabled);
        Assert.IsTrue(SessionMeta.Create(b2, b2.shipParameters, "", new DateTime(2026, 10, 18)).assessment_settings.Get("E2").enabled);
        Assert.AreEqual(AreaSaveStatus.Saved, doc.Save(root, false, new DateTime(2026, 10, 18, 11, 0, 0)), doc.LastMessage);
        ScenarioLibraryReport rep = ScenarioFolder.Load(root, cat);
        Assert.AreEqual(1, rep.Scenarios.Count, rep.Text(DateTime.Now));
        Assert.IsFalse(rep.Scenarios[0].EffectiveAssessment().Get("E2").enabled);

        // Другой набор, где E2 и так выключен, — отличие убирается; «Как в наборе»
        var off = ScriptableObject.CreateInstance<AssessmentCriteria>();
        off.name = "Assessment_Off";
        off.settings = AssessmentSettings.Default();
        off.settings.Get("E2").enabled = false;
        CriteriaEditing.SetBase(s, off);
        Assert.AreSame(off, s.assessment);
        Assert.AreEqual(0, s.criteriaSwitches.Count);
        Assert.IsFalse(CriteriaEditing.Effective(s, "E2").enabled);
        CriteriaEditing.ToggleEnabled(s, "E9");
        Assert.IsTrue(CriteriaEditing.Reset(s));
        Assert.AreEqual(0, s.criteriaSwitches.Count);
        (int en, int man) = CriteriaEditing.Count(s);
        Assert.AreEqual(AssessmentCatalog.All.Count - 1, en);
        Assert.Greater(man, 0);
    }

    [Test]
    public void Checks_ErrorsBlock_WarningsDoNot()
    {
        ScenarioDefinition b2 = BuiltInB2(out ScenarioCatalog cat);
        AreaDocument ad = AreaDocument.New("Бухта");
        ScenarioDefinition s = ScenarioDocument.New(ad.Area, b2.shipParameters, "МОЙ-05", "Проверка", cat, null).Scenario;
        AreaCheckResult c = ScenarioChecks.Check(s, cat);
        Assert.IsTrue(c.Errors.Exists(e => e.Contains("меньше двух")), string.Join("; ", c.Errors));
        Assert.IsTrue(c.Warnings.Exists(e => e.Contains("брифинга")), "пустой брифинг — предупреждение");

        RouteEditing.Add(s.route, new WorldPosition(0, 0));
        RouteEditing.Add(s.route, new WorldPosition(0, 2000));
        c = ScenarioChecks.Check(s, cat);
        Assert.AreEqual(0, c.Errors.Count, string.Join("; ", c.Errors));
        s.windKnots = 10f;   // ветер без 3 СС — предупреждение
        Assert.IsTrue(ScenarioChecks.Check(s, cat).Warnings.Exists(e => e.Contains("3 СС")));
        Assert.AreEqual(0, ScenarioChecks.Check(s, cat).Errors.Count, string.Join("; ", ScenarioChecks.Check(s, cat).Errors));

        RouteEditing.Move(s.route, 1, new WorldPosition(0, 9000));   // вне района ± 5 км
        Assert.IsTrue(ScenarioChecks.Check(s, cat).Errors.Exists(e => e.Contains("вне района")));
        RouteEditing.Move(s.route, 1, new WorldPosition(0, 2000));
        s.route[1].xteLimit = 0f;
        Assert.IsTrue(ScenarioChecks.Check(s, cat).Errors.Exists(e => e.Contains("XTE")));
        s.route[1].xteLimit = 150f;
        s.id = "B2-01";
        Assert.IsTrue(ScenarioChecks.Check(s, cat).Errors.Exists(e => e.Contains("встроенного")));
        s.id = "МОЙ-05";
        s.initial.position = new WorldPosition(7000, 0);
        Assert.IsTrue(ScenarioChecks.Check(s, cat).Errors.Exists(e => e.StartsWith("Начальная позиция")));
        Assert.IsFalse(ScenarioChecks.IsError("T1: начальная позиция вне района акватории."), "цели вне района — предупреждение");
    }

    [Test]
    public void Briefing_Template()
    {
        ScenarioDefinition b2 = BuiltInB2(out ScenarioCatalog cat);
        AreaDocument ad = AreaDocument.New("Бухта");
        ScenarioDefinition s = ScenarioDocument.New(ad.Area, b2.shipParameters, "МОЙ-01", "Бухта", cat, null).Scenario;
        RouteEditing.Add(s.route, new WorldPosition(0, 0));
        RouteEditing.Add(s.route, new WorldPosition(1000, 1000));
        s.route[1].narrowChannel = true;
        s.windKnots = 15f;
        s.windFromDeg = 90f;
        s.visibilityMeters = 926f;
        s.timeLimitSeconds = 1200f;
        CriteriaEditing.ToggleMandatory(s, "E2");
        string t = ScenarioChecks.Briefing(s);
        StringAssert.Contains("курс 0°, 10,0 уз", t);
        StringAssert.Contains("WP1 → WP2: курс 45°", t);
        StringAssert.Contains("правило 9", t);
        StringAssert.Contains("видимость 0,5 мили", t);
        StringAssert.Contains("ветер 15 уз с 90°", t);
        StringAssert.Contains("Лимит времени — 20 мин", t);
        StringAssert.Contains("E2 — ", t);
        s.timeLimitSeconds = 0f;
        StringAssert.Contains("Лимита времени нет", ScenarioChecks.Briefing(s));
    }

    // ─────────── 21.5а: другие суда ───────────
    [Test]
    public void Traffic_AddMove_PlacementKeepsPlace_Undo()
    {
        ScenarioDefinition b2 = BuiltInB2(out ScenarioCatalog cat);
        string before = ScenarioFile.Write(b2, cat);
        ScenarioDocument doc = ScenarioDocument.BasedOn(b2, "МОЙ-01", cat, null);
        ScenarioDefinition s = doc.Scenario;
        s.traffic.Clear();
        s.initial.position = new WorldPosition(0, 0);
        s.initial.headingDeg = 0f;
        s.initial.speedKnots = 10f;
        doc.Commit();
        OwnShipInitialState own = s.initial;

        Assert.AreEqual(0, TrafficEditing.Add(s.traffic, new WorldPosition(3000, 3000), own));
        TrafficShipDefinition d = s.traffic[0];
        Assert.AreEqual("T1", d.id);
        Assert.AreEqual("Цель-1", d.name);
        Assert.AreEqual(225f, d.headingDeg, "курс — на старт своего судна");
        Assert.AreEqual(TrafficPlacement.Absolute, d.placement);
        Assert.AreEqual(1, TrafficEditing.Add(s.traffic, new WorldPosition(-2000, 4000), own));
        Assert.AreEqual("T2", s.traffic[1].id);
        Assert.AreEqual("Цель-2", s.traffic[1].name);

        // Смена способа задания — цель на месте
        d.position = new WorldPosition(3000, 2500);
        d.headingDeg = 270f;
        WorldPosition p0 = TrafficEditing.StartOf(d, own);
        TrafficEditing.SetPlacement(d, own, TrafficPlacement.RelativeToOwnShip);
        Assert.AreEqual(0.0, (TrafficEditing.StartOf(d, own) - p0).Magnitude, 1e-3);
        Assert.AreEqual(Math.Sqrt(3000.0 * 3000.0 + 2500.0 * 2500.0) / 1852.0, d.distanceNm, 1e-5);
        TrafficEditing.SetPlacement(d, own, TrafficPlacement.MeetingPoint);
        Assert.AreEqual(0.0, (TrafficEditing.StartOf(d, own) - p0).Magnitude, 1.0, "встреча — по текущей геометрии");
        Assert.Greater(d.tcpaMinutes, 0f);
        Assert.Greater(d.cpaNm, 0f);
        Assert.IsFalse(TrafficEditing.MoveStart(d, own, new WorldPosition(0, 0)), "«встреча» — место считается само");
        TrafficEditing.SetPlacement(d, own, TrafficPlacement.Absolute);
        Assert.IsTrue(TrafficEditing.MoveStart(d, own, new WorldPosition(500, 600)));
        Assert.AreEqual(500.0, d.position.East, 1e-9);
        // «пеленг и дистанция» — от старта своего судна
        TrafficEditing.SetPlacement(d, own, TrafficPlacement.RelativeToOwnShip);
        Assert.IsTrue(TrafficEditing.MoveStart(d, own, new WorldPosition(0, 1852)));
        Assert.AreEqual(0f, d.bearingDeg, 1e-4f);
        Assert.AreEqual(1f, d.distanceNm, 1e-5f);

        Assert.AreEqual(0, TrafficEditing.AddPoint(d, new WorldPosition(10, 20)));
        Assert.IsTrue(doc.Commit());
        Assert.IsTrue(TrafficEditing.Remove(s.traffic, 1));
        Assert.AreEqual("T2", TrafficEditing.NextId(s.traffic), "номер освободился");
        doc.Commit();
        Assert.IsTrue(doc.Undo());
        Assert.AreEqual(2, doc.Scenario.traffic.Count);
        Assert.AreEqual(TrafficPlacement.RelativeToOwnShip, doc.Scenario.traffic[0].placement);
        Assert.AreEqual(1, doc.Scenario.traffic[0].route.Count);
        Assert.IsTrue(doc.Undo());
        Assert.AreEqual(0, doc.Scenario.traffic.Count);
        Assert.AreEqual(before, ScenarioFile.Write(b2, cat), "встроенный не меняется");
    }

    [Test]
    public void Traffic_Encounter_MatchesPlanner_MeetHandle_Hit()
    {
        var own = new OwnShipInitialState { position = new WorldPosition(100, 200), headingDeg = 0f, speedKnots = 12f };
        var d = new TrafficShipDefinition
        {
            placement = TrafficPlacement.MeetingPoint, cpaNm = 0.5f, tcpaMinutes = 12f, meetingSide = MeetingSide.Starboard,
            headingDeg = 270f, speedKnots = 10f
        };
        TrafficEncounter e = TrafficEditing.Meet(d, own);
        Assert.IsTrue(e.Closing);
        Assert.AreEqual(0.5, e.CpaNm, 1e-6);
        Assert.AreEqual(12.0, e.TcpaMin, 1e-6);
        TrafficStart st = TrafficPlanner.Resolve(d, own);
        Assert.AreEqual(st.Position.East, e.Start.Position.East, 1e-9, "тот же расчёт, что в тренажёре");
        Assert.AreEqual(st.MeetingPoint.East, e.TargetAtCpa.East, 1e-6);
        Assert.AreEqual(st.MeetingPoint.North, e.TargetAtCpa.North, 1e-6);
        double v = 12.0 * ShipParameters.KnotsToMs;
        Assert.AreEqual(200.0 + v * 720.0, e.OwnAtCpa.North, 1e-6);
        Assert.AreEqual(0.5 * 1852.0, (e.TargetAtCpa - e.OwnAtCpa).Magnitude, 1e-6, "концы отрезка CPA");
        Assert.AreEqual(MeetingSide.Starboard, TrafficEditing.SideAt(own, e.TargetAtCpa - e.OwnAtCpa));

        // Ромб TCPA — на пути своего судна; тянуть: проекция на курс, шаг 0,5 мин
        WorldPosition h = TrafficEditing.MeetHandle(d, own);
        Assert.AreEqual(e.OwnAtCpa.North, h.North, 1e-6);
        Assert.AreEqual(12f, TrafficEditing.TcpaFrom(own, h + new WorldPosition(30, 0)));
        Assert.AreEqual(6f, TrafficEditing.TcpaFrom(own, own.position + new WorldPosition(0, v * 360.0 + 20.0)));
        Assert.AreEqual(0.5f, TrafficEditing.TcpaFrom(own, own.position - new WorldPosition(0, 500)), "не меньше шага");
        Assert.AreEqual(-1f, TrafficEditing.TcpaFrom(new OwnShipInitialState { speedKnots = 0f }, h), "своё судно стоит");

        // Попадание мышью: 10 м/пикс., (0, 0) — пиксель (400, 300)
        MapViewport vp = View(10.0);
        var list = new List<TrafficShipDefinition> { d };
        vp.ToPixel(e.Start.Position, out double x, out double y);
        Assert.AreEqual(0, TrafficEditing.HitShip(list, own, vp, x + 5, y + 5));
        Assert.AreEqual(-1, TrafficEditing.HitShip(list, own, vp, x + 20, y));
        vp.ToPixel(h, out double hx, out double hy);
        Assert.IsTrue(TrafficEditing.HitMeet(d, own, vp, hx + 3, hy));
        d.placement = TrafficPlacement.Absolute;
        Assert.IsFalse(TrafficEditing.HitMeet(d, own, vp, hx, hy), "ромб — только у «встречи»");
    }

    [Test]
    public void Traffic_Checks_CloseStart_Cpa_TurnBeforeTcpa_File()
    {
        ScenarioDefinition b2 = BuiltInB2(out ScenarioCatalog cat);
        AreaDocument ad = AreaDocument.New("Бухта");
        ScenarioDocument doc = ScenarioDocument.New(ad.Area, b2.shipParameters, "МОЙ-01", "Встреча", cat, null);
        ScenarioDefinition s = doc.Scenario;
        s.initial.position = new WorldPosition(0, 0);
        s.initial.headingDeg = 0f;
        s.initial.speedKnots = 10f;
        s.route.Clear();
        RouteEditing.Add(s.route, new WorldPosition(0, 0));
        RouteEditing.Add(s.route, new WorldPosition(0, 1852));   // поворот у WP2 через 6 мин
        Assert.AreEqual(0, TrafficEditing.Check(s).Count, "судов нет");
        s.traffic.Add(new TrafficShipDefinition
        {
            id = "T1", name = "Пересекающее", placement = TrafficPlacement.MeetingPoint, cpaNm = 0.2f, tcpaMinutes = 12f,
            meetingSide = MeetingSide.Port, headingDeg = 90f, speedKnots = 10f
        });
        List<string> w = TrafficEditing.Check(s);
        Assert.AreEqual(1, w.Count, "маршрут без поворота — только CPA: " + string.Join("; ", w));
        StringAssert.Contains("CPA 0,20 мили < CPA_доп 0,50", w[0]);
        s.traffic[0].requiredCpaNm = 0.1f;
        Assert.AreEqual(0, TrafficEditing.Check(s).Count, "CPA_доп цели 0,1 мили");

        RouteEditing.Add(s.route, new WorldPosition(1852, 1852));
        s.traffic.Add(new TrafficShipDefinition { id = "T2", name = "Попутное", position = new WorldPosition(100, 300), headingDeg = 0f, speedKnots = 10f });
        w = TrafficEditing.Check(s);
        Assert.AreEqual(3, w.Count, string.Join("; ", w));
        StringAssert.Contains("поворачивает у «WP2» через 6,0 мин", w[0]);
        StringAssert.Contains("Судно T2 «Попутное»: на старте в 0,17 мили", w[1]);
        StringAssert.Contains("встречи не будет", w[2]);
        Assert.IsTrue(ScenarioChecks.Check(s, cat).Warnings.Contains(w[1]), "в общей проверке редактора");

        // Файл: цели — как заданы
        string text = ScenarioFile.Write(s, cat);
        ScenarioLoadResult r = ScenarioFile.Read(text, cat, new[] { ad.Area }, "МОЙ-01");
        Assert.IsTrue(r.Ok, string.Join("; ", r.Errors));
        Assert.AreEqual(2, r.Scenario.traffic.Count);
        TrafficShipDefinition t1 = r.Scenario.traffic[0];
        Assert.AreEqual(TrafficPlacement.MeetingPoint, t1.placement);
        Assert.AreEqual(MeetingSide.Port, t1.meetingSide);
        Assert.AreEqual(0.2f, t1.cpaNm);
        Assert.AreEqual(0.1f, t1.requiredCpaNm);
        Assert.AreEqual(ScenarioFile.Write(s, cat), ScenarioFile.Write(r.Scenario, cat), "бит в бит");
    }

    // ─────────── 21.5б: блоки задания ───────────
    private static ShipParameters Tug()
    {
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        p.name = "ShipParameters_Tug";
        ShipPresets.FillTug(p);
        return p;
    }

    [Test]
    public void Task_Mooring_OnOff_EndsMotionBerth_SetAndErrors()
    {
        ScenarioDefinition b2 = BuiltInB2(out ScenarioCatalog cat);
        ScenarioDocument doc = ScenarioDocument.BasedOn(b2, "МОЙ-01", cat, null);
        ScenarioDefinition s = doc.Scenario;
        Assert.IsFalse(s.IsMooring);
        Assert.IsTrue(s.Ends(ScenarioEndConditions.Arrival));
        Assert.IsNull(TaskEditing.SuggestedSet(s), "только маршрут — набор не подставляется");
        s.motionModel = ScenarioMotionModel.Nomoto;

        TaskEditing.SetMooring(s, true);
        Assert.IsTrue(s.IsMooring);
        Assert.AreEqual(ScenarioMotionModel.Manoeuvring3Dof, s.motionModel);
        Assert.IsTrue(s.Ends(ScenarioEndConditions.Moored));
        Assert.IsFalse(s.Ends(ScenarioEndConditions.Arrival), "прибытие завершило бы раньше швартовки");
        Assert.IsNotNull(TaskEditing.Berth(s.waterArea, s.mooring.berthIndex));
        Assert.IsFalse(s.Validate().Exists(p => p.StartsWith("Швартовка", StringComparison.Ordinal)), string.Join("; ", s.Validate()));
        Assert.AreEqual(TaskEditing.BerthingSet, TaskEditing.SuggestedSet(s));
        s.tugAvailable = true;
        Assert.AreEqual(TaskEditing.TugBerthingSet, TaskEditing.SuggestedSet(s));
        StringAssert.Contains("Швартовка к причалу «", ScenarioChecks.Briefing(s));

        int n = TaskEditing.BerthCount(s.waterArea);
        Assert.AreEqual(2, n, "демо-акватория: причалы №1 и №2");
        Assert.AreEqual(0, TaskEditing.NextBerth(s.waterArea, n - 1, 1));
        Assert.AreEqual(n - 1, TaskEditing.NextBerth(s.waterArea, 0, -1));
        Assert.AreEqual(0, TaskEditing.NextBerth(s.waterArea, 99, 1), "нет такого — первый");

        s.mooring.berthIndex = 99;   // причал удалён из акватории — ошибка, не запустить
        Assert.IsTrue(ScenarioChecks.Check(s, cat).Errors.Exists(p => p.StartsWith("Швартовка: в акватории нет причала", StringComparison.Ordinal)));
        doc.Commit();
        TaskEditing.SetMooring(s, false);
        Assert.IsFalse(s.Ends(ScenarioEndConditions.Moored));
        Assert.IsTrue(s.Ends(ScenarioEndConditions.Arrival), "прибытие вернулось");
        doc.Commit();
        Assert.IsTrue(doc.Undo());
        Assert.IsTrue(doc.Scenario.IsMooring, "отмена");
    }

    [Test]
    public void Task_TugWork_PlacementAsDemo_Checks_File()
    {
        ScenarioDefinition b2 = BuiltInB2(out ScenarioCatalog cat);
        ShipParameters tug = Tug(), mariner = LowSpeedModelTests.Mariner();
        cat.Add(tug);
        cat.Add(mariner);
        var demo = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoTugWorkScenario.Fill(demo, b2.waterArea, tug, mariner, null);

        ScenarioDocument doc = ScenarioDocument.BasedOn(b2, "МОЙ-01", cat, null);
        ScenarioDefinition s = doc.Scenario;
        s.tugWork.berthIndex = DemoWaterArea.Berth2Index;
        TaskEditing.SetMooring(s, true);
        TaskEditing.SetTugWork(s, true, tug, mariner);
        Assert.IsTrue(s.IsTugWork);
        Assert.IsFalse(s.IsMooring, "швартовка и работа буксира — не вместе");
        Assert.AreEqual(ScenarioShipType.Tug, s.ownShipType);
        Assert.AreSame(tug, s.shipParameters);
        Assert.AreSame(mariner, s.tugWorkClient);
        Assert.AreEqual(ScenarioMotionModel.Manoeuvring3Dof, s.motionModel);
        Assert.IsTrue(s.Ends(ScenarioEndConditions.TugWorkDone));
        Assert.IsFalse(s.Ends(ScenarioEndConditions.Arrival) || s.Ends(ScenarioEndConditions.Moored));
        Assert.AreEqual(TaskEditing.TugWorkSet, TaskEditing.SuggestedSet(s));

        // Старт буксира и линия «Старт → Борт судна» — как у встроенного 19-03 (тот же расчёт, что в тренажёре)
        Assert.AreEqual(0.0, (s.initial.position - demo.initial.position).Magnitude, 1e-6);
        Assert.AreEqual(demo.initial.headingDeg, s.initial.headingDeg, 1e-3f);
        Assert.AreEqual(0f, s.initial.speedKnots);
        Assert.AreEqual(2, s.route.Count);
        Assert.AreEqual(TaskEditing.ContactPointName, s.route[1].name);
        Assert.AreEqual(0.0, (s.route[1].position - demo.route[1].position).Magnitude, 1e-6);
        TugWorkLayout l = TaskEditing.Layout(s);
        Assert.IsTrue(l.Ok);
        Assert.AreEqual(0, TaskEditing.Check(s).Count, string.Join("; ", TaskEditing.Check(s)));

        // Клиент дальше от причала на 20 м — буксир на старте тоже на 20 м дальше
        WorldPosition before = s.initial.position;
        s.tugWork.clientGapM += 20f;
        Assert.IsTrue(TaskEditing.PlaceTugWork(s));
        Assert.AreEqual(20.0, (s.initial.position - before).Magnitude, 1e-6);
        s.initial.position = s.initial.position + new WorldPosition(50, 0);
        StringAssert.Contains("не совпадает с расстановкой", string.Join("; ", TaskEditing.Check(s)));
        TaskEditing.PlaceTugWork(s);

        string text = ScenarioChecks.Briefing(s);
        StringAssert.Contains("Вы — на буксире", text);
        StringAssert.DoesNotContain("пройти по путевым точкам", text);

        // Файл — бит в бит
        string file = ScenarioFile.Write(s, cat);
        ScenarioLoadResult r = ScenarioFile.Read(file, cat, null, "МОЙ-01");
        Assert.IsTrue(r.Ok, string.Join("; ", r.Errors));
        Assert.IsTrue(r.Scenario.IsTugWork);
        Assert.AreEqual(s.tugWork.clientGapM, r.Scenario.tugWork.clientGapM);
        Assert.AreEqual(file, ScenarioFile.Write(r.Scenario, cat));

        // Своё судно не буксир — ошибка
        s.ownShipType = ScenarioShipType.Default;
        Assert.IsTrue(ScenarioChecks.Check(s, cat).Errors.Exists(p => p.StartsWith("Буксир: упражнение на буксире", StringComparison.Ordinal)));
        TaskEditing.SetTugWork(s, false, null, null);
        Assert.IsFalse(s.Ends(ScenarioEndConditions.TugWorkDone));
        Assert.IsTrue(s.Ends(ScenarioEndConditions.Arrival));
    }

    [Test]
    public void Task_BerthFits_Depth_Length()
    {
        ShipParameters p = LowSpeedModelTests.Model45m();   // 45 м, осадка 3 м
        var b = new BerthArea { a = new WorldPosition(0, 0), b = new WorldPosition(100, 0), depthM = 5f };
        Assert.IsNull(TaskEditing.Fits(b, p));
        b.depthM = 3.5f;
        StringAssert.Contains("меньше осадки 3,0 м + 1 м", TaskEditing.Fits(b, p));
        b.depthM = 5f;
        b.b = new WorldPosition(40, 0);
        StringAssert.Contains("причал 40 м короче судна 45 м", TaskEditing.Fits(b, p));
        Assert.IsNull(TaskEditing.Fits(null, p));
    }

    // ─────────── 21.6б: триггеры в редакторе ───────────
    [Test]
    public void Triggers_AddCopyRemove_ConditionAndActionDefaults_Undo()
    {
        ScenarioDefinition b2 = BuiltInB2(out ScenarioCatalog cat);
        ScenarioDocument doc = ScenarioDocument.BasedOn(b2, "МОЙ-01", cat, null);
        ScenarioDefinition s = doc.Scenario;
        Assert.AreEqual(0, TriggerEditing.Add(s));
        ScenarioTrigger t = s.triggers[0];
        Assert.AreEqual("Тр1", t.id);
        Assert.AreEqual(TriggerCondition.Time, t.condition);
        Assert.AreEqual(60f, t.timeSeconds);
        Assert.AreEqual(TriggerActionKind.Message, t.actions[0].kind);
        Assert.IsTrue(doc.Commit());

        TriggerEditing.SetCondition(s, t, TriggerCondition.EnterCircle);
        Assert.AreEqual(0.0, t.center.East, 1e-9);
        Assert.AreEqual(2100.0, t.center.North, 1e-9, "середина маршрута 9,2 км — на втором участке");
        Assert.AreEqual(300f, t.radiusM);
        TriggerEditing.SetCondition(s, t, TriggerCondition.Waypoint);
        Assert.AreEqual("Вход в фарватер", t.waypoint, "первая точка после старта");
        CollectionAssert.AreEqual(new[] { "Вход в фарватер", "Поворот", "Выход из фарватера" }, TriggerEditing.WaypointChoices(s));
        Assert.AreEqual("Выход из фарватера", TriggerEditing.Cycle(TriggerEditing.WaypointChoices(s), t.waypoint, -1));

        int c = TriggerEditing.Duplicate(s, 0);
        Assert.AreEqual(1, c);
        Assert.AreEqual("Тр2", s.triggers[1].id);
        s.triggers[1].actions[0].text = "другое";
        Assert.AreEqual(TriggerEditing.DefaultMessage, t.actions[0].text, "действия копии — свои");
        TriggerEditing.SetCondition(s, s.triggers[1], TriggerCondition.AfterTrigger);
        Assert.AreEqual("Тр1", s.triggers[1].afterTrigger);
        CollectionAssert.AreEqual(new[] { "Тр1" }, TriggerEditing.TriggerChoices(s, s.triggers[1]));
        TriggerEditing.SetCondition(s, s.triggers[1], TriggerCondition.TargetRange);
        Assert.AreEqual("", s.triggers[1].target, "целей нет");

        int a = TriggerEditing.AddAction(s, t, TriggerActionKind.Visibility);
        Assert.AreEqual(1, a);
        Assert.AreEqual(1f, t.actions[1].visibilityNm, "видимость по умолчанию — 1 миля");
        TriggerEditing.SetActionKind(s, t.actions[1], TriggerActionKind.Wind);
        Assert.AreEqual(15f, t.actions[1].windKnots);
        Assert.IsTrue(TriggerEditing.RemoveAction(t, 1));
        Assert.IsFalse(TriggerEditing.RemoveAction(t, 5));
        Assert.IsTrue(doc.Commit());
        Assert.IsTrue(TriggerEditing.Remove(s, 1));
        Assert.IsTrue(doc.Commit());
        Assert.AreEqual(1, doc.Scenario.triggers.Count);
        Assert.IsTrue(doc.Undo());
        Assert.AreEqual(2, doc.Scenario.triggers.Count, "отмена — триггер вернулся");
        Assert.AreEqual(TriggerCondition.Waypoint, doc.Scenario.triggers[0].condition);
    }

    [Test]
    public void Triggers_MapHits_Radius_Clock()
    {
        ScenarioDefinition s = BuiltInB2(out _);
        MapViewport v = View(1.0);
        int i = TriggerEditing.Add(s);
        ScenarioTrigger t = s.triggers[i];
        t.condition = TriggerCondition.EnterCircle;
        t.center = new WorldPosition(0, 0);
        t.radiusM = 100f;
        v.ToPixel(t.center, out double cx, out double cy);
        Assert.AreEqual(0, TriggerEditing.HitCenter(s, v, cx + 5, cy));
        Assert.AreEqual(-1, TriggerEditing.HitCenter(s, v, cx + 15, cy));
        Assert.IsTrue(TriggerEditing.HitEdge(s, 0, v, cx + 100 + 4, cy), "ручка радиуса — к востоку");
        Assert.IsFalse(TriggerEditing.HitEdge(s, 0, v, cx - 100, cy));
        Assert.AreEqual(0, TriggerEditing.HitRing(s, v, cx, cy + 103), "окружность ± 6 пикс.");
        Assert.AreEqual(-1, TriggerEditing.HitRing(s, v, cx, cy + 50));
        Assert.AreEqual(140f, TriggerEditing.RadiusFrom(t, new WorldPosition(137, 0), 10.0));
        Assert.AreEqual(10f, TriggerEditing.RadiusFrom(t, new WorldPosition(2, 0), 10.0), "не меньше 10 м");
        t.condition = TriggerCondition.Time;
        Assert.AreEqual(-1, TriggerEditing.HitCenter(s, v, cx, cy), "у другого условия круга нет");

        Assert.IsTrue(TriggerEditing.ParseClock("1:30", out double sec)); Assert.AreEqual(90.0, sec);
        Assert.IsTrue(TriggerEditing.ParseClock("5", out sec)); Assert.AreEqual(300.0, sec, "число — минуты");
        Assert.IsTrue(TriggerEditing.ParseClock("1,5", out sec)); Assert.AreEqual(90.0, sec);
        Assert.IsTrue(TriggerEditing.ParseClock("1:02:03", out sec)); Assert.AreEqual(3723.0, sec);
        Assert.IsFalse(TriggerEditing.ParseClock("0:75", out _));
        Assert.IsFalse(TriggerEditing.ParseClock("", out _));
        Assert.IsFalse(TriggerEditing.ParseClock("-1", out _));
    }

    [Test]
    public void Triggers_Checks_ErrorsBlock_WarningsDoNot()
    {
        ScenarioDefinition b2 = BuiltInB2(out ScenarioCatalog cat);
        ScenarioDefinition s = ScenarioDocument.BasedOn(b2, "МОЙ-07", cat, null).Scenario;   // копия: id встроенного не трогаем
        s.traffic.Clear();
        Assert.AreEqual(0, ScenarioChecks.Check(s, cat).Errors.Count, string.Join("; ", ScenarioChecks.Check(s, cat).Errors));
        s.traffic.Add(new TrafficShipDefinition { id = "T1", placement = TrafficPlacement.Absolute, position = new WorldPosition(1500, 0), speedKnots = 6f,
                                                  motion = TrafficMotion.Route, route = new List<WorldPosition> { new WorldPosition(1500, 3000) } });
        // Ошибки: нет цели, пустое сообщение, цикл
        ScenarioTrigger a = s.triggers[TriggerEditing.Add(s)];
        a.actions.Add(new TriggerAction { kind = TriggerActionKind.TargetOrder, target = "T9", speedKnots = 5f });
        AreaCheckResult c = ScenarioChecks.Check(s, cat);
        Assert.IsTrue(c.Errors.Exists(e => e.StartsWith("Триггер Тр1: нет цели «T9»")), string.Join("; ", c.Errors));
        a.actions[1].target = "T1";
        a.actions[0].text = " ";
        Assert.IsTrue(ScenarioChecks.Check(s, cat).Errors.Exists(e => e.Contains("пустое сообщение")));
        a.actions[0].text = "ок";
        ScenarioTrigger b = s.triggers[TriggerEditing.Add(s)];
        a.condition = TriggerCondition.AfterTrigger; a.afterTrigger = "Тр2";
        b.condition = TriggerCondition.AfterTrigger; b.afterTrigger = "Тр1";
        Assert.IsTrue(ScenarioChecks.Check(s, cat).Errors.Exists(e => e.Contains("замкнута по кругу")));
        b.condition = TriggerCondition.Time;
        Assert.AreEqual(0, ScenarioChecks.Check(s, cat).Errors.Count, string.Join("; ", ScenarioChecks.Check(s, cat).Errors));

        // Предупреждения: курс цели «по маршруту», круг не на маршруте, старт в круге, вне района, время позже лимита
        a.actions[1].courseDeg = 90f;
        b.condition = TriggerCondition.EnterCircle; b.center = new WorldPosition(2000, 0); b.radiusM = 300f;
        b.timeSeconds = 5000f;
        c = ScenarioChecks.Check(s, cat);
        Assert.AreEqual(0, c.Errors.Count, string.Join("; ", c.Errors));
        Assert.IsTrue(c.Warnings.Exists(w => w.Contains("заданный курс снимет маршрут")));
        Assert.IsTrue(c.Warnings.Exists(w => w.Contains("круг не пересекает маршрут")));
        b.center = s.initial.position;
        Assert.IsTrue(ScenarioChecks.Check(s, cat).Warnings.Exists(w => w.Contains("старт уже в круге")));
        b.center = new WorldPosition(20000, 0);
        c = ScenarioChecks.Check(s, cat);
        Assert.IsTrue(c.Warnings.Exists(w => w.Contains("центр круга вне района")) && c.Errors.Count == 0, string.Join("; ", c.Errors));
        b.condition = TriggerCondition.Time;
        Assert.IsTrue(ScenarioChecks.Check(s, cat).Warnings.Exists(w => w.Contains("не раньше лимита")));
        Assert.IsTrue(ScenarioChecks.IsError("Триггер Тр3: нет действий."));
        Assert.IsFalse(ScenarioChecks.IsError("Триггер Тр3: ветер действует только в модели 3 СС."));
    }

    [Test]
    public void Triggers_ReferencesFollowRenameAndRemoval()
    {
        ScenarioDefinition s = BuiltInB2(out _);
        s.traffic.Add(new TrafficShipDefinition { name = "A", placement = TrafficPlacement.Absolute, position = new WorldPosition(1500, 0) });
        s.traffic.Add(new TrafficShipDefinition { name = "B", placement = TrafficPlacement.Absolute, position = new WorldPosition(1500, 1000) });
        ScenarioTrigger t = s.triggers[TriggerEditing.Add(s)];
        t.condition = TriggerCondition.Waypoint; t.waypoint = "Поворот";
        t.actions.Add(new TriggerAction { kind = TriggerActionKind.TargetOrder, target = "T2", speedKnots = 5f });
        ScenarioTrigger u = s.triggers[TriggerEditing.Add(s)];
        u.condition = TriggerCondition.TargetRange; u.target = "T1";

        Assert.AreEqual(1, TriggerEditing.RenameWaypoint(s, "Поворот", "WP-поворот"));
        Assert.AreEqual("WP-поворот", t.waypoint);
        // id первой цели задали — ссылка «T1» следует за ней
        List<string> before = TriggerEditing.TargetIds(s);
        s.traffic[0].id = "КАТЕР";
        Assert.AreEqual(1, TriggerEditing.RemapTargets(s, before, TriggerEditing.TargetIds(s)));
        Assert.AreEqual("КАТЕР", u.target);
        // удалили первую: вторая (без id) стала «T1» — ссылка «T2» следует; ссылка на удалённую — «(удалено)», ошибка
        before = TriggerEditing.TargetIds(s);
        s.traffic.RemoveAt(0);
        List<string> after = TriggerEditing.TargetIds(s);
        after.Insert(0, null);
        TriggerEditing.RemapTargets(s, before, after);
        Assert.AreEqual("T1", t.actions[1].target);
        Assert.AreEqual("КАТЕР (удалено)", u.target);
        Assert.IsTrue(s.Validate().Exists(p => p.Contains("нет цели «КАТЕР (удалено)»")));
    }
}
