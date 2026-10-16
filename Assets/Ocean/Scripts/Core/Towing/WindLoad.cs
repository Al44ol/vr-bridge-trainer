// ============================================================
// WindLoad.cs — Ветровая нагрузка на судно по Isherwood (1972) (запрос 19.3а)
//
// Isherwood R. M. (1972). Wind Resistance of Merchant Ships. Trans. RINA 115, 327–338.
// Коэффициенты A_i, B_i, C_i по углу 0…180° через 10° и формулы — дословно как в MSS
// (Fossen, LIBRARY/environment/isherwood72.m, ред. 2024-07-01), знаки — по Blendermann (1994):
//   C_X = −(A0 + A1·2A_L/L² + A2·2A_F/B² + A3·L/B + A4·S/L + A5·C/L + A6·M),
//   C_Y =   B0 + B1·2A_L/L² + B2·2A_F/B² + B3·L/B + B4·S/L + B5·C/L + B6·A_SS/A_L,
//   C_N =   C0 + C1·2A_L/L² + C2·2A_F/B² + C3·L/B + C4·S/L + C5·C/L;
//   X = ½ρ_a·V_r²·A_F·C_X,  Y = ½ρ_a·V_r²·A_L·C_Y·sign(γ),  N = ½ρ_a·V_r²·A_L·L·C_N·sign(γ),  ρ_a = 1,224 кг/м³.
// Относительный ветер (Fossen 2011, п. 8.1): скорость воздуха относительно судна в связанных осях
//   u_rw = u − u_w, v_rw = v − v_w; V_r = √(u_rw² + v_rw²); угол атаки γ = −atan2(v_rw, u_rw):
//   γ = 0 — ветер в нос, γ < 0 — ветер с правого борта (сила — на левый борт, Y < 0).
// Ветер задаётся как в метеосводке: скорость и направление «откуда» (0 = N, по часовой).
// Площади проекций (A_L, A_F, A_SS, S, C, M) — допущения по размерениям (ShipWindArea.Estimate),
// пока у ассетов судов их нет. Силы действуют только в модели 3 СС (как силы причала, 19.2).
// ============================================================
using System;

/// <summary>Ветер: скорость, м/с, и направление «откуда», ° (0 = N, по часовой).</summary>
public struct Wind
{
    public double SpeedMs;
    public double FromDeg;

    public Wind(double speedMs, double fromDeg) { SpeedMs = speedMs; FromDeg = fromDeg; }

    public static Wind Knots(double knots, double fromDeg) => new Wind(knots * ShipParameters.KnotsToMs, fromDeg);

    /// <summary>Скорость воздуха (куда дует), м/с: восток, север.</summary>
    public WorldPosition Velocity
    {
        get
        {
            double to = (FromDeg + 180.0) * Math.PI / 180.0;
            return new WorldPosition(SpeedMs * Math.Sin(to), SpeedMs * Math.Cos(to));
        }
    }

    public bool Calm => SpeedMs <= 1e-6;
}

/// <summary>Надводная часть для ветровой нагрузки (входы Isherwood).</summary>
public sealed class ShipWindArea
{
    /// <summary>Длина, ширина, м.</summary>
    public double Length, Beam;
    /// <summary>Площади проекций: боковая A_L, лобовая A_F, боковая надстройки A_SS, м².</summary>
    public double LateralM2, FrontalM2, SuperstructureM2;
    /// <summary>Периметр боковой проекции без ватерлинии S, м; от носа до центра боковой проекции C, м; групп мачт M.</summary>
    public double PerimeterM, CentroidFromBowM;
    public int Masts;

    /// <summary>
    /// Оценка по размерениям (допущение, пока нет чертежей): надводный борт f = max(1,5; 0,1·L) м,
    /// надстройка — 0,3·L в длину, 2 яруса по 2,5 м, ширина 0,8·B, центр — на середине длины;
    /// A_L = L·f + A_SS, A_F = B·f + 0,8·B·h_н, S = L + 2·(f + h_н), одна мачта.
    /// Для Model45m: f = 4,5 м (как верх Hull в сцене), A_L ≈ 270 м², A_F ≈ 85 м².
    /// </summary>
    public static ShipWindArea Estimate(double length, double beam)
    {
        double f = Math.Max(1.5, 0.1 * length), hs = 5.0, ls = 0.3 * length;
        double ass = ls * hs;
        return new ShipWindArea
        {
            Length = length, Beam = beam,
            LateralM2 = length * f + ass, FrontalM2 = beam * f + 0.8 * beam * hs, SuperstructureM2 = ass,
            PerimeterM = length + 2.0 * (f + hs), CentroidFromBowM = 0.5 * length, Masts = 1
        };
    }

