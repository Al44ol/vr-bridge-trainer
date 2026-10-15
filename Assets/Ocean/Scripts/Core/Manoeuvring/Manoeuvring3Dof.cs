// ============================================================
// Manoeuvring3Dof.cs — Модель манёвренности 3 СС для всех скоростей, включая малые (запрос 19.1)
//
// Номото описывает только рыскание: судно идёт строго по своему диаметральной плоскости,
// без дрейфа, и на стопе не движется ни лагом, ни разворотом. Для швартовки (буксиры,
// концы, кранцы, подруливающее устройство) нужны три степени свободы — продольная u,
// поперечная v и рыскание r — и силы, приложенные в любой точке корпуса.
//
// Оси: связанные с судном, x — в нос, y — на правый борт; r > 0 — поворот вправо;
// δ > 0 — руль на правый борт (как в тренажёре). Мидель — начало координат.
//
// Уравнения (Fossen, 2011, гл. 6–7; размерный вид, ρ = 1025 кг/м³):
//   m₁₁·u̇ = m₁₁·(u_зад − u)/τ + m·v·r + m·x_G·r² + X_вн
//   m₂₂·v̇ + m₂₃·ṙ = |u|·(Y_v·v + Y_r·r) − m·u·r       + Y_пс + Y_р + Y_в + Y_вн
//   m₃₂·v̇ + m₃₃·ṙ = u·N_v·v + |u|·k_N·N_r·r − m·x_G·u·r + N_пс + N_р + N_в + N_вн
//   m₁₁ = m − X_u̇,  m₂₂ = m − Y_v̇,  m₂₃ = m·x_G − Y_ṙ,  m₃₂ = m·x_G − N_v̇,  m₃₃ = I_z − N_ṙ.
//
// 1. Продольное движение — прежний закон телеграфа du/dt = (u_зад − u)/τ (A2) плюс связь
//    с поперечным движением m·v·r (даёт потерю скорости на циркуляции сама, без C_r')
//    и внешняя сила.
// 2. Линейные гидродинамические производные — регрессия Clarke, Gedling, Hine (1983)
//    по L, B, d, C_B (дословно как в MSS clarke83.m, Fossen): нормировка ½ρL²U,
//    Y'_v = −S(1 + 0,4·C_B·B/d), Y'_r = −S(−½ + 2,2·B/L − 0,08·B/d),
//    N'_v = −S(½ + 2,4·d/L),    N'_r = −S(¼ + 0,039·B/d − 0,56·B/L),
//    присоединённые массы Y'_v̇, Y'_ṙ, N'_v̇, N'_ṙ — там же; S = π(d/L)².
//    В отличие от clarke83.m (где центробежный член, по документации функции, считается
//    вошедшим в Y'_r, N'_r), здесь −m·u·r и −m·x_G·u·r добавлены явно: значения Clarke
//    для размерений Mariner совпадают с гидродинамическими Y_r, N_r ПМУ-модели Mariner
//    после вычета m' (Y'_r: Clarke 0,0032, Mariner 0,0030) — см. ThesisNotes 19.
//    Масштаб — фактическая продольная скорость (подъёмная сила корпуса как крыла малого
//    удлинения): на стопе линейные члены исчезают, остаётся поперечное сопротивление.
//    Демпфирующие члены Y_v, Y_r, N_r — с |u|; момент N_v·v (подъёмная сила приложена ближе
//    к набегающей кромке, плюс момент Мунка) — с u со знаком: на заднем ходу «нос» корпуса
//    по потоку — корма, и момент меняет знак. С |u| в N_v при дрейфе без хода возникало
//    ложное самоподдерживающееся вращение (связь u ↔ m·v·r), см. ThesisNotes 19.
// 3. Поперечное сопротивление (cross-flow drag, Fossen 2011, п. 6.4; MSS crossFlowDrag.m):
//    по 20 полосам корпуса, местная поперечная скорость v + x·r,
//    Y_пс = −½ρ·d·C_d·Σ|v + x·r|·(v + x·r)·Δx,  N_пс = −½ρ·d·C_d·Σ x·|v + x·r|·(v + x·r)·Δx,
//    C_d — двумерный коэффициент по кривой Хёрнера от B/(2d) (MSS Hoerner.m, Leite et al. 1998).
//    Именно этот член даёт движение лагом и разворот на месте при u = 0.
// 3а. Согласование нелинейности на ходу (matchTurningCircle): принцип поперечного обтекания
//    выведен для больших углов дрейфа и на ходу передаёт нелинейность корпуса и руля грубо —
//    с C_d по Хёрнеру установившаяся циркуляция с рулём 35° отличается от откалиброванной
//    в 0,6…2,4 раза (ThesisNotes 19). Поэтому C_d_эфф = C_d·k(u), k = 1 при |u| ≤ 0,25·V_эксп,
//    k = k_∞ при |u| ≥ 0,5·V_эксп, между ними — плавно (smoothstep); k_∞ подбирается бисекцией так,
//    чтобы диаметр установившейся циркуляции (руль 35°, «Полный вперёд») совпал с моделью
//    Номото/Норрбина этого ассета. Малые скорости (швартовка) — по Хёрнеру без подбора.
// 4. Руль: Y_р = ½ρL²·q·Y'_δ·δ, N_р = x_R·Y_р; «скоростной напор» на руле
//    q = u·|u| + k_ps·max(0, u_зад² − u·|u|) при u_зад > 0 (струя винта при работе вперёд),
//    q = u·|u| иначе. В установившемся ходе q = u² — откалиброванная поворотливость сохраняется;
//    при даче хода с места («толчок» машиной) руль уже действует. k_ps — допущение.
// 5. Поперечная сила винта на заднем ходу (правый винт фиксированного шага — корму влево):
//    Y_в = −c_в·T_зх, приложена в корме (x = −L/2); T_зх = m₁₁·|u_зад|/τ_разг — упор,
//    который закон телеграфа даёт в начале разгона. c_в — допущение.
// 6. Согласование с калибровкой: Y'_δ (при заданном плече x'_R) и множитель демпфирования
//    рыскания k_N подбираются так, чтобы линеаризованная модель 2-го порядка
//    (Nomoto et al., 1957) давала те же K' и T' = T₁ + T₂ − T₃, что ассет судна.
//    Если по T' точного решения нет — k_N берётся по наименьшей невязке, совпадает только K'.
//
// Ограничения: производные Clarke — для водоизмещающих судов на переднем ходу; на заднем ходу
// используются те же производные с |u| (приближение); мелководье, ветер и течение не учтены
// (ветер — в части 19.3); вертикальные движения — у SeakeepingModel отдельно.
// ============================================================
using System;

