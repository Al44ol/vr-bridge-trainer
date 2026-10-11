// ============================================================
// ExerciseAssessor.cs — Оценка действий обучающегося (C2)
//
// Один и тот же класс работает в реальном времени (LiveAssessment: события
// шины и срезы состояния 5 Гц из сцены) и офлайн (SessionAssessment: те же
// события и строки из events.csv, track.csv, targets.csv). Входы округляются
// так же, как в CSV (SessionFormat.Quantize), поэтому результаты совпадают до бита.
//
// Порядок обработки не зависит от того, как события и срезы пришли:
// срез t обрабатывается, когда пришёл следующий (или при завершении), а перед
// ним — все события с временем ≤ t. Так в шлеме событие, опубликованное в Update
// после среза того же шага, и в файле — одна и та же последовательность.
//
// По каждой цели на каждом срезе (своё судно — по курсу и скорости, без дрейфа):
//   R₀ = P_ц − P_св, V_r = V_ц − V_св, TCPA = −R₀·V_r/|V_r|², CPA = |R₀ + V_r·TCPA|
//   (CpaCalculator, п. 8.5); пеленг П, дистанция D, курсовые углы q_о и q_ц;
//   скорость изменения пеленга dП/dt — МНК по окну bearingWindowS (правило 7 г).
// Обнаружение — D ≤ D_обн (DetectionModel). Опасность столкновения (правило 7):
//   цель обнаружена и CPA < k·CPA_доп и 0 < TCPA ≤ T_гор; в этот момент ситуация
//   классифицируется (EncounterClassifier) и фиксируется до «окончательно прошла
//   и разошлась» (правило 13 г): TCPA < 0 и дистанция растёт. Одна ситуация на цель.
// Наименьшая дистанция — точный минимум на каждом интервале между срезами
//   (относительное движение на 0,2 с линейно): τ* = clamp(−r₀·Δr/|Δr|²; 0; 1).
// Пересечение курса впереди цели (правило 15): в системе цели (вперёд f, вправо y)
//   y меняет знак при f > 0 (точка пересечения — линейная интерполяция).
// Управляющее действие для расхождения — перекладка руля на |δ_зад| ≥ actionRudderDeg
//   (направление — знак итогового руля HelmEnded) или команда телеграфу обучающегося;
//   перекладки в сторону поворота маршрута в зоне поворота — это поворот маршрута,
//   а не манёвр расхождения (зона: до точки ≤ max(3·d_рек; 300 м), после ≤ max(2·d_рек; 300 м)).
// Изменение курса относительно маршрута: Δψ_м(k) = ψ_k − ψ_a − (χ(уч_k) − χ(уч_a)),
//   срезы в зонах поворота маршрута не учитываются.
// Формулы и пороги каждого критерия — AssessmentSettings.cs и ThesisNotes, раздел 10.
// ============================================================
using System;
using System.Collections.Generic;

public struct TargetObservation
{
    public int OwnIndex;
    public double Time;
    public double East, North, HeadingDeg, SpeedKnots;
    public double RangeM, BearingDeg, QOwnDeg, QTargetDeg, CpaM, TcpaS;
}

public sealed class Encounter
{
    public EncounterSituation Situation;
    public int OnsetObs, OnsetOwnIndex;
    public double OnsetTime, OnsetRangeM, OnsetCpaM, OnsetTcpaS;
    public double BearingRateDegMin = double.NaN;
    public bool SteadyBearing;
    /// <summary>Своё судно в момент возникновения — на участке узкости (правило 9).</summary>
    public bool NarrowChannel;
    public bool Ended;
    public double EndTime = double.NaN;
    public double MinDistanceM = double.PositiveInfinity, MinTime = double.NaN;
    public int MinOwnIndex = -1;
    public bool CrossedAhead;
    public double CrossAheadDistanceM = double.NaN, CrossAheadTime = double.NaN;
    /// <summary>dП/dt, °/мин — по первому полному окну наблюдения (в момент опасности или позже).</summary>
    public double BearingRateTime = double.NaN;
    /// <summary>Привилегированное судно может действовать (правило 17 а ii): TCPA ≤ … или D ≤ …</summary>
    public double FreeTime = double.NaN;
    /// <summary>Курс цели в момент (пере)классификации, °; цель отвернула больше чем на targetTurnDeg — ситуация определяется заново.</summary>
    public double TargetCourseRef;
    public bool TargetTurning;
    /// <summary>Сколько раз ситуация определялась заново (цель изменила курс, например по фарватеру).</summary>
    public int Reclassified;
    public EncounterSituation FirstSituation;
    /// <summary>Участок маршрута своего судна в момент (пере)классификации: после поворота по маршруту ситуация определяется заново.</summary>
    public int OwnLegRef = -1;
    public bool OwnTurning;
}

public sealed class TargetTrack
{
    public AssessmentTargetInfo Info;
    public readonly List<TargetObservation> Obs = new List<TargetObservation>();
    public bool Detected;
    public double DetectTime = double.NaN, DetectRangeNm = double.NaN, DetectionRangeNm = double.NaN;
    /// <summary>Текущее сближение (null — нет опасности).</summary>
    public Encounter Encounter;
    /// <summary>Завершённые сближения: с одной целью их может быть несколько (опасность исчезла после
    /// манёвра для другой цели, а позже возникла снова).</summary>
    public readonly List<Encounter> Done = new List<Encounter>();
    public double LastEndTime = double.NaN;
    public Encounter LastEncounter => Encounter ?? (Done.Count > 0 ? Done[Done.Count - 1] : null);
    public bool Collided;
    public double CollisionTime = double.NaN;

    /// <summary>Наблюдение с временем ≤ t (−1 — нет).</summary>
    public int ObsAt(double t)
    {
        int lo = 0, hi = Obs.Count - 1, ans = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (Obs[mid].Time <= t + 1e-9) { ans = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return ans;
    }
}

public enum ControlActionKind { Helm, Telegraph }

public sealed class ControlAction
{
    public ControlActionKind Kind;
    public double Time;
    public WorldPosition Position;
    public double RudderStart = double.NaN, RudderFinal = double.NaN;
    public double SfNew = double.NaN, SfOld = double.NaN;
}

public sealed class ExerciseAssessor
{
    public AssessmentContext Context { get; }
    private AssessmentSettings S => Context.Settings;

