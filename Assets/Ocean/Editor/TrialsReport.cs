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
        sb.AppendLine($"Дата: {DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv)}. Модель: Номото 1-го порядка + скорость 1-го порядка " +
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

/// <summary>Простой генератор графиков SVG (линии, оси, сетка, легенда) — для печати по ГОСТ.</summary>
public sealed class SvgPlot
{
    public enum Dash { Solid, Dashed, Dotted, DashDot }

    public string Title = "", XLabel = "", YLabel = "";
    public bool EqualAspect;
    public float Width = 680, Height = 480;

    private readonly List<(string label, Dash dash, List<Vector2> pts)> series = new List<(string, Dash, List<Vector2>)>();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public int Count => series.Count;
    public void Add(string label, Dash dash, List<Vector2> pts) { if (pts != null && pts.Count > 1) series.Add((label, dash, pts)); }

    public string Render()
    {
        const float ml = 80, mr = 20, mt = 40, mb = 60;
        float pw = Width - ml - mr, ph = Height - mt - mb;

        // Диапазоны
        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        foreach (var s in series)
        foreach (Vector2 p in s.pts)
        {
            x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x);
            y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y);
        }
        if (x1 - x0 < 1e-6f) { x0 -= 1; x1 += 1; }
        if (y1 - y0 < 1e-6f) { y0 -= 1; y1 += 1; }
        float xs = NiceStep(x1 - x0), ys = NiceStep(y1 - y0);
        x0 = Mathf.Floor(x0 / xs) * xs; x1 = Mathf.Ceil(x1 / xs) * xs;
        y0 = Mathf.Floor(y0 / ys) * ys; y1 = Mathf.Ceil(y1 / ys) * ys;
        if (EqualAspect)
        {
            // Одинаковый масштаб по осям: расширяем меньший диапазон
            float k = Mathf.Max((x1 - x0) / pw, (y1 - y0) / ph);
            float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
            x0 = cx - k * pw / 2; x1 = cx + k * pw / 2;
            y0 = cy - k * ph / 2; y1 = cy + k * ph / 2;
            xs = ys = NiceStep(Mathf.Max(x1 - x0, y1 - y0));
        }

        float X(float v) => ml + (v - x0) / (x1 - x0) * pw;
        float Y(float v) => mt + ph - (v - y0) / (y1 - y0) * ph;

        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(Width)}\" height=\"{N(Height)}\" " +
                      $"viewBox=\"0 0 {N(Width)} {N(Height)}\" font-family=\"Times New Roman, serif\" font-size=\"14\">");
        sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");

        // Сетка и подписи делений
        for (float v = Mathf.Ceil(x0 / xs) * xs; v <= x1 + xs * 1e-3f; v += xs)
        {
            sb.AppendLine($"<line x1=\"{N(X(v))}\" y1=\"{N(mt)}\" x2=\"{N(X(v))}\" y2=\"{N(mt + ph)}\" stroke=\"#c8c8c8\" stroke-width=\"0.5\"/>");
            sb.AppendLine($"<text x=\"{N(X(v))}\" y=\"{N(mt + ph + 18)}\" text-anchor=\"middle\">{Tick(v, xs)}</text>");
        }
        for (float v = Mathf.Ceil(y0 / ys) * ys; v <= y1 + ys * 1e-3f; v += ys)
        {
            sb.AppendLine($"<line x1=\"{N(ml)}\" y1=\"{N(Y(v))}\" x2=\"{N(ml + pw)}\" y2=\"{N(Y(v))}\" stroke=\"#c8c8c8\" stroke-width=\"0.5\"/>");
            sb.AppendLine($"<text x=\"{N(ml - 6)}\" y=\"{N(Y(v) + 5)}\" text-anchor=\"end\">{Tick(v, ys)}</text>");
        }
        sb.AppendLine($"<rect x=\"{N(ml)}\" y=\"{N(mt)}\" width=\"{N(pw)}\" height=\"{N(ph)}\" fill=\"none\" stroke=\"black\" stroke-width=\"1\"/>");

        // Подписи осей и заголовок
        sb.AppendLine($"<text x=\"{N(ml + pw / 2)}\" y=\"{N(Height - 14)}\" text-anchor=\"middle\">{Xml(XLabel)}</text>");
        sb.AppendLine($"<text x=\"18\" y=\"{N(mt + ph / 2)}\" text-anchor=\"middle\" transform=\"rotate(-90 18 {N(mt + ph / 2)})\">{Xml(YLabel)}</text>");
        sb.AppendLine($"<text x=\"{N(ml + pw / 2)}\" y=\"24\" text-anchor=\"middle\" font-size=\"15\">{Xml(Title)}</text>");

        // Линии
        sb.AppendLine($"<clipPath id=\"plot\"><rect x=\"{N(ml)}\" y=\"{N(mt)}\" width=\"{N(pw)}\" height=\"{N(ph)}\"/></clipPath>");
        foreach (var s in series)
        {
            var pts = new StringBuilder();
            foreach (Vector2 p in s.pts) pts.Append(N(X(p.x))).Append(',').Append(N(Y(p.y))).Append(' ');
            sb.AppendLine($"<polyline clip-path=\"url(#plot)\" fill=\"none\" stroke=\"black\" stroke-width=\"1.6\"{DashAttr(s.dash)} points=\"{pts}\"/>");
        }

        // Легенда (правый верхний угол поля)
        float ly = mt + 16;
        foreach (var s in series)
        {
            float lx = ml + pw - 250;
            sb.AppendLine($"<rect x=\"{N(lx - 6)}\" y=\"{N(ly - 13)}\" width=\"254\" height=\"20\" fill=\"white\" fill-opacity=\"0.85\"/>");
            sb.AppendLine($"<line x1=\"{N(lx)}\" y1=\"{N(ly - 4)}\" x2=\"{N(lx + 40)}\" y2=\"{N(ly - 4)}\" stroke=\"black\" stroke-width=\"1.6\"{DashAttr(s.dash)}/>");
            sb.AppendLine($"<text x=\"{N(lx + 48)}\" y=\"{N(ly)}\">{Xml(s.label)}</text>");
            ly += 20;
        }
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static string DashAttr(Dash d)
    {
        switch (d)
        {
            case Dash.Dashed: return " stroke-dasharray=\"8,4\"";
            case Dash.Dotted: return " stroke-dasharray=\"2,3\"";
            case Dash.DashDot: return " stroke-dasharray=\"8,3,2,3\"";
            default: return "";
        }
    }

    public static float NiceStep(float range)
    {
        float raw = range / 6f;
        float mag = Mathf.Pow(10, Mathf.Floor(Mathf.Log10(raw)));
        float n = raw / mag;
        return (n < 1.5f ? 1 : n < 3.5f ? 2 : n < 7.5f ? 5 : 10) * mag;
    }

    // Подписи делений: десятичная запятая (ГОСТ)
    private static string Tick(float v, float step)
    {
        if (Mathf.Abs(v) < step * 1e-3f) v = 0;
        int digits = step >= 1 ? 0 : Mathf.CeilToInt(-Mathf.Log10(step));
        return v.ToString("F" + digits, Inv).Replace('.', ',').Replace("-", "−");
    }

    private static string N(float v) => v.ToString("0.##", Inv);
    private static string Xml(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
