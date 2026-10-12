// ============================================================
// AssessmentTests.cs — Оценка действий обучающегося (C2)
//
// Синтетические ситуации (кинематика: курс меняется с постоянной скоростью
// поворота, без модели движения) — классификация по правилам 13–17 и критерии;
// прогоны сценариев B2-01 и B3-01 моделями (ExerciseSimulation) — маршрут,
// аварии и главное требование C2: оценка в реальном времени и офлайн по
// записанной сессии совпадают.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class AssessmentTests
{
    private const double Nm = 1852.0;
    private const double Kn = 0.514444;

    // ─────────── Синтетическая ситуация ───────────
    private sealed class Synth
    {
        public readonly AssessmentContext Ctx = new AssessmentContext { Weather = WeatherPresetId.Calm, TimeOfDay = TimeOfDay.Day, DetectionRangeNm = 10 };
        public double OwnE = 0, OwnN = 0, OwnCourse, OwnSpeed = 12;
        public double TgtE, TgtN, TgtCourse, TgtSpeed = 12, TgtLength = 100;
        public double RequiredCpaNm = 0.5;
        public bool Narrow = false;
        public double XteLimit = 2000;
        /// <summary>Повороты: время, изменение курса (°), сколько держать (с).</summary>
        public readonly List<(double t, double dCourse, double hold)> Turns = new List<(double, double, double)>();
        public readonly List<(double t, double sf)> Telegraph = new List<(double, double)>();
        public readonly List<(double t, TimeOfDay tod)> TimeChanges = new List<(double, TimeOfDay)>();
        public double TurnRateDegS = 1.0;
        public ExerciseAssessor A;

        public AssessmentResult Run(double duration)
        {
            var start = new WorldPosition(OwnE, OwnN);
            CpaCalculator.Velocity(OwnCourse, 1.0, out double ce, out double cn);
            Ctx.Route.Clear();
            Ctx.Route.Add(new RouteWaypoint("Старт", start, (float)XteLimit));
            Ctx.Route.Add(new RouteWaypoint("Конец", new WorldPosition(OwnE + ce / Kn * 30000, OwnN + cn / Kn * 30000), (float)XteLimit, Narrow));
            Ctx.Targets.Clear();
            Ctx.Targets.Add(new AssessmentTargetInfo { Id = "T1", Name = "цель", Index = 0, LengthM = TgtLength, RequiredCpaNm = RequiredCpaNm });
            A = new ExerciseAssessor(Ctx);

            double e = OwnE, n = OwnN, psi = OwnCourse, ordered = OwnCourse, u = OwnSpeed, uOrdered = OwnSpeed;
            double te = TgtE, tn = TgtN;
            double rudder = 0;
            var events = new List<ExerciseEvent>();
            void Ev(ExerciseEventType type, double time, double value = double.NaN, double value2 = double.NaN, int index = -1, string subject = "")
            {
                events.Add(new ExerciseEvent(type) { Time = time, InExercise = true, Value = value, Value2 = value2, Index = index, Subject = subject, Position = new WorldPosition(e, n) });
            }
            Ev(ExerciseEventType.ExerciseStarted, 0);
            var pending = new List<(double t, Action act)>();
            foreach (var turn in Turns)
            {
                var tr = turn;
                pending.Add((tr.t, () => { ordered = psi + tr.dCourse; rudder = Math.Sign(tr.dCourse) * 20; Ev(ExerciseEventType.HelmStarted, tr.t, 0); }));
                pending.Add((tr.t + 3, () => Ev(ExerciseEventType.HelmEnded, tr.t + 3, Math.Sign(tr.dCourse) * 20, 3)));
                pending.Add((tr.t + tr.hold, () => { ordered = OwnCourse; rudder = -Math.Sign(tr.dCourse) * 20; Ev(ExerciseEventType.HelmStarted, tr.t + tr.hold, 0); }));
                pending.Add((tr.t + tr.hold + 3, () => Ev(ExerciseEventType.HelmEnded, tr.t + tr.hold + 3, -Math.Sign(tr.dCourse) * 20, 3)));
            }
            foreach (var tg in Telegraph)
            {
                var x = tg;
                pending.Add((x.t, () => { uOrdered = OwnSpeed * x.sf; Ev(ExerciseEventType.TelegraphChanged, x.t, x.sf, 1.0, (int)ScenarioText.OrderFromSpeedFactor(x.sf)); }));
            }
            foreach (var tc in TimeChanges)
            {
                var x = tc;
                pending.Add((x.t, () => Ev(ExerciseEventType.TimeOfDayChanged, x.t, 0, double.NaN, (int)x.tod)));
            }
            pending.Sort((a, b) => a.t.CompareTo(b.t));

            const double dt = 0.2;
            int pi = 0;
            for (int k = 0; k * dt <= duration + 1e-9; k++)
            {
                double t = k * dt;
                while (pi < pending.Count && pending[pi].t <= t + 1e-9) pending[pi++].act();
                foreach (ExerciseEvent x in events) A.AddEvent(x);
                events.Clear();
                double xte = RouteTracker.CrossTrack(Ctx.Route[0].position, Ctx.Route[1].position, new WorldPosition(e, n));
                var own = new TrackSample
                {
                    Time = t, East = e, North = n, HeadingDeg = (psi % 360 + 360) % 360, SpeedKnots = u,
                    RudderOrderedDeg = Math.Abs(EncounterClassifier.Signed(ordered - psi)) > 0.5 ? rudder : 0, XteM = xte, Leg = 0, XteLimitM = XteLimit
                };
                A.AddSample(own, new List<TargetSample> { new TargetSample { Time = t, Id = "T1", East = te, North = tn, HeadingDeg = TgtCourse, SpeedKnots = TgtSpeed } });
                // шаг кинематики
                double err = EncounterClassifier.Signed(ordered - psi);
                psi += Math.Max(-TurnRateDegS * dt, Math.Min(TurnRateDegS * dt, err));
                u += (uOrdered - u) * dt / 20.0;
                CpaCalculator.Velocity(psi, u, out double ve, out double vn);
                e += ve * dt; n += vn * dt;
                CpaCalculator.Velocity(TgtCourse, TgtSpeed, out double we, out double wn);
                te += we * dt; tn += wn * dt;
            }
            A.AddEvent(new ExerciseEvent(ExerciseEventType.ExerciseEnded) { Time = duration, InExercise = true, EndReason = ScenarioEndReason.Aborted });
            return A.Finish();
        }
    }

    private static void Print(AssessmentResult r)
    {
        TestContext.WriteLine(r.Headline());
        foreach (EncounterSummary e in r.Encounters)
            TestContext.WriteLine($"  {e.TargetId}: {EncounterClassifier.Title(e.Situation)}; обнаружена t = {e.DetectTime:F1} с на {e.DetectRangeNm:F2} мили; " +
                                  $"опасность t = {e.OnsetTime:F1} с, CPA {e.OnsetCpaNm:F2} мили, TCPA {e.OnsetTcpaMin:F1} мин, dП/dt {e.BearingRateDegMin:F2} °/мин; " +
                                  $"действие {e.Action} t = {e.ActionTime:F1}; min {e.MinDistanceM:F0} м в t = {e.MinTime:F1}");
        foreach (CriterionResult c in r.Items)
            TestContext.WriteLine($"  {(c.Evaluated ? c.Passed ? "+" : "×" : "–")} {c.Code} {c.Subject}: {AssessmentResult.ValueText(c)} (балл {c.Score:F2}) — {c.Note}");
    }

    // ─────────── CPA/TCPA, пеленг ───────────
    [Test]
    public void Cpa_Tcpa_BearingRate_MatchManualCalculation()
    {
        // Курс столкновения: своё 000° 12 уз из (0; 0), цель из (1852; 1852) курсом 270° 12 уз.
        // R₀ = (1852; 1852), V_r = (−6,173; −6,173) м/с → TCPA = 1852/6,173 = 300 с, CPA = 0, пеленг 045° постоянен
        var s = new Synth { OwnCourse = 0, TgtE = Nm, TgtN = Nm, TgtCourse = 270 };
        AssessmentResult r = s.Run(120);
        TargetTrack tr = s.A.Targets[0];
        TargetObservation o = tr.Obs[0];
        Assert.AreEqual(Math.Sqrt(2) * Nm, o.RangeM, 1e-6);
        Assert.AreEqual(45.0, o.BearingDeg, 1e-9);
        Assert.AreEqual(Nm / (12 * ShipParameters.KnotsToMs), o.TcpaS, 1e-6);
        Assert.AreEqual(0.0, o.CpaM, 1e-6);
        Assert.AreEqual(45.0, o.QOwnDeg, 1e-9, "цель на курсовом 45° ПБ");
        Assert.AreEqual(315.0, o.QTargetDeg, 1e-9, "своё судно с цели — 45° ЛБ");
        Assert.AreEqual(0.0, tr.LastEncounter.BearingRateDegMin, 1e-6, "пеленг не меняется — правило 7 г");
        Assert.IsTrue(tr.LastEncounter.SteadyBearing);

        // Пеленг меняется: цель идёт 270° в 3 милях к северу, своё 000°.
        // dП/dt = (r_e·v_n − r_n·v_e)/R² (рад/с) при r = (0; 5556), v_r = (−6,173; −6,173): −(5556·(−6,173))/5556² ... в °/мин
        var s2 = new Synth { OwnCourse = 0, TgtE = 0, TgtN = 3 * Nm, TgtCourse = 270, RequiredCpaNm = 5 };
        s2.Run(70);
        TargetTrack t2 = s2.A.Targets[0];
        Assert.IsNotNull(t2.LastEncounter);
        // Аналитически по окну 60 с (МНК по 61 точке, t = 0…60 с): сравнить с численной производной пеленга в середине окна
        TargetObservation mid = t2.Obs[150];
        double re = mid.East - 0, rn = mid.North - 12 * Kn * 30.0;
        double vre = -12 * Kn, vrn = -12 * Kn;
        double analytic = (rn * vre - re * vrn) / (re * re + rn * rn) * 180 / Math.PI * 60;   // dП/dt = (r_n·v_e − r_e·v_n)/R²
        TestContext.WriteLine($"dП/dt: МНК {t2.LastEncounter.BearingRateDegMin:F3} °/мин, аналитически в середине окна {analytic:F3} °/мин");
        Assert.AreEqual(analytic, t2.LastEncounter.BearingRateDegMin, 0.05);
    }

    // ─────────── Классификация ───────────
    [Test]
    public void Classify_Rules13to15_Boundaries()
    {
        Assert.AreEqual(EncounterSituation.HeadOn, EncounterClassifier.Classify(3, 356, 6));
        Assert.AreEqual(EncounterSituation.HeadOn, EncounterClassifier.Classify(6, 354, 6), "граница α = 6°");
        Assert.AreEqual(EncounterSituation.CrossingGiveWay, EncounterClassifier.Classify(6.5, 0, 6), "за сектором встречных — пересечение, цель справа");
        Assert.AreEqual(EncounterSituation.CrossingStandOn, EncounterClassifier.Classify(330, 40, 6), "цель слева — привилегированное");
        Assert.AreEqual(EncounterSituation.CrossingGiveWay, EncounterClassifier.Classify(45, 315, 6));
        // 22,5° позади траверза: q = 112,5°
        Assert.AreEqual(EncounterSituation.Overtaking, EncounterClassifier.Classify(2, 113, 6), "своё судно позади траверза цели — обгоняет");
        Assert.AreEqual(EncounterSituation.Overtaking, EncounterClassifier.Classify(2, 247, 6));
        Assert.AreEqual(EncounterSituation.CrossingGiveWay, EncounterClassifier.Classify(20, 112, 6), "112° — ещё не обгон");
        Assert.AreEqual(EncounterSituation.Overtaken, EncounterClassifier.Classify(180, 2, 6), "цель позади — обгоняют своё судно");
        Assert.AreEqual(EncounterSituation.Overtaken, EncounterClassifier.Classify(112.5, 30, 6));
        Assert.IsTrue(EncounterClassifier.GiveWay(EncounterSituation.Overtaking));
        Assert.IsFalse(EncounterClassifier.GiveWay(EncounterSituation.Overtaken));
    }

    // ─────────── Встречные курсы ───────────
    private static Synth HeadOn() => new Synth { OwnCourse = 0, TgtE = -60, TgtN = 2 * Nm, TgtCourse = 180 };

    [Test]
    public void HeadOn_TurnToStarboard_Pass()
    {
        Synth s = HeadOn();
        s.Turns.Add((30, 40, 240));
        AssessmentResult r = s.Run(600);
        Print(r);
        Assert.AreEqual(EncounterSituation.HeadOn, r.Encounters[0].Situation);
        Assert.IsTrue(r.Find(AssessmentCatalog.E3, "T1").Passed, "поворот вправо — правило 14");
        Assert.IsTrue(r.Find(AssessmentCatalog.E1, "T1").Passed);
        Assert.IsTrue(r.Find(AssessmentCatalog.E8, "T1").Passed);
        Assert.AreEqual(30.0, r.Find(AssessmentCatalog.E9, "T1").Value, 1e-9, "реакция — от обнаружения (t = 0) до перекладки");
        Assert.Greater(r.Find(AssessmentCatalog.E2, "T1").Value, 4.0, "TCPA в начале манёвра ≈ 4,5 мин");
    }

    [Test]
    public void HeadOn_TurnToPort_ViolationRule14()
    {
        Synth s = HeadOn();
        s.Turns.Add((30, -40, 240));
        AssessmentResult r = s.Run(600);
        Print(r);
        CriterionResult e3 = r.Find(AssessmentCatalog.E3, "T1");
        Assert.IsTrue(e3.Evaluated);
        Assert.IsFalse(e3.Passed, "поворот влево на встречных — нарушение правила 14");
        CollectionAssert.Contains(r.MandatoryFailed, AssessmentCatalog.E3);
        Assert.IsFalse(r.Passed);
    }

    // ─────────── Пересечение курсов ───────────
    // Цель справа на курсе столкновения: пеленг 045°, 2 мили, курс 270°, 12 уз (TCPA ≈ 6,3 мин)
    private static Synth CrossingFromStarboard() => new Synth { OwnCourse = 0, TgtE = 2 * Nm * Math.Sin(Math.PI / 4), TgtN = 2 * Nm * Math.Cos(Math.PI / 4), TgtCourse = 270, RequiredCpaNm = 0.3 };

    [Test]
    public void Crossing_GiveWayPassesAhead_ViolationRule15()
    {
        Synth s = CrossingFromStarboard();
        s.Turns.Add((20, -60, 400));
        AssessmentResult r = s.Run(900);
        Print(r);
        Assert.AreEqual(EncounterSituation.CrossingGiveWay, r.Encounters[0].Situation);
        CriterionResult e4 = r.Find(AssessmentCatalog.E4, "T1");
        Assert.IsFalse(e4.Passed, "уступающее прошло по носу цели — нарушение правила 15");
        Assert.IsFalse(r.Find(AssessmentCatalog.E5, "T1").Passed, "поворот влево на цель справа — замечание");
        Assert.IsFalse(r.Passed);
    }

    [Test]
    public void Crossing_GiveWayTurnsStarboardPassesAstern_Pass()
    {
        Synth s = CrossingFromStarboard();
        s.Turns.Add((20, 60, 400));
        AssessmentResult r = s.Run(900);
        Print(r);
        Assert.IsTrue(r.Find(AssessmentCatalog.E4, "T1").Passed, "прошёл за кормой цели");
        Assert.IsTrue(r.Find(AssessmentCatalog.E5, "T1").Passed);
        Assert.IsTrue(r.Find(AssessmentCatalog.E1, "T1").Passed);
        Assert.IsTrue(r.Find(AssessmentCatalog.E8, "T1").Passed);
    }

    [Test]
    public void Crossing_SpeedReduction_CountsAsAction()
    {
        Synth s = CrossingFromStarboard();
        s.Telegraph.Add((20, 0.0));
        AssessmentResult r = s.Run(900);
        Print(r);
        CriterionResult e1 = r.Find(AssessmentCatalog.E1, "T1");
        Assert.IsTrue(e1.Passed, "стоп машине — снижение скорости ≥ 50 %");
        Assert.IsFalse(r.Find(AssessmentCatalog.E5, "T1").Evaluated, "перекладок нет — направление не оценивается");
        Assert.IsTrue(r.Find(AssessmentCatalog.E4, "T1").Passed, "цель прошла впереди");
    }

    // ─────────── Привилегированное судно ───────────
    // Цель слева: пеленг 315°, 2 мили, курс 090°, CPA ≈ 0,2 мили
    private static Synth CrossingFromPort() => new Synth { OwnCourse = 0, TgtE = -2 * Nm * Math.Sin(Math.PI / 4), TgtN = 2 * Nm * Math.Cos(Math.PI / 4) + 300, TgtCourse = 90 };

    [Test]
    public void StandOn_KeepsCourseAndSpeed_Pass()
    {
        Synth s = CrossingFromPort();
        AssessmentResult r = s.Run(900);
        Print(r);
        Assert.AreEqual(EncounterSituation.CrossingStandOn, r.Encounters[0].Situation);
        Assert.IsTrue(r.Find(AssessmentCatalog.E6, "T1").Passed, "курс и скорость сохранены — правило 17 а(i)");
        Assert.IsFalse(r.Find(AssessmentCatalog.E7, "T1").Evaluated);
        Assert.IsNull(r.Find(AssessmentCatalog.E1, "T1"), "привилегированное судно не обязано маневрировать");
    }

    [Test]
    public void StandOn_TurnsToPortEarly_ViolationRule17()
    {
        Synth s = CrossingFromPort();
        s.Turns.Add((20, -40, 300));
        AssessmentResult r = s.Run(900);
        Print(r);
        Assert.IsFalse(r.Find(AssessmentCatalog.E6, "T1").Passed, "изменил курс до момента, когда действия разрешены");
        Assert.IsFalse(r.Find(AssessmentCatalog.E7, "T1").Passed, "поворот влево на судно слева — правило 17 в");
        Assert.IsFalse(r.Passed);
    }

    // ─────────── Обгон ───────────
    [Test]
    public void Overtaking_BothDirections_Rule13()
    {
        // Своё судно 15 уз догоняет цель 6 уз впереди в 50 м правее
        var s = new Synth { OwnCourse = 0, OwnSpeed = 15, TgtE = 50, TgtN = 0.5 * Nm, TgtCourse = 0, TgtSpeed = 6, RequiredCpaNm = 0.05 };
        AssessmentResult r = s.Run(400);
        Print(r);
        Assert.AreEqual(EncounterSituation.Overtaking, r.Encounters[0].Situation);
        Assert.IsFalse(r.Find(AssessmentCatalog.E8, "T1").Passed, "прошёл в 50 м при CPA_доп 93 м");

        // Цель 15 уз догоняет своё судно 6 уз
        var s2 = new Synth { OwnCourse = 0, OwnSpeed = 6, TgtE = 400, TgtN = -1.5 * Nm, TgtCourse = 0, TgtSpeed = 15, RequiredCpaNm = 0.3 };
        AssessmentResult r2 = s2.Run(1200);
        Print(r2);
        Assert.AreEqual(EncounterSituation.Overtaken, r2.Encounters[0].Situation);
        Assert.IsTrue(r2.Find(AssessmentCatalog.E6, "T1").Passed, "курс и скорость сохранены до D ≤ 1 мили");
        Assert.IsFalse(r2.Find(AssessmentCatalog.E8, "T1").Passed, "цель прошла в 400 м при CPA_доп 0,3 мили — это нарушение цели, но E8 оценивает результат расхождения");
    }

    // ─────────── Обнаружение ───────────
    [Test]
    public void Detection_NightByLights_DayByVisibility()
    {
        double vNight = WeatherVisibility.VisibilityM(WeatherPresetId.Night);
        double vModerate = WeatherVisibility.VisibilityM(WeatherPresetId.Moderate);
        Assert.AreEqual(11539, vNight, 1.0, "V = 1,7308/0,00015");
        // Табл. 8.6: ночь после B3 (V = 6,23 мили) — топовый 6 миль 4,23, огонь 3 мили 2,37, огонь 2 мили 1,67
        Assert.AreEqual(4.23, DetectionModel.RangeNm(161, 0, TimeOfDay.Night, vNight, 0), 0.01, "сухогруз с носа — топовый");
        Assert.AreEqual(2.37, DetectionModel.RangeNm(161, 180, TimeOfDay.Night, vNight, 0), 0.01, "с кормы — кормовой (3 мили)");
        Assert.AreEqual(1.67, DetectionModel.RangeNm(45, 180, TimeOfDay.Night, vNight, 0), 0.01, "малое судно с кормы — кормовой 2 мили (T3 B3-01)");
        Assert.AreEqual(4.23, DetectionModel.RangeNm(161, 112.5, TimeOfDay.Twilight, vNight, 0), 0.01, "граница сектора топового — топовый; сумерки — по огням");
        Assert.AreEqual(vModerate / Nm, DetectionModel.RangeNm(161, 0, TimeOfDay.Day, vModerate, 0), 1e-9, "днём — видимость");
        Assert.AreEqual(5.0, DetectionModel.RangeNm(161, 0, TimeOfDay.Night, vNight, 5.0), 1e-9, "задана сценарием");

        // Смена времени суток событием: днём в штиль V = 3,11 мили; ночью топовый 6 миль при V 3,11 — меньше
        double dayRange = DetectionModel.RangeNm(100, 0, TimeOfDay.Day, WeatherVisibility.VisibilityM(WeatherPresetId.Calm), 0);
        double nightRange = DetectionModel.RangeNm(100, 0, TimeOfDay.Night, WeatherVisibility.VisibilityM(WeatherPresetId.Calm), 0);
        TestContext.WriteLine($"штиль: днём {dayRange:F2} мили, ночью (топовый 6 миль) {nightRange:F2} мили");
        var s = new Synth { OwnCourse = 0, TgtE = -60, TgtN = 4 * Nm, TgtCourse = 180 };
        s.Ctx.DetectionRangeNm = 0;
        s.TimeChanges.Add((10, TimeOfDay.Night));
        s.Run(300);
        TargetTrack tr = s.A.Targets[0];
        Assert.IsTrue(tr.Detected);
        Assert.AreEqual(nightRange, tr.DetectionRangeNm, 1e-9, "после события — ночная дальность");
        Assert.LessOrEqual(tr.DetectRangeNm, nightRange + 1e-9);
        Assert.Greater(tr.DetectRangeNm, nightRange - 24 * Kn * 0.2 / Nm - 1e-9, "обнаружена на первом срезе внутри дальности");
    }

    // ─────────── Итог ───────────
    [Test]
    public void Score_GroupsWeightsAndMandatory()
    {
        var set = AssessmentSettings.Default();
        var r = new AssessmentResult();
        CriterionResult Item(string code, double score, bool passed)
        {
            var c = new CriterionResult { Info = AssessmentCatalog.Find(code), Setting = set.Get(code), Evaluated = true, Score = score, Passed = passed };
            r.Items.Add(c);
            return c;
        }
        Item(AssessmentCatalog.E8, 1.0, true);
        Item(AssessmentCatalog.E8, 0.5, true);     // по второй цели — среднее 0,75
        Item(AssessmentCatalog.E3, 1.0, true);
        Item(AssessmentCatalog.E9, 0.0, false);    // не обязательный
        Item(AssessmentCatalog.R1, 1.0, true);
        r.Aggregate(set);
        // Расхождение: (10·0,75 + 10·1)/20 = 0,875; реакция 0; маршрут 1
        // Итог: (0,6·0,875 + 0,1·0 + 0,3·1)/1,0 = 0,825
        Assert.AreEqual(87.5, r.GroupScores[AssessmentGroup.Encounters], 1e-9);
        Assert.AreEqual(82.5, r.Score, 1e-9);
        Assert.IsTrue(r.Passed, "незачёт необязательного критерия не мешает зачёту при балле ≥ 70");

        Item(AssessmentCatalog.E4, 0.0, false);    // обязательный
        r.Aggregate(set);
        CollectionAssert.Contains(r.MandatoryFailed, AssessmentCatalog.E4);
        Assert.IsFalse(r.Passed);

        Assert.AreEqual(0.5, AssessmentResult.ScoreOf(2.0, set.Get(AssessmentCatalog.E2), true), 1e-9, "E2: 3 мин — 1, 1 мин — 0");
        Assert.AreEqual(0.5, AssessmentResult.ScoreOf(75, set.Get(AssessmentCatalog.E9), false), 1e-9, "E9: 30 с — 1, 120 с — 0");
    }

    // ─────────── Прогоны сценариев ───────────
    internal static ShipParameters Model45m()
    {
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        p.name = "ShipParameters_Model45m";
        p.length = 45f; p.serviceSpeedKnots = 15f;
        p.accelerationTimeConstant = 30f; p.decelerationTimeConstant = 60f; p.asternFactor = 0.6f;
        p.nomotoKPrime = 0.8f; p.nomotoTPrime = 2f; p.minCalcSpeed = 0.05f;
        p.maxRudderAngle = 35f; p.rudderRate = 65f / 28f;
        p.beam = 8.71f; p.draft = 3f;
        return p;
    }

    internal static ShipParameters Mariner()
    {
        var p = ScriptableObject.CreateInstance<ShipParameters>();
        p.name = "ShipParameters_Mariner";
        p.length = 160.93f; p.serviceSpeedKnots = 15.001633f;
        p.accelerationTimeConstant = 95.196686f; p.decelerationTimeConstant = 150f; p.asternFactor = 0.6f;
        p.nomotoKPrime = 3.4845078f; p.nomotoTPrime = 3.969945f; p.minCalcSpeed = 0.05f;
        p.yawModel = YawModel.Norrbin; p.norrbinAlphaPrime = 53.740433f; p.turnSpeedLossCoeff = 0.8062914f;
        p.maxRudderAngle = 35f; p.rudderRate = 5f;
        p.beam = 23.16f; p.draft = 8f;
        return p;
    }

    public static ScenarioDefinition DemoB2()
    {
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoScenario.Fill(s, area, Model45m());
        return s;
    }

    public static ScenarioDefinition DemoB3()
    {
        var area = ScriptableObject.CreateInstance<WaterArea>();
        DemoWaterArea.Fill(area);
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoTrafficScenario.Fill(s, area, Model45m(), Mariner(), Mariner(), Model45m());
        return s;
    }

    // Критерий по цели: при нескольких сближениях с одной целью объект — «T2 (1)», «T2 (2)»
    private static CriterionResult Of(AssessmentResult r, string code, string target) =>
        r.Items.Find(c => c.Code == code && (c.Subject == target || c.Subject.StartsWith(target + " (")));

    /// <summary>Прогоны B3-01 (DemoAssessmentRuns): без манёвра, правильные действия, поворот влево, поздно.</summary>
    public static ExerciseSimulation B3Run(string variant, string folder = "")
    {
        var sim = new ExerciseSimulation { Scenario = DemoB3(), RecordFolder = folder };
        DemoAssessmentRuns.Configure(sim, variant);
        sim.Run();
        return sim;
    }

    [Test]
    public void Route_WheelOverAndTurn_B2()
    {
        var good = new ExerciseSimulation { Scenario = DemoB2() };
        good.Run();
        Print(good.LiveResult);
        Assert.AreEqual(ScenarioEndReason.Arrived, good.EndReason);
        Assert.IsTrue(good.LiveResult.Find(AssessmentCatalog.R4).Passed);
        Assert.IsTrue(good.LiveResult.Find(AssessmentCatalog.R5).Passed);
        Assert.IsTrue(good.LiveResult.Find(AssessmentCatalog.R6, "Поворот").Passed, "поворот с упреждением — малое отклонение");
        Assert.IsTrue(good.LiveResult.Find(AssessmentCatalog.R7, "Поворот").Passed, "перекладка — в рекомендуемой точке");

        var late = new ExerciseSimulation { Scenario = DemoB2(), WheelOverFactor = 0 };
        late.Run();
        Print(late.LiveResult);
        Assert.IsFalse(late.LiveResult.Find(AssessmentCatalog.R6, "Поворот").Passed, "поворот без упреждения — вынос наружу (табл. 6.4)");
        Assert.IsFalse(late.LiveResult.Find(AssessmentCatalog.R7, "Поворот").Passed, "перекладка в точке поворота — поздно");
        Assert.Less(late.LiveResult.Score, good.LiveResult.Score);
    }

    [Test]
    public void B3_NoManoeuvre_CollisionIsFailureRegardlessOfScore()
    {
        ExerciseSimulation sim = B3Run("none");
        Print(sim.LiveResult);
        Assert.AreEqual(ScenarioEndReason.Collision, sim.EndReason);
        Assert.IsTrue(sim.LiveResult.Failed);
        Assert.IsFalse(sim.LiveResult.Passed);
        StringAssert.Contains("T2", sim.LiveResult.FailureText);
        Assert.AreEqual(0.0, Of(sim.LiveResult, AssessmentCatalog.E8, "T2").Value, 1e-9);
        Assert.IsFalse(Of(sim.LiveResult, AssessmentCatalog.E1, "T2").Passed, "манёвра нет");
    }

    [Test]
    public void B3_Variants_CorrectPassesPortTurnViolates()
    {
        ExerciseSimulation ok = B3Run("correct");
        TestContext.WriteLine("── правильные действия ──");
        Print(ok.LiveResult);
        ExerciseSimulation port = B3Run("port");
        TestContext.WriteLine("── поворот влево на T2 ──");
        Print(port.LiveResult);

        Assert.AreEqual(ScenarioEndReason.Arrived, ok.EndReason);
        Assert.AreEqual(EncounterSituation.CrossingGiveWay, ok.LiveResult.Encounters.Find(e => e.TargetId.StartsWith("T2")).Situation);
        Assert.IsTrue(Of(ok.LiveResult, AssessmentCatalog.E4, "T2").Passed);
        Assert.IsTrue(ok.LiveResult.Passed, "правильные действия — зачёт");

        CriterionResult e5 = Of(port.LiveResult, AssessmentCatalog.E5, "T2");
        Assert.IsTrue(e5 != null && e5.Evaluated && !e5.Passed, "поворот влево на цель справа");
        Assert.IsFalse(port.LiveResult.Passed);
    }

    // ─────────── Приёмка C2 ───────────
    [Test]
    public void LiveAndOffline_SameSession_IdenticalResults()
    {
        string dir = Path.Combine(Path.GetTempPath(), "VRBridgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            ExerciseSimulation sim = B3Run("correct", dir);
            AssessmentResult live = sim.LiveResult;
            RecordedSession rs = SessionReader.Load(dir);
            CollectionAssert.IsEmpty(rs.Warnings, string.Join("; ", rs.Warnings));
            AssessmentResult offline = SessionAssessment.Assess(rs);

            Assert.AreEqual(live.Items.Count, offline.Items.Count);
            for (int i = 0; i < live.Items.Count; i++)
            {
                CriterionResult a = live.Items[i], b = offline.Items[i];
                string tag = $"{a.Code} {a.Subject}";
                Assert.AreEqual(a.Code, b.Code, tag);
                Assert.AreEqual(a.Subject, b.Subject, tag);
                Assert.AreEqual(a.Evaluated, b.Evaluated, tag);
                Assert.IsTrue(a.Value.Equals(b.Value), $"{tag}: {a.Value:R} ≠ {b.Value:R}");
                Assert.AreEqual(a.Passed, b.Passed, tag);
                Assert.IsTrue(a.Score.Equals(b.Score), tag);
                Assert.AreEqual(a.Note, b.Note, tag);
            }
            Assert.IsTrue(live.Score.Equals(offline.Score));
            Assert.AreEqual(live.Passed, offline.Passed);
            Assert.AreEqual(live.Encounters.Count, offline.Encounters.Count);

            // Файл assessment.csv, записанный «в шлеме», совпадает с пересчётом по записи
            string file = File.ReadAllText(Path.Combine(dir, SessionFormat.AssessmentFile));
            Assert.AreEqual(file.TrimStart('﻿'), offline.ToCsv());
            Assert.IsTrue(rs.Meta.assessment.assessed);
            Assert.AreEqual(Math.Round(live.Score, 1), rs.Meta.assessment.score, 1e-9);
            TestContext.WriteLine($"Сессия: {rs.Track.Count} строк траектории, {rs.Events.Count} событий; {live.Headline()}, критериев {live.EvaluatedCount}");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }

    [Test]
    public void Settings_SnapshotInSessionJson_UsedOffline()
    {
        ScenarioDefinition s = DemoB3();
        var asset = ScriptableObject.CreateInstance<AssessmentCriteria>();
        asset.settings.defaultRequiredCpaNm = 0.8;
        asset.settings.Get(AssessmentCatalog.E9).pass = 45;
        s.assessment = asset;
        SessionMeta meta = SessionMeta.Create(s, s.shipParameters, "1", new DateTime(2026, 10, 11, 12, 0, 0));
        var back = new SessionMeta();
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(meta, true), back);
        AssessmentContext ctx = AssessmentContext.From(back);
        Assert.AreEqual(0.8, ctx.Settings.defaultRequiredCpaNm, 1e-12);
        Assert.AreEqual(45.0, ctx.Settings.Get(AssessmentCatalog.E9).pass, 1e-12);
        Assert.AreEqual(AssessmentCatalog.All.Count, ctx.Settings.criteria.Count);
        Assert.AreEqual(DemoTrafficScenario.FairwayRequiredCpaNm, ctx.Target("T1").RequiredCpaNm, 1e-6);
        Assert.IsTrue(ctx.Route[2].narrowChannel && ctx.Route[3].narrowChannel && !ctx.Route[1].narrowChannel);
        Assert.IsNotNull(ctx.Own);
        Assert.AreEqual(45.0, ctx.Own.length, 1e-6);

        // Сессия до C2 — без снимка: значения по умолчанию
        var old = new SessionMeta();
        JsonUtility.FromJsonOverwrite("{\"format\":\"VRBridge.Session\"}", old);
        Assert.AreEqual(0.5, AssessmentContext.From(old).Settings.defaultRequiredCpaNm, 1e-12);
        Assert.AreEqual(60.0, AssessmentContext.From(old).Settings.Get(AssessmentCatalog.E9).pass, 1e-12);
    }
}