    public static ShipWindArea Estimate(ShipParameters p) => Estimate(p.length, p.beam);
}

/// <summary>Сила и момент ветра в связанных осях: X — в нос, Y — на ПБ, Н; N — Н·м (> 0 — нос вправо).</summary>
public struct WindForce
{
    public double X, Y, N;
    /// <summary>Относительный ветер: скорость, м/с, угол атаки γ, ° (0 — в нос, < 0 — с ПБ).</summary>
    public double RelativeSpeed, GammaDeg;
}

public static class WindLoad
{
    public const double AirDensity = 1.224;

    // Таблицы Isherwood (1972): строка — угол 0, 10 … 180°
    private static readonly double[,] A =
    {
        { 2.152, -5.00, 0.243, -0.164, 0, 0, 0 }, { 1.714, -3.33, 0.145, -0.121, 0, 0, 0 }, { 1.818, -3.97, 0.211, -0.143, 0, 0, 0.033 },
        { 1.965, -4.81, 0.243, -0.154, 0, 0, 0.041 }, { 2.333, -5.99, 0.247, -0.190, 0, 0, 0.042 }, { 1.726, -6.54, 0.189, -0.173, 0.348, 0, 0.048 },
        { 0.913, -4.68, 0, -0.104, 0.482, 0, 0.052 }, { 0.457, -2.88, 0, -0.068, 0.346, 0, 0.043 }, { 0.341, -0.91, 0, -0.031, 0, 0, 0.032 },
        { 0.355, 0, 0, 0, -0.247, 0, 0.018 }, { 0.601, 0, 0, 0, -0.372, 0, -0.020 }, { 0.651, 1.29, 0, 0, -0.582, 0, -0.031 },
        { 0.564, 2.54, 0, 0, -0.748, 0, -0.024 }, { -0.142, 3.58, 0, 0.047, -0.700, 0, -0.028 }, { -0.677, 3.64, 0, 0.069, -0.529, 0, -0.032 },
        { -0.723, 3.14, 0, 0.064, -0.475, 0, -0.032 }, { -2.148, 2.56, 0, 0.081, 0, 1.27, -0.027 }, { -2.707, 3.97, -0.175, 0.126, 0, 1.81, 0 },
        { -2.529, 3.76, -0.174, 0.128, 0, 1.55, 0 }
    };
    private static readonly double[,] B =
    {
        { 0, 0, 0, 0, 0, 0, 0 }, { 0.096, 0.22, 0, 0, 0, 0, 0 }, { 0.176, 0.71, 0, 0, 0, 0, 0 },
        { 0.225, 1.38, 0, 0.023, 0, -0.29, 0 }, { 0.329, 1.82, 0, 0.043, 0, -0.59, 0 }, { 1.164, 1.26, 0.121, 0, -0.242, -0.95, 0 },
        { 1.163, 0.96, 0.101, 0, -0.177, -0.88, 0 }, { 0.916, 0.53, 0.069, 0, 0, -0.65, 0 }, { 0.844, 0.55, 0.082, 0, 0, -0.54, 0 },
        { 0.889, 0, 0.138, 0, 0, -0.66, 0 }, { 0.799, 0, 0.155, 0, 0, -0.55, 0 }, { 0.797, 0, 0.151, 0, 0, -0.55, 0 },
        { 0.996, 0, 0.184, 0, -0.212, -0.66, 0.34 }, { 1.014, 0, 0.191, 0, -0.280, -0.69, 0.44 }, { 0.784, 0, 0.166, 0, -0.209, -0.53, 0.38 },
        { 0.536, 0, 0.176, -0.029, -0.163, 0, 0.27 }, { 0.251, 0, 0.106, -0.022, 0, 0, 0 }, { 0.125, 0, 0.046, -0.012, 0, 0, 0 },
        { 0, 0, 0, 0, 0, 0, 0 }
    };
    private static readonly double[,] C =
    {
        { 0, 0, 0, 0, 0, 0 }, { 0.0596, 0.061, 0, 0, 0, -0.074 }, { 0.1106, 0.204, 0, 0, 0, -0.170 },
        { 0.2258, 0.245, 0, 0, 0, -0.380 }, { 0.2017, 0.457, 0, 0.0067, 0, -0.472 }, { 0.1759, 0.573, 0, 0.0118, 0, -0.523 },
        { 0.1925, 0.480, 0, 0.0115, 0, -0.546 }, { 0.2133, 0.315, 0, 0.0081, 0, -0.526 }, { 0.1827, 0.254, 0, 0.0053, 0, -0.443 },
        { 0.2627, 0, 0, 0, 0, -0.508 }, { 0.2102, 0, -0.0195, 0, 0.0335, -0.492 }, { 0.1567, 0, -0.0258, 0, 0.0497, -0.457 },
        { 0.0801, 0, -0.0311, 0, 0.0740, -0.396 }, { -0.0189, 0, -0.0488, 0.0101, 0.1128, -0.420 }, { 0.0256, 0, -0.0422, 0.0100, 0.0889, -0.463 },
        { 0.0552, 0, -0.0381, 0.0109, 0.0689, -0.476 }, { 0.0881, 0, -0.0306, 0.0091, 0.0366, -0.415 }, { 0.0851, 0, -0.0122, 0.0025, 0, -0.220 },
        { 0, 0, 0, 0, 0, 0 }
    };

