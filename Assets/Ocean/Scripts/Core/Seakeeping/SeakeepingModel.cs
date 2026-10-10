// ============================================================
// SeakeepingModel.cs — Качка судна на волнах (запрос 4)
//
// Чистый C#-класс (сборка VRBridge.Core): используется ShipMotion
// (в FixedUpdate), стендом мореходности (без Play Mode) и тестами.
//
// Обозначения: ζ (heave) — вертикальное смещение, м (> 0 — вверх);
// φ (roll) — крен, рад (> 0 — на правый борт, правый борт вниз);
// θ (pitch) — дифферент, рад (> 0 — нос вверх); ψ — курс (0 = N, по часовой).
//
// 1. Возмущение. Высота воды z_j сэмплируется в n = n_x·n_y точках сетки
//    по корпусу (n_x вдоль, от кормы до носа, ±L/2; n_y поперёк, ±B/2)
//    вокруг центра качки. Точка j в системе судна (x_j — к носу, y_j — к ПБ)
//    переводится в мир: P_j = P_c + x_j·(sin ψ, cos ψ) + y_j·(cos ψ, −sin ψ).
//    По точкам МНК строится плоскость z = h + s_x·x + s_y·y:
//        [h, s_x, s_y]ᵀ = (AᵀA)⁻¹Aᵀ·z,  A_j = [1, x_j, y_j]
//    (матрица (AᵀA)⁻¹Aᵀ зависит только от сетки — считается один раз).
//    Волновое возмущение: ζ_w = h,  θ_w = atan s_x,  φ_w = −atan s_y.
//    Точки сетки осредняют короткие волны по длине и ширине корпуса
//    (для одной волны вдоль судна множитель ≈ sin(kL/2)/(kL/2)).
//
// 2. Динамика: корпус — не поплавок. Каждая степень свободы — звено
//    2-го порядка с собственной частотой ω₀ = 2π/T₀ и затуханием ζ₀:
//        ẍ + 2ζ₀ω₀ẋ + ω₀²x = ω₀²·x_w.
//    Статический коэффициент 1: очень длинная волна — судно лежит на её склоне.
//    АЧХ |H| = 1/√((1 − Λ²)² + (2ζ₀Λ)²), Λ = ω_e/ω₀: короткие (частые) волны
//    гасятся, у резонанса (Λ ≈ 1) отклик усиливается в 1/(2ζ₀) раз.
//    Частота встречи ω_e = ω − k·U·cos μ возникает сама: судно движется
//    по полю волн (μ — угол между курсом и направлением распространения волны).
//
// 3. Дискретизация точная при постоянном на шаге входе (как в A2):
//        e = x − x̄_w,  [e; ẋ](t+dt) = Φ(dt)·[e; ẋ](t),  Φ = exp(A·dt),
//        A = [[0, 1], [−ω₀², −2ζ₀ω₀]],
//        Φ = e^{−σdt}·[[cos + (σ/ω_d)·sin,  sin/ω_d], [−(ω₀²/ω_d)·sin,  cos − (σ/ω_d)·sin]],
//        σ = ζ₀ω₀,  ω_d = ω₀√(1 − ζ₀²),  cos/sin — от ω_d·dt;
//    вход на шаге x̄_w = (x_w(t) + x_w(t+dt))/2 (без запаздывания на полшага).
//    Устойчиво при любом dt; ζ₀ ограничен 0…0,95.
//
// 4. Комфорт (только для отображения, физику не меняет):
//        x_отобр = k_комф·x;  φ, θ — мягкий предел x_max·tanh(x/x_max)
//    (производная непрерывна — без рывков при подходе к пределу).
// ============================================================
using System;

public sealed class SeakeepingModel
{
    public ShipParameters Parameters;

    // ── Сетка точек ──
    /// <summary>Число точек вдоль корпуса (от кормы до носа), ≥ 2.</summary>
    public int SamplesAlong = 5;
    /// <summary>Число точек поперёк (от ЛБ до ПБ), ≥ 2.</summary>
    public int SamplesAcross = 3;
    /// <summary>Центр качки относительно точки позиции судна, в системе судна: к носу и к ПБ, м.</summary>
    public double CenterForward, CenterStarboard;
    /// <summary>Итерации обратной задачи Герстнера.</summary>
    public int Iterations = 3;

    // ── Состояние (физика, без комфорта) ──
    public double Heave, HeaveRate;   // м, м/с
    public double Roll, RollRate;     // рад, рад/с
    public double Pitch, PitchRate;   // рад, рад/с

    // ── Последнее волновое возмущение (плоскость МНК) ──
    public double WaveHeave, WaveRoll, WavePitch;

    // ── Последние точки (для отладки и гизмо): мир (м) и высота воды (м) ──
    public double[] SampleEast = Array.Empty<double>(), SampleNorth = Array.Empty<double>(), SampleHeight = Array.Empty<double>();
    // Координаты точек в системе судна относительно центра (к носу, к ПБ), м
    public double[] LocalForward = Array.Empty<double>(), LocalStarboard = Array.Empty<double>();

