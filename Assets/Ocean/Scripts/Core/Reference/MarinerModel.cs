// ============================================================
// MarinerModel.cs — Эталонная модель сухогруза класса Mariner (A3.2, калибровка)
//
// Нелинейная модель манёвренности 3 СС (продольная, поперечная, рыскание)
// в форме Абковица (Abkowitz, 1964) с гидродинамическими производными,
// полученными на планарном механизме (ПМУ) для модели судна класса Mariner
// (USS Compass Island) и пересчитанными на натуру:
//   Chislett M.S., Strøm-Tejsen J. Planar Motion Mechanism Tests and Full-Scale
//   Steering and Manoeuvring Predictions for a Mariner Class Vessel.
//   Technical Report Hy-5/Hy-6. Hydro- and Aerodynamics Laboratory, Lyngby, 1965.
// Коэффициенты и уравнения перенесены дословно из открытой реализации
//   Fossen T.I. Marine Systems Simulator (MSS), файл CRAFT/SHIP/models/mariner.m
//   (авт. T. Lauvdal, 1994; ред. 2001–2024), https://github.com/cybergalactic/MSS
// Отличие от mariner.m — только интегрирование (здесь RK4 с шагом dt)
// и знак руля на входе/выходе (здесь δ > 0 — правый борт, как в тренажёре).
//
// Главные данные модели: L = 160,93 м, U0 = 7,7175 м/с (15 уз), δ_max = 40°,
// |dδ/dt| ≤ 5 °/с, рулевая машина — апериодическое звено с T = 1 с.
// Обороты винта постоянны (модель возмущений около U0): телеграфа нет,
// торможение и разгон этой моделью не описываются.
//
// Состояние (как в mariner.m): Δu — возмущение продольной скорости от U0, v, r,
// x (север), y (восток), ψ, δ_м (рад, знак mariner.m: δ_м = −δ_тренажёра).
// ============================================================
using System;

public sealed class MarinerModel : IVesselModel
{
    // ── Главные данные (mariner.m) ──
    public const double L = 160.93;          // м
    public const double NominalSpeed = 7.7175; // м/с = 15 уз
    public double MaxRudderDeg = 40.0;       // δ_max, градусы
    public double RudderRateDegS = 5.0;      // |dδ/dt|, °/с
    /// <summary>
    /// Учитывать постоянные члены Y'_0, N'_0 (асимметрия от винта): на прямом руле судно
    /// поворачивает вправо (≈ 0,17 °/с при 15 уз). Тренажёрная модель симметрична, поэтому
    /// при идентификации эти члены выключаются; их влияние приводится в отчёте.
    /// </summary>
    public bool PropellerAsymmetry = true;

    // ── Безразмерные массы и производные (mariner.m, ·1e−5) ──
    private const double m = 798e-5, Iz = 39.2e-5, xG = -0.023;
    private const double Xudot = -42e-5, Yvdot = -748e-5, Nvdot = 4.646e-5;
    private const double Xu = -184e-5, Yrdot = -9.354e-5, Nrdot = -43.8e-5;
    private const double Xuu = -110e-5, Yv = -1160e-5, Nv = -264e-5;
    private const double Xuuu = -215e-5, Yr = -499e-5, Nr = -166e-5;
    private const double Xvv = -899e-5, Yvvv = -8078e-5, Nvvv = 1636e-5;
    private const double Xrr = 18e-5, Yvvr = 15356e-5, Nvvr = -5483e-5;
    private const double Xdd = -95e-5, Yvu = -1160e-5, Nvu = -264e-5;
    private const double Xudd = -190e-5, Yru = -499e-5, Nru = -166e-5;
    private const double Xrv = 798e-5, Yd = 278e-5, Nd = -139e-5;
    private const double Xvd = 93e-5, Yddd = -90e-5, Nddd = 45e-5;
    private const double Xuvd = 93e-5, Yud = 556e-5, Nud = -278e-5;
    private const double Yuud = 278e-5, Nuud = -139e-5;
    private const double Yvdd = -4e-5, Nvdd = 13e-5;
    private const double Yvvd = 1190e-5, Nvvd = -489e-5;
    private const double Y0 = -4e-5, N0 = 3e-5;
    private const double Y0u = -8e-5, N0u = 6e-5;
    private const double Y0uu = -4e-5, N0uu = 3e-5;

    private const double m11 = m - Xudot;
    private const double m22 = m - Yvdot;
    private const double m23 = m * xG - Yrdot;
    private const double m32 = m * xG - Nvdot;
    private const double m33 = Iz - Nrdot;

