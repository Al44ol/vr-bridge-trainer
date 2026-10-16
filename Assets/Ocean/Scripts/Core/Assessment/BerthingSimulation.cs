// ============================================================
// BerthingSimulation.cs — Прогон сценария швартовки «за обучающегося» (запрос 19.2б-3в)
//
// Та же цепочка, что в тренажёре в сценарии швартовки: модель 3 СС своего судна (Δt = 0,02 с),
// силы причала, кранцев и концов (MooringSystem — как MooringController, до шага модели),
// контроль маршрута (RouteTracker — линия подхода), этапы задания (MooringTaskTracker — как
// ScenarioRunner, событие MooringStage, финиш «ошвартован»), запись сессии (SessionRecorder:
// track.csv, events.csv, mooring.csv — срез MooringSampler.Build, как ExerciseRecorder) и оценка
// в реальном времени (LiveAssessment с той же строкой mooring.csv). Шина ExerciseEvents не используется.
//
// «Обучающегося» заменяет план BerthingPlan:
//   1. подход — курс по LOS на линию, параллельную кромке, в gapTarget м от борта до кромки
//      (поправка курса не больше maxApproachAngleDeg); отдельно — постоянный угол к кромке (вариант «под углом»);
//   2. остановка — «Стоп» за stopLeadM до середины причала, «Самый малый назад», пока ход больше 0,15 уз
//      у середины, затем «Стоп»;
//   3. концы — носовые продольный и прижимной, кормовые прижимной и продольный — подать, когда ход меньше 0,3 уз
//      (через linesDelayS после остановки), и выбирать: «понемногу» (держать, если нос или корма идут
//      к причалу быстрее 0,1 уз, снова выбирать, когда медленнее 0,05 уз) или без остановок;
//   4. место — переход на крыло со стороны причала в начале (или остаться у штурвала);
//   5. 19.3в: ветер сценария (направление — середина сектора; WindLoad, только 3 СС) и буксир (TowingSystem, как
//      TowingController): вызов к месту TugStation после «Стоп» на ходу меньше TugCallBelowKn (у дальнего от причала борта, «стоп»);
//      после остановки — толкать к причалу: 10 % (около равновесия с ветром) или 25 % (подвести) по зазору и скорости
//      к причалу (RunTug); концы — ближе LinesMaxGapM, после подачи — держать 10 %; TugFixedPower — постоянная тяга
//      (вариант «полная тяга»). Тянуть к причалу буксир не может: он оказался бы между судном и причалом.
// Каждое действие публикуется как действие обучающегося: TelegraphChanged, HelmStarted/HelmEnded,
// LineCommand, StationChanged — как в сцене (меню B).
// ============================================================
using System;
using System.Collections.Generic;

/// <summary>План действий «за обучающегося» в сценарии швартовки.</summary>
public sealed class BerthingPlan
{
    /// <summary>Подход: зазор борта до кромки, к которому выходит судно, м; поправка курса не больше, °.</summary>
    public double GapTargetM = 5.0, MaxApproachAngleDeg = 15.0, LookaheadM = 40.0;
    /// <summary>Постоянный угол курса к кромке на подходе, ° (> 0 — к причалу); 0 — по LOS к линии.</summary>
    public double FixedApproachAngleDeg;
    /// <summary>«Стоп» за … м до середины причала (по мидель-шпангоуту вдоль кромки); «Самый малый назад» — за …, если ход больше 0,15 уз.</summary>
    public double StopLeadM = 45.0, AsternLeadM = 15.0;
    /// <summary>Подать концы через … с после того, как ход стал меньше 0,3 уз.</summary>
    public double LinesDelayS = 5.0;
    /// <summary>Начать выбирать через … с после подачи (до того — на тормозе).</summary>
    public double HaulDelayS;
    /// <summary>Выбирать «понемногу» (держать при скорости к причалу > 0,1 уз); false — без остановок.</summary>
    public bool CarefulHaul = true;
    /// <summary>«Понемногу»: держать край, если он идёт к причалу быстрее HoldAboveKn; снова выбирать — медленнее ResumeBelowKn, уз.</summary>
    public double HoldAboveKn = 0.06, ResumeBelowKn = 0.03;
    /// <summary>Перейти на крыло со стороны причала (через 5 с после начала).</summary>
    public bool GoToWing = true;

