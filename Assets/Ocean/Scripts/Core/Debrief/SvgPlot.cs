// ============================================================
// SvgPlot.cs — Простой генератор графиков SVG для ВКР
//
// Стиль одобрен для ВКР (Handoff, «Договорённости о графиках»): чёрно-белый,
// линии разного типа, подписи на русском, десятичная запятая, минус «−»,
// Times New Roman 14, белый фон, светло-серая сетка, легенда справа вверху.
// До C3 класс был в редакторе (TrialsReport.cs); перенесён в Core, чтобы
// отчёт по упражнению (report.html, C3) строился и в шлеме. Вывод прежних
// графиков не изменился: новые возможности включаются только явно —
//   • точка с NaN разрывает линию (курс 359° → 1°);
//   • AddMark — вертикальная отметка момента (действие, сближение).
// ============================================================
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>Простой генератор графиков SVG (линии, оси, сетка, легенда) — для печати по ГОСТ.</summary>
public sealed class SvgPlot
{
    public enum Dash { Solid, Dashed, Dotted, DashDot }

    public string Title = "", XLabel = "", YLabel = "";
    public bool EqualAspect;
    /// <summary>Легенда в правом нижнем углу (по умолчанию — в правом верхнем), если линии идут вверх-вправо.</summary>
    public bool LegendBottomRight;
    public float Width = 680, Height = 480;
    /// <summary>id области отсечения: на одной HTML-странице у каждого графика свой (report.html, C3).</summary>
    public string ClipId = "plot";

    private readonly List<(string label, Dash dash, List<Vector2> pts)> series = new List<(string, Dash, List<Vector2>)>();
    private readonly List<(float x, string label)> marks = new List<(float, string)>();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public int Count => series.Count;
    public void Add(string label, Dash dash, List<Vector2> pts) { if (pts != null && pts.Count > 1) series.Add((label, dash, pts)); }

    /// <summary>Вертикальная отметка момента x (пунктир с подписью вверху поля).</summary>
    public void AddMark(float x, string label) { if (!float.IsNaN(x)) marks.Add((x, label ?? "")); }

    public string Render()
    {
        const float ml = 80, mr = 20, mt = 40, mb = 60;
        float pw = Width - ml - mr, ph = Height - mt - mb;

        // Диапазоны (точки с NaN — разрывы линии, в диапазон не входят)
        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        foreach (var s in series)
        foreach (Vector2 p in s.pts)
        {
            if (float.IsNaN(p.x) || float.IsNaN(p.y)) continue;
            x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x);
            y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y);
        }
        if (x0 > x1) { x0 = 0; x1 = 1; y0 = 0; y1 = 1; }
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

        // Отметки моментов
        foreach (var m in marks)
        {
            if (m.x < x0 || m.x > x1) continue;
            sb.AppendLine($"<line x1=\"{N(X(m.x))}\" y1=\"{N(mt)}\" x2=\"{N(X(m.x))}\" y2=\"{N(mt + ph)}\" stroke=\"black\" stroke-width=\"0.8\" stroke-dasharray=\"2,3\"/>");
            if (m.label.Length > 0)
                sb.AppendLine($"<text x=\"{N(X(m.x) + 3)}\" y=\"{N(mt + 13)}\" font-size=\"12\">{Xml(m.label)}</text>");
        }

        // Линии (NaN — разрыв: несколько отрезков одной линии)
        sb.AppendLine($"<clipPath id=\"{ClipId}\"><rect x=\"{N(ml)}\" y=\"{N(mt)}\" width=\"{N(pw)}\" height=\"{N(ph)}\"/></clipPath>");
        string clip = ClipId;
        foreach (var s in series)
        {
            var pts = new StringBuilder();
            int n = 0;
            foreach (Vector2 p in s.pts)
            {
                if (float.IsNaN(p.x) || float.IsNaN(p.y))
                {
                    Polyline(sb, pts, n, s.dash, clip);
                    pts.Clear(); n = 0;
                    continue;
                }
                pts.Append(N(X(p.x))).Append(',').Append(N(Y(p.y))).Append(' ');
                n++;
            }
            Polyline(sb, pts, n, s.dash, clip);
        }

        // Легенда (правый верхний угол поля или правый нижний)
        float ly = LegendBottomRight ? mt + ph - 20 * series.Count + 10 : mt + 16;
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

    private static void Polyline(StringBuilder sb, StringBuilder pts, int n, Dash dash, string clip)
    {
        if (n < 2) return;
        sb.AppendLine($"<polyline clip-path=\"url(#{clip})\" fill=\"none\" stroke=\"black\" stroke-width=\"1.6\"{DashAttr(dash)} points=\"{pts}\"/>");
    }

    public static string DashAttr(Dash d)
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
    public static string Tick(float v, float step)
    {
        if (Mathf.Abs(v) < step * 1e-3f) v = 0;
        int digits = step >= 1 ? 0 : Mathf.CeilToInt(-Mathf.Log10(step));
        return v.ToString("F" + digits, Inv).Replace('.', ',').Replace("-", "−");
    }

    public static string N(float v) => v.ToString("0.##", Inv);
    public static string Xml(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
