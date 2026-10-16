// ============================================================
// Towing.cs — Буксир, буксирный трос, толкание (запрос 19.3а, Core)
//
// Буксир — отдельное судно: своя модель 3 СС (ShipDynamicsModel, параметры ShipPresets.FillTug) и
// движитель из двух азимутальных колонок в корме (ASD) в точках (x_к, ±y_к). Колонка даёт упор любого
// направления не больше T_гак/2 (T_гак — тяга на гаке, по умолчанию 20 т). Требуемые обобщённые силы (X, Y, N) в осях
// буксира раскладываются на колонки (AzimuthAllocation):
//   f_y1 = f_y2 = Y/2,  f_x1 − f_x2 = (x_к·Y − N)/y_к,  f_x1 + f_x2 = X
//   (момент колонки i в (x_к, y_i): x_к·f_yi − y_i·f_xi; y_1 = +y_к — ПБ); если упор колонки больше
//   предела — все составляющие уменьшаются в одно число раз. Набор упора — звено 1-го порядка, τ_к = 2 с.
// Буксир работает «носом»: трос — от гака на носу буксира (x = +L_б/2) к клюзу судна (нос или корма по ДП).
//
// Автоматика буксира (TugSkipper) выполняет команду капитана (TugCommand):
//   • «тянуть» — направление силы на судно θ относительно ДП (0 — вперёд, 90 — на ПБ, 180 — назад,
//     270 — на ЛБ) и доля тяги P: буксир держится на линии θ от клюза, носом к судну; пока трос слаб —
//     ПД-регулятор положения гака на расстоянии длины троса; трос обтянут — упор назад P·T_гак;
//   • «толкать» — буксир упирается носом (кранец) в борт судна напротив точки x_т и толкает с упором P·T_гак
//     в направлении θ (только поперёк: 90 или 270);
//   • «стоп» — без силы на судно (19.3б: буксир держит своё место, см. ниже; в 19.3а — упор 0).
//   Поперечное положение и курс — ПД-регуляторы: Y = m₂₂·(−ω²·e_y − 2ζω·ė_y), N = m₃₃·(−ω_ψ²·e_ψ − 2ζω_ψ·(r_б − r_с)),
//   ω = 0,3 рад/с, ω_ψ = 0,4 рад/с, ζ = 0,8 (допущения; подобраны на стенде TowingTrials).
// Трос (TowLine) — упругий синтетический, как швартовный конец (MooringLine.ElasticTension: T = MBL·(ε/0,2)^1,5,
// демпфирование ζ = 0,1); MBL троса = 2,5·T_гак (допущение по практике портовой буксировки: 2…3 тяги на гаке),
// перегрузка > 0,5·MBL, обрыв > MBL. На судно — сила в клюзе к гаку, на буксир — в гаке к клюзу (горизонтально).
// Кранец буксира (толкание): точка носа буксира внутри контура корпуса судна (MooringHull) на глубину δ —
// сила F = k·δ + c·δ̇ (δ̇ > 0), k = 1,5 МН/м, c = 0,3 МН·с/м (допущения: резиновый кранец буксира), по нормали к борту;
// трение вдоль борта μ = 0,6. В касании курс буксира держит только кормовая поперечная сила (поворот вокруг носа).
// Ветер (WindLoad, Isherwood) действует на судно и на буксир. Все силы — только в модели 3 СС.
// Порядок вызова — как MooringSystem: Apply(ship, dt, t) до ship.Step(dt); буксир делает свой шаг внутри.
//
// 19.3б (сцена): место буксира у судна — TugStation (у кормы / у носа / у миделя: клюз троса и точка
// толкания, TugOrders.TowPointX / PushPointX); «стоп» — буксир держит своё место (на линии θ в длине троса
// или в StandoffM от борта) без силы на судно — геометрия последней рабочей команды (Geometry);
// смена направления тяги — рабочее направление поворачивается к заданному со скоростью SwingRateDegS по дуге вокруг клюза,
// не через судно (у кормы — не через «вперёд», у носа — не через «назад»), буксир не режет корму;
// смена борта толкания — обход оконечности судна (сначала вдоль своего борта за оконечность, затем на
// другой борт, в AroundClearM от корпуса), иначе ПД-регулятор вёл бы буксир сквозь корпус.
// ============================================================
using System;
using System.Collections.Generic;

