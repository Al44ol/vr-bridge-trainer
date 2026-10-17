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
// D1: вводные инструктора (SimFailure) — отказ главного двигателя / рулевого устройства
// в заданный момент и устранение через заданное время; события — как у InstructorInputs.
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
    /// <summary>C4: id цели — манёвр через Time с после первого услышанного туманного сигнала этой цели.</summary>
    public string AfterHeard = "";
}

/// <summary>Вводная инструктора в прогоне (D1).</summary>
public sealed class SimFailure
{
    public double Time;
    /// <summary>true — главный двигатель, false — рулевое устройство.</summary>
    public bool Engine;
    /// <summary>Устранить через, с (NaN — до конца прогона).</summary>
    public double Duration = double.NaN;
}

public sealed class ExerciseSimulation
{
    public ScenarioDefinition Scenario;
    /// <summary>Вводные инструктора (D1).</summary>
    public readonly List<SimFailure> Failures = new List<SimFailure>();
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
    /// <summary>C4: свои туманные сигналы в ограниченную видимость — интервал, с (0 — не подавать); первый — через FogSignalFirstS.</summary>
    public double FogSignalIntervalS;
    public double FogSignalFirstS = 5.0;
    /// <summary>Длительность продолжительного звука, с (правило 32: 4…6 с).</summary>
    public double FogBlastS = 5.0;
    /// <summary>Туманные сигналы целей (C4) — тот же класс, что в сцене (TrafficController).</summary>
    public readonly FogSignalSystem TargetSignals = new FogSignalSystem();

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
        var manoeuvres = new List<SimManoeuvre>();
        var onHeard = new List<SimManoeuvre>();
        foreach (SimManoeuvre mv in Manoeuvres) (string.IsNullOrEmpty(mv.AfterHeard) ? manoeuvres : onHeard).Add(mv);
        manoeuvres.Sort((a, b) => a.Time.CompareTo(b.Time));
        double helmEndAt = double.NaN;

        // C4: ограниченная видимость (видимость сценария не меняется), свои туманные сигналы, сигналы целей
        bool restricted = FogSignals.Restricted(DetectionModel.Visibility(s.weather, s.visibilityMeters), 2.0);
        double nextFog = FogSignalIntervalS > 0 ? FogSignalFirstS : double.NaN;
        var blastPlan = new List<(double start, double end)>();
        double blastStart = double.NaN;
        var parser = new SoundSignalParser();
        var sources = new List<FogSignalSource>();
        var heard = new List<HeardSignal>();
        var heardIds = new HashSet<string>();
        TargetSignals.Reset();

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
            ApplyFailures();

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

            // Свои туманные сигналы: «—» имея ход, «— —» без хода (правило 35 а, б)
            if (restricted && !double.IsNaN(nextFog) && t >= nextFog - 1e-9)
            {
                blastPlan.Add((t, t + FogBlastS));
                if (Math.Abs(m.U) / ShipParameters.KnotsToMs < 0.5) blastPlan.Add((t + FogBlastS + FogSignals.PauseS, t + 2 * FogBlastS + FogSignals.PauseS));
                nextFog += FogSignalIntervalS;
            }
            if (double.IsNaN(blastStart) && blastPlan.Count > 0 && t >= blastPlan[0].start - 1e-9)
            {
                blastStart = t;
                Publish(new ExerciseEvent(ExerciseEventType.HornStarted));
            }
            else if (!double.IsNaN(blastStart) && t >= blastPlan[0].end - 1e-9)
            {
                double duration = t - blastStart;
                BlastKind kind = parser.AddBlast(blastStart, duration);
                Publish(new ExerciseEvent(ExerciseEventType.HornEnded) { Value = duration, Index = (int)kind });
                blastPlan.RemoveAt(0);
                blastStart = double.NaN;
            }
            if (parser.TryComplete(t, !double.IsNaN(blastStart), out SoundSignal sig))
                Publish(new ExerciseEvent(ExerciseEventType.SoundSignal) { Index = (int)sig.Kind, Subject = sig.Pattern, Value = sig.Blasts.Count, Value2 = sig.End - sig.Start });

