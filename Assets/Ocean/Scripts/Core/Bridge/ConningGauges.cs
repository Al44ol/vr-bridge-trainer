// ============================================================
// ConningGauges.cs — Геометрия и значения приборов мониторов пульта (запрос 16а)
//
// Чистая модель (без сцены) для приборов на экранах МОСТИКИ:
//   • GaugeScale — шкала стрелочного прибора: значение → угол стрелки (0° — вверх, по часовой),
//     деления, точка на окружности; сектора (зелёный ПБ / вперёд, красный ЛБ / назад);
//   • CompassCard — картушка: курс сверху (под курсовой чертой), картушка вращается против курса;
//   • EngineRpm — обороты ГД по телеграфу: n_зад = k_тел · n_ном (0 при отказе ГД), разгон / сброс —
//     инерционное звено первого порядка dn/dt = (n_зад − n)/T_гд (отображение, модель движения не меняется);
//   • ConningText — подписи: курс «034,9°», руль «12° ПБ», телеграф по-русски, обороты «+540».
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>Шкала стрелочного прибора: значения min…max на дуге от startDeg до endDeg (0° — вверх, по часовой).</summary>
public readonly struct GaugeScale
{
    public readonly double Min, Max, StartDeg, EndDeg;

    public GaugeScale(double min, double max, double startDeg, double endDeg)
    {
        Min = min; Max = max; StartDeg = startDeg; EndDeg = endDeg;
    }

    /// <summary>Угол стрелки для значения (за пределами шкалы — упор в край).</summary>
    public double AngleOf(double value)
    {
        if (Max <= Min) return StartDeg;
        double k = (value - Min) / (Max - Min);
        if (double.IsNaN(k)) k = 0.0;
        k = Math.Max(0.0, Math.Min(1.0, k));
        return StartDeg + k * (EndDeg - StartDeg);
    }

    /// <summary>Значения делений с шагом step от min (включая max, если попадает).</summary>
    public List<double> Ticks(double step)
    {
        var list = new List<double>();
        if (step <= 0.0 || Max <= Min) return list;
        double first = Math.Ceiling(Min / step - 1e-9) * step;
        for (double v = first; v <= Max + step * 1e-6; v += step) list.Add(Math.Round(v / step) * step);
        return list;
    }

    /// <summary>Точка на окружности радиуса r для угла (0° — вверх, по часовой): x вправо, y вверх.</summary>
    public static (double x, double y) Polar(double angleDeg, double r)
    {
        double a = angleDeg * Math.PI / 180.0;
        return (r * Math.Sin(a), r * Math.Cos(a));
    }
}

public static class CompassCard
{
    /// <summary>Угол поворота картушки (по часовой) при курсе heading: курс — под курсовой чертой сверху.</summary>
    public static double CardRotation(double headingDeg) => -Repeat(headingDeg);

    /// <summary>Где на экране (угол от верха, по часовой) окажется деление картушки bearing при курсе heading.</summary>
    public static double ScreenAngle(double bearingDeg, double headingDeg) => Repeat(bearingDeg - headingDeg);

    public static double Repeat(double deg)
    {
        double d = deg % 360.0;
        return d < 0.0 ? d + 360.0 : d;
    }
}

/// <summary>Обороты ГД для прибора: по телеграфу, с инерцией разгона и сброса.</summary>
public sealed class EngineRpm
{
    /// <summary>Номинальные обороты (телеграф «Полный»), об/мин.</summary>
    public double RatedRpm = 750.0;
    /// <summary>Постоянная времени изменения оборотов, с.</summary>
    public double TimeConstantS = 4.0;
    public double Rpm { get; private set; }

    /// <summary>Заданные обороты: k_тел · n_ном (k — speedFactor телеграфа, −1…1); при отказе ГД — 0.</summary>
    public double Target(double telegraphFactor, bool engineFailed) =>
        engineFailed ? 0.0 : Math.Max(-1.0, Math.Min(1.0, telegraphFactor)) * RatedRpm;

    /// <summary>Шаг: n += (n_зад − n)·(1 − e^(−dt/T)).</summary>
    public double Step(double telegraphFactor, bool engineFailed, double dt)
    {
        double target = Target(telegraphFactor, engineFailed);
        if (dt <= 0.0) return Rpm;
        double k = TimeConstantS > 1e-6 ? 1.0 - Math.Exp(-dt / TimeConstantS) : 1.0;
        Rpm += (target - Rpm) * k;
        if (Math.Abs(target - Rpm) < 0.05) Rpm = target;
        return Rpm;
    }

    public void Reset(double rpm = 0.0) => Rpm = rpm;
}

public static class ConningText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;   // запятая — заменой (культура ru-RU есть не на всех платформах)

    /// <summary>Курс для табло: «034,9».</summary>
    public static string Heading(double deg)
    {
        double d = CompassCard.Repeat(deg);
        if (Math.Round(d, 1) >= 360.0) d = 0.0;   // 359,97° → «000,0», не «360,0»
        return d.ToString("000.0", Inv).Replace('.', ',');
    }

    /// <summary>Руль: «0°», «12° ПБ», «5° ЛБ» (δ &gt; 0 — правый борт).</summary>
    public static string Rudder(double deg)
    {
        if (Math.Abs(deg) < 0.5) return "0°";
        return $"{Math.Abs(deg).ToString("0", Inv)}° {(deg > 0.0 ? "ПБ" : "ЛБ")}";
    }

    /// <summary>Обороты со знаком: «+540», «−300», «0».</summary>
    public static string Rpm(double rpm)
    {
        long r = (long)Math.Round(rpm);
        return r == 0 ? "0" : r > 0 ? "+" + r.ToString(Inv) : "−" + (-r).ToString(Inv);
    }

    /// <summary>Положение телеграфа по-русски («Half Ahead» → «СРЕДНИЙ ВПЕРЁД»).</summary>
    public static string Telegraph(string name)
    {
        string n = (name ?? "").ToLowerInvariant();
        string dir = n.Contains("astern") ? " НАЗАД" : n.Contains("ahead") ? " ВПЕРЁД" : "";
        if (n.Contains("stop")) return "СТОП";
        if (n.Contains("full")) return "ПОЛНЫЙ" + dir;
        if (n.Contains("half")) return "СРЕДНИЙ" + dir;
        if (n.Contains("dead slow")) return "САМЫЙ МАЛЫЙ" + dir;
        if (n.Contains("slow")) return "МАЛЫЙ" + dir;
        return string.IsNullOrEmpty(name) ? "—" : name;
    }
}