/// <summary>Режим работы буксира (команда капитана).</summary>
public enum TugMode
{
    Stop,   // без силы на судно: держит своё место (19.3б; в 19.3а — упор 0)
    Pull,   // тянуть на тросе
    Push    // толкать носом в борт
}

/// <summary>Место буксира у судна (19.3б): клюз троса и точка толкания — TugOrders.TowPointX / PushPointX.</summary>
public enum TugStation
{
    Stern,     // у кормы
    Bow,       // у носа
    Midship    // у миделя
}

/// <summary>Команда буксиру: режим, направление силы на судно (° от ДП, по часовой), доля тяги 0…1.</summary>
public struct TugCommand
{
    public TugMode Mode;
    public double DirectionDeg;
    public double Power;

    public static TugCommand Pull(double dirDeg, double power) => new TugCommand { Mode = TugMode.Pull, DirectionDeg = dirDeg, Power = power };
    public static TugCommand Push(double dirDeg, double power) => new TugCommand { Mode = TugMode.Push, DirectionDeg = dirDeg, Power = power };
    public static TugCommand Stop => new TugCommand { Mode = TugMode.Stop };

    public string Describe()
    {
        if (Mode == TugMode.Stop) return "стоп";
        string dir = DirectionTitle(DirectionDeg);
        return $"{(Mode == TugMode.Pull ? "тянуть" : "толкать")} {dir}, {ScenarioText.Num(Power * 100.0)} %";
    }

    /// <summary>Направление силы на судно словами: «вперёд», «на ПБ», «назад», «на ЛБ», «вперёд-ПБ» …</summary>
    public static string DirectionTitle(double deg)
    {
        double d = ((deg % 360.0) + 360.0) % 360.0;
        string[] names = { "вперёд", "вперёд-ПБ", "на ПБ", "назад-ПБ", "назад", "назад-ЛБ", "на ЛБ", "вперёд-ЛБ" };
        return names[(int)Math.Round(d / 45.0) % 8];
    }
}

/// <summary>Характеристики буксира.</summary>
public sealed class TugSpec
{
    /// <summary>Тяга на гаке, Н.</summary>
    public double BollardPull = DefaultBollardPullTonnes * 9.80665e3;
    /// <summary>Колонки: от миделя (< 0 — в корме), м; от ДП, м.</summary>
    public double ThrusterX = -11.0, ThrusterY = 2.5;
    /// <summary>Гак (трос) и кранец — на носу, от миделя, м.</summary>
    public double BowX = 14.0;
    /// <summary>Набор упора колонок, с.</summary>
    public double SpoolTime = 2.0;
    /// <summary>Кранец буксира: жёсткость, Н/м; демпфирование, Н·с/м.</summary>
    public double FenderStiffness = 1.5e6, FenderDamping = 0.3e6;
    /// <summary>Трение кранца буксира о борт (резина по стали, допущение).</summary>
    public double FenderFriction = 0.6;

    /// <summary>Тяга на гаке по умолчанию, т (ShipPresets.FillTug).</summary>
    public const double DefaultBollardPullTonnes = 20.0;

    public static TugSpec For(ShipParameters tug, double bollardPullTonnes = DefaultBollardPullTonnes) => new TugSpec
    {
        BollardPull = bollardPullTonnes * 9.80665e3,
        ThrusterX = -0.4 * tug.length, ThrusterY = 0.25 * tug.beam, BowX = 0.5 * tug.length
    };
}

/// <summary>Распределение обобщённых сил (X, Y, N) на две азимутальные колонки (формулы — в шапке файла).</summary>
public static class AzimuthAllocation
{
    /// <summary>Упоры колонок (ПБ: fx1, fy1; ЛБ: fx2, fy2) и множитель ограничения k ≤ 1.</summary>
    public static double Solve(double X, double Y, double N, double xa, double ya, double maxPerUnit,
                               out double fx1, out double fy1, out double fx2, out double fy2)
    {
        double d = (xa * Y - N) / Math.Max(1e-6, ya);
        fx1 = 0.5 * (X + d); fx2 = 0.5 * (X - d);
        fy1 = fy2 = 0.5 * Y;
        double m = Math.Max(Math.Sqrt(fx1 * fx1 + fy1 * fy1), Math.Sqrt(fx2 * fx2 + fy2 * fy2));
        double k = m > maxPerUnit && m > 0 ? maxPerUnit / m : 1.0;
        fx1 *= k; fy1 *= k; fx2 *= k; fy2 *= k;
        return k;
    }