/// <summary>Безразмерные производные Clarke et al. (1983), нормировка ½ρL²U (как в MSS clarke83.m).</summary>
public struct ClarkeDerivatives
{
    public double Yvdot, Yrdot, Nvdot, Nrdot;
    public double Yv, Yr, Nv, Nr;

    /// <summary>Производные по главным размерениям: L, B, d — м, C_B — безразмерный.</summary>
    public static ClarkeDerivatives Compute(double L, double B, double T, double Cb)
    {
        double S = Math.PI * (T / L) * (T / L);
        return new ClarkeDerivatives
        {
            Yvdot = -S * (1.0 + 0.16 * Cb * B / T - 5.1 * (B / L) * (B / L)),
            Yrdot = -S * (0.67 * B / L - 0.0033 * (B / T) * (B / T)),
            Nvdot = -S * (1.1 * B / L - 0.041 * (B / T)),
            Nrdot = -S * (1.0 / 12.0 + 0.017 * Cb * (B / T) - 0.33 * (B / L)),
            Yv = -S * (1.0 + 0.4 * Cb * (B / T)),
            Yr = -S * (-0.5 + 2.2 * (B / L) - 0.08 * (B / T)),
            Nv = -S * (0.5 + 2.4 * (T / L)),
            Nr = -S * (0.25 + 0.039 * (B / T) - 0.56 * (B / L)),
        };
    }
}

/// <summary>Вспомогательные формулы корпуса (MSS: Hoerner.m, addedMassSurge.m).</summary>
public static class HullForces
{
    public const double Rho = 1025.0;