    // ── 19.3в: буксир ──
    public bool UseTug;
    public TugStation TugStation = TugStation.Midship;
    /// <summary>Вызвать буксир после «Стоп», когда ход меньше …, уз, и ещё через …, с (буксир подходит на малом ходу:
    /// на 3 уз он у борта отстаёт — короткий корпус с колонками плохо держит курс лагом, 19.24.3).</summary>
    public double TugCallBelowKn = 1.0, TugCallDelayS;
    /// <summary>Тяга: смена не чаще, с; наибольшая ступень (TugOrders.PowerSteps: 0 — 10 %, 1 — 25 %, 2 — 50 %, 3 — 100 %).</summary>
    public double TugStepS = 20.0;
    public int TugMaxPowerIndex = 1;
    /// <summary>Концы подавать, когда наименьший зазор до кромки не больше, м (∞ — сразу после остановки, как 19-01).</summary>
    public double LinesMaxGapM = double.PositiveInfinity;
    /// <summary>Постоянная доля тяги после остановки (0 — регулятор).</summary>
    public double TugFixedPower;
}

public sealed class BerthingSimulation
{
    public ScenarioDefinition Scenario;
    public BerthingPlan Plan = new BerthingPlan();
    public double Dt = 0.02;
    public double RateHz = 5.0;
    /// <summary>Папка записи (пусто — без записи).</summary>
    public string RecordFolder = "";
    /// <summary>Высота клюзов над водой, м (в сцене — по Setup, 4,5 м).</summary>
    public double FairleadHeightM = 4.5;

    // Результаты
    public AssessmentResult LiveResult;
    public ScenarioEndReason EndReason;
    public double EndTime;
    public readonly List<ExerciseEvent> Events = new List<ExerciseEvent>();
    /// <summary>Ход прогона: действия плана с зазорами, углом и ходом в этот момент.</summary>
    public readonly List<string> Log = new List<string>();
    public MooringTaskTracker Task { get; private set; }
    /// <summary>Наибольшая скорость навала, уз (по событиям).</summary>
    public double MaxImpactKn { get; private set; }

    private ShipDynamicsModel m;
    private MooringSystem sys;
    private BerthArea berth;
    private double t;
    private SessionRecorder rec;
    private LiveAssessment live;
    private BridgeStation station = BridgeStation.Helm;
    private RouteTracker route;
    // 19.3в: ветер и буксир
    private Wind wind;
    private ShipWindArea shipWind;
    /// <summary>Буксир прогона (null — не вызывался).</summary>
    public TowingSystem Tug { get; private set; }
    private int tugPowerIndex = -1;
    private double tugDir, tugNextStep;

    private static readonly int[] Lines = { (int)MooringLineKind.HeadLine, (int)MooringLineKind.ForwardBreast, (int)MooringLineKind.AftBreast, (int)MooringLineKind.SternLine };
    private static readonly int[] BowLines = { (int)MooringLineKind.HeadLine, (int)MooringLineKind.ForwardBreast };
    private static readonly int[] SternLines = { (int)MooringLineKind.AftBreast, (int)MooringLineKind.SternLine };