    /// <summary>Обобщённые силы от упоров колонок (проверка разложения).</summary>
    public static void Resultant(double fx1, double fy1, double fx2, double fy2, double xa, double ya, out double X, out double Y, out double N)
    {
        X = fx1 + fx2; Y = fy1 + fy2;
        N = xa * fy1 - ya * fx1 + xa * fy2 + ya * fx2;
    }
}

/// <summary>Буксирный трос (упругий, на лебёдке буксира).</summary>
public sealed class TowLine
{
    public const double MblFactor = 2.5;
    public bool Fast { get; private set; }
    public bool Broken { get; private set; }
    public double RestLength { get; private set; }
    public double Length { get; private set; }
    public double Tension { get; private set; }
    public double MaxTension { get; private set; }
    public double Mbl;
    public bool Overloaded { get; private set; }
    private double prevLength = double.NaN;

    public TowLine(double mbl) { Mbl = mbl; }

    public void MakeFast(double length)
    {
        Fast = true; Broken = false;
        RestLength = Math.Max(MooringLine.MinLength, length);
        Length = length; Tension = 0; MaxTension = 0; Overloaded = false; prevLength = double.NaN;
    }

    public void Release()
    {
        Fast = false; Tension = 0; Overloaded = false; prevLength = double.NaN;
    }

    /// <summary>Лебёдка буксира: новая длина без натяжения, м.</summary>
    public void SetRestLength(double length) => RestLength = Math.Max(MooringLine.MinLength, length);

    /// <summary>Шаг: длина хорды → натяжение, перегрузка, обрыв (как MooringLine.Update). mass — масса буксира (демпфирование).</summary>
    public MooringLineEvent Update(double chord, double dt, double mass)
    {
        if (!Fast) { Tension = 0; return MooringLineEvent.None; }
        Length = chord;
        double rate = double.IsNaN(prevLength) || dt <= 0 ? 0.0 : (chord - prevLength) / dt;
        prevLength = chord;
        double te = MooringLine.ElasticTension(Mbl, RestLength, chord, out double k);
        double t = te;
        if (te > 0.0) t += 2.0 * MooringLine.DampingRatio * Math.Sqrt(k * Math.Max(1.0, mass)) * rate;
        Tension = Math.Max(0.0, t);
        if (Tension > MaxTension) MaxTension = Tension;
        if (Tension > Mbl)
        {
            Fast = false; Broken = true; Tension = 0; Overloaded = false;
            return MooringLineEvent.Broken;
        }
        if (!Overloaded && Tension > MooringLine.OverloadFraction * Mbl) { Overloaded = true; return MooringLineEvent.Overload; }
        if (Overloaded && Tension < MooringLine.OverloadClearFraction * Mbl) { Overloaded = false; return MooringLineEvent.OverloadCleared; }
        return MooringLineEvent.None;
    }
}

public enum TowEventType
{
    LineMadeFast,       // трос подан: Value — длина, м
    LineReleased,       // трос отдан
    LineOverload,       // натяжение > 0,5·MBL: Value — T, Н; Value2 — T/MBL
    LineOverloadCleared,
    LineBroken,         // обрыв: Value — T_max, Н
    PushContact,        // буксир упёрся в борт: Value — скорость сближения, м/с
    CommandChanged      // новая команда: Subject — словами
}

public struct TowEvent
{
    public TowEventType Type;
    public double Time, Value, Value2;
    public string Subject;
}

/// <summary>Буксир с колонками и автоматикой (шкипером).</summary>
public sealed class TugVessel
{
    public readonly ShipDynamicsModel Model;
    public readonly TugSpec Spec;
    public readonly ShipWindArea WindArea;
    public TugCommand Command = TugCommand.Stop;
    /// <summary>Заданные и фактические обобщённые силы колонок (оси буксира), Н, Н·м.</summary>
    public double OrderX, OrderY, OrderN, ThrustX, ThrustY, ThrustN;
    /// <summary>Упоры колонок последнего шага (ПБ, ЛБ), Н (для вида и стенда).</summary>
    public double Fx1, Fy1, Fx2, Fy2;

