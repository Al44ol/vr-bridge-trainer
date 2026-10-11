// ============================================================
// ExerciseSimulation.cs — Прогон упражнения вне сцены (C2)
//
// Для тестов и отчёта (Tools/VR Bridge/Assessment Report): та же цепочка, что
// в тренажёре, — модель движения своего судна (Δt = 0,02 с), цели (TrafficVessel),
// контроль маршрута (RouteTracker), мели (GroundingDetector), контакт корпусов
// (HullContact), запись сессии (SessionRecorder) и оценка в реальном времени
// (LiveAssessment) с теми же срезами 5 Гц.
// «Обучающегося» заменяет авторулевой по маршруту (LOS, упреждение поворота
// как у целей, п. 8.4) со смещением от оси в узкости (правило 9) и манёврами
// по плану: изменение курса на заданный угол на заданное время и/или телеграф.
// Действия публикуются как действия обучающегося: перекладка руля — HelmStarted
// и через 3 с HelmEnded с заданным рулём; поворот маршрута — тоже перекладка.
// Шина ExerciseEvents не используется (статическое состояние сцены не трогается):
// события получают время и признак «в упражнении» здесь же.
// ============================================================
using System;
using System.Collections.Generic;

/// <summary>Манёвр по плану прогона.</summary>
public sealed class SimManoeuvre
{
    /// <summary>Время начала, с упражнения.</summary>
    public double Time;
    /// <summary>Изменение курса от текущего, ° (> 0 — вправо); 0 — без поворота.</summary>
    public double CourseChangeDeg;
    /// <summary>Сколько держать новый курс, с; затем — снова по маршруту.</summary>
    public double HoldSeconds = 300;
    /// <summary>Новое положение телеграфа (SF), NaN — не менять.</summary>
    public double SpeedFactor = double.NaN;
    /// <summary>Новое смещение от оси в узкости, м (NaN — не менять): обгон с другого борта, возврат к правой стороне.</summary>
    public double OffsetM = double.NaN;
    public string Label = "";
}

public sealed class ExerciseSimulation
{
    public ScenarioDefinition Scenario;
    public double Dt = 0.02;
    /// <summary>Смещение от оси в узкости вправо, м (правило 9; 0 — по оси).</summary>
    public double ChannelOffsetM;
    public readonly List<SimManoeuvre> Manoeuvres = new List<SimManoeuvre>();
    /// <summary>Папка записи (пусто — без записи).</summary>
    public string RecordFolder = "";
    public double RateHz = 5.0;
    /// <summary>Шаг, с которым сохраняются траектории для графиков (0 — не сохранять).</summary>
    public double TrackPlotStep = 2.0;
    /// <summary>Упреждение поворота: доля расчётного d_упр (1 — по формуле п. 8.4; 0 — поворот в точке).</summary>
    public double WheelOverFactor = 1.0;

    // Результаты
    public AssessmentResult LiveResult;
    public ScenarioEndReason EndReason;
    public double EndTime;
    public readonly List<ExerciseEvent> Events = new List<ExerciseEvent>();
    public readonly List<List<WorldPosition>> Tracks = new List<List<WorldPosition>>();
    public readonly List<string> TrackNames = new List<string>();

    private ShipDynamicsModel m;
    private double t;
    private SessionRecorder rec;
    private LiveAssessment live;

