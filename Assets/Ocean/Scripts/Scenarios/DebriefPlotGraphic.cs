// ============================================================
// DebriefPlotGraphic.cs — График на вкладке «Графики» панели разбора (запрос 17)
//
// Рисует одно описание DebriefPlot (то же, что SVG в report.html) сеткой UI,
// как схема C3 (DebriefMapGraphic): фон, сетка, рамка поля, отметки моментов,
// линии рядов — полосы цветом вершин, без текстур и своих шейдеров (работает
// в SPI и на Quest). Оси и деления — PlotAxes, общий с SVG расчёт: подписи
// делений в шлеме и в отчёте одинаковые. Точка NaN — разрыв линии; точки
// ближе 1 единицы холста к предыдущей пропускаются (до 1500 точек ряда).
// Подписи (заголовок, деления, оси, отметки) — текстом TextMeshPro: их ставит
// ScenarioPanel по Labels() в координатах этой графики. Сетка строится один
// раз на смену графика.
// ============================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// CanvasRenderer — явно, как у DebriefMapGraphic (базовый Graphic его не требует; без него — Error Pause от лучей XRI)
[RequireComponent(typeof(CanvasRenderer))]
public class DebriefPlotGraphic : MaskableGraphic
{
    public struct Label
    {
        public Vector2 Local;
        public string Text;
        public Color Color;
        public float Size;
        /// <summary>Точка привязки текста: x 0 — слева, 0,5 — по центру, 1 — справа.</summary>
        public Vector2 Pivot;
        /// <summary>Поворот, градусы (подпись оси Y — 90).</summary>
        public float Rotation;
    }

    // Поля поля графика, единиц холста: слева — деления и подпись оси Y, снизу — деления и подпись оси X, сверху — заголовок
    public const float MarginLeft = 74f, MarginRight = 16f, MarginTop = 34f, MarginBottom = 52f;

    public static readonly Color Background = DebriefMapGraphic.Background;
    public static readonly Color GridColor = new Color(1f, 1f, 1f, 0.09f);
    public static readonly Color FrameColor = new Color(1f, 1f, 1f, 0.45f);
    public static readonly Color TextColor = new Color(0.78f, 0.84f, 0.9f);
    public static readonly Color ManoeuvreColor = new Color(0.75f, 0.8f, 0.86f, 0.8f);
    public static readonly Color PauseColor = new Color(0.6f, 0.65f, 1f, 0.85f);
    public static readonly Color SecondColor = new Color(1f, 0.62f, 0.28f);
    public static readonly Color SafeSpeedColor = new Color(1f, 0.85f, 0.35f);

    private DebriefPlot plot;
    private PlotAxes axes;
    private Rect area, field;

    public DebriefPlot Plot => plot;

    /// <summary>Цвет ряда: первый — как своё судно на схеме; второй — по смыслу графика.</summary>
    public static Color SeriesColor(DebriefPlot p, int index)
    {
        if (index == 0) return DebriefMapGraphic.OwnColor;
        switch (p.Key)
        {
            case "speed": return SafeSpeedColor;
            case "xte": return DebriefMapGraphic.CorridorColor;
            default: return SecondColor;
        }
    }

    public static Color MarkColor(PlotMarkKind k)
    {
        switch (k)
        {
            case PlotMarkKind.Accident: return DebriefMapGraphic.BadColor;
            case PlotMarkKind.Failure: return DebriefMapGraphic.FailureColor;
            case PlotMarkKind.Pause: return PauseColor;
            default: return ManoeuvreColor;
        }
    }

    public void SetPlot(DebriefPlot p)
    {
        plot = p;
        raycastTarget = false;
        SetVerticesDirty();
    }

    private void Layout()
    {
        area = GetPixelAdjustedRect();
        field = new Rect(area.xMin + MarginLeft, area.yMin + MarginBottom,
                         Mathf.Max(10f, area.width - MarginLeft - MarginRight), Mathf.Max(10f, area.height - MarginTop - MarginBottom));
        if (plot != null) axes = plot.Axes(field.width, field.height);
    }

    private float X(float v) => field.xMin + (v - axes.X0) / (axes.X1 - axes.X0) * field.width;
    private float Y(float v) => field.yMin + (v - axes.Y0) / (axes.Y1 - axes.Y0) * field.height;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Layout();
        Quad(vh, new Vector2(area.xMin, area.yMin), new Vector2(area.xMax, area.yMin), new Vector2(area.xMax, area.yMax), new Vector2(area.xMin, area.yMax), Background);
        if (plot == null) return;

        // Сетка по делениям и рамка поля
        foreach (float v in axes.XTicks()) Line(vh, new Vector2(X(v), field.yMin), new Vector2(X(v), field.yMax), 1f, GridColor);
        foreach (float v in axes.YTicks()) Line(vh, new Vector2(field.xMin, Y(v)), new Vector2(field.xMax, Y(v)), 1f, GridColor);
        if (axes.Y0 < 0 && axes.Y1 > 0) Line(vh, new Vector2(field.xMin, Y(0)), new Vector2(field.xMax, Y(0)), 1.2f, FrameColor * new Color(1, 1, 1, 0.5f));
        Line(vh, new Vector2(field.xMin, field.yMin), new Vector2(field.xMax, field.yMin), 1.4f, FrameColor);
        Line(vh, new Vector2(field.xMax, field.yMin), new Vector2(field.xMax, field.yMax), 1.4f, FrameColor);
        Line(vh, new Vector2(field.xMax, field.yMax), new Vector2(field.xMin, field.yMax), 1.4f, FrameColor);
        Line(vh, new Vector2(field.xMin, field.yMax), new Vector2(field.xMin, field.yMin), 1.4f, FrameColor);