    // Регуляторы (допущения, ThesisNotes 19.24)
    public double Omega = 0.3, OmegaHeading = 0.4, Zeta = 0.8;

    public TugVessel(ShipParameters p, TugSpec spec)
    {
        Spec = spec ?? TugSpec.For(p);
        Model = new ShipDynamicsModel(p) { MotionOverride = MotionModel.Manoeuvring3Dof, SpeedFactor = 0.0 };
        WindArea = ShipWindArea.Estimate(p);
    }

    public WorldPosition Position => new WorldPosition(Model.East, Model.North);
    public double HeadingDeg { get { double h = Model.Psi * 180.0 / Math.PI % 360.0; return h < 0 ? h + 360.0 : h; } }
    public double SpeedKnots => Model.SpeedThroughWater / ShipParameters.KnotsToMs;

    /// <summary>Гак / кранец (нос буксира) в мировых координатах.</summary>
    public WorldPosition Bow => MooringHull.ToWorld(Model.East, Model.North, Model.Psi, Spec.BowX, 0.0);

    /// <summary>Упор колонок на шаг: набор упора к заданному, разложение, силы в модель.</summary>
    public void ApplyThrust(double dt)
    {
        double a = 1.0 - Math.Exp(-dt / Math.Max(1e-3, Spec.SpoolTime));
        ThrustX += (OrderX - ThrustX) * a;
        ThrustY += (OrderY - ThrustY) * a;
        ThrustN += (OrderN - ThrustN) * a;
        AzimuthAllocation.Solve(ThrustX, ThrustY, ThrustN, Spec.ThrusterX, Spec.ThrusterY, 0.5 * Spec.BollardPull, out Fx1, out Fy1, out Fx2, out Fy2);
        Model.AddForce(Fx1 + Fx2, Fy1 + Fy2, Spec.ThrusterX);
        Model.AddMoment(-Spec.ThrusterY * Fx1 + Spec.ThrusterY * Fx2);
    }
}

/// <summary>
/// Судно + буксир + трос + толкание + ветер в одном шаге (как MooringSystem). Судно может отсутствовать
/// (ship == null) — тогда трос закреплён на неподвижной точке Anchor (стенд «тяга на гаке»).
/// </summary>
public sealed class TowingSystem
{
    public readonly TugVessel Tug;
    public readonly TowLine Line;
    public ShipParameters ShipParams;
    public ShipWindArea ShipWind;
    public Wind Wind;
    /// <summary>Клюз судна для троса и точка толкания: от миделя, м (> 0 — в нос).</summary>
    public double TowPointX, PushPointX;
    /// <summary>Неподвижная точка вместо судна (стенд), если судна нет.</summary>
    public WorldPosition Anchor;
    public readonly List<TowEvent> Events = new List<TowEvent>();

    // Состояние для индикации
    public double PushForce { get; private set; }
    public WindForce ShipWindForce { get; private set; }
    public bool PushContact => PushForce > 0.0;

    /// <summary>Трос считается слабым, если короче длины без натяжения больше чем на …, м (регулятор подтягивает буксир).</summary>
    public const double SlackM = 1.0;
    /// <summary>19.3б: «стоп» у борта — нос буксира в … м от борта, м.</summary>
    public const double StandoffM = 2.0;
    /// <summary>19.3б: обход оконечности при смене борта толкания — в … м от корпуса, м.</summary>
    public const double AroundClearM = 15.0;
    /// <summary>19.3б: рабочее направление тяги поворачивается к заданному не быстрее …, °/с (буксир идёт по дуге вокруг клюза).</summary>
    public const double SwingRateDegS = 3.0;

    /// <summary>19.3б: место буксира у судна (клюз и точка толкания).</summary>
    public TugStation Station { get; private set; } = TugStation.Stern;
    /// <summary>19.3б: геометрия работы (Pull — на тросе по линии θ, Push — у борта) — по последней рабочей команде; её держит «стоп».</summary>
    public TugMode Geometry { get; private set; } = TugMode.Pull;
    /// <summary>19.3б: буксир обходит оконечность судна (смена борта толкания).</summary>
    public bool GoingAround { get; private set; }

