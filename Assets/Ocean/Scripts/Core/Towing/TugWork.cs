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
    /// <summary>
    /// 19.4г: «случайное событие» у причала — при зазоре клиента в [eventGapMinM; eventGapMaxM] м (случайно) один конец
    /// (нос или корма — случайно) отходит на N ∈ [eventAngleMinDeg; eventAngleMaxDeg]°, ближний ложится и его крепят (стопор);
    /// буксир переходит к отошедшему концу (зона ± contactZoneM около ± endZoneX·L от миделя) и дожимает его.
    /// </summary>
    public bool angleEvent;
    public float eventGapMinM = 4f, eventGapMaxM = 7f, eventAngleMinDeg = 4f, eventAngleMaxDeg = 8f, endZoneX = 0.3f;

    public BerthArea BerthIn(WaterArea area) =>
        area != null && area.berths != null && berthIndex >= 0 && berthIndex < area.berths.Count ? area.berths[berthIndex] : null;

    public TugWorkDefinition Clone() => (TugWorkDefinition)MemberwiseClone();
}

/// <summary>
/// Команды лоцмана: задания (Approach … LetGo, 19.4б-1) и 19.4б-3 — упор буксира (PushStep: ступень 0…4 рукоятки,
/// StopPush), обстановка (Info: зазор и скорость к причалу), «быстро идёт, сбавь» (Slower). Новые — только в конец.
/// </summary>
public enum PilotOrderKind { None, Approach, Push, Hold, LetGo, PushStep, StopPush, Info, Slower, ToEnd }

