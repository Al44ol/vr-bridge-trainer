// ============================================================
// DebriefSvg.cs — Схема траектории в SVG (C3)
//
// Стиль — как у графиков ВКР (SvgPlot): чёрно-белый, Times New Roman,
// светло-серая сетка с подписями в метрах (восток, север), одинаковый
// масштаб по осям, легенда справа вверху. Обозначения:
//   суша — серая заливка; мель — светло-серая заливка, штриховой контур, глубина;
//   ось фарватера — серый штрихпунктир; маршрут — тонкая сплошная с точками;
//   коридор — пунктир; своё судно — толстая сплошная; цели — штрих, штрихпунктир,
//   пунктир; отметки времени — точки с минутами; знаки МАМС по форме
//   (левой стороны — квадрат, правой — треугольник, кардинальные — ромб,
//   прочие — круг); манёвр — кружок; наименьшая дистанция — отрезок между
//   судами с подписью; авария — крест.
//   19.2б-3б: пирс — серая заливка, кромка — толстая линия, кранцы — чёрные
//   прямоугольники, кнехты — кружки (крупный масштаб); навал — ромб, удар о
//   кромку — залитый ромб; контуры судна (вид «У причала») — тонкие.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public static class DebriefSvg
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly SvgPlot.Dash[] TargetDash = { SvgPlot.Dash.Dashed, SvgPlot.Dash.DashDot, SvgPlot.Dash.Dotted };
    /// <summary>19.2б-3б: кнехты рисуются при масштабе не мельче, пикс./м (окно ≲ 1,2 км).</summary>
    public const double BollardMinScale = 0.5;

    /// <summary>SVG вида view. id — уникальный префикс (несколько схем на одной странице).</summary>
    public static string Map(DebriefMapData d, DebriefMapView view, string id = "map", float width = 680, float maxHeight = 720)
    {
        const float ml = 80, mr = 20, mt = 40, mb = 60;
        float pw = width - ml - mr;
        // Высота — по пропорциям окна (одинаковый масштаб), не выше maxHeight
        double aspect = view.Bounds.Width / Math.Max(1e-6, view.Bounds.Height);
        float ph = (float)Math.Min(pw / aspect, maxHeight - mt - mb);
        ph = Math.Max(ph, 260);
        float height = ph + mt + mb;
        MapRect r = view.Bounds.WithAspect(pw / ph);
        double scale = pw / r.Width;   // пикс./м
        float X(double e) => (float)(ml + (e - r.MinE) * scale);
        float Y(double n) => (float)(mt + ph - (n - r.MinN) * scale);

        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(width)}\" height=\"{N(height)}\" viewBox=\"0 0 {N(width)} {N(height)}\" " +
                      "font-family=\"Times New Roman, serif\" font-size=\"14\">");
        sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");

        // Сетка (м)
        float step = SvgPlot.NiceStep((float)Math.Max(r.Width, r.Height));
        for (double v = Math.Ceiling(r.MinE / step) * step; v <= r.MaxE; v += step)
        {
            sb.AppendLine($"<line x1=\"{N(X(v))}\" y1=\"{N(mt)}\" x2=\"{N(X(v))}\" y2=\"{N(mt + ph)}\" stroke=\"#c8c8c8\" stroke-width=\"0.5\"/>");
            sb.AppendLine($"<text x=\"{N(X(v))}\" y=\"{N(mt + ph + 18)}\" text-anchor=\"middle\">{SvgPlot.Tick((float)v, step)}</text>");
        }
        for (double v = Math.Ceiling(r.MinN / step) * step; v <= r.MaxN; v += step)
        {
            sb.AppendLine($"<line x1=\"{N(ml)}\" y1=\"{N(Y(v))}\" x2=\"{N(ml + pw)}\" y2=\"{N(Y(v))}\" stroke=\"#c8c8c8\" stroke-width=\"0.5\"/>");
            sb.AppendLine($"<text x=\"{N(ml - 6)}\" y=\"{N(Y(v) + 5)}\" text-anchor=\"end\">{SvgPlot.Tick((float)v, step)}</text>");
        }
        sb.AppendLine($"<text x=\"{N(ml + pw / 2)}\" y=\"{N(height - 14)}\" text-anchor=\"middle\">Восток, м</text>");
        sb.AppendLine($"<text x=\"18\" y=\"{N(mt + ph / 2)}\" text-anchor=\"middle\" transform=\"rotate(-90 18 {N(mt + ph / 2)})\">Север, м</text>");
        sb.AppendLine($"<text x=\"{N(ml + pw / 2)}\" y=\"24\" text-anchor=\"middle\" font-size=\"15\">{SvgPlot.Xml(view.Title)}</text>");
        sb.AppendLine($"<clipPath id=\"{id}\"><rect x=\"{N(ml)}\" y=\"{N(mt)}\" width=\"{N(pw)}\" height=\"{N(ph)}\"/></clipPath>");
        sb.AppendLine($"<g clip-path=\"url(#{id})\">");

        double eps = 1.2 / scale;   // прореживание: 1,2 пикселя
        string Pts(IList<WorldPosition> pts)
        {
            var b = new StringBuilder();
            foreach (WorldPosition p in MapGeometry.Decimate(pts, eps)) b.Append(N(X(p.East))).Append(',').Append(N(Y(p.North))).Append(' ');
            return b.ToString();
        }

        // Суша, мели
        foreach (MapPolygon p in d.Polygons)
        {
            if (p.Points.Count < 3 || (p.Layer != MapLayer.Land && p.Layer != MapLayer.Shoal)) continue;
            bool land = p.Layer == MapLayer.Land;
            sb.AppendLine($"<polygon points=\"{Pts(p.Points)}\" fill=\"{(land ? "#bdbdbd" : "#ebebeb")}\" stroke=\"black\" stroke-width=\"0.6\"{(land ? "" : " stroke-dasharray=\"3,2\"")}/>");
            if (!land && p.Label.Length > 0)
            {
                WorldPosition c = Polygon2D.Centroid(p.Points);
                if (r.Contains(c)) sb.AppendLine($"<text x=\"{N(X(c.East))}\" y=\"{N(Y(c.North) + 4)}\" text-anchor=\"middle\" font-size=\"11\">{SvgPlot.Xml(p.Label)}</text>");
            }
        }
        // Ось фарватера, коридор, маршрут
        foreach (MapLine l in d.Lines)
            if (l.Layer == MapLayer.Fairway)
                sb.AppendLine($"<polyline points=\"{Pts(l.Points)}\" fill=\"none\" stroke=\"#808080\" stroke-width=\"0.8\" stroke-dasharray=\"10,3,2,3\"/>");
        foreach (MapLine l in d.Lines)
            if (l.Layer == MapLayer.Corridor)
                sb.AppendLine($"<polyline points=\"{Pts(l.Points)}\" fill=\"none\" stroke=\"black\" stroke-width=\"0.7\" stroke-dasharray=\"2,3\"/>");
        foreach (MapLine l in d.Lines)
            if (l.Layer == MapLayer.Route)
                sb.AppendLine($"<polyline points=\"{Pts(l.Points)}\" fill=\"none\" stroke=\"black\" stroke-width=\"0.8\"/>");
        foreach (MapMarker m in d.Markers)
            if (m.Layer == MapLayer.Waypoint)
            {
                sb.AppendLine($"<circle cx=\"{N(X(m.Position.East))}\" cy=\"{N(Y(m.Position.North))}\" r=\"3.5\" fill=\"white\" stroke=\"black\" stroke-width=\"0.8\"/>");
                sb.AppendLine($"<text x=\"{N(X(m.Position.East) + 6)}\" y=\"{N(Y(m.Position.North) + 4)}\" font-size=\"11\">{SvgPlot.Xml(m.Label)}</text>");
            }
        // Знаки МАМС
        foreach (MapMarker m in d.Markers)
            if (m.Layer == MapLayer.Aid && r.Contains(m.Position))
                sb.AppendLine(AidShape(X(m.Position.East), Y(m.Position.North), m.Aid));

        // 19.2б-3б: причал — пирс (серая заливка), кромка (толстая), кранцы (чёрные), кнехты (при крупном масштабе);
        // контуры судна — только вид «У причала»
        foreach (MapPolygon p in d.Polygons)
            if (p.Layer == MapLayer.Pier && p.Points.Count >= 3)
                sb.AppendLine($"<polygon points=\"{Pts(p.Points)}\" fill=\"#d9d9d9\" stroke=\"black\" stroke-width=\"0.6\"/>");
        foreach (MapLine l in d.Lines)
            if (l.Layer == MapLayer.Quay)
                sb.AppendLine($"<polyline points=\"{Pts(l.Points)}\" fill=\"none\" stroke=\"black\" stroke-width=\"1.6\"/>");
        foreach (MapPolygon p in d.Polygons)
            if (p.Layer == MapLayer.Fender && p.Points.Count >= 3)
                sb.AppendLine($"<polygon points=\"{Pts(p.Points)}\" fill=\"black\" stroke=\"black\" stroke-width=\"0.4\"/>");
        if (scale >= BollardMinScale)
            foreach (MapMarker m in d.Markers)
                if (m.Layer == MapLayer.Bollard && r.Contains(m.Position))
                    sb.AppendLine($"<circle cx=\"{N(X(m.Position.East))}\" cy=\"{N(Y(m.Position.North))}\" r=\"2.2\" fill=\"white\" stroke=\"black\" stroke-width=\"0.8\"/>");
        if (view.ShowHulls)
            foreach (MapPolygon p in d.Polygons)
                if (p.Layer == MapLayer.HullOutline && p.Points.Count >= 3)
                    sb.AppendLine($"<polygon points=\"{Pts(p.Points)}\" fill=\"none\" stroke=\"black\" stroke-width=\"0.6\"/>");

        // Траектории
        foreach (MapLine l in d.Lines)
            if (l.Layer == MapLayer.TargetTrack)
                sb.AppendLine($"<polyline points=\"{Pts(l.Points)}\" fill=\"none\" stroke=\"black\" stroke-width=\"1.3\"{SvgPlot.DashAttr(TargetDash[Math.Max(0, l.Series) % TargetDash.Length])}/>");
        foreach (MapLine l in d.Lines)
            if (l.Layer == MapLayer.OwnTrack)
                sb.AppendLine($"<polyline points=\"{Pts(l.Points)}\" fill=\"none\" stroke=\"black\" stroke-width=\"2.2\"/>");
        // Отметки времени (минуты)
        foreach (MapMarker m in d.Markers)
        {
            if (m.Layer != MapLayer.TimeTick || !r.Contains(m.Position)) continue;
            if (Math.Abs(Math.IEEERemainder(m.Time, view.TickStepS)) > 1e-6) continue;
            sb.AppendLine($"<circle cx=\"{N(X(m.Position.East))}\" cy=\"{N(Y(m.Position.North))}\" r=\"2\" fill=\"black\"/>");
            sb.AppendLine($"<text x=\"{N(X(m.Position.East) - 5)}\" y=\"{N(Y(m.Position.North) + 4)}\" text-anchor=\"end\" font-size=\"10\">{SvgPlot.Xml(m.Label)}</text>");
        }
        // Наименьшая дистанция
        foreach (MapLine l in d.Lines)
        {
            if (l.Layer != MapLayer.MinLink || l.Points.Count < 2) continue;
            WorldPosition a = l.Points[0], b = l.Points[1];
            sb.AppendLine($"<line x1=\"{N(X(a.East))}\" y1=\"{N(Y(a.North))}\" x2=\"{N(X(b.East))}\" y2=\"{N(Y(b.North))}\" stroke=\"black\" stroke-width=\"1\"/>");
            float mx = (X(a.East) + X(b.East)) / 2 + 6, my = (Y(a.North) + Y(b.North)) / 2 - 4;
            sb.AppendLine($"<text x=\"{N(mx)}\" y=\"{N(my)}\" font-size=\"12\"{(l.Ok ? "" : " font-weight=\"bold\"")}>{SvgPlot.Xml(l.Label)}</text>");
        }
        foreach (MapMarker m in d.Markers)
        {
            float x = X(m.Position.East), y = Y(m.Position.North);
            switch (m.Layer)
            {
                case MapLayer.MinPoint:
                    sb.AppendLine($"<circle cx=\"{N(x)}\" cy=\"{N(y)}\" r=\"2.8\" fill=\"black\"/>");
                    if (m.Label.Length > 0) sb.AppendLine($"<text x=\"{N(x + 6)}\" y=\"{N(y + 14)}\" font-size=\"12\">{SvgPlot.Xml(m.Label)}</text>");
                    break;
                case MapLayer.Manoeuvre:
                    sb.AppendLine($"<circle cx=\"{N(x)}\" cy=\"{N(y)}\" r=\"5.5\" fill=\"none\" stroke=\"black\" stroke-width=\"1.2\"/>");
                    sb.AppendLine($"<text x=\"{N(x + 8)}\" y=\"{N(y - 6)}\" font-size=\"12\">манёвр ({SvgPlot.Xml(m.Label)})</text>");
                    break;
                case MapLayer.Start:
                    sb.AppendLine($"<rect x=\"{N(x - 4)}\" y=\"{N(y - 4)}\" width=\"8\" height=\"8\" fill=\"black\"/>");
                    // Подпись — если старт не совпадает с первой точкой маршрута (у неё своя подпись)
                    if (!d.Markers.Exists(w => w.Layer == MapLayer.Waypoint && Dist(w.Position, m.Position) < 50))
                        sb.AppendLine($"<text x=\"{N(x + 8)}\" y=\"{N(y + 4)}\" font-size=\"12\">старт</text>");
                    break;
                case MapLayer.Accident:
                    sb.AppendLine($"<path d=\"M{N(x - 6)},{N(y - 6)} L{N(x + 6)},{N(y + 6)} M{N(x - 6)},{N(y + 6)} L{N(x + 6)},{N(y - 6)}\" stroke=\"black\" stroke-width=\"2\"/>");
                    sb.AppendLine($"<text x=\"{N(x + 8)}\" y=\"{N(y + 14)}\" font-size=\"12\" font-weight=\"bold\">{SvgPlot.Xml(m.Label)}</text>");
                    break;
                case MapLayer.Failure:   // D1: вводная — треугольник «внимание»
                    sb.AppendLine($"<path d=\"M{N(x)},{N(y - 7)} L{N(x + 6)},{N(y + 4)} L{N(x - 6)},{N(y + 4)} Z\" fill=\"none\" stroke=\"black\" stroke-width=\"1.4\"/>");
                    sb.AppendLine($"<text x=\"{N(x + 8)}\" y=\"{N(y - 6)}\" font-size=\"12\">{SvgPlot.Xml(m.Label)}</text>");
                    break;
                case MapLayer.Berthing:   // 19.2б-3б: навал — ромб, удар о кромку — залитый ромб и жирная подпись
                    if (!r.Contains(m.Position)) break;
                    sb.AppendLine($"<path d=\"M{N(x)},{N(y - 6)} L{N(x + 5)},{N(y)} L{N(x)},{N(y + 6)} L{N(x - 5)},{N(y)} Z\" fill=\"{(m.Ok ? "white" : "black")}\" stroke=\"black\" stroke-width=\"1.2\"/>");
                    sb.AppendLine($"<text x=\"{N(x + 8)}\" y=\"{N(y + 16)}\" font-size=\"12\"{(m.Ok ? "" : " font-weight=\"bold\"")}>{SvgPlot.Xml(m.Label)}</text>");
                    break;
            }
        }
        // Подписи целей у конца видимой части траектории
        foreach (MapLine l in d.Lines)
        {
            if (l.Layer != MapLayer.TargetTrack) continue;
            for (int i = l.Points.Count - 1; i >= 0; i--)
                if (r.Contains(l.Points[i]))
                {
                    sb.AppendLine($"<text x=\"{N(X(l.Points[i].East) + 5)}\" y=\"{N(Y(l.Points[i].North) - 5)}\" font-size=\"12\">{SvgPlot.Xml(l.Label)}</text>");
                    break;
                }
        }
        sb.AppendLine("</g>");
        sb.AppendLine($"<rect x=\"{N(ml)}\" y=\"{N(mt)}\" width=\"{N(pw)}\" height=\"{N(ph)}\" fill=\"none\" stroke=\"black\" stroke-width=\"1\"/>");

        // Легенда
        var legend = new List<(string label, string sample)>
        {
            ("своё судно", "stroke-width=\"2.2\"")
        };
        for (int i = 0; i < d.TargetNames.Count; i++)
            legend.Add((d.TargetNames[i], "stroke-width=\"1.3\"" + SvgPlot.DashAttr(TargetDash[i % TargetDash.Length])));
        if (d.HasRoute)
        {
            legend.Add(("маршрут", "stroke-width=\"0.8\""));
            legend.Add(("коридор (± XTE доп.)", "stroke-width=\"0.7\" stroke-dasharray=\"2,3\""));
        }
        if (view.ShowHulls) legend.Add(("контур судна", "stroke-width=\"0.6\""));
        if (d.HasBerths && d.Lines.Exists(l => l.Layer == MapLayer.Quay && l.Points.Exists(r.Contains))) legend.Add(("кромка причала", "stroke-width=\"1.6\""));
        // Угол легенды — тот, где меньше всего линий (по умолчанию правый верхний, как в SvgPlot)
        const float lw = 234;
        float lh = 20 * legend.Count;
        var corners = new[] { (ml + pw - lw - 4, mt + 3), (ml + 4, mt + 3), (ml + pw - lw - 4, mt + ph - lh - 24), (ml + 4, mt + ph - lh - 24) };
        int best = 0, bestCount = int.MaxValue;
        for (int k = 0; k < corners.Length; k++)
        {
            (float cx, float cy) = corners[k];
            int count = 0;
            foreach (MapLine l in d.Lines)
            {
                if (l.Layer != MapLayer.OwnTrack && l.Layer != MapLayer.TargetTrack && l.Layer != MapLayer.Route) continue;
                foreach (WorldPosition p in MapGeometry.Decimate(l.Points, 4 / scale))
                {
                    float px = X(p.East), py = Y(p.North);
                    if (px >= cx && px <= cx + lw && py >= cy && py <= cy + lh) count += l.Layer == MapLayer.OwnTrack ? 3 : 1;
                }
            }
            if (count < bestCount) { best = k; bestCount = count; }
        }
        float lx = corners[best].Item1 + 6, ly = corners[best].Item2 + 13;
        sb.AppendLine($"<rect x=\"{N(lx - 6)}\" y=\"{N(ly - 13)}\" width=\"{N(lw)}\" height=\"{N(lh)}\" fill=\"white\" fill-opacity=\"0.85\"/>");
        foreach (var (label, sample) in legend)
        {
            sb.AppendLine($"<line x1=\"{N(lx)}\" y1=\"{N(ly - 4)}\" x2=\"{N(lx + 40)}\" y2=\"{N(ly - 4)}\" stroke=\"black\" {sample}/>");
            sb.AppendLine($"<text x=\"{N(lx + 48)}\" y=\"{N(ly)}\">{SvgPlot.Xml(label)}</text>");
            ly += 20;
        }
        // Масштаб — в нижнем углу, свободном от легенды
        string note = $"сетка {SvgPlot.Tick(step, 1)} м, отметки на траектории — каждые {ScenarioText.Num(view.TickStepS / 60.0)} мин";
        bool noteRight = best == 3;
        sb.AppendLine($"<rect x=\"{N(noteRight ? ml + pw - 330 : ml + 2)}\" y=\"{N(mt + ph - 21)}\" width=\"328\" height=\"18\" fill=\"white\" fill-opacity=\"0.85\"/>");
        sb.AppendLine($"<text x=\"{N(noteRight ? ml + pw - 6 : ml + 6)}\" y=\"{N(mt + ph - 8)}\" font-size=\"11\"{(noteRight ? " text-anchor=\"end\"" : "")}>{note}</text>");
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static string AidShape(float x, float y, AidType t)
    {
        const string st = "fill=\"white\" stroke=\"black\" stroke-width=\"1\"";
        switch (t)
        {
            case AidType.LateralPort:
                return $"<rect x=\"{N(x - 3.5f)}\" y=\"{N(y - 3.5f)}\" width=\"7\" height=\"7\" {st}/>";
            case AidType.LateralStarboard:
                return $"<path d=\"M{N(x)},{N(y - 4.5f)} L{N(x + 4)},{N(y + 3.5f)} L{N(x - 4)},{N(y + 3.5f)} Z\" {st}/>";
            case AidType.CardinalNorth: case AidType.CardinalEast: case AidType.CardinalSouth: case AidType.CardinalWest:
                return $"<path d=\"M{N(x)},{N(y - 5)} L{N(x + 4)},{N(y)} L{N(x)},{N(y + 5)} L{N(x - 4)},{N(y)} Z\" fill=\"black\"/>";
            default:
                return $"<circle cx=\"{N(x)}\" cy=\"{N(y)}\" r=\"3.5\" {st}/>";
        }
    }

    private static string N(float v) => v.ToString("0.#", Inv);

    private static double Dist(WorldPosition a, WorldPosition b)
    {
        double de = a.East - b.East, dn = a.North - b.North;
        return Math.Sqrt(de * de + dn * dn);
    }
}
