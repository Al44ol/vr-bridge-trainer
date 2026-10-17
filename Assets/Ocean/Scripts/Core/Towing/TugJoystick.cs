// ============================================================
// TugJoystick.cs — Свой буксир: джойстик → две азимутальные колонки (запрос 19.4а, Core)
//
// Обучающийся на буксире (тип судна «буксир») управляет колонками одной рукояткой, как на
// современных ASD-буксирах: наклон вперёд/назад — ход, вбок — лагом, поворот рукоятки — разворот.
// Положение джойстика j = (j_x, j_y, j_r), каждая ось −1…1 (> 0 — вперёд / на ПБ / нос вправо).
//
// 1. Мёртвая зона: j' = sign(j)·(|j| − z)/(1 − z) при |j| > z, иначе 0; z = 0,1 — рука не держит
//    рукоятку идеально в нуле.
// 2. Требуемые обобщённые силы в осях буксира:
//        X = j'_x·X_max,  Y = j'_y·Y_max,  N = j'_r·N_max;
//    X_max = T_гак (обе колонки вперёд или назад — колонка поворачивается на 180°);
//    Y_max — наибольшая чисто поперечная сила без момента: колонки в корме (x_к < 0) дают момент x_к·Y,
//    его гасит разность продольных упоров f_x1 − f_x2 = x_к·Y/y_к (AzimuthAllocation) — тогда упор колонки
//    |f| = (|Y|/2)·√(1 + (x_к/y_к)²) ≤ T_гак/2 → Y_max = T_гак/√(1 + (x_к/y_к)²) (у буксира 22 × 8 м ≈ 0,22·T_гак);
//    N_max — чистый разворот разностью упоров: |f_x| = |N|/(2y_к) ≤ T_гак/2 → N_max = y_к·T_гак.
//    Наклон по двум осям сразу — сумма; если упор колонки больше предела, AzimuthAllocation уменьшает
//    все составляющие в одно число раз — направление движения, заданное джойстиком, сохраняется.
// 3. Набор упора — звено 1-го порядка τ_к = 2 с (как у буксира 19.3а, TugVessel): поворот колонок
//    и раскрутка винтов не моделируются отдельно.
// 4. Силы — в модель 3 СС своего судна (ShipDynamicsModel.AddForce/AddMoment) до шага; телеграф и руль
//    на буксире в модель не идут (SF = 0: закон телеграфа даёт линейное сопротивление корпуса, ShipPresets.FillTug).
// Рукоятка в сцене: наклон до MaxTiltDeg и поворот до MaxTwistDeg — полный упор по оси (FromHandle).
// 19.4а-4 (по проверке в шлеме — «не понимаю, сколько даю»): ход рукоятки больше (35° / 45° вместо 20° / 30°);
// ступени, как у телеграфа: упор 25 / 50 / 75 / 100 % («самый малый / малый / средний / полный») — положение
// рукоятки ступени k: j = sign(k)·(z + (1 − z)·|k|/4) — мёртвая зона учтена, j' = |k|/4 ровно (StepPosition);
// подписи меню и событий — в процентах упора j', а не хода рукоятки.
// ============================================================
using System;

/// <summary>Положение джойстика: ход, лагом, поворот — каждое −1…1 (> 0 — вперёд / на ПБ / нос вправо).</summary>
public struct JoystickState
{
    public double Surge, Sway, Yaw;

    public JoystickState(double surge, double sway, double yaw)
    {
        Surge = Clamp(surge); Sway = Clamp(sway); Yaw = Clamp(yaw);
    }

    public static JoystickState Zero => default;
    public bool IsZero => Surge == 0.0 && Sway == 0.0 && Yaw == 0.0;

    /// <summary>Наибольшая разница по осям (для порога «положение сменилось»).</summary>
    public double DistanceTo(JoystickState o) =>
        Math.Max(Math.Abs(Surge - o.Surge), Math.Max(Math.Abs(Sway - o.Sway), Math.Abs(Yaw - o.Yaw)));

    private static double Clamp(double v) => double.IsNaN(v) ? 0.0 : Math.Max(-1.0, Math.Min(1.0, v));
}