    private double prevPen = double.NaN;
    private bool contactReported;

    public TowingSystem(ShipParameters ship, ShipParameters tug, TugSpec spec = null)
    {
        ShipParams = ship;
        Tug = new TugVessel(tug, spec ?? TugSpec.For(tug));
        Line = new TowLine(TowLine.MblFactor * Tug.Spec.BollardPull);
        if (ship != null)
        {
            ShipWind = ShipWindArea.Estimate(ship);
            TowPointX = -0.45 * ship.length;
            PushPointX = -0.3 * ship.length;
        }
    }

    // ── Точки судна ──
    /// <summary>Клюз судна (на ДП) в мировых координатах.</summary>
    public WorldPosition TowPoint(ShipDynamicsModel ship) =>
        ship == null ? Anchor : MooringHull.ToWorld(ship.East, ship.North, ship.Psi, TowPointX, 0.0);

    /// <summary>Сторона толкания (борт, у которого стоит буксир): −1 — ЛБ, +1 — ПБ; сила — к противоположному борту.</summary>
    public static int PushSide(double directionDeg) => Math.Sin(directionDeg * Math.PI / 180.0) >= 0.0 ? -1 : 1;

    /// <summary>Команда буксиру (событие CommandChanged).</summary>
    public void Order(TugCommand c, double time)
    {
        Tug.Command = c;
        if (c.Mode != TugMode.Stop) Geometry = c.Mode;
        Events.Add(new TowEvent { Type = TowEventType.CommandChanged, Time = time, Subject = c.Describe(), Value = c.Power, Value2 = c.DirectionDeg });
    }

    /// <summary>19.3б: место буксира у судна — клюз троса и точка толкания (TugOrders).</summary>
    public void SetStation(TugStation s)
    {
        Station = s;
        if (ShipParams == null) return;
        TowPointX = TugOrders.TowPointX(s, ShipParams.length);
        PushPointX = TugOrders.PushPointX(s, ShipParams.length);
    }

    /// <summary>19.3б: геометрия работы без команды (вызов буксира: у кормы и носа — на тросе, у миделя — у борта).</summary>
    public void SetGeometry(TugMode g)
    {
        if (g != TugMode.Stop) Geometry = g;
    }

    public void MakeFast(double length, double time)
    {
        Line.MakeFast(length);
        Events.Add(new TowEvent { Type = TowEventType.LineMadeFast, Time = time, Value = length });
    }

    public void ReleaseLine(double time)
    {
        if (!Line.Fast) return;
        Line.Release();
        Events.Add(new TowEvent { Type = TowEventType.LineReleased, Time = time });
    }

    /// <summary>
    /// Поставить буксир на место по команде (без скорости): «тянуть» — на линии θ в длине троса от клюза,
    /// «толкать» — носом к борту у точки толкания. Для начала стенда и сценария.
    /// </summary>
    public void PlaceTug(ShipDynamicsModel ship, TugCommand c, double lineLength)
    {
        double psiS = ship != null ? ship.Psi : 0.0;
        double theta = psiS + c.DirectionDeg * Math.PI / 180.0;
        if (c.Mode != TugMode.Stop) Geometry = c.Mode;
        TugMode geo = c.Mode == TugMode.Stop ? Geometry : c.Mode;   // 19.3б: «стоп» — на месте последней работы
        WorldPosition bow;
        double psiT;
        if (geo == TugMode.Push && ship != null)
        {
            int side = PushSide(c.DirectionDeg);
            double y = side * MooringHull.HalfBeamAt(PushPointX, ShipParams.length, ShipParams.beam);
            bow = MooringHull.ToWorld(ship.East, ship.North, ship.Psi, PushPointX, y + side * (c.Mode == TugMode.Stop ? StandoffM : 0.3));
            psiT = psiS - side * Math.PI / 2.0;
        }
        else
        {
            WorldPosition f = TowPoint(ship);
            bow = new WorldPosition(f.East + Math.Sin(theta) * lineLength, f.North + Math.Cos(theta) * lineLength);
            psiT = theta + Math.PI;
        }
        // Центр буксира — на BowX позади гака по его курсу
        double e = bow.East - Tug.Spec.BowX * Math.Sin(psiT), n = bow.North - Tug.Spec.BowX * Math.Cos(psiT);
        Tug.Model.Teleport(e, n, psiT, 0.0, 0.0);
        Tug.Command = c;
        Tug.OrderX = Tug.OrderY = Tug.OrderN = Tug.ThrustX = Tug.ThrustY = Tug.ThrustN = 0.0;
        prevPen = double.NaN;
        contactReported = false;
        GoingAround = false;
        swingDeg = c.DirectionDeg;
        lastGeometry = geo;
    }