    private static double Row(double[,] t, int col, double deg)
    {
        double x = Math.Max(0.0, Math.Min(180.0, deg)) / 10.0;
        int i = Math.Min(17, (int)Math.Floor(x));
        double f = x - i;
        return t[i, col] + (t[i + 1, col] - t[i, col]) * f;
    }

    /// <summary>Коэффициенты C_X, C_Y, C_N при угле атаки γ (градусы; знак не учитывается — его даёт sign(γ) в силе).</summary>
    public static void Coefficients(ShipWindArea a, double gammaDeg, out double cx, out double cy, out double cn)
    {
        double g = gammaDeg % 360.0;
        if (g < 0) g += 360.0;
        if (g > 180.0) g = 360.0 - g;
        double L = a.Length, Bm = a.Beam;
        double al = 2.0 * a.LateralM2 / (L * L), af = 2.0 * a.FrontalM2 / (Bm * Bm), lb = L / Bm, sl = a.PerimeterM / L, cl = a.CentroidFromBowM / L;
        double ass = a.LateralM2 > 0 ? a.SuperstructureM2 / a.LateralM2 : 0.0;
        cx = -(Row(A, 0, g) + Row(A, 1, g) * al + Row(A, 2, g) * af + Row(A, 3, g) * lb + Row(A, 4, g) * sl + Row(A, 5, g) * cl + Row(A, 6, g) * a.Masts);
        cy = Row(B, 0, g) + Row(B, 1, g) * al + Row(B, 2, g) * af + Row(B, 3, g) * lb + Row(B, 4, g) * sl + Row(B, 5, g) * cl + Row(B, 6, g) * ass;
        cn = Row(C, 0, g) + Row(C, 1, g) * al + Row(C, 2, g) * af + Row(C, 3, g) * lb + Row(C, 4, g) * sl + Row(C, 5, g) * cl;
    }

    /// <summary>Сила ветра на судно с курсом psi (рад), скоростями u, v (м/с, связанные оси).</summary>
    public static WindForce Compute(ShipWindArea a, Wind w, double psi, double u, double v)
    {
        var f = new WindForce();
        if (a == null) return f;
        WorldPosition vw = w.Velocity;
        double s = Math.Sin(psi), c = Math.Cos(psi);
        double uw = vw.East * s + vw.North * c, vwy = vw.East * c - vw.North * s;   // скорость воздуха в связанных осях
        double urw = u - uw, vrw = v - vwy;
        double vr2 = urw * urw + vrw * vrw;
        f.RelativeSpeed = Math.Sqrt(vr2);
        if (vr2 < 1e-12) return f;
        double gamma = -Math.Atan2(vrw, urw);
        f.GammaDeg = gamma * 180.0 / Math.PI;
        Coefficients(a, f.GammaDeg, out double cx, out double cy, out double cn);
        double q = 0.5 * AirDensity * vr2, sg = Math.Sign(gamma);
        f.X = q * a.FrontalM2 * cx;
        f.Y = q * a.LateralM2 * cy * sg;
        f.N = q * a.LateralM2 * a.Length * cn * sg;
        return f;
    }

    /// <summary>Ветер на модель (до model.Step): сила в миделе и момент. Только модель 3 СС.</summary>
    public static WindForce Apply(ShipDynamicsModel m, ShipWindArea a, Wind w)
    {
        if (m == null || w.Calm || !m.Uses3Dof) return default;
        WindForce f = Compute(a, w, m.Psi, m.U, m.V);
        m.AddForce(f.X, f.Y, 0.0);
        m.AddMoment(f.N);
        return f;
    }
}