    // Состояние [Δu, v, r, x, y, ψ, δ_м]
    private readonly double[] x = new double[7];
    private readonly double[] k1 = new double[7], k2 = new double[7], k3 = new double[7], k4 = new double[7], tmp = new double[7];
    private readonly double u0;

    /// <summary>Модель на номинальной скорости u0 (по умолчанию 15 уз), прямо, руль 0.</summary>
    public MarinerModel(double nominalSpeed = NominalSpeed)
    {
        u0 = nominalSpeed;
    }

    // ── Выходы в обозначениях тренажёра ──
    public double SurgeSpeed => u0 + x[0];
    public double SwaySpeed => x[1];
    public double Psi => x[5];
    public double R => x[2];
    /// <summary>Модуль скорости, м/с.</summary>
    public double U => Math.Sqrt(SurgeSpeed * SurgeSpeed + x[1] * x[1]);
    public double Delta => -x[6] * 180.0 / Math.PI;
    public double Ordered { get; private set; }
    public double North => x[3];
    public double East => x[4];
    public double Length => L;
    /// <summary>Угол дрейфа β = atan2(−v, u), градусы (> 0 — нос внутрь циркуляции вправо).</summary>
    public double DriftDeg => Math.Atan2(-x[1], SurgeSpeed) * 180.0 / Math.PI;

    public double RudderCommandDeg { get; set; }
    /// <summary>Не используется: обороты винта постоянны.</summary>
    public double SpeedFactor { get; set; } = 1.0;

    public void Step(double dt)
    {
        Ordered = Math.Max(-MaxRudderDeg, Math.Min(MaxRudderDeg, RudderCommandDeg));
        Derivatives(x, k1);
        Add(x, k1, 0.5 * dt, tmp); Derivatives(tmp, k2);
        Add(x, k2, 0.5 * dt, tmp); Derivatives(tmp, k3);
        Add(x, k3, dt, tmp); Derivatives(tmp, k4);
        for (int i = 0; i < 7; i++) x[i] += dt / 6.0 * (k1[i] + 2.0 * k2[i] + 2.0 * k3[i] + k4[i]);
    }

    private static void Add(double[] a, double[] b, double h, double[] o)
    {
        for (int i = 0; i < 7; i++) o[i] = a[i] + h * b[i];
    }

    /// <summary>Правые части — дословно mariner.m (Fossen, MSS).</summary>
    private void Derivatives(double[] s, double[] xdot)
    {
        double U = Math.Sqrt((u0 + s[0]) * (u0 + s[0]) + s[1] * s[1]);

        // δ_c = −ui: в mariner.m положительная команда даёт положительное r
        double deltaMax = MaxRudderDeg * Math.PI / 180.0;
        double deltaC = -Ordered * Math.PI / 180.0;
        if (Math.Abs(deltaC) >= deltaMax) deltaC = Math.Sign(deltaC) * deltaMax;

        double u = s[0] / U, v = s[1] / U, r = s[2] * L / U, psi = s[5], delta = s[6];

        double deltaDot = deltaC - delta;
        double rateMax = RudderRateDegS * Math.PI / 180.0;
        if (Math.Abs(deltaDot) >= rateMax) deltaDot = Math.Sign(deltaDot) * rateMax;

        double X = Xu * u + Xuu * u * u + Xuuu * u * u * u + Xvv * v * v + Xrr * r * r + Xrv * r * v + Xdd * delta * delta
                 + Xudd * u * delta * delta + Xvd * v * delta + Xuvd * u * v * delta;

        double Y = Yv * v + Yr * r + Yvvv * v * v * v + Yvvr * v * v * r + Yvu * v * u + Yru * r * u + Yd * delta
                 + Yddd * delta * delta * delta + Yud * u * delta + Yuud * u * u * delta + Yvdd * v * delta * delta
                 + Yvvd * v * v * delta + (PropellerAsymmetry ? Y0 + Y0u * u + Y0uu * u * u : 0.0);

        double N = Nv * v + Nr * r + Nvvv * v * v * v + Nvvr * v * v * r + Nvu * v * u + Nru * r * u + Nd * delta
                 + Nddd * delta * delta * delta + Nud * u * delta + Nuud * u * u * delta + Nvdd * v * delta * delta
                 + Nvvd * v * v * delta + (PropellerAsymmetry ? N0 + N0u * u + N0uu * u * u : 0.0);

        double detM22 = m22 * m33 - m23 * m32;

        xdot[0] = X * (U * U / L) / m11;
        xdot[1] = -(-m33 * Y + m23 * N) * (U * U / L) / detM22;
        xdot[2] = (-m32 * Y + m22 * N) * (U * U / (L * L)) / detM22;
        xdot[3] = (Math.Cos(psi) * (u0 / U + u) - Math.Sin(psi) * v) * U;
        xdot[4] = (Math.Sin(psi) * (u0 / U + u) + Math.Cos(psi) * v) * U;
        xdot[5] = r * (U / L);
        xdot[6] = deltaDot;
    }

