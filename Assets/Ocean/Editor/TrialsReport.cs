// ============================================================
// TrialsReport.cs — Сохранение результатов манёвренных испытаний
//
// ThesisData/Trials/<параметры>_<дата>/:
//   <id>.csv          — траектория испытания (формат CSV по CLAUDE.md);
//   summary.csv       — сводная таблица (показатель, значение, критерий IMO);
//   summary.md        — то же в Markdown для вставки в ВКР;
//   *.svg             — графики для ВКР: чёрно-белые, линии разного типа,
//                       подписи на русском, шрифт Times New Roman.
// Запрос 19.1 (модель 3 СС):
//   lowspeed_*.csv, lowspeed_drift_spin.svg, lowspeed_kick.svg, lowspeed_astern.svg —
//                       испытания малых скоростей (траектории с контуром корпуса);
//   comparison_3dof.csv/.md — Номото/Норрбин — 3 СС (Хёрнер) — 3 СС (согласованная) [— эталон Mariner];
//   comparison_turning.svg  — циркуляции этих вариантов;
//   model_3dof.md     — коэффициенты модели 3 СС: производные Clarke, k_N, Y'_δ, C_d, k_∞
//                       (у Mariner — рядом производные ПМУ).
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

    public static string Save(List<TrialResult> results, TrialSettings s, List<LowSpeedTrials.Variant> comparison = null)
    {
        string name = s.Parameters != null ? s.Parameters.name : "params";
        if (s.Motion == MotionModel.Manoeuvring3Dof) name += "_3dof";
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

        // Запрос 19.1
        WritePlot(dir, "lowspeed_drift_spin.svg", OutlinePlot("Дрейф под силой в борт и разворот парой сил, без хода",
            s.Parameters, 60.0, (Find("lowspeed_drift"), "дрейф лагом", SvgPlot.Dash.Solid),
            (Find("lowspeed_spin"), "разворот на месте", SvgPlot.Dash.Dashed)));
        WritePlot(dir, "lowspeed_kick.svg", OutlinePlot("«Толчок» машиной с места, руль 35° ПБ, 30 с",
            s.Parameters, 10.0, (Find("lowspeed_kick"), "с обдувом руля струёй винта", SvgPlot.Dash.Solid),
            (Find("lowspeed_kick_noslip"), "без обдува (k_ps = 0)", SvgPlot.Dash.Dashed)));
        WritePlot(dir, "lowspeed_astern.svg", OutlinePlot("Задний ход с места, руль прямо",
            s.Parameters, 20.0, (Find("lowspeed_astern"), "полный назад", SvgPlot.Dash.Solid)));
        bool any3Dof = s.Motion == MotionModel.Manoeuvring3Dof || comparison != null || results.Exists(r => r.Id.StartsWith("lowspeed"));
        if (comparison != null)
        {
            File.WriteAllText(Path.Combine(dir, "comparison_3dof.csv"), ComparisonCsv(comparison), Utf8Bom);
            File.WriteAllText(Path.Combine(dir, "comparison_3dof.md"), ComparisonMarkdown(comparison, s), Utf8Bom);
            WritePlot(dir, "comparison_turning.svg", ComparisonTurningPlot(comparison, s));
        }
        if (any3Dof && s.Parameters != null)
            File.WriteAllText(Path.Combine(dir, "model_3dof.md"), Model3DofMarkdown(s.Parameters), Utf8Bom);
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
        sb.AppendLine("t_s;x_m;y_m;heading_deg;heading_change_deg;u_ms;speed_kn;r_degs;rudder_ordered_deg;rudder_actual_deg;v_ms");
        foreach (TrialSample p in r.Samples)
        {
            sb.Append(F(p.T, 2)).Append(';').Append(F(p.X, 3)).Append(';').Append(F(p.Y, 3)).Append(';')
              .Append(F(p.HeadingDeg, 3)).Append(';').Append(F(p.DeltaPsiDeg, 3)).Append(';')
              .Append(F(p.U, 4)).Append(';').Append(F(p.U / ManoeuvringTrials.KnotsToMs, 3)).Append(';')
              .Append(F(p.RDegS, 5)).Append(';').Append(F(p.RudderOrderedDeg, 3)).Append(';')
              .Append(F(p.RudderActualDeg, 3)).Append(';').Append(F(p.V, 4)).AppendLine();
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
        MotionModel motion = s.Motion ?? p.motionModel;
        if (motion == MotionModel.Manoeuvring3Dof)
            sb.AppendLine($"Дата: {DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv)}. Модель: 3 СС (продольная, поперечная, рыскание; " +
                          $"запрос 19.1, коэффициенты — model_3dof.md), согласована с {yaw} ассета; шаг dt = {F(s.Dt, 3)} с. " +
                          "Испытания малых скоростей — всегда 3 СС.");
        else
            sb.AppendLine($"Дата: {DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv)}. Модель: {yaw} + скорость 1-го порядка{loss} " +
                          $"(ShipDynamicsModel), шаг dt = {F(s.Dt, 3)} с." +
                          (results.Exists(r => r.Id.StartsWith("lowspeed")) ? " Испытания малых скоростей — модель 3 СС (запрос 19.1)." : ""));
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

    // ─────────── Запрос 19.1: малые скорости и сравнение моделей ───────────

    /// <summary>Траектории миделя и контуры корпуса через каждые outlineEvery секунд (вид сверху, восток — вправо).</summary>
    private static SvgPlot OutlinePlot(string title, ShipParameters p, double outlineEvery,
                                       params (TrialResult r, string label, SvgPlot.Dash dash)[] runs)
    {
        if (p == null) return null;
        var plot = new SvgPlot { Title = title, XLabel = "Восток, м", YLabel = "Север, м", EqualAspect = true };
        var outlines = new List<Vector2>();
        foreach (var (r, label, dash) in runs)
        {
            if (r == null) continue;
            plot.Add(label, dash, Xy(r));
            double next = 0.0;
            foreach (TrialSample s in r.Samples)
            {
                if (s.T + 1e-6 < next) continue;
                AddHull(outlines, s, p.length, p.beam);
                next += outlineEvery;
            }
            AddHull(outlines, r.Samples[r.Samples.Count - 1], p.length, p.beam);
        }
        if (plot.Count == 0) return null;
        plot.Add($"контур корпуса (L {F(p.length, 0)} × B {F(p.beam, 1)} м)", SvgPlot.Dash.Dotted, outlines);
        return plot;
    }

    // Контур корпуса: острый нос, прямоугольная корма; связанные оси x — в нос, y — на ПБ
    private static void AddHull(List<Vector2> pts, TrialSample s, double L, double B)
    {
        double psi = s.HeadingDeg * Math.PI / 180.0;
        double sin = Math.Sin(psi), cos = Math.Cos(psi);
        double[,] hull = { { 0.5, 0 }, { 0.3, 0.5 }, { -0.5, 0.5 }, { -0.5, -0.5 }, { 0.3, -0.5 }, { 0.5, 0 } };
        for (int i = 0; i < hull.GetLength(0); i++)
        {
            double x = hull[i, 0] * L, y = hull[i, 1] * B;
            pts.Add(new Vector2((float)(s.X + x * sin + y * cos), (float)(s.Y + x * cos - y * sin)));
        }
        pts.Add(new Vector2(float.NaN, float.NaN));
    }

    private static string ComparisonCsv(List<LowSpeedTrials.Variant> cmp)
    {
        var sb = new StringBuilder();
        sb.Append("trial;metric;unit");
        foreach (var v in cmp) sb.Append(';').Append(Esc(v.Name));
        sb.AppendLine();
        foreach (var (trial, metric, unit, values) in ComparisonRows(cmp))
        {
            sb.Append(Esc(trial)).Append(';').Append(Esc(metric)).Append(';').Append(unit);
            foreach (double x in values) sb.Append(';').Append(F(x, 2));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string ComparisonMarkdown(List<LowSpeedTrials.Variant> cmp, TrialSettings s)
    {
        ShipParameters p = s.Parameters;
        var sb = new StringBuilder();
        sb.AppendLine($"# Сравнение моделей движения: {p.name}");
        sb.AppendLine();
        sb.AppendLine($"Скорость испытаний V = {F(s.TestSpeedMs / ManoeuvringTrials.KnotsToMs, 2)} уз, dt = {F(s.Dt, 3)} с. " +
                      "Варианты: модель ассета (Номото/Норрбин); 3 СС с C_d по Хёрнеру на всех скоростях; 3 СС с согласованной " +
                      "циркуляцией (C_d·k_∞ на ходу, ThesisNotes 19). Коэффициенты — model_3dof.md.");
        sb.AppendLine();
        sb.Append("| Испытание | Показатель |");
        foreach (var v in cmp) sb.Append(' ').Append(v.Name).Append(" |");
        sb.AppendLine();
        sb.Append("|---|---|");
        foreach (var _ in cmp) sb.Append("---|");
        sb.AppendLine();
        foreach (var (trial, metric, unit, values) in ComparisonRows(cmp))
        {
            sb.Append($"| {trial} | {metric}, {unit} |");
            foreach (double x in values) sb.Append(' ').Append(double.IsNaN(x) ? "—" : F(x, unit == "°" ? 2 : 1)).Append(" |");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // Строки сравнения: для каждого показателя первого варианта — значения во всех вариантах (по id испытания и имени)
    private static List<(string trial, string metric, string unit, double[] values)> ComparisonRows(List<LowSpeedTrials.Variant> cmp)
    {
        var rows = new List<(string, string, string, double[])>();
        foreach (TrialResult r in cmp[0].Results)
        foreach (TrialMetric m in r.Metrics)
        {
            var values = new double[cmp.Count];
            for (int i = 0; i < cmp.Count; i++)
            {
                TrialResult ri = cmp[i].Results.Find(x => x.Id == r.Id);
                TrialMetric mi = ri?.Metrics.Find(x => x.Name == m.Name);
                values[i] = mi != null ? mi.Value : double.NaN;
            }
            rows.Add((r.Title, m.Name, m.Unit, values));
        }
        return rows;
    }

    private static SvgPlot ComparisonTurningPlot(List<LowSpeedTrials.Variant> cmp, TrialSettings s)
    {
        var plot = new SvgPlot
        {
            Title = $"Циркуляция, руль 35° ПБ, V = {F(s.TestSpeedMs / ManoeuvringTrials.KnotsToMs, 1)} уз",
            XLabel = "Смещение от начального курса, м",
            YLabel = "Продвижение вдоль начального курса, м",
            EqualAspect = true,
        };
        SvgPlot.Dash[] dashes = { SvgPlot.Dash.Solid, SvgPlot.Dash.Dashed, SvgPlot.Dash.DashDot, SvgPlot.Dash.Dotted };
        for (int i = 0; i < cmp.Count; i++)
        {
            TrialResult r = cmp[i].Results.Find(x => x.Id == "turning_35_stbd");
            if (r != null) plot.Add(cmp[i].Name, dashes[i % dashes.Length], Xy(r));
        }
        return plot.Count > 0 ? plot : null;
    }

    private static string Model3DofMarkdown(ShipParameters p)
    {
        Manoeuvring3DofCoefficients c = Manoeuvring3DofCoefficients.For(p, null);
        ClarkeDerivatives d = c.Clarke;
        var sb = new StringBuilder();
        sb.AppendLine($"# Модель 3 СС: {p.name}");
        sb.AppendLine();
        sb.AppendLine("Формулы — шапка `Core/Manoeuvring/Manoeuvring3Dof.cs`, ThesisNotes 19. Нормировка Clarke: силы ½ρL²U, моменты ½ρL³U, массы ½ρL³ (ρ = 1025 кг/м³).");
        sb.AppendLine();
        sb.AppendLine("| Величина | Значение | Источник |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine($"| L × B × d, C_B | {F(c.L, 2)} × {F(c.B, 2)} × {F(c.T, 2)} м, {F(c.Cb, 3)} | ассет |");
        sb.AppendLine($"| Масса m | {F(c.Mass / 1000.0, 0)} т (m' = {F(c.MassPrime, 5)}) | ρ·C_B·L·B·d |");
        sb.AppendLine($"| Присоединённая масса −X_u̇ | {F(c.AddedMassSurge / 1000.0, 0)} т | Söding (1982), MSS addedMassSurge.m |");
        sb.AppendLine($"| I_z | R₆₆ = {F(p.yawGyrationRadiusL, 3)}·L, x_G = {F(c.XG, 2)} м (I'_z = {F(c.IzPrime, 6)}) | допущение (MSS clarke83.m) |");
        sb.AppendLine($"| Y'_v̇, Y'_ṙ, N'_v̇, N'_ṙ | {F(d.Yvdot, 5)}, {F(d.Yrdot, 6)}, {F(d.Nvdot, 6)}, {F(d.Nrdot, 6)} | Clarke et al. (1983) |");
        sb.AppendLine($"| Y'_v, Y'_r, N'_v, N'_r | {F(d.Yv, 5)}, {F(d.Yr, 5)}, {F(d.Nv, 5)}, {F(d.Nr, 5)} | Clarke et al. (1983) |");
        sb.AppendLine($"| Критерий устойчивости на курсе C' (k_N = 1) | {c.StabilityIndex(1.0):E3} ({(c.StabilityIndex(1.0) > 0 ? "устойчиво" : "неустойчиво")}) | Y'_v·N'_r − (Y'_r − m')·N'_v |");
        sb.AppendLine($"| Множитель демпфирования рыскания k_N | {F(c.YawDampingFactor, 4)} | подбор по T' = {F(c.TargetTPrime, 3)} |");
        sb.AppendLine($"| Y'_δ, x'_R | {F(c.YdeltaPrime, 6)}, {F(c.RudderLeverPrime, 3)} | подбор по K' = {F(c.TargetKPrime, 3)}; x'_R — допущение |");
        sb.AppendLine($"| K', T' линеаризации | {F(c.LinearKPrime, 4)}, {F(c.LinearTPrime, 4)} | {(c.FitExact ? "совпали с ассетом" : c.FitNote)} |");
        sb.AppendLine($"| C_d (поперечное сопротивление) | {F(c.CrossFlowCd, 3)} | {(p.crossFlowDragCoeff > 0f ? "ассет" : $"Хёрнер, B/(2d) = {F(c.B / (2.0 * c.T), 3)}")} |");
        sb.AppendLine($"| Множитель C_d на ходу k_∞ | {F(c.CrossFlowHighSpeedFactor, 3)} (переход {F(Manoeuvring3DofCoefficients.FadeLow, 2)}…{F(Manoeuvring3DofCoefficients.FadeHigh, 2)}·V_эксп) | {(p.matchTurningCircle ? "подбор по циркуляции" : "согласование выключено")} {c.CircleNote} |");
        sb.AppendLine($"| Диаметр установившейся циркуляции, руль {F(Manoeuvring3DofCoefficients.CircleRudderDeg(p), 0)}°, «Полный вперёд» | цель {F(c.CircleTargetD, 1)} м; 3 СС {F(c.CircleD, 1)} м; 3 СС с C_d Хёрнера {F(c.CircleHoernerD, 1)} м | модель ассета / 3 СС |");
        sb.AppendLine($"| Обдув руля k_ps, поперечная сила винта | {F(p.rudderSlipstreamFactor, 2)}, {F(100.0 * p.propellerSideForceFactor, 1)} % упора | допущения |");

        if (p.name.IndexOf("Mariner", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            ClarkeDerivatives m = MarinerModel.HydroDerivatives(out double mp, out double izp, out double xgp);
            sb.AppendLine();
            sb.AppendLine("## Регрессия Clarke против ПМУ-модели Mariner (Chislett & Strøm-Tejsen, 1965; MSS mariner.m)");
            sb.AppendLine();
            sb.AppendLine("Гидродинамические Y'_r, N'_r Mariner — без центробежного члена (Y'_r + m', N'_r + m'·x'_G).");
            sb.AppendLine();
            sb.AppendLine("| Производная | Clarke (по размерениям ассета) | ПМУ Mariner | Отношение |");
            sb.AppendLine("|---|---|---|---|");
            void Row(string n, double a, double b) => sb.AppendLine($"| {n} | {F(a, 6)} | {F(b, 6)} | {(Math.Abs(b) > 1e-12 ? F(a / b, 2) : "—")} |");
            Row("Y'_v̇", d.Yvdot, m.Yvdot); Row("Y'_ṙ", d.Yrdot, m.Yrdot);
            Row("N'_v̇", d.Nvdot, m.Nvdot); Row("N'_ṙ", d.Nrdot, m.Nrdot);
            Row("Y'_v", d.Yv, m.Yv); Row("Y'_r", d.Yr, m.Yr);
            Row("N'_v", d.Nv, m.Nv); Row("N'_r", d.Nr, m.Nr);
            Row("m'", c.MassPrime, mp); Row("I'_z", c.IzPrime, izp);
            MarinerModel.LinearNomoto(out double kM, out double tM, out _, out _, out _);
            sb.AppendLine();
            sb.AppendLine($"Линейные K', T' эталона Mariner (по производным ПМУ): {F(kM, 3)}, {F(tM, 3)}; у ассета (идентификация, Норрбин): {F(p.nomotoKPrime, 3)}, {F(p.nomotoTPrime, 3)}.");
        }
        return sb.ToString();
    }

    private static string F(double v, int digits) => double.IsNaN(v) ? "" : v.ToString("F" + digits, Inv);
    private static string Esc(string s) => (s ?? "").Replace(';', ',').Replace('\n', ' ');
}
