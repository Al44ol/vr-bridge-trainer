// ============================================================
// MooringLine.cs — Швартовный конец и лебёдка (запрос 19.2)
//
// Конец — от клюза на судне (x, y связанных осей, высота h_кл над водой) до кнехта на
// причале (высота h_пр). Длина хорды L = √(L_г² + Δh²), L_г — по горизонтали.
// Длина без натяжения L₀ меняет лебёдка. Натяжение синтетического троса (неупругая
// кривая «нагрузка — удлинение», жёстче к разрыву):
//   ε = (L − L₀)/L₀;  T_у = MBL·(ε/ε_р)^n при ε > 0, иначе 0 (провис — сила не передаётся);
//   ε_р = 0,20 (удлинение при разрыве, полиамид/полиэстер), n = 1,5 — допущения;
//   демпфирование троса T_д = c·L̇, c = 2·ζ·√(k·m₂₂), ζ = 0,1, k = dT_у/dL — только при ε > 0;
//   T = max(0, T_у + T_д). На судно — горизонтальная составляющая T·L_г/L к кнехту.
// Пределы (OCIMF MEG4, 2018): рабочая нагрузка 0,5·MBL — «перегрузка» (гистерезис 0,45);
// тормоз лебёдки держит 0,6·MBL — выше конец «травится» сам (render) со скоростью 0,1 м/с;
// T > MBL — обрыв. Конец, закреплённый на кнехтах судна (OnBitts), не травится — может оборваться.
// Лебёдка: травить 0,3 м/с (≈ 18 м/мин); выбирать — v = v₀·(1 − T/T_тяги), v₀ = 0,3 м/с, тяга барабана
// T_тяги = 0,15·MBL (скорость падает с нагрузкой, на тяге «встаёт» — иначе лебёдка «вдёргивает» судно в кранцы);
// держать — тормоз.
// MBL по водоизмещению (допущение): MBL = 30 кН·Δ^(1/3), Δ — т (Model45m ≈ 270 кН ≈ 27 т,
// Mariner ≈ 830 кН ≈ 85 т); подать можно на дистанцию до L_max = max(40 м, 0,6·L).
// ============================================================
using System;

/// <summary>Назначение конца (порядок — от носа к корме).</summary>
public enum MooringLineKind
{
    HeadLine,        // носовой продольный
    ForwardBreast,   // носовой прижимной
    ForwardSpring,   // носовой шпринг
    AftSpring,       // кормовой шпринг
    AftBreast,       // кормовой прижимной
    SternLine        // кормовой продольный
}

public enum MooringLineState
{
    Aboard,   // на судне (не подан или отдан)
    Fast,     // подан и закреплён на кнехте причала
    Broken    // оборван
}

public enum WinchCommand { Hold, HaulIn, PayOut }

public sealed class MooringLine
{
    public const double BreakStrain = 0.20, StiffnessExponent = 1.5, DampingRatio = 0.1;
    public const double OverloadFraction = 0.5, OverloadClearFraction = 0.45, BrakeFraction = 0.6, WinchPullFraction = 0.15;
    public const double WinchSpeed = 0.3, RenderSpeed = 0.1, MinLength = 1.0;

    public readonly MooringLineKind Kind;
    /// <summary>Клюз: от миделя (> 0 — в нос), м; борт задаётся при подаче (Side: +1 — ПБ, −1 — ЛБ).</summary>
    public readonly double FairleadX;
    /// <summary>Желаемое место кнехта вдоль борта относительно клюза, м (> 0 — в нос).</summary>
    public readonly double LeadAlong;
    public double Mbl;           // Н
    public double MaxLength;     // м

    public MooringLineState State { get; private set; } = MooringLineState.Aboard;
    public WinchCommand Winch = WinchCommand.Hold;
    /// <summary>Закреплён на кнехтах судна (не на тормозе лебёдки): не травится.</summary>
    public bool OnBitts;
    public int Side { get; private set; } = 1;
    public int Bollard { get; private set; } = -1;
    /// <summary>Длина без натяжения L₀, м.</summary>
    public double RestLength { get; private set; }
    public double Length { get; private set; }
    public double Tension { get; private set; }
    public double MaxTension { get; private set; }
    public bool Overloaded { get; private set; }

    private double prevLength = double.NaN;

    public MooringLine(MooringLineKind kind, double fairleadX, double leadAlong, double mbl, double maxLength)
    {
        Kind = kind; FairleadX = fairleadX; LeadAlong = leadAlong; Mbl = mbl; MaxLength = maxLength;
    }

    /// <summary>Скорость выбирания при натяжении T, м/с: v₀·(1 − T/T_тяги) — характеристика лебёдки (на холостом ходу v₀, на тяге барабана — 0).</summary>
    public static double HaulSpeed(double tension, double mbl) => WinchSpeed * Math.Max(0.0, 1.0 - tension / (WinchPullFraction * mbl));

