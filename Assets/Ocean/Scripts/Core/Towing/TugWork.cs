// ============================================================
// TugWork.cs — Упражнение на своём буксире: судно-клиент, лоцман, задания (запрос 19.4б-1, Core)
//
// Сценарий 19-03 «Буксир прижимает судно к причалу при отжимном ветре»: обучающийся на своём буксире
// (тип судна «буксир», джойстик 19.4а) подходит к борту судна-клиента, упирается носовым кранцем у миделя
// и прижимает клиента к причалу против ветра, держит, пока клиент заводит концы, и отходит.
//
// Клиент (ClientShip) — модель 3 СС (ShipDynamicsModel, Model45m) + MooringSystem (кранцы, кромка, концы,
// подруливающее, 19.2а) + автоматика «лоцман» (Pilot):
//   • курс — по кромке причала: подруливающее (PD: cmd = −(e_ψ/EψFull + r/RFull), |cmd| ≤ 1) и руль (HeadingAutopilot);
//   • место вдоль причала — машиной: SF = −k_м·e_t − k_v·u (|SF| ≤ SfMax) — e_t — мидель от заданной точки вдоль кромки;
//   • поперёк кромки клиент не управляется: его сносит ветер, к причалу ведёт только буксир;
//   • концы — носовой и кормовой продольные и прижимные — подаёт, когда наибольший зазор борта до кромки ≤ linesGapM,
//     и выбирает «понемногу» (как BerthingSimulation 19.2б-3в: держать край, идущий к причалу быстрее 0,06 уз,
//     снова выбирать медленнее 0,03 уз); «концы закреплены» — все четыре поданы, зазор ≤ atFendersGapM,
//     ход клиента ≤ securedSpeedKnots в течение securedHoldS — тогда лебёдки на тормоз.
// Свой буксир — модель судна в тренажёре (ShipNavigation) или в прогоне; TugWorkSystem.Apply вызывается до его
// шага: ветер на клиента (и на буксир — только в прогоне: в сцене его даёт TowingController), лоцман, причал клиента,
// контакт буксир — клиент (HullToHull: кранец носа и корпус к корпусу), шаг клиента.
//
// Команды лоцмана (PilotOrderKind, событие PilotOrder): «к правому борту на миделе, упирайтесь» (на старте) →
// «толкайте к причалу» (после касания в зоне) → «держите, заводим концы» (клиент у кранцев) → «отходите»
// (концы закреплены). Задания (TugWorkStage, событие TugWorkStage), отмечаются по порядку:
//   1. подход — касание кранцем дальнего от причала борта клиента в ± contactZoneM от миделя;
//   2. прижать — клиент у кранцев: наибольший зазор ≤ atFendersGapM;
//   3. держать — до «концы закреплены»;
//   4. отойти — нос буксира в ≥ clearDistanceM от корпуса клиента после «отходите».
// Касания (скорость по нормали, угол к борту, место), касания корпусом, доля времени «держать» с упором —
// пишутся для оценки (19.4в). Пороги — предварительные (ThesisNotes 19.29).
// ============================================================
using System;
using System.Collections.Generic;

/// <summary>Задание упражнения на буксире (поле ScenarioDefinition.tugWork).</summary>
[Serializable]
public class TugWorkDefinition
{
    public bool enabled;
    /// <summary>Номер причала в WaterArea.berths.</summary>
    public int berthIndex;
    /// <summary>Клиент на старте: борт до кромки, м; мидель от середины причала вдоль кромки, м (> 0 — к концу B).</summary>
    public float clientGapM = 30f, clientAlongM = 0f;
    /// <summary>Свой буксир на старте: нос в … м от борта клиента напротив миделя, носом к нему, без хода.</summary>
    public float tugDistanceM = 150f;
    /// <summary>Подход: касание в ± … м от миделя клиента.</summary>
    public float contactZoneM = 10f;
    /// <summary>Прижать: клиент у кранцев — наибольший зазор борта до кромки ≤ …, м.</summary>
    public float atFendersGapM = 1f;
    /// <summary>Лоцман подаёт концы при зазоре ≤ …, м.</summary>
    public float linesGapM = 1f;
    /// <summary>Концы закреплены: ход клиента ≤ … уз в течение … с.</summary>
    public float securedSpeedKnots = 0.1f, securedHoldS = 30f;
    /// <summary>Отойти: нос буксира в ≥ … м от корпуса клиента.</summary>
    public float clearDistanceM = 20f;
    /// <summary>Контрольное время задания, с.</summary>
    public float targetTimeS = 480f;