    /// <summary>Цель обнаружена / возникла опасность столкновения (для шины в реальном времени).</summary>
    public event Action<TargetTrack> TargetDetected, EncounterStarted;

    public IReadOnlyList<TrackSample> Own => own;
    public IReadOnlyList<TargetTrack> Targets => targets;
    public IReadOnlyList<ControlAction> Actions => actions;
    public bool Finished { get; private set; }
    public ScenarioEndReason EndReason { get; private set; }
    public double EndTime { get; private set; } = double.NaN;
    public double Time => own.Count > 0 ? own[own.Count - 1].Time : 0.0;

    private readonly List<TrackSample> own = new List<TrackSample>();
    private readonly List<TargetTrack> targets = new List<TargetTrack>();
    private readonly List<ControlAction> actions = new List<ControlAction>();
    private readonly List<ExerciseEvent> queue = new List<ExerciseEvent>();
    private bool hasPending;
    private TrackSample pendingOwn;
    private readonly List<TargetSample> pendingTargets = new List<TargetSample>();
    private bool suppressCallbacks;

    private WeatherPresetId weather;
    private TimeOfDay timeOfDay;
    private int openHelm = -1;
    private int groundings, collisions;
    private string firstGrounding = "", firstCollision = "";
    private readonly HashSet<int> passedWaypoints = new HashSet<int>();
    private readonly Dictionary<int, double> turnSpeed = new Dictionary<int, double>();

    private const double Nm = NavigationLights.MetersPerNm;

    public ExerciseAssessor(AssessmentContext context)
    {
        Context = context ?? new AssessmentContext();
        weather = Context.Weather;
        timeOfDay = Context.TimeOfDay;
        foreach (AssessmentTargetInfo t in Context.Targets) targets.Add(new TargetTrack { Info = t });
    }

    // ─────────── Входы ───────────
    public static bool IsAssessmentOutput(ExerciseEventType t) =>
        t == ExerciseEventType.TargetDetected || t == ExerciseEventType.EncounterStarted;

    public void AddEvent(in ExerciseEvent e)
    {
        if (Finished || IsAssessmentOutput(e.Type)) return;
        queue.Add(e);
    }

    public void AddSample(in TrackSample ownSample, IReadOnlyList<TargetSample> targetSamples)
    {
        if (Finished) return;
        if (hasPending) ProcessPending();
        pendingOwn = ownSample;
        pendingTargets.Clear();
        if (targetSamples != null) pendingTargets.AddRange(targetSamples);
        hasPending = true;
    }

    /// <summary>Конец упражнения: обработать всё накопленное и дать итог.</summary>
    public AssessmentResult Finish()
    {
        if (!Finished)
        {
            suppressCallbacks = true;
            if (hasPending) ProcessPending();
            foreach (ExerciseEvent e in queue) ProcessEvent(e);
            queue.Clear();
            Finished = true;
            suppressCallbacks = false;
        }
        return Evaluate();
    }

    private void ProcessPending()
    {
        double t = pendingOwn.Time;
        int n = 0;
        while (n < queue.Count && queue[n].Time <= t + 1e-9) ProcessEvent(queue[n++]);
        if (n > 0) queue.RemoveRange(0, n);
        ProcessSample(pendingOwn, pendingTargets);
        hasPending = false;
    }

    private void ProcessEvent(in ExerciseEvent e)
    {
        switch (e.Type)
        {
            case ExerciseEventType.WeatherChanged:
                if (Enum.IsDefined(typeof(WeatherPresetId), e.Index)) weather = (WeatherPresetId)e.Index;
                break;
            case ExerciseEventType.TimeOfDayChanged:
                if (Enum.IsDefined(typeof(TimeOfDay), e.Index)) timeOfDay = (TimeOfDay)e.Index;
                break;
            case ExerciseEventType.HelmStarted:
                actions.Add(new ControlAction { Kind = ControlActionKind.Helm, Time = e.Time, Position = e.Position, RudderStart = e.Value });
                openHelm = actions.Count - 1;
                break;
            case ExerciseEventType.HelmEnded:
                if (openHelm >= 0) actions[openHelm].RudderFinal = e.Value;
                openHelm = -1;
                break;
            case ExerciseEventType.TelegraphChanged:
                if (!e.ByInstructor)
                    actions.Add(new ControlAction { Kind = ControlActionKind.Telegraph, Time = e.Time, Position = e.Position, SfNew = e.Value, SfOld = e.Value2 });
                break;
            case ExerciseEventType.WaypointPassed:
                passedWaypoints.Add(e.Index);
                break;
            case ExerciseEventType.Grounded:
                if (groundings++ == 0) firstGrounding = e.Subject ?? "";
                break;
            case ExerciseEventType.Collision:
                if (collisions++ == 0) firstCollision = e.Subject ?? "";
                TargetTrack tr = Track(e.Subject);
                if (!tr.Collided)
                {
                    tr.Collided = true;
                    tr.CollisionTime = e.Time;
                    if (tr.Encounter != null && !tr.Encounter.Ended)
                    {
                        tr.Encounter.MinDistanceM = 0.0;
                        tr.Encounter.MinTime = e.Time;
                        tr.Encounter.MinOwnIndex = own.Count - 1;
                        tr.Encounter.Ended = true;
                        tr.Encounter.EndTime = e.Time;
                    }
                }
                break;
            case ExerciseEventType.ExerciseEnded:
                EndReason = e.EndReason;
                EndTime = e.Time;
                break;
        }
    }

    private TargetTrack Track(string id)
    {
        foreach (TargetTrack t in targets) if (t.Info.Id == id) return t;
        var n = new TargetTrack { Info = new AssessmentTargetInfo { Id = id ?? "", Name = id ?? "", Index = targets.Count } };
        targets.Add(n);
        return n;
    }

