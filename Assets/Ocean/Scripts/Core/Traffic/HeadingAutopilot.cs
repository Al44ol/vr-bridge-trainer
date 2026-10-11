// ============================================================
// HeadingAutopilot.cs — Авторулевой целей: курс и путевые точки (B3)
//
// 1. Регулятор курса (ПД): δ = K_p·(ψ_зад − ψ) − K_d·r,  |δ| ≤ δ_max
//    (δ, ψ — градусы, r — °/с, K_d — с).
//    Коэффициенты — размещением полюсов замкнутой системы с моделью Номото
//    T·ṙ + r = K·δ (для Норрбина — линеаризация при r = 0):
//        T·ψ̈ + (1 + K·K_d)·ψ̇ + K·K_p·ψ = K·K_p·ψ_зад,
//        ω_n² = K·K_p/T,   2ζ·ω_n = (1 + K·K_d)/T,
//        K_p = T·ω_n²/K,   K_d = (2ζ·ω_n·T − 1)/K,
//    K = K'·U/L, T = T'·L/U (как в ShipDynamicsModel), собственная частота
//    в безразмерном виде ω_n' = ω_n·L/U (по умолчанию 0,75), ζ = 0,7.
//    Для судна сцены (Model45m, 15 уз) это даёт K_p = 1,41, K_d = 8,0 с — тот же
//    регулятор, что в тестах B1/B2 (1,5 и 8 с); для Mariner — 0,64 и 19 с,
//    для KCS — 0,39 и 9,5 с. Один авторулевой работает для судов разных размеров.
//    На малом ходу коэффициенты считаются по U ≥ 0,25·V_эксп (без «взрыва» K_p).
//
// 2. Следование по путевым точкам (LOS, line of sight — как авторулевой тестов B2):
//        χ_зад = χ_уч − arctg(XTE/Δ),  Δ = max(Δ_min, n_Δ·L)  (по умолчанию 150 м и 3L);
//    XTE > 0 — правее линии пути.
//    Упреждение поворота (точка начала перекладки, wheel-over):
//        d_упр = R·tg(Δχ/2) + U·δ_пов/ω_δ,
//    R = U/r_уст(δ_пов) — радиус установившейся циркуляции при руле δ_пов (20°),
//    второй член — путь за время перекладки руля. Для судна сцены (поворот 60°,
//    15 уз) d_упр ≈ 160 м — близко к найденному в B2 оптимуму 150 м (табл. 6.4).
//    После последней точки цель держит курс последнего участка.
// ============================================================
using System;
using System.Collections.Generic;

public sealed class HeadingAutopilot
{
    /// <summary>Безразмерная собственная частота замкнутой системы ω_n' = ω_n·L/U.</summary>
    public double OmegaPrime = 0.75;
    /// <summary>Относительное затухание замкнутой системы ζ.</summary>
    public double Zeta = 0.7;
    /// <summary>Наименьшая расчётная скорость для коэффициентов, доля V_эксп.</summary>
    public double MinDesignSpeedFraction = 0.25;

    /// <summary>Последние коэффициенты: K_p (°/°) и K_d (с).</summary>
    public double Kp { get; private set; }
    public double Kd { get; private set; }

    private const double Deg = 180.0 / Math.PI;

    /// <summary>Коэффициенты ПД-регулятора по параметрам судна и скорости u (м/с).</summary>
    public static void Gains(ShipParameters p, double u, double omegaPrime, double zeta, double minSpeedFraction,
                             out double kp, out double kd)
    {
        double L = Math.Max(1.0, p.length);
        double U = Math.Max(Math.Abs(u), Math.Max(0.1, minSpeedFraction * p.ServiceSpeedMs));
        double K = Math.Max(1e-6, p.nomotoKPrime * U / L);   // 1/с на радиан руля
        double T = Math.Max(1e-3, p.nomotoTPrime * L / U);
        double wn = omegaPrime * U / L;
        kp = T * wn * wn / K;
        kd = Math.Max(0.0, (2.0 * zeta * wn * T - 1.0) / K);
    }