    // ─────────── Шаг ───────────

    /// <summary>Силы троса, кранца буксира и ветра на судно (до ship.Step) и шаг буксира.</summary>
    public void Apply(ShipDynamicsModel ship, double dt, double time)
    {
        ShipDynamicsModel tug = Tug.Model;

        // Ветер
        ShipWindForce = ship != null ? WindLoad.Apply(ship, ShipWind, Wind) : default;
        WindLoad.Apply(tug, Tug.WindArea, Wind);

        // Трос: от гака буксира к клюзу судна
        WorldPosition f = TowPoint(ship), b = Tug.Bow;
        if (Line.Fast)
        {
            double hE = f.East - b.East, hN = f.North - b.North;
            double chord = Math.Sqrt(hE * hE + hN * hN);
            MooringLineEvent ev = Line.Update(chord, dt, Tug.Model.Coefficients3Dof.Mass);
            if (Line.Tension > 0.0 && chord > 1e-6)
            {
                double ue = hE / chord, un = hN / chord;
                MooringHull.ApplyWorldForce(tug, Line.Tension * ue, Line.Tension * un, b);
                if (ship != null) MooringHull.ApplyWorldForce(ship, -Line.Tension * ue, -Line.Tension * un, f);
            }
            switch (ev)
            {
                case MooringLineEvent.Overload: Events.Add(new TowEvent { Type = TowEventType.LineOverload, Time = time, Value = Line.Tension, Value2 = Line.Tension / Line.Mbl }); break;
                case MooringLineEvent.OverloadCleared: Events.Add(new TowEvent { Type = TowEventType.LineOverloadCleared, Time = time }); break;
                case MooringLineEvent.Broken: Events.Add(new TowEvent { Type = TowEventType.LineBroken, Time = time, Value = Line.MaxTension }); break;
            }
        }

        // Кранец буксира: нос буксира внутри контура корпуса судна
        PushForce = 0.0;
        if (ship != null) Contact(ship, b, dt, time);

        Skipper(ship, dt);
        Tug.ApplyThrust(dt);
        tug.Step(dt);
    }

    private void Contact(ShipDynamicsModel ship, WorldPosition bow, double dt, double time)
    {
        double s = Math.Sin(ship.Psi), c = Math.Cos(ship.Psi);
        double dE = bow.East - ship.East, dN = bow.North - ship.North;
        double x = dE * s + dN * c, y = dE * c - dN * s;   // связанные оси судна
        double half = MooringHull.HalfBeamAt(x, ShipParams.length, ShipParams.beam);
        double pen = half - Math.Abs(y);
        if (half <= 0.0 || pen <= 0.0)
        {
            prevPen = double.NaN;
            if (pen < -0.5) contactReported = false;
            return;
        }
        double rate = double.IsNaN(prevPen) ? 0.0 : (pen - prevPen) / Math.Max(1e-6, dt);
        prevPen = pen;
        double force = Math.Max(0.0, Tug.Spec.FenderStiffness * pen + Tug.Spec.FenderDamping * Math.Max(0.0, rate));
        PushForce = force;
        int side = y >= 0.0 ? 1 : -1;
        // Нормаль борта наружу в мировых осях: ŝ = (cos ψ, −sin ψ) — на ПБ
        double nE = side * c, nN = -side * s;
        // Трение кранца вдоль борта (ось x судна): F_т = −μ·F·th(v_т/0,05 м/с) — нос буксира не скользит по борту
        WorldPosition vb = MooringHull.PointVelocity(Tug.Model, bow), vs = MooringHull.PointVelocity(ship, bow);
        double vt = (vb.East - vs.East) * s + (vb.North - vs.North) * c;
        double ft = -Tug.Spec.FenderFriction * force * Math.Tanh(vt / 0.05);
        double tE = ft * s, tN = ft * c;   // вдоль x̂ судна = (sin ψ, cos ψ)
        MooringHull.ApplyWorldForce(ship, -force * nE - tE, -force * nN - tN, bow);
        MooringHull.ApplyWorldForce(Tug.Model, force * nE + tE, force * nN + tN, bow);
        if (!contactReported)
        {
            contactReported = true;
            Events.Add(new TowEvent { Type = TowEventType.PushContact, Time = time, Value = rate });
        }
    }