    public void Run()
    {
        ScenarioDefinition s = Scenario;
        ShipParameters p = s.shipParameters;
        berth = s.mooring.BerthIn(s.waterArea);
        if (berth == null) throw new InvalidOperationException("в акватории нет причала задания");
        m = new ShipDynamicsModel(p) { MotionOverride = MotionModel.Manoeuvring3Dof };
        m.Teleport(s.initial.position.East, s.initial.position.North, s.initial.headingDeg * Math.PI / 180.0,
                   s.initial.speedKnots * ShipParameters.KnotsToMs, s.initial.rudderDeg);
        m.SpeedFactor = ScenarioText.SpeedFactor(s.initial.telegraph);
        sys = new MooringSystem(berth, p) { FairleadHeightM = FairleadHeightM };
        Task = new MooringTaskTracker(s.mooring);
        route = new RouteTracker();
        route.Reset(s.route);
        var ap = new HeadingAutopilot();
        Events.Clear();
        Log.Clear();
        MaxImpactKn = 0.0;

        rec = string.IsNullOrEmpty(RecordFolder) ? null
            : new SessionRecorder(RecordFolder, SessionMeta.Create(s, p, "sim", new DateTime(2026, 10, 16, 12, 0, 0)), background: false);
        if (rec != null)
        {
            rec.Meta.recording.track_rate_hz = RateHz;
            rec.Meta.recording.fixed_dt_s = Dt;
            rec.Meta.ship.motion_model = MotionModel.Manoeuvring3Dof.ToString();
        }
        live = new LiveAssessment();
        live.Output += e => Publish(e);
        live.Begin(AssessmentContext.FromScenario(s, p), RateHz);
        var sampler = new TrackSampler(RateHz);
        wind = s.windKnots > 0f ? Wind.Knots(s.windKnots, s.windFromDeg) : new Wind(0.0, 0.0);
        shipWind = ShipWindArea.Estimate(p);
        Tug = null;
        tugPowerIndex = -1;
        tugNextStep = 0.0;
        stoppedAtTug = double.NaN;

        t = 0;
        Publish(new ExerciseEvent(ExerciseEventType.ExerciseStarted) { Subject = s.title });
        route.Update(Pos, 0, 0);
        if (sampler.Due(t)) Sample();

        // Борт к причалу (+1 — правый) и направление «к причалу» в поправке курса
        int side = sys.SideToBerth(m);
        BridgeStation wing = side < 0 ? BridgeStation.WingPort : BridgeStation.WingStarboard;
        double middle = 0.5 * berth.Length;
        double nTarget = Plan.GapTargetM + 0.5 * p.beam;
        WorldPosition la = berth.ToWorld(0, nTarget), lb = berth.ToWorld(berth.Length, nTarget);
        double helmEndAt = double.NaN, stoppedAt = double.NaN, lastLineTry = -1e9, givenAt = double.NaN;
        bool haulStarted = false;
        double slowAt = double.NaN;   // 19.3в: ход стал меньше TugCallBelowKn
        int phase = 0;   // 0 — подход, 1 — «Стоп», 2 — назад, 3 — остановился
        bool linesGiven = false;
        bool[] haulEnd = { false, false };   // выбираются: нос, корма

        EndReason = ScenarioEndReason.None;
        while (EndReason == ScenarioEndReason.None)
        {
            if (Plan.GoToWing && station == BridgeStation.Helm && t >= 5.0) SetStation(wing);

            // Положение вдоль кромки и ход
            berth.ToLocal(Pos, out double along, out _);
            double u = m.U / ShipParameters.KnotsToMs;

            // Машина
            if (phase == 0 && along >= middle - Plan.StopLeadM) { Telegraph(0.0); phase = 1; Note("«Стоп»"); }
            else if (phase == 1 && along >= middle - Plan.AsternLeadM && u > 0.15) { Telegraph(-0.2); phase = 2; Note("«Самый малый назад»"); }   // у середины ещё есть ход — назад
            else if ((phase == 1 || phase == 2) && u < 0.1)
            {
                if (phase == 2) Telegraph(0.0);
                phase = 3;
                Note("остановился");
            }
            if (phase >= 1 && double.IsNaN(stoppedAt) && u < 0.3) { stoppedAt = t; Note("ход < 0,3 уз"); }   // можно подавать концы

            // Руль: на подходе — к линии у кромки; после «Стоп» — по кромке (на малом ходу руль почти не действует)
            double course = berth.BearingDeg;
            if (phase == 0)
            {
                if (Math.Abs(Plan.FixedApproachAngleDeg) > 1e-6) course += side * Plan.FixedApproachAngleDeg;   // к причалу: ЛБ (side −1) — влево
                else
                {
                    double xte = RouteTracker.CrossTrack(la, lb, Pos);   // > 0 — правее линии
                    double corr = -Math.Atan(xte / Plan.LookaheadM) * 180.0 / Math.PI;
                    course += Math.Max(-Plan.MaxApproachAngleDeg, Math.Min(Plan.MaxApproachAngleDeg, corr));
                }
            }
            double rudder = ap.Rudder(m, course);
            if (Math.Abs(rudder - m.RudderCommandDeg) > 2.0 && double.IsNaN(helmEndAt)) StartHelm(ref helmEndAt);
            m.RudderCommandDeg = rudder;

            // Концы: подать после остановки, затем выбирать
            if (!linesGiven && !double.IsNaN(stoppedAt) && t >= stoppedAt + Plan.LinesDelayS && t - lastLineTry >= 1.0 && GapOk())
            {
                lastLineTry = t;
                bool all = true;
                foreach (int i in Lines) if (sys.Lines[i].State != MooringLineState.Fast) all &= Order(i, LineOrder.Give);
                if (all)
                {
                    linesGiven = true;
                    Note("концы поданы");
                    givenAt = t;
                }
            }
            if (linesGiven && !haulStarted && t >= givenAt + Plan.HaulDelayS - 1e-9)
            {
                haulStarted = true;
                foreach (int i in Lines) Order(i, LineOrder.HaulIn);
                haulEnd[0] = haulEnd[1] = true;
            }
            if (haulStarted && Plan.CarefulHaul && Math.Round(t / Dt) % 10 == 0)
            {
                // Нос и корма — раздельно: держать край, который идёт к причалу быстрее 0,1 уз (снова выбирать —
                // медленнее 0,05 уз) или уже ближе к кромке, чем другой, больше чем на 1 м (выравнивание лагом)
                BerthReadout br = BerthReadout.Compute(berth, p, m);
                for (int end = 0; end < 2; end++)
                {
                    double v = end == 0 ? br.BowSpeedKn : br.SternSpeedKn;
                    double gap = end == 0 ? br.BowGapM : br.SternGapM, other = end == 0 ? br.SternGapM : br.BowGapM;
                    bool want = sys.InContact || ((haulEnd[end] ? v < Plan.HoldAboveKn : v < Plan.ResumeBelowKn) && gap >= other - 1.0);
                    if (want == haulEnd[end]) continue;
                    foreach (int i in end == 0 ? BowLines : SternLines)
                        if (sys.Lines[i].State == MooringLineState.Fast) Order(i, want ? LineOrder.HaulIn : LineOrder.Hold);
                    haulEnd[end] = want;
                }
            }

            // 19.3в: буксир — вызов и работа после остановки
            if (Plan.UseTug && Tug == null && phase >= 1 && u < Plan.TugCallBelowKn)
            {
                if (double.IsNaN(slowAt)) slowAt = t;
                if (t >= slowAt + Plan.TugCallDelayS - 1e-9) CallTug(p, side);
            }
            stoppedAtTug = stoppedAt;
            if (Tug != null && Math.Round(t / Dt) % 50 == 0) RunTug();

            // Шаг: силы причала (как MooringController, до шага модели), ветер и буксир (как TowingController), модель
            sys.Apply(m, Dt, t);
            if (Tug != null) { Tug.Wind = wind; Tug.Apply(m, Dt, t); FlushTowEvents(); }
            else if (!wind.Calm) WindLoad.Apply(m, shipWind, wind);
            m.Step(Dt);
            t += Dt;
            FlushMooringEvents();

            if (!double.IsNaN(helmEndAt) && t >= helmEndAt - 1e-9)
            {
                Publish(new ExerciseEvent(ExerciseEventType.HelmEnded) { Value = m.Ordered, Value2 = 3.0 });
                helmEndAt = double.NaN;
            }

            // Маршрут (линия подхода): события — как ScenarioRunner; прибытие в сценарии швартовки не завершает
            foreach (RouteEvent e in route.Update(Pos, t, Dt))
                switch (e.Kind)
                {
                    case RouteEventKind.WaypointPassed:
                        Publish(new ExerciseEvent(ExerciseEventType.WaypointPassed) { Index = e.Index, Subject = e.Name, Value = e.Xte, Value2 = e.Value2 }); break;
                    case RouteEventKind.CorridorExit:
                        Publish(new ExerciseEvent(ExerciseEventType.CorridorExit) { Index = e.Index, Subject = e.Name, Value = e.Xte, Value2 = e.Value2 }); break;
                    case RouteEventKind.CorridorReturn:
                        Publish(new ExerciseEvent(ExerciseEventType.CorridorReturn) { Index = e.Index, Subject = e.Name, Value = e.Xte, Value2 = e.Value2 }); break;
                }

            // Этапы задания — как ScenarioRunner.TrackMooring
            var x = new MooringTaskInput
            {
                Readout = BerthReadout.Compute(berth, p, m), FenderContact = sys.InContact,
                SpeedOverGroundKn = Math.Sqrt(m.VelocityEast * m.VelocityEast + m.VelocityNorth * m.VelocityNorth) / ShipParameters.KnotsToMs
            };
            MooringTasks.LinesFast(sys.Lines, out x.BowLineFast, out x.SternLineFast);
            foreach (MooringStage st in Task.Update(x, t, Dt))
                Publish(new ExerciseEvent(ExerciseEventType.MooringStage)
                {
                    Index = (int)st, Subject = MooringTasks.Title(st),
                    Value = x.Readout.Valid ? Math.Max(x.Readout.BowGapM, x.Readout.SternGapM) : double.NaN, Value2 = x.SpeedOverGroundKn
                });
            if (Task.Moored && s.Ends(ScenarioEndConditions.Moored)) EndReason = ScenarioEndReason.Moored;
            if (EndReason == ScenarioEndReason.None && s.timeLimitSeconds > 0 && s.Ends(ScenarioEndConditions.TimeLimit) && t >= s.timeLimitSeconds)
                EndReason = ScenarioEndReason.TimeLimit;
            if (EndReason == ScenarioEndReason.None && t >= Math.Max(60.0, s.timeLimitSeconds > 0 ? s.timeLimitSeconds : 3600.0) + 1.0)
                EndReason = ScenarioEndReason.Aborted;

            if (EndReason == ScenarioEndReason.None && sampler.Due(t)) Sample();
        }

        EndTime = t;
        Publish(new ExerciseEvent(ExerciseEventType.ExerciseEnded)
        {
            EndReason = EndReason, Subject = s.title, Value = route.PassedCount, Value2 = route.Valid ? route.WaypointCount - 1 : 0
        });
        if (live.NeedsFinalSample(t) || (rec != null && (double.IsNaN(rec.LastTrackTime) || rec.LastTrackTime < t - 1e-6))) Sample();
        LiveResult = live.End();

        if (rec != null)
        {
            rec.Meta.completed = true;
            rec.Meta.end_reason = EndReason.ToString();
            rec.Meta.end_reason_ru = ScenarioText.EndReason(EndReason);
            rec.Meta.duration_s = Math.Round(t, 3);
            rec.Meta.summary = SessionSummary.From(ExerciseSummary.From(s, new List<ExerciseEvent>(Events), route));
            rec.Meta.assessment = SessionAssessmentSummary.From(LiveResult);
            rec.WriteAssessment(LiveResult.ToCsv());
            rec.Close();
        }
    }