public static class TugJoystick
{
    /// <summary>Мёртвая зона по каждой оси (доля хода рукоятки).</summary>
    public const double DeadZone = 0.1;
    /// <summary>Шаг упора одной ступени (меню «Джойстик», ступени рукоятки).</summary>
    public const double MenuStep = 0.25;
    /// <summary>Ступеней в каждую сторону: 25 / 50 / 75 / 100 % упора.</summary>
    public const int StepCount = 4;
    /// <summary>Наклон рукоятки на полный упор по оси, °; поворот рукоятки на полный разворот, ° (19.4а-4: было 20 / 30).</summary>
    public const double MaxTiltDeg = 35.0, MaxTwistDeg = 45.0;

    /// <summary>Ось с мёртвой зоной: 0 внутри DeadZone, затем линейно до ±1.</summary>
    public static double Shape(double v)
    {
        double a = Math.Abs(v);
        if (a <= DeadZone) return 0.0;
        return Math.Sign(v) * Math.Min(1.0, (a - DeadZone) / (1.0 - DeadZone));
    }

    /// <summary>Обратное Shape: доля упора (−1…1) → положение рукоятки (0 → 0).</summary>
    public static double Unshape(double f)
    {
        if (Math.Abs(f) < 1e-9) return 0.0;
        f = Math.Max(-1.0, Math.Min(1.0, f));
        return Math.Sign(f) * (DeadZone + (1.0 - DeadZone) * Math.Abs(f));
    }

    /// <summary>19.4а-4: положение рукоятки ступени k (−4…4): упор ровно k·25 %.</summary>
    public static double StepPosition(int k)
    {
        k = Math.Max(-StepCount, Math.Min(StepCount, k));
        return Unshape(k * MenuStep);
    }

    /// <summary>Ближайшая ступень к положению рукоятки (по положению — как телеграф по углу рычага).</summary>
    public static int NearestStep(double v)
    {
        int best = 0;
        double bestD = double.MaxValue;
        for (int k = -StepCount; k <= StepCount; k++)
        {
            double d = Math.Abs(v - StepPosition(k));
            if (d < bestD) { bestD = d; best = k; }
        }
        return best;
    }

    /// <summary>Положение рукоятки, поставленное в ближайшую ступень.</summary>
    public static double Snap(double v) => StepPosition(NearestStep(v));

    public static JoystickState Snap(JoystickState j) => new JoystickState(Snap(j.Surge), Snap(j.Sway), Snap(j.Yaw));

    /// <summary>Название ступени хода: 0 — «стоп», 1 — «самый малый», 2 — «малый», 3 — «средний», 4 — «полный».</summary>
    public static string StepName(int k)
    {
        switch (Math.Abs(k))
        {
            case 0: return "стоп";
            case 1: return "самый малый";
            case 2: return "малый";
            case 3: return "средний";
            default: return "полный";
        }
    }

    /// <summary>Положение по наклону рукоятки (вперёд, на ПБ) и повороту (по часовой сверху), °.</summary>
    public static JoystickState FromHandle(double tiltForwardDeg, double tiltStarboardDeg, double twistDeg) =>
        new JoystickState(tiltForwardDeg / MaxTiltDeg, tiltStarboardDeg / MaxTiltDeg, twistDeg / MaxTwistDeg);

    public static double MaxSurge(TugSpec s) => s.BollardPull;
    public static double MaxSway(TugSpec s)
    {
        double k = s.ThrusterX / Math.Max(1e-6, Math.Abs(s.ThrusterY));
        return s.BollardPull / Math.Sqrt(1.0 + k * k);
    }
    public static double MaxYaw(TugSpec s) => Math.Abs(s.ThrusterY) * s.BollardPull;

    /// <summary>Требуемые обобщённые силы (оси буксира), Н, Н·м — с мёртвой зоной, до ограничения колонок.</summary>
    public static void Demand(JoystickState j, TugSpec s, out double x, out double y, out double n)
    {
        x = Shape(j.Surge) * MaxSurge(s);
        y = Shape(j.Sway) * MaxSway(s);
        n = Shape(j.Yaw) * MaxYaw(s);
    }

    /// <summary>Ступень меню: положение рукоятки следующей ступени (19.4а-4: ступени упора 25 %, не хода рукоятки).</summary>
    public static double Step(double v, int dir) => StepPosition(NearestStep(v) + Math.Sign(dir));

    // Подписи — по доле упора j' (положение рукоятки → Shape); 19.4а-4: ход — с названием ступени
    public static string SurgeTitle(double v) => ThrustTitle(Shape(v), "вперёд", "назад", true);
    public static string SwayTitle(double v) => ThrustTitle(Shape(v), "на ПБ", "на ЛБ", false);
    public static string YawTitle(double v) => ThrustTitle(Shape(v), "нос вправо", "нос влево", false);

