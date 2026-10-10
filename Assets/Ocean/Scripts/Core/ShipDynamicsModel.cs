// ============================================================
// ShipDynamicsModel.cs — Математическая модель движения судна (A3)
//
// Чистый C#-класс без MonoBehaviour и сцены: используется ShipNavigation
// (в FixedUpdate), стендом манёвренных испытаний (без Play Mode) и тестами.
// Вынесен из ShipNavigation.Step без изменений — порядок операций и типы
// сохранены, чтобы результат совпадал бит в бит (проверяется тестом).
//
// Обозначения: ψ — курс (рад, 0 = N, по часовой), r = dψ/dt (рад/с, > 0 — вправо),
// u — скорость хода (м/с, < 0 — задний ход), δ — фактическая перекладка
// (градусы, > 0 — правый борт), L — длина судна, SF — телеграф (−1..1).
//
// 1. Рулевая машина: δ += clamp(δ_зад − δ, −ω_δ·dt, +ω_δ·dt), |δ_зад| ≤ δ_max.
// 2. Скорость: du/dt = (u_зад − u)/τ,
//        u_зад = SF·V_эксп (SF ≥ 0),  SF·V_эксп·k_зх (SF < 0);
//        τ = τ_разг, если |u| растёт к u_зад того же знака, иначе τ_торм;
//        точно: u(t+dt) = u_зад + (u − u_зад)·e^(−dt/τ).
// 3. Поворот (Номото 1-го порядка): T·ṙ + r = K·δ,
//        K = K'·u/L (u со знаком), T = T'·L/U, U = max(|u|, U_min);
//        точно: r(t+dt) = K·δ + (r − K·δ)·e^(−dt/T).
// 4. Кинематика: ψ += r·dt; E += u·sin ψ·dt; N += u·cos ψ·dt.
// Подробные пояснения — в шапке ShipNavigation.cs.
//
// Расширения A3.2 (по умолчанию выключены — тогда шаг совпадает с прежним бит в бит):
// 2а. Потеря скорости на циркуляции: du/dt = (u_зад − u)/τ − C_r'·L·r².
//     Обоснование: в уравнении продольной силы модели Абковица (Abkowitz, 1964)
//     потерю скорости на повороте дают члены X'_vv·v'², X'_rr·r'², X'_vr·v'·r';
//     на циркуляции v' ∝ r', поэтому все три ∝ r'². В размерном виде
//     ΔX/m = (X'/m')·L·r², отсюда безразмерный C_r'. Шаг точный при r = const:
//         u(t+dt) = u* + (u − u*)·e^(−dt/τ), u* = u_зад − sign(u)·C_r'·L·r²·τ;
//     установившаяся потеря Δu = τ·C_r'·L·r². Потеря только уменьшает |u|.
// 3а. Норрбин (Norrbin, 1963): T·ṙ + r + α·r³ = K·δ, α = α'·(L/U)², α' ≥ 0.
//     Шаг: экспонента с «замороженным» множителем g = 1 + α·r̄²
//     (T_эфф = T/g, r_уст = K·δ/g), r̄ — середина шага (предиктор-корректор).
//     Устойчиво при любом dt; в установившемся режиме r + α·r³ = K·δ точно.
// ============================================================
using System;
using UnityEngine;

public sealed class ShipDynamicsModel : IVesselModel
{
    public ShipParameters Parameters;

    // ── Состояние ──
    /// <summary>Курс, рад, 0..2π (0 = N, по часовой).</summary>
    public double Psi;
    /// <summary>Угловая скорость, рад/с (> 0 — поворот вправо).</summary>
    public double R;
    /// <summary>Скорость хода, м/с (< 0 — задний ход).</summary>
    public double U;
    /// <summary>Фактический угол руля, градусы (> 0 — правый борт).</summary>
    public double Delta;
    /// <summary>Заданный угол руля после ограничения δ_max, градусы.</summary>
    public double Ordered;
    /// <summary>Позиция: восток, север, м.</summary>
    public double East, North;

    // ── Входы ──
    /// <summary>Команда руля, градусы (> 0 — правый борт), до ограничения δ_max.</summary>
    public double RudderCommandDeg;
    /// <summary>Положение телеграфа SF, −1..1.</summary>
    public double SpeedFactor;
    /// <summary>Удерживать скорость = V_эксп (тест циркуляции F5 в ShipNavigation).</summary>
    public bool HoldServiceSpeed;

    public ShipDynamicsModel(ShipParameters parameters)
    {
        Parameters = parameters;
    }

