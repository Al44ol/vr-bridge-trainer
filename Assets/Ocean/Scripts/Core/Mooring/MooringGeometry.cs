// ============================================================
// MooringGeometry.cs — Причал и корпус для швартовки (запрос 19.2, часть Core)
//
// Причал (BerthArea) — прямая кромка (кордон) от A до B в координатах восток/север.
// Локальные оси причала: t — вдоль кромки от A (м), n — от кромки в сторону воды (м);
// вода — справа от направления A→B. На кромке: кранцы (цилиндрические, подвешены вдоль
// кромки, выступают на D_н — наружный диаметр) с шагом s_к и кнехты на s_кн, в b_кн м от кромки.
//
// Корпус (MooringHull) в связанных осях (x — в нос от миделя, y — на правый борт):
// шестиугольник L × B — цилиндрическая вставка от кормы до x_пл = 0,25·L, дальше
// сужение к форштевню (L/2, 0). Корма — прямая (транец). Для контакта с кранцами и
// причалом и для точек клюзов концов (полуширина на x).
//
// Сила в мировых осях в точке корпуса → связанные оси модели (ShipDynamicsModel.AddForce):
//   f̂ = (sin ψ, cos ψ) — в нос, ŝ = (cos ψ, −sin ψ) — на правый борт;
//   F_x = F·f̂, F_y = F·ŝ; точка: x = d·f̂, y = d·ŝ (d — от точки судна);
//   момент N = x·F_y − y·F_x (> 0 — нос вправо).
// Скорость точки корпуса (поворот по часовой с r > 0): v = V_судна + (r·d_N, −r·d_E).
// ============================================================
using System;
using System.Collections.Generic;

/// <summary>Причал: кромка, кранцы, кнехты (данные — в WaterArea, стенд — свой).</summary>
[Serializable]
public class BerthArea
{
    public string name = "Причал";
    /// <summary>Начало и конец кромки причала; вода — справа от A→B.</summary>
    public WorldPosition a, b;
    /// <summary>Высота кромки над водой, м.</summary>
    public float quayHeightM = 2.5f;
    /// <summary>Глубина у причала, м.</summary>
    public float depthM = 6f;

    // ── Кранцы (цилиндрические, допущение по каталогам — см. ShipParameters_Sources.md, разд. 19.2) ──
    /// <summary>Наружный диаметр = вылет от кромки, м.</summary>
    public float fenderOuterDiameterM = 0.5f;
    /// <summary>Номинальное обжатие δ_н, м (у цилиндрического ≈ 0,5·D_н).</summary>
    public float fenderRatedDeflectionM = 0.25f;
    /// <summary>Номинальная реакция R_н при δ_н, кН.</summary>
    public float fenderRatedReactionKn = 300f;
    /// <summary>Длина кранца вдоль кромки, м.</summary>
    public float fenderLengthM = 1.5f;
    /// <summary>Наибольший шаг кранцев, м (расставляются равномерно); первый и последний — в fenderEndMarginM от концов.</summary>
    public float fenderSpacingM = 8f;
    public float fenderEndMarginM = 4f;
    /// <summary>Коэффициент трения корпуса о кранец вдоль кромки.</summary>
    public float fenderFriction = 0.3f;

    // ── Кнехты ──
    public float bollardSpacingM = 10f;
    public float bollardEndMarginM = 3f;
    /// <summary>Кнехт — в b_кн м от кромки на причале.</summary>
    public float bollardSetbackM = 1f;

    public double Length => (b - a).Magnitude;

    /// <summary>Единичный вектор вдоль кромки (A→B).</summary>
    public WorldPosition TAxis
    {
        get { WorldPosition d = b - a; double l = Math.Max(1e-9, d.Magnitude); return new WorldPosition(d.East / l, d.North / l); }
    }

    /// <summary>Единичная нормаль к кромке в сторону воды (справа от A→B).</summary>
    public WorldPosition NAxis { get { WorldPosition t = TAxis; return new WorldPosition(t.North, -t.East); } }

    /// <summary>Курс вдоль кромки A→B, градусы (0 = N).</summary>
    public double BearingDeg
    {
        get { WorldPosition t = TAxis; double d = Math.Atan2(t.East, t.North) * 180.0 / Math.PI; return d < 0 ? d + 360.0 : d; }
    }

    public void ToLocal(WorldPosition p, out double t, out double n)
    {
        WorldPosition d = p - a, ta = TAxis, na = NAxis;
        t = d.East * ta.East + d.North * ta.North;
        n = d.East * na.East + d.North * na.North;
    }

    public WorldPosition ToWorld(double t, double n)
    {
        WorldPosition ta = TAxis, na = NAxis;
        return new WorldPosition(a.East + t * ta.East + n * na.East, a.North + t * ta.North + n * na.North);
    }

    private static List<double> Positions(double length, double margin, double spacing)
    {
        var list = new List<double>();
        if (length <= 0) return list;
        double m = Math.Min(margin, 0.5 * length);
        double usable = length - 2 * m;
        // spacing — наибольший шаг: промежутков — с округлением вверх
        int n = spacing > 0 ? (int)Math.Ceiling(usable / spacing - 1e-9) + 1 : 1;
        double step = n > 1 ? usable / (n - 1) : 0;
        for (int i = 0; i < n; i++) list.Add(n > 1 ? m + i * step : 0.5 * length);
        return list;
    }

    /// <summary>Положения кранцев вдоль кромки t, м (равномерно, концы — с отступом).</summary>
    public List<double> FenderPositions() => Positions(Length, fenderEndMarginM, fenderSpacingM);

    /// <summary>Положения кнехтов вдоль кромки t, м.</summary>
    public List<double> BollardPositions() => Positions(Length, bollardEndMarginM, bollardSpacingM);

