// ============================================================
// TugWorkSimulation.cs — Прогон упражнения на буксире «за обучающегося» (запрос 19.4б-1, Core)
//
// Та же цепочка, что в тренажёре: свой буксир — модель 3 СС (ShipParameters_Tug) с колонками по джойстику
// (AzimuthDrive, 19.4а), клиент и причал — TugWorkSystem (до шага своей модели), ветер сценария (середина сектора).
// Шаг Δt = 0,02 с. Обучающегося заменяет автомат (TugWorkAutopilot) — он двигает только джойстик:
//   1. подход — нос буксира к точке касания у миделя рабочего борта клиента, курс поперёк борта (+ ApproachAngleDeg);
//      ход относительно клиента по ошибке до точки: TransitKn вдали, ApproachKn у борта (с SlowDownM м — по корню);
//      поперёк — к точке, разворот — к заданному курсу (П-регуляторы скорости, рукоятка — с мёртвой зоной);
//   2. «толкайте» — упор по скорости клиента к причалу (v_з = min(0,8; 0,1 + 0,02·зазор) уз); курс поперёк борта;
//   3. «держите» — HoldPower до «отходите» (ReleaseEarlyS > 0 — снять упор раньше, вариант 19.4в);
//   4. «отходите» — назад ClearPower до ClearDistance, затем джойстик в ноль.
// События упражнения — в Events, итог — EndReason, этапы — System. 19.4б-2: запись сессии (RecordFolder: track.csv,
// targets.csv с клиентом «CLIENT», events.csv, mooring.csv — TugWorkSystem.Sample, session.json) и оценка «в шлеме»
// (LiveAssessment, LiveResult) — как в тренажёре.
// 19.4в: событие JoystickChanged — установившееся положение рукоятки, как OwnTugDrive (по нему G11 — реакция на команды
// лоцмана); варианты «за обучающегося» (DemoAssessmentRuns.TugWorkVariants): не слушает команды (IgnorePilot), опаздывает
// (PilotDelayS), полный до причала (FixedPush), быстрый подход (ApproachKn), под углом (ApproachAngleDeg — подход к точке
// касания вектором, после касания — поперёк борта), рано отпустил упор (ReleaseEarlyS).
// ============================================================
using System;
using System.Collections.Generic;

/// <summary>План действий автомата буксира (уставки — ThesisNotes 19.29).</summary>
public sealed class TugWorkPlan
{
    public string Name = "правильно";
    /// <summary>Ход к борту относительно клиента: вдали, у борта, уз; сбавлять с … м до точки касания.</summary>
    public double TransitKn = 5.0, ApproachKn = 0.2, SlowDownM = 60.0;
    /// <summary>Отклонение курса подхода от перпендикуляра к борту, ° (вариант «под углом»).</summary>
    public double ApproachAngleDeg;
    /// <summary>
    /// «Толкайте»: клиент идёт к причалу со скоростью v_з = min(MaxPushKn, 0,1 + 0,02·зазор) уз (у кранцев ≈ 0,1 уз); упор
    /// P = WindBalance + KPush·(v_з − v_к), 0…PushPower (доля T_гак); FixedPush — постоянный PushPower (вариант «полная тяга»).
    /// </summary>
    public double MaxPushKn = 0.8, WindBalance = 0.08, KPush = 0.5, PushPower = 0.5;
    public bool FixedPush;
    /// <summary>19.4б-3: «толкать» и «держать» — ступенью упора, которую назвал лоцман (TugWorkSystem.PowerStep); выкл — свой регулятор.</summary>
    public bool FollowPilot = true;
    /// <summary>Упор: держать, отходить — доли T_гак.</summary>
    public double HoldPower = 0.12, ClearPower = 0.4;
    /// <summary>> 0 — снять упор через … с после «держите» (не дождавшись «отходите»).</summary>
    public double ReleaseEarlyS;
    /// <summary>19.4в, «не слушает команды»: толкать и держать постоянным PushPower, команды лоцмана упором не выполнять.</summary>
    public bool IgnorePilot;
    /// <summary>19.4в, «опаздывает»: ступень лоцмана ставить через … с после команды (до первой — самый малый 25 %).</summary>
    public double PilotDelayS;
    /// <summary>
    /// 19.4г, «случайное событие»: переход вдоль борта к отошедшему концу — держаться в RelocateStandoffM м от борта, лагом
    /// не быстрее RelocateSwayMs м/с, у точки — подход как к миделю. Варианты: IgnoreEvent — «не перешёл» (толкает у миделя
    /// PushPower, не слушая «стоп»); WrongEnd — пошёл к ближнему концу и толкает там самым малым; RelocateDelayS — «поздно».
    /// </summary>
    public double RelocateStandoffM = 4.0, RelocateSwayMs = 1.0, RelocateDelayS;
    public bool IgnoreEvent, WrongEnd;
}