    // ─────────── Срез ───────────
    private void ProcessSample(in TrackSample s, List<TargetSample> tgts)
    {
        int oi = own.Count;
        own.Add(s);
        RememberTurnSpeed(s);
        double visibility = DetectionModel.Visibility(weather, Context.ScenarioVisibilityM);
        foreach (TargetSample ts in tgts)
        {
            TargetTrack tr = Track(ts.Id);
            TargetObservation o = Observe(oi, s, ts);
            bool hasPrev = tr.Obs.Count > 0;
            TargetObservation prev = hasPrev ? tr.Obs[tr.Obs.Count - 1] : default;
            tr.Obs.Add(o);

            if (!tr.Detected)
            {
                double dDet = DetectionModel.RangeNm(tr.Info.LengthM, o.QTargetDeg, timeOfDay, visibility, Context.DetectionRangeNm);
                if (o.RangeM <= dDet * Nm)
                {
                    tr.Detected = true;
                    tr.DetectTime = o.Time;
                    tr.DetectRangeNm = o.RangeM / Nm;
                    tr.DetectionRangeNm = dDet;
                    if (!suppressCallbacks) TargetDetected?.Invoke(tr);
                }
            }

            Encounter enc = tr.Encounter;
            if (enc != null && enc.Ended && !tr.Collided)
            {
                tr.Done.Add(enc);
                tr.LastEndTime = enc.EndTime;
                tr.Encounter = enc = null;
            }
            if (enc == null)
            {
                // Повторное сближение с той же целью — не раньше 60 с после прежнего и при TCPA ≥ 1 мин
                // (иначе цель уже на кратчайшей дистанции и развивающейся опасности нет)
                bool repeat = !double.IsNaN(tr.LastEndTime);
                bool pause = repeat && (o.Time - tr.LastEndTime < 60.0 || o.TcpaS < 60.0);
                if (tr.Detected && !tr.Collided && !pause && Risk(tr, o))
                {
                    tr.Encounter = enc = new Encounter
                    {
                        Situation = EncounterClassifier.Classify(o.QOwnDeg, o.QTargetDeg, S.headOnSectorDeg),
                        OnsetObs = tr.Obs.Count - 1, OnsetOwnIndex = oi, OnsetTime = o.Time,
                        OnsetRangeM = o.RangeM, OnsetCpaM = o.CpaM, OnsetTcpaS = o.TcpaS,
                        MinDistanceM = o.RangeM, MinTime = o.Time, MinOwnIndex = oi,
                        NarrowChannel = NarrowLeg(s.Leg),
                        TargetCourseRef = o.HeadingDeg,
                        OwnLegRef = s.Leg
                    };
                    enc.FirstSituation = enc.Situation;
                    if (!suppressCallbacks) EncounterStarted?.Invoke(tr);
                }
            }
            else if (!enc.Ended && hasPrev)
            {
                if (!UpdateEncounter(tr, enc, prev, o, s)) tr.Encounter = enc = null;   // цель отвернула, опасности нет — ситуация снята
            }
            // Скорость изменения пеленга — когда наблюдения покрывают окно (в момент опасности или позже)
            if (enc != null && double.IsNaN(enc.BearingRateDegMin))
            {
                enc.BearingRateDegMin = BearingRate(tr, o.Time);
                enc.SteadyBearing = !double.IsNaN(enc.BearingRateDegMin) && Math.Abs(enc.BearingRateDegMin) < S.steadyBearingDegPerMin;
                if (!double.IsNaN(enc.BearingRateDegMin)) enc.BearingRateTime = o.Time;
            }
        }
    }

    private bool Risk(TargetTrack tr, in TargetObservation o)
    {
        double limit = S.riskCpaFactor * Context.RequiredCpaM(tr.Info);
        return o.CpaM < limit && o.TcpaS > 0.0 && o.TcpaS <= S.riskHorizonMin * 60.0;
    }

    private static TargetObservation Observe(int oi, in TrackSample s, in TargetSample t)
    {
        var ownPos = new WorldPosition(s.East, s.North);
        var tgtPos = new WorldPosition(t.East, t.North);
        CpaResult c = CpaCalculator.FromCourses(ownPos, s.HeadingDeg, s.SpeedKnots, tgtPos, t.HeadingDeg, t.SpeedKnots);
        return new TargetObservation
        {
            OwnIndex = oi, Time = s.Time,
            East = t.East, North = t.North, HeadingDeg = t.HeadingDeg, SpeedKnots = t.SpeedKnots,
            RangeM = c.Range, BearingDeg = c.BearingDeg,
            QOwnDeg = CpaCalculator.RelativeBearing(c.BearingDeg, s.HeadingDeg),
            QTargetDeg = CpaCalculator.RelativeBearing(c.BearingDeg + 180.0, t.HeadingDeg),
            CpaM = c.Cpa, TcpaS = c.Tcpa
        };
    }

