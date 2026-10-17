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
// (LiveAssessment, LiveResult) — как в тренажёре; критерии работы буксира — 19.4в.
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
        double psiD = Math.Atan2(-nE, -nN) + Plan.ApproachAngleDeg * Math.PI / 180.0;
        double ePsi = Math.IEEERemainder(psiD - own.Psi, 2.0 * Math.PI) * 180.0 / Math.PI;
        double rDeg = own.R * 180.0 / Math.PI;
        double yaw = Clamp(KPsi * ePsi - KR * rDeg);

        WorldPosition c = sys.ContactPoint(), b = sys.Bow(own);
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
        }
        else if (!sys.Done(TugWorkStage.Push))
        {
            if (Plan.FixedPush) surge = Plan.PushPower;
            else if (Plan.FollowPilot && sys.PowerStep >= 0) surge = sys.PowerStep * TugJoystick.MenuStep;   // 19.4б-3: ступень лоцмана
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
            surge = Plan.ReleaseEarlyS > 0 && time - sys.DoneAt(TugWorkStage.Push) >= Plan.ReleaseEarlyS ? 0.0
                  : Plan.FollowPilot && sys.PowerStep >= 0 ? sys.PowerStep * TugJoystick.MenuStep : Plan.HoldPower;
        else if (!sys.Done(TugWorkStage.Clear))
        {
            surge = -Plan.ClearPower;
            sway = 0.0;
        }
        else return JoystickState.Zero;
        if (sys.Done(TugWorkStage.Approach)) sway = 0.0;   // у борта лагом не ходить: кранец с трением о борт разворачивает буксир, корпус касается борта
        return new JoystickState(Unshape(surge), Unshape(sway), Unshape(yaw));
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
        TugSpec spec = TugSpec.For(tug, BollardPullTonnes);
        System = new TugWorkSystem(s.tugWork, client, berth, spec)
        {
            Wind = s.windKnots > 0f ? Wind.Knots(s.windKnots, s.windFromDeg) : new Wind(0.0, 0.0),
            ApplyOwnWind = true
        };
        Own = new ShipDynamicsModel(tug) { MotionOverride = MotionModel.Manoeuvring3Dof, SpeedFactor = 0.0 };
        System.Place(Own);
        Drive = new AzimuthDrive(spec);
        var pilot = new TugWorkAutopilot { Plan = Plan };
        Events.Clear();
        MaxClientGapM = 0.0;
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