    /// <summary>Один шаг интегрирования, dt — с.</summary>
    public void Step(double dt)
    {
        ShipParameters p = Parameters;

        // 1. Рулевая машина
        double maxRudder = p.maxRudderAngle;
        Ordered = Clamp(RudderCommandDeg, -maxRudder, maxRudder);
        double maxStep = p.rudderRate * dt;
        Delta += Clamp(Ordered - Delta, -maxStep, maxStep);

        // 2. Скорость
        double vService = p.ServiceSpeedMs;
        double sf = SpeedFactor;
        double uSet = sf >= 0.0 ? sf * vService : sf * vService * p.asternFactor;
        if (HoldServiceSpeed)
        {
            uSet = vService;
            U = vService;
        }
        bool accelerating = Math.Abs(uSet) > Math.Abs(U) && U * uSet >= 0.0;
        double tau = Math.Max(0.1, accelerating ? p.accelerationTimeConstant : p.decelerationTimeConstant);
        double L = Math.Max(1.0, p.length);
        double uTarget = uSet;
        if (p.turnSpeedLossCoeff != 0f && !HoldServiceSpeed && Math.Abs(U) > p.minCalcSpeed)
        {
            // 2а. Потеря скорости на циркуляции (A3.2)
            double lossAccel = p.turnSpeedLossCoeff * L * R * R;      // м/с²
            uTarget = uSet - Math.Sign(U) * lossAccel * tau;
            // Потеря не разворачивает ход: если цель без неё того же знака, что u, — не дальше нуля.
            // (При торможении «назад» u_зад и u разных знаков — цель не трогаем.)
            if (uSet * U >= 0.0 && uTarget * U < 0.0) uTarget = 0.0;
        }
        U = uTarget + (U - uTarget) * Math.Exp(-dt / tau);

        // 3. Поворот — модель Номото 1-го порядка (или Норрбина, A3.2)
        double Uc = Math.Max(Math.Abs(U), Math.Max(1e-4, p.minCalcSpeed));
        // K — по фактической скорости (A3): на стопе K → 0 и руль не поворачивает судно.
        // До A3 здесь было K'·sign(u)·Uc/L: при |u| < U_min судно медленно вращалось
        // на месте (≈ 6° за 10 мин при L = 150 м). При |u| ≥ U_min результат тот же бит в бит.
        // U_min остаётся только в T = T'·L/U (защита от деления на ноль).
        double K = p.nomotoKPrime * U / L;
        double T = p.nomotoTPrime * L / Uc;
        double rSteady = K * Delta * Mathf.Deg2Rad;
        double alpha = p.yawModel == YawModel.Norrbin ? Math.Max(0.0, p.norrbinAlphaPrime) * (L / Uc) * (L / Uc) : 0.0;
        if (alpha == 0.0)
        {
            R = rSteady + (R - rSteady) * Math.Exp(-dt / T);
        }
        else
        {
            // 3а. Норрбин: предиктор по r в начале шага, корректор по середине шага
            double g = 1.0 + alpha * R * R;
            double rPred = rSteady / g + (R - rSteady / g) * Math.Exp(-dt * g / T);
            double rMid = 0.5 * (R + rPred);
            g = 1.0 + alpha * rMid * rMid;
            R = rSteady / g + (R - rSteady / g) * Math.Exp(-dt * g / T);
        }

        // 4. Кинематика
        Psi = Wrap2Pi(Psi + R * dt);
        East += U * Math.Sin(Psi) * dt;
        North += U * Math.Cos(Psi) * dt;
    }

    // ── IVesselModel (стенд испытаний) ──
    double IVesselModel.Psi => Psi;
    double IVesselModel.R => R;
    double IVesselModel.U => U;
    double IVesselModel.Delta => Delta;
    double IVesselModel.Ordered => Ordered;
    double IVesselModel.East => East;
    double IVesselModel.North => North;
    double IVesselModel.Length => Math.Max(1.0, Parameters.length);
    double IVesselModel.RudderCommandDeg { get => RudderCommandDeg; set => RudderCommandDeg = value; }
    double IVesselModel.SpeedFactor { get => SpeedFactor; set => SpeedFactor = value; }

    /// <summary>
    /// Установившаяся угловая скорость при руле δ и скорости u, рад/с:
    /// Номото r = K·δ; Норрбин — корень r + α·r³ = K·δ (Ньютон).
    /// </summary>
    public static double SteadyYawRate(ShipParameters p, double u, double deltaDeg)
    {
        double L = Math.Max(1.0, p.length);
        double Uc = Math.Max(Math.Abs(u), Math.Max(1e-4, p.minCalcSpeed));
        double kd = p.nomotoKPrime * u / L * deltaDeg * Mathf.Deg2Rad;   // как в Step
        double alpha = p.yawModel == YawModel.Norrbin ? Math.Max(0.0, p.norrbinAlphaPrime) * (L / Uc) * (L / Uc) : 0.0;
        if (alpha == 0.0) return kd;
        double r = kd;
        for (int i = 0; i < 50; i++)
        {
            double f = r + alpha * r * r * r - kd;
            double step = f / (1.0 + 3.0 * alpha * r * r);
            r -= step;
            if (Math.Abs(step) < 1e-15) break;
        }
        return r;
    }

    /// <summary>Установившаяся скорость при положении телеграфа SF, м/с.</summary>
    public static double SteadySpeed(ShipParameters p, double sf) =>
        sf >= 0.0 ? sf * p.ServiceSpeedMs : sf * p.ServiceSpeedMs * p.asternFactor;

    public static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

    public static double Wrap2Pi(double a)
    {
        a %= 2.0 * Math.PI;
        return a < 0 ? a + 2.0 * Math.PI : a;
    }
}