    /// <summary>
    /// Линейные производные ПМУ-модели Mariner в форме Clarke (запрос 19.1, сравнение с регрессией):
    /// гидродинамические Y'_r, N'_r без центробежного члена — в mariner.m он входит в Y'_r, N'_r
    /// (Y'_r,гд = Y'_r + m', N'_r,гд = N'_r + m'·x'_G). Нормировка та же (½ρL²U, ½ρL³).
    /// </summary>
    public static ClarkeDerivatives HydroDerivatives(out double massPrime, out double izPrime, out double xgPrime)
    {
        massPrime = m; izPrime = Iz; xgPrime = xG;
        return new ClarkeDerivatives
        {
            Yvdot = Yvdot, Yrdot = Yrdot, Nvdot = Nvdot, Nrdot = Nrdot,
            Yv = Yv, Yr = Yr + m, Nv = Nv, Nr = Nr + m * xG,
        };
    }

    // ─────────── Линеаризация (для проверки идентификации) ───────────

    /// <summary>
    /// Постоянная времени продольного движения при постоянных оборотах, с:
    /// из du/dt = X'·(U²/L)/m'₁₁ при X' ≈ X'_u·Δu/U → τ_u = m'₁₁·L/(−X'_u·U0).
    /// </summary>
    public static double SurgeTimeConstant(double u0 = NominalSpeed) => m11 * L / (-Xu * u0);

    /// <summary>
    /// Безразмерные K', T' Номото 1-го порядка из линейных производных при U = U0
    /// (Nomoto et al., 1957; Fossen, 2011, гл. 7): линейная модель
    /// M·[v̇', ṙ']ᵀ + N·[v', r']ᵀ = b·δ, где N учитывает Y'_v + Y'_vu, Y'_r + Y'_ru − m', …
    /// Передаточная функция r/δ = K'(1 + T3's)/((1 + T1's)(1 + T2's)), T' = T1' + T2' − T3'.
    /// Знак K' приведён к соглашению тренажёра (δ > 0 → r > 0).
    /// </summary>
    public static void LinearNomoto(out double kPrime, out double tPrime, out double t1, out double t2, out double t3)
    {
        // При Δu = 0 (U = U0) члены с u' обнуляются: Y = Y'_v·v' + Y'_r·r' + Y'_δ·δ (и так же N).
        // В уравнениях mariner.m нет отдельных центростремительных членов (−m'·u·r),
        // поэтому линеаризация берёт коэффициенты так, как они заданы в источнике.
        // Постоянные смещения Y'_0, N'_0 (асимметрия винта) на K', T' не влияют.
        double yv = Yv, yr = Yr, nv = Nv, nr = Nr, yd = Yd, nd = Nd;
        // M·ẋ = A·x + b·δ,  M = [[m22, m23],[m32, m33]], A = [[yv, yr],[nv, nr]], b = [yd, nd]
        double det = m22 * m33 - m23 * m32;
        // A_s = M⁻¹·A, b_s = M⁻¹·b
        double a11 = (m33 * yv - m23 * nv) / det, a12 = (m33 * yr - m23 * nr) / det;
        double a21 = (-m32 * yv + m22 * nv) / det, a22 = (-m32 * yr + m22 * nr) / det;
        double b1 = (m33 * yd - m23 * nd) / det, b2 = (-m32 * yd + m22 * nd) / det;
        // r/δ = (b2·s + (a21·b1 − a11·b2)) / (s² − (a11 + a22)·s + (a11·a22 − a12·a21))
        double c0 = a11 * a22 - a12 * a21, c1 = -(a11 + a22);
        double num0 = a21 * b1 - a11 * b2, num1 = b2;
        double k = num0 / c0;
        t3 = num1 / num0;
        // (1 + T1 s)(1 + T2 s) = 1 + (c1/c0) s + (1/c0) s²
        double sum = c1 / c0, prod = 1.0 / c0;
        double disc = Math.Sqrt(Math.Max(0.0, sum * sum - 4.0 * prod));
        t1 = 0.5 * (sum + disc);
        t2 = 0.5 * (sum - disc);
        tPrime = t1 + t2 - t3;
        kPrime = -k;          // δ_м = −δ_тренажёра
    }
}
