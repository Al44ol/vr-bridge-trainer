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
// 19.2б-3в: если есть mooring.csv — ещё два графика: зазор борта до кромки (нос, корма) и скорость
// к причалу (пунктир — 0,3 уз); отметки — навалы (скорость), удар о кромку, «ошвартован».
// До запроса 17 ряды готовил SessionReport.Plots — код перенесён без изменений:
// report.html без паузы совпадает с прежним до символа.
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlotMarkKind { Manoeuvre, Accident, Failure, Pause, Berthing }

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
        AddMooring(plots, s);
        AddTowing(plots, s);
        AddTugPower(plots, s);
        return plots;
    }

    /// <summary>
    /// 19.4в: упражнение на своём буксире — упор, %: названный лоцманом (команды упором, ступенями), рукоятка (установившиеся
    /// положения JoystickChanged, ход) и упор колонок (mooring.csv, доля тяги на гаке); отметки — задания. Только если были
    /// команды упором (иначе графика нет — прежние отчёты не меняются).
    /// </summary>
    private static void AddTugPower(List<DebriefPlot> plots, RecordedSession s)
    {
        var order = new List<Vector2>(); var handle = new List<Vector2>(); var units = new List<Vector2>();
        double end = 0.0;
        foreach (ExerciseEvent e in s.Events) end = Math.Max(end, e.Time);
        if (s.Mooring.Count > 0) end = Math.Max(end, s.Mooring[s.Mooring.Count - 1].Time);
        float xe = (float)(end / 60.0);
        foreach (ExerciseEvent e in s.Events)
        {
            float x = (float)(e.Time / 60.0);
            if (TugWorkReaction.IsPowerOrder(e)) Step(order, x, TugWorkReaction.StepOf(e.Value) * 25f);
            else if (e.Type == ExerciseEventType.JoystickChanged) Step(handle, x, (float)e.Value);
            else if (e.Type == ExerciseEventType.TugWorkStage && e.Index == (int)TugWorkStage.Hold && order.Count > 0)
            {
                order.Add(new Vector2(x, order[order.Count - 1].y));   // после «концы закреплены» команд упором нет
                order.Add(new Vector2(x, float.NaN));
            }
        }
        if (order.Count == 0) return;
        if (!float.IsNaN(order[order.Count - 1].y)) order.Add(new Vector2(xe, order[order.Count - 1].y));
        if (handle.Count > 0) handle.Add(new Vector2(xe, handle[handle.Count - 1].y));
        int step = Math.Max(1, s.Mooring.Count / MaxPoints);
        for (int i = 0; i < s.Mooring.Count; i += step)
        {
            MooringSample m = s.Mooring[i];
            if (m.Towing.Present && !double.IsNaN(m.Towing.PowerPct)) units.Add(new Vector2((float)(m.Time / 60.0), (float)m.Towing.PowerPct));
        }
        var p = new DebriefPlot { Key = "tug_power", Title = "Упор буксира: команда лоцмана и рукоятка", ShortTitle = "упор", XLabel = "Время, мин", YLabel = "Упор, % тяги на гаке",
                                  Caption = "Упор, названный лоцманом (ступени 25 %), установившееся положение рукоятки (ход; < 0 — назад) и суммарный упор колонок; отметки — задания" };
        p.Add("команда лоцмана", SvgPlot.Dash.Solid, order);
        p.Add("рукоятка", SvgPlot.Dash.Dashed, handle);
        p.Add("упор колонок", SvgPlot.Dash.Dotted, units);
        foreach (ExerciseEvent e in s.Events)
            if (e.Type == ExerciseEventType.TugWorkStage)
                p.AddMark((float)(e.Time / 60.0), e.Index == (int)TugWorkStage.Approach ? "касание" : e.Index == (int)TugWorkStage.Push ? "у кранцев"
                                                  : e.Index == (int)TugWorkStage.Hold ? "концы" : "отошёл", PlotMarkKind.Berthing);
        plots.Add(p);
    }

    // Ступенчатый ряд: прежнее значение до x, затем новое
    private static void Step(List<Vector2> l, float x, float y)
    {
        if (l.Count > 0 && !float.IsNaN(l[l.Count - 1].y)) l.Add(new Vector2(x, l[l.Count - 1].y));
        l.Add(new Vector2(x, y));
    }

    /// <summary>
    /// 19.2б-3в: у причала (mooring.csv, строки с причалом) — зазор борта до кромки у носа и у кормы и скорость
    /// этих точек к причалу (штрих — учебная граница 0,3 уз); отметки — навалы, удары о кромку, «ошвартован».
    /// </summary>
    private static void AddMooring(List<DebriefPlot> plots, RecordedSession s)
    {
        if (s.Mooring.Count < 2) return;
        int step = Math.Max(1, s.Mooring.Count / MaxPoints);
        var bowGap = new List<Vector2>(); var sternGap = new List<Vector2>();
        var bowV = new List<Vector2>(); var sternV = new List<Vector2>();
        for (int i = 0; i < s.Mooring.Count; i += step)
        {
            MooringSample m = s.Mooring[i];
            float x = (float)(m.Time / 60.0);
            if (!m.HasBerth)
            {
                foreach (List<Vector2> l in new[] { bowGap, sternGap, bowV, sternV }) if (l.Count > 0 && !float.IsNaN(l[l.Count - 1].y)) l.Add(new Vector2(x, float.NaN));
                continue;
            }
            bowGap.Add(new Vector2(x, (float)m.BowGapM)); sternGap.Add(new Vector2(x, (float)m.SternGapM));
            bowV.Add(new Vector2(x, (float)m.BowSpeedKn)); sternV.Add(new Vector2(x, (float)m.SternSpeedKn));
        }
        if (bowGap.Count < 2) return;
        var pg = new DebriefPlot { Key = "berth_gap", Title = "Зазор борта до кромки причала", ShortTitle = "зазор", XLabel = "Время, мин", YLabel = "Зазор, м",
                                   Caption = "Зазор от борта до кромки причала у носа (x = 0,25·L) и у кормы (x = −0,45·L); отметки — навалы (скорость, уз) и «ошвартован»" };
        pg.Add("нос", SvgPlot.Dash.Solid, bowGap);
        pg.Add("корма", SvgPlot.Dash.Dashed, sternGap);
        var pv = new DebriefPlot { Key = "berth_speed", Title = "Скорость к причалу", ShortTitle = "к причалу", XLabel = "Время, мин", YLabel = "Скорость, уз (> 0 — к причалу)",
                                   Caption = "Скорость носа и кормы к кромке причала; пунктир — учебная граница скорости навала 0,3 уз" };
        pv.Add("нос", SvgPlot.Dash.Solid, bowV);
        pv.Add("корма", SvgPlot.Dash.Dashed, sternV);
        pv.Add("0,3 уз", SvgPlot.Dash.Dotted, new List<Vector2> { new Vector2(bowV[0].x, 0.3f), new Vector2(bowV[bowV.Count - 1].x, 0.3f) });
        foreach (DebriefPlot p in new[] { pg, pv })
        {
            foreach (ExerciseEvent e in s.Events)
            {
                float x = (float)(e.Time / 60.0);
                if (e.Type == ExerciseEventType.BerthingImpact) p.AddMark(x, ScenarioText.Num(e.Value, 2), PlotMarkKind.Berthing);   // скорость навала, уз (подпись короткая — отметки рядом)
                else if (e.Type == ExerciseEventType.QuayImpact) p.AddMark(x, "удар", PlotMarkKind.Accident);
                else if (e.Type == ExerciseEventType.MooringStage && e.Index == (int)MooringStage.Moored) p.AddMark(x, "ошвартован", PlotMarkKind.Berthing);
            }
            plots.Add(p);
        }
    }

    /// <summary>
    /// 19.3в: буксир (столбцы буксира mooring.csv) — заданная тяга P·T_гак (T_гак — из события вызова буксира, иначе 20 т),
    /// упор в борт и натяжение троса, кН; отметки — команды буксиру (тяга, %).
    /// </summary>
    private static void AddTowing(List<DebriefPlot> plots, RecordedSession s)
    {
        int present = 0;
        foreach (MooringSample m in s.Mooring) if (m.Towing.Present) present++;
        if (present < 2) return;
        double bp = TugSpec.DefaultBollardPullTonnes * 9.80665;
        foreach (ExerciseEvent e in s.Events)
            if (e.Type == ExerciseEventType.TugCalled && !double.IsNaN(e.Value) && e.Value > 0) { bp = e.Value; break; }
        int step = Math.Max(1, s.Mooring.Count / MaxPoints);
        var order = new List<Vector2>(); var push = new List<Vector2>(); var line = new List<Vector2>();
        bool anyLine = false;
        for (int i = 0; i < s.Mooring.Count; i += step)
        {
            MooringSample m = s.Mooring[i];
            float x = (float)(m.Time / 60.0);
            if (!m.Towing.Present)
            {
                foreach (List<Vector2> l in new[] { order, push, line }) if (l.Count > 0 && !float.IsNaN(l[l.Count - 1].y)) l.Add(new Vector2(x, float.NaN));
                continue;
            }
            order.Add(new Vector2(x, (float)(m.Towing.Mode == TugMode.Stop ? 0.0 : m.Towing.PowerPct / 100.0 * bp)));
            push.Add(new Vector2(x, (float)(double.IsNaN(m.Towing.PushKn) ? 0.0 : m.Towing.PushKn)));
            bool fast = m.Towing.Line == TowLineStatus.Fast;
            anyLine |= fast;
            line.Add(new Vector2(x, fast ? (float)m.Towing.LineTensionKn : float.NaN));
        }
        var p = new DebriefPlot { Key = "tug", Title = "Буксир: заданная тяга, упор в борт, натяжение троса", ShortTitle = "буксир", XLabel = "Время, мин", YLabel = "Сила, кН",
                                  Caption = $"Заданная тяга P·T_гак (T_гак = {ScenarioText.Num(bp)} кН), упор буксира в борт и натяжение буксирного троса; отметки — команды буксиру" };
        p.Add("заданная тяга", SvgPlot.Dash.Dashed, order);
        p.Add("упор в борт", SvgPlot.Dash.Solid, push);
        if (anyLine) p.Add("натяжение троса", SvgPlot.Dash.Dotted, line);
        foreach (ExerciseEvent e in s.Events)
        {
            float x = (float)(e.Time / 60.0);
            if (e.Type == ExerciseEventType.TugOrder) p.AddMark(x, e.Index == (int)TugMode.Stop ? "стоп" : $"{ScenarioText.Num(e.Value)} %", PlotMarkKind.Manoeuvre);
            else if (e.Type == ExerciseEventType.TowLineBroken) p.AddMark(x, "обрыв", PlotMarkKind.Accident);
        }
        plots.Add(p);
    }
}