    public BerthArea BerthIn(WaterArea area) =>
        area != null && area.berths != null && berthIndex >= 0 && berthIndex < area.berths.Count ? area.berths[berthIndex] : null;

    public TugWorkDefinition Clone() => (TugWorkDefinition)MemberwiseClone();
}

public enum PilotOrderKind { None, Approach, Push, Hold, LetGo }

public enum TugWorkStage { None, Approach, Push, Hold, Clear }

public static class TugWorkText
{
    public static string Order(PilotOrderKind k)
    {
        switch (k)
        {
            case PilotOrderKind.Approach: return "Буксир, к правому борту на миделе, упирайтесь";
            case PilotOrderKind.Push: return "Толкайте к причалу";
            case PilotOrderKind.Hold: return "Держите, заводим концы";
            case PilotOrderKind.LetGo: return "Концы закреплены, отходите";
            default: return "—";
        }
    }

    /// <summary>Текст команды для борта (+1 — ПБ клиента, −1 — ЛБ).</summary>
    public static string Order(PilotOrderKind k, int side) =>
        k == PilotOrderKind.Approach ? $"Буксир, к {(side > 0 ? "правому" : "левому")} борту на миделе, упирайтесь" : Order(k);

    public static string Stage(TugWorkStage s)
    {
        switch (s)
        {
            case TugWorkStage.Approach: return "Подойти к борту у миделя";
            case TugWorkStage.Push: return "Прижать судно к причалу";
            case TugWorkStage.Hold: return "Держать, пока заводят концы";
            case TugWorkStage.Clear: return "Отойти от борта";
            default: return "—";
        }
    }

    public static string Condition(TugWorkDefinition d, TugWorkStage s)
    {
        switch (s)
        {
            case TugWorkStage.Approach: return $"касание кранцем в ± {ScenarioText.Num(d.contactZoneM)} м от миделя, медленно и носом поперёк борта";
            case TugWorkStage.Push: return $"судно у кранцев: зазор ≤ {ScenarioText.Num(d.atFendersGapM)} м";
            case TugWorkStage.Hold: return $"упор не снимать до команды «отходите» (ход судна ≤ {ScenarioText.Num(d.securedSpeedKnots)} уз {ScenarioText.Num(d.securedHoldS)} с)";
            case TugWorkStage.Clear: return $"нос буксира в {ScenarioText.Num(d.clearDistanceM)} м от борта, без касания корпусом";
            default: return "";
        }
    }
}

/// <summary>Судно-клиент: модель 3 СС, причал, концы, подруливающее и автоматика «лоцман».</summary>
public sealed class ClientShip : ITrackedTarget
{
    public const string Id = "CLIENT";

    public readonly ShipParameters Parameters;
    public readonly ShipDynamicsModel Model;
    public readonly MooringSystem Mooring;
    public readonly ShipWindArea WindArea;
    private readonly HeadingAutopilot autopilot = new HeadingAutopilot();

    // Лоцман (допущения, подобраны прогоном — ThesisNotes 19.29)
    public double EpsiFullDeg = 3.0, RFullDegS = 0.3;      // подруливающее: полная тяга при ошибке курса / скорости поворота
    public double KAlong = 0.004, KSpeed = 0.15, SfMax = 0.1;   // машина: SF на м ошибки места, на м/с хода; предел
    public double HoldAboveKn = 0.06, ResumeBelowKn = 0.03;     // выбирать концы «понемногу»

    /// <summary>Заданный курс (по кромке), °; заданное место миделя вдоль кромки, м.</summary>
    public double CourseDeg, HoldAlongM;
    public bool LinesGiven { get; private set; }
    public bool Secured { get; private set; }
    /// <summary>Сколько секунд подряд выполнено условие «концы закреплены».</summary>
    public double SecuredHold { get; private set; }

