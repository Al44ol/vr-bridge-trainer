// ============================================================
// TugWorkSceneTests.cs — Упражнение 19-03 на своём буксире: тип судна сценария, запись, отчёт (запрос 19.4б-2, Core)
//
// Сценарий 19-03 требует своего буксира (прежние — нет); прогон «за обучающегося» с записью: mooring.csv — по клиенту
// и своему буксиру, клиент «CLIENT» в targets.csv, session.json → scenario.tug_work и summary.tug_work_*; оценка
// в шлеме = по записи, клиент — не цель расхождения; report.html — раздел «Работа буксира»; «Задание» планшета.
// ============================================================
using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class TugWorkSceneTests
{
    private static string dir;
    private static TugWorkSimulation sim;
    private static RecordedSession session;

    private static ShipParameters Tug()
    {
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        p.name = "ShipParameters_Tug";
        ShipPresets.FillTug(p);
        return p;
    }

    private static ScenarioDefinition Scenario()
    {
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        var crit = ScriptableObject.CreateInstance<AssessmentCriteria>();
        crit.settings = DemoTugWorkScenario.Assessment();
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoTugWorkScenario.Fill(s, area, Tug(), LowSpeedModelTests.Mariner(), crit);   // 19.4б-3: клиент — Mariner
        return s;
    }

    // Один прогон с записью на все тесты (≈ 6 мин упражнения, шаг 0,02 с)
    private static RecordedSession Session()
    {
        if (session != null) return session;
        dir = Path.Combine(Path.GetTempPath(), "VRBridgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        sim = new TugWorkSimulation { Scenario = Scenario(), RecordFolder = dir };
        sim.Run();
        session = SessionReader.Load(dir);
        return session;
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        if (dir != null) { try { Directory.Delete(dir, true); } catch (Exception) { } }
        dir = null; sim = null; session = null;
    }

    [Test]
    public void Scenario_19_03_RequiresOwnTug_OthersDoNot()
    {
        ScenarioDefinition s = Scenario();
        Assert.IsTrue(s.RequiresOwnTug);
        Assert.AreEqual(ScenarioShipType.Tug, s.ownShipType);
        CollectionAssert.IsEmpty(s.Validate());

        var berth = ScriptableObject.CreateInstance<ScenarioDefinition>();
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        DemoBerthScenario.Fill(berth, area, LowSpeedModelTests.Model45m(), null);
        Assert.IsFalse(berth.RequiresOwnTug, "прежние сценарии — модель 45 м");
        Assert.AreEqual(ScenarioShipType.Default, ScriptableObject.CreateInstance<ScenarioDefinition>().ownShipType, "по умолчанию — как обычно");

        s.ownShipType = ScenarioShipType.Default;
        Assert.IsTrue(s.Validate().Exists(p => p.Contains("свой буксир")), "упражнение на буксире без типа судна «буксир» — замечание");
    }

    [Test]
    public void Recording_MooringRowsByClient_OwnTugColumns_ClientTarget()
    {
        RecordedSession s = Session();
        Assert.AreEqual(ScenarioEndReason.TugWorkDone, sim.EndReason);
        Assert.Greater(s.Track.Count, 100);
        Assert.AreEqual(s.Track.Count, s.Mooring.Count, "строка mooring.csv на каждую строку track.csv");
        Assert.IsTrue(s.Mooring.TrueForAll(m => m.HasBerth && m.Towing.Present && m.Towing.Station == TugStation.Midship), "клиент у причала, свой буксир у миделя");
        Assert.IsTrue(s.Mooring.TrueForAll(m => Math.Abs(m.Towing.WindKn - 15.0) < 1e-9), "ветер в каждой строке");
        Assert.IsTrue(s.Mooring.Exists(m => m.Towing.Mode == TugMode.Push && m.Towing.PushKn > 10.0 && Math.Abs(m.Towing.DirectionDeg - 270.0) < 1e-9),
            "упор кранцем в правый борт — к причалу (270° от ДП клиента)");
        Assert.IsTrue(s.Mooring.Exists(m => m.Towing.PowerPct > 5.0), "доля тяги колонок");
        Assert.IsTrue(s.Mooring.Exists(m => m.FenderContact), "клиент на кранцах");
        Assert.IsTrue(s.Mooring.Exists(m => m.Lines != null && CountFast(m) >= 4), "концы клиента поданы (4)");
        MooringSample last = s.Mooring[s.Mooring.Count - 1];
        Assert.AreEqual(TugWorkSystem.StageCount, last.StagesDone, "stages_done — задания буксира");
        Assert.GreaterOrEqual(last.MooredHoldS, sim.Scenario.tugWork.securedHoldS - 0.3, "moored_hold_s — «концы закреплены» клиента");
        Assert.IsTrue(s.Targets.Exists(t => t.Id == ClientShip.Id), "клиент — «CLIENT» в targets.csv");
        Assert.IsTrue(s.Events.Exists(e => e.Type == ExerciseEventType.BerthingImpact), "навал клиента — в events.csv");
    }

    private static int CountFast(MooringSample m)
    {
        int n = 0;
        foreach (MooringLineSample l in m.Lines) if (l.Fast) n++;
        return n;
    }

    [Test]
    public void SessionJson_TugWorkAndSummary()
    {
        RecordedSession s = Session();
        SessionScenario sc = s.Meta.scenario;
        Assert.AreEqual("Tug", sc.own_ship_type);
        Assert.IsTrue(sc.tug_work.enabled);
        Assert.AreEqual(ClientShip.Id, sc.tug_work.client_id);
        Assert.AreEqual(160.93, sc.tug_work.client_length_m, 1e-4);
        TugWorkDefinition d = sc.tug_work.ToDefinition(), src = sim.Scenario.tugWork;
        Assert.AreEqual(src.berthIndex, d.berthIndex);
        Assert.AreEqual(src.clientGapM, d.clientGapM);
        Assert.AreEqual(src.contactZoneM, d.contactZoneM);
        Assert.AreEqual(src.securedHoldS, d.securedHoldS);
        Assert.AreEqual(src.clearDistanceM, d.clearDistanceM);
        Assert.IsFalse(sc.mooring.enabled);
        Assert.AreEqual(TugWorkSystem.StageCount, s.Meta.summary.tug_work_stages_done);
        Assert.AreEqual(Math.Round(sim.System.DoneAt(TugWorkStage.Clear), 2), s.Meta.summary.tug_work_done_s, 1e-6);
        Assert.AreEqual(ScenarioEndReason.TugWorkDone.ToString(), s.Meta.end_reason);

        var other = ScriptableObject.CreateInstance<ScenarioDefinition>();
        Assert.IsFalse(SessionScenario.From(other).tug_work.enabled, "у прежних сценариев — выключено");
        Assert.IsNull(SessionScenario.From(other).tug_work.ToDefinition());
    }

    [Test]
    public void Assessment_LiveEqualsOffline_ClientNotAnEncounter()
    {
        RecordedSession s = Session();
        AssessmentResult offline = SessionAssessment.Assess(s);
        Assert.AreEqual(sim.LiveResult.ToCsv(), offline.ToCsv(), "оценка в шлеме = по записи");
        Assert.IsFalse(offline.Encounters.Exists(e => e.TargetId.StartsWith(ClientShip.Id, StringComparison.Ordinal)), "клиент — не сближение");
        Assert.IsTrue(WorkVessels.Is(ClientShip.Id) && WorkVessels.Is(TugTarget.Id) && !WorkVessels.Is("T1"));

        // Без цели клиента — та же оценка
        var bare = SessionReader.Load(dir);
        bare.Targets.Clear();
        Assert.AreEqual(offline.ToCsv(), SessionAssessment.Assess(bare).ToCsv(), "клиент не влияет на оценку расхождения");
    }

    [Test]
    public void Report_TugWorkSection_MapBerthView_TaskPage()
    {
        RecordedSession s = Session();
        AssessmentResult r = SessionAssessment.Assess(s);
        DebriefMapData map = DebriefMap.Build(s, r);
        string html = SessionReport.Build(s, r, map);
        StringAssert.Contains("Работа буксира</h2>", html);
        StringAssert.Contains("«Буксир, к правому борту на миделе, упирайтесь»", html);
        StringAssert.Contains("«Толкайте к причалу»", html);
        StringAssert.Contains("«Держите, заводим концы»", html);
        StringAssert.Contains("«Концы закреплены, отходите»", html);
        StringAssert.Contains("4. Отойти от борта", html);
        StringAssert.Contains("ход судна ≤ 0,1 уз 30 с", html);
        StringAssert.Contains("кранцем, правый борт", html);
        StringAssert.Contains("Судно-клиент у причала</h2>", html);
        StringAssert.DoesNotContain("<h2>Швартовка</h2>", html);
        StringAssert.Contains("Своё судно — буксир (раздел «Работа буксира»)", html);
        Assert.IsTrue(map.Views.Exists(v => v.Key == "berth"), "вид «У причала»");

        // «Задание» планшета: задания буксира с отметками и последняя команда лоцмана
        string page = ExerciseTasks.Page(sim.Scenario, TaskPhase.Running, 100.0, null, null, sim.System);
        StringAssert.Contains("Задания (по командам лоцмана)", page);
        StringAssert.Contains("Лоцман: «Концы закреплены, отходите»", page);
        StringAssert.Contains("Заданий выполнено: 4 из 4", page);
        StringAssert.Contains("заданий 4 из 4", ExerciseSummary.From(sim.Scenario, sim.Events, null).Progress);
    }
}