/// <summary>Автомат буксира: положение джойстика по состоянию упражнения.</summary>
public sealed class TugWorkAutopilot
{
    public TugWorkPlan Plan = new TugWorkPlan();
    public double Ku = 1.5, KyPos = 0.25, Kv = 3.0, KPsi = 0.1, KR = 0.5;

    /// <summary>Доля оси (−1…1) → положение рукоятки с учётом мёртвой зоны (обратное TugJoystick.Shape).</summary>
    public static double Unshape(double f) => TugJoystick.Unshape(f);

    public JoystickState Update(TugWorkSystem sys, ShipDynamicsModel own, double time)
    {
        ShipDynamicsModel cm = sys.Client.Model;
        int side = sys.WorkSide;
        double nE = side * Math.Cos(cm.Psi), nN = -side * Math.Sin(cm.Psi);   // нормаль рабочего борта наружу
        // 19.4в: под углом — только подход; после касания — поперёк борта, как толкают
        double psiD = Math.Atan2(-nE, -nN) + (sys.Done(TugWorkStage.Approach) ? 0.0 : Plan.ApproachAngleDeg * Math.PI / 180.0);
        double ePsi = Math.IEEERemainder(psiD - own.Psi, 2.0 * Math.PI) * 180.0 / Math.PI;
        double rDeg = own.R * 180.0 / Math.PI;
        double yaw = Clamp(KPsi * ePsi - KR * rDeg);

        // 19.4г: при событии — точка касания у названного конца (WrongEnd — у другого)
        bool toEnd = sys.EventActive && !Plan.IgnoreEvent;
        double tx = toEnd ? (Plan.WrongEnd ? -1.0 : 1.0) * sys.EndTargetX : 0.0;
        bool atTarget = toEnd && sys.Contact.FenderForce > 0.0 && sys.Contact.TouchSide == side && Math.Abs(sys.Contact.TouchX - tx) <= sys.Task.contactZoneM;
        bool relocating = toEnd && !sys.TugAtEnd && !(Plan.WrongEnd && atTarget);
        WorldPosition c = sys.ContactPoint(tx), b = sys.Bow(own);
        double dE = c.East - b.East, dN = c.North - b.North;
        double s = Math.Sin(own.Psi), co = Math.Cos(own.Psi);
        double ex = dE * s + dN * co, ey = dE * co - dN * s;   // до точки касания в осях буксира
        // Скорость относительно клиента в осях буксира (клиента сносит ветром)
        double rvE = own.VelocityEast - cm.VelocityEast, rvN = own.VelocityNorth - cm.VelocityNorth;
        double ru = rvE * s + rvN * co, rv = rvE * co - rvN * s;
        double sway = Clamp(Kv * (Clamp(KyPos * ey, 0.5) - rv));

        double surge;
        if (!sys.Done(TugWorkStage.Approach))
        {
            double k = Math.Max(0.0, Math.Min(1.0, (ex - 2.0) / Math.Max(1.0, Plan.SlowDownM)));
            double uD = (Plan.ApproachKn + (Plan.TransitKn - Plan.ApproachKn) * Math.Sqrt(k)) * ShipParameters.KnotsToMs;
            surge = Clamp(Ku * (uD - ru));
            if (Plan.ApproachAngleDeg != 0.0)
            {
                // 19.4в, «под углом»: нос под углом к борту — идти к точке касания вектором (ход и лагом), не по оси буксира,
                // иначе лагом не успевает и нос бьёт борт вне зоны; скорость — та же по расстоянию до точки
                double d = Math.Sqrt(ex * ex + ey * ey);
                double kd = Math.Max(0.0, Math.Min(1.0, (d - 2.0) / Math.Max(1.0, Plan.SlowDownM)));
                double vD = (Plan.ApproachKn + (Plan.TransitKn - Plan.ApproachKn) * Math.Sqrt(kd)) * ShipParameters.KnotsToMs;
                double ux = d > 1e-6 ? ex / d : 1.0, uy = d > 1e-6 ? ey / d : 0.0;
                surge = Clamp(Ku * (vD * ux - ru));
                sway = Clamp(Kv * (vD * uy - rv));
            }
        }
        else if (relocating)
        {
            if (time - sys.EventTime < Plan.RelocateDelayS) { surge = 0.0; sway = 0.0; }   // «поздно»: стоит у миделя
            else if (Math.Abs(ey) > 4.0)
            {
                // вдоль борта: держаться в RelocateStandoffM м, лагом к точке
                surge = Clamp(Ku * (Clamp(0.25 * (ex - Plan.RelocateStandoffM), 0.6) - ru));
                sway = Clamp(Kv * (Clamp(KyPos * ey, Plan.RelocateSwayMs) - rv));
            }
            else
            {
                double k = Math.Max(0.0, Math.Min(1.0, (ex - 2.0) / Math.Max(1.0, Plan.SlowDownM)));
                double uD = (Plan.ApproachKn + (Plan.TransitKn - Plan.ApproachKn) * Math.Sqrt(k)) * ShipParameters.KnotsToMs;
                surge = Clamp(Ku * (uD - ru));
            }
        }
        else if (toEnd && Plan.WrongEnd && !sys.TugAtEnd) surge = TugJoystick.MenuStep;   // у ближнего конца — самый малый
        else if (!sys.Done(TugWorkStage.Push))
        {
            int step = PilotStep(sys, time);
            if (Plan.FixedPush || Plan.IgnorePilot || Plan.IgnoreEvent && sys.EventActive) surge = Plan.PushPower;
            else if (Plan.FollowPilot && step >= 0) surge = step * TugJoystick.MenuStep;   // 19.4б-3: ступень лоцмана
            else
            {
                BerthReadout br = sys.Client.Readout;
                double gap = sys.Client.GapM;
                double vD = Math.Min(Plan.MaxPushKn, 0.1 + 0.02 * (double.IsNaN(gap) ? 0.0 : gap));
                double vK = br.Valid ? 0.5 * (br.BowSpeedKn + br.SternSpeedKn) : 0.0;
                surge = Math.Max(0.0, Math.Min(Plan.PushPower, Plan.WindBalance + Plan.KPush * (vD - vK)));
            }
        }
        else if (!sys.Done(TugWorkStage.Hold))
        {
            int step = PilotStep(sys, time);
            surge = Plan.ReleaseEarlyS > 0 && time - sys.DoneAt(TugWorkStage.Push) >= Plan.ReleaseEarlyS ? 0.0
                  : Plan.IgnorePilot ? Plan.PushPower
                  : Plan.FollowPilot && step >= 0 ? step * TugJoystick.MenuStep : Plan.HoldPower;
        }
        else if (!sys.Done(TugWorkStage.Clear))
        {
            surge = -Plan.ClearPower;
            sway = 0.0;
        }
        else return JoystickState.Zero;
        if (sys.Done(TugWorkStage.Approach) && !relocating) sway = 0.0;   // у борта лагом не ходить: кранец с трением о борт разворачивает буксир, корпус касается борта
        return new JoystickState(Unshape(surge), Unshape(sway), Unshape(yaw));
    }