    private static readonly int[] Lines = { (int)MooringLineKind.HeadLine, (int)MooringLineKind.ForwardBreast, (int)MooringLineKind.AftBreast, (int)MooringLineKind.SternLine };
    private static readonly int[] BowLines = { (int)MooringLineKind.HeadLine, (int)MooringLineKind.ForwardBreast };
    private static readonly int[] SternLines = { (int)MooringLineKind.AftBreast, (int)MooringLineKind.SternLine };
    private readonly bool[] haulEnd = { false, false };
    private double lastLineTry = -1e9, haulCheck;

    public ClientShip(ShipParameters p, BerthArea berth)
    {
        Parameters = p;
        Model = new ShipDynamicsModel(p) { MotionOverride = MotionModel.Manoeuvring3Dof, SpeedFactor = 0.0 };
        Mooring = new MooringSystem(berth, p) { FairleadHeightM = 4.5 };
        WindArea = ShipWindArea.Estimate(p);
    }

    public BerthArea Berth => Mooring.Berth;

    /// <summary>Поставить лагом к причалу: борт в gap м от кромки, мидель в along м от начала кромки, без хода.</summary>
    public void Place(double along, double gap)
    {
        WorldPosition p = Berth.AlongsidePosition(along, gap, Parameters.beam, out double hdg);
        Model.Teleport(p.East, p.North, hdg * Math.PI / 180.0, 0.0, 0.0);
        Model.SpeedFactor = 0.0;
        Mooring.ResetState();
        CourseDeg = hdg;
        HoldAlongM = along;
        LinesGiven = Secured = false;
        SecuredHold = 0.0;
        haulEnd[0] = haulEnd[1] = false;
        lastLineTry = -1e9;
    }

    /// <summary>Борт к причалу: +1 — ПБ, −1 — ЛБ.</summary>
    public int SideToBerth => Mooring.SideToBerth(Model);

    public BerthReadout Readout => BerthReadout.Compute(Berth, Parameters, Model);

    /// <summary>Наибольший зазор борта до кромки (нос, корма), м.</summary>
    public double GapM { get { BerthReadout r = Readout; return r.Valid ? Math.Max(r.BowGapM, r.SternGapM) : double.NaN; } }

    public double SpeedOverGroundKn => Math.Sqrt(Model.VelocityEast * Model.VelocityEast + Model.VelocityNorth * Model.VelocityNorth) / ShipParameters.KnotsToMs;