    // Кривая Хёрнера: B/(2d) → C_d (MSS Hoerner.m, оцифровка Leite et al., 1998)
    private static readonly double[] HoernerX =
    {
        0.0108623, 0.176606, 0.353025, 0.451863, 0.472838, 0.492877, 0.493252, 0.558473, 0.646401, 0.833589,
        0.988002, 1.30807, 1.63918, 1.85998, 2.31288, 2.59998, 3.00877, 3.45075, 3.7379, 4.00309,
    };
    private static readonly double[] HoernerY =
    {
        1.96608, 1.96573, 1.89756, 1.78718, 1.58374, 1.27862, 1.21082, 1.08356, 0.998631, 0.87959,
        0.828415, 0.759941, 0.691442, 0.657076, 0.630693, 0.596186, 0.586846, 0.585909, 0.559877, 0.559315,
    };

    /// <summary>Двумерный коэффициент поперечного сопротивления по B и d (линейная интерполяция, края — по краю таблицы).</summary>
    public static double Hoerner(double B, double T)
    {
        double x = B / (2.0 * Math.Max(1e-6, T));
        if (x <= HoernerX[0]) return HoernerY[0];
        int n = HoernerX.Length;
        if (x >= HoernerX[n - 1]) return HoernerY[n - 1];
        int i = 1;
        while (HoernerX[i] < x) i++;
        double f = (x - HoernerX[i - 1]) / (HoernerX[i] - HoernerX[i - 1]);
        return HoernerY[i - 1] + (HoernerY[i] - HoernerY[i - 1]) * f;
    }

    /// <summary>Присоединённая масса по продольной оси, кг (Söding, 1982): A₁₁ = 2,7·ρ·∇^(5/3)/L².</summary>
    public static double AddedMassSurge(double mass, double L)
    {
        double nabla = mass / Rho;
        return 2.7 * Rho * Math.Pow(nabla, 5.0 / 3.0) / (L * L);
    }
}

/// <summary>Коэффициенты модели 3 СС для одного набора ShipParameters (размерные + итог согласования).</summary>
public sealed class Manoeuvring3DofCoefficients
{
    public const int Strips = 20;

    // ── Главные данные ──
    public double L, B, T, Cb;
    public double Mass, Iz, XG;
    public double AddedMassSurge;
    public double M11, M22, M23, M32, M33;
    // Обратная матрица 2×2 поперечного движения
    private double inv11, inv12, inv21, inv22;

    // ── Безразмерные значения ──
    public ClarkeDerivatives Clarke;
    public double MassPrime, IzPrime, XGPrime;
    /// <summary>Подобранная Y'_δ (рад⁻¹, &lt; 0: руль на ПБ даёт силу на ЛБ).</summary>
    public double YdeltaPrime;
    /// <summary>Плечо руля x'_R = x_R/L.</summary>
    public double RudderLeverPrime;
    /// <summary>Подобранный множитель демпфирования рыскания k_N (1 — значение Clarke).</summary>
    public double YawDampingFactor = 1.0;
    /// <summary>K' и T' линеаризованной модели после согласования (для проверки и отчёта).</summary>
    public double LinearKPrime, LinearTPrime;
    public double TargetKPrime, TargetTPrime;
    /// <summary>Линеаризация устойчива на курсе (C = Y'_v·N'_r − (Y'_r − m')·N'_v &gt; 0 с учётом k_N).</summary>
    public bool StableOnCourse;
    /// <summary>T' совпал точно (иначе — только K').</summary>
    public bool FitExact;
    public string FitNote = "";

    // ── Размерные коэффициенты (умножаются на |u|) ──
    public double Yv, Yr, Nv, Nr;
    /// <summary>Сила руля на единицу q·δ (q — м²/с², δ — рад), Н.</summary>
    public double Ydelta, Ndelta;
    /// <summary>Поперечное сопротивление: ½ρ·d·C_d·Δx, кг/м²; C_d.</summary>
    public double CrossFlowK, CrossFlowCd;
    private readonly double[] stripX = new double[Strips];

    public double SlipstreamFactor, PropSideForceFactor;

    // ── Согласование циркуляции (п. 3а) ──
    /// <summary>Границы перехода множителя поперечного сопротивления, доли V_эксп.</summary>
    public const double FadeLow = 0.25, FadeHigh = 0.5;
    public double ServiceSpeedMs;
    /// <summary>Множитель C_d на ходу k_∞ (1 — без согласования).</summary>
    public double CrossFlowHighSpeedFactor = 1.0;
    /// <summary>Диаметр установившейся циркуляции (руль 35°, «Полный вперёд»), м: цель (модель Номото/Норрбина) и итог 3 СС.</summary>
    public double CircleTargetD = double.NaN, CircleD = double.NaN;
    /// <summary>Тот же диаметр 3 СС с C_d по Хёрнеру на всех скоростях (k_∞ = 1) — для сравнения.</summary>
    public double CircleHoernerD = double.NaN;
    public bool CircleMatched;
    public string CircleNote = "";