/// <summary>19.4г: ход «случайного события» (событие TugWorkEvent, Index): 1 — конец отходит, буксиру «к носу/корме»; 2 — буксир упёрся в зоне конца; 3 — ближний конец у кранцев на стопоре; 4 — судно всё у кранцев (событие закончено).</summary>
public enum TugEventPhase { None, ToEnd, AtEnd, Pinned, Done }

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

    /// <summary>19.4б-3: команда упором: «Толкай малым» / «Держи средним» (hold — этап «держать»), 0 — «Стоп толкать».</summary>
    public static string Power(int step, bool hold)
    {
        if (step <= 0) return "Стоп толкать";
        string[] by = { "", "самым малым", "малым", "средним", "полным" };
        return $"{(hold ? "Держи" : "Толкай")} {by[Math.Min(4, step)]}";
    }

    /// <summary>19.4б-3: обстановка: «До причала 20 м, идёт 0,30 уз» (скорость к причалу; ≤ 0 — «стоит» / «отходит»).</summary>
    public static string Info(double gapM, double towardKn) =>
        $"До причала {ScenarioText.Num(gapM)} м, " + (towardKn > 0.02 ? $"идёт {ScenarioText.Num(towardKn, 2)} уз"
                                                   : towardKn < -0.02 ? $"отходит {ScenarioText.Num(-towardKn, 2)} уз" : "стоит");

    public const string SlowerText = "Быстро идёт, сбавь";

    /// <summary>19.4г: конец судна: +1 — нос, −1 — корма.</summary>
    public static string End(int end, bool genitive = false) => end > 0 ? (genitive ? "носу" : "нос") : (genitive ? "корме" : "корма");

    /// <summary>19.4г: команда «к концу»: «Нос отходит, до 6°. Корма ложится. Буксир — к носу».</summary>
    public static string ToEnd(int offEnd, double angleDeg) =>
        $"{(offEnd > 0 ? "Нос" : "Корма")} отходит, до {ScenarioText.Num(angleDeg)}°. {(offEnd > 0 ? "Корма" : "Нос")} ложится. Буксир — к {End(offEnd, true)}";

    /// <summary>19.4г: ближний конец у кранцев на стопоре.</summary>
    public static string Pinned(int pinnedEnd) =>
        pinnedEnd > 0 ? "Нос у кранцев, носовые на стопоре" : "Корма у кранцев, кормовые на стопоре";

    /// <summary>19.4г: обстановка по отошедшему концу: «Нос: 12 м, угол 5°, идёт 0,10 уз».</summary>
    public static string EndInfo(int offEnd, double gapM, double angleDeg, double towardKn) =>
        $"{(offEnd > 0 ? "Нос" : "Корма")}: {ScenarioText.Num(gapM)} м, угол {ScenarioText.Num(angleDeg, 1)}°, " +
        (towardKn > 0.02 ? $"идёт {ScenarioText.Num(towardKn, 2)} уз" : towardKn < -0.02 ? $"отходит {ScenarioText.Num(-towardKn, 2)} уз" : "стоит");

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
            case TugWorkStage.Hold: return $"упор не снимать до команды «отходите» (ход судна ≤ {ScenarioText.Num(d.securedSpeedKnots, 1)} уз {ScenarioText.Num(d.securedHoldS)} с)";
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

    // ─────────── 19.4г: случайное событие ───────────
    /// <summary>Курс по кромке (до события), °.</summary>
    public double BaseCourseDeg { get; private set; }
    /// <summary>Лоцман доворачивает судно на заданный угол (событие); после — курс свободен: руль прямо, подруливающее стоп.</summary>
    public bool Swinging { get; private set; }
    public bool HeadingFree { get; private set; }
    /// <summary>Скорость доворота при событии, °/с (ближний конец идёт к кранцам не быстрее ≈ 0,1–0,2 уз).</summary>
    public double SwingRateDegS = 0.05;
    /// <summary>Ближний конец, который крепят (стопор): +1 — нос, −1 — корма, 0 — нет; PinFast — у кранцев, концы на тормозе.</summary>
    public int PinEnd { get; private set; }
    public bool PinFast { get; private set; }
    private double swingTarget, swingSign = 1.0, pinCheck;
    private bool pinGiven, pinHaul;

    private static readonly int[] Lines = { (int)MooringLineKind.HeadLine, (int)MooringLineKind.ForwardBreast, (int)MooringLineKind.AftBreast, (int)MooringLineKind.SternLine };
    private static readonly int[] BowLines = { (int)MooringLineKind.HeadLine, (int)MooringLineKind.ForwardBreast };
    private static readonly int[] SternLines = { (int)MooringLineKind.AftBreast, (int)MooringLineKind.SternLine };
    private readonly bool[] haulEnd = { false, false };
    private double lastLineTry = -1e9, haulCheck;

    public ClientShip(ShipParameters p, BerthArea berth)
    {
        Parameters = p;
        Model = new ShipDynamicsModel(p) { MotionOverride = MotionModel.Manoeuvring3Dof, SpeedFactor = 0.0 };
        Mooring = new MooringSystem(berth, p) { FairleadHeightM = p.length > 100.0 ? 7.0 : 4.5 };   // 19.4б-3: у Mariner борт выше
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
        BaseCourseDeg = hdg;
        Swinging = HeadingFree = PinFast = pinGiven = pinHaul = false;
        PinEnd = 0;
        pinCheck = 0.0;
        swingSign = 1.0;
    }

    /// <summary>Борт к причалу: +1 — ПБ, −1 — ЛБ.</summary>
    public int SideToBerth => Mooring.SideToBerth(Model);

    public BerthReadout Readout => BerthReadout.Compute(Berth, Parameters, Model);

    /// <summary>
    /// Наибольший зазор борта до кранцев (нос, корма), м: до кромки минус диаметр кранца. 19.4б-3: у причала №2 (Mariner)
    /// кранцы ⌀ 1,15 м — «у кранцев ≤ 1 м» до кромки не достигалось бы (было — до кромки; у причала №1 кранцы ⌀ 0,37 м).
    /// </summary>
    public double GapM
    {
        get
        {
            BerthReadout r = Readout;
            return r.Valid ? Math.Max(r.BowGapM, r.SternGapM) - Berth.fenderOuterDiameterM : double.NaN;
        }
    }

    public double SpeedOverGroundKn => Math.Sqrt(Model.VelocityEast * Model.VelocityEast + Model.VelocityNorth * Model.VelocityNorth) / ShipParameters.KnotsToMs;

    /// <summary>19.4г: зазор конца до кранцев (+1 — нос, −1 — корма), м; скорость конца к причалу, уз.</summary>
    public double EndGapM(int end)
    {
        BerthReadout r = Readout;
        return r.Valid ? (end > 0 ? r.BowGapM : r.SternGapM) - Berth.fenderOuterDiameterM : double.NaN;
    }

    public double EndSpeedKn(int end)
    {
        BerthReadout r = Readout;
        return r.Valid ? (end > 0 ? r.BowSpeedKn : r.SternSpeedKn) : 0.0;
    }

    /// <summary>19.4г: угол отхода от курса по кромке, ° (> 0 — в сторону доворота события).</summary>
    public double OffAngleDeg => swingSign * Math.IEEERemainder(Model.Psi * 180.0 / Math.PI - BaseCourseDeg, 360.0);

    /// <summary>
    /// 19.4г: событие — лоцман доворачивает судно на deltaDeg (SwingRateDegS) и отпускает курс; ближний конец pinEnd
    /// крепят сразу: концы этого конца подают и выбирают «понемногу», у кранцев — на тормоз (стопор).
    /// </summary>
    public void StartSwing(double deltaDeg, int pinEnd)
    {
        swingTarget = CourseDeg + deltaDeg;
        swingSign = deltaDeg >= 0.0 ? 1.0 : -1.0;
        Swinging = true;
        HeadingFree = false;
        PinEnd = pinEnd;
        pinGiven = pinHaul = PinFast = false;
        pinCheck = 0.0;
    }

    /// <summary>19.4г: курс свободен — руль прямо, подруливающее стоп (конец доворота или буксир упёрся у отошедшего конца).</summary>
    public void FreeHeading()
    {
        if (!Swinging && !HeadingFree) return;
        Swinging = false;
        HeadingFree = true;
    }

    /// <summary>Лоцман на шаг (до Mooring.Apply и Model.Step): подруливающее, руль, машина, концы.</summary>
    public void Pilot(TugWorkDefinition d, double dt, double time)
    {
        // Курс — подруливающим и рулём (19.4г: при событии — доворот на заданный угол, затем курс свободен)
        double e = Math.IEEERemainder(Model.Psi * 180.0 / Math.PI - CourseDeg, 360.0);
        double r = Model.R * 180.0 / Math.PI;
        if (Swinging)
        {
            double step = SwingRateDegS * dt, dc = swingTarget - CourseDeg;
            CourseDeg += Clamp(dc, step);
            if (Math.Abs(swingTarget - CourseDeg) < 1e-9) FreeHeading();   // разворот задан — курс свободен
        }
        if (HeadingFree)
        {
            Mooring.Thruster.Command = 0.0;
            Model.RudderCommandDeg = 0.0;
        }
        else
        {
            Mooring.Thruster.Command = Clamp(-(e / EpsiFullDeg + r / RFullDegS), 1.0);
            Model.RudderCommandDeg = autopilot.Rudder(Model, CourseDeg);
        }
        // Место вдоль причала — машиной
        Berth.ToLocal(new WorldPosition(Model.East, Model.North), out double along, out _);
        double sign = Math.Cos(Model.Psi - Berth.BearingDeg * Math.PI / 180.0) >= 0.0 ? 1.0 : -1.0;   // нос к концу B — «вперёд» = +t
        Model.SpeedFactor = Clamp(-sign * KAlong * (along - HoldAlongM) - KSpeed * Model.U, SfMax);

        // 19.4г: стопор — концы ближнего конца подают сразу, выбирают «понемногу», у кранцев — на тормоз
        if (PinEnd != 0 && !LinesGiven)
        {
            int[] pl = PinEnd > 0 ? BowLines : SternLines;
            if (!pinGiven && time - lastLineTry >= 1.0)
            {
                lastLineTry = time;
                bool all = true;
                foreach (int i in pl)
                    if (Mooring.Lines[i].State != MooringLineState.Fast) all &= Mooring.Order(i, LineOrder.Give, Model, time, out _);
                pinGiven = all;
            }
            if (pinGiven && !PinFast)
            {
                pinCheck += dt;
                if (pinCheck >= 0.2 - 1e-9)
                {
                    pinCheck = 0.0;
                    double eg = EndGapM(PinEnd);
                    if (!double.IsNaN(eg) && eg <= d.atFendersGapM)
                    {
                        PinFast = true;
                        foreach (int i in pl) Mooring.Order(i, LineOrder.Hold, Model, time, out _);
                    }
                    else
                    {
                        double v = EndSpeedKn(PinEnd);
                        bool want = pinHaul ? v < HoldAboveKn : v < ResumeBelowKn;
                        if (want != pinHaul)
                        {
                            foreach (int i in pl) Mooring.Order(i, want ? LineOrder.HaulIn : LineOrder.Hold, Model, time, out _);
                            pinHaul = want;
                        }
                    }
                }
            }
        }

        // Концы
        double gap = GapM;
        // 19.4г: ближний конец на стопоре, отошедший дожат к кранцам — подают все концы (корму, вытянувшую концы на тормозе, выбирают)
        bool endIn = PinEnd != 0 && PinFast && EndGapM(-PinEnd) <= d.linesGapM;
        if (!LinesGiven && !double.IsNaN(gap) && (gap <= d.linesGapM || endIn) && time - lastLineTry >= 1.0)
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
    public double LengthM => Parameters.length;
    public double BeamM => Parameters.beam;

    public static bool IsClient(string id) => id == Id;

    /// <summary>Срез цели «CLIENT» для targets.csv и оценки (оценка её отбрасывает — WorkVessels).</summary>
    public TargetSample Sample(double t) =>
        new TargetSample { Time = t, Id = Id, East = Model.East, North = Model.North, HeadingDeg = HeadingDeg, SpeedKnots = SpeedKnots };
}