    private WorldPosition Pos => new WorldPosition(m.East, m.North);

    // Ход прогона для отчёта и подбора плана: время, действие, зазоры, угол, ход
    private void Note(string what)
    {
        BerthReadout r = BerthReadout.Compute(berth, Scenario.shipParameters, m);
        Log.Add($"{ScenarioText.Clock(t)} {what}: зазор нос {ScenarioText.Num(r.BowGapM, 1)} / корма {ScenarioText.Num(r.SternGapM, 1)} м, " +
                $"угол {ScenarioText.Num(r.AngleDeg, 1)}°, ход {ScenarioText.Num(m.U / ShipParameters.KnotsToMs, 2)} уз");
    }

    private void Telegraph(double sf)
    {
        double old = m.SpeedFactor;
        if (Math.Abs(old - sf) < 1e-9) return;
        m.SpeedFactor = sf;
        Publish(new ExerciseEvent(ExerciseEventType.TelegraphChanged)
        {
            Index = (int)ScenarioText.OrderFromSpeedFactor(sf), Value = sf, Value2 = old,
            Subject = ScenarioText.Telegraph(ScenarioText.OrderFromSpeedFactor(sf))
        });
    }

    private void StartHelm(ref double helmEndAt)
    {
        Publish(new ExerciseEvent(ExerciseEventType.HelmStarted) { Value = m.Ordered });
        helmEndAt = t + 3.0;
    }