    // ── Кэш (общий для всех моделей с одинаковыми значениями параметров) ──
    private float[] key;
    private static readonly System.Collections.Generic.Dictionary<string, Manoeuvring3DofCoefficients> cache =
        new System.Collections.Generic.Dictionary<string, Manoeuvring3DofCoefficients>();

    private Manoeuvring3DofCoefficients() { }

    /// <summary>Коэффициенты по параметрам судна; пересчёт только при изменении значимых полей.</summary>
    public static Manoeuvring3DofCoefficients For(ShipParameters p, Manoeuvring3DofCoefficients cached)
    {
        float[] k = KeyOf(p);
        if (cached != null && cached.key != null && SameKey(cached.key, k)) return cached;
        string s = string.Join("|", k);
        lock (cache)
        {
            if (cache.TryGetValue(s, out Manoeuvring3DofCoefficients hit)) return hit;
        }
        var c = Build(p);
        c.key = k;
        lock (cache)
        {
            if (cache.Count > 64) cache.Clear();
            cache[s] = c;
        }
        return c;
    }

    private static float[] KeyOf(ShipParameters p) => new[]
    {
        p.length, p.beam, p.draft, p.blockCoefficient, p.nomotoKPrime, p.nomotoTPrime,
        p.yawGyrationRadiusL, p.centreOfGravityX, p.crossFlowDragCoeff, p.rudderLeverL,
        p.rudderSlipstreamFactor, p.propellerSideForceFactor,
        // цель согласования циркуляции — модель Номото/Норрбина этого же ассета
        p.matchTurningCircle ? 1f : 0f, p.serviceSpeedKnots, p.accelerationTimeConstant, p.decelerationTimeConstant,
        (float)p.yawModel, p.norrbinAlphaPrime, p.turnSpeedLossCoeff, p.minCalcSpeed, p.maxRudderAngle, p.rudderRate,
    };

