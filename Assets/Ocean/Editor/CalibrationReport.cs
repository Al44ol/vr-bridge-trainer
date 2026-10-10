// ============================================================
// CalibrationReport.cs — Отчёт о калибровке модели (A3.2)
//
// ThesisData/Calibration/<судно>_<дата>/:
//   parameters.csv / .md   — найденные параметры по этапам, пояснения, ограничения;
//   comparison.csv / .md   — «источник / модель / погрешность, %» по каждому показателю;
//   replay_errors.csv      — RMS-ошибки повтора руля эталона (Mariner);
//   reference/<id>.csv     — эталонные манёвры (Mariner);
//   stages/<этап>/<id>.csv — испытания стенда с параметрами этапа;
//   *.svg                  — графики тем же SvgPlot, что и стенд испытаний
//                            (чёрно-белые, Times New Roman, десятичная запятая).
// Формат CSV — по CLAUDE.md (точка, «;», UTF-8 с BOM, единицы в заголовках).
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class CalibrationReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);

    public static string Save(CalibrationResult res)
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ThesisData", "Calibration",
            $"{res.Ship}_{DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv)}"));
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "parameters.csv"), ParametersCsv(res), Utf8Bom);
        File.WriteAllText(Path.Combine(dir, "parameters.md"), ParametersMarkdown(res), Utf8Bom);
        File.WriteAllText(Path.Combine(dir, "comparison.csv"), ComparisonCsv(res), Utf8Bom);
        File.WriteAllText(Path.Combine(dir, "comparison.md"), ComparisonMarkdown(res), Utf8Bom);

        if (res.Reference.Count > 0)
        {
            File.WriteAllText(Path.Combine(dir, "replay_errors.csv"), ReplayErrorsCsv(res), Utf8Bom);
            string refDir = Path.Combine(dir, "reference");
            Directory.CreateDirectory(refDir);
            foreach (TrialResult r in res.Reference)
                File.WriteAllText(Path.Combine(refDir, r.Id + ".csv"), TrialsReport.TrajectoryCsv(Decimate(r, 0.5)), Utf8Bom);
        }
        foreach (CalibrationStage st in res.Stages)
        {
            string sd = Path.Combine(dir, "stages", st.Id);
            Directory.CreateDirectory(sd);
            foreach (TrialResult r in st.Trials)
                File.WriteAllText(Path.Combine(sd, r.Id + ".csv"), TrialsReport.TrajectoryCsv(r), Utf8Bom);
        }

        if (res.Ship == "Mariner") WriteMarinerPlots(dir, res);
        else WriteModelOnlyPlots(dir, res);
        return dir;
    }

    // ─────────── Таблицы ───────────

    private static string ParametersCsv(CalibrationResult res)
    {
        var sb = new StringBuilder();
        sb.AppendLine("stage;title;yaw_model;K_prime;T_prime;alpha_prime;Cr_prime;tau_acc_s;cost_J;evaluations;recommended");
        for (int i = 0; i < res.Stages.Count; i++)
        {
            CalibrationStage st = res.Stages[i]; ShipParameters p = st.Parameters;
            sb.Append(st.Id).Append(';').Append(Esc(st.Title)).Append(';').Append(p.yawModel).Append(';')
              .Append(F(p.nomotoKPrime, 5)).Append(';').Append(F(p.nomotoTPrime, 5)).Append(';')
              .Append(F(p.yawModel == YawModel.Norrbin ? p.norrbinAlphaPrime : 0, 5)).Append(';')
              .Append(F(p.turnSpeedLossCoeff, 5)).Append(';').Append(F(p.accelerationTimeConstant, 2)).Append(';')
              .Append(F(st.Cost, 6)).Append(';').Append(st.Evaluations).Append(';')
              .Append(i == res.Recommended ? "да" : "").AppendLine();
        }
        return sb.ToString();
    }

    private static string ParametersMarkdown(CalibrationResult res)
    {
        ShipParameters b = res.Base;
        var sb = new StringBuilder();
        sb.AppendLine($"# Калибровка модели: {res.Title}");
        sb.AppendLine();
        sb.AppendLine($"Дата: {DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv)}. Шаг dt = {F(res.Dt, 3)} с. " +
                      $"Скорость манёвров V = {F(res.TestSpeedMs / ManoeuvringTrials.KnotsToMs, 2)} уз ({F(res.TestSpeedMs, 3)} м/с).");
        sb.AppendLine();
        sb.AppendLine($"Общие параметры: L = {F(b.length, 2)} м; V_эксп = {F(b.serviceSpeedKnots, 2)} уз; δ_max = {F(b.maxRudderAngle, 0)}°; " +
                      $"ω_δ = {F(b.rudderRate, 2)} °/с; τ_разг = {F(b.accelerationTimeConstant, 1)} с; τ_торм = {F(b.decelerationTimeConstant, 0)} с; " +
                      $"k_зх = {F(b.asternFactor, 2)}; C_r' = {F(b.turnSpeedLossCoeff, 4)}. Источники — Assets/Ocean/Settings/ShipParameters_Sources.md.");
        sb.AppendLine();
        sb.AppendLine("| Этап | Модель | K' | T' | α' | J | Вычислений J |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        for (int i = 0; i < res.Stages.Count; i++)
        {
            CalibrationStage st = res.Stages[i]; ShipParameters p = st.Parameters;
            sb.AppendLine($"| {(i == res.Recommended ? "**" + st.Title + "**" : st.Title)}<br><small>{st.Description}</small> | {p.yawModel} | " +
                          $"{F(p.nomotoKPrime, 3)} | {F(p.nomotoTPrime, 3)} | {(p.yawModel == YawModel.Norrbin ? F(p.norrbinAlphaPrime, 2) : "—")} | " +
                          $"{F(st.Cost, 4)} | {st.Evaluations} |");
        }
        if (res.Reference.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("RMS-ошибки повтора руля эталона (Δψ, °):");
            sb.AppendLine();
            var ids = new List<string>(res.Stages[0].ReplayErrors.Keys);
            sb.AppendLine("| Этап | " + string.Join(" | ", ids) + " |");
            sb.AppendLine("|---|" + string.Join("", ids.ConvertAll(_ => "---|")));
            foreach (CalibrationStage st in res.Stages)
                sb.AppendLine($"| {st.Title} | " + string.Join(" | ", ids.ConvertAll(id => F(st.ReplayErrors[id].psiDeg, 2))) + " |");
        }
        sb.AppendLine();
        sb.AppendLine("Пояснения и ограничения:");
        sb.AppendLine();
        foreach (string n in res.Notes) sb.AppendLine("- " + n);
        return sb.ToString();
    }

    private static string ComparisonCsv(CalibrationResult res)
    {
        var sb = new StringBuilder();
        sb.Append("trial;metric;unit;reference;source;used_in_fit");
        foreach (CalibrationStage st in res.Stages) sb.Append(';').Append(st.Id).Append("_value;").Append(st.Id).Append("_error_pct");
        sb.AppendLine();
        foreach (ComparisonRow r in res.Rows)
        {
            sb.Append(Esc(r.TrialTitle)).Append(';').Append(Esc(r.Metric)).Append(';').Append(r.Unit).Append(';')
              .Append(F(r.Reference, 4)).Append(';').Append(Esc(r.Source)).Append(';').Append(r.UseInFit ? "да" : "нет");
            for (int i = 0; i < res.Stages.Count; i++)
                sb.Append(';').Append(F(r.Values[i], 4)).Append(';').Append(F(r.ErrorPercent(i), 1));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string ComparisonMarkdown(CalibrationResult res)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Сравнение с эталоном: {res.Title}");
        sb.AppendLine();
        sb.AppendLine("Погрешность δ = (модель − эталон) / эталон · 100 %.");
        sb.AppendLine();
        sb.Append("| Испытание | Показатель | Эталон |");
        foreach (CalibrationStage st in res.Stages) sb.Append($" {st.Title} | δ, % |");
        sb.AppendLine();
        sb.Append("|---|---|---|");
        foreach (CalibrationStage _ in res.Stages) sb.Append("---|---|");
        sb.AppendLine();
        foreach (ComparisonRow r in res.Rows)
        {
            int digits = r.Unit == "L" ? 2 : r.Unit == "°" ? 2 : 1;
            sb.Append($"| {r.TrialTitle} | {r.Metric}{(r.UseInFit ? "" : " (справочно)")} | {F(r.Reference, digits)} {r.Unit} |");
            for (int i = 0; i < res.Stages.Count; i++) sb.Append($" {F(r.Values[i], digits)} | {Pct(r.ErrorPercent(i))} |");
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("Источники эталона:");
        sb.AppendLine();
        var sources = new HashSet<string>();
        foreach (ComparisonRow r in res.Rows) if (sources.Add(r.Source)) sb.AppendLine("- " + r.Source);
        return sb.ToString();
    }

    private static string ReplayErrorsCsv(CalibrationResult res)
    {
        var sb = new StringBuilder();
        sb.AppendLine("stage;manoeuvre;rms_heading_change_deg;rms_r_degs;rms_u_ms");
        foreach (CalibrationStage st in res.Stages)
        foreach (var kv in st.ReplayErrors)
            sb.Append(st.Id).Append(';').Append(kv.Key).Append(';').Append(F(kv.Value.psiDeg, 3)).Append(';')
              .Append(F(kv.Value.rDegS, 5)).Append(';').Append(F(kv.Value.uMs, 4)).AppendLine();
        return sb.ToString();
    }

    // ─────────── Графики ───────────

    private static void WriteMarinerPlots(string dir, CalibrationResult res)
    {
        CalibrationStage lin = res.Stages.Find(s => s.Id == "nomoto_all");
        CalibrationStage nor = res.Stages.Find(s => s.Id == "norrbin_all");
        CalibrationStage zz = res.Stages.Find(s => s.Id == "nomoto_zz10");
        CalibrationStage tc = res.Stages.Find(s => s.Id == "nomoto_tc35");
        TrialResult Ref(string id) => res.Reference.Find(r => r.Id == id);
        TrialResult Of(CalibrationStage st, string id) => st?.Trials.Find(r => r.Id == id);

        foreach (string id in new[] { "zigzag_10_10", "zigzag_20_20" })
        {
            TrialResult r = Ref(id);
            if (r == null) continue;
            var plot = new SvgPlot { Title = $"{r.Title}, V = 15 уз: эталон и модель", XLabel = "Время, с", YLabel = "Угол, °" };
            plot.Add("Δψ эталон (Mariner)", SvgPlot.Dash.Solid, Series(Decimate(r, 0.5), s => s.T, s => s.DeltaPsiDeg));
            plot.Add("Δψ Номото", SvgPlot.Dash.Dashed, Series(Of(lin, id), s => s.T, s => s.DeltaPsiDeg));
            plot.Add("Δψ Норрбин", SvgPlot.Dash.Dotted, Series(Of(nor, id), s => s.T, s => s.DeltaPsiDeg));
            plot.Add("угол руля δ", SvgPlot.Dash.DashDot, Series(Decimate(r, 0.5), s => s.T, s => s.RudderActualDeg));
            Write(dir, id + ".svg", plot);
        }

        TrialResult tc35 = Ref("turning_35_stbd");
        if (tc35 != null)
        {
            var plot = new SvgPlot
            {
                Title = "Циркуляция, руль 35° ПБ, V = 15 уз", XLabel = "Смещение от начального курса, м",
                YLabel = "Продвижение вдоль начального курса, м", EqualAspect = true,
            };
            plot.Add("эталон (Mariner)", SvgPlot.Dash.Solid, Series(Decimate(tc35, 0.5), s => s.X, s => s.Y));
            plot.Add("Номото", SvgPlot.Dash.Dashed, Series(Of(lin, tc35.Id), s => s.X, s => s.Y));
            plot.Add("Норрбин", SvgPlot.Dash.Dotted, Series(Of(nor, tc35.Id), s => s.X, s => s.Y));
            Write(dir, "turning_35.svg", plot);

            var sp = new SvgPlot { Title = "Скорость на циркуляции, руль 35° ПБ", XLabel = "Время, с", YLabel = "Скорость, уз" };
            Func<TrialSample, double> kn = s => s.U / ManoeuvringTrials.KnotsToMs;
            sp.Add("эталон (Mariner)", SvgPlot.Dash.Solid, Series(Decimate(tc35, 0.5), s => s.T, kn));
            sp.Add("Номото", SvgPlot.Dash.Dashed, Series(Of(lin, tc35.Id), s => s.T, kn));
            sp.Add("Норрбин", SvgPlot.Dash.Dotted, Series(Of(nor, tc35.Id), s => s.T, kn));
            Write(dir, "turning_35_speed.svg", sp);
        }

        // Установившаяся безразмерная угловая скорость r' = L/R = 2L/D по углу руля
        var rp = new SvgPlot
        {
            Title = "Установившаяся циркуляция: r' = L/R от угла руля", XLabel = "Угол руля δ, °", YLabel = "r' = L/R",
            LegendBottomRight = true,
        };
        rp.Add("эталон (Mariner)", SvgPlot.Dash.Solid, SteadyRate(res.Reference));
        rp.Add("Номото по зигзагу 10°/10°", SvgPlot.Dash.Dashed, SteadyRate(zz?.Trials));
        rp.Add("Номото по циркуляции 35°", SvgPlot.Dash.DashDot, SteadyRate(tc?.Trials));
        rp.Add("Норрбин по всем манёврам", SvgPlot.Dash.Dotted, SteadyRate(nor?.Trials));
        Write(dir, "steady_turn_rate.svg", rp);
    }

    private static void WriteModelOnlyPlots(string dir, CalibrationResult res)
    {
        CalibrationStage a = res.Stages.Count > 0 ? res.Stages[0] : null;
        CalibrationStage b = res.Stages.Count > 1 ? res.Stages[1] : null;
        TrialResult Of(CalibrationStage st, string id) => st?.Trials.Find(r => r.Id == id);
        string v = F(res.TestSpeedMs / ManoeuvringTrials.KnotsToMs, 1).Replace('.', ',');
        foreach (string id in new[] { "zigzag_10_10", "zigzag_20_20" })
        {
            TrialResult ra = Of(a, id), rb = Of(b, id);
            if (ra == null) continue;
            var plot = new SvgPlot { Title = $"{ra.Title}, V = {v} уз: модель", XLabel = "Время, с", YLabel = "Угол, °" };
            plot.Add($"Δψ {a.Title}", SvgPlot.Dash.Dashed, Series(ra, s => s.T, s => s.DeltaPsiDeg));
            if (rb != null) plot.Add($"Δψ {b.Title}", SvgPlot.Dash.Solid, Series(rb, s => s.T, s => s.DeltaPsiDeg));
            plot.Add("угол руля δ", SvgPlot.Dash.Dotted, Series(rb ?? ra, s => s.T, s => s.RudderActualDeg));
            Write(dir, id + ".svg", plot);
        }
        TrialResult ta = Of(a, "turning_35_stbd"), tb = Of(b, "turning_35_stbd");
        if (ta != null)
        {
            var plot = new SvgPlot
            {
                Title = $"Циркуляция, руль 35°, V = {v} уз: модель", XLabel = "Смещение от начального курса, м",
                YLabel = "Продвижение вдоль начального курса, м", EqualAspect = true,
            };
            plot.Add(a.Title, SvgPlot.Dash.Dashed, Series(ta, s => s.X, s => s.Y));
            if (tb != null) plot.Add(b.Title, SvgPlot.Dash.Solid, Series(tb, s => s.X, s => s.Y));
            Write(dir, "turning_35.svg", plot);
        }
    }

    private static List<Vector2> SteadyRate(List<TrialResult> trials)
    {
        if (trials == null) return null;
        var pts = new List<Vector2> { new Vector2(0, 0) };
        foreach (double d in new[] { 10.0, 20.0, 35.0 })
        {
            TrialResult t = trials.Find(r => r.Id == $"turning_{d:F0}_stbd");
            TrialMetric m = t?.Metrics.Find(x => x.Name == "Диаметр установившейся циркуляции");
            if (m != null && !double.IsNaN(m.ValueInL) && m.ValueInL > 0) pts.Add(new Vector2((float)d, (float)(2.0 / m.ValueInL)));
        }
        return pts;
    }

    private static List<Vector2> Series(TrialResult r, Func<TrialSample, double> x, Func<TrialSample, double> y)
    {
        if (r == null) return null;
        var pts = new List<Vector2>(r.Samples.Count);
        foreach (TrialSample s in r.Samples) pts.Add(new Vector2((float)x(s), (float)y(s)));
        return pts;
    }

    /// <summary>Прореживание записи эталона (шаг dt) до интервала CSV/графиков.</summary>
    private static TrialResult Decimate(TrialResult r, double interval)
    {
        var o = new TrialResult { Id = r.Id, Title = r.Title, Completed = r.Completed, Message = r.Message };
        o.Metrics.AddRange(r.Metrics);
        double next = 0;
        foreach (TrialSample s in r.Samples)
            if (s.T + 1e-9 >= next) { o.Samples.Add(s); next += interval; }
        if (r.Samples.Count > 0 && o.Samples[o.Samples.Count - 1].T < r.Samples[r.Samples.Count - 1].T)
            o.Samples.Add(r.Samples[r.Samples.Count - 1]);
        return o;
    }

    private static void Write(string dir, string file, SvgPlot plot)
    {
        if (plot != null && plot.Count > 0) File.WriteAllText(Path.Combine(dir, file), plot.Render(), new UTF8Encoding(false));
    }

    private static string Pct(double v) => double.IsNaN(v) ? "—" : (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("F1", Inv);
    private static string F(double v, int digits) => double.IsNaN(v) ? "" : v.ToString("F" + digits, Inv);
    private static string Esc(string s) => (s ?? "").Replace(';', ',').Replace('\n', ' ');
}
