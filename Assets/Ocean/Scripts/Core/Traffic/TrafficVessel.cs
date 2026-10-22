// ============================================================
// TrafficVessel.cs — Другое судно: модель движения + авторулевой (B3)
//
// Чистый класс (сборка VRBridge.Core): TrafficController вызывает Step в
// FixedUpdate (Δt = 0,02 с), тесты и офлайн-прогоны — без сцены.
// Движение — тот же ShipDynamicsModel, что у своего судна, со своими
// ShipParameters: поведение целей физически согласовано с нашим судном
// (рулевая машина, инерция, циркуляция, потеря скорости по параметрам).
// Скорость задаётся телеграфом: SF = v_зад/V_эксп (0…1); старт — сразу
// с заданной скоростью (Teleport), без разгона.
// Курс — HeadingAutopilot; заданный курс — постоянный или от RouteFollower.
// После столкновения цель ставит «Стоп» (EngineStopped). 21.6а: приказ триггера — Order (курс, скорость).
// Состояние для записи (targets.csv) — через ITrackedTarget (C1).
// ============================================================
using System;

public sealed class TrafficVessel : ITrackedTarget
{
    public readonly int Index;
    public readonly TrafficShipDefinition Definition;
    public readonly ShipDynamicsModel Model;
    public readonly HeadingAutopilot Autopilot = new HeadingAutopilot();
    public RouteFollower Route { get; private set; }
    public TrafficStart Start { get; private set; }

    public string Name => Definition != null ? Definition.name : TargetId;
    public TrafficVesselType Type => Definition != null ? Definition.type : TrafficVesselType.Cargo;
    /// <summary>Заданный курс (при постоянном курсе), °.</summary>
    public double OrderedCourseDeg;
    /// <summary>Заданная скорость, уз.</summary>
    public double OrderedSpeedKnots;
    /// <summary>Машина остановлена (после столкновения или отладочная цель).</summary>
    public bool EngineStopped;

    // Предыдущий шаг — для интерполяции отображения
    public double PrevEast, PrevNorth, PrevPsi;

    public TrafficVessel(int index, string targetId, TrafficShipDefinition definition, ShipParameters parameters, TrafficStart start)
    {
        Index = index;
        TargetId = string.IsNullOrEmpty(targetId) ? $"T{index + 1}" : targetId;
        Definition = definition;
        Model = new ShipDynamicsModel(parameters);
        Reset(start);
    }

    /// <summary>Поставить в начальное состояние (брифинг, «Начать»).</summary>
    public void Reset(TrafficStart start)
    {
        Start = start;
        OrderedCourseDeg = start.HeadingDeg;
        OrderedSpeedKnots = start.SpeedKnots;
        EngineStopped = false;
        Model.Teleport(start.Position.East, start.Position.North, start.HeadingDeg * Math.PI / 180.0,
                       start.SpeedKnots * ShipParameters.KnotsToMs, 0.0);
        Model.RudderCommandDeg = 0.0;
        Model.SpeedFactor = SpeedFactor;
        Route = start.Path != null && start.Path.Count > 0 ? new RouteFollower(start.Position, start.Path) : null;
        KeepPrevious();
    }

    /// <summary>Положение телеграфа для заданной скорости: SF = v/V_эксп, 0…1.</summary>
    public double SpeedFactor
    {
        get
        {
            if (EngineStopped) return 0.0;
            double v = Model.Parameters.serviceSpeedKnots;
            return v > 1e-3 ? Math.Max(0.0, Math.Min(1.0, OrderedSpeedKnots / v)) : 0.0;
        }
    }

    /// <summary>Запомнить состояние как «предыдущее» (перед шагом и на паузе).</summary>
    public void KeepPrevious()
    {
        PrevEast = Model.East; PrevNorth = Model.North; PrevPsi = Model.Psi;
    }

    /// <summary>
    /// 21.6а: приказ по триггеру сценария — курс, ° (NaN — не менять; заданный курс снимает маршрут —
    /// дальше постоянным курсом) и скорость, уз (NaN — не менять; 0 — стоп; машина снова работает).
    /// </summary>
    public void Order(double courseDeg, double speedKnots)
    {
        if (!double.IsNaN(courseDeg))
        {
            Route = null;
            OrderedCourseDeg = ((courseDeg % 360.0) + 360.0) % 360.0;
        }
        if (!double.IsNaN(speedKnots))
        {
            OrderedSpeedKnots = Math.Max(0.0, speedKnots);
            EngineStopped = false;
        }
    }

    /// <summary>Курс, который держит авторулевой сейчас, °.</summary>
    public double CourseOrder => Route != null ? Route.Course(Model) : OrderedCourseDeg;

    public void Step(double dt)
    {
        KeepPrevious();
        Model.RudderCommandDeg = Autopilot.Rudder(Model, CourseOrder);
        Model.SpeedFactor = SpeedFactor;
        Model.Step(dt);
    }

    /// <summary>Контур корпуса L × B (середина корпуса — в точке позиции).</summary>
    public HullFootprint Footprint =>
        HullFootprint.FromShip(Model.East, Model.North, Model.Psi, 0.0, 0.0,
                               Math.Max(1.0, Model.Parameters.length), Math.Max(0.5, Model.Parameters.beam));

    /// <summary>Скорость (восток, север), м/с.</summary>
    public void Velocity(out double ve, out double vn)
    {
        ve = Model.U * Math.Sin(Model.Psi);
        vn = Model.U * Math.Cos(Model.Psi);
    }

    // ── ITrackedTarget (C1, targets.csv) ──
    public string TargetId { get; }
    public WorldPosition Position => new WorldPosition(Model.East, Model.North);
    public double HeadingDeg => Model.Psi * 180.0 / Math.PI;
    public double SpeedKnots => Model.U / ShipParameters.KnotsToMs;
}

/// <summary>
/// Контакт корпусов своего судна и цели: пересечение контуров L × B
/// (HullFootprint, Polygon2D.PolygonDistance — как контроль мели B1).
/// Новый контакт — при D = 0, если до этого суда разошлись больше чем на гистерезис
/// (одно событие на одно столкновение, без «дребезга» при касании).
/// </summary>
public sealed class HullContact
{
    /// <summary>Расстояние расхождения, после которого возможен новый контакт, м.</summary>
    public double SeparationHysteresis = 5.0;

    public bool InContact { get; private set; }
    /// <summary>Расстояние между контурами на последнем обновлении, м (0 — пересечение).</summary>
    public double Distance { get; private set; } = double.PositiveInfinity;

    private readonly WorldPosition[] a = new WorldPosition[4], b = new WorldPosition[4];

    public void Reset()
    {
        InContact = false;
        Distance = double.PositiveInfinity;
    }

    /// <summary>Обновить; true — начался новый контакт (столкновение).</summary>
    public bool Update(in HullFootprint own, in HullFootprint other)
    {
        // Быстрый отсев по описанным окружностям
        double ro = 0.5 * Math.Sqrt(own.Length * own.Length + own.Beam * own.Beam);
        double rt = 0.5 * Math.Sqrt(other.Length * other.Length + other.Beam * other.Beam);
        double cd = (other.Center - own.Center).Magnitude;
        if (cd - ro - rt > SeparationHysteresis + 1.0)
        {
            Distance = cd - ro - rt;
            InContact = false;
            return false;
        }
        own.GetCorners(a);
        other.GetCorners(b);
        Distance = Polygon2D.PolygonDistance(a, b);
        if (Distance <= 0.0)
        {
            if (InContact) return false;
            InContact = true;
            return true;
        }
        if (Distance > SeparationHysteresis) InContact = false;
        return false;
    }
}