    // 19.4в: ступень лоцмана, которую ставит автомат; «опаздывает» (PilotDelayS) — каждая команда через … с после подачи,
    // до первой — самый малый (остаётся у борта); −1 — команд упором ещё не было
    private readonly List<(double at, int step)> delayed = new List<(double, int)>();
    private double lastOrderTime = double.NaN;

    private int PilotStep(TugWorkSystem sys, double time)
    {
        if (sys.PowerStep < 0 || Plan.PilotDelayS <= 0) return sys.PowerStep;
        if (!(sys.PowerStepTime == lastOrderTime))
        {
            lastOrderTime = sys.PowerStepTime;
            delayed.Add((sys.PowerStepTime + Plan.PilotDelayS, sys.PowerStep));
        }
        int step = 1;
        foreach ((double at, int s) in delayed) if (at <= time + 1e-9) step = s;
        return step;
    }

    private static double Clamp(double v, double max = 1.0) => v < -max ? -max : v > max ? max : v;
}

public sealed class TugWorkSimulation
{
    public ScenarioDefinition Scenario;
    public TugWorkPlan Plan = new TugWorkPlan();
    public double Dt = 0.02;
    /// <summary>Тяга на гаке своего буксира, т.</summary>
    public double BollardPullTonnes = TugSpec.DefaultBollardPullTonnes;
    /// <summary>19.4б-2: папка записи сессии (пусто — без записи); частота строк track/targets/mooring, Гц.</summary>
    public string RecordFolder = "";
    public double RateHz = 5.0;
    /// <summary>19.4г: «случайное событие» — в прогоне только по заказу и с заданными концом (+1 нос / −1 корма), углом, зазором (прогоны 19.4б–в — без события).</summary>
    public bool AngleEvent;
    public int EventOffEnd = 1;
    public double EventAngleDeg = 6.0, EventGapM = 5.0;

