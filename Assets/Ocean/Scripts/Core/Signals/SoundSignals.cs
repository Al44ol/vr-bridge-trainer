// ============================================================
// SoundSignals.cs — Звуки и звуковые сигналы судна (C4)
//
// Правило 32 МППСС-72: «короткий звук» — около 1 с, «продолжительный» — от 4 до 6 с.
// Тип звука по длительности τ (Δ — допуск оценки, предварительно 0,5 с, ThesisNotes, раздел 12):
//     короткий         ShortMinS ≤ τ ≤ 1,5 + Δ          (норма — «около 1 с», без границ);
//     продолжительный  4 − Δ ≤ τ ≤ 6 + Δ                (норма — 4…6 с);
//     иначе            неопределённый.
// Сигнал — группа звуков с паузами не длиннее GroupGapS (3 с; в сигнале 35 б пауза
// «около 2 с», в сигналах правила 34 — около 1 с). Сигнал закончен, когда после
// последнего звука прошло GroupGapS без нового звука.
// Распознаваемые сигналы (Д — продолжительный, К — короткий):
//     Д      — 35 а: судно с механическим двигателем на ходу, имеющее ход (в тумане);
//     ДД     — 35 б: на ходу, без хода;
//     ДКК    — 35 в: ограниченное в возможности маневрировать и др.;
//     К, КК, ККК — 34 а: изменяю курс вправо / влево, работаю движителями назад (суда видят друг друга);
//     ≥ 5 К  — 34 г: сомнение;
//     ДДК, ДДКК, ДКДК — 34 в: обгон в узкости (намерение справа / слева, согласие);
//     КККК   — 35 и: опознавательный сигнал лоцманского судна.
// Длительности каждого звука пишутся в events.csv (HornEnded), поэтому оценку можно
// пересчитать с другим допуском (например, строго 4…6 с).
// ============================================================
using System;
using System.Collections.Generic;
using System.Text;

public enum BlastKind { Unknown, Short, Prolonged }

public enum SoundSignalKind
{
    None,
    Unrecognized,
    FogMakingWay,        // 35 а
    FogNotMakingWay,     // 35 б
    FogRestricted,       // 35 в
    TurnStarboard,       // 34 а
    TurnPort,            // 34 а
    Astern,              // 34 а
    Doubt,               // 34 г
    OvertakeStarboard,   // 34 в i
    OvertakePort,        // 34 в i
    OvertakeAgree,       // 34 в ii
    PilotIdentity        // 35 и
}

public struct SoundBlast
{
    public double Start, Duration;
    public BlastKind Kind;
    public double End => Start + Duration;
}

public sealed class SoundSignal
{
    public readonly List<SoundBlast> Blasts = new List<SoundBlast>();
    public SoundSignalKind Kind;
    public double Start => Blasts.Count > 0 ? Blasts[0].Start : double.NaN;
    public double End => Blasts.Count > 0 ? Blasts[Blasts.Count - 1].End : double.NaN;
    /// <summary>Запись звуками: «—» продолжительный, «·» короткий, «?» неопределённый.</summary>
    public string Pattern => SoundSignals.Pattern(Blasts);
}

public static class SoundSignals
{
    public const double ShortMinS = 0.3, ShortMaxS = 1.5;
    public const double ProlongedMinS = 4.0, ProlongedMaxS = 6.0;
    /// <summary>Пауза, после которой сигнал считается законченным, с.</summary>
    public const double DefaultGroupGapS = 3.0;
    /// <summary>Допуск по умолчанию (предварительный), с.</summary>
    public const double DefaultToleranceS = 0.5;

    public static BlastKind Classify(double durationS, double toleranceS)
    {
        double tol = Math.Max(0.0, toleranceS);
        if (durationS >= ShortMinS && durationS <= ShortMaxS + tol) return BlastKind.Short;
        if (durationS >= ProlongedMinS - tol && durationS <= ProlongedMaxS + tol) return BlastKind.Prolonged;
        return BlastKind.Unknown;
    }

    public static string BlastTitle(BlastKind k) =>
        k == BlastKind.Short ? "короткий" : k == BlastKind.Prolonged ? "продолжительный" : "неопределённой длительности";

    public static string Pattern(IReadOnlyList<SoundBlast> blasts)
    {
        var b = new StringBuilder();
        foreach (SoundBlast x in blasts)
        {
            if (b.Length > 0) b.Append(' ');
            b.Append(x.Kind == BlastKind.Prolonged ? "—" : x.Kind == BlastKind.Short ? "·" : "?");
        }
        return b.ToString();
    }

