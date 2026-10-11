// ============================================================
// TrialsReport.cs — Сохранение результатов манёвренных испытаний
//
// ThesisData/Trials/<параметры>_<дата>/:
//   <id>.csv          — траектория испытания (формат CSV по CLAUDE.md);
//   summary.csv       — сводная таблица (показатель, значение, критерий IMO);
//   summary.md        — то же в Markdown для вставки в ВКР;
//   *.svg             — графики для ВКР: чёрно-белые, линии разного типа,
//                       подписи на русском, шрифт Times New Roman.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class TrialsReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);

    public static string Save(List<TrialResult> results, TrialSettings s)
    {
        string name = s.Parameters != null ? s.Parameters.name : "params";
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ThesisData", "Trials",
            $"{name}_{DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv)}"));
        Directory.CreateDirectory(dir);

        foreach (TrialResult r in results)
            File.WriteAllText(Path.Combine(dir, r.Id + ".csv"), TrajectoryCsv(r), Utf8Bom);
        File.WriteAllText(Path.Combine(dir, "summary.csv"), SummaryCsv(results, s), Utf8Bom);
        File.WriteAllText(Path.Combine(dir, "summary.md"), SummaryMarkdown(results, s), Utf8Bom);

        TrialResult Find(string id) => results.Find(r => r.Id == id);
        WritePlot(dir, "turning_35.svg", TurningPlot(Find("turning_35_stbd"), Find("turning_35_port"), s));
        WritePlot(dir, "zigzag_10_10.svg", ZigzagPlot(Find("zigzag_10_10")));
        WritePlot(dir, "zigzag_20_20.svg", ZigzagPlot(Find("zigzag_20_20")));
        WritePlot(dir, "speed.svg", SpeedPlot(Find("crash_stop"), Find("passive_stop"), Find("acceleration")));
        return dir;
    }

    private static void WritePlot(string dir, string file, SvgPlot plot)
    {
        if (plot != null) File.WriteAllText(Path.Combine(dir, file), plot.Render(), new UTF8Encoding(false));
    }

    // ─────────── CSV ───────────

    public static string TrajectoryCsv(TrialResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("t_s;x_m;y_m;heading_deg;heading_change_deg;u_ms;speed_kn;r_degs;rudder_ordered_deg;rudder_actual_deg");
        foreach (TrialSample p in r.Samples)
        {
            sb.Append(F(p.T, 2)).Append(';').Append(F(p.X, 3)).Append(';').Append(F(p.Y, 3)).Append(';')
              .Append(F(p.HeadingDeg, 3)).Append(';').Append(F(p.DeltaPsiDeg, 3)).Append(';')
              .Append(F(p.U, 4)).Append(';').Append(F(p.U / ManoeuvringTrials.KnotsToMs, 3)).Append(';')
              .Append(F(p.RDegS, 5)).Append(';').Append(F(p.RudderOrderedDeg, 3)).Append(';')
              .Append(F(p.RudderActualDeg, 3)).AppendLine();
        }
        return sb.ToString();
    }

    public static string SummaryCsv(List<TrialResult> results, TrialSettings s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("trial;metric;value;unit;value_L;criterion_imo;complies;note");
        foreach (TrialResult r in results)
        foreach (TrialMetric m in r.Metrics)
        {
            sb.Append(Esc(r.Title)).Append(';').Append(Esc(m.Name)).Append(';')
              .Append(F(m.Value, 2)).Append(';').Append(m.Unit).Append(';')
              .Append(F(m.ValueInL, 3)).Append(';').Append(Esc(m.Criterion)).Append(';')
              .Append(m.Pass == null ? "" : m.Pass.Value ? "да" : "нет").Append(';')
              .Append(Esc(m.Note)).AppendLine();
        }
        return sb.ToString();
    }

    public static string SummaryMarkdown(List<TrialResult> results, TrialSettings s)
    {
        ShipParameters p = s.Parameters;
        var sb = new StringBuilder();
        sb.AppendLine($"# Манёвренные испытания: {p.name}");
        sb.AppendLine();
        string yaw = p.yawModel == YawModel.Norrbin ? $"Норрбин (α' = {F(p.norrbinAlphaPrime, 2)})" : "Номото 1-го порядка";
        string loss = p.turnSpeedLossCoeff != 0f ? $", потеря скорости на циркуляции C_r' = {F(p.turnSpeedLossCoeff, 4)}" : "";
        sb.AppendLine($"Дата: {DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv)}. Модель: {yaw} + скорость 1-го порядка{loss} " +
                      $"(ShipDynamicsModel), шаг dt = {F(s.Dt, 3)} с.");
        sb.AppendLine();
        sb.AppendLine($"Параметры: L = {F(p.length, 1)} м; V_эксп = {F(p.serviceSpeedKnots, 1)} уз; " +
                      $"скорость испытаний V = {F(s.TestSpeedMs / ManoeuvringTrials.KnotsToMs, 2)} уз; " +
                      $"K' = {F(p.nomotoKPrime, 2)}; T' = {F(p.nomotoTPrime, 2)}; τ_разг = {F(p.accelerationTimeConstant, 0)} с; " +
                      $"τ_торм = {F(p.decelerationTimeConstant, 0)} с; k_зх = {F(p.asternFactor, 2)}; δ_max = {F(p.maxRudderAngle, 0)}°; " +
                      $"ω_δ = {F(p.rudderRate, 2)} °/с.");
        sb.AppendLine();
        sb.AppendLine("| Испытание | Показатель | Значение | В длинах L | Критерий IMO MSC.137(76) | Соответствует |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (TrialResult r in results)
        {
            foreach (TrialMetric m in r.Metrics)
            {
                string value = double.IsNaN(m.Value) ? "—" : $"{F(m.Value, m.Unit == "°" ? 2 : 1)} {m.Unit}";
                string inL = double.IsNaN(m.ValueInL) ? "" : F(m.ValueInL, 2) + " L";
                string pass = m.Pass == null ? "" : m.Pass.Value ? "да" : "**нет**";
                sb.AppendLine($"| {r.Title} | {m.Name}{(m.Note.Length > 0 ? $" <br><small>{m.Note}</small>" : "")} | " +
                              $"{value} | {inL} | {m.Criterion} | {pass} |");
            }
            if (!r.Completed) sb.AppendLine($"| {r.Title} | *не завершено: {r.Message}* | | | | |");
        }
        return sb.ToString();
    }

    // ─────────── Графики ───────────

    private static SvgPlot TurningPlot(TrialResult stbd, TrialResult port, TrialSettings s)
    {
        if (stbd == null && port == null) return null;
        var plot = new SvgPlot
        {
            Title = $"Циркуляция, руль 35°, V = {F(s.TestSpeedMs / ManoeuvringTrials.KnotsToMs, 1)} уз",
            XLabel = "Смещение от начального курса, м",
            YLabel = "Продвижение вдоль начального курса, м",
            EqualAspect = true,
        };
        if (stbd != null) plot.Add("на правый борт", SvgPlot.Dash.Solid, Xy(stbd));
        if (port != null) plot.Add("на левый борт", SvgPlot.Dash.Dashed, Xy(port));
        return plot;
    }

    private static SvgPlot ZigzagPlot(TrialResult r)
    {
        if (r == null) return null;
        var plot = new SvgPlot { Title = r.Title, XLabel = "Время, с", YLabel = "Угол, °" };
        var psi = new List<Vector2>(); var rud = new List<Vector2>();
        foreach (TrialSample p in r.Samples)
        {
            psi.Add(new Vector2((float)p.T, (float)p.DeltaPsiDeg));
            rud.Add(new Vector2((float)p.T, (float)p.RudderActualDeg));
        }
        plot.Add("изменение курса Δψ", SvgPlot.Dash.Solid, psi);
        plot.Add("угол руля δ", SvgPlot.Dash.Dashed, rud);
        return plot;
    }

    private static SvgPlot SpeedPlot(TrialResult crash, TrialResult passive, TrialResult accel)
    {
        var plot = new SvgPlot { Title = "Торможение и разгон", XLabel = "Время, с", YLabel = "Скорость, уз" };
        if (crash != null) plot.Add("полный вперёд → полный назад", SvgPlot.Dash.Solid, Speed(crash));
        if (passive != null) plot.Add("полный вперёд → стоп", SvgPlot.Dash.Dashed, Speed(passive));
        if (accel != null) plot.Add("стоп → полный вперёд", SvgPlot.Dash.Dotted, Speed(accel));
        return plot.Count > 0 ? plot : null;
    }

    private static List<Vector2> Xy(TrialResult r)
    {
        var pts = new List<Vector2>(r.Samples.Count);
        foreach (TrialSample p in r.Samples) pts.Add(new Vector2((float)p.X, (float)p.Y));
        return pts;
    }

    private static List<Vector2> Speed(TrialResult r)
    {
        var pts = new List<Vector2>(r.Samples.Count);
        foreach (TrialSample p in r.Samples) pts.Add(new Vector2((float)p.T, (float)(p.U / ManoeuvringTrials.KnotsToMs)));
        return pts;
    }

    private static string F(double v, int digits) => double.IsNaN(v) ? "" : v.ToString("F" + digits, Inv);
    private static string Esc(string s) => (s ?? "").Replace(';', ',').Replace('\n', ' ');
}
