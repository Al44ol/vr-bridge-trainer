// ============================================================
// RecordingTests.cs — EditMode-тесты записи упражнения (C1)
// Window → General → Test Runner → EditMode → Run All.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

public class RecordingTests
{
    private const double Dt = 0.02;
    private const double Deg = 180.0 / Math.PI;

    private static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "VRBridgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static void Delete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { }
    }

    // ─────────── Формат CSV ───────────
    [Test]
    public void Csv_InvariantNumbers_Quoting_NaNEmpty()
    {
        var b = new StringBuilder();
        CsvFormat.Num(b, 1234.5678, 2); CsvFormat.Sep(b);
        CsvFormat.Num(b, -0.5, 1); CsvFormat.Sep(b);
        CsvFormat.Num(b, double.NaN, 2); CsvFormat.Sep(b);
        CsvFormat.Text(b, "Отмель; у поворота"); CsvFormat.Sep(b);
        CsvFormat.Text(b, "знак \"А\""); CsvFormat.Sep(b);
        CsvFormat.Text(b, "две\nстроки");
        Assert.AreEqual("1234.57;-0.5;;\"Отмель; у поворота\";\"знак \"\"А\"\"\";две строки", b.ToString());

        List<string> f = CsvFormat.Split(b.ToString());
        CollectionAssert.AreEqual(new[] { "1234.57", "-0.5", "", "Отмель; у поворота", "знак \"А\"", "две строки" }, f);
        Assert.AreEqual(1234.57, CsvFormat.ParseDouble(f[0]), 1e-12);
        Assert.IsTrue(double.IsNaN(CsvFormat.ParseDouble(f[2])));
    }

    [Test]
    public void TrackSampler_5Hz_ExactTimesWithoutDrift()
    {
        var s = new TrackSampler(5);
        var times = new List<double>();
        double t = 0;
        for (int i = 0; i <= 90000; i++)   // 30 мин шагами 0,02 с
        {
            if (s.Due(t)) times.Add(t);
            t += Dt;
        }
        Assert.AreEqual(9001, times.Count, "t = 0; 0,2; … 1800 с");
        for (int k = 0; k < times.Count; k++) Assert.AreEqual(k * 0.2, times[k], 1e-6, $"строка {k}");
    }

    // ─────────── Запись → чтение ───────────
    [Test]
    public void Session_WriteThenRead_RoundTrip_Utf8Bom()
    {
        string dir = TempDir();
        try
        {
            var meta = new SessionMeta { session_id = "test", trainee_id = "017" };
            meta.scenario.id = "T-01";
            meta.scenario.route.Add(new SessionWaypoint { name = "Старт", x_e_m = 0, y_n_m = -2500, xte_limit_m = 250 });
            meta.scenario.route.Add(new SessionWaypoint { name = "Вход", x_e_m = 0, y_n_m = -300, xte_limit_m = 250 });
            var rec = new SessionRecorder(dir, meta, background: true);
            for (int i = 0; i < 50; i++)
                rec.AddTrack(new TrackSample
                {
                    Time = i * 0.2, East = 1.234 * i, North = -2500 + 1.5 * i, HeadingDeg = 359.5, CogDeg = 359.5,
                    SpeedKnots = -1.25, RotDegMin = 12.3, RudderOrderedDeg = -10, RudderActualDeg = -7.77,
                    TelegraphSf = 0.65, Telegraph = (int)TelegraphOrder.HalfAhead, XteM = -3.21, Leg = 0, XteLimitM = 250, Grounded = i == 49
                });
            ExerciseEvents.Time = 4.0;
            rec.AddEvent(new ExerciseEvent(ExerciseEventType.ShoalWarningStarted)
            {
                Time = 4.0, Index = 2, Value = 185.2, Value2 = 2.5, Subject = "Отмель; \"у поворота\"",
                Position = new WorldPosition(12.5, -2400), HeadingDeg = 1.5f, SpeedKnots = 14.9f
            });
            rec.AddEvent(new ExerciseEvent(ExerciseEventType.ExerciseEnded) { Time = 9.8, EndReason = ScenarioEndReason.Grounding, Value = 1, Value2 = 3 });
            rec.Flush();
            rec.AddTarget(new TargetSample { Time = 0.2, Id = "Цель 1", East = 100, North = 200, HeadingDeg = 90, SpeedKnots = 10 });
            meta.completed = true;
            Assert.IsTrue(rec.Close(5000));
            Assert.IsNull(rec.Writer.LastError);

            foreach (string f in new[] { SessionFormat.TrackFile, SessionFormat.TargetsFile, SessionFormat.EventsFile, SessionFormat.MetaFile })
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(dir, f));
                Assert.IsTrue(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, $"{f}: UTF-8 с BOM");
            }
            string[] lines = File.ReadAllLines(Path.Combine(dir, SessionFormat.TrackFile), Encoding.UTF8);
            Assert.AreEqual(SessionFormat.TrackHeader, lines[0]);
            Assert.AreEqual(51, lines.Length);
            StringAssert.Contains("-7.77", lines[1], "точка, не запятая");

            RecordedSession s = SessionReader.Load(dir);
            CollectionAssert.IsEmpty(s.Warnings);
            Assert.AreEqual("017", s.Meta.trainee_id);
            Assert.IsTrue(s.Meta.completed);
            Assert.AreEqual(2, s.Meta.scenario.route.Count);
            Assert.AreEqual(50, s.Meta.summary.track_rows);
            Assert.AreEqual(50, s.Track.Count);
            TrackSample t10 = s.Track[10];
            Assert.AreEqual(2.0, t10.Time, 1e-9);
            Assert.AreEqual(12.34, t10.East, 1e-9);
            Assert.AreEqual(-1.25, t10.SpeedKnots, 1e-9);
            Assert.AreEqual(-7.77, t10.RudderActualDeg, 1e-9);
            Assert.AreEqual((int)TelegraphOrder.HalfAhead, t10.Telegraph);
            Assert.AreEqual(0, t10.Leg);
            Assert.IsFalse(t10.Grounded);
            Assert.IsTrue(s.Track[49].Grounded);

            Assert.AreEqual(2, s.Events.Count);
            ExerciseEvent e = s.Events[0];
            Assert.AreEqual(ExerciseEventType.ShoalWarningStarted, e.Type);
            Assert.AreEqual("Отмель; \"у поворота\"", e.Subject);
            Assert.AreEqual(2, e.Index);
            Assert.AreEqual(185.2, e.Value, 1e-9);
            Assert.AreEqual(12.5, e.Position.East, 1e-9);
            Assert.AreEqual(ScenarioEndReason.Grounding, s.Events[1].EndReason);
            Assert.AreEqual(-1, s.Events[1].Index, "пустая ячейка → −1");
            Assert.AreEqual(1, s.Targets.Count);
            Assert.AreEqual("Цель 1", s.Targets[0].Id);
        }
        finally { Delete(dir); ExerciseEvents.Reset(); }
    }

    [Test]
    public void Reader_ColumnsByHeader_ExtraColumnsIgnored()
    {
        string dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, SessionFormat.TrackFile),
                "extra;heading_deg;t_s;x_e_m;y_n_m\nzzz;10.5;0.2;1;2\n", new UTF8Encoding(true));
            RecordedSession s = SessionReader.Load(dir);
            Assert.AreEqual(1, s.Track.Count);
            Assert.AreEqual(0.2, s.Track[0].Time, 1e-12);
            Assert.AreEqual(10.5, s.Track[0].HeadingDeg, 1e-12);
            Assert.AreEqual(2.0, s.Track[0].North, 1e-12);
            Assert.IsTrue(double.IsNaN(s.Track[0].XteM), "нет столбца — NaN");
            Assert.IsTrue(s.Warnings.Exists(w => w.Contains(SessionFormat.EventsFile)), "отсутствующий файл — замечание, не ошибка");
        }
        finally { Delete(dir); }
    }

    [Test]
    public void StateAt_InterpolatesHeadingThroughNorth()
    {
        var s = new RecordedSession();
        s.Track.Add(new TrackSample { Time = 0, East = 0, North = 0, HeadingDeg = 358, Leg = 0, XteM = 10 });
        s.Track.Add(new TrackSample { Time = 1, East = 10, North = 20, HeadingDeg = 4, Leg = 0, XteM = 20, Telegraph = 3 });
        TrackSample m = s.StateAt(0.5);
        Assert.AreEqual(1.0, m.HeadingDeg, 1e-9, "358° → 4° через 0°");
        Assert.AreEqual(5.0, m.East, 1e-9);
        Assert.AreEqual(15.0, m.XteM, 1e-9);
        Assert.AreEqual(0, m.Telegraph, "дискретные величины — по предыдущей строке");
        Assert.AreEqual(358.75, s.StateAt(0.125).HeadingDeg, 1e-9);
        Assert.AreEqual(4.0, s.StateAt(5).HeadingDeg, 1e-9, "после конца — последняя строка");
    }

    // ─────────── Действия обучающегося ───────────
    private static ControlState Ctl(double rudder, bool grabbed = false, int tel = (int)TelegraphOrder.FullAhead, bool telGrabbed = false,
                                    bool horn = false, int weather = (int)WeatherPresetId.Moderate) =>
        new ControlState
        {
            RudderOrderedDeg = rudder, WheelGrabbed = grabbed,
            Telegraph = tel, TelegraphSf = ScenarioText.SpeedFactor((TelegraphOrder)tel), TelegraphGrabbed = telGrabbed,
            HornSounding = horn, WeatherPreset = weather
        };

    private static List<(double t, ExerciseEvent e)> Run(ControlActionDetector d, Func<double, ControlState> f, double until)
    {
        var list = new List<(double, ExerciseEvent)>();
        double t = 0;
        d.Reset(t, f(t));
        for (int i = 1; i * Dt <= until + 1e-9; i++)
        {
            t = i * Dt;
            foreach (ExerciseEvent e in d.Update(t, f(t))) list.Add((t, e));
        }
        return list;
    }

    [Test]
    public void Helm_StartAndEnd_BySettleAndByRelease()
    {
        var d = new ControlActionDetector();
        // 1–3 с: штурвал в руке, руль 0 → 20° ПБ (10 °/с), держат неподвижно до 6 с, отпускают в 6 с;
        // 8–9 с: снова берут, 20° → 15°, отпускают в 9 с.
        List<(double t, ExerciseEvent e)> ev = Run(d, t =>
        {
            bool grabbed = (t >= 1 && t < 6) || (t >= 8 && t < 9);
            double r = t < 1 ? 0 : t < 3 ? (t - 1) * 10 : t < 8 ? 20 : t < 8.5 ? 20 - (t - 8) * 10 : 15;
            return Ctl(r, grabbed);
        }, 12);

        Assert.AreEqual(4, ev.Count, string.Join("\n", ev.ConvertAll(x => $"{x.t:F2} {x.e.Describe()}")));
        Assert.AreEqual(ExerciseEventType.HelmStarted, ev[0].e.Type);
        Assert.AreEqual(1.06, ev[0].t, 0.03, "начало — когда отклонение ≥ 0,5° (1,06 с: 0,6°)");
        Assert.AreEqual(0.0, ev[0].e.Value);
        Assert.AreEqual(ExerciseEventType.HelmEnded, ev[1].e.Type);
        Assert.AreEqual(20.0, ev[1].e.Value, 1e-9);
        Assert.AreEqual(4.0, ev[1].t, 0.03, "конец — 1 с без движения (последнее движение в 3 с)");
        Assert.AreEqual(3.0 - 1.06, ev[1].e.Value2, 0.03, "длительность — до последнего движения");
        Assert.AreEqual(ExerciseEventType.HelmStarted, ev[2].e.Type);
        Assert.AreEqual(20.0, ev[2].e.Value, 1e-9);
        Assert.AreEqual(ExerciseEventType.HelmEnded, ev[3].e.Type);
        Assert.AreEqual(9.0, ev[3].t, 0.03, "отпустили штурвал — конец сразу");
        Assert.AreEqual(15.0, ev[3].e.Value, 1e-9);
    }

    [Test]
    public void Helm_ChangedByCodeWithoutHand_NoEvents()
    {
        var d = new ControlActionDetector();
        // Начальные условия сценария: руль ставится программно (SetRudderOrder), штурвал не в руке
        List<(double t, ExerciseEvent e)> ev = Run(d, t => Ctl(t < 2 ? 0 : 10), 5);
        Assert.AreEqual(0, ev.Count);
        // …а потом обучающийся берёт штурвал и не двигает его — тоже не перекладка
        ev = Run(d, t => Ctl(10, grabbed: t > 1 && t < 3), 5);
        Assert.AreEqual(0, ev.Count);
    }

    [Test]
    public void Telegraph_IntermediatePositionsIgnored_SettledOrderRecorded()
    {
        var d = new ControlActionDetector();
        // Рычаг «Полный вперёд» → «Стоп» за 0,3 с (через средний, малый, самый малый), держат 2 с и отпускают;
        // потом инструктор ставит «Полный назад» кодом.
        List<(double t, ExerciseEvent e)> ev = Run(d, t =>
        {
            int tel = t < 1 ? 0 : t < 1.1 ? 1 : t < 1.2 ? 2 : t < 1.3 ? 3 : 4;
            if (t >= 5) tel = (int)TelegraphOrder.FullAstern;
            ControlState s = Ctl(0, tel: tel, telGrabbed: t >= 1 && t < 3.3);
            s.TelegraphByCode = t >= 5;
            return s;
        }, 6);

        Assert.AreEqual(2, ev.Count, string.Join("\n", ev.ConvertAll(x => $"{x.t:F2} {x.e.Describe()}")));
        Assert.AreEqual(ExerciseEventType.TelegraphChanged, ev[0].e.Type);
        Assert.AreEqual((int)TelegraphOrder.Stop, ev[0].e.Index);
        Assert.AreEqual(0.0, ev[0].e.Value, 1e-9);
        Assert.AreEqual(1.0, ev[0].e.Value2, 1e-9, "прежний SF — «Полный вперёд», а не промежуточный");
        Assert.AreEqual(1.3 + 0.5, ev[0].t, 0.03, "положение устоялось 0,5 с");
        Assert.IsFalse(ev[0].e.ByInstructor);
        Assert.AreEqual((int)TelegraphOrder.FullAstern, ev[1].e.Index);
        Assert.AreEqual(5.0, ev[1].t, 0.03, "кодом, без руки — сразу");
        Assert.IsTrue(ev[1].e.ByInstructor);
        Assert.AreEqual("Полный назад", ev[1].e.Subject);
    }

    [Test]
    public void Horn_Duration_And_WeatherChange()
    {
        var d = new ControlActionDetector();
        List<(double t, ExerciseEvent e)> ev = Run(d, t => Ctl(0, horn: t >= 1 && t < 4, weather: t < 5 ? 1 : (int)WeatherPresetId.Storm), 6);
        Assert.AreEqual(3, ev.Count);
        Assert.AreEqual(ExerciseEventType.HornStarted, ev[0].e.Type);
        Assert.AreEqual(ExerciseEventType.HornEnded, ev[1].e.Type);
        Assert.AreEqual(3.0, ev[1].e.Value, 0.03);
        Assert.AreEqual(ExerciseEventType.WeatherChanged, ev[2].e.Type);
        Assert.AreEqual((int)WeatherPresetId.Storm, ev[2].e.Index);
        Assert.AreEqual((double)WeatherPresetId.Moderate, ev[2].e.Value);
        Assert.AreEqual("шторм", ev[2].e.Subject);
    }

    // ─────────── Хранение ───────────
    [Test]
    public void Rotation_KeepsNewestAndMarked()
    {
        string root = TempDir();
        try
        {
            var names = new List<string>();
            for (int i = 0; i < 6; i++)
            {
                string name = SessionFormat.FolderName(new DateTime(2026, 10, 11, 10, i, 0), "B2-01", i.ToString());
                names.Add(name);
                string dir = SessionStorage.CreateFolder(root, name);
                File.WriteAllText(Path.Combine(dir, SessionFormat.MetaFile), JsonUtility.ToJson(new SessionMeta()));
            }
            Directory.CreateDirectory(Path.Combine(root, "чужая папка"));   // не сессия — не трогать
            SessionStorage.SetKept(Path.Combine(root, names[0]), true);      // самая старая — с меткой

            List<string> removed = SessionStorage.Rotate(root, 3);
            List<string> left = SessionStorage.List(root);
            Assert.AreEqual(3, removed.Count, "освобождено место под новую: 2 незащищённые + новая = 3");
            CollectionAssert.AreEqual(new[] { names[5], names[4], names[0] }, left.ConvertAll(Path.GetFileName));
            Assert.IsTrue(Directory.Exists(Path.Combine(root, "чужая папка")));
            Assert.AreEqual(0, SessionStorage.Rotate(root, 0).Count, "0 — без ротации");
            Assert.AreEqual("20261011_100500_B2-01_5", names[5]);
            Assert.AreEqual("20261011_100000_S_1_noid", SessionFormat.FolderName(new DateTime(2026, 10, 11, 10, 0, 0), "S/1", " "));
        }
        finally { Delete(root); }
    }

    // ─────────── Прогон сценария с записью ───────────
    // Та же цепочка, что в ScenarioTests: модель, детектор мели, контроль маршрута, авторулевой
    // с упреждением 150 м; события — через шину, запись — подписчик шины, траектория — 5 Гц.
    [Test]
    public void Demo_RecordedSession_MatchesBusLog()
    {
        string dir = TempDir();
        ExerciseEvents.Reset();
        try
        {
            var area = ScriptableObject.CreateInstance<WaterArea>();
            DemoWaterArea.Fill(area);
            var p = ScriptableObject.CreateInstance<ShipParameters>();
            p.length = 45f; p.serviceSpeedKnots = 15f;
            p.accelerationTimeConstant = 30f; p.decelerationTimeConstant = 60f; p.asternFactor = 0.6f;
            p.nomotoKPrime = 0.8f; p.nomotoTPrime = 2f; p.minCalcSpeed = 0.05f;
            p.maxRudderAngle = 35f; p.rudderRate = 65f / 28f; p.beam = 8.71f; p.draft = 3f;
            var sc = ScriptableObject.CreateInstance<ScenarioDefinition>();
            DemoScenario.Fill(sc, area, p);

            var m = new ShipDynamicsModel(p)
            {
                East = sc.initial.position.East, North = sc.initial.position.North, Psi = sc.initial.headingDeg / Deg,
                U = sc.initial.speedKnots * ShipParameters.KnotsToMs, SpeedFactor = ScenarioText.SpeedFactor(sc.initial.telegraph)
            };
            var route = new RouteTracker();
            var rec = new SessionRecorder(dir, SessionMeta.Create(sc, p, "017", new DateTime(2026, 10, 11, 12, 0, 0)));
            var sampler = new TrackSampler(5);
            double t = 0;

            void Publish(ExerciseEvent e)
            {
                e.Position = new WorldPosition(m.East, m.North);
                e.HeadingDeg = (float)(m.Psi * Deg);
                e.SpeedKnots = (float)(m.U / ShipParameters.KnotsToMs);
                ExerciseEvents.Publish(e);
            }
            void Sample() => rec.AddTrack(new TrackSample
            {
                Time = t, East = m.East, North = m.North, HeadingDeg = m.Psi * Deg, CogDeg = m.Psi * Deg,
                SpeedKnots = m.U / ShipParameters.KnotsToMs, RotDegMin = m.R * Deg * 60, RudderOrderedDeg = m.Ordered,
                RudderActualDeg = m.Delta, TelegraphSf = m.SpeedFactor, Telegraph = (int)ScenarioText.OrderFromSpeedFactor(m.SpeedFactor),
                XteM = route.Xte, Leg = route.Leg, XteLimitM = route.XteLimit, Grounded = m.Grounded
            });
            ExerciseEvents.Raised += e => { if (e.InExercise) rec.AddEvent(e); };

            ExerciseEvents.BeginExercise(sc.id);
            route.Reset(sc.route);
            Publish(new ExerciseEvent(ExerciseEventType.ExerciseStarted) { Subject = sc.title });
            route.Update(new WorldPosition(m.East, m.North), 0, 0);
            if (sampler.Due(t)) Sample();
            bool arrived = false;
            while (t < DemoScenario.TimeLimitSeconds && !arrived)
            {
                int leg = route.Leg;
                double legCourse = route.LegCourseDeg, xte = route.Xte;
                if (route.LegLength - route.Atd < 150.0 && leg + 2 < sc.route.Count)
                {
                    WorldPosition a = sc.route[leg + 1].position, b = sc.route[leg + 2].position;
                    legCourse = Math.Atan2(b.East - a.East, b.North - a.North) * Deg;
                    xte = RouteTracker.CrossTrack(a, b, new WorldPosition(m.East, m.North));
                }
                double err = Math.IEEERemainder((legCourse - Math.Atan(xte / 150.0) * Deg) / Deg - m.Psi, 2 * Math.PI) * Deg;
                m.RudderCommandDeg = Math.Max(-35, Math.Min(35, 1.5 * err - 8.0 * m.R * Deg));
                m.Step(Dt);
                t += Dt;
                ExerciseEvents.Time = t;
                foreach (RouteEvent e in route.Update(new WorldPosition(m.East, m.North), t, Dt))
                {
                    if (e.Kind == RouteEventKind.WaypointPassed)
                        Publish(new ExerciseEvent(ExerciseEventType.WaypointPassed) { Index = e.Index, Subject = e.Name, Value = e.Xte, Value2 = e.Value2 });
                    else if (e.Kind == RouteEventKind.Arrived) arrived = true;
                }
                if (sampler.Due(t)) Sample();
            }
            Publish(new ExerciseEvent(ExerciseEventType.ExerciseEnded) { EndReason = ScenarioEndReason.Arrived, Value = route.PassedCount, Value2 = 3 });
            if (rec.LastTrackTime < t - 1e-6) Sample();
            ExerciseEvents.EndExercise();
            rec.Meta.completed = true;
            rec.Meta.duration_s = t;
            Assert.IsTrue(rec.Close(5000));

            RecordedSession s = SessionReader.Load(dir);
            TestContext.WriteLine($"Прибытие {t:F1} с, строк траектории {s.Track.Count}, событий {s.Events.Count}, {rec.Writer.BytesWritten / 1024} КБ");
            Assert.IsTrue(arrived);
            Assert.AreEqual(ExerciseEvents.Log.Count, s.Events.Count, "в events.csv — весь журнал шины");
            for (int i = 0; i < s.Events.Count; i++)
            {
                Assert.AreEqual(ExerciseEvents.Log[i].Type, s.Events[i].Type, $"событие {i}");
                Assert.AreEqual(ExerciseEvents.Log[i].Time, s.Events[i].Time, 1e-3);
            }
            Assert.AreEqual(5, s.Events.Count, "начало, 3 точки, конец");
            int regular = (int)Math.Floor(t / 0.2 + 1e-6) + 1;   // t = 0; 0,2; … и, если конец не попал в сетку, ещё строка в момент конца
            Assert.That(s.Track.Count, Is.InRange(regular, regular + 1));
            Assert.AreEqual(rec.TrackRows, s.Track.Count);
            Assert.AreEqual(t, s.Track[s.Track.Count - 1].Time, 1e-3, "последняя строка — в момент конца");
            Assert.AreEqual(sc.route.Count, s.Meta.scenario.route.Count, "маршрут сохранён в session.json");
            Assert.AreEqual(p.length, s.Meta.ship.length_m, 1e-6);
            Assert.AreEqual("017", s.Meta.trainee_id);
            Assert.AreEqual("20261011_120000_B2-01_017", s.Meta.session_id);

            // Офлайн-контроль маршрута по записанной траектории (основа C2) даёт те же точки
            var offline = new RouteTracker();
            offline.Reset(s.Meta.scenario.ToRoute());
            int passed = 0;
            for (int i = 0; i < s.Track.Count; i++)
                foreach (RouteEvent e in offline.Update(s.Track[i].Position, s.Track[i].Time, i == 0 ? 0 : s.Track[i].Time - s.Track[i - 1].Time))
                    if (e.Kind == RouteEventKind.WaypointPassed) passed++;
            Assert.AreEqual(3, passed);
        }
        finally { Delete(dir); ExerciseEvents.Reset(); }
    }
}