    public TugWorkSystem System { get; private set; }
    public ShipDynamicsModel Own { get; private set; }
    public AzimuthDrive Drive { get; private set; }
    public readonly List<ExerciseEvent> Events = new List<ExerciseEvent>();
    public ScenarioEndReason EndReason { get; private set; }
    public double EndTime { get; private set; }
    /// <summary>Наибольшее расстояние клиента от причала (зазор) за прогон, м — снос ветром.</summary>
    public double MaxClientGapM { get; private set; }
    /// <summary>19.4б-2: итог оценки «в шлеме» (LiveAssessment по тем же строкам и событиям, что запись).</summary>
    public AssessmentResult LiveResult;

    private SessionRecorder rec;
    private LiveAssessment live;
    private double t;
    private readonly List<TargetSample> targets = new List<TargetSample>(1);

    /// <summary>Выполнить прогон; maxTime — свой предел (0 — лимит сценария).</summary>
    public void Run(double maxTime = 0.0)
    {
        ScenarioDefinition s = Scenario;
        ShipParameters tug = s.shipParameters, client = s.tugWorkClient;
        BerthArea berth = s.tugWork.BerthIn(s.waterArea);
        if (berth == null || tug == null || client == null) throw new InvalidOperationException("19-03: нет причала, буксира или клиента");
        TugSpec spec = TugSpec.ForOwnTug(tug, BollardPullTonnes);
        System = new TugWorkSystem(s.tugWork, client, berth, spec)
        {
            Wind = s.windKnots > 0f ? Wind.Knots(s.windKnots, s.windFromDeg) : new Wind(0.0, 0.0),
            ApplyOwnWind = true
        };
        Own = new ShipDynamicsModel(tug) { MotionOverride = MotionModel.Manoeuvring3Dof, SpeedFactor = 0.0 };
        System.Place(Own);
        if (AngleEvent) System.ArmEvent(EventOffEnd, EventAngleDeg, EventGapM);   // 19.4г
        Drive = new AzimuthDrive(spec);
        var pilot = new TugWorkAutopilot { Plan = Plan };
        Events.Clear();
        MaxClientGapM = 0.0;
        candidate = published = JoystickState.Zero;
        candidateDone = true;
        double limit = maxTime > 0 ? maxTime : (s.timeLimitSeconds > 0 ? s.timeLimitSeconds : 1800.0);

        // 19.4б-2: запись и оценка — как ExerciseRecorder / ExerciseAssessmentMonitor в тренажёре
        rec = string.IsNullOrEmpty(RecordFolder) ? null
            : new SessionRecorder(RecordFolder, SessionMeta.Create(s, tug, "sim", new DateTime(2026, 10, 17, 12, 0, 0)), background: false);
        if (rec != null)
        {
            rec.Meta.recording.track_rate_hz = RateHz;
            rec.Meta.recording.fixed_dt_s = Dt;
            rec.Meta.ship.motion_model = MotionModel.Manoeuvring3Dof.ToString();
        }
        live = new LiveAssessment();
        live.Output += e => Publish(e, keepTime: false);
        live.Begin(AssessmentContext.FromScenario(s, tug), RateHz);
        var sampler = new TrackSampler(RateHz);

        t = 0.0;
        Publish(new ExerciseEvent(ExerciseEventType.ExerciseStarted) { Subject = s.title }, keepTime: false);
        if (sampler.Due(t)) Sample();
        EndReason = ScenarioEndReason.None;
        while (EndReason == ScenarioEndReason.None)
        {
            Drive.Joystick = pilot.Update(System, Own, t);
            SettleJoystick(Drive.Joystick);
            Drive.Apply(Own, Dt);
            System.Apply(Own, Dt, t);
            Own.Step(Dt);
            t += Dt;
            foreach (ExerciseEvent e in System.Events) Publish(e, keepTime: true);
            System.Events.Clear();
            double gap = System.Client.GapM;
            if (!double.IsNaN(gap)) MaxClientGapM = Math.Max(MaxClientGapM, gap);
            if (System.Finished && s.Ends(ScenarioEndConditions.TugWorkDone)) EndReason = ScenarioEndReason.TugWorkDone;
            else if (t >= limit - 1e-9) EndReason = s.Ends(ScenarioEndConditions.TimeLimit) && t >= s.timeLimitSeconds - 1e-9 ? ScenarioEndReason.TimeLimit : ScenarioEndReason.Aborted;
            if (EndReason == ScenarioEndReason.None && sampler.Due(t)) Sample();
        }
        EndTime = t;
        Publish(new ExerciseEvent(ExerciseEventType.ExerciseEnded) { EndReason = EndReason, Subject = s.title }, keepTime: false);
        if (live.NeedsFinalSample(t) || (rec != null && (double.IsNaN(rec.LastTrackTime) || rec.LastTrackTime < t - 1e-6))) Sample();
        LiveResult = live.End();

        if (rec != null)
        {
            rec.Meta.completed = true;
            rec.Meta.end_reason = EndReason.ToString();
            rec.Meta.end_reason_ru = ScenarioText.EndReason(EndReason);
            rec.Meta.duration_s = Math.Round(t, 3);
            rec.Meta.summary = SessionSummary.From(ExerciseSummary.From(s, new List<ExerciseEvent>(Events), null));
            rec.Meta.assessment = SessionAssessmentSummary.From(LiveResult);
            rec.WriteAssessment(LiveResult.ToCsv());
            rec.Close();
        }
    }