    public static string Title(MooringLineKind k)
    {
        switch (k)
        {
            case MooringLineKind.HeadLine: return "носовой продольный";
            case MooringLineKind.ForwardBreast: return "носовой прижимной";
            case MooringLineKind.ForwardSpring: return "носовой шпринг";
            case MooringLineKind.AftSpring: return "кормовой шпринг";
            case MooringLineKind.AftBreast: return "кормовой прижимной";
            default: return "кормовой продольный";
        }
    }

    public string Title() => Title(Kind);

    /// <summary>MBL по водоизмещению, Н: 30 кН·Δ^(1/3) (Δ — т).</summary>
    public static double MblFor(ShipParameters p) => 30e3 * Math.Pow(BerthArea.ShipMass(p) / 1000.0, 1.0 / 3.0);

    /// <summary>Наибольшая дистанция подачи, м.</summary>
    public static double MaxLengthFor(ShipParameters p) => Math.Max(40.0, 0.6 * p.length);

    /// <summary>Подать на кнехт: длина — по хорде + провис slack (м); лебёдка — «держать».</summary>
    public void MakeFast(int bollard, int side, double chord, double slack = 0.5)
    {
        State = MooringLineState.Fast;
        Bollard = bollard;
        Side = side >= 0 ? 1 : -1;
        RestLength = Math.Max(MinLength, chord + slack);
        Length = chord;
        Winch = WinchCommand.Hold;
        OnBitts = false;
        Tension = 0.0;
        MaxTension = 0.0;
        Overloaded = false;
        prevLength = double.NaN;
    }

    /// <summary>Отдать (вернуть на борт).</summary>
    public void Release()
    {
        State = MooringLineState.Aboard;
        Bollard = -1;
        Tension = 0.0;
        Overloaded = false;
        Winch = WinchCommand.Hold;
        OnBitts = false;
        prevLength = double.NaN;
    }

    /// <summary>Вернуть оборванный конец в исходное (новый конец) — для перезапуска.</summary>
    public void Reset() => Release();

    /// <summary>Натяжение при длине хорды L и скорости её изменения; k — жёсткость dT/dL (Н/м).</summary>
    public static double ElasticTension(double mbl, double restLength, double length, out double stiffness)
    {
        double eps = (length - restLength) / Math.Max(1e-6, restLength);
        if (eps <= 0.0) { stiffness = 0.0; return 0.0; }
        double x = eps / BreakStrain;
        stiffness = mbl * StiffnessExponent * Math.Pow(x, StiffnessExponent - 1.0) / (BreakStrain * restLength);
        return mbl * Math.Pow(x, StiffnessExponent);
    }

    /// <summary>
    /// Шаг конца: длина хорды → натяжение, лебёдка, перегрузка, обрыв. sway mass — m₂₂ судна (демпфирование).
    /// Возвращает событие шага (или None).
    /// </summary>
    public MooringLineEvent Update(double chord, double dt, double swayMass)
    {
        if (State != MooringLineState.Fast) { Tension = 0.0; return MooringLineEvent.None; }
        Length = chord;
        double rate = double.IsNaN(prevLength) || dt <= 0 ? 0.0 : (chord - prevLength) / dt;
        prevLength = chord;

        double te = ElasticTension(Mbl, RestLength, chord, out double k);
        double t = te;
        if (te > 0.0) t += 2.0 * DampingRatio * Math.Sqrt(k * Math.Max(1.0, swayMass)) * rate;
        Tension = Math.Max(0.0, t);

        // Лебёдка (на кнехтах судна — нет)
        if (!OnBitts)
        {
            if (Winch == WinchCommand.HaulIn)
                RestLength = Math.Max(MinLength, RestLength - HaulSpeed(Tension, Mbl) * dt);
            else if (Winch == WinchCommand.PayOut)
                RestLength = Math.Min(MaxLength, RestLength + WinchSpeed * dt);
            if (Tension > BrakeFraction * Mbl && Winch != WinchCommand.PayOut)
                RestLength = Math.Min(MaxLength, RestLength + RenderSpeed * dt);   // тормоз травит
        }

        if (Tension > MaxTension) MaxTension = Tension;
        if (Tension > Mbl)
        {
            State = MooringLineState.Broken;
            Tension = 0.0;
            Overloaded = false;
            return MooringLineEvent.Broken;
        }
        if (!Overloaded && Tension > OverloadFraction * Mbl) { Overloaded = true; return MooringLineEvent.Overload; }
        if (Overloaded && Tension < OverloadClearFraction * Mbl) { Overloaded = false; return MooringLineEvent.OverloadCleared; }
        return MooringLineEvent.None;
    }
}

public enum MooringLineEvent { None, Overload, OverloadCleared, Broken }
