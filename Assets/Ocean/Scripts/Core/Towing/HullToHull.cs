// ============================================================
// HullToHull.cs — Контакт буксира с судном: носовой кранец и корпус к корпусу (запрос 19.4б-1, Core)
//
// Вынесено из TowingSystem (19.3а — кранец, 19.3б-2 — корпуса) без изменений арифметики: тот же расчёт
// работает и для вызываемого буксира (TowingSystem), и для своего буксира с судном-клиентом (TugWorkSystem, 19.4б).
//
// 1. Кранец носа буксира (точка гака/кранца на носу, TugSpec.BowX): нос внутри контура корпуса судна (MooringHull)
//    на глубину δ — сила F = k·δ + c·δ̇ (δ̇ > 0) по нормали к борту, k = TugSpec.FenderStiffness (1,5 МН/м),
//    c = FenderDamping (0,3 МН·с/м); трение вдоль борта F_т = −μ·F·th(v_т/0,05 м/с), μ = FenderFriction (0,6).
//    Начало касания (после отхода больше 0,5 м) — событие: скорость сближения носа с бортом по нормали,
//    угол буксира к борту (отклонение его ДП от перпендикуляра к борту, °), место касания от миделя судна, борт.
// 2. Корпус к корпусу: точки контуров обоих через HullSampleM; точка одного корпуса внутри другого заходит на
//    pen = min(полуширина − |y|, L/2 − |x|); F = k·pen + c·v_сбл, трение μ·F; нос буксира ближе HullBowSkipM к
//    форштевню — не здесь (это кранец). 19.4б: начало касания корпусом и наибольшая скорость сближения — для оценки.
// ============================================================
using System;
using System.Collections.Generic;

public sealed class HullToHull
{
    public const double HullStiffness = 0.6e6, HullDamping = 0.3e6, HullFriction = 0.3, HullSampleM = 2.0, HullBowSkipM = 2.5;

    /// <summary>Сила носового кранца на последнем шаге, Н (0 — нет касания).</summary>
    public double FenderForce { get; private set; }
    /// <summary>Сила «корпус к корпусу» на последнем шаге (сумма по точкам), Н.</summary>
    public double HullForce { get; private set; }

    // Последнее начало касания кранцем
    /// <summary>Скорость сближения носа с бортом по нормали, м/с (≥ 0).</summary>
    public double ContactClosingMs { get; private set; }
    /// <summary>Отклонение ДП буксира от перпендикуляра к борту, ° (0 — носом точно поперёк борта).</summary>
    public double ContactAngleDeg { get; private set; }
    /// <summary>Место касания от миделя судна, м (> 0 — к носу).</summary>
    public double ContactX { get; private set; }
    /// <summary>19.4г: место и борт упора кранцем на последнем шаге (пока FenderForce > 0), м от миделя (> 0 — к носу).</summary>
    public double TouchX { get; private set; }
    public int TouchSide { get; private set; }
    /// <summary>Борт касания: +1 — ПБ, −1 — ЛБ.</summary>
    public int ContactSide { get; private set; }

    /// <summary>19.4б: касание корпусом началось на этом шаге; наибольшая скорость сближения точек на шаге, м/с.</summary>
    public bool HullContactStarted { get; private set; }
    public double HullClosingMs { get; private set; }

    private double prevPen = double.NaN;
    private bool contactReported, hullWasTouching;
    private List<(double x, double y)> shipPoints, tugPoints;
    private ShipParameters shipPointsFor, tugPointsFor;

    public void Reset()
    {
        prevPen = double.NaN;
        contactReported = false;
        hullWasTouching = false;
        FenderForce = HullForce = 0.0;
    }

