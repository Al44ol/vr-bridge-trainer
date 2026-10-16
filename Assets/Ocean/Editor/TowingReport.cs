// ============================================================
// TowingReport.cs — Стенд буксировки и ветра для ВКР без Play Mode (запрос 19.3а)
// Меню: Tools/VR Bridge/Towing Trials → ThesisData/Towing/<дата>/ (своё судно — Model45m, буксир — ShipPresets.FillTug):
//   bollard.csv            — тяга на гаке 50/100 %: установившееся и наибольшее натяжение троса;
//   lateral.csv            — буксир тянет (от кормы / носа) и толкает (у миделя / кормы) на ЛБ, 50/100 %:
//                            поперечная и продольная скорость судна, угловая скорость, изменение курса, сила;
//   lateral_series.csv     — те же прогоны по времени (раз в секунду);
//   tug_lateral.svg        — поперечная скорость судна по времени (50 %), tug_heading.svg — изменение курса;
//   wind_coefficients.csv/.svg — коэффициенты Isherwood C_X, C_Y, C_N судна по углу ветра 0…180°;
//   wind_drift.csv/.svg    — дрейф судна без хода при ветре с ПБ 10/15/20/25 уз;
//   wind_hold.csv/.svg     — ветер 20 уз с ЛБ, буксир толкает у миделя на ЛБ 0/10/25/50 %;
//   summary.md             — сводка для ВКР.
// Формат CSV — по CLAUDE.md, графики — SvgPlot (стиль раздела 2 Handoff).
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class TowingReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);

    [MenuItem("Tools/VR Bridge/Towing Trials")]
    private static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[Буксировка] Выйдите из Play Mode.");
            return;
        }
        ShipParameters ship = AssetDatabase.LoadAssetAtPath<ShipParameters>("Assets/Ocean/Settings/ShipParameters_Model45m.asset");
        if (ship == null)
        {
            Debug.LogError("[Буксировка] Нет ассета ShipParameters_Model45m (Assets/Ocean/Settings).");
            return;
        }
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ThesisData", "Towing", DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv)));
        Directory.CreateDirectory(dir);
        TowingTrials.Report r;
        try
        {
            EditorUtility.DisplayProgressBar("Стенд буксировки", "прогоны (≈ 10 с)", 0.3f);
            r = TowingTrials.RunAll(ship);
        }
        finally { EditorUtility.ClearProgressBar(); }
        Write(dir, r);
        Debug.Log($"[Буксировка] Сохранено: {dir}");
        EditorUtility.RevealInFinder(Path.Combine(dir, "summary.md"));
    }

    private static string N(double v, int d = 2) => double.IsNaN(v) ? "" : v.ToString("F" + d, Inv);
    private static string R(double v, int d = 1) => double.IsNaN(v) ? "—" : ScenarioText.Num(v, d);

    private static void Save(string dir, string name, StringBuilder b) => File.WriteAllText(Path.Combine(dir, name), b.ToString(), Utf8Bom);
    private static void SaveSvg(string dir, string name, SvgPlot p) => File.WriteAllText(Path.Combine(dir, name), p.Render(), new UTF8Encoding(false));

    public static void Write(string dir, TowingTrials.Report r)
    {
        var csv = new StringBuilder("power;tension_mean_kn;tension_max_kn;bollard_pull_kn\n");
        foreach (TowingTrials.Run x in r.Bollard) csv.AppendLine($"{N(x.Power)};{N(x.ForceKn, 1)};{N(x.MaxForceKn, 1)};{N(r.BollardPullKn * x.Power, 1)}");
        Save(dir, "bollard.csv", csv);

        csv = new StringBuilder("key;title;mode;point_x_l;power;sway_kn;surge_kn;rate_deg_s;delta_psi_deg;force_mean_kn;force_max_kn\n");
        var series = new StringBuilder("key;t_s;sway_kn;surge_kn;rate_deg_s;delta_psi_deg;tension_kn;push_kn\n");
        foreach (TowingTrials.Run x in r.Lateral)
        {
            csv.AppendLine($"{x.Key};{x.Title};{x.Mode};{N(x.PointX)};{N(x.Power)};{N(x.SwayKn, 3)};{N(x.SurgeKn, 3)};{N(x.RateDegS, 3)};{N(x.DeltaPsiDeg, 1)};{N(x.ForceKn, 1)};{N(x.MaxForceKn, 1)}");
            foreach (TowingTrials.Row s in x.Series)
                series.AppendLine($"{x.Key};{N(s.T, 1)};{N(s.SwayKn, 3)};{N(s.SurgeKn, 3)};{N(s.RateDegS, 3)};{N(s.DeltaPsiDeg, 2)};{N(s.TensionKn, 1)};{N(s.PushKn, 1)}");
        }
        Save(dir, "lateral.csv", csv);
        Save(dir, "lateral_series.csv", series);

        SvgPlot.Dash[] dashes = { SvgPlot.Dash.Solid, SvgPlot.Dash.Dashed, SvgPlot.Dash.DashDot, SvgPlot.Dash.Dotted };
        var pv = new SvgPlot { Title = "Буксир 50 %: поперечная скорость судна", XLabel = "Время, с", YLabel = "Скорость лагом, уз (< 0 — на ЛБ)" };
        var ph = new SvgPlot { Title = "Буксир 50 %: изменение курса судна", XLabel = "Время, с", YLabel = "Δψ, °" };
        int i = 0;
        foreach (TowingTrials.Run x in r.Lateral)
        {
            if (Math.Abs(x.Power - 0.5) > 1e-6) continue;
            pv.Add(Short(x), dashes[i % dashes.Length], Pts(x.Series, s => s.SwayKn));
            ph.Add(Short(x), dashes[i % dashes.Length], Pts(x.Series, s => s.DeltaPsiDeg));
            i++;
        }
        SaveSvg(dir, "tug_lateral.svg", pv);
        SaveSvg(dir, "tug_heading.svg", ph);

        csv = new StringBuilder("gamma_deg;cx;cy;cn\n");
        foreach (var c in r.Coefficients) csv.AppendLine($"{N(c.gamma, 0)};{N(c.cx, 4)};{N(c.cy, 4)};{N(c.cn, 4)}");
        Save(dir, "wind_coefficients.csv", csv);
        var pc = new SvgPlot { Title = "Коэффициенты ветровой нагрузки (Isherwood, 1972)", XLabel = "Курсовой угол ветра γ, °", YLabel = "Коэффициент" };
        pc.Add("C_X", SvgPlot.Dash.Solid, Coef(r, 1));
        pc.Add("C_Y", SvgPlot.Dash.Dashed, Coef(r, 2));
        pc.Add("C_N", SvgPlot.Dash.Dotted, Coef(r, 3));
        SaveSvg(dir, "wind_coefficients.svg", pc);

        csv = new StringBuilder("wind_kn;force_y_kn;drift_kn;rate_deg_s;delta_psi_deg\n");
        var pw = new SvgPlot { Title = "Дрейф без хода при ветре с ПБ", XLabel = "Время, с", YLabel = "Скорость лагом, уз (< 0 — на ЛБ)" };
        i = 0;
        foreach (TowingTrials.Run x in r.Wind)
        {
            csv.AppendLine($"{N(x.WindKn, 0)};{N(x.ForceKn, 2)};{N(x.SwayKn, 3)};{N(x.RateDegS, 4)};{N(x.DeltaPsiDeg, 1)}");
            pw.Add($"{ScenarioText.Num(x.WindKn)} уз", dashes[i++ % dashes.Length], Pts(x.Series, s => s.SwayKn));
        }
        Save(dir, "wind_drift.csv", csv);
        SaveSvg(dir, "wind_drift.svg", pw);

        csv = new StringBuilder("wind_kn;power;sway_kn;rate_deg_s;delta_psi_deg;push_mean_kn\n");
        var pk = new SvgPlot { Title = "Ветер 20 уз с ЛБ, буксир толкает у миделя на ЛБ", XLabel = "Время, с", YLabel = "Скорость лагом, уз (> 0 — на ПБ, по ветру)" };
        i = 0;
        foreach (TowingTrials.Run x in r.Hold)
        {
            csv.AppendLine($"{N(x.WindKn, 0)};{N(x.Power)};{N(x.SwayKn, 3)};{N(x.RateDegS, 4)};{N(x.DeltaPsiDeg, 1)};{N(x.ForceKn, 1)}");
            pk.Add($"{ScenarioText.Num(x.Power * 100)} %", dashes[i++ % dashes.Length], Pts(x.Series, s => s.SwayKn));
        }
        Save(dir, "wind_hold.csv", csv);
        SaveSvg(dir, "wind_hold.svg", pk);

        Save(dir, "summary.md", Summary(r));
    }

    private static string Short(TowingTrials.Run x) =>
        x.Mode == TugMode.Pull ? (x.PointX < 0 ? "тянет от кормы" : "тянет от носа") : (Math.Abs(x.PointX) < 1e-6 ? "толкает у миделя" : "толкает у кормы");

    private static List<Vector2> Pts(List<TowingTrials.Row> rows, Func<TowingTrials.Row, double> y)
    {
        var list = new List<Vector2>();
        foreach (TowingTrials.Row s in rows) list.Add(new Vector2((float)s.T, (float)y(s)));
        return list;
    }

    private static List<Vector2> Coef(TowingTrials.Report r, int k)
    {
        var list = new List<Vector2>();
        foreach (var c in r.Coefficients) list.Add(new Vector2((float)c.gamma, (float)(k == 1 ? c.cx : k == 2 ? c.cy : c.cn)));
        return list;
    }

    private static StringBuilder Summary(TowingTrials.Report r)
    {
        var md = new StringBuilder("# Запрос 19.3а: буксир, буксирный трос, ветер — стенд\n\n");
        md.AppendLine($"Судно — {r.Ship.name} (модель 3 СС, машина «Стоп»), буксир — {r.TugParams.name}: L {R(r.TugParams.length, 0)} м, B {R(r.TugParams.beam, 0)} м, d {R(r.TugParams.draft, 1)} м, " +
                      $"тяга на гаке {R(r.BollardPullKn, 0)} кН, две азимутальные колонки. Трос {R(TowingTrials.LineLength, 0)} м, MBL {R(TowLine.MblFactor * r.BollardPullKn, 0)} кН. dt = {TowingTrials.Dt} с. " +
                      "Формулы и допущения — ThesisNotes, разд. 19.24.\n");
        md.AppendLine($"Надводная часть (оценка `ShipWindArea.Estimate`): судно A_L {R(r.ShipWind.LateralM2, 0)} м², A_F {R(r.ShipWind.FrontalM2, 0)} м²; буксир A_L {R(r.TugWind.LateralM2, 0)} м², A_F {R(r.TugWind.FrontalM2, 0)} м².\n");
        md.AppendLine($"Свободный ход буксира на полной тяге: {R(r.TugFreeSpeedKn, 2)} уз.\n");
        md.AppendLine("## Тяга на гаке\n\n| Тяга | Натяжение (последние 20 с), кН | Наибольшее, кН |\n|---|---|---|");
        foreach (TowingTrials.Run x in r.Bollard) md.AppendLine($"| {R(x.Power * 100, 0)} % ({R(r.BollardPullKn * x.Power, 0)} кН) | {R(x.ForceKn)} | {R(x.MaxForceKn)} |");
        md.AppendLine("\n## Буксир тянет и толкает судно на ЛБ (180 с)\n\n| Прогон | Лагом, уз | Ход, уз | Угл. скорость, °/с | Δψ, ° | Сила (среднее / max), кН |\n|---|---|---|---|---|---|");
        foreach (TowingTrials.Run x in r.Lateral) md.AppendLine($"| {x.Title} | {R(x.SwayKn, 2)} | {R(x.SurgeKn, 2)} | {R(x.RateDegS, 2)} | {R(x.DeltaPsiDeg)} | {R(x.ForceKn)} / {R(x.MaxForceKn)} |");
        md.AppendLine("\n## Дрейф под ветром с ПБ (300 с, без хода)\n\n| Ветер, уз | Сила Y, кН | Дрейф, уз | Δψ, ° |\n|---|---|---|---|");
        foreach (TowingTrials.Run x in r.Wind) md.AppendLine($"| {R(x.WindKn, 0)} | {R(x.ForceKn, 1)} | {R(x.SwayKn, 2)} | {R(x.DeltaPsiDeg)} |");
        md.AppendLine("\n## Ветер 20 уз с ЛБ, буксир толкает у миделя на ЛБ (180 с)\n\n| Тяга | Лагом, уз (> 0 — по ветру) | Δψ, ° | Упор в борт, кН |\n|---|---|---|---|");
        foreach (TowingTrials.Run x in r.Hold) md.AppendLine($"| {R(x.Power * 100, 0)} % | {R(x.SwayKn, 2)} | {R(x.DeltaPsiDeg)} | {R(x.ForceKn)} |");
        return md;
    }
}