/// <summary>
/// 19.4б-2: суда «своей работы» — вызванный буксир «TUG» (19.3б) и судно-клиент «CLIENT» (19.4б): не цели расхождения
/// (оценка C2, «Текущий балл», CPA в окне инструктора), на планшете видны всегда.
/// </summary>
public static class WorkVessels
{
    public static bool Is(string id) => TugTarget.IsTug(id) || ClientShip.IsClient(id);
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

    // ─────────── 19.4б-3: лоцман командует упором ───────────
    /// <summary>
    /// Заданная скорость клиента к причалу, уз, по зазору (больше 20 м / 10–20 / 3–10 / меньше 3 м); на этапе «держать» — 0.
    /// Упор: P = F_в/T_гак + K_v·(v_з − v) (+ запас «держать»), округлённо до ступени рукоятки 25 %; новая ступень —
    /// не чаще PowerIntervalS и только если нужная отличается от заданной больше чем на 0,75 ступени (без «дребезга»).
    /// </summary>
    public static double TargetSpeedKn(double gapM) => gapM > 20.0 ? 0.3 : gapM > 10.0 ? 0.2 : gapM > 3.0 ? 0.15 : 0.08;
    public const double PowerKv = 1.5, HoldMargin = 0.05, PowerIntervalS = 15.0, InfoIntervalS = 5.0, InfoPeriodS = 30.0, SlowerMarginKn = 0.25;
    /// <summary>Последний доклад обстановки («До причала N м, идёт N уз») и его время — строка монитора пульта.</summary>
    public string LastInfo { get; private set; } = "";
    public double LastInfoTime { get; private set; } = double.NaN;
    public static readonly double[] InfoGapsM = { 30.0, 20.0, 10.0, 5.0, 2.0 };

