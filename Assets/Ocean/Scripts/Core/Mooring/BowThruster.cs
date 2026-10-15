// ============================================================
// BowThruster.cs — Носовое подруливающее устройство (запрос 19.2)
//
// Поперечная сила в носу x_пу = 0,42·L (тоннельный винт), только модель 3 СС:
//   F = T_max · a · f(u),  a — фактическая доля упора (−1…1, > 0 — нос на правый борт),
//   da/dt = (a_зад − a)/τ_пу, τ_пу = 3 с (раскрутка винта, допущение);
//   f(u) = 1/(1 + (u/u½)²), u½ = 1,3 м/с (≈ 2,5 уз): на ходу струя сносится потоком и
//   эффективность падает — около половины на 2,5 уз, около 20 % на 5 уз
//   (качественно по Brix, 1993, «Manoeuvring Technical Manual»; значения — допущение).
// Упор по водоизмещению (допущение): T_max = 0,22 кН·Δ^(2/3), Δ — т
// (Model45m ≈ 18 кН ≈ 1,8 т; Mariner ≈ 170 кН ≈ 17 т).
// Положения рычага (сцена): ЛБ 100 / ЛБ 50 / 0 / ПБ 50 / ПБ 100 % → a_зад = −1…1.
// ============================================================
using System;

public sealed class BowThruster
{
    public const double LeverL = 0.42, SpoolTime = 3.0, HalfEffectSpeed = 1.3;

    /// <summary>Наибольший упор, Н.</summary>
    public double MaxThrust;
    /// <summary>Точка приложения от миделя, м.</summary>
    public double X;
    /// <summary>Заданная доля упора −1…1 (> 0 — нос вправо).</summary>
    public double Command;
    /// <summary>Фактическая доля упора (с раскруткой).</summary>
    public double Actual { get; private set; }
    /// <summary>Сила последнего шага, Н (> 0 — на правый борт).</summary>
    public double Force { get; private set; }

    public BowThruster(double maxThrust, double x)
    {
        MaxThrust = maxThrust;
        X = x;
    }

    public static BowThruster For(ShipParameters p) => new BowThruster(MaxThrustFor(p), LeverL * p.length);

    /// <summary>Упор по водоизмещению, Н: 0,22 кН·Δ^(2/3).</summary>
    public static double MaxThrustFor(ShipParameters p) => 0.22e3 * Math.Pow(BerthArea.ShipMass(p) / 1000.0, 2.0 / 3.0);

    /// <summary>Эффективность на ходу u (м/с).</summary>
    public static double SpeedFactor(double u) => 1.0 / (1.0 + (u / HalfEffectSpeed) * (u / HalfEffectSpeed));

    /// <summary>Шаг: раскрутка, сила в модель (до model.Step).</summary>
    public void Apply(ShipDynamicsModel m, double dt)
    {
        double cmd = Math.Max(-1.0, Math.Min(1.0, Command));
        Actual = cmd + (Actual - cmd) * Math.Exp(-dt / SpoolTime);
        if (Math.Abs(Actual) < 1e-6 && cmd == 0.0) Actual = 0.0;
        Force = MaxThrust * Actual * SpeedFactor(m.U);
        if (Force != 0.0) m.AddForce(0.0, Force, X);
    }

    public void Reset()
    {
        Command = 0.0;
        Actual = 0.0;
        Force = 0.0;
    }
}