    /// <summary>Лоцман на шаг (до Mooring.Apply и Model.Step): подруливающее, руль, машина, концы.</summary>
    public void Pilot(TugWorkDefinition d, double dt, double time)
    {
        // Курс — подруливающим и рулём
        double e = Math.IEEERemainder(Model.Psi * 180.0 / Math.PI - CourseDeg, 360.0);
        double r = Model.R * 180.0 / Math.PI;
        Mooring.Thruster.Command = Clamp(-(e / EpsiFullDeg + r / RFullDegS), 1.0);
        Model.RudderCommandDeg = autopilot.Rudder(Model, CourseDeg);
        // Место вдоль причала — машиной
        Berth.ToLocal(new WorldPosition(Model.East, Model.North), out double along, out _);
        double sign = Math.Cos(Model.Psi - Berth.BearingDeg * Math.PI / 180.0) >= 0.0 ? 1.0 : -1.0;   // нос к концу B — «вперёд» = +t
        Model.SpeedFactor = Clamp(-sign * KAlong * (along - HoldAlongM) - KSpeed * Model.U, SfMax);

        // Концы
        double gap = GapM;
        if (!LinesGiven && !double.IsNaN(gap) && gap <= d.linesGapM && time - lastLineTry >= 1.0)
        {
            lastLineTry = time;
            bool all = true;
            foreach (int i in Lines)
                if (Mooring.Lines[i].State != MooringLineState.Fast) all &= Mooring.Order(i, LineOrder.Give, Model, time, out _);
            if (all)
            {
                LinesGiven = true;
                foreach (int i in Lines) Mooring.Order(i, LineOrder.HaulIn, Model, time, out _);
                haulEnd[0] = haulEnd[1] = true;
            }
        }
        if (LinesGiven && !Secured)
        {
            haulCheck += dt;
            if (haulCheck >= 0.2 - 1e-9)
            {
                haulCheck = 0.0;
                BerthReadout br = Readout;
                for (int end = 0; end < 2; end++)
                {
                    double v = end == 0 ? br.BowSpeedKn : br.SternSpeedKn;
                    bool want = Mooring.InContact || (haulEnd[end] ? v < HoldAboveKn : v < ResumeBelowKn);
                    if (want == haulEnd[end]) continue;
                    foreach (int i in end == 0 ? BowLines : SternLines)
                        if (Mooring.Lines[i].State == MooringLineState.Fast) Mooring.Order(i, want ? LineOrder.HaulIn : LineOrder.Hold, Model, time, out _);
                    haulEnd[end] = want;
                }
            }
            bool allFast = true;
            foreach (int i in Lines) allFast &= Mooring.Lines[i].State == MooringLineState.Fast;
            bool ok = allFast && gap <= d.atFendersGapM && SpeedOverGroundKn <= d.securedSpeedKnots;
            SecuredHold = ok ? SecuredHold + dt : 0.0;
            if (SecuredHold >= d.securedHoldS - 1e-9)
            {
                Secured = true;
                foreach (int i in Lines) Mooring.Order(i, LineOrder.Hold, Model, time, out _);
            }
        }
    }

    private static double Clamp(double v, double max) => v < -max ? -max : v > max ? max : v;

    // ITrackedTarget — цель «CLIENT» (targets.csv, карты); оценка C2 её отбрасывает
    public string TargetId => Id;
    public WorldPosition Position => new WorldPosition(Model.East, Model.North);
    public double HeadingDeg { get { double h = Model.Psi * 180.0 / Math.PI % 360.0; return h < 0 ? h + 360.0 : h; } }
    public double SpeedKnots => SpeedOverGroundKn;
}

/// <summary>Свой буксир + клиент + причал клиента в одном шаге (вызывать до шага модели своего буксира).</summary>
public sealed class TugWorkSystem
{
    public readonly TugWorkDefinition Task;
    public readonly ClientShip Client;
    public readonly TugSpec OwnSpec;
    public readonly HullToHull Contact = new HullToHull();
    public Wind Wind;
    /// <summary>Ветер и на свой буксир (прогон); в сцене его даёт TowingController.</summary>
    public bool ApplyOwnWind;
    public ShipWindArea OwnWind;
    /// <summary>События упражнения за шаг (PilotOrder, TugWorkStage, касания, события причала клиента) — забирает вызывающий.</summary>
    public readonly List<ExerciseEvent> Events = new List<ExerciseEvent>();

    public PilotOrderKind Order { get; private set; }
    public double OrderTime { get; private set; } = double.NaN;
    /// <summary>Борт клиента, у которого работает буксир (дальний от причала): +1 — ПБ, −1 — ЛБ.</summary>
    public int WorkSide { get; private set; }
    public bool Done(TugWorkStage s) => (int)s >= 1 && (int)s <= StageCount && done[(int)s];
    public double DoneAt(TugWorkStage s) => (int)s >= 1 && (int)s <= StageCount ? doneAt[(int)s] : double.NaN;
    public int DoneCount { get { int n = 0; for (int i = 1; i <= StageCount; i++) if (done[i]) n++; return n; } }
    public bool Finished => done[StageCount];
    public const int StageCount = 4;

    // Для оценки (19.4в)
    /// <summary>Касания кранцем: время, скорость по нормали (уз), отклонение от перпендикуляра (°), место от миделя (м), в зоне.</summary>
    public readonly List<(double t, double kn, double angleDeg, double x, bool inZone)> Contacts = new List<(double, double, double, double, bool)>();
    /// <summary>Касания корпусом: время, наибольшая скорость сближения (уз).</summary>
    public readonly List<(double t, double kn)> HullContacts = new List<(double, double)>();
    /// <summary>Время этапа «держать» и из него — с упором кранцем в борт, с.</summary>
    public double HoldTime { get; private set; }
    public double HoldPushTime { get; private set; }