    /// <summary>Заданная ступень упора (0 — стоп, 1…4 — 25…100 %); −1 — команд упором ещё не было.</summary>
    public int PowerStep { get; private set; } = -1;
    public double PowerStepTime { get; private set; } = double.NaN;
    /// <summary>Последнее сообщение лоцмана (любое) и его время — для монитора и надписи.</summary>
    public string LastMessage { get; private set; } = "";
    public double LastMessageTime { get; private set; } = double.NaN;
    /// <summary>Упор, который нужен по расчёту лоцмана (доля T_гак, до округления) — для разбора и тестов.</summary>
    public double PowerNeeded { get; private set; }
    private double lastInfo = -1e9, lastSlower = -1e9;
    private int infoMark;

    // ─────────── 19.4г: «случайное событие» у причала ───────────
    /// <summary>Событие взведено (ArmEvent после Place): отходит конец EventOffEnd (+1 — нос, −1 — корма) на EventAngleDeg при зазоре EventGapM.</summary>
    public bool EventArmed { get; private set; }
    public int EventOffEnd { get; private set; }
    public double EventAngleDeg { get; private set; }
    public double EventGapM { get; private set; }
    /// <summary>Событие произошло (EventTime); буксир упёрся в зоне отошедшего конца; ближний конец на стопоре; судно всё у кранцев.</summary>
    public bool EventFired { get; private set; }
    public double EventTime { get; private set; } = double.NaN;
    public bool TugAtEnd { get; private set; }
    public bool EventPinned { get; private set; }
    public bool EventDone { get; private set; }
    /// <summary>Середина зоны отошедшего конца, м от миделя клиента (> 0 — к носу).</summary>
    public double EndTargetX => EventOffEnd * Task.endZoneX * Client.Parameters.length;
    /// <summary>Идёт событие: буксир должен быть у отошедшего конца (до «всё у кранцев»).</summary>
    public bool EventActive => EventFired && !EventDone;