    // ─────────── Автоматика буксира ───────────

    // 19.3б: рабочее направление тяги (от ДП, °) — поворачивается к заданному со скоростью SwingRateDegS в сторону,
    // где дуга вокруг клюза не проходит через судно (у кормы — не через «вперёд», у носа — не через «назад»).
    // При постоянной команде равно ей (стенд 19.3а не меняется). После работы у борта — с текущего пеленга буксира.
    private double swingDeg = double.NaN;
    private TugMode lastGeometry = TugMode.Pull;

    private double SwingDirection(ShipDynamicsModel ship, WorldPosition fairlead, WorldPosition bow, double commandDeg, double dt)
    {
        double psiS = ship != null ? ship.Psi : 0.0;
        if (double.IsNaN(swingDeg) || lastGeometry != TugMode.Pull)
        {
            double be = bow.East - fairlead.East, bn = bow.North - fairlead.North;
            swingDeg = lastGeometry != TugMode.Pull && be * be + bn * bn > 4.0 ? (Math.Atan2(be, bn) - psiS) * 180.0 / Math.PI : commandDeg;
        }
        lastGeometry = TugMode.Pull;
        double d = Math.IEEERemainder(commandDeg - swingDeg, 360.0);
        if (Math.Abs(d) < 1e-9) return swingDeg = commandDeg;
        // Запретное направление (дуга через корпус): у кормы — «вперёд» (0°), у носа — «назад» (180°)
        double forbidden = Station == TugStation.Stern ? 0.0 : Station == TugStation.Bow ? 180.0 : double.NaN;
        if (!double.IsNaN(forbidden))
        {
            double toForbidden = Math.IEEERemainder(forbidden - swingDeg, 360.0);
            if (Math.Sign(toForbidden) == Math.Sign(d) && Math.Abs(toForbidden) < Math.Abs(d)) d -= Math.Sign(d) * 360.0;
        }
        double step = SwingRateDegS * dt;
        swingDeg = Math.Abs(d) <= step ? commandDeg : swingDeg + Math.Sign(d) * step;
        return swingDeg;
    }