    private double[,] fit;            // (AᵀA)⁻¹Aᵀ, 3 × n
    private int fitAlong, fitAcross;
    private double fitL, fitB;
    private bool initialized;

    public SeakeepingModel(ShipParameters parameters)
    {
        Parameters = parameters;
    }

    /// <summary>Поставить судно в равновесие на текущей воде (без переходного процесса).</summary>
    public void Reset(OceanWaveField field, double east, double north, double psi, double timeOffset = 0.0)
    {
        ComputeForcing(field, east, north, psi, timeOffset);
        Heave = WaveHeave; Roll = WaveRoll; Pitch = WavePitch;
        HeaveRate = RollRate = PitchRate = 0.0;
        initialized = true;
    }

    /// <summary>
    /// Шаг dt (с). Поле волн и позиция судна (east, north — точка позиции судна, ψ — курс, рад)
    /// должны соответствовать концу шага: вход берётся средним начала и конца шага.
    /// </summary>
    public void Step(OceanWaveField field, double east, double north, double psi, double dt)
    {
        if (!initialized) { Reset(field, east, north, psi); return; }

        double hPrev = WaveHeave, rPrev = WaveRoll, pPrev = WavePitch;
        ComputeForcing(field, east, north, psi);

        ShipParameters p = Parameters;
        StepOscillator(ref Heave, ref HeaveRate, 0.5 * (hPrev + WaveHeave), p.heavePeriod, p.heaveDamping, dt);
        StepOscillator(ref Roll, ref RollRate, 0.5 * (rPrev + WaveRoll), p.rollPeriod, p.rollDamping, dt);
        StepOscillator(ref Pitch, ref PitchRate, 0.5 * (pPrev + WavePitch), p.pitchPeriod, p.pitchDamping, dt);
    }

    /// <summary>Сэмплировать воду в точках сетки и найти плоскость МНК (WaveHeave/Roll/Pitch).</summary>
    public void ComputeForcing(OceanWaveField field, double east, double north, double psi, double timeOffset = 0.0)
    {
        EnsureGrid();
        double sinP = Math.Sin(psi), cosP = Math.Cos(psi);
        // Нос: (sin ψ, cos ψ); правый борт: (cos ψ, −sin ψ)
        double cE = east + CenterForward * sinP + CenterStarboard * cosP;
        double cN = north + CenterForward * cosP - CenterStarboard * sinP;

        double h = 0.0, sx = 0.0, sy = 0.0;
        int n = LocalForward.Length;
        for (int j = 0; j < n; j++)
        {
            double x = LocalForward[j], y = LocalStarboard[j];
            double e = cE + x * sinP + y * cosP;
            double nn = cN + x * cosP - y * sinP;
            double z = field.SampleHeight(e, nn, timeOffset, Iterations);
            SampleEast[j] = e; SampleNorth[j] = nn; SampleHeight[j] = z;
            h += fit[0, j] * z;
            sx += fit[1, j] * z;
            sy += fit[2, j] * z;
        }
        WaveHeave = h;
        WavePitch = Math.Atan(sx);
        WaveRoll = -Math.Atan(sy);
    }

    // Сетка и матрица МНК — пересчёт только при изменении размеров
    private void EnsureGrid()
    {
        int na = Math.Max(2, SamplesAlong), nb = Math.Max(2, SamplesAcross);
        double L = Math.Max(1.0, Parameters.length), B = Math.Max(0.5, Parameters.beam);
        if (fit != null && na == fitAlong && nb == fitAcross && L == fitL && B == fitB) return;

        int n = na * nb;
        LocalForward = new double[n]; LocalStarboard = new double[n];
        SampleEast = new double[n]; SampleNorth = new double[n]; SampleHeight = new double[n];
        for (int i = 0; i < na; i++)
        for (int j = 0; j < nb; j++)
        {
            LocalForward[i * nb + j] = -0.5 * L + L * i / (na - 1);
            LocalStarboard[i * nb + j] = -0.5 * B + B * j / (nb - 1);
        }
        fit = LeastSquaresPlane(LocalForward, LocalStarboard);
        fitAlong = na; fitAcross = nb; fitL = L; fitB = B;
    }

    /// <summary>Матрица (AᵀA)⁻¹Aᵀ (3 × n) для плоскости z = h + s_x·x + s_y·y.</summary>
    public static double[,] LeastSquaresPlane(double[] x, double[] y)
    {
        int n = x.Length;
        // AᵀA — симметричная 3×3
        double s1 = n, sx = 0, sy = 0, sxx = 0, sxy = 0, syy = 0;
        for (int j = 0; j < n; j++)
        {
            sx += x[j]; sy += y[j];
            sxx += x[j] * x[j]; sxy += x[j] * y[j]; syy += y[j] * y[j];
        }
        double[,] m = { { s1, sx, sy }, { sx, sxx, sxy }, { sy, sxy, syy } };
        double[,] inv = Invert3(m);
        var w = new double[3, n];
        for (int j = 0; j < n; j++)
            for (int r = 0; r < 3; r++)
                w[r, j] = inv[r, 0] + inv[r, 1] * x[j] + inv[r, 2] * y[j];
        return w;
    }

