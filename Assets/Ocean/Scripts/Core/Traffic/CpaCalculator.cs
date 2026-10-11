// ============================================================
// CpaCalculator.cs — Кратчайшее сближение (CPA/TCPA) и «точка встречи» (B3)
//
// Чистый класс: модуль целей (B3), инспектор сценария, оценка (C2), тесты.
//
// Прямая задача (обе скорости постоянны):
//     R₀ = P_ц − P_св               — относительное положение цели, м;
//     V_r = V_ц − V_св              — относительная скорость, м/с;
//     TCPA = −(R₀·V_r)/|V_r|²       (при |V_r| → 0 — 0: дистанция не меняется);
//     CPA  = |R₀ + V_r·TCPA|.
// TCPA < 0 — точка кратчайшего сближения пройдена (суда расходятся).
//
// Обратная задача («выйти в точку встречи»): даны своё судно (позиция, курс,
// скорость), курс и скорость цели, CPA, TCPA и сторона. В момент TCPA
// относительное положение перпендикулярно V_r и равно по модулю CPA:
//     R(T) = CPA·n,  n ⟂ V_r,  |n| = 1,
// n выбирается из двух кандидатов (слева и справа от V_r) по стороне:
// ближайший к правому борту / левому борту / носу / корме своего судна.
// Начальное относительное положение: R₀ = CPA·n − V_r·T;  P_ц = P_св + R₀.
// Проверка — прямой задачей (TCPA = T, CPA = заданной) и прогоном двух
// моделей движения (тесты TrafficTests).
// Курс 0° = N, по часовой; скорость — по курсу (без дрейфа и течения).
// ============================================================
using System;

/// <summary>Результат расчёта сближения.</summary>
public struct CpaResult
{
    /// <summary>Дистанция кратчайшего сближения, м.</summary>
    public double Cpa;
    /// <summary>Время до кратчайшего сближения, с (< 0 — уже пройдено).</summary>
    public double Tcpa;
    /// <summary>Текущая дистанция, м.</summary>
    public double Range;
    /// <summary>Истинный пеленг со своего судна на цель, °.</summary>
    public double BearingDeg;
    /// <summary>Относительная скорость, м/с.</summary>
    public double RelativeSpeed;
}

public static class CpaCalculator
{
    public const double MetersPerNm = 1852.0;
    private const double Deg = 180.0 / Math.PI;

    /// <summary>Вектор скорости (восток, север), м/с, по курсу (°) и скорости (уз).</summary>
    public static void Velocity(double courseDeg, double speedKnots, out double ve, out double vn)
    {
        double v = speedKnots * ShipParameters.KnotsToMs;
        double c = courseDeg / Deg;
        ve = v * Math.Sin(c);
        vn = v * Math.Cos(c);
    }

    /// <summary>CPA/TCPA по позициям (м) и скоростям (м/с) своего судна и цели.</summary>
    public static CpaResult Compute(WorldPosition own, double ownVe, double ownVn,
                                    WorldPosition target, double tgtVe, double tgtVn)
    {
        double re = target.East - own.East, rn = target.North - own.North;
        double ve = tgtVe - ownVe, vn = tgtVn - ownVn;
        double v2 = ve * ve + vn * vn;
        double tcpa = v2 > 1e-12 ? -(re * ve + rn * vn) / v2 : 0.0;
        double ce = re + ve * tcpa, cn = rn + vn * tcpa;
        return new CpaResult
        {
            Cpa = Math.Sqrt(ce * ce + cn * cn),
            Tcpa = tcpa,
            Range = Math.Sqrt(re * re + rn * rn),
            BearingDeg = Bearing(re, rn),
            RelativeSpeed = Math.Sqrt(v2)
        };
    }

    /// <summary>CPA/TCPA по курсам (°) и скоростям (уз).</summary>
    public static CpaResult FromCourses(WorldPosition own, double ownCourseDeg, double ownSpeedKn,
                                        WorldPosition target, double tgtCourseDeg, double tgtSpeedKn)
    {
        Velocity(ownCourseDeg, ownSpeedKn, out double oe, out double on);
        Velocity(tgtCourseDeg, tgtSpeedKn, out double te, out double tn);
        return Compute(own, oe, on, target, te, tn);
    }

    /// <summary>
    /// Начальная позиция цели для заданных CPA (м) и TCPA (с). ambiguous — сторона
    /// не определяется однозначно (например, «по носу» на встречных курсах: оба
    /// кандидата n перпендикулярны оси своего судна) — взят левый кандидат.
    /// Возвращает false, если относительной скорости нет (сближения не будет).
    /// </summary>
    public static bool MeetingStart(WorldPosition ownStart, double ownCourseDeg, double ownSpeedKn,
                                    double tgtCourseDeg, double tgtSpeedKn,
                                    double cpaM, double tcpaS, MeetingSide side,
                                    out WorldPosition targetStart, out bool ambiguous)
    {
        Velocity(ownCourseDeg, ownSpeedKn, out double oe, out double on);
        Velocity(tgtCourseDeg, tgtSpeedKn, out double te, out double tn);
        double ve = te - oe, vn = tn - on;
        double v = Math.Sqrt(ve * ve + vn * vn);
        ambiguous = false;
        if (v < 1e-6)
        {
            targetStart = ownStart;
            ambiguous = true;
            return false;
        }

        // Кандидаты n: слева от V_r (−vn, ve)/v и справа (vn, −ve)/v
        double le = -vn / v, ln = ve / v;
        double psi = ownCourseDeg / Deg;
        double fe = Math.Sin(psi), fn = Math.Cos(psi);    // к носу своего судна
        double se = Math.Cos(psi), sn = -Math.Sin(psi);   // к правому борту
        double ae, an;
        switch (side)
        {
            case MeetingSide.Port: ae = -se; an = -sn; break;
            case MeetingSide.Ahead: ae = fe; an = fn; break;
            case MeetingSide.Astern: ae = -fe; an = -fn; break;
            default: ae = se; an = sn; break;
        }
        double dot = le * ae + ln * an;
        if (Math.Abs(dot) < 0.05 && cpaM > 0.0) ambiguous = true;
        double ne = dot >= 0.0 ? le : -le, nn = dot >= 0.0 ? ln : -ln;

        double r0e = cpaM * ne - ve * tcpaS, r0n = cpaM * nn - vn * tcpaS;
        targetStart = new WorldPosition(ownStart.East + r0e, ownStart.North + r0n);
        return true;
    }

    /// <summary>Истинный пеленг вектора (восток, север), 0…360°.</summary>
    public static double Bearing(double de, double dn)
    {
        double b = Math.Atan2(de, dn) * Deg;
        return b < 0 ? b + 360.0 : b;
    }

    /// <summary>Курсовой угол (0…360°, по часовой от носа) направления bearingDeg при курсе headingDeg.</summary>
    public static double RelativeBearing(double bearingDeg, double headingDeg)
    {
        double q = (bearingDeg - headingDeg) % 360.0;
        return q < 0 ? q + 360.0 : q;
    }
}