    /// <summary>Доля упора словами: «вперёд малый 50 %», «на ПБ 25 %», «0» (named — название ступени хода).</summary>
    public static string ThrustTitle(double f, string plus, string minus, bool named)
    {
        int p = (int)Math.Round(Math.Abs(f) * 100.0);
        if (p == 0) return "0";
        double k = Math.Abs(f) / MenuStep;
        string name = named && Math.Abs(k - Math.Round(k)) < 0.02 ? StepName((int)Math.Round(k)) + " " : "";
        return $"{(f > 0 ? plus : minus)} {name}{p} %";
    }

    /// <summary>Положение словами по упору: «ход вперёд малый 50 %, лагом на ПБ 25 %» (нулевые оси опускаются).</summary>
    public static string Describe(JoystickState j) => DescribeThrust(Shape(j.Surge), Shape(j.Sway), Shape(j.Yaw));

    /// <summary>19.4а-4: движение буксира словами (индикатор у рукоятки): «вперёд 5,8 уз · лагом 0,3 уз на ПБ · 1,2 °/с вправо».</summary>
    public static string MotionTitle(double uMs, double vMs, double rRadS)
    {
        double u = uMs / ShipParameters.KnotsToMs, v = vMs / ShipParameters.KnotsToMs, r = rRadS * 180.0 / Math.PI;
        string su = Math.Abs(u) < 0.05 ? "ход 0" : $"{(u > 0 ? "вперёд" : "назад")} {ScenarioText.Num(Math.Abs(u), 1)} уз";
        string sv = Math.Abs(v) < 0.05 ? "лагом 0" : $"лагом {ScenarioText.Num(Math.Abs(v), 1)} уз {(v > 0 ? "на ПБ" : "на ЛБ")}";
        string sr = Math.Abs(r) < 0.05 ? "поворот 0" : $"{ScenarioText.Num(Math.Abs(r), 1)} °/с {(r > 0 ? "вправо" : "влево")}";
        return $"{su} · {sv} · {sr}";
    }

    /// <summary>То же по долям упора (событие JoystickChanged с 19.4а-4 пишет упор, %).</summary>
    public static string DescribeThrust(double surge, double sway, double yaw)
    {
        if (Math.Abs(surge) < 0.005 && Math.Abs(sway) < 0.005 && Math.Abs(yaw) < 0.005) return "в нуле";
        var parts = new System.Collections.Generic.List<string>();
        if (Math.Abs(surge) >= 0.005) parts.Add("ход " + ThrustTitle(surge, "вперёд", "назад", true));
        if (Math.Abs(sway) >= 0.005) parts.Add("лагом " + ThrustTitle(sway, "на ПБ", "на ЛБ", false));
        if (Math.Abs(yaw) >= 0.005) parts.Add("разворот " + ThrustTitle(yaw, "нос вправо", "нос влево", false));
        return string.Join(", ", parts);
    }
}

/// <summary>Движитель своего буксира: джойстик → набор упора → две колонки → силы в модель (формулы — в шапке файла).</summary>
public sealed class AzimuthDrive
{
    public readonly TugSpec Spec;
    public JoystickState Joystick;
    /// <summary>Заданные и фактические обобщённые силы колонок (оси буксира), Н, Н·м.</summary>
    public double OrderX, OrderY, OrderN, ThrustX, ThrustY, ThrustN;
    /// <summary>Упоры колонок последнего шага (1 — ПБ, 2 — ЛБ), Н.</summary>
    public double Fx1, Fy1, Fx2, Fy2;

    public AzimuthDrive(TugSpec spec) { Spec = spec; }

    // ─────────── 19.4а-2: удержание курса (тумблер «УДЕРЖ. КУРС» на пульте) ───────────
    // Корпус буксира на ходу неустойчив по курсу (ThesisNotes 19.29.6): при нуле оси разворота он сам уходит
    // в циркуляцию. С удержанием курса ось разворота задаёт угловую скорость r_з = j′_r·RateMaxDegS; в нуле —
    // сначала гасится поворот (|r| < CaptureRateDegS), затем держится запомненный курс: r_з = K_ψ·(ψ_з − ψ).
    // Момент: N = M₃₃·K_r·(r_з − r), |N| ≤ |x_к|·T_гак; на ходу (β = min(1, |u|/1 м/с)) — поперечным упором колонок
    // в корме Y_N = β·N/x_к (плечо x_к в 4,4 раза больше, чем у разности упоров), на месте — разностью упоров
    // (разворот без сноса). Если упора не хватает, ход вперёд/назад уменьшается первым (приоритет курса).
    // Без удержания — как 19.4а (ось разворота — момент разностью упоров, насыщение — пропорционально).

