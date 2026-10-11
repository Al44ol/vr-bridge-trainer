// ============================================================
// TrafficReport.cs — Данные B3 для ВКР без Play Mode
// Меню: Tools/VR Bridge/Traffic Report → ThesisData/Traffic/<дата>/:
//   lights_sectors.csv / .svg — доля силы света огней по курсовому углу наблюдателя
//       (правило 21, границы — Приложение I, п. 9) для судна L ≥ 50 м;
//   luminous_range.csv / .svg — дальность видимости огней по закону Алларда
//       в зависимости от метеорологической видимости V (Кошмидер);
//   autopilot_step.csv / .svg — смена курса на 30° авторулевым целей
//       (Model45m, Mariner, KCS: коэффициенты — размещением полюсов);
//   meeting_check.csv — «точка встречи»: заданные CPA/TCPA и полученные прогоном моделей;
//   demo_b3_tracks.svg, demo_b3.csv — сценарий B3-01 без манёвра своего судна;
//   summary.md — сводка для вставки в ВКР.
// Формат CSV — по CLAUDE.md (;, точка, UTF-8 с BOM, единицы в заголовках),
// графики — SvgPlot (стиль раздела 2).
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class TrafficReport
{
    private const double Dt = 0.02;
    private const double Nm = 1852.0;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);

    [MenuItem("Tools/VR Bridge/Traffic Report")]
    private static void Run()
    {
        ShipParameters model45 = AssetDatabase.LoadAssetAtPath<ShipParameters>("Assets/Ocean/Settings/ShipParameters_Model45m.asset");
        ShipParameters mariner = AssetDatabase.LoadAssetAtPath<ShipParameters>("Assets/Ocean/Settings/ShipParameters_Mariner.asset");
        ShipParameters kcs = AssetDatabase.LoadAssetAtPath<ShipParameters>("Assets/Ocean/Settings/ShipParameters_KCS.asset");
        WaterArea area = AssetDatabase.LoadAssetAtPath<WaterArea>("Assets/Ocean/Settings/WaterArea_Demo.asset");
        if (model45 == null || mariner == null || kcs == null || area == null)
        {
            Debug.LogError("[Отчёт B3] Нужны ShipParameters_Model45m/Mariner/KCS и WaterArea_Demo (Assets/Ocean/Settings).");
            return;
        }
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ThesisData", "Traffic", DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv)));
        Directory.CreateDirectory(dir);
        var md = new StringBuilder("# B3: другие суда и навигационные огни — данные стенда\n\n");

        Sectors(dir, md);
        LuminousRange(dir, md);
        Autopilot(dir, md, model45, mariner, kcs);
        Meeting(dir, md, model45, mariner, kcs);
        DemoB3(dir, md, area, model45, mariner);

        File.WriteAllText(Path.Combine(dir, "summary.md"), md.ToString(), Utf8Bom);
        Debug.Log($"[Отчёт B3] Сохранено: {dir}");
        EditorUtility.RevealInFinder(Path.Combine(dir, "summary.md"));
    }

    // ─────────── Секторы огней ───────────
    private static void Sectors(string dir, StringBuilder md)
    {
        List<NavLight> lights = NavigationLights.PowerDrivenUnderway(HullProfile.For(TrafficVesselType.Cargo, 160.93, 23.16, 8));
        var csv = new StringBuilder("q_deg;masthead_fwd;masthead_aft;sidelight_stbd;sidelight_port;sternlight\n");
        var series = new List<Vector2>[4];
        for (int k = 0; k < 4; k++) series[k] = new List<Vector2>();
        for (double q = 0; q <= 360.0 + 1e-9; q += 0.5)
        {
            csv.Append(F(q, 1));
            foreach (NavLight l in lights) csv.Append(';').Append(F(NavigationLights.SectorFactor(l, q), 3));
            csv.Append('\n');
            series[0].Add(new Vector2((float)q, (float)NavigationLights.SectorFactor(lights[0], q)));
            series[1].Add(new Vector2((float)q, (float)NavigationLights.SectorFactor(lights[2], q)));
            series[2].Add(new Vector2((float)q, (float)NavigationLights.SectorFactor(lights[3], q)));
            series[3].Add(new Vector2((float)q, (float)NavigationLights.SectorFactor(lights[4], q)));
        }
        File.WriteAllText(Path.Combine(dir, "lights_sectors.csv"), csv.ToString(), Utf8Bom);
        var plot = new SvgPlot { Title = "Видимость огней по курсовому углу наблюдателя", XLabel = "Курсовой угол наблюдателя q, ° (от носа по часовой)", YLabel = "Доля силы света" };
        plot.Add("топовые", SvgPlot.Dash.Solid, series[0]);
        plot.Add("бортовой зелёный (ПБ)", SvgPlot.Dash.Dashed, series[1]);
        plot.Add("бортовой красный (ЛБ)", SvgPlot.Dash.DashDot, series[2]);
        plot.Add("кормовой", SvgPlot.Dash.Dotted, series[3]);
        File.WriteAllText(Path.Combine(dir, "lights_sectors.svg"), plot.Render(), new UTF8Encoding(false));

        md.AppendLine("## Огни судна L ≥ 50 м (Mariner, Приложение I)\n");
        md.AppendLine("| Огонь | Сектор | Дальность, мили | Сила света, кд | К носу от миделя, м | Над ватерлинией, м |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (NavLight l in lights)
            md.AppendLine($"| {NavigationLights.Title(l.Kind)} | {N(l.CenterDeg - l.HalfWidthDeg, 1)}…{N(l.CenterDeg + l.HalfWidthDeg, 1)}° | {N(l.NominalRangeNm, 0)} | {N(l.IntensityCd, 1)} | {N(l.Forward, 1)} | {N(l.Height, 1)} |");
        md.AppendLine();
    }

    // ─────────── Дальность огней в зависимости от видимости ───────────
    private static void LuminousRange(string dir, StringBuilder md)
    {
        double[] ranges = { 6, 5, 3, 2 };
        var csv = new StringBuilder("visibility_nm;transmissivity_k;range_6nm_light_nm;range_5nm_light_nm;range_3nm_light_nm;range_2nm_light_nm\n");
        var series = new List<Vector2>[ranges.Length];
        for (int k = 0; k < ranges.Length; k++) series[k] = new List<Vector2>();
        for (double v = 0.5; v <= 20.0 + 1e-9; v += 0.25)
        {
            double K = NavigationLights.TransmissivityFromVisibility(v * Nm);
            csv.Append(F(v, 2)).Append(';').Append(F(K, 4));
            for (int k = 0; k < ranges.Length; k++)
            {
                double r = NavigationLights.LuminousRangeNm(NavigationLights.IntensityForRange(ranges[k]), K);
                csv.Append(';').Append(F(r, 3));
                series[k].Add(new Vector2((float)v, (float)r));
            }
            csv.Append('\n');
        }
        File.WriteAllText(Path.Combine(dir, "luminous_range.csv"), csv.ToString(), Utf8Bom);
        var plot = new SvgPlot { Title = "Дальность видимости огня ночью (закон Алларда)", XLabel = "Метеорологическая видимость V, мили", YLabel = "Дальность видимости огня, мили", LegendBottomRight = true };
        SvgPlot.Dash[] dash = { SvgPlot.Dash.Solid, SvgPlot.Dash.Dashed, SvgPlot.Dash.DashDot, SvgPlot.Dash.Dotted };
        for (int k = 0; k < ranges.Length; k++) plot.Add($"огонь {N(ranges[k], 0)} миль (пр. 22)", dash[k], series[k]);
        File.WriteAllText(Path.Combine(dir, "luminous_range.svg"), plot.Render(), new UTF8Encoding(false));

        md.AppendLine("## Дальность видимости огней в зависимости от видимости V\n");
        md.AppendLine("| V, мили | K | огонь 6 миль | огонь 5 миль | огонь 3 мили | огонь 2 мили |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (double v in new[] { 1.0, 2.33, 4.0, 6.23, 10.0, 13.4, 20.0 })
        {
            double K = NavigationLights.TransmissivityFromVisibility(v * Nm);
            md.Append($"| {N(v, 2)} | {N(K, 3)}");
            foreach (double r in ranges) md.Append($" | {N(NavigationLights.LuminousRangeNm(NavigationLights.IntensityForRange(r), K), 2)}");
            md.AppendLine(" |");
        }
        md.AppendLine("\nV = 2,33 мили — пресет «умеренное» (ρ = 0,0004), 6,23 мили — «ночь» после B3 (ρ = 0,00015), 13,4 мили — K = 0,8 (номинал).\n");
    }

    // ─────────── Авторулевой ───────────
    private static void Autopilot(string dir, StringBuilder md, ShipParameters model45, ShipParameters mariner, ShipParameters kcs)
    {
        var ships = new (string name, ShipParameters p, double kn, SvgPlot.Dash dash)[]
        {
            ("Model45m, 15 уз", model45, 15, SvgPlot.Dash.Solid),
            ("Mariner, 15 уз", mariner, 15, SvgPlot.Dash.Dashed),
            ("KCS, 24 уз", kcs, 24, SvgPlot.Dash.DashDot)
        };
        var csv = new StringBuilder("ship;t_s;heading_deg;rudder_deg\n");
        var plot = new SvgPlot { Title = "Смена курса на 30° авторулевым целей", XLabel = "Время t, с", YLabel = "Курс ψ, °", LegendBottomRight = true };
        md.AppendLine("## Авторулевой целей: смена курса на 30°\n");
        md.AppendLine("| Судно | K_p | K_d, с | Перерегулирование, ° | Установление ±1°, с | в L/U | max \\|δ\\|, ° |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var s in ships)
        {
            var def = new TrafficShipDefinition { headingDeg = 0f, speedKnots = (float)s.kn, parameters = s.p };
            var v = new TrafficVessel(0, "T", def, s.p, TrafficPlanner.Resolve(def, null)) { OrderedCourseDeg = 30.0 };
            var pts = new List<Vector2>();
            double over = 0, settle = 0, maxRudder = 0, t = 0;
            for (int i = 0; i < 30000; i++)
            {
                v.Step(Dt);
                t += Dt;
                double h = Math.IEEERemainder(v.Model.Psi * 180.0 / Math.PI, 360.0);
                over = Math.Max(over, h - 30.0);
                maxRudder = Math.Max(maxRudder, Math.Abs(v.Model.Delta));
                if (Math.Abs(h - 30.0) > 1.0) settle = t;
                if (i % 25 == 0)
                {
                    csv.Append(s.name).Append(';').Append(F(t, 2)).Append(';').Append(F(h, 3)).Append(';').Append(F(v.Model.Delta, 3)).Append('\n');
                    if (t <= 400) pts.Add(new Vector2((float)t, (float)h));
                }
            }
            plot.Add(s.name, s.dash, pts);
            double lu = s.p.length / (s.kn * ShipParameters.KnotsToMs);
            md.AppendLine($"| {s.name} | {N(v.Autopilot.Kp, 3)} | {N(v.Autopilot.Kd, 2)} | {N(over, 2)} | {N(settle, 0)} | {N(settle / lu, 1)} | {N(maxRudder, 1)} |");
        }
        md.AppendLine();
        File.WriteAllText(Path.Combine(dir, "autopilot_step.csv"), csv.ToString(), Utf8Bom);
        File.WriteAllText(Path.Combine(dir, "autopilot_step.svg"), plot.Render(), new UTF8Encoding(false));
    }

    // ─────────── «Точка встречи» на моделях ───────────
    private static void Meeting(string dir, StringBuilder md, ShipParameters model45, ShipParameters mariner, ShipParameters kcs)
    {
        var cases = new (string ship, ShipParameters p, double course, double kn, double cpa, double tcpa, MeetingSide side)[]
        {
            ("Mariner", mariner, 270, 12, 0.5, 12, MeetingSide.Ahead),
            ("Mariner", mariner, 180, 12, 0.3, 10, MeetingSide.Port),
            ("KCS", kcs, 300, 20, 1.0, 15, MeetingSide.Astern),
            ("Model45m", model45, 90, 10, 0.0, 8, MeetingSide.Starboard),
            ("Model45m", model45, 0, 6, 0.2, 10, MeetingSide.Starboard)
        };
        var csv = new StringBuilder("target;course_deg;speed_kn;side;cpa_set_nm;tcpa_set_min;cpa_model_m;cpa_set_m;tcpa_model_s;tcpa_set_s\n");
        md.AppendLine("## «Точка встречи»: заданные и полученные CPA/TCPA (своё судно Model45m, 000°, 15 уз)\n");
        md.AppendLine("| Цель | Курс, ° | Скорость, уз | Сторона | CPA задано, м | CPA модели, м | TCPA задано, с | TCPA модели, с |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var c in cases)
        {
            var init = new OwnShipInitialState { position = new WorldPosition(0, -2500), headingDeg = 0f, speedKnots = 15f, telegraph = TelegraphOrder.FullAhead };
            var def = new TrafficShipDefinition
            {
                placement = TrafficPlacement.MeetingPoint, cpaNm = (float)c.cpa, tcpaMinutes = (float)c.tcpa, meetingSide = c.side,
                headingDeg = (float)c.course, speedKnots = (float)c.kn, parameters = c.p
            };
            var target = new TrafficVessel(0, "T", def, c.p, TrafficPlanner.Resolve(def, init));
            var own = new ShipDynamicsModel(model45) { East = 0, North = -2500, Psi = 0, U = 15 * ShipParameters.KnotsToMs, SpeedFactor = 1 };
            double minD = double.MaxValue, tMin = 0, t = 0;
            while (t < 2 * c.tcpa * 60)
            {
                own.Step(Dt); target.Step(Dt); t += Dt;
                double d = (target.Position - new WorldPosition(own.East, own.North)).Magnitude;
                if (d < minD) { minD = d; tMin = t; }
            }
            csv.Append($"{c.ship};{F(c.course, 0)};{F(c.kn, 1)};{c.side};{F(c.cpa, 2)};{F(c.tcpa, 1)};{F(minD, 2)};{F(c.cpa * Nm, 2)};{F(tMin, 2)};{F(c.tcpa * 60, 1)}\n");
            md.AppendLine($"| {c.ship} | {N(c.course, 0)} | {N(c.kn, 0)} | {ScenarioText.Side(c.side)} | {N(c.cpa * Nm, 1)} | {N(minD, 1)} | {N(c.tcpa * 60, 0)} | {N(tMin, 1)} |");
        }
        md.AppendLine();
        File.WriteAllText(Path.Combine(dir, "meeting_check.csv"), csv.ToString(), Utf8Bom);
    }

    // ─────────── B3-01 без манёвра своего судна ───────────
    private static void DemoB3(string dir, StringBuilder md, WaterArea area, ShipParameters own, ShipParameters mariner)
    {
        var s = ScriptableObject.CreateInstance<ScenarioDefinition>();
        DemoTrafficScenario.Fill(s, area, own, mariner, mariner, own);
        var m = new ShipDynamicsModel(own)
        {
            East = s.initial.position.East, North = s.initial.position.North, Psi = 0,
            U = s.initial.speedKnots * ShipParameters.KnotsToMs, SpeedFactor = 1
        };
        var routePts = new List<WorldPosition>();
        for (int i = 1; i < s.route.Count; i++) routePts.Add(s.route[i].position);
        var follower = new RouteFollower(s.initial.position, routePts);
        var ap = new HeadingAutopilot();
        var vessels = new List<TrafficVessel>();
        var contacts = new List<HullContact>();
        for (int i = 0; i < s.traffic.Count; i++)
        {
            vessels.Add(new TrafficVessel(i, s.traffic[i].TargetId(i), s.traffic[i], s.traffic[i].parameters, TrafficPlanner.Resolve(s.traffic[i], s.initial)));
            contacts.Add(new HullContact());
        }
        var tracks = new List<Vector2>[vessels.Count + 1];
        for (int i = 0; i < tracks.Length; i++) tracks[i] = new List<Vector2>();
        var minD = new double[vessels.Count];
        var minT = new double[vessels.Count];
        var firstContact = new double[vessels.Count];
        for (int i = 0; i < vessels.Count; i++) { minD[i] = double.MaxValue; firstContact[i] = double.NaN; }
        var csv = new StringBuilder("t_s;own_x_e_m;own_y_n_m");
        foreach (TrafficVessel v in vessels) csv.Append($";{v.TargetId}_x_e_m;{v.TargetId}_y_n_m;{v.TargetId}_range_m");
        csv.Append('\n');

        double t = 0;
        for (int step = 1; t < 1500.0; step++)
        {
            m.RudderCommandDeg = ap.Rudder(m, follower.Course(m));
            m.Step(Dt);
            foreach (TrafficVessel v in vessels) v.Step(Dt);
            t += Dt;
            HullFootprint hull = HullFootprint.FromShip(m.East, m.North, m.Psi, 0, 0, own.length, own.beam);
            for (int i = 0; i < vessels.Count; i++)
            {
                double d = (vessels[i].Position - new WorldPosition(m.East, m.North)).Magnitude;
                if (d < minD[i]) { minD[i] = d; minT[i] = t; }
                if (contacts[i].Update(hull, vessels[i].Footprint) && double.IsNaN(firstContact[i])) firstContact[i] = t;
            }
            if (step % 50 == 0)
            {
                tracks[0].Add(new Vector2((float)m.East, (float)m.North));
                csv.Append(F(t, 1)).Append(';').Append(F(m.East, 1)).Append(';').Append(F(m.North, 1));
                for (int i = 0; i < vessels.Count; i++)
                {
                    tracks[i + 1].Add(new Vector2((float)vessels[i].Model.East, (float)vessels[i].Model.North));
                    csv.Append(';').Append(F(vessels[i].Model.East, 1)).Append(';').Append(F(vessels[i].Model.North, 1))
                       .Append(';').Append(F((vessels[i].Position - new WorldPosition(m.East, m.North)).Magnitude, 1));
                }
                csv.Append('\n');
            }
        }
        File.WriteAllText(Path.Combine(dir, "demo_b3.csv"), csv.ToString(), Utf8Bom);

        var plot = new SvgPlot { Title = "B3-01 без манёвра своего судна (1500 с)", XLabel = "Восток, м", YLabel = "Север, м", EqualAspect = true };
        plot.Add("своё судно (авторулевой по маршруту)", SvgPlot.Dash.Solid, tracks[0]);
        SvgPlot.Dash[] dash = { SvgPlot.Dash.Dashed, SvgPlot.Dash.DashDot, SvgPlot.Dash.Dotted };
        for (int i = 0; i < vessels.Count; i++) plot.Add($"{vessels[i].TargetId} {vessels[i].Name}", dash[i % dash.Length], tracks[i + 1]);
        File.WriteAllText(Path.Combine(dir, "demo_b3_tracks.svg"), plot.Render(), new UTF8Encoding(false));

        md.AppendLine("## Сценарий B3-01 без манёвра своего судна\n");
        md.AppendLine("| Цель | Наименьшая дистанция между центрами, м | Время, с | Касание корпусов, с |");
        md.AppendLine("|---|---|---|---|");
        for (int i = 0; i < vessels.Count; i++)
            md.AppendLine($"| {vessels[i].TargetId} {vessels[i].Name} | {N(minD[i], 1)} | {N(minT[i], 1)} | {(double.IsNaN(firstContact[i]) ? "нет" : N(firstContact[i], 1))} |");
        md.AppendLine();
        UnityEngine.Object.DestroyImmediate(s);
    }

    private static string F(double v, int digits) => double.IsNaN(v) ? "" : v.ToString("F" + digits, Inv);
    private static string N(double v, int digits) => ScenarioText.Num(v, digits);
}