    /// <summary>Команда руля (градусы, > 0 — ПБ) для удержания курса courseDeg.</summary>
    public double Rudder(ShipDynamicsModel m, double courseDeg)
    {
        ShipParameters p = m.Parameters;
        Gains(p, m.U, OmegaPrime, Zeta, MinDesignSpeedFraction, out double kp, out double kd);
        Kp = kp; Kd = kd;
        double err = Math.IEEERemainder(courseDeg / Deg - m.Psi, 2.0 * Math.PI) * Deg;
        double delta = kp * err - kd * m.R * Deg;
        double max = p.maxRudderAngle;
        return delta < -max ? -max : delta > max ? max : delta;
    }
}

/// <summary>Следование по путевым точкам (LOS с упреждением поворота).</summary>
public sealed class RouteFollower
{
    /// <summary>Точки: [0] — начальная позиция, далее — путь.</summary>
    public readonly List<WorldPosition> Points = new List<WorldPosition>();
    /// <summary>Активный участок: Points[Leg] → Points[Leg + 1].</summary>
    public int Leg { get; private set; }
    /// <summary>Последняя точка пройдена — цель держит курс последнего участка.</summary>
    public bool Finished { get; private set; }
    public double FinalCourseDeg { get; private set; }
    /// <summary>XTE на активном участке, м (> 0 — правее).</summary>
    public double Xte { get; private set; }

    public double LookaheadLengths = 3.0;
    public double MinLookahead = 150.0;
    /// <summary>Руль, по которому считается радиус поворота для упреждения, °.</summary>
    public double TurnRudderDeg = 20.0;

    private const double Deg = 180.0 / Math.PI;

    public RouteFollower(WorldPosition start, IList<WorldPosition> path)
    {
        Points.Add(start);
        if (path != null)
            foreach (WorldPosition p in path)
                if ((p - Points[Points.Count - 1]).Magnitude > 1.0) Points.Add(p);
        Finished = Points.Count < 2;
    }

    /// <summary>Заданный курс (градусы) для текущего положения судна.</summary>
    public double Course(ShipDynamicsModel m)
    {
        if (Finished) return FinalCourseDeg;
        var pos = new WorldPosition(m.East, m.North);
        double u = Math.Abs(m.U);
        while (true)
        {
            WorldPosition a = Points[Leg], b = Points[Leg + 1];
            double legCourse = CourseOf(a, b);
            WorldPosition d = b - a;
            double len = d.Magnitude;
            double atd = ((pos.East - a.East) * d.East + (pos.North - a.North) * d.North) / len;
            double remaining = len - atd;
            if (Leg + 2 < Points.Count)
            {
                double next = CourseOf(b, Points[Leg + 2]);
                double turn = Math.Abs(Math.IEEERemainder(next - legCourse, 360.0));
                if (remaining <= WheelOverDistance(m.Parameters, u, turn, TurnRudderDeg)) { Leg++; continue; }
            }
            else if (remaining <= 0.0)
            {
                Finished = true;
                FinalCourseDeg = legCourse;
                Xte = 0.0;
                return FinalCourseDeg;
            }

            Xte = RouteTracker.CrossTrack(a, b, pos);
            double lookahead = Math.Max(MinLookahead, LookaheadLengths * Math.Max(1.0, m.Parameters.length));
            return legCourse - Math.Atan(Xte / lookahead) * Deg;
        }
    }

    /// <summary>
    /// Расстояние упреждения поворота, м: R·tg(Δχ/2) + U·δ_пов/ω_δ,
    /// R = U/r_уст(δ_пов) (ShipDynamicsModel.SteadyYawRate).
    /// </summary>
    public static double WheelOverDistance(ShipParameters p, double u, double turnDeg, double rudderDeg)
    {
        if (u < 0.05 || turnDeg < 0.5) return 0.0;
        double r = Math.Abs(ShipDynamicsModel.SteadyYawRate(p, u, rudderDeg));
        double radius = r > 1e-9 ? u / r : 1e6;
        double half = Math.Min(Math.Abs(turnDeg), 170.0) * 0.5 / Deg;
        double lag = p.rudderRate > 1e-3 ? u * rudderDeg / p.rudderRate : 0.0;
        return radius * Math.Tan(half) + lag;
    }

    public static double CourseOf(WorldPosition a, WorldPosition b) =>
        CpaCalculator.Bearing(b.East - a.East, b.North - a.North);
}