    /// <summary>Удержание курса включено (по умолчанию — да).</summary>
    public bool HeadingHold = true;
    public const double RateMaxDegS = 10.0, CaptureRateDegS = 0.5;
    /// <summary>Коэффициенты: по угловой скорости, 1/с; по курсу, (°/с)/°. Подобраны прогоном (ThesisNotes 19.30).</summary>
    public double KRate = 4.0, KHeading = 0.3;
    /// <summary>Держимый курс, ° (NaN — не держится: идёт разворот или гасится поворот).</summary>
    public double HoldHeadingDeg { get; private set; } = double.NaN;

    public void Reset()
    {
        Joystick = JoystickState.Zero;
        OrderX = OrderY = OrderN = ThrustX = ThrustY = ThrustN = 0.0;
        Fx1 = Fy1 = Fx2 = Fy2 = 0.0;
        HoldHeadingDeg = double.NaN;
    }

    /// <summary>Шаг: силы колонок в модель (до model.Step).</summary>
    public void Apply(ShipDynamicsModel m, double dt)
    {
        TugJoystick.Demand(Joystick, Spec, out OrderX, out OrderY, out OrderN);
        if (HeadingHold) HeadingDemand(m);
        else HoldHeadingDeg = double.NaN;
        double a = 1.0 - Math.Exp(-dt / Math.Max(1e-3, Spec.SpoolTime));
        ThrustX += (OrderX - ThrustX) * a;
        ThrustY += (OrderY - ThrustY) * a;
        ThrustN += (OrderN - ThrustN) * a;
        if (HeadingHold) AzimuthAllocation.ApplyToHeadingFirst(m, Spec, ThrustX, ThrustY, ThrustN, out Fx1, out Fy1, out Fx2, out Fy2);
        else AzimuthAllocation.ApplyTo(m, Spec, ThrustX, ThrustY, ThrustN, out Fx1, out Fy1, out Fx2, out Fy2);
    }

    private void HeadingDemand(ShipDynamicsModel m)
    {
        double rDeg = m.R * 180.0 / Math.PI, psiDeg = m.Psi * 180.0 / Math.PI;
        double jr = TugJoystick.Shape(Joystick.Yaw), rD;
        if (jr != 0.0) { rD = jr * RateMaxDegS; HoldHeadingDeg = double.NaN; }
        else
        {
            if (double.IsNaN(HoldHeadingDeg) && Math.Abs(rDeg) < CaptureRateDegS) HoldHeadingDeg = psiDeg;
            rD = double.IsNaN(HoldHeadingDeg) ? 0.0
                : Math.Max(-RateMaxDegS, Math.Min(RateMaxDegS, KHeading * Math.IEEERemainder(HoldHeadingDeg - psiDeg, 360.0)));
        }
        double nMax = Math.Abs(Spec.ThrusterX) * Spec.BollardPull;
        double n = m.Coefficients3Dof.M33 * KRate * (rD - rDeg) * Math.PI / 180.0;
        n = Math.Max(-nMax, Math.Min(nMax, n));
        double beta = Math.Min(1.0, Math.Max(0.0, m.U) / 1.0);   // на заднем ходу — только разность упоров
        OrderN = n;
        OrderY += beta * n / Spec.ThrusterX;
    }

    /// <summary>Направление упора колонки (1 — ПБ, 2 — ЛБ) от ДП, ° (0 — вперёд, 90 — на ПБ).</summary>
    public double UnitDirectionDeg(int unit)
    {
        double fx = unit == 1 ? Fx1 : Fx2, fy = unit == 1 ? Fy1 : Fy2;
        double d = Math.Atan2(fy, fx) * 180.0 / Math.PI;
        return d < 0 ? d + 360.0 : d;
    }

    /// <summary>Загрузка колонки 0…1 (доля T_гак/2).</summary>
    public double UnitLoad(int unit)
    {
        double fx = unit == 1 ? Fx1 : Fx2, fy = unit == 1 ? Fy1 : Fy2;
        return Math.Sqrt(fx * fx + fy * fy) / Math.Max(1.0, 0.5 * Spec.BollardPull);
    }
}
