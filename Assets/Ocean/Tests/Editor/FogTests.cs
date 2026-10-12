// ============================================================
// FogTests.cs — Ограниченная видимость и туманные сигналы (C4)
//
// Видимость ↔ плотность тумана; дневной порог огней; звуки и сигналы правил 32–35;
// туманные сигналы целей (интервал, слышимость, задержка звука); критерии F1–F6
// на прогонах сценария C4-01 (ExerciseSimulation) и правило 19 при обнаружении
// «не глазами»; оценка в реальном времени и по записи совпадают; прежние
// сценарии группу «Ограниченная видимость» не получают.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class FogTests
{
    private const double Nm = 1852.0;

    public static ScenarioDefinition DemoC4()
    {
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoFogScenario.Fill(s, area, AssessmentTests.Model45m(), AssessmentTests.Mariner());
        return s;
    }

    private static ExerciseSimulation C4Run(string variant, string folder = "", Action<ExerciseSimulation> tune = null)
    {
        var sim = new ExerciseSimulation { Scenario = DemoC4(), RecordFolder = folder };
        DemoAssessmentRuns.Configure(sim, variant);
        tune?.Invoke(sim);
        sim.Run();
        return sim;
    }

    private static CriterionResult Of(AssessmentResult r, string code, string subject = null) =>
        r.Items.Find(c => c.Code == code && (subject == null || c.Subject == subject));

    // ─────────── Видимость и огни ───────────
    [Test]
    public void Visibility_DensityRoundTrip_HiddenAtV()
    {
        float rho = WeatherVisibility.DensityFor(926.0);
        Assert.AreEqual(0.001869, rho, 1e-6, "ρ = 1,7308/926");
        Assert.AreEqual(926.0, WeatherVisibility.FromFogDensity(rho), 0.1);
        Assert.AreEqual(0.05, Math.Exp(-Math.Pow(rho * 926.0, 2)), 1e-3, "на дистанции V видно 5 %");
        Assert.AreEqual(0.47, Math.Exp(-Math.Pow(rho * 463.0, 2)), 0.01, "на V/2 видно около половины");
        Assert.AreEqual(0f, WeatherVisibility.DensityFor(0.0), "0 — по погоде");
        Assert.IsTrue(FogSignals.Restricted(926.0, 2.0));
        Assert.IsFalse(FogSignals.Restricted(WeatherVisibility.VisibilityM(WeatherPresetId.Moderate), 2.0), "умеренное — 2,3 мили, не ограниченная");
        Assert.IsTrue(FogSignals.Restricted(WeatherVisibility.VisibilityM(WeatherPresetId.Storm), 2.0), "шторм — 0,62 мили");
    }

    [Test]
    public void Lights_DayThreshold_NotVisibleBeyondHullInFog()
    {
        Assert.AreEqual(NavigationLights.ThresholdLux, NavigationLights.ThresholdFor(1.0), "ночь — как в B3");
        Assert.AreEqual(NavigationLights.ThresholdLux, NavigationLights.ThresholdFor(0.6), "сумерки — как в B4");
        Assert.AreEqual(NavigationLights.DayThresholdLux, NavigationLights.ThresholdFor(0.0), 1e-12);
        Assert.AreEqual(Math.Sqrt(NavigationLights.DayThresholdLux * NavigationLights.ThresholdLux), NavigationLights.ThresholdFor(0.25), 1e-12, "середина — среднее геометрическое");

        double k = NavigationLights.TransmissivityFromVisibility(926.0);
        double I = NavigationLights.IntensityForRange(6.0);
        double night = NavigationLights.LuminousRangeNm(I, k, NavigationLights.ThresholdFor(1.0));
        double day = NavigationLights.LuminousRangeNm(I, k, NavigationLights.ThresholdFor(0.0));
        TestContext.WriteLine($"Топовый огонь 6 миль в тумане 0,5 мили: ночью {night:F2} мили, днём {day:F2} мили");
        Assert.Greater(night, 0.5, "ночью огонь виден дальше корпуса (закон Алларда)");
        Assert.Less(day, 0.5, "днём — ближе, чем виден корпус");
        Assert.AreEqual(NavigationLights.DisplayLevel(1e-6), NavigationLights.DisplayLevel(1e-6, NavigationLights.ThresholdLux), "без порога — ночной, как раньше");
    }

    // ─────────── Звуки и сигналы ───────────
    [TestCase(1.0, 0.5, BlastKind.Short)]
    [TestCase(1.9, 0.5, BlastKind.Short)]
    [TestCase(3.0, 0.5, BlastKind.Unknown)]
    [TestCase(3.6, 0.5, BlastKind.Prolonged)]
    [TestCase(3.6, 0.0, BlastKind.Unknown)]
    [TestCase(5.0, 0.0, BlastKind.Prolonged)]
    [TestCase(6.4, 0.5, BlastKind.Prolonged)]
    [TestCase(7.0, 0.5, BlastKind.Unknown)]
    [TestCase(0.2, 0.5, BlastKind.Unknown)]
    public void Blast_Rule32_Classification(double duration, double tolerance, BlastKind expected)
    {
        Assert.AreEqual(expected, SoundSignals.Classify(duration, tolerance));
    }

    [Test]
    public void Signals_Recognize_Rules34And35()
    {
        SoundSignalKind R(string p)
        {
            var list = new List<SoundBlast>();
            foreach (char c in p) list.Add(new SoundBlast { Kind = c == 'P' ? BlastKind.Prolonged : c == 'S' ? BlastKind.Short : BlastKind.Unknown });
            return SoundSignals.Recognize(list);
        }
        Assert.AreEqual(SoundSignalKind.FogMakingWay, R("P"));
        Assert.AreEqual(SoundSignalKind.FogNotMakingWay, R("PP"));
        Assert.AreEqual(SoundSignalKind.FogRestricted, R("PSS"));
        Assert.AreEqual(SoundSignalKind.TurnStarboard, R("S"));
        Assert.AreEqual(SoundSignalKind.TurnPort, R("SS"));
        Assert.AreEqual(SoundSignalKind.Astern, R("SSS"));
        Assert.AreEqual(SoundSignalKind.PilotIdentity, R("SSSS"));
        Assert.AreEqual(SoundSignalKind.Doubt, R("SSSSS"));
        Assert.AreEqual(SoundSignalKind.Doubt, R("SSSSSSS"));
        Assert.AreEqual(SoundSignalKind.OvertakeStarboard, R("PPS"));
        Assert.AreEqual(SoundSignalKind.OvertakePort, R("PPSS"));
        Assert.AreEqual(SoundSignalKind.OvertakeAgree, R("PSPS"));
        Assert.AreEqual(SoundSignalKind.Unrecognized, R("PU"));
        Assert.AreEqual(SoundSignalKind.Unrecognized, R("SP"));

        // Группировка: пауза ≤ 3 с — один сигнал
        var blasts = new List<SoundBlast>
        {
            new SoundBlast { Start = 0, Duration = 5, Kind = BlastKind.Prolonged },
            new SoundBlast { Start = 7, Duration = 5, Kind = BlastKind.Prolonged },    // пауза 2 с — тот же сигнал
            new SoundBlast { Start = 100, Duration = 5, Kind = BlastKind.Prolonged }
        };
        List<SoundSignal> g = SoundSignals.Group(blasts);
        Assert.AreEqual(2, g.Count);
        Assert.AreEqual(SoundSignalKind.FogNotMakingWay, g[0].Kind);
        Assert.AreEqual("— —", g[0].Pattern);
        Assert.AreEqual(SoundSignalKind.FogMakingWay, g[1].Kind);
    }

    [Test]
    public void Detector_HoldToSound_BlastTypeSignalAndVisibility()
    {
        const double dt = 0.02;
        var d = new ControlActionDetector();
        // Звуки: 1…6 с (продолжительный), 8…13 с (второй, пауза 2 с), 30…31 с (короткий); видимость с 40 с
        Func<double, ControlState> f = t => new ControlState
        {
            Telegraph = (int)TelegraphOrder.HalfAhead, TelegraphSf = 0.65,
            HornSounding = (t >= 1 && t < 6) || (t >= 8 && t < 13) || (t >= 30 && t < 31),
            WeatherPreset = (int)WeatherPresetId.Calm, VisibilityOverrideM = t >= 40 ? 926 : 0
        };
        var ev = new List<(double t, ExerciseEvent e)>();
        d.Reset(0, f(0));
        for (int i = 1; i * dt <= 45 + 1e-9; i++)
        {
            double t = i * dt;
            foreach (ExerciseEvent e in d.Update(t, f(t))) ev.Add((t, e));
        }
        List<(double t, ExerciseEvent e)> Of(ExerciseEventType type) => ev.FindAll(x => x.e.Type == type);
        List<(double t, ExerciseEvent e)> ends = Of(ExerciseEventType.HornEnded);
        Assert.AreEqual(3, ends.Count);
        Assert.AreEqual((int)BlastKind.Prolonged, ends[0].e.Index);
        Assert.AreEqual(5.0, ends[0].e.Value, 0.03);
        Assert.AreEqual((int)BlastKind.Short, ends[2].e.Index);
        List<(double t, ExerciseEvent e)> sig = Of(ExerciseEventType.SoundSignal);
        Assert.AreEqual(2, sig.Count, "«— —» и «·»");
        Assert.AreEqual((int)SoundSignalKind.FogNotMakingWay, sig[0].e.Index);
        Assert.AreEqual("— —", sig[0].e.Subject);
        Assert.AreEqual(16.0, sig[0].t, 0.03, "сигнал закончен через 3 с после последнего звука");
        Assert.AreEqual((int)SoundSignalKind.TurnStarboard, sig[1].e.Index);
        List<(double t, ExerciseEvent e)> vis = Of(ExerciseEventType.VisibilityChanged);
        Assert.AreEqual(1, vis.Count);
        Assert.AreEqual(926.0, vis[0].e.Value);
        Assert.AreEqual(0.0, vis[0].e.Value2);
    }

    // ─────────── Сигналы целей ───────────
    [Test]
    public void TargetSignals_IntervalAudibilityDelay()
    {
        Assert.AreEqual(2.0, FogSignals.AudibleRangeNm(250));
        Assert.AreEqual(1.5, FogSignals.AudibleRangeNm(161));
        Assert.AreEqual(1.0, FogSignals.AudibleRangeNm(45));
        Assert.AreEqual(0.5, FogSignals.AudibleRangeNm(15));
        Assert.AreEqual(1.0, FogSignals.Loudness(50, 2778), 1e-12);
        Assert.AreEqual(Math.Pow(10, -30.0 / 20), FogSignals.Loudness(2778, 2778), 1e-9, "на границе слышимости −30 дБ");
        Assert.AreEqual(0.0, FogSignals.Loudness(2800, 2778));
        Assert.Greater(FogSignals.Loudness(500, 2778), FogSignals.Loudness(1500, 2778));

        var sys = new FogSignalSystem();
        var own = new WorldPosition(0, 0);
        var near = new FogSignalSource { Id = "T1", Index = 0, Position = new WorldPosition(1700, 0), SpeedKnots = 10, LengthM = 161 };   // 0,92 мили
        var far = new FogSignalSource { Id = "T2", Index = 1, Position = new WorldPosition(0, 3000), SpeedKnots = 0, LengthM = 161 };     // 1,62 мили — не слышен
        var sources = new List<FogSignalSource> { near, far };
        var heard = new List<HeardSignal>();
        var all = new List<HeardSignal>();
        for (int i = 0; i <= 30000; i++)   // 300 с, шаг 0,01 с
        {
            sys.Update(i * 0.01, true, own, 0, sources, heard);
            all.AddRange(heard);
        }
        // T1: подачи в 20, 130, 240 с; звук доходит через 1700/340 = 5 с
        Assert.AreEqual(3, all.Count, "T2 дальше 1,5 мили — не слышен");
        Assert.AreEqual(25.0, all[0].ArrivalTime, 0.011);
        Assert.AreEqual(135.0, all[1].ArrivalTime, 0.011);
        Assert.AreEqual(245.0, all[2].ArrivalTime, 0.011);
        Assert.AreEqual(SoundSignalKind.FogMakingWay, all[0].Kind);
        Assert.AreEqual(90.0, all[0].QOwnDeg, 1e-6, "справа на траверзе");
        Assert.AreEqual(1700.0, all[0].DistanceM, 1e-6);

        // Без хода — «— —»; без ограниченной видимости — сигналов нет
        sys.Reset();
        sources[0] = new FogSignalSource { Id = "T1", Index = 0, Position = new WorldPosition(500, 0), SpeedKnots = 0.2, LengthM = 161 };
        all.Clear();
        for (int i = 0; i <= 5000; i++) { sys.Update(i * 0.01, true, own, 0, sources, heard); all.AddRange(heard); }
        Assert.AreEqual(SoundSignalKind.FogNotMakingWay, all[0].Kind);
        sys.Reset();
        all.Clear();
        for (int i = 0; i <= 30000; i++) { sys.Update(i * 0.01, false, own, 0, sources, heard); all.AddRange(heard); }
        Assert.AreEqual(0, all.Count);
    }

    // ─────────── Критерии F1–F6 на прогонах C4-01 ───────────
    [Test]
    public void C4_Correct_PassesAllFogCriteria()
    {
        ExerciseSimulation sim = C4Run("fog_correct");
        AssessmentResult r = sim.LiveResult;
        TestContext.WriteLine(r.ToPanelText());
        Assert.AreEqual(ScenarioEndReason.Arrived, sim.EndReason);
        Assert.IsTrue(r.Passed, string.Join(", ", r.MandatoryFailed));
        Assert.IsTrue(Of(r, AssessmentCatalog.F1).Passed);
        Assert.AreEqual(1.0, Of(r, AssessmentCatalog.F2).Value);
        Assert.IsTrue(Of(r, AssessmentCatalog.F3).Passed);
        Assert.IsTrue(Of(r, AssessmentCatalog.F4).Passed);
        CriterionResult f5 = Of(r, AssessmentCatalog.F5, "T1");
        Assert.IsNotNull(f5, "сигнал T1 услышан впереди траверза, пока T1 не видна");
        Assert.AreEqual(20.0, f5.Value, 0.21);
        Assert.IsTrue(r.GroupScores.ContainsKey(AssessmentGroup.Visibility));
        // Сигнал T1 слышен раньше, чем T1 видна
        double firstHeard = sim.Events.Find(e => e.Type == ExerciseEventType.TargetSoundSignal).Time;
        Assert.Less(firstHeard, 60.0);
    }

    [Test]
    public void C4_OwnSignalGapOver2Min_IsViolation()
    {
        // Приёмка C4: пропуск собственного сигнала дольше 2 мин — нарушение
        AssessmentResult none = C4Run("fog_nosignals").LiveResult;
        CriterionResult f1 = Of(none, AssessmentCatalog.F1);
        Assert.IsFalse(f1.Passed);
        Assert.IsTrue(f1.Mandatory);
        Assert.IsFalse(none.Passed);
        CollectionAssert.Contains(none.MandatoryFailed, AssessmentCatalog.F1);

        AssessmentResult late = C4Run("fog_correct", tune: s => s.FogSignalIntervalS = 125).LiveResult;
        Assert.AreEqual(125.0, Of(late, AssessmentCatalog.F1).Value, 0.05);
        Assert.IsFalse(Of(late, AssessmentCatalog.F1).Passed, "125 с > 120 с");

        AssessmentResult ok = C4Run("fog_correct", tune: s => s.FogSignalIntervalS = 115).LiveResult;
        Assert.IsTrue(Of(ok, AssessmentCatalog.F1).Passed, "115 с ≤ 120 с");

        // Тип: без хода нужен «— —»; одиночный «—» при остановленном судне — неверный тип (F2)
        AssessmentResult stopped = C4Run("fog_correct", tune: s => s.Manoeuvres.Add(new SimManoeuvre { Time = 20, SpeedFactor = 0 })).LiveResult;
        Assert.AreEqual(1.0, Of(stopped, AssessmentCatalog.F2).Value, "прогон сам подаёт «— —» без хода");
    }

    [Test]
    public void C4_NoSlowdownOnSignal_Rule19e()
    {
        AssessmentResult r = C4Run("fog_noslow").LiveResult;
        CriterionResult f5 = Of(r, AssessmentCatalog.F5, "T1");
        Assert.IsFalse(f5.Passed);
        StringAssert.Contains("не снижен", f5.Note);
        Assert.IsFalse(r.Passed);
    }

    [Test]
    public void C4_PortTurnOnSignal_Rule19d()
    {
        AssessmentResult r = C4Run("fog_port").LiveResult;
        CriterionResult f6 = Of(r, AssessmentCatalog.F6, "T1");
        Assert.AreEqual(0.0, f6.Value);
        StringAssert.Contains("влево", f6.Note);
        Assert.IsFalse(r.Passed);
        Assert.IsNull(r.Items.Find(c => c.Code == AssessmentCatalog.R1 && !c.Passed), "отворот по правилу 19 маршрутом не штрафуется");
    }

    [Test]
    public void Rule19_DetectedNotSeen_NoStandOnVessel()
    {
        // Цель слева (в ясную погоду своё судно — привилегированное), обнаружение «по радару» на 3 мили
        ScenarioDefinition s = DemoC4();
        s.detectionRangeNm = 3f;
        TrafficShipDefinition t = s.traffic[0];
        t.meetingSide = MeetingSide.Port;
        t.headingDeg = 90f;
        t.motion = TrafficMotion.ConstantCourse;
        var sim = new ExerciseSimulation { Scenario = s };
        DemoAssessmentRuns.Configure(sim, "fog_correct");
        sim.Run();
        AssessmentResult r = sim.LiveResult;
        TestContext.WriteLine(r.ToPanelText());
        EncounterSummary e = r.Encounters.Find(x => x.TargetKey == "T1");
        Assert.IsNotNull(e);
        Assert.AreEqual(EncounterSituation.RestrictedVisibility, e.Situation, "правило 19: цель не видна");
        Assert.IsNull(Of(r, AssessmentCatalog.E6), "привилегированного судна нет");
        Assert.IsNull(Of(r, AssessmentCatalog.E7));
        Assert.IsNotNull(Of(r, AssessmentCatalog.E1), "действия оцениваются как у уступающего");
    }

    [Test]
    public void C4_LiveAndOffline_IdenticalAndMetaFields()
    {
        string dir = Path.Combine(Path.GetTempPath(), "VRBridgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            AssessmentResult live = C4Run("fog_correct", dir).LiveResult;
            RecordedSession rs = SessionReader.Load(dir);
            CollectionAssert.IsEmpty(rs.Warnings, string.Join("; ", rs.Warnings));
            AssessmentResult offline = SessionAssessment.Assess(rs);
            Assert.AreEqual(live.Items.Count, offline.Items.Count);
            for (int i = 0; i < live.Items.Count; i++)
            {
                CriterionResult a = live.Items[i], b = offline.Items[i];
                string tag = $"{a.Code} {a.Subject}";
                Assert.AreEqual(a.Code, b.Code, tag);
                Assert.AreEqual(a.Evaluated, b.Evaluated, tag);
                Assert.IsTrue(a.Value.Equals(b.Value), $"{tag}: {a.Value:R} ≠ {b.Value:R}");
                Assert.AreEqual(a.Note, b.Note, tag);
            }
            Assert.IsTrue(live.Score.Equals(offline.Score));
            Assert.AreEqual(File.ReadAllText(Path.Combine(dir, SessionFormat.AssessmentFile)).TrimStart('﻿'), offline.ToCsv());
            Assert.AreEqual(926.0, rs.Meta.scenario.visibility_m);
            Assert.AreEqual(6.0, rs.Meta.scenario.safe_speed_kn);
            Assert.IsTrue(rs.Meta.scenario.hide_targets);
            Assert.IsTrue(rs.Events.Exists(e => e.Type == ExerciseEventType.TargetSoundSignal && e.Subject == "T1"));
            Assert.IsTrue(rs.Events.Exists(e => e.Type == ExerciseEventType.HornEnded && e.Index == (int)BlastKind.Prolonged));

            // Пересчёт «строго 4–6 с» (допуск 0) по той же записи: звуки 5 с — всё равно верные
            AssessmentSettings strict = rs.Meta.assessment_settings.Clone();
            strict.blastToleranceS = 0;
            Assert.IsTrue(Of(SessionAssessment.Assess(rs, strict), AssessmentCatalog.F1).Passed);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }

    [Test]
    public void PreviousScenarios_NoVisibilityGroup()
    {
        AssessmentResult b3 = AssessmentTests.B3Run("correct").LiveResult;
        Assert.IsFalse(b3.Items.Exists(c => c.Info.Group == AssessmentGroup.Visibility), "B3-01 ночью, V = 6,2 мили — не ограниченная");
        Assert.IsFalse(b3.GroupScores.ContainsKey(AssessmentGroup.Visibility));
        Assert.AreEqual(95.465, b3.Score, 0.01, "балл B3-01 «правильно» — как в табл. 10.3");
        CollectionAssert.IsEmpty(DemoC4().Validate());
    }
}