    private void SetStation(BridgeStation s)
    {
        BridgeStation old = station;
        station = s;
        Publish(new ExerciseEvent(ExerciseEventType.StationChanged) { Index = (int)s, Value = (int)old, Subject = BridgeStations.Title(s) });
    }

    // Команда концу — как MooringController.OrderLine: событие LineCommand, затем события подачи / отдачи
    private bool Order(int i, LineOrder o)
    {
        bool ok = sys.Order(i, o, m, t, out string reason);
        string title = sys.Lines[i].Title();
        Publish(new ExerciseEvent(ExerciseEventType.LineCommand) { Index = i, Value = (int)o, Value2 = ok ? 1 : 0, Subject = ok ? title : $"{title}: {reason}" });
        FlushMooringEvents();
        return ok;
    }

    // ─────────── 19.3в: буксир ───────────

    private void CallTug(ShipParameters p, int side)
    {
        Tug = new TowingSystem(p, TowingTrials.TugParameters());
        Tug.SetStation(Plan.TugStation);
        Tug.SetGeometry(TugOrders.DefaultGeometry(Plan.TugStation));
        tugDir = side < 0 ? 270.0 : 90.0;   // сила на судно — к причалу
        // У миделя — у дальнего от причала борта, «стоп»
        TugCommand stop = TugOrders.Command(TugMode.Stop, tugDir, 0.0);
        Tug.Wind = wind;
        Tug.PlaceTug(m, stop, 30.0);
        Tug.Order(stop, t);
        Tug.Events.Clear();
        Publish(new ExerciseEvent(ExerciseEventType.TugCalled) { Index = (int)Plan.TugStation, Subject = TugOrders.StationTarget(Plan.TugStation), Value = Tug.Tug.Spec.BollardPull / 1000.0 });
        Note($"буксир вызван {TugOrders.StationTarget(Plan.TugStation)}");
    }