    /// <summary>Обновить сближение; false — цель изменила курс и опасности больше нет (сближение снимается).</summary>
    private bool UpdateEncounter(TargetTrack tr, Encounter enc, in TargetObservation a, in TargetObservation b, in TrackSample ownNow)
    {
        // Цель изменила курс (например, идёт по фарватеру и повернула в колене): когда новый курс
        // установился (за последние 10 с изменился меньше чем на 2°), ситуация определяется заново.
        // То же — после поворота своего судна по маршруту (сменился участок с поворотом), но не после манёвра расхождения.
        if (Math.Abs(EncounterClassifier.Signed(b.HeadingDeg - enc.TargetCourseRef)) > S.targetTurnDeg) enc.TargetTurning = true;
        if (ownNow.Leg != enc.OwnLegRef && enc.OwnLegRef >= 0 && TurnAt(enc.OwnLegRef + 1) != 0.0) enc.OwnTurning = true;
        if (enc.TargetTurning || enc.OwnTurning)
        {
            int k = tr.ObsAt(b.Time - 10.0);
            int ko = OwnIndexAt(b.Time - 10.0);
            bool targetSteady = k >= 0 && b.Time - tr.Obs[k].Time >= 9.9 && Math.Abs(EncounterClassifier.Signed(b.HeadingDeg - tr.Obs[k].HeadingDeg)) < 2.0;
            bool ownSteady = ko >= 0 && Math.Abs(EncounterClassifier.Signed(ownNow.HeadingDeg - own[ko].HeadingDeg)) < 2.0;
            if ((!enc.TargetTurning || targetSteady) && (!enc.OwnTurning || ownSteady))
            {
                if (!Risk(tr, b)) return false;
                enc.Situation = EncounterClassifier.Classify(b.QOwnDeg, b.QTargetDeg, S.headOnSectorDeg);
                enc.OnsetObs = tr.Obs.Count - 1; enc.OnsetOwnIndex = b.OwnIndex; enc.OnsetTime = b.Time;
                enc.OnsetRangeM = b.RangeM; enc.OnsetCpaM = b.CpaM; enc.OnsetTcpaS = b.TcpaS;
                enc.NarrowChannel = NarrowLeg(ownNow.Leg);
                enc.CrossedAhead = false; enc.CrossAheadDistanceM = double.NaN; enc.CrossAheadTime = double.NaN;
                enc.FreeTime = double.NaN; enc.BearingRateDegMin = double.NaN; enc.SteadyBearing = false;
                enc.TargetCourseRef = b.HeadingDeg; enc.TargetTurning = false;
                enc.OwnLegRef = ownNow.Leg; enc.OwnTurning = false;
                enc.Reclassified++;
                if (!suppressCallbacks) EncounterStarted?.Invoke(tr);
            }
        }

        TrackSample oa = own[a.OwnIndex], ob = own[b.OwnIndex];
        // Наименьшая дистанция на интервале: относительное положение линейно
        double r0e = a.East - oa.East, r0n = a.North - oa.North;
        double r1e = b.East - ob.East, r1n = b.North - ob.North;
        double de = r1e - r0e, dn = r1n - r0n;
        double d2 = de * de + dn * dn;
        double tau = d2 > 1e-12 ? -(r0e * de + r0n * dn) / d2 : 1.0;
        tau = tau < 0 ? 0 : tau > 1 ? 1 : tau;
        double me = r0e + de * tau, mn = r0n + dn * tau;
        double dist = Math.Sqrt(me * me + mn * mn);
        if (dist < enc.MinDistanceM)
        {
            enc.MinDistanceM = dist;
            enc.MinTime = a.Time + tau * (b.Time - a.Time);
            enc.MinOwnIndex = tau < 0.5 ? a.OwnIndex : b.OwnIndex;
        }

        // Пересечение курса впереди цели (система цели: f — вперёд, y — вправо)
        if (enc.Situation == EncounterSituation.CrossingGiveWay && !enc.CrossedAhead)
        {
            Frame(a, oa, out double f0, out double y0);
            Frame(b, ob, out double f1, out double y1);
            if ((y0 < 0 && y1 >= 0) || (y0 > 0 && y1 <= 0))
            {
                double k = Math.Abs(y0 - y1) > 1e-9 ? y0 / (y0 - y1) : 1.0;
                double f = f0 + (f1 - f0) * k;
                if (f > 0)
                {
                    enc.CrossedAhead = true;
                    enc.CrossAheadDistanceM = f;
                    enc.CrossAheadTime = a.Time + k * (b.Time - a.Time);
                }
            }
        }

        if (double.IsNaN(enc.FreeTime) && (b.TcpaS <= S.standOnFreeTcpaMin * 60.0 || b.RangeM <= S.standOnFreeRangeNm * Nm))
            enc.FreeTime = b.Time;

        // Окончательно прошла и разошлась (правило 13 г): TCPA < 0, дистанция растёт, и суда прошли друг
        // друга — цель позади траверза своего судна или своё судно более 22,5° позади траверза цели
        // (кроме обгона своим судном: там оно позади траверза цели всё время); либо дистанция
        // в 1,5 раза больше, чем в начале. Так отворот во время сближения не завершает его досрочно.
        bool opening = b.TcpaS < 0.0 && b.RangeM > a.RangeM;
        bool abaft = Math.Abs(EncounterClassifier.Signed(b.QOwnDeg)) >= 90.0
                     || (enc.Situation != EncounterSituation.Overtaking && Math.Abs(EncounterClassifier.Signed(b.QTargetDeg)) >= EncounterClassifier.AbaftBeamDeg);
        bool far = b.RangeM > 1.5 * enc.OnsetRangeM && !Risk(tr, b);
        if (opening && (abaft || far))
        {
            enc.Ended = true;
            enc.EndTime = b.Time;
        }
        return true;
    }

    private static void Frame(in TargetObservation t, in TrackSample s, out double fwd, out double right)
    {
        double psi = t.HeadingDeg * Math.PI / 180.0;
        double de = s.East - t.East, dn = s.North - t.North;
        fwd = de * Math.Sin(psi) + dn * Math.Cos(psi);
        right = de * Math.Cos(psi) - dn * Math.Sin(psi);
    }

    // Скорость изменения пеленга, °/мин: МНК по окну
    private double BearingRate(TargetTrack tr, double t)
    {
        double t0 = t - S.bearingWindowS;
        if (tr.Obs.Count == 0 || tr.Obs[0].Time > t0 + 1e-9) return double.NaN;   // окно ещё не заполнено
        int first = tr.Obs.Count - 1;
        while (first > 0 && tr.Obs[first - 1].Time >= t0 - 1e-9) first--;
        int n = tr.Obs.Count - first;
        if (n < 3) return double.NaN;
        double b0 = tr.Obs[first].BearingDeg, sx = 0, sy = 0, sxx = 0, sxy = 0, prev = 0;
        for (int i = first; i < tr.Obs.Count; i++)
        {
            double x = tr.Obs[i].Time - t;
            double y = prev + EncounterClassifier.Signed(tr.Obs[i].BearingDeg - (b0 + prev));   // развёртка через 0/360
            prev = y;
            sx += x; sy += y; sxx += x * x; sxy += x * y;
        }
        double den = n * sxx - sx * sx;
        return Math.Abs(den) > 1e-9 ? (n * sxy - sx * sy) / den * 60.0 : double.NaN;
    }

    // ─────────── Маршрут ───────────
    private IReadOnlyList<RouteWaypoint> Route => Context.Route;
    private bool RouteValid => Route.Count >= 2;

    private bool NarrowLeg(int leg) => leg >= 0 && leg + 1 < Route.Count && Route[leg + 1].narrowChannel;

    private double LegCourse(int leg) =>
        leg >= 0 && leg + 1 < Route.Count ? RouteFollower.CourseOf(Route[leg].position, Route[leg + 1].position) : double.NaN;

    /// <summary>Угол поворота маршрута в точке i (> 0 — вправо), °; 0 — не точка поворота.</summary>
    private double TurnAt(int i)
    {
        if (i < 1 || i + 1 >= Route.Count) return 0.0;
        double turn = EncounterClassifier.Signed(LegCourse(i) - LegCourse(i - 1));
        return Math.Abs(turn) >= 1.0 ? turn : 0.0;
    }

    private static double Atd(WorldPosition a, WorldPosition b, WorldPosition p)
    {
        WorldPosition d = b - a;
        double len = d.Magnitude;
        return len > 1e-9 ? ((p.East - a.East) * d.East + (p.North - a.North) * d.North) / len : 0.0;
    }