    /// <summary>Взвести событие (после Place): конец, угол, зазор срабатывания.</summary>
    public void ArmEvent(int offEnd, double angleDeg, double gapM)
    {
        EventArmed = true;
        EventOffEnd = offEnd >= 0 ? 1 : -1;
        EventAngleDeg = angleDeg;
        EventGapM = gapM;
    }

    /// <summary>Взвести событие со случайными концом, углом (шаг 0,5°) и зазором (шаг 0,1 м) в пределах задания.</summary>
    public void ArmEventRandom(Random rnd)
    {
        int end = rnd.Next(2) == 0 ? 1 : -1;
        double a = Math.Round(2.0 * (Task.eventAngleMinDeg + (Task.eventAngleMaxDeg - Task.eventAngleMinDeg) * rnd.NextDouble())) / 2.0;
        double g = Math.Round(10.0 * (Task.eventGapMinM + (Task.eventGapMaxM - Task.eventGapMinM) * rnd.NextDouble())) / 10.0;
        ArmEvent(end, a, g);
    }

    // Срабатывание: лоцман «стоп толкать», доворот клиента (отходящий конец — от причала), ближний конец — на стопор
    private void FireEvent(double time)
    {
        EventFired = true;
        EventTime = time;
        int s = Client.SideToBerth;
        Client.StartSwing(-s * EventOffEnd * EventAngleDeg, -EventOffEnd);
        PowerStep = 0;
        PowerStepTime = time;
        Say(PilotOrderKind.StopPush, time, "Стоп толкать!", 0.0);
        Say(PilotOrderKind.ToEnd, time, TugWorkText.ToEnd(EventOffEnd, EventAngleDeg), EventAngleDeg);
        EventMark(TugEventPhase.ToEnd, time, EventAngleDeg, EventGapM);
    }

    private void EventMark(TugEventPhase p, double time, double value, double value2) =>
        Events.Add(new ExerciseEvent(ExerciseEventType.TugWorkEvent)
        {
            Time = time, Index = (int)p, Value = Math.Round(value, 2), Value2 = Math.Round(value2, 2), Subject = TugWorkText.End(EventOffEnd)
        });

    // Ход события на шаге (после контакта буксир — клиент и шага клиента)
    private void EventStep(double time)
    {
        if (!EventArmed) return;
        if (!EventFired)
        {
            double g = Client.GapM;
            if (done[(int)TugWorkStage.Approach] && !done[(int)TugWorkStage.Push] && !double.IsNaN(g) && g <= EventGapM) FireEvent(time);
            return;
        }
        if (EventDone) return;
        if (!TugAtEnd && Contact.FenderForce > 0.0 && Contact.TouchSide == WorkSide && Math.Abs(Contact.TouchX - EndTargetX) <= Task.contactZoneM)
        {
            TugAtEnd = true;
            Client.FreeHeading();   // лоцман стопорит подруливающее — дальше конец ведёт буксир
            PowerStep = -1;   // команда упором — сразу (как первая после «толкайте»)
            lastInfo = -1e9;
            EventMark(TugEventPhase.AtEnd, time, time - EventTime, Contact.TouchX);
        }
        if (!EventPinned && Client.PinFast)
        {
            EventPinned = true;
            Say(PilotOrderKind.Info, time, TugWorkText.Pinned(-EventOffEnd), 0.0);
            EventMark(TugEventPhase.Pinned, time, Client.OffAngleDeg, Client.EndGapM(-EventOffEnd));
        }
        if (done[(int)TugWorkStage.Push])
        {
            EventDone = true;
            EventMark(TugEventPhase.Done, time, Client.OffAngleDeg, Client.GapM);
        }
    }

    /// <summary>
    /// 19.4г: нужный упор у отошедшего конца — ближний конец закреплён (или вот-вот), судно поворачивается вокруг него:
    /// ветер берёт буксир с плечом |x_ст| / |x_б − x_ст| (x_ст — ближний конец, x_б — буксир, от миделя; ветер — у миделя).
    /// </summary>
    public double EndPowerFor(double gapM, double towardKn)
    {
        double xp = EventOffEnd > 0 ? BerthReadout.SternX : BerthReadout.BowX, xb = EventOffEnd * Task.endZoneX;
        double lever = Math.Abs(xp) / Math.Max(0.05, Math.Abs(xb - xp));
        double p = lever * WindOffBerth() / Math.Max(1.0, OwnSpec.BollardPull) + PowerKv * (TargetSpeedKn(gapM) - towardKn);
        return Math.Max(0.0, Math.Min(1.0, p));
    }