    // Раз в секунду после остановки: толкать к причалу. Тяга — две ступени (10 % ≈ равновесие с ветром 20 уз, 25 % —
    // подвести, 19.24.4), смена не чаще TugStepS: дальше 15 м — 25 %, если к причалу медленнее 0,15 уз, 10 % — быстрее
    // 0,35 уз; 5…15 м — 25 % медленнее 0,05 уз, 10 % быстрее 0,15 уз; ближе 5 м и после подачи концов — 10 % (держать
    // против ветра, концы выбирают). TugFixedPower — постоянная тяга (вариант «полная тяга»).
    private void RunTug()
    {
        if (double.IsNaN(stoppedAtTug)) return;
        int idx;
        if (Plan.TugFixedPower > 0) idx = TugOrders.NearestIndex(TugOrders.PowerSteps, Plan.TugFixedPower);
        else
        {
            if (t < tugNextStep - 1e-9) return;
            BerthReadout br = BerthReadout.Compute(berth, Scenario.shipParameters, m);
            double v = 0.5 * (br.BowSpeedKn + br.SternSpeedKn), gap = Math.Min(br.BowGapM, br.SternGapM);
            bool lines = sys.Lines.Exists(l => l.State == MooringLineState.Fast);
            idx = tugPowerIndex < 0 ? 1 : tugPowerIndex;
            if (lines) idx = -1;                 // концы поданы — «стоп»: дальше выбирают концы против ветра
            else if (gap <= 5.0) idx = 0;
            else if (gap > 15.0) idx = v < 0.15 ? 1 : v > 0.35 ? 0 : idx;
            else idx = v < 0.05 ? 1 : v > 0.15 ? 0 : idx;
            idx = Math.Min(idx, Math.Min(Plan.TugMaxPowerIndex, TugOrders.PowerSteps.Length - 1));
            if (idx != tugPowerIndex) tugNextStep = t + Plan.TugStepS;
        }
        Issue(idx, TugMode.Push, tugDir);
    }

    private double stoppedAtTug = double.NaN;
    private readonly List<TargetSample> tugTargets = new List<TargetSample>();