    // Скорость при подходе к точке поворота (за милю) — для d_рек
    private void RememberTurnSpeed(in TrackSample s)
    {
        if (!RouteValid || s.Leg < 0) return;
        int i = s.Leg + 1;
        if (TurnAt(i) == 0.0 || turnSpeed.ContainsKey(i)) return;
        double len = (Route[i].position - Route[i - 1].position).Magnitude;
        double remaining = len - Atd(Route[i - 1].position, Route[i].position, s.Position);
        if (remaining <= Nm) turnSpeed[i] = Math.Abs(s.SpeedKnots) * ShipParameters.KnotsToMs;
    }

    /// <summary>Рекомендуемое упреждение поворота d_рек в точке i, м (NaN — нет параметров судна).</summary>
    public double RecommendedWheelOver(int i)
    {
        double turn = TurnAt(i);
        if (Context.Own == null || turn == 0.0) return double.NaN;
        double u = turnSpeed.TryGetValue(i, out double v) ? v : own.Count > 0 ? Math.Abs(own[own.Count - 1].SpeedKnots) * ShipParameters.KnotsToMs : Context.Own.ServiceSpeedMs;
        return RouteFollower.WheelOverDistance(Context.Own, u, Math.Abs(turn), S.wheelOverRudderDeg);
    }

    private void Zone(int i, out double before, out double after)
    {
        double d = RecommendedWheelOver(i);
        if (double.IsNaN(d)) d = 200.0;
        before = Math.Max(3.0 * d, 300.0);
        after = Math.Max(2.0 * d, 300.0);
    }

    /// <summary>
    /// Точка поворота, в зоне которой находится судно (−1 — вне зон);
    /// toGo — до точки по входящему участку (> 0), после неё — минус пройденное по исходящему.
    /// </summary>
    private int TurnZone(WorldPosition p, int leg, out double toGo)
    {
        toGo = double.NaN;
        if (!RouteValid || leg < 0) return -1;
        int i = leg + 1;   // точка, к которой ведёт участок
        if (TurnAt(i) != 0.0)
        {
            Zone(i, out double before, out _);
            double len = (Route[i].position - Route[i - 1].position).Magnitude;
            double remaining = len - Atd(Route[i - 1].position, Route[i].position, p);
            if (remaining <= before) { toGo = remaining; return i; }
        }
        i = leg;           // точка, от которой ведёт участок
        if (i + 1 < Route.Count && TurnAt(i) != 0.0)
        {
            Zone(i, out _, out double after);
            double atd = Atd(Route[i].position, Route[i + 1].position, p);
            if (atd <= after) { toGo = -atd; return i; }
        }
        return -1;
    }