    // 19.4в: событие JoystickChanged — как OwnTugDrive в тренажёре: положение держится SettleS (рукоятка не сдвигалась
    // больше SettleEps по оси), сдвиг от прошлого события ≥ MinChange по какой-либо оси или возврат в ноль; в событии —
    // доли упора, % (после мёртвой зоны). По нему оценивается реакция на команды лоцмана (G11).
    public const double SettleS = 0.5, MinChange = 0.05, SettleEps = 0.02;
    private JoystickState candidate, published;
    private double candidateSince;
    private bool candidateDone = true;

    private void SettleJoystick(JoystickState j)
    {
        if (j.DistanceTo(candidate) >= SettleEps) { candidate = j; candidateSince = t; candidateDone = false; return; }
        if (candidateDone || t - candidateSince < SettleS - 1e-9) return;
        candidateDone = true;
        if (candidate.DistanceTo(published) < MinChange && !(candidate.IsZero && !published.IsZero)) return;
        published = candidate;
        Publish(new ExerciseEvent(ExerciseEventType.JoystickChanged)
        {
            Value = Math.Round(TugJoystick.Shape(candidate.Surge) * 100.0), Value2 = Math.Round(TugJoystick.Shape(candidate.Sway) * 100.0),
            Index = (int)Math.Round(TugJoystick.Shape(candidate.Yaw) * 100.0), Subject = "рукой"
        }, keepTime: false);
    }

    // Событие: время — от TugWorkSystem (keepTime) или текущее; положение — своего буксира
    private void Publish(ExerciseEvent e, bool keepTime)
    {
        if (!keepTime) e.Time = t;
        e.InExercise = true;
        e.Position = new WorldPosition(Own.East, Own.North);
        double h = (Own.Psi * 180.0 / Math.PI) % 360.0;
        e.HeadingDeg = (float)(h < 0 ? h + 360.0 : h);
        e.SpeedKnots = (float)(Own.U / ShipParameters.KnotsToMs);
        Events.Add(e);
        rec?.AddEvent(e);
        live?.OnEvent(e);
    }

    // Срез — как ExerciseRecorder (track.csv; клиент — «CLIENT» в targets.csv; mooring.csv — TugWorkSystem.Sample)
    private void Sample()
    {
        ShipDynamicsModel m = Own;
        double heading = (m.Psi * 180.0 / Math.PI) % 360.0;
        if (heading < 0) heading += 360.0;
        double ve = m.VelocityEast, vn = m.VelocityNorth;
        double cog = ve * ve + vn * vn < 1e-4 ? heading : (Math.Atan2(ve, vn) * 180.0 / Math.PI + 360.0) % 360.0;
        var s = new TrackSample
        {
            Time = t, East = m.East, North = m.North, HeadingDeg = heading, CogDeg = cog,
            SpeedKnots = m.U / ShipParameters.KnotsToMs, RotDegMin = m.R * 180.0 / Math.PI * 60.0,
            RudderOrderedDeg = m.Ordered, RudderActualDeg = m.Delta, TelegraphSf = m.SpeedFactor,
            Telegraph = (int)ScenarioText.OrderFromSpeedFactor(m.SpeedFactor),
            XteM = double.NaN, Leg = -1, XteLimitM = double.NaN, Grounded = m.Grounded
        };
        targets.Clear();
        targets.Add(System.Client.Sample(t));
        MooringSample ms = System.Sample(t, Scenario.tugWork.berthIndex, Drive, System.Wind);
        if (rec != null) { rec.AddTrack(s); foreach (TargetSample ts in targets) rec.AddTarget(ts); rec.AddMooring(ms); }
        if (live.Running) live.AddSample(s, targets, ms);
    }
}
