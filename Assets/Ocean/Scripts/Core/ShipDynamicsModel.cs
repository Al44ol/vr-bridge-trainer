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
// ============================================================
using System;
using UnityEngine;

public sealed class ShipDynamicsModel
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
        U = uSet + (U - uSet) * Math.Exp(-dt / tau);

        // 3. Поворот — модель Номото 1-го порядка
        double L = Math.Max(1.0, p.length);
        double Uc = Math.Max(Math.Abs(U), Math.Max(1e-4, p.minCalcSpeed));
        // K — по фактической скорости (A3): на стопе K → 0 и руль не поворачивает судно.
        // До A3 здесь было K'·sign(u)·Uc/L: при |u| < U_min судно медленно вращалось
        // на месте (≈ 6° за 10 мин при L = 150 м). При |u| ≥ U_min результат тот же бит в бит.
        // U_min остаётся только в T = T'·L/U (защита от деления на ноль).
        double K = p.nomotoKPrime * U / L;
        double T = p.nomotoTPrime * L / Uc;
        double rSteady = K * Delta * Mathf.Deg2Rad;
        R = rSteady + (R - rSteady) * Math.Exp(-dt / T);

        // 4. Кинематика
        Psi = Wrap2Pi(Psi + R * dt);
        East += U * Math.Sin(Psi) * dt;
        North += U * Math.Cos(Psi) * dt;
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