    /// <summary>Кнехт i в мировых координатах.</summary>
    public WorldPosition BollardWorld(int i)
    {
        List<double> bp = BollardPositions();
        return ToWorld(bp[Math.Max(0, Math.Min(bp.Count - 1, i))], -bollardSetbackM);
    }

    /// <summary>Ближайший к t кнехт.</summary>
    public int NearestBollard(double t)
    {
        List<double> bp = BollardPositions();
        int best = 0;
        for (int i = 1; i < bp.Count; i++) if (Math.Abs(bp[i] - t) < Math.Abs(bp[best] - t)) best = i;
        return best;
    }

    public FenderSpec Fender => new FenderSpec(fenderOuterDiameterM, fenderRatedDeflectionM, fenderRatedReactionKn * 1000.0, fenderLengthM);

    /// <summary>Энергия навала по PIANC (2002), Дж: E = ½·m·v²·C_M·C_E·C_S·C_C; C_M = 1 + 2d/B, C_S = C_C = 1.</summary>
    public static double PiancBerthingEnergy(double massKg, double draft, double beam, double vNormal, double eccentricity = 1.0) =>
        0.5 * massKg * vNormal * vNormal * (1.0 + 2.0 * draft / Math.Max(0.1, beam)) * eccentricity;

    /// <summary>
    /// Причал «под судно» для стенда: кранцы подобраны по энергии навала (PIANC: скорость 0,15 м/с,
    /// C_E = 0,5, коэффициент аварийного навала 2; геометрическое подобие кранца, E ∝ D_н³),
    /// кнехты — через 0,2·L. Кромка — от (0, 0) на восток, вода — к югу (n < 0 по северу).
    /// </summary>
    public static BerthArea ForShip(ShipParameters p, double lengthFactor = 1.6)
    {
        double mass = ShipMass(p);
        double eDesign = 2.0 * PiancBerthingEnergy(mass, p.draft, p.beam, 0.15, 0.5);
        FenderSpec f = FenderSpec.ForEnergy(eDesign, Math.Max(1.0, 0.035 * p.length));
        double len = lengthFactor * p.length;
        return new BerthArea
        {
            name = "Причал стенда",
            a = new WorldPosition(0, 0),
            b = new WorldPosition(len, 0),
            quayHeightM = p.length > 100 ? 3f : 2.5f,
            depthM = (float)(p.draft + 3.0),
            fenderOuterDiameterM = (float)f.OuterDiameter,
            fenderRatedDeflectionM = (float)f.RatedDeflection,
            fenderRatedReactionKn = (float)(f.RatedReaction / 1000.0),
            fenderLengthM = (float)f.Length,
            fenderSpacingM = (float)Math.Max(6.0, Math.Round(0.15 * p.length)),
            fenderEndMarginM = 4f,
            bollardSpacingM = (float)Math.Max(8.0, Math.Round(0.2 * p.length)),
            bollardEndMarginM = 3f,
            bollardSetbackM = 1f
        };
    }

    /// <summary>Водоизмещение, кг: ρ·L·B·d·C_B.</summary>
    public static double ShipMass(ShipParameters p) => 1025.0 * p.length * p.beam * p.draft * p.blockCoefficient;
}

/// <summary>Контур корпуса для швартовки и действия сил в точке корпуса.</summary>
public static class MooringHull
{
    /// <summary>Начало сужения к носу от миделя, доля L.</summary>
    public const double ParallelForwardL = 0.25;

    /// <summary>Шестиугольник в связанных осях (x, y): корма-ЛБ, корма-ПБ, плечо-ПБ, нос, плечо-ЛБ.</summary>
    public static (double x, double y)[] Outline(double length, double beam)
    {
        double h = 0.5 * length, w = 0.5 * beam, xs = ParallelForwardL * length;
        return new[] { (-h, -w), (-h, w), (xs, w), (h, 0.0), (xs, -w) };
    }

    /// <summary>Полуширина корпуса на x (0 вне длины).</summary>
    public static double HalfBeamAt(double x, double length, double beam)
    {
        double h = 0.5 * length, w = 0.5 * beam, xs = ParallelForwardL * length;
        if (x < -h || x > h) return 0.0;
        if (x <= xs) return w;
        return w * (h - x) / Math.Max(1e-9, h - xs);
    }

    /// <summary>Точка корпуса (x, y связанных осей) в мировых координатах.</summary>
    public static WorldPosition ToWorld(double east, double north, double psi, double x, double y)
    {
        double s = Math.Sin(psi), c = Math.Cos(psi);
        return new WorldPosition(east + x * s + y * c, north + x * c - y * s);
    }

    /// <summary>Скорость точки корпуса относительно грунта, м/с (восток, север).</summary>
    public static WorldPosition PointVelocity(ShipDynamicsModel m, WorldPosition point)
    {
        double dE = point.East - m.East, dN = point.North - m.North;
        return new WorldPosition(m.VelocityEast + m.R * dN, m.VelocityNorth - m.R * dE);
    }

    /// <summary>Сила (fE, fN, Н) в мировой точке point → модель (связанные оси, момент с обоими плечами).</summary>
    public static void ApplyWorldForce(ShipDynamicsModel m, double fE, double fN, WorldPosition point)
    {
        double s = Math.Sin(m.Psi), c = Math.Cos(m.Psi);
        double fx = fE * s + fN * c, fy = fE * c - fN * s;
        double dE = point.East - m.East, dN = point.North - m.North;
        double x = dE * s + dN * c, y = dE * c - dN * s;
        m.AddForce(fx, fy, x);
        m.AddMoment(-y * fx);
    }
}