    public void Run()
    {
        ScenarioDefinition s = Scenario;
        ShipParameters p = s.shipParameters;
        m = new ShipDynamicsModel(p)
        {
            East = s.initial.position.East, North = s.initial.position.North, Psi = s.initial.headingDeg * Math.PI / 180.0,
            U = s.initial.speedKnots * ShipParameters.KnotsToMs, SpeedFactor = ScenarioText.SpeedFactor(s.initial.telegraph)
        };
        var route = new RouteTracker();
        route.Reset(s.route);
        var ap = new HeadingAutopilot();
        var grounding = new GroundingDetector { Area = s.waterArea, Draft = p.draft };
        var vessels = new List<TrafficVessel>();
        var contacts = new List<HullContact>();
        if (s.traffic != null)
            for (int i = 0; i < s.traffic.Count; i++)
            {
                TrafficShipDefinition d = s.traffic[i];
                vessels.Add(new TrafficVessel(i, d.TargetId(i), d, d.parameters, TrafficPlanner.Resolve(d, s.initial)));
                contacts.Add(new HullContact());
            }

        Tracks.Clear(); TrackNames.Clear(); Events.Clear();
        Tracks.Add(new List<WorldPosition>()); TrackNames.Add("своё судно");
        foreach (TrafficVessel v in vessels) { Tracks.Add(new List<WorldPosition>()); TrackNames.Add(v.TargetId); }

        rec = string.IsNullOrEmpty(RecordFolder) ? null
            : new SessionRecorder(RecordFolder, SessionMeta.Create(s, p, "sim", new DateTime(2026, 10, 11, 12, 0, 0)), background: false);
        if (rec != null) { rec.Meta.recording.track_rate_hz = RateHz; rec.Meta.recording.fixed_dt_s = Dt; }
        live = new LiveAssessment();
        live.Output += e => Publish(e);
        live.Begin(AssessmentContext.FromScenario(s, p), RateHz);
        var sampler = new TrackSampler(RateHz);
        double nextPlot = 0;

        // Следование по маршруту (как RouteFollower, со смещением в узкости)
        int fLeg = 0;
        double lookahead = Math.Max(150.0, 3.0 * p.length);
        SimManoeuvre active = null;
        double holdCourse = 0, activeEnd = 0;
        int nextManoeuvre = 0;
        var manoeuvres = new List<SimManoeuvre>(Manoeuvres);
        manoeuvres.Sort((a, b) => a.Time.CompareTo(b.Time));
        double helmEndAt = double.NaN;

        t = 0;
        Publish(new ExerciseEvent(ExerciseEventType.ExerciseStarted) { Subject = s.title });
        route.Update(Pos, 0, 0);
        if (sampler.Due(t)) Sample(route, vessels);

        EndReason = ScenarioEndReason.None;
        while (EndReason == ScenarioEndReason.None)
        {
            // План манёвров
            while (nextManoeuvre < manoeuvres.Count && manoeuvres[nextManoeuvre].Time <= t + 1e-9)
            {
                SimManoeuvre mv = manoeuvres[nextManoeuvre++];
                if (!double.IsNaN(mv.SpeedFactor))
                {
                    double old = m.SpeedFactor;
                    m.SpeedFactor = mv.SpeedFactor;
                    Publish(new ExerciseEvent(ExerciseEventType.TelegraphChanged)
                    {
                        Index = (int)ScenarioText.OrderFromSpeedFactor(mv.SpeedFactor), Value = mv.SpeedFactor, Value2 = old,
                        Subject = ScenarioText.Telegraph(ScenarioText.OrderFromSpeedFactor(mv.SpeedFactor))
                    });
                }
                if (!double.IsNaN(mv.OffsetM))
                {
                    ChannelOffsetM = mv.OffsetM;
                    if (Math.Abs(mv.CourseChangeDeg) <= 1e-6) StartHelm(ref helmEndAt);
                }
                if (Math.Abs(mv.CourseChangeDeg) > 1e-6)
                {
                    active = mv;
                    holdCourse = m.Psi * 180.0 / Math.PI + mv.CourseChangeDeg;
                    activeEnd = t + mv.HoldSeconds;
                    StartHelm(ref helmEndAt);
                }
            }
            if (active != null && t >= activeEnd)
            {
                active = null;
                StartHelm(ref helmEndAt);   // возврат на маршрут — тоже перекладка
            }

            // Заданный курс
            double course;
            if (active != null) course = holdCourse;
            else
            {
                int before = fLeg;
                course = FollowRoute(s.route, ref fLeg, lookahead, p);
                if (fLeg != before) StartHelm(ref helmEndAt);   // упреждение поворота — перекладка
            }
            m.RudderCommandDeg = ap.Rudder(m, course);
            m.Step(Dt);
            foreach (TrafficVessel v in vessels) v.Step(Dt);
            t += Dt;

            if (!double.IsNaN(helmEndAt) && t >= helmEndAt - 1e-9)
            {
                Publish(new ExerciseEvent(ExerciseEventType.HelmEnded) { Value = m.Ordered, Value2 = 3.0 });
                helmEndAt = double.NaN;
            }

            foreach (RouteEvent e in route.Update(Pos, t, Dt))
            {
                switch (e.Kind)
                {
                    case RouteEventKind.WaypointPassed:
                        Publish(new ExerciseEvent(ExerciseEventType.WaypointPassed) { Index = e.Index, Subject = e.Name, Value = e.Xte, Value2 = e.Value2 }); break;
                    case RouteEventKind.CorridorExit:
                        Publish(new ExerciseEvent(ExerciseEventType.CorridorExit) { Index = e.Index, Subject = e.Name, Value = e.Xte, Value2 = e.Value2 }); break;
                    case RouteEventKind.CorridorReturn:
                        Publish(new ExerciseEvent(ExerciseEventType.CorridorReturn) { Index = e.Index, Subject = e.Name, Value = e.Xte, Value2 = e.Value2 }); break;
                    case RouteEventKind.Arrived:
                        if (s.Ends(ScenarioEndConditions.Arrival)) EndReason = ScenarioEndReason.Arrived;
                        break;
                }
            }

            HullFootprint own = HullFootprint.FromShip(m.East, m.North, m.Psi, 0, 0, p.length, p.beam);
            if (EndReason == ScenarioEndReason.None && s.waterArea != null)
            {
                GroundingEvents ge = grounding.Update(own);
                if ((ge & GroundingEvents.Grounded) != 0)
                {
                    m.SetGrounded(true);
                    HazardInfo h = grounding.GroundedOn;
                    Publish(new ExerciseEvent(ExerciseEventType.Grounded) { Index = h.Index, Subject = h.Name, Value = h.Distance, Value2 = h.Depth });
                    if (s.Ends(ScenarioEndConditions.Grounding)) EndReason = ScenarioEndReason.Grounding;
                }
            }
            for (int i = 0; i < vessels.Count && EndReason == ScenarioEndReason.None; i++)
                if (contacts[i].Update(own, vessels[i].Footprint))
                {
                    vessels[i].EngineStopped = true;
                    vessels[i].Velocity(out double ve, out double vn);
                    double oe = m.U * Math.Sin(m.Psi), on = m.U * Math.Cos(m.Psi);
                    double rel = Math.Sqrt((ve - oe) * (ve - oe) + (vn - on) * (vn - on)) / ShipParameters.KnotsToMs;
                    double brg = CpaCalculator.Bearing(vessels[i].Position.East - m.East, vessels[i].Position.North - m.North);
                    Publish(new ExerciseEvent(ExerciseEventType.Collision)
                    {
                        Subject = vessels[i].TargetId, Index = i, Value = rel, Value2 = CpaCalculator.RelativeBearing(brg, m.Psi * 180.0 / Math.PI)
                    });
                    if (s.Ends(ScenarioEndConditions.Collision)) EndReason = ScenarioEndReason.Collision;
                }
            if (EndReason == ScenarioEndReason.None && s.timeLimitSeconds > 0 && s.Ends(ScenarioEndConditions.TimeLimit) && t >= s.timeLimitSeconds)
                EndReason = ScenarioEndReason.TimeLimit;
            if (EndReason == ScenarioEndReason.None && t >= Math.Max(60.0, s.timeLimitSeconds > 0 ? s.timeLimitSeconds : 3600.0) + 1.0)
                EndReason = ScenarioEndReason.Aborted;

            if (EndReason == ScenarioEndReason.None && sampler.Due(t)) Sample(route, vessels);
            if (TrackPlotStep > 0 && t >= nextPlot)
            {
                Tracks[0].Add(Pos);
                for (int i = 0; i < vessels.Count; i++) Tracks[i + 1].Add(vessels[i].Position);
                nextPlot += TrackPlotStep;
            }
        }

        EndTime = t;
        Publish(new ExerciseEvent(ExerciseEventType.ExerciseEnded)
        {
            EndReason = EndReason, Subject = s.title, Value = route.PassedCount, Value2 = route.Valid ? route.WaypointCount - 1 : 0
        });
        if (live.NeedsFinalSample(t) || (rec != null && (double.IsNaN(rec.LastTrackTime) || rec.LastTrackTime < t - 1e-6)))
            Sample(route, vessels);
        LiveResult = live.End();

        if (rec != null)
        {
            rec.Meta.completed = true;
            rec.Meta.end_reason = EndReason.ToString();
            rec.Meta.end_reason_ru = ScenarioText.EndReason(EndReason);
            rec.Meta.duration_s = Math.Round(t, 3);
            var log = new List<ExerciseEvent>();
            foreach (ExerciseEvent e in Events) log.Add(e);
            rec.Meta.summary = SessionSummary.From(ExerciseSummary.From(s, log, route));
            rec.Meta.assessment = SessionAssessmentSummary.From(LiveResult);
            rec.WriteAssessment(LiveResult.ToCsv());
            rec.Close();
        }
    }