    private readonly bool[] done = new bool[StageCount + 1];
    private readonly double[] doneAt = new double[StageCount + 1];

    public TugWorkSystem(TugWorkDefinition task, ShipParameters client, BerthArea berth, TugSpec ownSpec)
    {
        Task = task;
        Client = new ClientShip(client, berth);
        OwnSpec = ownSpec;
        Reset();
    }

    public void Reset()
    {
        for (int i = 0; i <= StageCount; i++) { done[i] = false; doneAt[i] = double.NaN; }
        Order = PilotOrderKind.None;
        OrderTime = double.NaN;
        Contacts.Clear();
        HullContacts.Clear();
        HoldTime = HoldPushTime = 0.0;
        Contact.Reset();
        Events.Clear();
    }

    /// <summary>Начальные условия: клиент лагом к причалу; свой буксир — напротив миделя дальнего борта, носом к нему, без хода.</summary>
    public void Place(ShipDynamicsModel own)
    {
        Reset();
        BerthArea b = Client.Berth;
        Client.Place(0.5 * b.Length + Task.clientAlongM, Task.clientGapM);
        WorkSide = -Client.SideToBerth;
        TugStart(Client.Model, Client.Parameters, OwnSpec, WorkSide, Task.tugDistanceM, out double e, out double n, out double psi);
        own.Teleport(e, n, psi, 0.0, 0.0);
    }

    /// <summary>Место своего буксира: нос в distance м от борта side клиента напротив миделя, носом к борту.</summary>
    public static void TugStart(ShipDynamicsModel client, ShipParameters cp, TugSpec spec, int side, double distance,
                                out double east, out double north, out double psi)
    {
        double s = Math.Sin(client.Psi), c = Math.Cos(client.Psi);
        double nE = side * c, nN = -side * s;   // нормаль борта наружу
        double r = MooringHull.HalfBeamAt(0.0, cp) + distance + spec.BowX;
        east = client.East + nE * r;
        north = client.North + nN * r;
        psi = Math.Atan2(-nE, -nN);
        if (psi < 0) psi += 2.0 * Math.PI;
    }

    /// <summary>Точка касания у миделя на рабочем борту клиента (мир).</summary>
    public WorldPosition ContactPoint(double x = 0.0) =>
        MooringHull.ToWorld(Client.Model.East, Client.Model.North, Client.Model.Psi, x, WorkSide * MooringHull.HalfBeamAt(x, Client.Parameters));

    /// <summary>Нос своего буксира (кранец) в мире.</summary>
    public WorldPosition Bow(ShipDynamicsModel own) => MooringHull.ToWorld(own.East, own.North, own.Psi, OwnSpec.BowX, 0.0);

    /// <summary>Расстояние от носа буксира до корпуса клиента (контур MooringHull), м (0 — внутри).</summary>
    public double BowDistance(ShipDynamicsModel own)
    {
        WorldPosition b = Bow(own);
        TowingSystem.ShipAxes(Client.Model, b, out double x, out double y);
        double halfL = 0.5 * Client.Parameters.length;
        double xc = Math.Max(-halfL, Math.Min(halfL, x));
        double dx = Math.Abs(x) - halfL, dy = Math.Abs(y) - MooringHull.HalfBeamAt(xc, Client.Parameters);
        if (dx <= 0.0) return Math.Max(0.0, dy);
        return Math.Sqrt(dx * dx + Math.Max(0.0, dy) * Math.Max(0.0, dy));
    }

