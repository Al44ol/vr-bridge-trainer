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
//
// Посадка на мель (B1; при Grounded = false шаг прежний бит в бит):
//   s = sign(u) в момент касания (GroundedDirection).
//   • Ход в сторону s («наползание» на грунт) гаснет: u(t+dt) = u·e^(−dt/τ_ост),
//     путь наползания ≈ u₀·τ_ост; работа машиной в сторону s судно не двигает.
//   • Ход от мели (−s) разрешён с повышенным сопротивлением:
//     u_цель = k_снят·u_зад (если u_зад·s < 0, иначе 0), du/dt = (u_цель − u)/τ_разг.
//   • Поворот: r(t+dt) = r·e^(−dt/τ_ост) — корпус лежит на грунте, руль не действует.
//   Снятие (Grounded = false) решает детектор: контур корпуса вышел из мели.
//
// Вводные инструктора (D1; при выключенных флагах шаг прежний бит в бит):
//   • EngineFailed — отказ главного двигателя: u_зад = 0 при любом телеграфе, ход гаснет
//     с τ_торм (тот же закон, что «Стоп»; выбег без упора винта, сопротивление — в τ_торм);
//   • SteeringFailed — отказ рулевого устройства: δ остаётся на месте (рулевая машина
//     не отрабатывает δ_зад), заданный руль по-прежнему идёт от штурвала (видно расхождение).
//
// Модель 3 СС (запрос 19.1; Manoeuvring3Dof.cs): включается полем motionModel ассета или
// MotionOverride (переключатель в сцене). Тогда шаги 2–4 заменяет одна система уравнений
// продольного, поперечного движения и рыскания (RK4), появляется поперечная скорость V
// (> 0 — на правый борт) и внешние силы AddForce (буксиры, концы, кранцы — части 19.2–19.3).
// Рулевая машина, отказы, посадка на мель — те же. Кинематика:
//   E += (u·sin ψ + v·cos ψ)·dt,  N += (u·cos ψ − v·sin ψ)·dt.
// В модели Номото V = 0, внешние силы не учитываются (сбрасываются после шага).
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
    /// <summary>Поперечная скорость, м/с (> 0 — на правый борт); только в модели 3 СС, иначе 0.</summary>
    public double V;

    // ── Модель 3 СС (запрос 19.1) ──
    /// <summary>Модель движения вместо указанной в ассете (null — как в ассете).</summary>
    public MotionModel? MotionOverride;
    /// <summary>Действующая модель движения.</summary>
    public MotionModel ActiveMotionModel =>
        MotionOverride ?? (Parameters != null ? Parameters.motionModel : MotionModel.Course1Dof);
    public bool Uses3Dof => ActiveMotionModel == MotionModel.Manoeuvring3Dof;
    /// <summary>Коэффициенты модели 3 СС (строятся при первом шаге и при смене параметров).</summary>
    public Manoeuvring3DofCoefficients Coefficients3Dof =>
        coeffPinned ? coeff : (coeff = Manoeuvring3DofCoefficients.For(Parameters, coeff));
    private Manoeuvring3DofCoefficients coeff;
    private bool coeffPinned;

    /// <summary>Работать с заданными коэффициентами 3 СС (подбор при их построении), а не по параметрам.</summary>
    internal void PinCoefficients(Manoeuvring3DofCoefficients c)
    {
        coeff = c;
        coeffPinned = true;
    }
    // Внешние силы, набранные к следующему шагу (связанные оси): Н, Н, Н·м
    private double extX, extY, extN;
    /// <summary>Внешние силы последнего шага (для индикации): продольная, поперечная, Н; момент, Н·м.</summary>
    public double LastExternalX { get; private set; }
    public double LastExternalY { get; private set; }
    public double LastExternalN { get; private set; }

    /// <summary>
    /// Внешняя сила на следующий шаг (только модель 3 СС): fx — в нос, fy — на правый борт, Н;
    /// xPoint — точка приложения от миделя, м (> 0 — в нос). Сбрасывается после каждого шага.
    /// </summary>
    public void AddForce(double fx, double fy, double xPoint = 0.0)
    {
        extX += fx; extY += fy; extN += xPoint * fy;
    }

    /// <summary>Внешний момент относительно вертикальной оси на следующий шаг, Н·м (> 0 — нос вправо).</summary>
    public void AddMoment(double n) => extN += n;

    /// <summary>Скорость относительно грунта: восток, север, м/с (с учётом поперечной V).</summary>
    public double VelocityEast => U * Math.Sin(Psi) + V * Math.Cos(Psi);
    public double VelocityNorth => U * Math.Cos(Psi) - V * Math.Sin(Psi);

    // ── Входы ──
    /// <summary>Команда руля, градусы (> 0 — правый борт), до ограничения δ_max.</summary>
    public double RudderCommandDeg;
    /// <summary>Положение телеграфа SF, −1..1.</summary>
    public double SpeedFactor;
    /// <summary>Удерживать скорость = V_эксп (тест циркуляции F5 в ShipNavigation).</summary>
    public bool HoldServiceSpeed;
    /// <summary>D1: отказ главного двигателя — u_зад = 0, телеграф не действует.</summary>
    public bool EngineFailed;
    /// <summary>D1: отказ рулевого устройства — фактический руль δ не меняется.</summary>
    public bool SteeringFailed;

    // ── Посадка на мель (B1) ──
    /// <summary>Судно на мели (ставит GroundingMonitor через SetGrounded).</summary>
    public bool Grounded { get; private set; }
    /// <summary>Знак хода в момент касания: +1 — шло вперёд, −1 — назад.</summary>
    public int GroundedDirection { get; private set; } = 1;
    /// <summary>Постоянная остановки на грунте τ_ост, с.</summary>
    public double GroundingStopTime = 1.0;
    /// <summary>Доля скорости при съёме с мели k_снят (от u_зад телеграфа).</summary>
    public double RefloatSpeedFactor = 0.3;

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
        if (!SteeringFailed) Delta += Clamp(Ordered - Delta, -maxStep, maxStep);

        if (Grounded)
        {
            StepGrounded(dt);
            ClearExternal();
            return;
        }
        if (Uses3Dof)
        {
            Step3Dof(dt);
            ClearExternal();
            return;
        }
        V = 0.0;

        // 2. Скорость
        double vService = p.ServiceSpeedMs;
        double sf = EngineFailed ? 0.0 : SpeedFactor;
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
        ClearExternal();
    }

    private void ClearExternal()
    {
        LastExternalX = extX; LastExternalY = extY; LastExternalN = extN;
        extX = extY = extN = 0.0;
    }

    // ─────────── Модель 3 СС (запрос 19.1) ───────────

    // Входы шага, замороженные на время RK4
    private double s3uSet, s3tau, s3delta, s3sideForce;
    private bool s3hold;

    private void Step3Dof(double dt)
    {
        ShipParameters p = Parameters;
        Manoeuvring3DofCoefficients c = Coefficients3Dof;

        // Заданная скорость и τ — как в модели Номото (шаг 2)
        double vService = p.ServiceSpeedMs;
        double sf = EngineFailed ? 0.0 : SpeedFactor;
        double uSet = sf >= 0.0 ? sf * vService : sf * vService * p.asternFactor;
        s3hold = HoldServiceSpeed;
        if (HoldServiceSpeed)
        {
            uSet = vService;
            U = vService;
        }
        bool accelerating = Math.Abs(uSet) > Math.Abs(U) && U * uSet >= 0.0;
        s3uSet = uSet;
        s3tau = Math.Max(0.1, accelerating ? p.accelerationTimeConstant : p.decelerationTimeConstant);
        s3delta = Delta * Mathf.Deg2Rad;
        // Поперечная сила винта на заднем ходу: c_в·T_зх, T_зх = m₁₁·|u_зад|/τ_разг (на правый винт — корма влево)
        s3sideForce = uSet < 0.0
            ? c.PropSideForceFactor * c.M11 * Math.Abs(uSet) / Math.Max(0.1, p.accelerationTimeConstant)
            : 0.0;

        // RK4 по (u, v, r)
        double u0 = U, v0 = V, r0 = R;
        Derivs3Dof(c, u0, v0, r0, out double ku1, out double kv1, out double kr1);
        Derivs3Dof(c, u0 + 0.5 * dt * ku1, v0 + 0.5 * dt * kv1, r0 + 0.5 * dt * kr1, out double ku2, out double kv2, out double kr2);
        Derivs3Dof(c, u0 + 0.5 * dt * ku2, v0 + 0.5 * dt * kv2, r0 + 0.5 * dt * kr2, out double ku3, out double kv3, out double kr3);
        Derivs3Dof(c, u0 + dt * ku3, v0 + dt * kv3, r0 + dt * kr3, out double ku4, out double kv4, out double kr4);
        U = u0 + dt / 6.0 * (ku1 + 2.0 * ku2 + 2.0 * ku3 + ku4);
        V = v0 + dt / 6.0 * (kv1 + 2.0 * kv2 + 2.0 * kv3 + kv4);
        R = r0 + dt / 6.0 * (kr1 + 2.0 * kr2 + 2.0 * kr3 + kr4);

        // Кинематика (как в шаге 4, с поперечной скоростью)
        Psi = Wrap2Pi(Psi + R * dt);
        double sin = Math.Sin(Psi), cos = Math.Cos(Psi);
        East += (U * sin + V * cos) * dt;
        North += (U * cos - V * sin) * dt;
    }

    /// <summary>Правые части модели 3 СС (формулы — в шапке Manoeuvring3Dof.cs).</summary>
    private void Derivs3Dof(Manoeuvring3DofCoefficients c, double u, double v, double r,
                            out double du, out double dv, out double dr)
    {
        double m = c.Mass, xg = c.XG;
        double au = Math.Abs(u);

        // Продольное движение: закон телеграфа + связь с поперечным движением + внешняя сила
        du = s3hold ? 0.0 : (s3uSet - u) / s3tau + (m * v * r + m * xg * r * r + extX) / c.M11;

        // Руль: скоростной напор с учётом струи винта при работе вперёд
        double q = u * au;
        if (s3uSet > 0.0) q += c.SlipstreamFactor * Math.Max(0.0, s3uSet * s3uSet - q);

        c.CrossFlow(u, v, r, out double ycf, out double ncf);
        double halfL = 0.5 * c.L;
        double y = au * (c.Yv * v + c.Yr * r) - m * u * r + ycf + c.Ydelta * q * s3delta - s3sideForce + extY;
        double n = u * c.Nv * v + au * c.Nr * r - m * xg * u * r + ncf + c.Ndelta * q * s3delta + halfL * s3sideForce + extN;
        c.SolveSway(y, n, out dv, out dr);
    }

    /// <summary>Посадить на мель / снять с мели. При посадке запоминается направление хода.</summary>
    public void SetGrounded(bool grounded)
    {
        if (grounded && !Grounded)
            GroundedDirection = U >= 0.0 ? 1 : -1;
        Grounded = grounded;
    }

    // Шаг на мели: скорость и поворот (рулевая машина уже отработала)
    private void StepGrounded(double dt)
    {
        ShipParameters p = Parameters;
        double s = GroundedDirection;
        double decay = Math.Exp(-dt / Math.Max(0.05, GroundingStopTime));

        if (U * s > 0.0)
        {
            // Наползание на грунт: ход в сторону мели гаснет независимо от машины
            U *= decay;
            if (Math.Abs(U) < 1e-3) U = 0.0;
        }
        else
        {
            // Съём: только от мели, с повышенным сопротивлением грунта
            double vService = p.ServiceSpeedMs;
            double sf = EngineFailed ? 0.0 : SpeedFactor;
            double uSet = sf >= 0.0 ? sf * vService : sf * vService * p.asternFactor;
            double uTarget = uSet * s < 0.0 ? uSet * RefloatSpeedFactor : 0.0;
            double tau = Math.Max(0.1, p.accelerationTimeConstant);
            U = uTarget + (U - uTarget) * Math.Exp(-dt / tau);
        }

        R *= decay;
        if (Math.Abs(R) < 1e-7) R = 0.0;

        Psi = Wrap2Pi(Psi + R * dt);
        East += U * Math.Sin(Psi) * dt;
        North += U * Math.Cos(Psi) * dt;

        // 3 СС: поперечное движение на грунте гаснет так же (в модели Номото V = 0 — шаг прежний)
        if (V != 0.0)
        {
            V *= decay;
            if (Math.Abs(V) < 1e-3) V = 0.0;
            East += V * Math.Cos(Psi) * dt;
            North -= V * Math.Sin(Psi) * dt;
        }
    }

    /// <summary>Скорость относительно воды по модулю, м/с: |u| в модели Номото, √(u² + v²) в модели 3 СС.</summary>
    public double SpeedThroughWater => V == 0.0 ? Math.Abs(U) : Math.Sqrt(U * U + V * V);

    /// <summary>
    /// Мгновенно переставить судно, без поворота (r = 0): снятие с мели инструктором (B1) —
    /// стоп; начальные условия сценария (B2) — скорость u (м/с) и фактический руль δ (градусы).
    /// Без δ руль не трогается (как в B1).
    /// </summary>
    public void Teleport(double east, double north, double psi, double u = 0.0, double? deltaDeg = null)
    {
        East = east; North = north; Psi = Wrap2Pi(psi);
        U = u; R = 0.0; V = 0.0;
        if (deltaDeg.HasValue)
        {
            double max = Parameters != null ? Parameters.maxRudderAngle : 35.0;
            Delta = Ordered = Clamp(deltaDeg.Value, -max, max);
        }
        Grounded = false;
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