    private int OwnIndexAt(double t)
    {
        int lo = 0, hi = own.Count - 1, ans = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (own[mid].Time <= t + 1e-9) { ans = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return ans;
    }

    // ─────────── Действия ───────────
    /// <summary>Итоговый руль перекладки (если перекладка ещё идёт — заданный руль по последнему срезу).</summary>
    private double FinalRudder(ControlAction a)
    {
        if (!double.IsNaN(a.RudderFinal)) return a.RudderFinal;
        return own.Count > 0 ? own[own.Count - 1].RudderOrderedDeg : 0.0;
    }

    /// <summary>Перекладка в сторону поворота маршрута в зоне поворота (−1 — нет); toGo — см. TurnZone.</summary>
    private int RouteTurnOf(ControlAction a, out double toGo)
    {
        toGo = double.NaN;
        if (a.Kind != ControlActionKind.Helm) return -1;
        int k = OwnIndexAt(a.Time);
        if (k < 0) return -1;
        int i = TurnZone(a.Position, own[k].Leg, out toGo);
        if (i < 0) return -1;
        double rudder = FinalRudder(a);
        return Math.Abs(rudder) >= S.actionRudderDeg && Math.Sign(rudder) == Math.Sign(TurnAt(i)) ? i : -1;
    }

    /// <summary>Манёвр расхождения: перекладка |δ| ≥ порога (не поворот маршрута) или команда телеграфу.</summary>
    private bool IsManoeuvre(ControlAction a)
    {
        if (a.Kind == ControlActionKind.Telegraph) return true;
        return Math.Abs(FinalRudder(a)) >= S.actionRudderDeg && RouteTurnOf(a, out _) < 0;
    }

    private ControlAction FirstManoeuvre(double from, double to, bool helmOnly = false)
    {
        foreach (ControlAction a in actions)
        {
            if (a.Time < from - 1e-9) continue;
            if (a.Time > to + 1e-9) break;
            if (helmOnly && a.Kind != ControlActionKind.Helm) continue;
            if (IsManoeuvre(a)) return a;
        }
        return null;
    }

    private static string Side(double rudder) => rudder > 0 ? "вправо" : "влево";

    private string Describe(ControlAction a)
    {
        if (a == null) return "нет";
        if (a.Kind == ControlActionKind.Telegraph)
            return $"телеграф «{ScenarioText.Telegraph(ScenarioText.OrderFromSpeedFactor(a.SfNew))}»";
        double r = FinalRudder(a);
        return $"руль {AssessmentResult.Num(Math.Abs(r))}° {(r > 0 ? "ПБ" : "ЛБ")}";
    }

    /// <summary>Наибольшее изменение курса относительно маршрута и снижение скорости на [ka; kb].</summary>
    // dir = ±1 — учитывать изменение курса только в сторону манёвра (возврат на маршрут не в счёт); 0 — по модулю.
    private void Deviation(int ka, int kb, int dir, out double maxDevDeg, out double signedAtMax, out double speedDrop)
    {
        maxDevDeg = 0; signedAtMax = 0; speedDrop = 0;
        if (ka < 0 || ka >= own.Count) return;
        TrackSample a = own[ka];
        double chiA = LegCourse(a.Leg);
        double ua = Math.Abs(a.SpeedKnots);
        kb = Math.Min(kb, own.Count - 1);
        for (int k = ka; k <= kb; k++)
        {
            TrackSample s = own[k];
            if (ua > 0.5) speedDrop = Math.Max(speedDrop, 1.0 - Math.Abs(s.SpeedKnots) / ua);
            if (TurnZone(s.Position, s.Leg, out _) >= 0) continue;
            double chiK = LegCourse(s.Leg);
            double legShift = double.IsNaN(chiA) || double.IsNaN(chiK) ? 0.0 : EncounterClassifier.Signed(chiK - chiA);
            double dev = EncounterClassifier.Signed(s.HeadingDeg - a.HeadingDeg - legShift);
            double m = dir == 0 ? Math.Abs(dev) : dir * dev;
            if (m > maxDevDeg) { maxDevDeg = m; signedAtMax = dev; }
        }
    }

    // Интервалы манёвров расхождения: маршрутные критерии R1–R3, R8a их не учитывают (правила МППСС — выше
    // плана перехода). Интервал — от первого действия до возврата в коридор после окончания сближения,
    // но не дольше routeReturnAllowanceMin.
    private readonly List<(double from, double to)> routeExclusions = new List<(double, double)>();

    private void ExcludeFromRoute(double from, double encounterEnd)
    {
        double to = double.PositiveInfinity;
        if (!double.IsInfinity(encounterEnd))
        {
            double cap = encounterEnd + S.routeReturnAllowanceMin * 60.0;
            to = cap;
            for (int k = Math.Max(0, OwnIndexAt(encounterEnd)); k < own.Count; k++)
            {
                TrackSample s = own[k];
                if (s.Time > cap) break;
                if (s.Time >= encounterEnd && !double.IsNaN(s.XteM) && s.XteLimitM > 0 && Math.Abs(s.XteM) <= s.XteLimitM) { to = s.Time; break; }
            }
        }
        routeExclusions.Add((from, to));
    }

    private bool ExcludedFromRoute(double t)
    {
        foreach (var w in routeExclusions) if (t >= w.from - 1e-9 && t < w.to - 1e-9) return true;
        return false;
    }

    // ─────────── Оценка ───────────
    /// <summary>Оценка по данным на сейчас (до Finish — промежуточная).</summary>
    public AssessmentResult Evaluate()
    {
        bool final = Finished;
        var r = new AssessmentResult { Final = final, Time = final && !double.IsNaN(EndTime) ? EndTime : Time, EndReason = EndReason };
        routeExclusions.Clear();
        bool aborted = final && (EndReason == ScenarioEndReason.Aborted || EndReason == ScenarioEndReason.None);

        foreach (TargetTrack tr in targets)
        {
            int n = tr.Done.Count + (tr.Encounter != null ? 1 : 0);
            for (int k = 0; k < n; k++)
            {
                Encounter e = k < tr.Done.Count ? tr.Done[k] : tr.Encounter;
                EvaluateEncounter(r, tr, e, n > 1 ? $"{tr.Info.Id} ({k + 1})" : tr.Info.Id, final, aborted);
            }
        }
        EvaluateRoute(r, final, aborted);

        Add(r, AssessmentCatalog.A1, "", groundings, groundings > 0 ? $"посадка на мель «{firstGrounding}»" : "нет");
        Add(r, AssessmentCatalog.A2, "", collisions, collisions > 0 ? $"столкновение с «{firstCollision}»" : "нет");
        r.Failed = groundings > 0 || collisions > 0;
        r.FailureText = collisions > 0 ? $"столкновение с «{firstCollision}»" : groundings > 0 ? $"посадка на мель «{firstGrounding}»" : "";
        r.Aggregate(S);
        return r;
    }

    private void Add(AssessmentResult r, string code, string subject, double value, string note)
    {
        CriterionSetting set = S.Get(code);
        if (!set.enabled) return;
        CriterionInfo info = AssessmentCatalog.Find(code);
        r.Items.Add(new CriterionResult
        {
            Info = info, Setting = set, Subject = subject ?? "", Evaluated = true, Value = value,
            Passed = AssessmentResult.Meets(value, set.pass, info.HigherIsBetter),
            Score = AssessmentResult.ScoreOf(value, set, info.HigherIsBetter), Note = note ?? ""
        });
    }

    private void Skip(AssessmentResult r, string code, string subject, string why)
    {
        CriterionSetting set = S.Get(code);
        if (!set.enabled) return;
        r.Items.Add(new CriterionResult { Info = AssessmentCatalog.Find(code), Setting = set, Subject = subject ?? "", Evaluated = false, Note = why });
    }

    private void EvaluateEncounter(AssessmentResult r, TargetTrack tr, Encounter e, string id, bool final, bool aborted)
    {
        EncounterSituation sit = e.Situation;
        bool collided = tr.Collided && ReferenceEquals(e, tr.LastEncounter);
        bool complete = e.Ended || collided;
        double end = e.Ended ? e.EndTime : Time;
        double reqM = Context.RequiredCpaM(tr.Info);

        var summary = new EncounterSummary
        {
            TargetId = id, TargetName = tr.Info.Name, Situation = sit,
            DetectTime = tr.DetectTime, DetectRangeNm = tr.DetectRangeNm, DetectionRangeNm = tr.DetectionRangeNm,
            OnsetTime = e.OnsetTime, OnsetCpaNm = e.OnsetCpaM / Nm, OnsetTcpaMin = e.OnsetTcpaS / 60.0,
            BearingRateDegMin = e.BearingRateDegMin, SteadyBearing = e.SteadyBearing, NarrowChannel = e.NarrowChannel,
            MinDistanceM = e.MinDistanceM, MinTime = e.MinTime, RequiredCpaM = reqM, Complete = complete, Collided = collided
        };
        r.Encounters.Add(summary);
        if (!complete)
        {
            // Сближение не завершено (идёт или упражнение кончилось раньше): по нему ничего не оценивается,
            // кроме уже случившегося пересечения курса по носу
            if (e.Situation == EncounterSituation.CrossingGiveWay && e.CrossedAhead)
                Add(r, AssessmentCatalog.E4, id, 0, $"пересёк курс в {AssessmentResult.Num(e.CrossAheadDistanceM / Nm, 2)} мили впереди цели");
            if (final) Skip(r, AssessmentCatalog.E8, id, "сближение не завершено");
            return;
        }

        bool giveWayActive = sit == EncounterSituation.HeadOn || sit == EncounterSituation.CrossingGiveWay;
        ControlAction act = FirstManoeuvre(e.OnsetTime, end);
        ControlAction helm = FirstManoeuvre(e.OnsetTime, end, helmOnly: true);
        if (act != null)
        {
            summary.ActionTime = act.Time; summary.Action = Describe(act);
            ExcludeFromRoute(act.Time, e.Ended ? e.EndTime : double.PositiveInfinity);
        }

        if (giveWayActive)
        {
            // E1 — манёвр достаточен (в узкости не требуется: правило 9, см. R8b)
            if (e.NarrowChannel) Skip(r, AssessmentCatalog.E1, id, "в узкости — по правилу 9 (R8b)");
            else if (act == null) Add(r, AssessmentCatalog.E1, id, 0.0, "манёвра нет");
            else
            {
                int dir = act.Kind == ControlActionKind.Helm ? Math.Sign(FinalRudder(act)) : 0;
                double until = double.IsNaN(e.MinTime) ? end : Math.Min(Math.Max(e.MinTime, act.Time), end);   // до момента наименьшей дистанции
                Deviation(OwnIndexAt(act.Time), OwnIndexAt(until), dir, out double dev, out double signedDev, out double drop);
                double v = Math.Max(dev / S.actionCourseDeg, drop / S.actionSpeedReduction);
                Add(r, AssessmentCatalog.E1, id, v,
                    $"курс {AssessmentResult.Num(dev)}° {(signedDev >= 0 ? "вправо" : "влево")}, скорость −{AssessmentResult.Num(100 * drop)} % (нужно ≥ {AssessmentResult.Num(S.actionCourseDeg)}° или ≥ {AssessmentResult.Num(100 * S.actionSpeedReduction)} %)");
            }

            // E2 — заблаговременно: TCPA в начале манёвра
            if (act == null) Skip(r, AssessmentCatalog.E2, id, "манёвра нет");
            else if (e.OnsetTcpaS / 60.0 < S.Get(AssessmentCatalog.E2).pass)
                Skip(r, AssessmentCatalog.E2, id, $"опасность возникла при TCPA {AssessmentResult.Num(e.OnsetTcpaS / 60.0, 1)} мин — заблаговременность не оценивается");
            else
            {
                int oi = tr.ObsAt(act.Time);
                TargetObservation o = tr.Obs[Math.Max(0, oi)];
                Add(r, AssessmentCatalog.E2, id, o.TcpaS / 60.0,
                    $"{Describe(act)}: TCPA {AssessmentResult.Num(o.TcpaS / 60.0, 1)} мин, D {AssessmentResult.Num(o.RangeM / Nm, 2)} мили");
            }

            // E9 — время реакции: от возникновения опасности (цель обнаружена и CPA мала) до первого действия
            if (act == null) Skip(r, AssessmentCatalog.E9, id, "действия нет");
            else Add(r, AssessmentCatalog.E9, id, act.Time - e.OnsetTime,
                     $"{AssessmentResult.Num(act.Time - e.OnsetTime)} с от возникновения опасности (D {AssessmentResult.Num(e.OnsetRangeM / Nm, 2)} мили; цель обнаружена на {AssessmentResult.Num(tr.DetectRangeNm, 2)} мили) до действия: {Describe(act)}");
        }

        // E3 — на встречных курсах вправо
        if (sit == EncounterSituation.HeadOn)
        {
            if (helm == null) Skip(r, AssessmentCatalog.E3, id, "перекладок нет");
            else { double rd = FinalRudder(helm); Add(r, AssessmentCatalog.E3, id, rd > 0 ? 1 : 0, $"первая перекладка {Side(rd)} ({Describe(helm)})"); }
        }

        if (sit == EncounterSituation.CrossingGiveWay)
        {
            // E4 — не пересекать курс по носу
            Add(r, AssessmentCatalog.E4, id, e.CrossedAhead ? 0 : 1,
                e.CrossedAhead ? $"пересёк курс в {AssessmentResult.Num(e.CrossAheadDistanceM / Nm, 2)} мили впереди цели" : "курс впереди цели не пересекал");
            // E5 — поворот влево на цель справа (замечание)
            if (helm == null) Skip(r, AssessmentCatalog.E5, id, "перекладок нет");
            else { double rd = FinalRudder(helm); Add(r, AssessmentCatalog.E5, id, rd > 0 ? 1 : 0, $"первая перекладка {Side(rd)}"); }
        }

        if (sit == EncounterSituation.CrossingStandOn || sit == EncounterSituation.Overtaken)
        {
            // E6 — сохранять курс и скорость до момента, когда можно действовать (17 а ii)
            double free = double.IsNaN(e.FreeTime) ? end : Math.Min(e.FreeTime, end);
            if (free <= e.OnsetTime + 1e-9)
                Skip(r, AssessmentCatalog.E6, id, $"действия разрешены сразу (TCPA ≤ {AssessmentResult.Num(S.standOnFreeTcpaMin)} мин или D ≤ {AssessmentResult.Num(S.standOnFreeRangeNm)} мили)");
            else
            {
                Deviation(e.OnsetOwnIndex, OwnIndexAt(free), 0, out double dev, out _, out double drop);
                double v = Math.Max(dev / S.standOnHeadingTolDeg, drop / S.standOnSpeedTol);
                Add(r, AssessmentCatalog.E6, id, v,
                    $"до {(double.IsNaN(e.FreeTime) ? "конца сближения" : $"t = {ScenarioText.Clock(free)}")}: курс ±{AssessmentResult.Num(dev)}°, скорость −{AssessmentResult.Num(100 * drop)} % (доп. {AssessmentResult.Num(S.standOnHeadingTolDeg)}° / {AssessmentResult.Num(100 * S.standOnSpeedTol)} %)");
            }
            // E7 — при пересечении не влево на судно слева (17 в)
            if (sit == EncounterSituation.CrossingStandOn)
            {
                if (helm == null) Skip(r, AssessmentCatalog.E7, id, "перекладок нет");
                else { double rd = FinalRudder(helm); Add(r, AssessmentCatalog.E7, id, rd > 0 ? 1 : 0, $"перекладка {Side(rd)} в t = {ScenarioText.Clock(helm.Time)}"); }
            }
        }

        // E8 — наименьшая дистанция
        Add(r, AssessmentCatalog.E8, id, e.MinDistanceM / reqM,
                collided ? "столкновение" : $"{AssessmentResult.Num(e.MinDistanceM)} м ({AssessmentResult.Num(e.MinDistanceM / Nm, 2)} мили) при CPA_доп {AssessmentResult.Num(reqM)} м");

        // R8b — встречное в узкости: у правой стороны в момент наименьшей дистанции
        if (sit == EncounterSituation.HeadOn && e.MinOwnIndex >= 0 && e.MinOwnIndex < own.Count)
        {
            TrackSample m = own[e.MinOwnIndex];
            if (NarrowLeg(m.Leg) && !double.IsNaN(m.XteM) && m.XteLimitM > 0)
                Add(r, AssessmentCatalog.R8b, id, m.XteM / m.XteLimitM,
                    $"XTE {AssessmentResult.Num(m.XteM)} м {(m.XteM >= 0 ? "правее" : "левее")} оси при доп. {AssessmentResult.Num(m.XteLimitM)} м");
        }
    }

    private void EvaluateRoute(AssessmentResult r, bool final, bool aborted)
    {
        if (!RouteValid) return;
        double maxRatio = 0, sumSq = 0, outside = 0;
        int n = 0, narrow = 0, narrowRight = 0;
        double maxXte = 0;
        double excluded = 0;
        for (int k = 0; k < own.Count; k++)
        {
            TrackSample s = own[k];
            if (double.IsNaN(s.XteM) || !(s.XteLimitM > 0)) continue;
            if (ExcludedFromRoute(s.Time)) { if (k + 1 < own.Count) excluded += own[k + 1].Time - s.Time; continue; }
            double ratio = s.XteM / s.XteLimitM;
            n++;
            sumSq += ratio * ratio;
            if (Math.Abs(ratio) > maxRatio) { maxRatio = Math.Abs(ratio); maxXte = s.XteM; }
            if (Math.Abs(s.XteM) > s.XteLimitM && k + 1 < own.Count) outside += own[k + 1].Time - s.Time;
            if (NarrowLeg(s.Leg)) { narrow++; if (s.XteM >= 0) narrowRight++; }
        }
        if (n > 0)
        {
            string excl = excluded > 0 ? $"; манёвр расхождения ({AssessmentResult.Num(excluded)} с) не учитывается" : "";
            Add(r, AssessmentCatalog.R1, "", maxRatio, $"max |XTE| {AssessmentResult.Num(Math.Abs(maxXte))} м ({AssessmentResult.Num(maxRatio, 2)} доп.){excl}");
            double rms = Math.Sqrt(sumSq / n);
            Add(r, AssessmentCatalog.R2, "", rms, $"{AssessmentResult.Num(rms, 2)} доп.");
            Add(r, AssessmentCatalog.R3, "", outside, $"{AssessmentResult.Num(outside)} с вне коридора");
        }
        if (narrow > 0)
        {
            double share = (double)narrowRight / narrow;
            Add(r, AssessmentCatalog.R8a, "", share, $"{AssessmentResult.Num(100 * share)} % времени в узкости правее оси");
        }

        // R4, R5 — только по завершении (кроме досрочного)
        int total = Route.Count - 1;
        if (!final) { }
        else if (aborted)
        {
            Skip(r, AssessmentCatalog.R4, "", "завершено досрочно");
            if (Context.TimeLimitS > 0) Skip(r, AssessmentCatalog.R5, "", "завершено досрочно");
        }
        else
        {
            Add(r, AssessmentCatalog.R4, "", total > 0 ? (double)passedWaypoints.Count / total : 1.0, $"пройдено {passedWaypoints.Count} из {total}");
            if (Context.TimeLimitS > 0)
            {
                if (EndReason == ScenarioEndReason.Arrived)
                    Add(r, AssessmentCatalog.R5, "", EndTime <= Context.TimeLimitS + 1e-6 ? 1 : 0, $"{ScenarioText.Clock(EndTime)} при лимите {ScenarioText.Clock(Context.TimeLimitS)}");
                else if (EndReason == ScenarioEndReason.TimeLimit)
                    Add(r, AssessmentCatalog.R5, "", 0, $"лимит {ScenarioText.Clock(Context.TimeLimitS)} истёк");
                else Skip(r, AssessmentCatalog.R5, "", "маршрут не пройден");
            }
        }

        // R6, R7 — по точкам поворота
        for (int i = 1; i + 1 < Route.Count; i++)
        {
            double turn = TurnAt(i);
            if (turn == 0.0) continue;
            string name = Route[i].name;
            Zone(i, out double before, out double after);
            bool entered = false, left = false;
            double maxZone = 0;
            double maxZoneXte = 0;
            double xteRef = double.NaN;   // боковое смещение на входе в зону (держался правее оси — не вынос)
            for (int k = 0; k < own.Count; k++)
            {
                TrackSample s = own[k];
                if (s.Leg > i) { left = entered; break; }
                if (TurnZone(s.Position, s.Leg, out _) != i) { if (entered && s.Leg == i) { left = true; break; } continue; }
                entered = true;
                if (double.IsNaN(s.XteM) || !(s.XteLimitM > 0)) continue;
                if (double.IsNaN(xteRef)) xteRef = s.XteM;
                double ratio = Math.Abs(s.XteM - xteRef) / s.XteLimitM;
                if (ratio > maxZone) { maxZone = ratio; maxZoneXte = s.XteM - xteRef; }
            }
            bool done = left || (final && passedWaypoints.Contains(i));
            if (!entered) { if (final) Skip(r, AssessmentCatalog.R6, name, "зона поворота не пройдена"); }
            else if (!done) { if (final) Skip(r, AssessmentCatalog.R6, name, "поворот не завершён"); }
            else Add(r, AssessmentCatalog.R6, name, maxZone, $"вынос {AssessmentResult.Num(Math.Abs(maxZoneXte))} м {(maxZoneXte * TurnAt(i) < 0 ? "наружу" : "внутрь")} поворота от линии входа (XTE на входе {AssessmentResult.Num(xteRef)} м; {AssessmentResult.Num(maxZone, 2)} доп.)");

            double dRec = RecommendedWheelOver(i);
            if (!done) continue;
            if (double.IsNaN(dRec)) { Skip(r, AssessmentCatalog.R7, name, "нет параметров судна"); continue; }
            ControlAction wo = null;
            double toGo = double.NaN;
            foreach (ControlAction a in actions)
                if (RouteTurnOf(a, out double g) == i) { wo = a; toGo = g; break; }
            if (wo == null) Skip(r, AssessmentCatalog.R7, name, "перекладки на поворот не найдено");
            else
                Add(r, AssessmentCatalog.R7, name, Math.Abs(toGo - dRec) / dRec,
                    $"перекладка за {AssessmentResult.Num(toGo)} м до точки, рекомендуется {AssessmentResult.Num(dRec)} м ({(Math.Abs(toGo - dRec) <= 0.1 * dRec ? "в рекомендуемой точке" : toGo > dRec ? "раньше" : "позже")})");
        }
    }
}
