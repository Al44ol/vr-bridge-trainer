// ============================================================
// DebriefPlots.cs — Графики разбора по записи упражнения (запрос 17)
//
// Одно описание графиков — два исполнителя (как схема C3, DebriefMap):
//   • SessionReport — SVG в report.html (DebriefPlot.ToSvg → SvgPlot);
//   • DebriefPlotGraphic — вкладка «Графики» панели разбора в шлеме.
// Поэтому ряды и деления осей в шлеме и в отчёте одинаковые (тест
// Plots_ReportAndPanelSameSeriesAndAxes).
// Графики (ось X — время упражнения, мин):
//   курс — разрыв линии на переходе 359° → 0° (точка NaN);
//   скорость — штрих безопасной скорости сценария (C4, safe_speed_kn);
//   XTE — разрыв на смене участка, ± XTE доп. участка;
//   руль — заданный и фактический.
// Отметки (одни на всех графиках): начало манёвра расхождения (C2),
// мель / столкновение, вводные инструктора (D1), пауза (D1; время упражнения
// на паузе стоит — отметка, не полоса).
// До запроса 17 ряды готовил SessionReport.Plots — код перенесён без изменений:
// report.html без паузы совпадает с прежним до символа.
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlotMarkKind { Manoeuvre, Accident, Failure, Pause }

public sealed class PlotSeries
{
    public string Label;
    public SvgPlot.Dash Dash;
    /// <summary>Точки (x — мин, y — величина); точка с NaN — разрыв линии.</summary>
    public List<Vector2> Points;
}

public struct PlotMark
{
    public float X;
    public string Label;
    public PlotMarkKind Kind;
}

/// <summary>Один график разбора: подписи, ряды, отметки.</summary>
public sealed class DebriefPlot
{
    /// <summary>course / speed / xte / rudder.</summary>
    public string Key = "";
    public string Title = "", ShortTitle = "", XLabel = "", YLabel = "", Caption = "";
    public readonly List<PlotSeries> Series = new List<PlotSeries>();
    public readonly List<PlotMark> Marks = new List<PlotMark>();

    /// <summary>Ряд из одной точки не рисуется (как SvgPlot.Add).</summary>
    public void Add(string label, SvgPlot.Dash dash, List<Vector2> pts)
    {
        if (pts != null && pts.Count > 1) Series.Add(new PlotSeries { Label = label, Dash = dash, Points = pts });
    }

    public void AddMark(float x, string label, PlotMarkKind kind)
    {
        if (!float.IsNaN(x)) Marks.Add(new PlotMark { X = x, Label = label ?? "", Kind = kind });
    }

    /// <summary>Оси по рядам — те же, что у SVG (поле pw × ph, без равного масштаба).</summary>
    public PlotAxes Axes(float pw, float ph)
    {
        var pts = new List<List<Vector2>>(Series.Count);
        foreach (PlotSeries s in Series) pts.Add(s.Points);
        return PlotAxes.Compute(pts, false, pw, ph);
    }

    /// <summary>График SVG для report.html.</summary>
    public SvgPlot ToSvg(string clipId, float height)
    {
        var p = new SvgPlot { Title = Title, XLabel = XLabel, YLabel = YLabel, ClipId = clipId, Height = height };
        foreach (PlotSeries s in Series) p.Add(s.Label, s.Dash, s.Points);
        foreach (PlotMark m in Marks) p.AddMark(m.X, m.Label);
        return p;
    }
}

/// <summary>Диапазоны и шаги делений осей графика (общие для SVG и панели в шлеме).</summary>
public struct PlotAxes
{
    public float X0, X1, Y0, Y1, XStep, YStep;