    private WorldPosition Pos => new WorldPosition(m.East, m.North);

    private void StartHelm(ref double helmEndAt)
    {
        if (!double.IsNaN(helmEndAt)) Publish(new ExerciseEvent(ExerciseEventType.HelmEnded) { Value = m.Ordered, Value2 = 3.0 - (helmEndAt - t) });
        Publish(new ExerciseEvent(ExerciseEventType.HelmStarted) { Value = m.Ordered });
        helmEndAt = t + 3.0;
    }

    // LOS по маршруту со смещением вправо в узкости; переход на следующий участок — за d_упр до точки
    private double FollowRoute(List<RouteWaypoint> r, ref int leg, double lookahead, ShipParameters p)
    {
        var pos = Pos;
        while (true)
        {
            WorldPosition a = r[leg].position, b = r[leg + 1].position;
            double legCourse = RouteFollower.CourseOf(a, b);
            WorldPosition d = b - a;
            double len = d.Magnitude;
            double atd = ((pos.East - a.East) * d.East + (pos.North - a.North) * d.North) / len;
            if (leg + 2 < r.Count)
            {
                double next = RouteFollower.CourseOf(b, r[leg + 2].position);
                double turn = Math.Abs(Math.IEEERemainder(next - legCourse, 360.0));
                if (len - atd <= WheelOverFactor * RouteFollower.WheelOverDistance(p, Math.Abs(m.U), turn, 20.0)) { leg++; continue; }
            }
            double offset = r[leg + 1].narrowChannel ? ChannelOffsetM : 0.0;
            double xte = RouteTracker.CrossTrack(a, b, pos) - offset;
            return legCourse - Math.Atan(xte / lookahead) * 180.0 / Math.PI;
        }
    }

