// ============================================================
// MooringRecordingTests.cs — Запись швартовки и причал на картах (запрос 19.2б-3б)
//
// mooring.csv: запись и чтение строки бит в бит с округлением «как в файле» (Quantize), файла нет,
// если причал не понадобился; снимок причала в session.json (геометрия, кранцы, кнехты, JSON);
// сквозная запись швартовки концами (срез MooringSampler) → схема: пирс, кромка, кранцы, кнехты,
// навал в точке касания на кромке, вид «У причала» с контурами судна; без подхода к причалу вида нет;
// неподвижный слой окна инструктора и планшета (BuildStatic) показывает причал.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class MooringRecordingTests
{
    private static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "VRBridgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Delete(string dir)
    {
        try { Directory.Delete(dir, true); } catch (Exception) { }
    }

    private static WaterArea DemoArea()
    {
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        return area;
    }

    private static ScenarioDefinition BerthScenario(out WaterArea area, out ShipParameters p)
    {
        area = DemoArea();
        p = LowSpeedModelTests.Model45m();
        var crit = ScriptableObject.CreateInstance<AssessmentCriteria>();
        crit.settings = DemoBerthScenario.Assessment();
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoBerthScenario.Fill(s, area, p, crit);
        return s;
    }

    private static MooringSample Sample(double t)
    {
        var s = new MooringSample
        {
            Time = t, Berth = 0, BowGapM = 3.14159, SternGapM = 12.3456, BowSpeedKn = 0.123456, SternSpeedKn = -0.0456,
            AngleDeg = -3.21987, SpeedOverGroundKn = 0.30001, FenderContact = true, FenderDeflectionRatio = 0.87654,
            FenderForceKn = 123.4567, QuayContact = false, ThrusterOrderPct = -50, ThrusterActualPct = -31.234,
            Station = (int)BridgeStation.WingPort, StagesDone = 2, MooredHoldS = 12.34,
            Lines = new MooringLineSample[MooringSample.LineCount]
        };
        s.Lines[0] = new MooringLineSample { Status = MooringLineStatus.HaulingIn, Bollard = 7, LengthM = 21.237, TensionKn = 12.3456 };
        s.Lines[4] = new MooringLineSample { Status = MooringLineStatus.OnBitts, Bollard = 1, LengthM = 9.999, TensionKn = 0.004 };
        s.Lines[5] = new MooringLineSample { Status = MooringLineStatus.Broken, Bollard = -1, LengthM = double.NaN };
        for (int i = 1; i < 4; i++) s.Lines[i] = new MooringLineSample { Status = MooringLineStatus.Aboard, Bollard = -1, LengthM = double.NaN };
        return s;
    }

    private static void AssertSame(in MooringSample a, in MooringSample b)
    {
        Assert.AreEqual(a.Time, b.Time); Assert.AreEqual(a.Berth, b.Berth);
        Assert.AreEqual(a.BowGapM, b.BowGapM); Assert.AreEqual(a.SternGapM, b.SternGapM);
        Assert.AreEqual(a.BowSpeedKn, b.BowSpeedKn); Assert.AreEqual(a.SternSpeedKn, b.SternSpeedKn);
        Assert.AreEqual(a.AngleDeg, b.AngleDeg); Assert.AreEqual(a.SpeedOverGroundKn, b.SpeedOverGroundKn);
        Assert.AreEqual(a.FenderContact, b.FenderContact); Assert.AreEqual(a.FenderDeflectionRatio, b.FenderDeflectionRatio);
        Assert.AreEqual(a.FenderForceKn, b.FenderForceKn); Assert.AreEqual(a.QuayContact, b.QuayContact);
        Assert.AreEqual(a.ThrusterOrderPct, b.ThrusterOrderPct); Assert.AreEqual(a.ThrusterActualPct, b.ThrusterActualPct);
        Assert.AreEqual(a.Station, b.Station); Assert.AreEqual(a.StagesDone, b.StagesDone); Assert.AreEqual(a.MooredHoldS, b.MooredHoldS);
        for (int i = 0; i < MooringSample.LineCount; i++)
        {
            Assert.AreEqual(a.Lines[i].Status, b.Lines[i].Status, $"конец {i}");
            Assert.AreEqual(a.Lines[i].Bollard, b.Lines[i].Bollard, $"конец {i}");
            Assert.AreEqual(a.Lines[i].LengthM, b.Lines[i].LengthM, $"конец {i}");
            Assert.AreEqual(a.Lines[i].TensionKn, b.Lines[i].TensionKn, $"конец {i}");
        }
    }

    // ─────────── mooring.csv ───────────
    [Test]
    public void MooringCsv_WrittenAndReadBackBitExact()
    {
        string dir = NewDir();
        try
        {
            ScenarioDefinition sc = BerthScenario(out _, out ShipParameters p);
            var rec = new SessionRecorder(dir, SessionMeta.Create(sc, p, "1", DateTime.Now), background: false);
            var written = new List<MooringSample>();
            for (int k = 0; k < 3; k++)
            {
                MooringSample s = Sample(0.2 * k + 1e-7);
                if (k == 2) { s.Berth = -1; s.BowGapM = s.SternGapM = s.BowSpeedKn = s.SternSpeedKn = s.AngleDeg = double.NaN; s.StagesDone = -1; }
                rec.AddTrack(new TrackSample { Time = s.Time, Leg = -1 });
                rec.AddMooring(s);
                written.Add(s);
            }
            Assert.IsTrue(rec.Close());

            string text = File.ReadAllText(Path.Combine(dir, SessionFormat.MooringFile));
            StringAssert.StartsWith(SessionFormat.MooringHeader, text.TrimStart('﻿'));
            RecordedSession r = SessionReader.Load(dir);
            Assert.AreEqual(3, r.Mooring.Count);
            for (int k = 0; k < 3; k++) AssertSame(SessionFormat.Quantize(written[k]), r.Mooring[k]);
            Assert.IsFalse(r.Mooring[2].HasBerth, "без причала — пустые зазоры");
            Assert.AreEqual(3, r.Meta.summary.mooring_rows);
            CollectionAssert.Contains(r.Meta.recording.files, SessionFormat.MooringFile);
            Assert.AreEqual(Enum.GetValues(typeof(MooringLineStatus)).Length, r.Meta.recording.mooring_line_states.Count);
            Assert.AreEqual(written[1].Time, r.MooringAt(0.3).Value.Time, 1e-3);
            Assert.IsNull(r.MooringAt(-1));
        }
        finally { Delete(dir); }
    }

    [Test]
    public void MooringCsv_NoBerth_NoFile()
    {
        string dir = NewDir();
        try
        {
            var rec = new SessionRecorder(dir, new SessionMeta(), background: false);
            rec.AddTrack(new TrackSample { Time = 0, Leg = -1 });
            Assert.IsTrue(rec.Close());
            Assert.IsFalse(File.Exists(Path.Combine(dir, SessionFormat.MooringFile)));
            RecordedSession r = SessionReader.Load(dir);
            Assert.AreEqual(0, r.Mooring.Count);
            Assert.IsFalse(r.Warnings.Exists(w => w.Contains(SessionFormat.MooringFile)), "без файла — без замечания");
            CollectionAssert.DoesNotContain(r.Meta.recording.files, SessionFormat.MooringFile);
        }
        finally { Delete(dir); }
    }

    // ─────────── session.json: причал ───────────
    [Test]
    public void SessionArea_BerthSnapshot_SameGeometryThroughJson()
    {
        WaterArea area = DemoArea();
        Assert.Greater(area.berths.Count, 0);
        SessionArea snap = SessionArea.From(area);
        var back = new SessionArea();
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(snap), back);
        Assert.AreEqual(area.berths.Count, back.berths.Count);
        Assert.IsFalse(back.IsEmpty);
        BerthArea a = area.berths[0], b = back.Berth(0);
        Assert.AreEqual(a.name, b.name);
        Assert.AreEqual(a.a.East, b.a.East, 1e-3); Assert.AreEqual(a.b.North, b.b.North, 1e-3);
        Assert.AreEqual(a.fenderOuterDiameterM, b.fenderOuterDiameterM, 1e-3);
        Assert.AreEqual(a.FenderPositions().Count, b.FenderPositions().Count);
        Assert.AreEqual(a.FenderPositions().Count, back.berths[0].fender_t_m.Count);
        Assert.AreEqual(a.BollardPositions().Count, back.berths[0].bollard_t_m.Count);
        for (int i = 0; i < a.BollardPositions().Count; i++)
            Assert.AreEqual(a.BollardWorld(i).East, b.BollardWorld(i).East, 1e-2, $"кнехт {i}");
        Assert.IsNull(back.Berth(5));
        // Записи до 19.2б-3б: поля нет — список пуст
        var old = new SessionArea();
        JsonUtility.FromJsonOverwrite("{\"name\":\"WaterArea_Demo\",\"aids\":[]}", old);
        Assert.AreEqual(0, old.berths.Count);
    }

    // ─────────── Сквозная запись швартовки → схема ───────────

    // Швартовка концами с 3 м (как BerthScenarioTests.EndToEnd), запись track.csv + mooring.csv + события навала
    private static string RecordMooring(out ScenarioDefinition sc, out int berthingEvents)
    {
        sc = BerthScenario(out WaterArea area, out ShipParameters p);
        BerthArea b = area.berths[0];
        string dir = NewDir();
        var rec = new SessionRecorder(dir, SessionMeta.Create(sc, p, "1", DateTime.Now), background: false);
        WorldPosition pos = b.AlongsidePosition(0.5 * b.Length, 3.0, p.beam, out double hdg);
        var m = new ShipDynamicsModel(p) { MotionOverride = MotionModel.Manoeuvring3Dof };
        m.Teleport(pos.East, pos.North, hdg * Math.PI / 180.0, 0.0, 0.0);
        var sys = new MooringSystem(b, p) { FairleadHeightM = 4.5 };
        var tr = new MooringTaskTracker(sc.mooring);
        int[] lines = { 0, 1, 4, 5 };
        foreach (int i in lines) { sys.Order(i, LineOrder.Give, m, 0, out _); sys.Order(i, LineOrder.HaulIn, m, 0, out _); }
        var sampler = new TrackSampler(5);
        const double dt = 0.02;
        double t = 0;
        bool hauling = true;
        berthingEvents = 0;
        for (int k = 0; k < 30000 && !tr.Moored; k++)
        {
            if (sampler.Due(t))
            {
                rec.AddTrack(new TrackSample { Time = t, East = m.East, North = m.North, HeadingDeg = m.Psi * 180.0 / Math.PI, CogDeg = m.Psi * 180.0 / Math.PI, Leg = -1 });
                rec.AddMooring(MooringSampler.Build(t, b, 0, p, m, sys, ThrusterOrders.ZeroIndex, BridgeStation.WingPort, tr));
            }
            if (k % 10 == 0)
            {
                BerthReadout br = BerthReadout.Compute(b, p, m);
                bool want = sys.InContact || (hauling ? Math.Max(br.BowSpeedKn, br.SternSpeedKn) < 0.1 : Math.Max(br.BowSpeedKn, br.SternSpeedKn) < 0.05);
                if (want != hauling) foreach (int i in lines) sys.Order(i, want ? LineOrder.HaulIn : LineOrder.Hold, m, t, out _);
                hauling = want;
            }
            sys.Apply(m, dt, t);
            m.Step(dt);
            t += dt;
            foreach (MooringEvent e in sys.Events)
            {
                ExerciseEvent ev = MooringEventBus.ToExerciseEvent(e);
                ev.Time = t; ev.InExercise = true;
                ev.Position = new WorldPosition(m.East, m.North); ev.HeadingDeg = (float)(m.Psi * 180.0 / Math.PI);
                rec.AddEvent(ev);
                if (e.Type == MooringEventType.BerthingImpact) berthingEvents++;
            }
            sys.Events.Clear();
            var x = new MooringTaskInput
            {
                Readout = BerthReadout.Compute(b, p, m), FenderContact = sys.InContact,
                SpeedOverGroundKn = Math.Sqrt(m.VelocityEast * m.VelocityEast + m.VelocityNorth * m.VelocityNorth) / ShipParameters.KnotsToMs
            };
            MooringTasks.LinesFast(sys.Lines, out x.BowLineFast, out x.SternLineFast);
            tr.Update(x, t, dt);
        }
        Assert.IsTrue(tr.Moored, "ошвартован");
        rec.AddTrack(new TrackSample { Time = t, East = m.East, North = m.North, HeadingDeg = m.Psi * 180.0 / Math.PI, Leg = -1 });
        rec.AddMooring(MooringSampler.Build(t, b, 0, p, m, sys, ThrusterOrders.ZeroIndex, BridgeStation.WingPort, tr));
        Assert.IsTrue(rec.Close());
        return dir;
    }

    [Test]
    public void EndToEnd_MooringRecorded_BerthViewWithHullsAndImpact()
    {
        string dir = RecordMooring(out ScenarioDefinition sc, out int impacts);
        try
        {
            RecordedSession s = SessionReader.Load(dir);
            Assert.AreEqual(s.Track.Count, s.Mooring.Count, "строка mooring.csv на каждую строку track.csv");
            MooringSample last = s.Mooring[s.Mooring.Count - 1];
            Assert.AreEqual(4, last.StagesDone, "ошвартован — 4 задания");
            Assert.IsTrue(last.FenderContact, "у кранцев");
            Assert.Less(last.MinGapM, sc.mooring.stopGapM);
            Assert.AreEqual((int)BridgeStation.WingPort, last.Station);
            int fast = 0;
            foreach (MooringLineSample l in last.Lines) if (l.Fast) { fast++; Assert.Greater(l.LengthM, 0); Assert.GreaterOrEqual(l.Bollard, 0); }
            Assert.AreEqual(4, fast);
            Assert.AreEqual(MooringLineStatus.Aboard, last.Lines[2].Status, "шпринги не подавались");

            DebriefMapData map = DebriefMap.Build(s, null);
            Assert.IsTrue(map.HasBerths);
            int nb = sc.waterArea.berths.Count, fenders = 0, bollards = 0;   // 19.4б-3: причалов два
            foreach (BerthArea ba in sc.waterArea.berths) { fenders += ba.FenderPositions().Count; bollards += ba.BollardPositions().Count; }
            Assert.AreEqual(nb, map.Polygons.FindAll(x => x.Layer == MapLayer.Pier).Count);
            Assert.AreEqual(fenders, map.Polygons.FindAll(x => x.Layer == MapLayer.Fender).Count);
            Assert.AreEqual(bollards, map.Markers.FindAll(x => x.Layer == MapLayer.Bollard).Count);
            Assert.AreEqual(nb, map.Lines.FindAll(x => x.Layer == MapLayer.Quay).Count);

            DebriefMapView berth = map.Views.Find(v => v.Key == "berth");
            Assert.IsNotNull(berth, "вид «У причала» в сценарии швартовки");
            Assert.AreEqual(1, map.Views.IndexOf(berth), "сразу после «Весь маршрут»");
            Assert.IsTrue(berth.ShowHulls);
            Assert.IsFalse(map.Views[0].ShowHulls);
            Assert.GreaterOrEqual(berth.Bounds.Width, DebriefMap.BerthWindowMinM - 1e-6);
            Assert.Less(Math.Max(berth.Bounds.Width, berth.Bounds.Height), 400, "окно — у причала, не весь маршрут");
            int hulls = map.Polygons.FindAll(x => x.Layer == MapLayer.HullOutline).Count;
            double dur = s.Track[s.Track.Count - 1].Time;
            Assert.AreEqual((int)Math.Ceiling(dur / DebriefMap.HullOutlineStepS - 1e-6) + 1, hulls, "контуры через 15 с и в конце");

            // Навал: значок на кромке (n ≈ 0), подпись — скорость, уз
            List<MapMarker> marks = map.Markers.FindAll(x => x.Layer == MapLayer.Berthing);
            Assert.Greater(impacts, 0, "навал был");
            int speeds = 0;
            foreach (MapMarker mk in marks) speeds += mk.Label.Split('·').Length;
            Assert.AreEqual(impacts, speeds, "каждый навал — скорость в подписи (близкие — в одной отметке)");
            BerthArea b = sc.waterArea.berths[0];
            foreach (MapMarker mk in marks)
            {
                b.ToLocal(mk.Position, out double tt, out double nn);
                Assert.AreEqual(0.0, nn, 0.01, "точка касания — на кромке (снимок округлён до мм)");
                Assert.That(tt, Is.InRange(0.0, b.Length));
                StringAssert.StartsWith("навал ", mk.Label);
                StringAssert.EndsWith(" уз", mk.Label);
                Assert.IsTrue(mk.Ok);
            }

            string svg = DebriefSvg.Map(map, berth, "b");
            StringAssert.Contains("fill=\"#d9d9d9\"", svg, "пирс");
            StringAssert.Contains("контур судна", svg);
            StringAssert.Contains("навал ", svg);
            StringAssert.DoesNotContain("контур судна", DebriefSvg.Map(map, map.Views[0], "a"), "контуры — только «У причала»");
            AssessmentResult res = SessionAssessment.Assess(s);
            string html = SessionReport.Build(s, res, DebriefMap.Build(s, res));
            StringAssert.Contains("У причала", html);
            TestContext.Out.WriteLine($"Запись швартовки: {s.Track.Count} строк, {dur:F1} с, контуров {hulls}, навалов {impacts}; окно «У причала» {berth.Bounds.Width:F0} × {berth.Bounds.Height:F0} м");
        }
        finally { Delete(dir); }
    }

    [Test]
    public void Map_FarFromBerth_NoBerthView_StaticLayerHasPier()
    {
        WaterArea area = DemoArea();
        var s = new RecordedSession();
        s.Meta.scenario.area = SessionArea.From(area);
        s.Meta.ship.length_m = 45; s.Meta.ship.beam_m = 10;
        for (int k = 0; k <= 20; k++) s.Track.Add(new TrackSample { Time = 10 * k, East = 0, North = -2500 + 50 * k, HeadingDeg = 0, Leg = -1 });
        DebriefMapData map = DebriefMap.Build(s, null);
        Assert.IsTrue(map.HasBerths, "причал на схеме есть");
        Assert.IsNull(map.Views.Find(v => v.Key == "berth"), "к причалу не подходил — вида нет");
        Assert.AreEqual(0, map.Polygons.FindAll(x => x.Layer == MapLayer.HullOutline).Count);

        // Окно инструктора и планшет: неподвижный слой с причалом, границы его включают
        DebriefMapData st = DebriefMap.BuildStatic(SessionArea.From(area), null);
        Assert.AreEqual(area.berths.Count, st.Polygons.FindAll(x => x.Layer == MapLayer.Pier).Count);   // 19.4б-3: причалов два
        Assert.IsTrue(st.Views[0].Bounds.Contains(area.berths[0].Middle));
    }
}