    private static double[,] Invert3(double[,] a)
    {
        double c00 = a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1];
        double c01 = a[1, 2] * a[2, 0] - a[1, 0] * a[2, 2];
        double c02 = a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0];
        double det = a[0, 0] * c00 + a[0, 1] * c01 + a[0, 2] * c02;
        if (Math.Abs(det) < 1e-12) throw new InvalidOperationException("Вырожденная сетка точек качки");
        double k = 1.0 / det;
        return new double[,]
        {
            { c00 * k, (a[0, 2] * a[2, 1] - a[0, 1] * a[2, 2]) * k, (a[0, 1] * a[1, 2] - a[0, 2] * a[1, 1]) * k },
            { c01 * k, (a[0, 0] * a[2, 2] - a[0, 2] * a[2, 0]) * k, (a[0, 2] * a[1, 0] - a[0, 0] * a[1, 2]) * k },
            { c02 * k, (a[0, 1] * a[2, 0] - a[0, 0] * a[2, 1]) * k, (a[0, 0] * a[1, 1] - a[0, 1] * a[1, 0]) * k },
        };
    }

    /// <summary>
    /// Точный шаг звена ẍ + 2ζω₀ẋ + ω₀²x = ω₀²·u при постоянном u на шаге.
    /// period — T₀, с; zeta — ζ₀ (ограничивается 0…0,95).
    /// </summary>
    public static void StepOscillator(ref double x, ref double v, double u, double period, double zeta, double dt)
    {
        double w0 = 2.0 * Math.PI / Math.Max(0.1, period);
        double z = zeta < 0.0 ? 0.0 : (zeta > 0.95 ? 0.95 : zeta);
        double sigma = z * w0;
        double wd = w0 * Math.Sqrt(1.0 - z * z);
        double ex = Math.Exp(-sigma * dt);
        double c = Math.Cos(wd * dt), s = Math.Sin(wd * dt);

        double e = x - u;
        double eNew = ex * ((c + sigma / wd * s) * e + s / wd * v);
        double vNew = ex * (-(w0 * w0) / wd * s * e + (c - sigma / wd * s) * v);
        x = u + eNew;
        v = vNew;
    }

    /// <summary>Мягкий предел: max·tanh(x/max) (max ≤ 0 — без предела).</summary>
    public static double SoftLimit(double x, double max) => max > 0.0 ? max * Math.Tanh(x / max) : x;

    /// <summary>
    /// Период встречи с волной, с: T_e = 2π/|ω − k·U·cos μ|,
    /// ω — частота волны (с учётом множителей скорости), k — волновое число,
    /// μ — угол между курсом судна и направлением распространения волны (0 — попутная, π — встречная).
    /// </summary>
    public static double EncounterPeriod(double omega, double k, double u, double muRad)
    {
        double we = omega - k * u * Math.Cos(muRad);
        return Math.Abs(we) > 1e-9 ? 2.0 * Math.PI / Math.Abs(we) : double.PositiveInfinity;
    }

    // ─────────── Оценки собственных периодов по главным размерениям ───────────

    /// <summary>
    /// Период бортовой качки, с: T_φ = 2·C·B/√GM,
    /// C = 0,373 + 0,023·(B/d) − 0,043·(L_wl/100) (Кодекс остойчивости IS Code 2008,
    /// рез. MSC.267(85), ч. A, п. 2.3.4 — критерий погоды). L, B, d, GM — м.
    /// </summary>
    public static double RollPeriodIsCode(double L, double B, double d, double gm)
    {
        double c = 0.373 + 0.023 * (B / d) - 0.043 * (L / 100.0);
        return 2.0 * c * B / Math.Sqrt(Math.Max(gm, 1e-3));
    }

    /// <summary>
    /// Период вертикальной качки, с: T_z = 2π·√((1 + a₃₃/m)·C_B·d/(C_WP·g)).
    /// ω_z² = c₃₃/(m + a₃₃), c₃₃ = ρg·C_WP·L·B, m = ρ·C_B·L·B·d (Journée & Massie, 2001).
    /// addedMassRatio — a₃₃/m (≈ 1 для судовых обводов).
    /// </summary>
    public static double HeavePeriod(double d, double cb, double cwp, double addedMassRatio = 1.0)
    {
        return 2.0 * Math.PI * Math.Sqrt((1.0 + addedMassRatio) * cb * d / (Math.Max(cwp, 0.1) * OceanWaveField.Gravity));
    }
}