    /// <summary>Кранец носа буксира о борт судна (силы — в обе модели). true — касание началось на этом шаге.</summary>
    public bool Fender(ShipDynamicsModel ship, ShipParameters shipParams, ShipDynamicsModel tug, TugSpec spec, WorldPosition bow, double dt)
    {
        FenderForce = 0.0;
        double s = Math.Sin(ship.Psi), c = Math.Cos(ship.Psi);
        double dE = bow.East - ship.East, dN = bow.North - ship.North;
        double x = dE * s + dN * c, y = dE * c - dN * s;   // связанные оси судна
        double half = MooringHull.HalfBeamAt(x, shipParams);
        double pen = half - Math.Abs(y);
        if (half <= 0.0 || pen <= 0.0)
        {
            prevPen = double.NaN;
            if (pen < -0.5) contactReported = false;
            return false;
        }
        double rate = double.IsNaN(prevPen) ? 0.0 : (pen - prevPen) / Math.Max(1e-6, dt);
        prevPen = pen;
        double force = Math.Max(0.0, spec.FenderStiffness * pen + spec.FenderDamping * Math.Max(0.0, rate));
        FenderForce = force;
        TouchX = x;   // 19.4г: где упирается кранец сейчас (зона конца)
        TouchSide = y >= 0.0 ? 1 : -1;
        int side = y >= 0.0 ? 1 : -1;
        // Нормаль борта наружу в мировых осях: ŝ = (cos ψ, −sin ψ) — на ПБ
        double nE = side * c, nN = -side * s;
        // Трение кранца вдоль борта (ось x судна): F_т = −μ·F·th(v_т/0,05 м/с) — нос буксира не скользит по борту
        WorldPosition vb = MooringHull.PointVelocity(tug, bow), vs = MooringHull.PointVelocity(ship, bow);
        double vt = (vb.East - vs.East) * s + (vb.North - vs.North) * c;
        double ft = -spec.FenderFriction * force * Math.Tanh(vt / 0.05);
        double tE = ft * s, tN = ft * c;   // вдоль x̂ судна = (sin ψ, cos ψ)
        MooringHull.ApplyWorldForce(ship, -force * nE - tE, -force * nN - tN, bow);
        MooringHull.ApplyWorldForce(tug, force * nE + tE, force * nN + tN, bow);
        if (contactReported) return false;
        contactReported = true;
        // 19.3в: скорость сближения носа буксира с бортом по нормали (было — прирост обжатия, на первом шаге касания 0)
        double closing = -((vb.East - vs.East) * nE + (vb.North - vs.North) * nN);
        ContactClosingMs = Math.Max(0.0, closing);
        // 19.4б: ДП буксира (к носу) против нормали борта внутрь: 0° — поперёк борта
        double cosA = -(Math.Sin(tug.Psi) * nE + Math.Cos(tug.Psi) * nN);
        ContactAngleDeg = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cosA))) * 180.0 / Math.PI;
        ContactX = x;
        ContactSide = side;
        return true;
    }

    /// <summary>Корпус к корпусу (кроме носового кранца буксира в bowX); силы — в обе модели.</summary>
    public void Hulls(ShipDynamicsModel ship, ShipParameters shipParams, ShipDynamicsModel tug, double bowX)
    {
        HullForce = 0.0;
        HullClosingMs = 0.0;
        HullContactStarted = false;
        ShipParameters tp = tug.Parameters;
        if (shipParams == null || tp == null) { hullWasTouching = false; return; }
        double dE = tug.East - ship.East, dN = tug.North - ship.North;
        double reach = 0.5 * (shipParams.length + tp.length) + 2.0;
        if (dE * dE + dN * dN > reach * reach) { hullWasTouching = false; return; }
        if (shipPoints == null || shipPointsFor != shipParams)
        {
            shipPoints = Perimeter(MooringHull.Outline(shipParams), HullSampleM, double.NaN, 0.0);
            shipPointsFor = shipParams;
        }
        if (tugPoints == null || tugPointsFor != tp)
        {
            tugPoints = Perimeter(MooringHull.Outline(tp), HullSampleM, bowX, HullBowSkipM);
            tugPointsFor = tp;
        }
        foreach ((double x, double y) in tugPoints)
            Penetrate(tug, ship, shipParams, MooringHull.ToWorld(tug.East, tug.North, tug.Psi, x, y));
        foreach ((double x, double y) in shipPoints)
            Penetrate(ship, tug, tp, MooringHull.ToWorld(ship.East, ship.North, ship.Psi, x, y));
        bool touching = HullForce > 0.0;
        HullContactStarted = touching && !hullWasTouching;
        hullWasTouching = touching;
    }

    private static List<(double x, double y)> Perimeter((double x, double y)[] outline, double step, double skipNearX, double skipM)
    {
        var pts = new List<(double x, double y)>();
        for (int i = 0; i < outline.Length; i++)
        {
            (double ax, double ay) = outline[i];
            (double bx, double by) = outline[(i + 1) % outline.Length];
            double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            int n = Math.Max(1, (int)Math.Ceiling(len / step));
            for (int k = 0; k < n; k++)
            {
                double u = k / (double)n, x = ax + (bx - ax) * u, y = ay + (by - ay) * u;
                if (!double.IsNaN(skipNearX) && Math.Sqrt((x - skipNearX) * (x - skipNearX) + y * y) < skipM) continue;
                pts.Add((x, y));
            }
        }
        return pts;
    }

    // Точка p корпуса a внутри корпуса b (параметры pb): сила на a — наружу из b, на b — обратная
    private void Penetrate(ShipDynamicsModel a, ShipDynamicsModel b, ShipParameters pb, WorldPosition p)
    {
        TowingSystem.ShipAxes(b, p, out double x, out double y);
        double half = MooringHull.HalfBeamAt(x, pb);
        if (half <= 0.0) return;
        double penLat = half - Math.Abs(y);
        if (penLat <= 0.0) return;
        double penLong = 0.5 * pb.length - Math.Abs(x);
        double s = Math.Sin(b.Psi), c = Math.Cos(b.Psi);
        double nE, nN, pen;
        if (penLat <= penLong) { double sy = y >= 0.0 ? 1.0 : -1.0; nE = sy * c; nN = -sy * s; pen = penLat; }   // к борту
        else { double sx = x >= 0.0 ? 1.0 : -1.0; nE = sx * s; nN = sx * c; pen = penLong; }                      // к оконечности
        WorldPosition va = MooringHull.PointVelocity(a, p), vb = MooringHull.PointVelocity(b, p);
        double rvE = va.East - vb.East, rvN = va.North - vb.North;
        double vn = rvE * nE + rvN * nN;                   // > 0 — расходятся
        double f = Math.Max(0.0, HullStiffness * pen + HullDamping * Math.Max(0.0, -vn));
        double tE = -nN, tN = nE;                          // касательная
        double ft = -HullFriction * f * Math.Tanh((rvE * tE + rvN * tN) / 0.05);
        double fE = f * nE + ft * tE, fN = f * nN + ft * tN;
        MooringHull.ApplyWorldForce(a, fE, fN, p);
        MooringHull.ApplyWorldForce(b, -fE, -fN, p);
        HullForce += f;
        if (-vn > HullClosingMs) HullClosingMs = -vn;
    }
}
