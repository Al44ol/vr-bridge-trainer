// ============================================================
// MooringReport.cs — Стенд швартовки для ВКР без Play Mode (запрос 19.2)
// Меню: Tools/VR Bridge/Mooring Trials → ThesisData/Mooring/<дата>/<судно>/ (Model45m и Mariner):
//   impacts.csv          — навалы лагом 0,05…0,5 уз и носом под 5°/10°: энергия W, ½·m₂₂·v², PIANC,
//                          доля, обжатие (доля δ_н), сумма реакций, коэффициент восстановления;
//   impact_0.2kn.csv     — навал 0,2 уз по времени: зазор на миделе, ΣR, скорость к кромке;
//   fender_loop.svg      — петля «обжатие — реакция» навала 0,2 уз и кривая кранцев (нагрузка);
//   berthing_energy.svg  — энергия навала по скорости: модель, ½·m₂₂·v², PIANC;
//   hold.csv, mooring_hold.svg — удержание на концах: натяжения при нарастающей силе;
//   thruster.csv, thruster.svg — подруливающее на ходу 0/2/4/6 уз;
//   summary.md (в папке даты) — сводка по обоим судам для ВКР.
// Формат CSV — по CLAUDE.md, графики — SvgPlot (стиль раздела 2 Handoff).
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class MooringReport
{
    private const double Kn = 0.514444;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);

    [MenuItem("Tools/VR Bridge/Mooring Trials")]
    private static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[Швартовка] Выйдите из Play Mode.");
            return;
        }
        var ships = new List<ShipParameters>();
        foreach (string n in new[] { "ShipParameters_Model45m", "ShipParameters_Mariner" })
        {
            ShipParameters p = AssetDatabase.LoadAssetAtPath<ShipParameters>($"Assets/Ocean/Settings/{n}.asset");
            if (p != null) ships.Add(p);
            else Debug.LogWarning($"[Швартовка] Нет ассета {n}.");
        }
        if (ships.Count == 0) return;

        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ThesisData", "Mooring", DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv)));
        Directory.CreateDirectory(root);
        var md = new StringBuilder("# Запрос 19.2: причал, кранцы, концы, подруливающее — стенд\n\n");
        md.AppendLine("Модель 3 СС (разд. 19), dt = 0,02 с, причал «под судно» (`BerthArea.ForShip`). Формулы и допущения — ThesisNotes, разд. 19.11–19.14.\n");
        try
        {
            for (int i = 0; i < ships.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Стенд швартовки", ships[i].name, (float)i / ships.Count);
                BerthingTrials.Report r = BerthingTrials.RunAll(ships[i]);
                string dir = Path.Combine(root, ships[i].name.Replace("ShipParameters_", ""));
                Directory.CreateDirectory(dir);
                Write(dir, r);
                Summary(md, r);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        File.WriteAllText(Path.Combine(root, "summary.md"), md.ToString(), Utf8Bom);
        Debug.Log($"[Швартовка] Сохранено: {root}");
        EditorUtility.RevealInFinder(Path.Combine(root, "summary.md"));
    }

    private static string N(double v, int d = 2) => double.IsNaN(v) ? "" : v.ToString("F" + d, Inv);
    private static string R(double v, int d = 1) => double.IsNaN(v) ? "—" : ScenarioText.Num(v, d);

    private static void Write(string dir, BerthingTrials.Report r)
    {
        // Навалы
        var csv = new StringBuilder("approach_kn;angle_deg;vn_ms;energy_kj;kinetic_m22_kj;pianc_kj;energy_share;max_deflection_ratio;max_force_kn;restitution;fenders;quay_impact;fender_overload\n");
        foreach (BerthingTrials.ImpactRun i in Impacts(r))
            csv.AppendLine($"{N(i.ApproachKn)};{N(i.AngleDeg, 0)};{N(i.Vn, 4)};{N(i.Energy / 1e3, 3)};{N(i.KineticM22 / 1e3, 3)};{N(i.Pianc / 1e3, 3)};" +
                           $"{N(i.EnergyShare, 3)};{N(i.MaxRatio, 3)};{N(i.MaxForce / 1e3, 1)};{N(i.Restitution, 3)};{i.Fenders};{(i.QuayImpact ? 1 : 0)};{(i.FenderOverload ? 1 : 0)}");
        Save(dir, "impacts.csv", csv);

        BerthingTrials.ImpactRun rec = r.Impacts.Find(i => i.Series.Count > 0);
        if (rec != null)
        {
            var ts = new StringBuilder("t_s;gap_m;force_kn;approach_speed_ms\n");
            foreach (var s in rec.Series) ts.AppendLine($"{N(s.t, 1)};{N(s.gap, 4)};{N(s.force / 1e3, 2)};{N(s.vn, 4)}");
            Save(dir, "impact_0.2kn.csv", ts);

            var loop = new SvgPlot { Title = $"Навал лагом 0,2 уз: обжатие — реакция ({r.Parameters.name.Replace("ShipParameters_", "")})", XLabel = "Наибольшее обжатие кранца, м", YLabel = "Сумма реакций кранцев, кН" };
            var pts = new List<Vector2>();
            foreach (var l in rec.Loop) pts.Add(new Vector2((float)l.defl, (float)(l.force / 1e3)));
            loop.Add("модель (нагрузка и разгрузка)", SvgPlot.Dash.Solid, pts);
            var curve = new List<Vector2>();
            double maxD = 0;
            foreach (var l in rec.Loop) maxD = Math.Max(maxD, l.defl);
            for (int k = 0; k <= 40; k++)
            {
                double d = maxD * k / 40.0;
                curve.Add(new Vector2((float)d, (float)(rec.Fenders * r.Fender.Reaction(d) / 1e3)));
            }
            loop.Add($"кривая кранца × {rec.Fenders} (без гистерезиса)", SvgPlot.Dash.Dashed, curve);
            WritePlot(dir, "fender_loop.svg", loop);
        }

        var en = new SvgPlot { Title = "Энергия навала лагом по скорости подхода", XLabel = "Скорость подхода к кромке, уз", YLabel = "Энергия, кДж", LegendBottomRight = false };
        var w = new List<Vector2>(); var ek = new List<Vector2>(); var pi = new List<Vector2>();
        foreach (BerthingTrials.ImpactRun i in r.Impacts)
        {
            w.Add(new Vector2((float)i.ApproachKn, (float)(i.Energy / 1e3)));
            ek.Add(new Vector2((float)i.ApproachKn, (float)(i.KineticM22 / 1e3)));
            pi.Add(new Vector2((float)i.ApproachKn, (float)(i.Pianc / 1e3)));
        }
        en.Add("поглощена кранцами (модель)", SvgPlot.Dash.Solid, w);
        en.Add("½·m₂₂·v² (присоединённая масса модели)", SvgPlot.Dash.Dashed, ek);
        en.Add("PIANC, C_M = 1 + 2d/B, C_E = 1", SvgPlot.Dash.Dotted, pi);
        WritePlot(dir, "berthing_energy.svg", en);

        // Удержание
        var hold = new StringBuilder("mode;force_kn;offset_m;" + string.Join(";", Enum.GetNames(typeof(MooringLineKind))) + "\n");
        foreach (BerthingTrials.HoldRun h in r.Holds)
            foreach (var s in h.Series)
            {
                hold.Append($"{(h.Longitudinal ? "along" : "off")}_{(h.OnBitts ? "bitts" : "brake")};{N(s.force / 1e3, 1)};{N(s.offset, 3)}");
                foreach (double t in s.tension) hold.Append(';').Append(N(t / 1e3, 1));
                hold.AppendLine();
            }
        Save(dir, "hold.csv", hold);

        BerthingTrials.HoldRun off = r.Holds.Find(h => !h.Longitudinal && h.OnBitts);
        if (off != null)
        {
            var hp = new SvgPlot { Title = "Удержание на концах (на кнехтах): сила от причала", XLabel = "Сила от причала, кН", YLabel = "Натяжение, кН", LegendBottomRight = false };
            SvgPlot.Dash[] dashes = { SvgPlot.Dash.Solid, SvgPlot.Dash.Dashed, SvgPlot.Dash.Dotted, SvgPlot.Dash.DashDot, SvgPlot.Dash.Dashed, SvgPlot.Dash.Dotted };
            int lines = off.Series.Count > 0 ? off.Series[0].tension.Length : 0;
            for (int k = 0; k < lines; k++)
            {
                var line = new List<Vector2>();
                foreach (var s in off.Series) line.Add(new Vector2((float)(s.force / 1e3), (float)(s.tension[k] / 1e3)));
                hp.Add(MooringLine.Title((MooringLineKind)k), dashes[k % dashes.Length], line);
            }
            float fMax = off.Series.Count > 0 ? (float)(off.Series[off.Series.Count - 1].force / 1e3) : 1f;
            hp.Add("0,5·MBL (рабочая нагрузка)", SvgPlot.Dash.DashDot, new List<Vector2> { new Vector2(0, (float)(0.5 * r.Mbl / 1e3)), new Vector2(fMax, (float)(0.5 * r.Mbl / 1e3)) });
            WritePlot(dir, "mooring_hold.svg", hp);
        }

        // Подруливающее
        var th = new StringBuilder("speed_kn;speed_factor;force_kn;delta_psi_60s_deg;r_60s_deg_s;v_60s_ms\n");
        var tp = new SvgPlot { Title = "Подруливающее 100 %: изменение курса", XLabel = "Время, с", YLabel = "Изменение курса, °" };
        SvgPlot.Dash[] td = { SvgPlot.Dash.Solid, SvgPlot.Dash.Dashed, SvgPlot.Dash.Dotted, SvgPlot.Dash.DashDot };
        for (int k = 0; k < r.Thrusters.Count; k++)
        {
            BerthingTrials.ThrusterRun t = r.Thrusters[k];
            th.AppendLine($"{N(t.SpeedKn, 1)};{N(t.Factor, 3)};{N(t.Force / 1e3, 2)};{N(t.DeltaPsi, 2)};{N(t.R, 3)};{N(t.V, 3)}");
            var pts = new List<Vector2> { Vector2.zero };
            foreach (var s in t.Series) pts.Add(new Vector2((float)s.t, (float)s.psiDeg));
            tp.Add($"{ScenarioText.Num(t.SpeedKn)} уз", td[k % td.Length], pts);
        }
        Save(dir, "thruster.csv", th);
        WritePlot(dir, "thruster.svg", tp);
    }

    private static IEnumerable<BerthingTrials.ImpactRun> Impacts(BerthingTrials.Report r)
    {
        foreach (var i in r.Impacts) yield return i;
        foreach (var i in r.Angled) yield return i;
    }

    private static void Summary(StringBuilder md, BerthingTrials.Report r)
    {
        ShipParameters p = r.Parameters;
        BerthArea b = r.Berth;
        FenderSpec f = r.Fender;
        md.AppendLine($"## {p.name.Replace("ShipParameters_", "")}\n");
        md.AppendLine($"L {R(p.length)} м, B {R(p.beam, 2)} м, d {R(p.draft)} м, Δ {R(r.Mass / 1e6, 2)} тыс. т, m₂₂ {R(r.M22 / 1e6, 2)} тыс. т " +
                      $"(m₂₂/m = {R(r.M22 / r.Mass, 2)}; PIANC C_M = 1 + 2d/B = {R(1 + 2 * p.draft / p.beam, 2)}).");
        md.AppendLine($"Кранцы: D_н {R(f.OuterDiameter, 2)} м, δ_н {R(f.RatedDeflection, 3)} м, R_н {R(f.RatedReaction / 1e3, 0)} кН, E_н {R(f.RatedEnergy / 1e3, 1)} кДж, " +
                      $"шаг {R(b.fenderSpacingM, 0)} м; кнехты через {R(b.bollardSpacingM, 0)} м. Концы: MBL {R(r.Mbl / 1e3, 0)} кН, L_max {R(MooringLine.MaxLengthFor(p), 0)} м. " +
                      $"Подруливающее: {R(r.Thrust / 1e3, 1)} кН.\n");

        md.AppendLine("**Навал** (лагом, затем носом под углом, 0,2 уз)\n");
        md.AppendLine("| Подход, уз | Угол, ° | W модели, кДж | ½·m₂₂·v², кДж | PIANC, кДж | W/(½m₂₂v²) | δ/δ_н | ΣR, кН | e | Кранцев | Удар о кромку |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (BerthingTrials.ImpactRun i in Impacts(r))
            md.AppendLine($"| {R(i.ApproachKn, 2)} | {R(i.AngleDeg, 0)} | {R(i.Energy / 1e3, 2)} | {R(i.KineticM22 / 1e3, 2)} | {R(i.Pianc / 1e3, 2)} | {R(i.EnergyShare, 2)} | " +
                          $"{R(i.MaxRatio, 2)} | {R(i.MaxForce / 1e3, 0)} | {R(i.Restitution, 2)} | {i.Fenders} | {(i.QuayImpact ? "да" : "нет")} |");
        md.AppendLine();
        md.AppendLine("**Удержание на шести концах** (сила растёт на MBL/200 в секунду)\n");
        md.AppendLine("| Сила | Перегрузка (0,5·MBL), кН | Какой конец | Предел, кН | Что | Какой конец |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (BerthingTrials.HoldRun h in r.Holds)
            md.AppendLine($"| {h.Title} | {R(h.ForceOverload / 1e3, 0)} | {h.LineOverload} | {R(h.ForceLimit / 1e3, 0)} | {h.LimitKind} | {h.LineLimit} |");
        md.AppendLine();
        md.AppendLine("**Подруливающее 100 %**\n");
        md.AppendLine("| Ход, уз | f(u) | Сила, кН | Δψ за 60 с, ° | r через 60 с, °/с |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (BerthingTrials.ThrusterRun t in r.Thrusters)
            md.AppendLine($"| {R(t.SpeedKn, 0)} | {R(t.Factor, 2)} | {R(t.Force / 1e3, 1)} | {R(t.DeltaPsi, 1)} | {R(t.R, 2)} |");
        md.AppendLine();
    }

    private static void Save(string dir, string file, StringBuilder b) => File.WriteAllText(Path.Combine(dir, file), b.ToString(), Utf8Bom);

    private static void WritePlot(string dir, string file, SvgPlot plot)
    {
        if (plot.Count == 0) return;
        File.WriteAllText(Path.Combine(dir, file), plot.Render(), new UTF8Encoding(false));
    }
}
