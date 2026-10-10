// ============================================================
// SeakeepingReport.cs — Сохранение результатов стенда мореходности (запрос 4)
//
// ThesisData/Seakeeping/<параметры>_<дата>/:
//   summary.csv / summary.md       — σ, значительные амплитуды (2σ), максимумы,
//                                    доминирующие периоды, теоретические T_e;
//   <пресет>_q<угол>_v<уз>.csv     — ряды для курсовых углов 0°, 90°, 180°;
//   <пресет>_roll_pitch_vs_heading.svg — σ крена и дифферента по курсовому углу;
//   <пресет>_heave_vs_heading.svg      — σ вертикальной качки;
//   <пресет>_encounter_period.svg      — период встречи: теория и измерение;
//   <пресет>_timeseries_0kn.svg        — крен лагом и дифферент носом на стопе.
// Стиль графиков — как у TrialsReport (SvgPlot, одобрен для ВКР).
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class SeakeepingReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);
    private const double Deg = 180.0 / Math.PI;
    private static readonly double[] SeriesHeadings = { 0.0, 90.0, 180.0 };

    public static string Save(List<SeakeepingResult> results, ShipParameters p, string conditions)
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ThesisData", "Seakeeping",
            $"{(p != null ? p.name : "params")}_{DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv)}"));
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "summary.csv"), SummaryCsv(results), Utf8Bom);
        File.WriteAllText(Path.Combine(dir, "summary.md"), SummaryMarkdown(results, p, conditions), Utf8Bom);

        foreach (SeakeepingResult r in results)
            if (Array.IndexOf(SeriesHeadings, r.Case.HeadingDeg) >= 0)
                File.WriteAllText(Path.Combine(dir, r.Case.Id + ".csv"), SeriesCsv(r), Utf8Bom);

        var presets = new List<string>();
        foreach (SeakeepingResult r in results)
            if (!presets.Contains(r.Case.Preset)) presets.Add(r.Case.Preset);
        foreach (string preset in presets)
        {
            List<SeakeepingResult> rs = results.FindAll(r => r.Case.Preset == preset);
            WritePlot(dir, preset + "_roll_pitch_vs_heading.svg", RollPitchPlot(rs, preset));
            WritePlot(dir, preset + "_heave_vs_heading.svg", HeavePlot(rs, preset));
            WritePlot(dir, preset + "_encounter_period.svg", EncounterPlot(rs, preset));
            WritePlot(dir, preset + "_timeseries_0kn.svg", TimeSeriesPlot(rs, preset));
        }
        return dir;
    }

    private static void WritePlot(string dir, string file, SvgPlot plot)
    {
        if (plot != null && plot.Count > 0) File.WriteAllText(Path.Combine(dir, file), plot.Render(), new UTF8Encoding(false));
    }

    // ─────────── CSV ───────────

    public static string SummaryCsv(List<SeakeepingResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("preset;intensity;wave_speed_factor;heading_rel_deg;course_deg;speed_kn;" +
                      "sigma_heave_m;sigma_roll_deg;sigma_pitch_deg;sig_ampl_heave_m;sig_ampl_roll_deg;sig_ampl_pitch_deg;" +
                      "max_heave_m;max_roll_deg;max_pitch_deg;sigma_wave_heave_m;sigma_wave_roll_deg;sigma_wave_pitch_deg;" +
                      "peak_period_heave_s;peak_period_roll_s;peak_period_pitch_s;peak_period_wave_heave_s;" +
                      "te_wave1_s;te_wave2_s;te_wave3_s;te_wave4_s;step_time_us");
        foreach (SeakeepingResult r in results)
        {
            SeakeepingCase c = r.Case;
            sb.Append(c.Preset).Append(';').Append(F(c.Intensity, 3)).Append(';').Append(F(c.WaveSpeedFactor, 3)).Append(';')
              .Append(F(c.HeadingDeg, 0)).Append(';').Append(F(r.PsiDeg, 1)).Append(';').Append(F(c.SpeedKnots, 1)).Append(';')
              .Append(F(r.SigmaHeave, 4)).Append(';').Append(F(r.SigmaRollDeg, 3)).Append(';').Append(F(r.SigmaPitchDeg, 3)).Append(';')
              .Append(F(2 * r.SigmaHeave, 4)).Append(';').Append(F(2 * r.SigmaRollDeg, 3)).Append(';').Append(F(2 * r.SigmaPitchDeg, 3)).Append(';')
              .Append(F(r.MaxHeave, 4)).Append(';').Append(F(r.MaxRollDeg, 3)).Append(';').Append(F(r.MaxPitchDeg, 3)).Append(';')
              .Append(F(r.SigmaWaveHeave, 4)).Append(';').Append(F(r.SigmaWaveRollDeg, 3)).Append(';').Append(F(r.SigmaWavePitchDeg, 3)).Append(';')
              .Append(F(r.PeakPeriodHeave, 2)).Append(';').Append(F(r.PeakPeriodRoll, 2)).Append(';')
              .Append(F(r.PeakPeriodPitch, 2)).Append(';').Append(F(r.PeakPeriodWaveHeave, 2)).Append(';');
            for (int i = 0; i < OceanWaveField.WaveCount; i++)
                sb.Append(F(r.EncounterPeriods[i], 2)).Append(';');
            sb.Append(F(r.StepMicroseconds, 2)).AppendLine();
        }
        return sb.ToString();
    }

    public static string SeriesCsv(SeakeepingResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("t_s;heave_m;roll_deg;pitch_deg;wave_heave_m;wave_roll_deg;wave_pitch_deg");
        foreach (SeakeepingSample s in r.Samples)
            sb.Append(F(s.T, 2)).Append(';').Append(F(s.Heave, 4)).Append(';').Append(F(s.Roll * Deg, 3)).Append(';')
              .Append(F(s.Pitch * Deg, 3)).Append(';').Append(F(s.WaveHeave, 4)).Append(';')
              .Append(F(s.WaveRoll * Deg, 3)).Append(';').Append(F(s.WavePitch * Deg, 3)).AppendLine();
        return sb.ToString();
    }

    public static string SummaryMarkdown(List<SeakeepingResult> results, ShipParameters p, string conditions)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Стенд мореходности: {(p != null ? p.name : "—")}");
        sb.AppendLine();
        if (p != null)
            sb.AppendLine($"L = {G(p.length, 1)} м, B = {G(p.beam, 2)} м, d = {G(p.draft, 2)} м, GM = {G(p.metacentricHeight, 2)} м; " +
                          $"T_φ = {G(p.rollPeriod, 2)} с, T_z = {G(p.heavePeriod, 2)} с, T_θ = {G(p.pitchPeriod, 2)} с; " +
                          $"ζ_φ = {G(p.rollDamping, 2)}, ζ_z = {G(p.heaveDamping, 2)}, ζ_θ = {G(p.pitchDamping, 2)}.");
        sb.AppendLine();
        sb.AppendLine(conditions);
        sb.AppendLine();
        sb.AppendLine("q — курсовой угол волны 1 (0° — носом на волну, 90° — лагом с ПБ, 180° — попутная). " +
                      "σ — СКО; значительная амплитуда ≈ 2σ. T_пик — доминирующий период отклика; T_e1 — теоретический период встречи с волной 1. " +
                      "Значения — физика модели, без множителя комфорта и пределов отображения.");
        sb.AppendLine();
        sb.AppendLine("| Пресет | q, ° | V, уз | σ_z, м | σ_φ, ° | σ_θ, ° | max φ, ° | max θ, ° | T_пик z, с | T_пик φ, с | T_пик θ, с | T_пик возм. z, с | T_e1, с |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (SeakeepingResult r in results)
        {
            SeakeepingCase c = r.Case;
            sb.AppendLine($"| {c.Preset} | {G(c.HeadingDeg, 0)} | {G(c.SpeedKnots, 1)} | {G(r.SigmaHeave, 2)} | {G(r.SigmaRollDeg, 2)} | " +
                          $"{G(r.SigmaPitchDeg, 2)} | {G(r.MaxRollDeg, 1)} | {G(r.MaxPitchDeg, 1)} | {G(r.PeakPeriodHeave, 1)} | " +
                          $"{G(r.PeakPeriodRoll, 1)} | {G(r.PeakPeriodPitch, 1)} | {G(r.PeakPeriodWaveHeave, 1)} | {G(r.EncounterPeriods[0], 1)} |");
        }
        double us = 0.0;
        foreach (SeakeepingResult r in results) us += r.StepMicroseconds;
        sb.AppendLine();
        sb.AppendLine("Среднее время шага модели качки (сэмплер + МНК + динамика) на ПК в редакторе: " +
                      $"{G(results.Count > 0 ? us / results.Count : 0, 2)} мкс.");
        return sb.ToString();
    }

    // ─────────── Графики ───────────

    private static List<double> Speeds(List<SeakeepingResult> rs)
    {
        var v = new List<double>();
        foreach (SeakeepingResult r in rs) if (!v.Contains(r.Case.SpeedKnots)) v.Add(r.Case.SpeedKnots);
        v.Sort();
        return v;
    }

    private static List<Vector2> Line(List<SeakeepingResult> rs, double knots, Func<SeakeepingResult, double> f)
    {
        var pts = new List<Vector2>();
        foreach (SeakeepingResult r in rs)
            if (r.Case.SpeedKnots == knots && !double.IsNaN(f(r)) && !double.IsInfinity(f(r)))
                pts.Add(new Vector2((float)r.Case.HeadingDeg, (float)f(r)));
        pts.Sort((a, b) => a.x.CompareTo(b.x));
        return pts;
    }

    private static SvgPlot RollPitchPlot(List<SeakeepingResult> rs, string preset)
    {
        var plot = new SvgPlot
        {
            Title = $"СКО крена и дифферента ({PresetName(preset)})",
            XLabel = "Курсовой угол волны q, ° (0 — носом на волну, 90 — лагом)", YLabel = "СКО, °",
        };
        var dashes = new[] { SvgPlot.Dash.Solid, SvgPlot.Dash.Dashed, SvgPlot.Dash.Dotted, SvgPlot.Dash.DashDot };
        int d = 0;
        foreach (double v in Speeds(rs))
        {
            plot.Add($"крен, {Kn(v)}", dashes[d++ % 4], Line(rs, v, r => r.SigmaRollDeg));
            plot.Add($"дифферент, {Kn(v)}", dashes[d++ % 4], Line(rs, v, r => r.SigmaPitchDeg));
        }
        return plot;
    }

    private static SvgPlot HeavePlot(List<SeakeepingResult> rs, string preset)
    {
        var plot = new SvgPlot
        {
            Title = $"СКО вертикальной качки ({PresetName(preset)})",
            XLabel = "Курсовой угол волны q, °", YLabel = "СКО, м",
        };
        var dashes = new[] { SvgPlot.Dash.Solid, SvgPlot.Dash.Dashed, SvgPlot.Dash.Dotted };
        int d = 0;
        foreach (double v in Speeds(rs))
        {
            plot.Add($"судно, {Kn(v)}", dashes[d++ % 3], Line(rs, v, r => r.SigmaHeave));
        }
        List<double> speeds = Speeds(rs);
        if (speeds.Count > 0)
            plot.Add("возмущение (плоскость МНК)", SvgPlot.Dash.DashDot, Line(rs, speeds[0], r => r.SigmaWaveHeave));
        return plot;
    }

    private static SvgPlot EncounterPlot(List<SeakeepingResult> rs, string preset)
    {
        List<double> speeds = Speeds(rs);
        if (speeds.Count == 0) return null;
        double v = speeds[speeds.Count - 1];
        var plot = new SvgPlot
        {
            Title = $"Период встречи с волной, {Kn(v)} ({PresetName(preset)})",
            XLabel = "Курсовой угол волны q, °", YLabel = "Период, с",
        };
        const double cap = 40.0;   // у ω_e → 0 период уходит в бесконечность — обрезаем
        plot.Add("теория, волна 1 (λ = 90 м)", SvgPlot.Dash.Solid, Line(rs, v, r => Math.Min(cap, r.EncounterPeriods[0])));
        plot.Add("теория, волна 2 (λ = 45 м)", SvgPlot.Dash.Dashed, Line(rs, v, r => Math.Min(cap, r.EncounterPeriods[1])));
        plot.Add("модель: пик спектра возмущения z", SvgPlot.Dash.Dotted, Line(rs, v, r => Math.Min(cap, r.PeakPeriodWaveHeave)));
        return plot;
    }

    private static SvgPlot TimeSeriesPlot(List<SeakeepingResult> rs, string preset)
    {
        SeakeepingResult head = rs.Find(r => r.Case.SpeedKnots == 0 && r.Case.HeadingDeg == 0);
        SeakeepingResult beam = rs.Find(r => r.Case.SpeedKnots == 0 && r.Case.HeadingDeg == 90);
        if (head == null || beam == null) return null;
        var plot = new SvgPlot
        {
            Title = $"Качка на стопе ({PresetName(preset)})",
            XLabel = "Время t, с", YLabel = "Угол, °", Width = 760, Height = 440,
        };
        plot.Add("крен, лагом (q = 90°)", SvgPlot.Dash.Solid, Series(beam, s => s.Roll * Deg));
        plot.Add("дифферент, носом (q = 0°)", SvgPlot.Dash.Dashed, Series(head, s => s.Pitch * Deg));
        plot.Add("крен, носом (q = 0°)", SvgPlot.Dash.Dotted, Series(head, s => s.Roll * Deg));
        return plot;
    }

    private static List<Vector2> Series(SeakeepingResult r, Func<SeakeepingSample, double> f)
    {
        var pts = new List<Vector2>();
        foreach (SeakeepingSample s in r.Samples)
            if (s.T >= 100.0 && s.T <= 220.0)
                pts.Add(new Vector2((float)s.T, (float)f(s)));
        return pts;
    }

    private static string PresetName(string preset)
    {
        switch (preset)
        {
            case "calm": return "штиль";
            case "moderate": return "умеренное волнение";
            case "storm": return "шторм";
            case "night": return "ночь";
            default: return preset;
        }
    }

    private static string Kn(double v) => v == 0 ? "стоп" : $"{G(v, 1)} уз";
    private static string F(double v, int digits) => double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString("F" + digits, Inv);
    // Для Markdown: десятичная запятая, минус «−»
    private static string G(double v, int digits) =>
        double.IsNaN(v) ? "—" : double.IsInfinity(v) ? "∞" : v.ToString("F" + digits, Inv).Replace('.', ',').Replace("-", "−");
}