    private void Publish(ExerciseEvent e)
    {
        e.Time = t;
        e.InExercise = true;
        e.Position = Pos;
        e.HeadingDeg = (float)(m.Psi * 180.0 / Math.PI);
        e.SpeedKnots = (float)(m.U / ShipParameters.KnotsToMs);
        Events.Add(e);
        rec?.AddEvent(e);
        live?.OnEvent(e);
    }

    private readonly List<TargetSample> targetBuffer = new List<TargetSample>();

    private void Sample(RouteTracker route, List<TrafficVessel> vessels)
    {
        double heading = (m.Psi * 180.0 / Math.PI) % 360.0;
        if (heading < 0) heading += 360.0;
        double xte = double.NaN, limit = double.NaN;
        int leg = -1;
        if (route.Valid)
        {
            leg = route.Leg;
            limit = route.XteLimit;
            xte = RouteTracker.CrossTrack(route.Route[leg].position, route.Route[leg + 1].position, Pos);
        }
        var s = new TrackSample
        {
            Time = t, East = m.East, North = m.North, HeadingDeg = heading,
            CogDeg = m.U >= 0 ? heading : (heading + 180.0) % 360.0,
            SpeedKnots = m.U / ShipParameters.KnotsToMs, RotDegMin = m.R * 180.0 / Math.PI * 60.0,
            RudderOrderedDeg = m.Ordered, RudderActualDeg = m.Delta, TelegraphSf = m.SpeedFactor,
            Telegraph = (int)ScenarioText.OrderFromSpeedFactor(m.SpeedFactor),
            XteM = xte, Leg = leg, XteLimitM = limit, Grounded = m.Grounded
        };
        targetBuffer.Clear();
        foreach (TrafficVessel v in vessels)
            targetBuffer.Add(new TargetSample { Time = t, Id = v.TargetId, East = v.Position.East, North = v.Position.North, HeadingDeg = v.HeadingDeg, SpeedKnots = v.SpeedKnots });
        if (rec != null)
        {
            rec.AddTrack(s);
            foreach (TargetSample ts in targetBuffer) rec.AddTarget(ts);
        }
        if (live.Running) live.AddSample(s, targetBuffer);
    }
}