    /// <summary>Границы по точкам (NaN не входит), округление до «круглого» шага; при equalAspect — одинаковый масштаб на поле pw × ph.</summary>
    public static PlotAxes Compute(List<List<Vector2>> series, bool equalAspect, float pw, float ph)
    {
        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        foreach (List<Vector2> s in series)
        foreach (Vector2 p in s)
        {
            if (float.IsNaN(p.x) || float.IsNaN(p.y)) continue;
            x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x);
            y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y);
        }
        if (x0 > x1) { x0 = 0; x1 = 1; y0 = 0; y1 = 1; }
        if (x1 - x0 < 1e-6f) { x0 -= 1; x1 += 1; }
        if (y1 - y0 < 1e-6f) { y0 -= 1; y1 += 1; }
        float xs = SvgPlot.NiceStep(x1 - x0), ys = SvgPlot.NiceStep(y1 - y0);
        x0 = Mathf.Floor(x0 / xs) * xs; x1 = Mathf.Ceil(x1 / xs) * xs;
        y0 = Mathf.Floor(y0 / ys) * ys; y1 = Mathf.Ceil(y1 / ys) * ys;
        if (equalAspect)
        {
            // Одинаковый масштаб по осям: расширяем меньший диапазон
            float k = Mathf.Max((x1 - x0) / pw, (y1 - y0) / ph);
            float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
            x0 = cx - k * pw / 2; x1 = cx + k * pw / 2;
            y0 = cy - k * ph / 2; y1 = cy + k * ph / 2;
            xs = ys = SvgPlot.NiceStep(Mathf.Max(x1 - x0, y1 - y0));
        }
        return new PlotAxes { X0 = x0, X1 = x1, Y0 = y0, Y1 = y1, XStep = xs, YStep = ys };
    }

    public List<float> XTicks() => Ticks(X0, X1, XStep);
    public List<float> YTicks() => Ticks(Y0, Y1, YStep);

    /// <summary>Значения делений (тот же шаг с накоплением, что и в SVG — подписи совпадают).</summary>
    public static List<float> Ticks(float a0, float a1, float step)
    {
        var list = new List<float>();
        for (float v = Mathf.Ceil(a0 / step) * step; v <= a1 + step * 1e-3f; v += step) list.Add(v);
        return list;
    }
}

public static class DebriefPlots
{
    /// <summary>Наибольшее число точек ряда (как в report.html до запроса 17).</summary>
    public const int MaxPoints = 1500;