    private void Issue(int idx, TugMode mode, double dir)
    {
        if (idx == tugPowerIndex && (Tug.Tug.Command.Mode != TugMode.Stop) == (idx >= 0)) return;
        tugPowerIndex = idx;
        TugCommand c = idx < 0 ? TugOrders.Command(TugMode.Stop, dir, 0.0) : TugOrders.Command(mode, dir, TugOrders.PowerSteps[idx]);
        Tug.Order(c, t);
        FlushTowEvents();
    }
    // Концы — когда судно подошло (с буксиром: ближе LinesMaxGapM)
    private bool GapOk()
    {
        if (double.IsInfinity(Plan.LinesMaxGapM)) return true;
        BerthReadout br = BerthReadout.Compute(berth, Scenario.shipParameters, m);
        return br.Valid && Math.Min(br.BowGapM, br.SternGapM) <= Plan.LinesMaxGapM;
    }

    private double Chord()
    {
        WorldPosition f = Tug.TowPoint(m), b = Tug.Tug.Bow;
        return Math.Sqrt((b.East - f.East) * (b.East - f.East) + (b.North - f.North) * (b.North - f.North));
    }

    private void FlushTowEvents()
    {
        if (Tug == null) return;
        foreach (TowEvent te in Tug.Events) Publish(TowEventBus.ToExerciseEvent(te));
        Tug.Events.Clear();
    }

    private void FlushMooringEvents()
    {
        foreach (MooringEvent me in sys.Events)
        {
            ExerciseEvent e = MooringEventBus.ToExerciseEvent(me);
            if (e.Type == ExerciseEventType.BerthingImpact && e.Value > MaxImpactKn) MaxImpactKn = e.Value;
            Publish(e);
        }
        sys.Events.Clear();
    }

    private void Publish(ExerciseEvent e)
    {
        e.Time = t;
        e.InExercise = true;
        e.Position = Pos;
        double h = (m.Psi * 180.0 / Math.PI) % 360.0;
        e.HeadingDeg = (float)(h < 0 ? h + 360.0 : h);
        e.SpeedKnots = (float)(m.U / ShipParameters.KnotsToMs);
        Events.Add(e);
        rec?.AddEvent(e);
        live?.OnEvent(e);
    }

    // Срез — как ExerciseRecorder.BuildTrackSample (путевой угол с дрейфом) и BuildMooringSample
    private void Sample()
    {
        double heading = (m.Psi * 180.0 / Math.PI) % 360.0;
        if (heading < 0) heading += 360.0;
        double ve = m.VelocityEast, vn = m.VelocityNorth;
        double cog = ve * ve + vn * vn < 1e-4 ? heading : (Math.Atan2(ve, vn) * 180.0 / Math.PI + 360.0) % 360.0;
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
            Time = t, East = m.East, North = m.North, HeadingDeg = heading, CogDeg = cog,
            SpeedKnots = m.U / ShipParameters.KnotsToMs, RotDegMin = m.R * 180.0 / Math.PI * 60.0,
            RudderOrderedDeg = m.Ordered, RudderActualDeg = m.Delta, TelegraphSf = m.SpeedFactor,
            Telegraph = (int)ScenarioText.OrderFromSpeedFactor(m.SpeedFactor),
            XteM = xte, Leg = leg, XteLimitM = limit, Grounded = m.Grounded
        };
        MooringSample ms = MooringSampler.Build(t, berth, Scenario.mooring.berthIndex, Scenario.shipParameters, m, sys,
                                                ThrusterOrders.ZeroIndex, station, Task);
        ms.Towing = TowingSample.Build(Tug, wind);   // 19.3в: как TowingController.Sample
        // 19.3в: буксир — цель «TUG» в targets.csv (как ExerciseTargets в сцене; оценка расхождения его не учитывает)
        tugTargets.Clear();
        if (Tug != null)
        {
            var tg = new TugTarget(Tug.Tug);
            tugTargets.Add(new TargetSample { Time = t, Id = tg.TargetId, East = tg.Position.East, North = tg.Position.North, HeadingDeg = tg.HeadingDeg, SpeedKnots = tg.SpeedKnots });
        }
        if (rec != null) { rec.AddTrack(s); foreach (TargetSample ts in tugTargets) rec.AddTarget(ts); rec.AddMooring(ms); }
        if (live.Running) live.AddSample(s, tugTargets, ms);
    }
}