    private void Skipper(ShipDynamicsModel ship, double dt)
    {
        TugVessel tv = Tug;
        ShipDynamicsModel t = tv.Model;
        TugCommand cmd = tv.Command;
        bool stop = cmd.Mode == TugMode.Stop;
        TugMode geo = stop ? Geometry : cmd.Mode;   // 19.3б: «стоп» держит место последней работы
        Manoeuvring3DofCoefficients k = t.Coefficients3Dof;
        double bp = tv.Spec.BollardPull, p = stop ? 0.0 : Math.Max(0.0, Math.Min(1.0, cmd.Power));
        double psiS = ship != null ? ship.Psi : 0.0, rS = ship != null ? ship.R : 0.0;
        WorldPosition bow = tv.Bow;

        // Цель: точка носа буксира и его курс
        WorldPosition target;
        double psiT;
        GoingAround = false;
        if (geo == TugMode.Push && ship != null)
        {
            lastGeometry = TugMode.Push;
            int side = PushSide(cmd.DirectionDeg);
            double y = side * (MooringHull.HalfBeamAt(PushPointX, ShipParams.length, ShipParams.beam) + (stop ? StandoffM : 0.0));
            target = MooringHull.ToWorld(ship.East, ship.North, ship.Psi, PushPointX, y);
            psiT = psiS - side * Math.PI / 2.0;
            // 19.3б: нос буксира у другого борта — обход оконечности (вдоль своего борта за оконечность, затем на нужный борт)
            ShipAxes(ship, bow, out double xb, out double yb);
            if (Math.Sign(yb) != side)
            {
                double half = 0.5 * ShipParams.length;
                double xEnd = (xb >= 0.0 ? 1.0 : -1.0) * (half + AroundClearM);
                double ySide = Math.Abs(xb) < half + 5.0 ? (yb >= 0.0 ? 1.0 : -1.0) : side;
                target = MooringHull.ToWorld(ship.East, ship.North, ship.Psi, xEnd, ySide * (0.5 * ShipParams.beam + AroundClearM));
                GoingAround = true;
            }
        }
        else
        {
            WorldPosition f = TowPoint(ship);
            double rel = SwingDirection(ship, f, bow, cmd.DirectionDeg, dt) * Math.PI / 180.0;
            double theta = psiS + rel;
            double len = Line.Fast ? Line.RestLength : 30.0;
            target = new WorldPosition(f.East + Math.Sin(theta) * len, f.North + Math.Cos(theta) * len);
            psiT = theta + Math.PI;
        }

        // Ошибка положения носа и её скорость (относительно точки судна) — в осях буксира
        WorldPosition vb = MooringHull.PointVelocity(t, bow);
        WorldPosition vt = ship != null ? MooringHull.PointVelocity(ship, target) : new WorldPosition(0, 0);
        double s = Math.Sin(t.Psi), c = Math.Cos(t.Psi);
        double eE = bow.East - target.East, eN = bow.North - target.North;
        double ex = eE * s + eN * c, ey = eE * c - eN * s;
        double dvE = vb.East - vt.East, dvN = vb.North - vt.North;
        double vx = dvE * s + dvN * c, vy = dvE * c - dvN * s;
        double w = tv.Omega, z = tv.Zeta, wp = tv.OmegaHeading;
        double ePsi = Math.IEEERemainder(t.Psi - psiT, 2.0 * Math.PI);

        double X;
        double Y = k.M22 * (-w * w * ey - 2.0 * z * w * vy);
        double N = k.M33 * (-wp * wp * ePsi - 2.0 * z * wp * (t.R - rS));
        if (stop || GoingAround)
        {
            // 19.3б: «стоп» и обход — только положение (на тросе упор назад не даётся, у борта — без упора)
            X = k.M11 * (-w * w * ex - 2.0 * z * w * vx);
        }
        else if (cmd.Mode == TugMode.Pull)
        {
            bool taut = Line.Fast && Line.Length >= Line.RestLength - SlackM;   // слабее чем на 1 м — подтянуться регулятором
            X = taut ? -p * bp : k.M11 * (-w * w * ex - 2.0 * z * w * vx);
        }
        else
        {
            // Толкать: подойти к борту (цель — касание 0,05 м), в касании — упор P·T_гак (0 % — стоит у борта без упора).
            // В касании нос держит трение кранца: курс — только кормовой поперечной силой (поворот вокруг носа),
            // Y = N/x_к — без регулятора положения (он требовал бы от той же силы противоположного)
            bool touching = PushForce > 0.0 || ex > -0.3;
            X = touching ? p * bp : k.M11 * (-w * w * (ex - 0.05) - 2.0 * z * w * vx);
            if (touching && PushForce > 0.0)
            {
                Y = N / tv.Spec.ThrusterX;
                N = tv.Spec.ThrusterX * Y;
            }
        }
        tv.OrderX = Clamp(X, bp);
        tv.OrderY = Clamp(Y, bp);
        tv.OrderN = Clamp(N, bp * Math.Abs(tv.Spec.ThrusterX));
    }

    private static double Clamp(double v, double m) => v < -m ? -m : v > m ? m : v;

    /// <summary>Точка в связанных осях судна: x — к носу от миделя, y — к ПБ, м.</summary>
    public static void ShipAxes(ShipDynamicsModel ship, WorldPosition p, out double x, out double y)
    {
        double s = Math.Sin(ship.Psi), c = Math.Cos(ship.Psi);
        double dE = p.East - ship.East, dN = p.North - ship.North;
        x = dE * s + dN * c;
        y = dE * c - dN * s;
    }
}