    /// <summary>Сила ветра на клиента от причала (по нормали к кромке), Н (> 0 — сносит от причала).</summary>
    public double WindOffBerth()
    {
        if (Wind.Calm) return 0.0;
        ShipDynamicsModel cm = Client.Model;
        WindForce f = WindLoad.Compute(Client.WindArea, Wind, cm.Psi, cm.U, cm.V);
        return -f.Y * Client.SideToBerth;   // Y > 0 — на ПБ; причал у ЛБ (−1) — к причалу −Y
    }

    /// <summary>Нужная доля упора по зазору, скорости к причалу и ветру (этап «держать» — hold).</summary>
    public double PowerFor(double gapM, double towardKn, bool hold)
    {
        double vt = hold ? 0.0 : TargetSpeedKn(gapM);
        double p = WindOffBerth() / Math.Max(1.0, OwnSpec.BollardPull) + PowerKv * (vt - towardKn) + (hold ? HoldMargin : 0.0);
        return Math.Max(0.0, Math.Min(1.0, p));
    }

    // Команды упором, обстановка и «сбавь» — от касания в зоне («толкайте») до «концы закреплены»
    private void PilotPower(double time)
    {
        if (!done[(int)TugWorkStage.Approach] || done[(int)TugWorkStage.Hold]) return;
        bool hold = done[(int)TugWorkStage.Push];
        if (EventActive && !TugAtEnd) return;   // 19.4г: буксир переходит к отошедшему концу — команд упором нет
        bool atEnd = EventActive && !hold;      // 19.4г: у отошедшего конца — по его зазору и скорости
        double gap = atEnd ? Client.EndGapM(EventOffEnd) : Client.GapM;
        if (double.IsNaN(gap)) return;
        BerthReadout r = Client.Readout;
        double v = atEnd ? Client.EndSpeedKn(EventOffEnd) : r.Valid ? 0.5 * (r.BowSpeedKn + r.SternSpeedKn) : 0.0;
        PowerNeeded = atEnd ? EndPowerFor(gap, v) : PowerFor(gap, v, hold);
        int want = (int)Math.Round(PowerNeeded * TugJoystick.StepCount);
        bool first = PowerStep < 0;
        if (first || want != PowerStep && time - PowerStepTime >= PowerIntervalS - 1e-9 &&
                     Math.Abs(PowerNeeded * TugJoystick.StepCount - PowerStep) > 0.75)
        {
            PowerStep = want;
            PowerStepTime = time;
            Say(want > 0 ? PilotOrderKind.PushStep : PilotOrderKind.StopPush, time, TugWorkText.Power(want, hold), want * 25.0);
        }
        // Обстановка — сразу с первой командой упором, на 30, 20, 10, 5, 2 м и (19.4б-3 по проверке: «толкаю, а как будто
        // безрезультатно — думал, уже у причала») не реже раза в InfoPeriodS
        if (!hold)
        {
            bool mark = false;
            while (infoMark < InfoGapsM.Length && gap <= InfoGapsM[infoMark]) { infoMark++; mark = true; }
            bool due = first || mark && time - lastInfo >= InfoIntervalS - 1e-9 || time - lastInfo >= InfoPeriodS - 1e-9;
            if (due)
            {
                lastInfo = time;
                LastInfo = atEnd ? TugWorkText.EndInfo(EventOffEnd, gap, Math.Abs(Client.OffAngleDeg), v) : TugWorkText.Info(gap, v);
                LastInfoTime = time;
                Say(PilotOrderKind.Info, time, LastInfo, Math.Round(gap, 1));
            }
        }
        // Быстро идёт к причалу
        if (!hold && gap < 20.0 && v > TargetSpeedKn(gap) + SlowerMarginKn && time - lastSlower >= PowerIntervalS - 1e-9)
        {
            lastSlower = time;
            Say(PilotOrderKind.Slower, time, TugWorkText.SlowerText, Math.Round(v, 2));
        }
    }

