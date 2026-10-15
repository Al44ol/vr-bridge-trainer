// ============================================================
// FenderModel.cs — Кранец: реакция при обжатии, гистерезис, энергия (запрос 19.2)
//
// Реакция цилиндрического резинового кранца — нормированная кривая каталогов
// (вид по PIANC, 2002, WG 33; значения — допущение, ShipParameters_Sources.md):
//   R(δ) = R_н · f(δ/δ_н), f — кусочно-линейная по таблице (δ/δ_н → R/R_н):
//   0 → 0; 0,2 → 0,15; 0,4 → 0,33; 0,6 → 0,52; 0,8 → 0,74; 1,0 → 1,0; 1,1 → 1,35; 1,2 → 2,0;
//   дальше — с наклоном последнего участка (кранец «сел», реакция растёт резко).
// Поглощённая энергия при обжатии δ: E(δ) = ∫₀^δ R dδ; номинальная E_н = 0,448·R_н·δ_н.
// Гистерезис резины: при разгрузке (δ̇ < 0) реакция ниже на долю h = 0,35:
//   R = R(δ)·(1 − h·s(−δ̇/v_s)), s — гладкая ступень 0…1, v_s = 0,01 м/с —
//   часть энергии навала рассеивается, судно отходит медленнее, чем подошло.
// Подбор кранца по энергии (геометрическое подобие, E ∝ D_н³): от образца D_н = 0,5 м,
// δ_н = 0,25 м, R_н = 300 кН (E_н = 33,6 кДж) — D_н = 0,5·(E/33,6 кДж)^(1/3), δ_н = 0,5·D_н,
// R_н = E/(0,448·δ_н).
// ============================================================
using System;

public readonly struct FenderSpec
{
    /// <summary>Наружный диаметр = вылет от кромки, м.</summary>
    public readonly double OuterDiameter;
    /// <summary>Номинальное обжатие δ_н, м.</summary>
    public readonly double RatedDeflection;
    /// <summary>Номинальная реакция R_н, Н.</summary>
    public readonly double RatedReaction;
    /// <summary>Длина вдоль кромки, м.</summary>
    public readonly double Length;

    public FenderSpec(double outerDiameter, double ratedDeflection, double ratedReactionN, double length)
    {
        OuterDiameter = Math.Max(0.05, outerDiameter);
        RatedDeflection = Math.Max(0.01, Math.Min(ratedDeflection, OuterDiameter));
        RatedReaction = Math.Max(1.0, ratedReactionN);
        Length = Math.Max(0.1, length);
    }

    /// <summary>Номинальная энергия E_н = ∫₀^δн R dδ, Дж.</summary>
    public double RatedEnergy => FenderCurve.EnergyFraction(1.0) * RatedReaction * RatedDeflection;

    /// <summary>Жёсткость на номинале R_н/δ_н, Н/м (для оценок шага и демпфирования).</summary>
    public double RatedStiffness => RatedReaction / RatedDeflection;

    /// <summary>Реакция при обжатии δ (м) без гистерезиса, Н.</summary>
    public double Reaction(double deflection) => RatedReaction * FenderCurve.ReactionFraction(deflection / RatedDeflection);

    /// <summary>Энергия обжатия до δ, Дж.</summary>
    public double Energy(double deflection) => RatedReaction * RatedDeflection * FenderCurve.EnergyFraction(deflection / RatedDeflection);

    /// <summary>Образец подбора: D_н 0,5 м, δ_н 0,25 м, R_н 300 кН.</summary>
    public static readonly FenderSpec Reference = new FenderSpec(0.5, 0.25, 300e3, 1.5);

    /// <summary>Кранец на энергию E (Дж) по подобию с образцом; length — длина вдоль кромки, м.</summary>
    public static FenderSpec ForEnergy(double energy, double length)
    {
        double e0 = Reference.RatedEnergy;
        double d = Math.Max(0.3, Math.Min(2.5, 0.5 * Math.Pow(Math.Max(1.0, energy) / e0, 1.0 / 3.0)));
        double dr = 0.5 * d;
        double rr = Math.Max(energy, e0 * Math.Pow(d / 0.5, 3)) / (FenderCurve.EnergyFraction(1.0) * dr);
        return new FenderSpec(d, dr, rr, length);
    }
}

/// <summary>Нормированная кривая кранца и гистерезис.</summary>
public static class FenderCurve
{
    private static readonly double[] X = { 0.0, 0.2, 0.4, 0.6, 0.8, 1.0, 1.1, 1.2 };
    private static readonly double[] Y = { 0.0, 0.15, 0.33, 0.52, 0.74, 1.0, 1.35, 2.0 };
    /// <summary>Доля реакции, теряемая при разгрузке (гистерезис резины).</summary>
    public const double Hysteresis = 0.35;
    /// <summary>Скорость, на которой гистерезис включается полностью, м/с.</summary>
    public const double HysteresisSpeed = 0.01;

    /// <summary>R/R_н при обжатии x = δ/δ_н.</summary>
    public static double ReactionFraction(double x)
    {
        if (x <= 0.0) return 0.0;
        int n = X.Length;
        for (int i = 1; i < n; i++)
            if (x <= X[i]) return Y[i - 1] + (Y[i] - Y[i - 1]) * (x - X[i - 1]) / (X[i] - X[i - 1]);
        double slope = (Y[n - 1] - Y[n - 2]) / (X[n - 1] - X[n - 2]);
        return Y[n - 1] + slope * (x - X[n - 1]);
    }

    /// <summary>∫₀ˣ f dx — энергия в долях R_н·δ_н.</summary>
    public static double EnergyFraction(double x)
    {
        if (x <= 0.0) return 0.0;
        double e = 0.0;
        int n = X.Length;
        for (int i = 1; i < n; i++)
        {
            double x1 = Math.Min(x, X[i]);
            e += 0.5 * (Y[i - 1] + ReactionFraction(x1)) * (x1 - X[i - 1]);
            if (x <= X[i]) return e;
        }
        double yl = Y[n - 1], ye = ReactionFraction(x);
        return e + 0.5 * (yl + ye) * (x - X[n - 1]);
    }

    /// <summary>Множитель реакции с гистерезисом по скорости обжатия (> 0 — обжимается), м/с.</summary>
    public static double HysteresisFactor(double deflectionRate)
    {
        if (deflectionRate >= 0.0) return 1.0;
        double s = Math.Min(1.0, -deflectionRate / HysteresisSpeed);
        s = s * s * (3.0 - 2.0 * s);
        return 1.0 - Hysteresis * s;
    }
}