/// <summary>
/// Прогоны сценариев «за обучающегося» для тестов и отчёта ВКР (C2): одни и те же манёвры.
/// B3-01: none — без манёвра; correct — T2 справа: вправо 60° через 30 с на 5 мин, в фарватере —
/// у правой кромки (70 м, правило 9), T3 обгоняется левым бортом (−40 м) и возврат вправо;
/// port — T2: влево 40° (нарушение правила 15); late — как correct, но манёвр T2 через 150 с.
/// B2-01: wheelover — поворот с упреждением по формуле п. 8.4; nowheelover — поворот в точке.
/// </summary>
public static class DemoAssessmentRuns
{
    public static readonly string[] B3Variants = { "none", "correct", "port", "late" };
    public static readonly string[] B2Variants = { "wheelover", "nowheelover" };

    public static string Title(string variant)
    {
        switch (variant)
        {
            case "none": return "без манёвра";
            case "correct": return "правильные действия";
            case "port": return "поворот влево на T2";
            case "late": return "манёвр T2 поздно (150 с)";
            case "wheelover": return "поворот с упреждением";
            case "nowheelover": return "поворот без упреждения";
            default: return variant;
        }
    }

    public static void Configure(ExerciseSimulation sim, string variant)
    {
        switch (variant)
        {
            case "correct":
            case "late":
                double t = variant == "late" ? 150 : 30;
                sim.Manoeuvres.Add(new SimManoeuvre { Time = t, CourseChangeDeg = 60, HoldSeconds = 300, Label = "T2: вправо 60°" });
                sim.Manoeuvres.Add(new SimManoeuvre { Time = 1120, OffsetM = -40, Label = "T3: обгон левым бортом" });
                sim.Manoeuvres.Add(new SimManoeuvre { Time = 1500, OffsetM = 70, Label = "к правой стороне фарватера" });
                sim.ChannelOffsetM = 70;
                break;
            case "port":
                sim.Manoeuvres.Add(new SimManoeuvre { Time = 30, CourseChangeDeg = -40, HoldSeconds = 240, Label = "T2: влево 40°" });
                break;
            case "nowheelover":
                sim.WheelOverFactor = 0;
                break;
        }
    }
}