    // Сообщение лоцмана без смены этапа (Order — команда задания — не меняется)
    private void Say(PilotOrderKind k, double time, string text, double value)
    {
        LastMessage = text;
        LastMessageTime = time;
        Events.Add(new ExerciseEvent(ExerciseEventType.PilotOrder) { Time = time, Index = (int)k, Subject = text, Value = value });
    }

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
        PowerStep = -1;
        PowerStepTime = LastMessageTime = double.NaN;
        LastMessage = "";
        PowerNeeded = 0.0;
        LastInfo = "";
        LastInfoTime = double.NaN;
        lastInfo = lastSlower = -1e9;
        infoMark = 0;
        EventArmed = EventFired = TugAtEnd = EventPinned = EventDone = false;   // 19.4г: событие взводят после Place
        EventTime = double.NaN;
    }

    /// <summary>
    /// Начальные условия: клиент лагом к причалу; свой буксир — напротив миделя дальнего борта, носом к нему, без хода.
    /// moveOwn = false (19.4б-2, сцена) — свой буксир не переставлять: его ставит ScenarioRunner из начальных условий
    /// сценария (DemoTugWorkScenario.Fill посчитал их той же TugStart).
    /// </summary>
    public void Place(ShipDynamicsModel own, bool moveOwn = true)
    {
        Reset();
        BerthArea b = Client.Berth;
        Client.Place(0.5 * b.Length + Task.clientAlongM, Task.clientGapM);
        WorkSide = -Client.SideToBerth;
        if (!moveOwn || own == null) return;
        TugStart(Client.Model, Client.Parameters, OwnSpec, WorkSide, Task.tugDistanceM, out double e, out double n, out double psi);
        own.Teleport(e, n, psi, 0.0, 0.0);
    }

    // ─────────── 19.4б-2: срез для mooring.csv ───────────

    /// <summary>
    /// Строка mooring.csv в 19-03: столбцы причала — по клиенту (зазоры, кранцы, кромка, концы, подруливающее клиента),
    /// stages_done — заданий буксира выполнено, moored_hold_s — «концы закреплены» подряд, с (ClientShip.SecuredHold);
    /// столбцы буксира — свой буксир (OwnTugSample). Один срез у тренажёра (ExerciseRecorder) и прогона (TugWorkSimulation).
    /// </summary>
    public MooringSample Sample(double t, int berthIndex, AzimuthDrive drive, Wind wind, BridgeStation station = BridgeStation.Helm)
    {
        MooringSample s = MooringSampler.Build(t, Client.Berth, berthIndex, Client.Parameters, Client.Model, Client.Mooring,
                                               ThrusterOrders.ZeroIndex, station, null);
        s.StagesDone = DoneCount;
        s.MooredHoldS = Client.SecuredHold;
        s.ThrusterOrderPct = Client.Mooring.Thruster != null ? Client.Mooring.Thruster.Command * 100.0 : 0.0;
        s.Towing = OwnTugSample(drive, wind);
        return s;
    }

    /// <summary>
    /// Столбцы буксира (табл. 19.17) для своего буксира: место — у миделя; режим — «толкать», пока кранец упирается в борт,
    /// иначе «стоп»; направление силы на клиента от его ДП (к причалу: 270° при работе у ПБ, 90° — у ЛБ); доля тяги —
    /// суммарный упор колонок от тяги на гаке, %; упор в борт — сила кранца, кН; трос — не подан; ветер.
    /// </summary>
    public TowingSample OwnTugSample(AzimuthDrive drive, Wind wind)
    {
        TowingSample s = TowingSample.Build(null, wind);
        s.Present = true;
        s.Station = TugStation.Midship;
        bool push = Contact.FenderForce > 0.0;
        s.Mode = push ? TugMode.Push : TugMode.Stop;
        s.DirectionDeg = push ? (WorkSide >= 0 ? 270.0 : 90.0) : double.NaN;
        s.PowerPct = drive != null ? 50.0 * (drive.UnitLoad(1) + drive.UnitLoad(2)) : double.NaN;
        s.Line = TowLineStatus.NotFast;
        s.PushKn = Contact.FenderForce / 1000.0;
        return s;
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
        EventStep(time);    // 19.4г
        PilotPower(time);   // 19.4б-3
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
        LastMessage = TugWorkText.Order(k, WorkSide == 0 ? 1 : WorkSide);
        LastMessageTime = time;
        Events.Add(new ExerciseEvent(ExerciseEventType.PilotOrder) { Time = time, Index = (int)k, Subject = LastMessage });
    }
}