        // Отметки моментов — пунктир во всю высоту поля
        foreach (PlotMark m in plot.Marks)
        {
            if (m.X < axes.X0 || m.X > axes.X1) continue;
            float x = X(m.X);
            Dashed(vh, new List<Vector2> { new Vector2(x, field.yMin), new Vector2(x, field.yMax) }, m.Kind == PlotMarkKind.Manoeuvre ? 1.5f : 2f, MarkColor(m.Kind), 4f, 4f);
        }

        // Ряды: сплошные — толще; штриховые (безопасная скорость, допуск, фактический руль) — штрих
        for (int i = 0; i < plot.Series.Count; i++)
        {
            PlotSeries s = plot.Series[i];
            Color c = SeriesColor(plot, i);
            float w = s.Dash == SvgPlot.Dash.Solid ? 2.6f : 2f;
            foreach (List<Vector2> run in Runs(s.Points))
            {
                if (s.Dash == SvgPlot.Dash.Solid) Polyline(vh, run, w, c);
                else Dashed(vh, run, w, c, 9f, 6f);
            }
        }
    }

    /// <summary>Отрезки ряда между разрывами (NaN) в координатах графики, с прореживанием по 1 единице холста.</summary>
    private List<List<Vector2>> Runs(List<Vector2> pts)
    {
        var runs = new List<List<Vector2>>();
        List<Vector2> cur = null;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 p = pts[i];
            if (float.IsNaN(p.x) || float.IsNaN(p.y)) { cur = null; continue; }
            var q = new Vector2(X(p.x), Y(p.y));
            if (cur == null) { cur = new List<Vector2>(); runs.Add(cur); cur.Add(q); continue; }
            bool lastOfRun = i + 1 >= pts.Count || float.IsNaN(pts[i + 1].x) || float.IsNaN(pts[i + 1].y);
            if (!lastOfRun && (q - cur[cur.Count - 1]).sqrMagnitude < 1f) continue;
            cur.Add(q);
        }
        runs.RemoveAll(r => r.Count < 2);
        return runs;
    }

    /// <summary>Подписи текущего графика (координаты этой графики).</summary>
    public List<Label> Labels()
    {
        var list = new List<Label>();
        if (plot == null) return list;
        Layout();
        list.Add(new Label { Local = new Vector2(field.center.x, area.yMax - 17f), Text = plot.Title, Color = Color.white, Size = 18f, Pivot = new Vector2(0.5f, 0.5f) });
        foreach (float v in axes.XTicks())
            list.Add(new Label { Local = new Vector2(X(v), field.yMin - 14f), Text = SvgPlot.Tick(v, axes.XStep), Color = TextColor, Size = 14f, Pivot = new Vector2(0.5f, 0.5f) });
        foreach (float v in axes.YTicks())
            list.Add(new Label { Local = new Vector2(field.xMin - 6f, Y(v)), Text = SvgPlot.Tick(v, axes.YStep), Color = TextColor, Size = 14f, Pivot = new Vector2(1f, 0.5f) });
        list.Add(new Label { Local = new Vector2(field.center.x, area.yMin + 13f), Text = plot.XLabel, Color = TextColor, Size = 14f, Pivot = new Vector2(0.5f, 0.5f) });
        list.Add(new Label { Local = new Vector2(area.xMin + 13f, field.center.y), Text = plot.YLabel, Color = TextColor, Size = 14f, Pivot = new Vector2(0.5f, 0.5f), Rotation = 90f });
        foreach (PlotMark m in plot.Marks)
            if (m.Label.Length > 0 && m.X >= axes.X0 && m.X <= axes.X1)
                list.Add(new Label { Local = new Vector2(X(m.X) + 4f, field.yMax - 11f), Text = m.Label, Color = MarkColor(m.Kind), Size = 13f, Pivot = new Vector2(0f, 0.5f) });
        return list;
    }

    // ─────────── Примитивы ───────────
    private readonly List<(double s0, double s1)> dashRuns = new List<(double s0, double s1)>();

    private void Polyline(VertexHelper vh, List<Vector2> pts, float width, Color c)
    {
        for (int i = 1; i < pts.Count; i++) Line(vh, pts[i - 1], pts[i], width, c);
    }

    private void Dashed(VertexHelper vh, List<Vector2> pts, float width, Color c, float dash, float gap)
    {
        double phase = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            Vector2 a = pts[i - 1], d = pts[i] - a;
            double len = d.magnitude;
            if (len < 1e-6) continue;
            // Штрихи — MapGeometry.DashRuns (фаза в пределах периода, без зацикливания, C3/C4)
            MapGeometry.DashRuns(len, dash, gap, ref phase, dashRuns);
            foreach ((double s0, double s1) in dashRuns)
                Line(vh, a + d * (float)(s0 / len), a + d * (float)(s1 / len), width, c);
        }
    }

    private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color c)
    {
        Vector2 d = b - a;
        if (d.sqrMagnitude < 1e-6f) return;
        Vector2 n = new Vector2(-d.y, d.x).normalized * (width * 0.5f);
        Quad(vh, a - n, b - n, b + n, a + n, c);
    }

    private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a, col, Vector2.zero);
        vh.AddVert(b, col, Vector2.zero);
        vh.AddVert(c, col, Vector2.zero);
        vh.AddVert(d, col, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i + 2, i + 3, i);
    }
}