    private static bool SameKey(float[] a, float[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>Построить коэффициенты и согласовать с K', T' ассета.</summary>
    public static Manoeuvring3DofCoefficients Build(ShipParameters p)
    {
        var c = new Manoeuvring3DofCoefficients();
        double rho = HullForces.Rho;
        c.L = Math.Max(1.0, p.length);
        c.B = Math.Max(0.1, p.beam);
        c.T = Math.Max(0.05, p.draft);
        c.Cb = Math.Max(0.2, Math.Min(1.0, p.blockCoefficient));
        double L = c.L;

        c.Mass = rho * c.Cb * L * c.B * c.T;
        c.XG = p.centreOfGravityX;
        double r66 = Math.Max(0.05, p.yawGyrationRadiusL) * L;
        c.Iz = c.Mass * r66 * r66 + c.Mass * c.XG * c.XG;
        c.AddedMassSurge = HullForces.AddedMassSurge(c.Mass, L);

        double q3 = 0.5 * rho * L * L * L;
        c.MassPrime = c.Mass / q3;
        c.XGPrime = c.XG / L;
        c.IzPrime = c.Iz / (q3 * L * L);
        c.Clarke = ClarkeDerivatives.Compute(L, c.B, c.T, c.Cb);
        ClarkeDerivatives d = c.Clarke;

        // Матрица масс (размерная): M_A = ½ρL³·[[−Y'_v̇, −Y'_ṙ·L], [−N'_v̇·L, −N'_ṙ·L²]] (MSS clarke83.m)
        c.M11 = c.Mass + c.AddedMassSurge;
        c.M22 = c.Mass - q3 * d.Yvdot;
        c.M23 = c.Mass * c.XG - q3 * L * d.Yrdot;
        c.M32 = c.Mass * c.XG - q3 * L * d.Nvdot;
        c.M33 = c.Iz - q3 * L * L * d.Nrdot;
        double det = c.M22 * c.M33 - c.M23 * c.M32;
        c.inv11 = c.M33 / det; c.inv12 = -c.M23 / det;
        c.inv21 = -c.M32 / det; c.inv22 = c.M22 / det;

        // Поперечное сопротивление
        c.CrossFlowCd = p.crossFlowDragCoeff > 0f ? p.crossFlowDragCoeff : HullForces.Hoerner(c.B, c.T);
        double dx = L / Strips;
        c.CrossFlowK = 0.5 * rho * c.T * c.CrossFlowCd * dx;
        for (int i = 0; i < Strips; i++) c.stripX[i] = -L / 2.0 + (i + 0.5) * dx;

        c.SlipstreamFactor = Math.Max(0.0, p.rudderSlipstreamFactor);
        c.PropSideForceFactor = Math.Max(0.0, p.propellerSideForceFactor);
        c.RudderLeverPrime = p.rudderLeverL;

        // Согласование с K', T'
        c.TargetKPrime = p.nomotoKPrime;
        c.TargetTPrime = p.nomotoTPrime;
        c.Fit();

        // Размерные линейные коэффициенты (на единицу |u|)
        double q2 = 0.5 * rho * L * L;
        c.Yv = q2 * d.Yv;
        c.Yr = q2 * L * d.Yr;
        c.Nv = q2 * L * d.Nv;
        c.Nr = q2 * L * L * d.Nr * c.YawDampingFactor;
        c.Ydelta = q2 * c.YdeltaPrime;
        c.Ndelta = c.RudderLeverPrime * L * c.Ydelta;

        c.ServiceSpeedMs = Math.Max(0.1, p.ServiceSpeedMs);
        c.FitCircle(p);
        return c;
    }

    // ─────────── Согласование циркуляции (п. 3а) ───────────

    /// <summary>Множитель поперечного сопротивления при продольной скорости u: 1 на малом ходу, k_∞ на полном.</summary>
    public double CrossFlowFactor(double u)
    {
        if (CrossFlowHighSpeedFactor == 1.0) return 1.0;
        double x = (Math.Abs(u) / ServiceSpeedMs - FadeLow) / (FadeHigh - FadeLow);
        if (x <= 0.0) return 1.0;
        if (x >= 1.0) return CrossFlowHighSpeedFactor;
        double s = x * x * (3.0 - 2.0 * x);
        return 1.0 + (CrossFlowHighSpeedFactor - 1.0) * s;
    }

    /// <summary>Руль циркуляции для согласования: 35° (или δ_max, если меньше).</summary>
    public static double CircleRudderDeg(ShipParameters p) => Math.Min(35.0, p.maxRudderAngle);

    private void FitCircle(ShipParameters p)
    {
        CircleTargetD = SteadyCircleDiameter(p, null);
        CrossFlowHighSpeedFactor = 1.0;
        CircleHoernerD = SteadyCircleDiameter(p, this);
        CircleD = CircleHoernerD;
        if (!p.matchTurningCircle) { CircleNote = "согласование циркуляции выключено"; return; }
        if (double.IsNaN(CircleTargetD) || double.IsNaN(CircleHoernerD))
        {
            CircleNote = "циркуляция не установилась — согласование не выполнено";
            return;
        }

        // D растёт с k_∞ (больше сопротивление — меньше угол дрейфа и r); бисекция по ln k на 0,05…20
        double lo = Math.Log(0.05), hi = Math.Log(20.0);
        double dLo = Eval(p, Math.Exp(lo)), dHi = Eval(p, Math.Exp(hi));
        if (double.IsNaN(dLo) || double.IsNaN(dHi) || CircleTargetD < dLo || CircleTargetD > dHi)
        {
            double k = !double.IsNaN(dLo) && CircleTargetD < dLo ? Math.Exp(lo) : Math.Exp(hi);
            CrossFlowHighSpeedFactor = k;
            CircleD = Eval(p, k);
            CircleNote = $"цель вне диапазона k_∞ 0,05…20: k_∞ = {k:0.##}";
            return;
        }
        for (int it = 0; it < 40; it++)
        {
            double mid = 0.5 * (lo + hi);
            double d = Eval(p, Math.Exp(mid));
            if (double.IsNaN(d)) break;
            if (d < CircleTargetD) lo = mid; else hi = mid;
            if (hi - lo < 1e-4) break;
        }
        CrossFlowHighSpeedFactor = Math.Exp(0.5 * (lo + hi));
        CircleD = Eval(p, CrossFlowHighSpeedFactor);
        CircleMatched = Math.Abs(CircleD - CircleTargetD) <= 0.01 * CircleTargetD;
    }

    private double Eval(ShipParameters p, double k)
    {
        CrossFlowHighSpeedFactor = k;
        return SteadyCircleDiameter(p, this);
    }

    /// <summary>
    /// Диаметр установившейся циркуляции D = 2·U/|r|, м: руль 35° сразу, «Полный вперёд», старт с V_эксп.
    /// c = null — модель Номото/Норрбина ассета, иначе 3 СС с этими коэффициентами. NaN — не установилась.
    /// </summary>
    public static double SteadyCircleDiameter(ShipParameters p, Manoeuvring3DofCoefficients c)
    {
        double rudder = CircleRudderDeg(p);
        var m = new ShipDynamicsModel(p)
        {
            U = p.ServiceSpeedMs, SpeedFactor = 1.0, RudderCommandDeg = rudder, Delta = rudder,
            MotionOverride = c != null ? MotionModel.Manoeuvring3Dof : MotionModel.Course1Dof,
        };
        if (c != null) m.PinCoefficients(c);
        const double dt = 0.05;
        double prev = 0.0;
        for (int i = 1; i <= 120000; i++)
        {
            m.Step(dt);
            if (double.IsNaN(m.R) || double.IsNaN(m.U)) return double.NaN;
            if (i % 200 != 0) continue;          // проверка раз в 10 с
            if (i >= 1200 && Math.Abs(m.R - prev) <= 1e-5 * Math.Abs(m.R) + 1e-9)
                return Math.Abs(m.R) > 1e-6 ? 2.0 * m.SpeedThroughWater / Math.Abs(m.R) : double.NaN;
            prev = m.R;
        }
        return double.NaN;
    }

    // ─────────── Линеаризация и согласование ───────────

    /// <summary>
    /// Линеаризованная модель при u' = 1: M'·[v̇', ṙ'] = A'·[v', r'] + b'·δ,
    /// A' = [[Y'_v, Y'_r − m'], [N'_v, k_N·N'_r − m'x'_G]], b' = [Y'_δ, x'_R·Y'_δ].
    /// Передаточная функция r'/δ = K'(1 + T₃'s)/((1 + T₁'s)(1 + T₂'s)), T' = T₁' + T₂' − T₃'.
    /// Возвращает false, если модель неустойчива на курсе (c₀ ≤ 0).
    /// </summary>
    public bool Linearize(double kN, double yDeltaPrime, out double kPrime, out double tPrime, out double t3)
    {
        ClarkeDerivatives d = Clarke;
        double m22 = MassPrime - d.Yvdot, m23 = MassPrime * XGPrime - d.Yrdot;
        double m32 = MassPrime * XGPrime - d.Nvdot, m33 = IzPrime - d.Nrdot;
        double yv = d.Yv, yr = d.Yr - MassPrime, nv = d.Nv, nr = kN * d.Nr - MassPrime * XGPrime;
        double yd = yDeltaPrime, nd = RudderLeverPrime * yDeltaPrime;
        double det = m22 * m33 - m23 * m32;
        double a11 = (m33 * yv - m23 * nv) / det, a12 = (m33 * yr - m23 * nr) / det;
        double a21 = (-m32 * yv + m22 * nv) / det, a22 = (-m32 * yr + m22 * nr) / det;
        double b1 = (m33 * yd - m23 * nd) / det, b2 = (-m32 * yd + m22 * nd) / det;
        double c0 = a11 * a22 - a12 * a21, c1 = -(a11 + a22);
        double num0 = a21 * b1 - a11 * b2;
        kPrime = num0 / c0;
        t3 = Math.Abs(num0) > 1e-15 ? b2 / num0 : 0.0;
        tPrime = c1 / c0 - t3;
        return c0 > 0.0 && c1 > 0.0;
    }

    private void Fit()
    {
        // T₃ и T₁ + T₂ от масштаба Y'_δ не зависят: сначала k_N по T', затем Y'_δ по K'.
        const double probe = -0.01;
        double tTarget = Math.Max(0.05, TargetTPrime);
        Func<double, double> resid = kN =>
        {
            bool ok = Linearize(kN, probe, out _, out double tp, out _);
            return ok ? tp - tTarget : double.NaN;
        };

        // Сетка по k_N 0,05…20 (логарифмическая): ищем смену знака невязки на устойчивом участке
        double bestK = double.NaN, bestAbs = double.PositiveInfinity;
        double prevK = double.NaN, prevR = double.NaN;
        double lo = double.NaN, hi = double.NaN;
        for (int i = 0; i <= 200; i++)
        {
            double kN = 0.05 * Math.Pow(400.0, i / 200.0);
            double r = resid(kN);
            if (double.IsNaN(r)) { prevK = double.NaN; prevR = double.NaN; continue; }
            if (Math.Abs(r) < bestAbs) { bestAbs = Math.Abs(r); bestK = kN; }
            // Смена знака — корень; из нескольких берётся ближайший к k_N = 1 (меньше всего правит Clarke)
            bool bracket = !double.IsNaN(prevR) && Math.Sign(prevR) != Math.Sign(r);
            if (bracket && (double.IsNaN(lo) || Math.Abs(Math.Log(prevK * kN)) < Math.Abs(Math.Log(lo * hi))))
            {
                lo = prevK; hi = kN;
            }
            prevK = kN; prevR = r;
        }

        if (!double.IsNaN(lo))
        {
            double rLo = resid(lo);
            for (int it = 0; it < 100; it++)
            {
                double mid = 0.5 * (lo + hi);
                double rm = resid(mid);
                if (double.IsNaN(rm)) break;
                if (Math.Sign(rm) == Math.Sign(rLo)) { lo = mid; rLo = rm; } else hi = mid;
                if (hi - lo < 1e-12 * hi) break;
            }
            YawDampingFactor = 0.5 * (lo + hi);
            FitExact = true;
        }
        else if (!double.IsNaN(bestK))
        {
            YawDampingFactor = bestK;
            FitExact = false;
            FitNote = $"T' не согласован: наименьшая невязка при k_N = {bestK:F3}";
        }
        else
        {
            YawDampingFactor = 1.0;
            FitExact = false;
            FitNote = "линеаризация неустойчива на курсе при любом k_N из 0,05…20";
        }

        // Y'_δ — по K' (K' линеен по Y'_δ)
        Linearize(YawDampingFactor, probe, out double kProbe, out _, out _);
        YdeltaPrime = Math.Abs(kProbe) > 1e-15 ? probe * TargetKPrime / kProbe : 0.0;
        StableOnCourse = Linearize(YawDampingFactor, YdeltaPrime, out LinearKPrime, out LinearTPrime, out _);
    }

    /// <summary>Критерий устойчивости на курсе при заданном k_N: C' = Y'_v·N_r' − (Y'_r − m')·N'_v (N_r' с учётом m'x'_G).</summary>
    public double StabilityIndex(double kN)
    {
        ClarkeDerivatives d = Clarke;
        double yr = d.Yr - MassPrime, nr = kN * d.Nr - MassPrime * XGPrime;
        return d.Yv * nr - yr * d.Nv;
    }

    // ─────────── Силы ───────────

    /// <summary>Поперечное сопротивление по полосам при продольной скорости u, Н и Н·м.</summary>
    public void CrossFlow(double u, double v, double r, out double y, out double n)
    {
        y = 0.0; n = 0.0;
        double k = CrossFlowK * CrossFlowFactor(u);
        for (int i = 0; i < Strips; i++)
        {
            double x = stripX[i];
            double w = v + x * r;
            double f = -k * Math.Abs(w) * w;
            y += f;
            n += x * f;
        }
    }

    /// <summary>Решить M₂₂·[v̇, ṙ] = [Y, N].</summary>
    public void SolveSway(double y, double n, out double vdot, out double rdot)
    {
        vdot = inv11 * y + inv12 * n;
        rdot = inv21 * y + inv22 * n;
    }

    /// <summary>Аналитика для тестов и отчёта: установившаяся скорость лагом под силой F на миделе (u = 0), м/с.</summary>
    public double SteadyDriftSpeed(double force) =>
        Math.Sign(force) * Math.Sqrt(Math.Abs(force) / (0.5 * HullForces.Rho * T * CrossFlowCd * L));

    /// <summary>Аналитика: установившаяся угловая скорость под моментом N (u = v = 0), рад/с; ∫x²|x|dx = L⁴/32.</summary>
    public double SteadySpinRate(double moment) =>
        Math.Sign(moment) * Math.Sqrt(Math.Abs(moment) / (0.5 * HullForces.Rho * T * CrossFlowCd * Math.Pow(L, 4) / 32.0));
}