            // Туманные сигналы целей: событие — когда звук дошёл; манёвры «после сигнала»
            sources.Clear();
            foreach (TrafficVessel v in vessels)
                sources.Add(new FogSignalSource { Id = v.TargetId, Index = v.Index, Position = v.Position, SpeedKnots = v.SpeedKnots, LengthM = v.Model.Parameters.length });
            TargetSignals.Update(t, restricted, Pos, m.Psi * 180.0 / Math.PI, sources, heard);
            foreach (HeardSignal h in heard)
            {
                Publish(new ExerciseEvent(ExerciseEventType.TargetSoundSignal) { Subject = h.Id, Index = (int)h.Kind, Value = h.DistanceM, Value2 = h.QOwnDeg });
                if (!heardIds.Add(h.Id)) continue;
                foreach (SimManoeuvre mv in onHeard)
                {
                    if (mv.AfterHeard != h.Id) continue;
                    var due = new SimManoeuvre
                    {
                        Time = t + mv.Time, CourseChangeDeg = mv.CourseChangeDeg, HoldSeconds = mv.HoldSeconds,
                        SpeedFactor = mv.SpeedFactor, OffsetM = mv.OffsetM, Label = mv.Label
                    };
                    int at = nextManoeuvre;
                    while (at < manoeuvres.Count && manoeuvres[at].Time <= due.Time) at++;
                    manoeuvres.Insert(at, due);
                }
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

    // Вводные: отказ в момент Time, устранение через Duration (события — как в сцене, InstructorInputs)
    private double engineFailedAt, steeringFailedAt;
    private static readonly bool[] FailureKinds = { true, false };

    private void ApplyFailures()
    {
        if (Failures.Count == 0) return;
        foreach (bool engine in FailureKinds)
        {
            bool due = false;
            foreach (SimFailure f in Failures)
                if (f.Engine == engine && t >= f.Time - 1e-9 && (double.IsNaN(f.Duration) || t < f.Time + f.Duration - 1e-9)) due = true;
            bool on = engine ? m.EngineFailed : m.SteeringFailed;
            if (due == on) continue;
            if (engine)
            {
                m.EngineFailed = due;
                if (due) engineFailedAt = t;
                Publish(due
                    ? new ExerciseEvent(ExerciseEventType.EngineFailure) { Value = m.SpeedFactor, Subject = ScenarioText.Telegraph(ScenarioText.OrderFromSpeedFactor(m.SpeedFactor)), ByInstructor = true }
                    : new ExerciseEvent(ExerciseEventType.EngineRestored) { Value = t - engineFailedAt, ByInstructor = true });
            }
            else
            {
                m.SteeringFailed = due;
                if (due) steeringFailedAt = t;
                Publish(due
                    ? new ExerciseEvent(ExerciseEventType.SteeringFailure) { Value = m.Delta, Value2 = m.Ordered, ByInstructor = true }
                    : new ExerciseEvent(ExerciseEventType.SteeringRestored) { Value = t - steeringFailedAt, ByInstructor = true });
            }
        }
    }

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
    /// <summary>C4-01 (туман): правильно; без сигналов; не снизил ход по сигналу; поворот влево на сигнал.</summary>
    public static readonly string[] C4Variants = { "fog_correct", "fog_nosignals", "fog_noslow", "fog_port" };
    /// <summary>
    /// D1, B2-01 с вводными: отказ руля в t = 300 с на 120 с — «Стоп» через 10 с / через 45 с / без реакции;
    /// отказ главного двигателя в t = 300 с на 180 с — «Стоп» через 10 с. После устранения — снова прежний ход.
    /// </summary>
    public static readonly string[] FailureVariants = { "steer_fast", "steer_late", "steer_none", "engine_fast" };
    public const double FailureTime = 300;
    /// <summary>
    /// 19.2б-3в, 19-01 (BerthingSimulation): правильно (подход к линии в 6 м от кромки, «Стоп» за 70 м до середины,
    /// концы «понемногу», с крыла ЛБ); быстро (концы выбирать без остановок); под углом (курс 20° к кромке, «Стоп» за 25 м,
    /// без выравнивания); у штурвала (без перехода на крыло); долго (концы поданы, но выбирать — через 10 мин).
    /// </summary>
    public static readonly string[] BerthVariants = { "berth_correct", "berth_fast", "berth_angle", "berth_helm", "berth_slow" };

    public static void ConfigureBerth(BerthingPlan plan, string variant)
    {
        switch (variant)
        {
            case "berth_fast": plan.CarefulHaul = false; break;
            case "berth_angle": plan.FixedApproachAngleDeg = 20.0; plan.StopLeadM = 25.0; break;
            case "berth_helm": plan.GoToWing = false; break;
            case "berth_slow": plan.HaulDelayS = 600.0; break;
        }
    }

    /// <summary>
    /// 19.3в, 19-02 (BerthingSimulation, ветер 20 уз отжимной): правильно (буксир у миделя с ПБ — вызван на ходу < 1 уз, после остановки
    /// толкать на ЛБ 10 / 25 % по зазору и скорости к причалу, концы — ближе 8 м, «понемногу»); без буксира (концы издалека,
    /// выбирать против ветра); полная тяга (толкать сразу 100 %); поздно (буксир вызван через 2 мин после того, как ход стал меньше 1 уз).
    /// </summary>
    public static readonly string[] TugBerthVariants = { "tugberth_correct", "tugberth_none", "tugberth_full", "tugberth_late" };

    public static void ConfigureTugBerth(BerthingPlan plan, string variant)
    {
        plan.UseTug = variant != "tugberth_none";
        switch (variant)
        {
            case "tugberth_correct": plan.LinesMaxGapM = 8.0; break;   // концы — когда буксир подвёл судно
            case "tugberth_full": plan.LinesMaxGapM = 8.0; plan.TugFixedPower = 1.0; break;
            case "tugberth_late": plan.LinesMaxGapM = 8.0; plan.TugCallDelayS = 120.0; break;   // вызвал через 2 мин — судно снесло ветром
        }
    }
    /// <summary>
    /// 19.4в, 19-03 (TugWorkSimulation, свой буксир, клиент Mariner, ветер 15 уз): правильно (по командам лоцмана); не слушает команды
    /// (толкает и держит всё время «малым» 50 %); опаздывает (ступень лоцмана — через 40 с); полный до причала (100 % до кранцев,
    /// дальше по командам); быстрый подход (касание на 0,8 уз); под углом (курс подхода 45° от перпендикуляра к борту);
    /// рано отпустил упор (упор 0 через 10 с после «держите»).
    /// </summary>
    public static readonly string[] TugWorkVariants =
        { "tugwork_correct", "tugwork_ignore", "tugwork_late", "tugwork_full", "tugwork_fast", "tugwork_angle", "tugwork_release" };

    public static void ConfigureTugWork(TugWorkPlan plan, string variant)
    {
        plan.Name = Title(variant);
        switch (variant)
        {
            case "tugwork_ignore": plan.IgnorePilot = true; plan.PushPower = 0.5; break;
            case "tugwork_late": plan.PilotDelayS = 40.0; break;
            case "tugwork_full": plan.FixedPush = true; plan.PushPower = 1.0; break;
            case "tugwork_fast": plan.ApproachKn = 0.8; break;
            case "tugwork_angle": plan.ApproachAngleDeg = 45.0; break;
            case "tugwork_release": plan.ReleaseEarlyS = 10.0; break;
        }
    }

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
            case "fog_correct": return "правильные действия в тумане";
            case "fog_nosignals": return "без туманных сигналов";
            case "fog_noslow": return "не снизил ход, услышав сигнал";
            case "fog_port": return "поворот влево на сигнал";
            case "steer_fast": return "отказ руля — «Стоп» через 10 с";
            case "steer_late": return "отказ руля — «Стоп» через 45 с";
            case "steer_none": return "отказ руля — без реакции";
            case "engine_fast": return "отказ ГД — «Стоп» через 10 с";
            case "berth_correct": return "швартовка: правильные действия";
            case "berth_fast": return "швартовка: концы выбирать без остановок";
            case "berth_angle": return "швартовка: подход под углом 20°";
            case "berth_helm": return "швартовка: у штурвала, без перехода на крыло";
            case "berth_slow": return "швартовка: концы выбирать через 10 мин";
            case "tugberth_correct": return "с буксиром: правильные действия";
            case "tugberth_none": return "с буксиром: без буксира";
            case "tugberth_full": return "с буксиром: толкать полной тягой";
            case "tugberth_late": return "с буксиром: буксир вызван на 2 мин позже";
            case "tugwork_correct": return "буксир: правильно, по командам лоцмана";
            case "tugwork_ignore": return "буксир: не слушает команды (всё время 50 %)";
            case "tugwork_late": return "буксир: команды с опозданием 40 с";
            case "tugwork_full": return "буксир: полным до причала";
            case "tugwork_fast": return "буксир: быстрый подход 0,8 уз";
            case "tugwork_angle": return "буксир: подход под углом 45°";
            case "tugwork_release": return "буксир: отпустил упор через 10 с после «держите»";
            default: return variant;
        }
    }

    /// <summary>C4-01 «правильно»: снова «Малый вперёд» через … с после первого сигнала T1 (T1 прошла).</summary>
    public const double FogResumeAfterHeardS = 480;

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
            // C4-01: сразу «Малый вперёд» (безопасная скорость), туманный сигнал «—» каждые 100 с
            case "fog_correct":
            case "fog_nosignals":
            case "fog_noslow":
            case "fog_port":
                if (variant != "fog_nosignals") sim.FogSignalIntervalS = 100;
                sim.Manoeuvres.Add(new SimManoeuvre { Time = 10, SpeedFactor = 0.4, Label = "«Малый вперёд» — безопасная скорость" });
                if (variant == "fog_correct" || variant == "fog_nosignals")
                {
                    sim.Manoeuvres.Add(new SimManoeuvre { AfterHeard = "T1", Time = 20, SpeedFactor = 0.2, Label = "сигнал T1 впереди траверза — «Самый малый вперёд»" });
                    sim.Manoeuvres.Add(new SimManoeuvre { AfterHeard = "T1", Time = FogResumeAfterHeardS, SpeedFactor = 0.4, Label = "T1 прошла — «Малый вперёд»" });
                }
                if (variant == "fog_port")
                    sim.Manoeuvres.Add(new SimManoeuvre { AfterHeard = "T1", Time = 20, CourseChangeDeg = -30, HoldSeconds = 240, Label = "сигнал T1 — влево 30°" });
                break;
            // D1: вводные на B2-01
            case "steer_fast":
            case "steer_late":
            case "steer_none":
            case "engine_fast":
            {
                bool engine = variant == "engine_fast";
                double duration = engine ? 180 : 120;
                sim.Failures.Add(new SimFailure { Time = FailureTime, Engine = engine, Duration = duration });
                double ahead = sim.Scenario != null && sim.Scenario.initial != null ? ScenarioText.SpeedFactor(sim.Scenario.initial.telegraph) : 1.0;
                if (variant == "steer_none") break;
                sim.Manoeuvres.Add(new SimManoeuvre { Time = FailureTime + (variant == "steer_late" ? 45 : 10), SpeedFactor = 0.0, Label = "«Стоп»" });
                sim.Manoeuvres.Add(new SimManoeuvre { Time = FailureTime + duration + 5, SpeedFactor = ahead, Label = "отказ устранён — прежний ход" });
                break;
            }
        }
    }
}