    /// <summary>Шаг: ветер, лоцман, причал клиента, контакт буксир — клиент, шаг клиента; задания и команды.</summary>
    public void Apply(ShipDynamicsModel own, double dt, double time)
    {
        ShipDynamicsModel cm = Client.Model;
        if (Order == PilotOrderKind.None) Issue(PilotOrderKind.Approach, time);

        if (!Wind.Calm)
        {
            WindLoad.Apply(cm, Client.WindArea, Wind);
            if (ApplyOwnWind) WindLoad.Apply(own, OwnWind ?? (OwnWind = ShipWindArea.Estimate(own.Parameters)), Wind);
        }

        // Контакт буксир — клиент (силы в обе модели до их шагов)
        WorldPosition bow = Bow(own);
        if (Contact.Fender(cm, Client.Parameters, own, OwnSpec, bow, dt))
        {
            bool inZone = Contact.ContactSide == WorkSide && Math.Abs(Contact.ContactX) <= Task.contactZoneM;
            double kn = Contact.ContactClosingMs / ShipParameters.KnotsToMs;
            Contacts.Add((time, kn, Contact.ContactAngleDeg, Contact.ContactX, inZone));
            Events.Add(new ExerciseEvent(ExerciseEventType.TugWorkContact)
            {
                Time = time, Value = Math.Round(kn, 3), Value2 = Math.Round(Contact.ContactAngleDeg, 1), Index = (int)Math.Round(Contact.ContactX),
                Subject = (Contact.ContactSide > 0 ? "правый борт" : "левый борт") + (inZone ? "" : " (вне зоны)")
            });
            if (inZone && !done[(int)TugWorkStage.Approach]) Mark(TugWorkStage.Approach, time, kn);
        }
        Contact.Hulls(cm, Client.Parameters, own, OwnSpec.BowX);
        if (Contact.HullContactStarted)
        {
            double kn = Contact.HullClosingMs / ShipParameters.KnotsToMs;
            HullContacts.Add((time, kn));
            Events.Add(new ExerciseEvent(ExerciseEventType.TugWorkHullContact) { Time = time, Value = Math.Round(kn, 3) });
        }

        // Клиент: лоцман, причал, шаг
        Client.Pilot(Task, dt, time);
        Client.Mooring.Apply(cm, dt, time);
        foreach (MooringEvent me in Client.Mooring.Events)
        {
            ExerciseEvent e = MooringEventBus.ToExerciseEvent(me);
            e.Time = time;
            Events.Add(e);
        }
        Client.Mooring.Events.Clear();
        cm.Step(dt);

        // Задания по порядку
        if (done[(int)TugWorkStage.Approach] && !done[(int)TugWorkStage.Hold] && done[(int)TugWorkStage.Push])
        {
            HoldTime += dt;
            if (Contact.FenderForce > 0.0) HoldPushTime += dt;
        }
        double gap = Client.GapM;
        if (done[(int)TugWorkStage.Approach] && !done[(int)TugWorkStage.Push] && !double.IsNaN(gap) && gap <= Task.atFendersGapM)
            Mark(TugWorkStage.Push, time, gap);
        if (done[(int)TugWorkStage.Push] && !done[(int)TugWorkStage.Hold] && Client.Secured)
            Mark(TugWorkStage.Hold, time, Client.SpeedOverGroundKn);
        if (done[(int)TugWorkStage.Hold] && !done[(int)TugWorkStage.Clear])
        {
            double d = BowDistance(own);
            if (d >= Task.clearDistanceM) Mark(TugWorkStage.Clear, time, d);
        }
    }

    private void Mark(TugWorkStage s, double time, double value)
    {
        done[(int)s] = true;
        doneAt[(int)s] = time;
        Events.Add(new ExerciseEvent(ExerciseEventType.TugWorkStage) { Time = time, Index = (int)s, Subject = TugWorkText.Stage(s), Value = Math.Round(value, 3) });
        switch (s)
        {
            case TugWorkStage.Approach: Issue(PilotOrderKind.Push, time); break;
            case TugWorkStage.Push: Issue(PilotOrderKind.Hold, time); break;
            case TugWorkStage.Hold: Issue(PilotOrderKind.LetGo, time); break;
        }
    }

    private void Issue(PilotOrderKind k, double time)
    {
        Order = k;
        OrderTime = time;
        Events.Add(new ExerciseEvent(ExerciseEventType.PilotOrder) { Time = time, Index = (int)k, Subject = TugWorkText.Order(k, WorkSide == 0 ? 1 : WorkSide) });
    }
}