    /// <summary>Четыре графика по записи (пусто — меньше двух строк траектории).</summary>
    public static List<DebriefPlot> Build(RecordedSession s, AssessmentResult r)
    {
        var plots = new List<DebriefPlot>();
        if (s == null || s.Track.Count < 2) return plots;
        int step = Math.Max(1, s.Track.Count / MaxPoints);
        var course = new List<Vector2>(); var speed = new List<Vector2>();
        var xte = new List<Vector2>(); var limP = new List<Vector2>(); var limN = new List<Vector2>();
        var rudO = new List<Vector2>(); var rudA = new List<Vector2>();
        double prevHdg = double.NaN; int prevLeg = int.MinValue;
        for (int i = 0; i < s.Track.Count; i += step)
        {
            TrackSample t = s.Track[i];
            float x = (float)(t.Time / 60.0);
            if (!double.IsNaN(prevHdg) && Math.Abs(t.HeadingDeg - prevHdg) > 180) course.Add(new Vector2(x, float.NaN));   // 359° → 1°: разрыв
            course.Add(new Vector2(x, (float)t.HeadingDeg));
            prevHdg = t.HeadingDeg;
            speed.Add(new Vector2(x, (float)t.SpeedKnots));
            if (t.Leg != prevLeg && prevLeg != int.MinValue) { xte.Add(new Vector2(x, float.NaN)); limP.Add(new Vector2(x, float.NaN)); limN.Add(new Vector2(x, float.NaN)); }
            prevLeg = t.Leg;
            if (!double.IsNaN(t.XteM)) xte.Add(new Vector2(x, (float)t.XteM));
            if (!double.IsNaN(t.XteLimitM) && t.XteLimitM > 0) { limP.Add(new Vector2(x, (float)t.XteLimitM)); limN.Add(new Vector2(x, (float)-t.XteLimitM)); }
            rudO.Add(new Vector2(x, (float)t.RudderOrderedDeg));
            rudA.Add(new Vector2(x, (float)t.RudderActualDeg));
        }

        var pc = new DebriefPlot { Key = "course", Title = "Курс", ShortTitle = "курс", XLabel = "Время, мин", YLabel = "Курс, °",
                                   Caption = "Курс по времени; отметки — начало манёвра для расхождения с целью" };
        pc.Add("курс", SvgPlot.Dash.Solid, course);
        plots.Add(pc);

        // C4: безопасная скорость сценария (ограниченная видимость, F3, F4)
        double safe = s.Meta != null && s.Meta.scenario != null ? s.Meta.scenario.safe_speed_kn : 0.0;
        var ps = new DebriefPlot { Key = "speed", Title = "Скорость", ShortTitle = "скорость", XLabel = "Время, мин", YLabel = "Скорость, уз",
                                   Caption = safe > 0 ? "Скорость по времени; штрих — безопасная скорость сценария (ограниченная видимость)" : "Скорость по времени" };
        ps.Add("скорость", SvgPlot.Dash.Solid, speed);
        if (safe > 0 && speed.Count > 1)
            ps.Add("безопасная", SvgPlot.Dash.Dashed, new List<Vector2> { new Vector2(speed[0].x, (float)safe), new Vector2(speed[speed.Count - 1].x, (float)safe) });
        plots.Add(ps);

        var px = new DebriefPlot { Key = "xte", Title = "Боковое отклонение от линии пути", ShortTitle = "XTE", XLabel = "Время, мин", YLabel = "XTE, м (> 0 — правее)",
                                   Caption = "Боковое отклонение XTE и допуск коридора по участкам" };
        px.Add("XTE", SvgPlot.Dash.Solid, xte);
        // обе границы коридора — одна линия легенды (разрыв между ними)
        var lim = new List<Vector2>(limP) { new Vector2(float.NaN, float.NaN) };
        lim.AddRange(limN);
        px.Add("± XTE доп.", SvgPlot.Dash.Dashed, lim);
        plots.Add(px);

        var pr = new DebriefPlot { Key = "rudder", Title = "Руль", ShortTitle = "руль", XLabel = "Время, мин", YLabel = "Руль, ° (> 0 — ПБ)",
                                   Caption = "Заданный и фактический угол руля" };
        pr.Add("заданный", SvgPlot.Dash.Solid, rudO);
        pr.Add("фактический", SvgPlot.Dash.Dashed, rudA);
        plots.Add(pr);

        // Отметки — одни на всех графиках (порядок как в report.html до запроса 17)
        foreach (DebriefPlot p in plots)
        {
            if (r != null)
                foreach (EncounterSummary e in r.Encounters)
                    if (!double.IsNaN(e.ActionTime)) p.AddMark((float)(e.ActionTime / 60.0), e.TargetId, PlotMarkKind.Manoeuvre);
            foreach (ExerciseEvent e in s.Events)
            {
                float x = (float)(e.Time / 60.0);
                switch (e.Type)
                {
                    case ExerciseEventType.Grounded: p.AddMark(x, "мель", PlotMarkKind.Accident); break;
                    case ExerciseEventType.Collision: p.AddMark(x, "столкн.", PlotMarkKind.Accident); break;
                    // D1: вводные инструктора и пауза
                    case ExerciseEventType.EngineFailure: p.AddMark(x, "отказ ГД", PlotMarkKind.Failure); break;
                    case ExerciseEventType.SteeringFailure: p.AddMark(x, "отказ руля", PlotMarkKind.Failure); break;
                    case ExerciseEventType.ExercisePaused: p.AddMark(x, "пауза", PlotMarkKind.Pause); break;
                }
            }
        }
        return plots;
    }
}