    public static SoundSignalKind Recognize(IReadOnlyList<SoundBlast> blasts)
    {
        if (blasts == null || blasts.Count == 0) return SoundSignalKind.None;
        var p = new StringBuilder();
        foreach (SoundBlast x in blasts)
        {
            if (x.Kind == BlastKind.Unknown) return SoundSignalKind.Unrecognized;
            p.Append(x.Kind == BlastKind.Prolonged ? 'P' : 'S');
        }
        string s = p.ToString();
        switch (s)
        {
            case "P": return SoundSignalKind.FogMakingWay;
            case "PP": return SoundSignalKind.FogNotMakingWay;
            case "PSS": return SoundSignalKind.FogRestricted;
            case "S": return SoundSignalKind.TurnStarboard;
            case "SS": return SoundSignalKind.TurnPort;
            case "SSS": return SoundSignalKind.Astern;
            case "SSSS": return SoundSignalKind.PilotIdentity;
            case "PPS": return SoundSignalKind.OvertakeStarboard;
            case "PPSS": return SoundSignalKind.OvertakePort;
            case "PSPS": return SoundSignalKind.OvertakeAgree;
        }
        if (s.Length >= 5 && s.Replace("S", "").Length == 0) return SoundSignalKind.Doubt;
        return SoundSignalKind.Unrecognized;
    }

    /// <summary>Туманный сигнал судна с механическим двигателем на ходу (правило 35 а, б).</summary>
    public static bool IsUnderwayFogSignal(SoundSignalKind k) => k == SoundSignalKind.FogMakingWay || k == SoundSignalKind.FogNotMakingWay;

    public static string Title(SoundSignalKind k)
    {
        switch (k)
        {
            case SoundSignalKind.FogMakingWay: return "один продолжительный — на ходу, имею ход (правило 35 а)";
            case SoundSignalKind.FogNotMakingWay: return "два продолжительных — на ходу, хода не имею (правило 35 б)";
            case SoundSignalKind.FogRestricted: return "продолжительный и два коротких — ограничен в возможности маневрировать (правило 35 в)";
            case SoundSignalKind.TurnStarboard: return "один короткий — изменяю курс вправо (правило 34 а)";
            case SoundSignalKind.TurnPort: return "два коротких — изменяю курс влево (правило 34 а)";
            case SoundSignalKind.Astern: return "три коротких — работаю движителями назад (правило 34 а)";
            case SoundSignalKind.Doubt: return "не менее пяти коротких — сомнение (правило 34 г)";
            case SoundSignalKind.OvertakeStarboard: return "два продолжительных и короткий — намерен обогнать по правому борту (правило 34 в)";
            case SoundSignalKind.OvertakePort: return "два продолжительных и два коротких — намерен обогнать по левому борту (правило 34 в)";
            case SoundSignalKind.OvertakeAgree: return "продолжительный, короткий, продолжительный, короткий — согласие на обгон (правило 34 в)";
            case SoundSignalKind.PilotIdentity: return "четыре коротких — опознавательный сигнал лоцманского судна (правило 35 и)";
            case SoundSignalKind.Unrecognized: return "сигнал не распознан";
            default: return "";
        }
    }

    /// <summary>Сгруппировать звуки (время начала, длительность) в сигналы.</summary>
    public static List<SoundSignal> Group(IReadOnlyList<SoundBlast> blasts, double gapS = DefaultGroupGapS)
    {
        var list = new List<SoundSignal>();
        SoundSignal cur = null;
        foreach (SoundBlast b in blasts)
        {
            if (cur == null || b.Start - cur.End > gapS)
            {
                if (cur != null) { cur.Kind = Recognize(cur.Blasts); list.Add(cur); }
                cur = new SoundSignal();
            }
            cur.Blasts.Add(b);
        }
        if (cur != null) { cur.Kind = Recognize(cur.Blasts); list.Add(cur); }
        return list;
    }
}

/// <summary>
/// Разбор сигналов по ходу упражнения (ControlActionDetector, прогон ExerciseSimulation):
/// звуки добавляются по окончании, сигнал выдаётся, когда пауза после последнего звука ≥ GroupGapS.
/// </summary>
public sealed class SoundSignalParser
{
    public double GroupGapS = SoundSignals.DefaultGroupGapS;
    public double ToleranceS = SoundSignals.DefaultToleranceS;
    private SoundSignal current;

    public void Reset() => current = null;

    /// <summary>Звук закончился. Возвращает его тип.</summary>
    public BlastKind AddBlast(double start, double duration)
    {
        var b = new SoundBlast { Start = start, Duration = duration, Kind = SoundSignals.Classify(duration, ToleranceS) };
        if (current == null) current = new SoundSignal();
        current.Blasts.Add(b);
        return b.Kind;
    }

    /// <summary>Сигнал закончен (после последнего звука прошло GroupGapS; sounding — звук ещё идёт).</summary>
    public bool TryComplete(double time, bool sounding, out SoundSignal signal)
    {
        signal = null;
        if (current == null || sounding || time - current.End < GroupGapS - 1e-9) return false;
        current.Kind = SoundSignals.Recognize(current.Blasts);
        signal = current;
        current = null;
        return true;
    }
}
