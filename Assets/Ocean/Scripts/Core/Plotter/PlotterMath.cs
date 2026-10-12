// ============================================================
// PlotterMath.cs — Расчёты навигационного планшета (D2)
//
// Чистый класс (тесты без Unity). Планшет — карта в левой руке обучающегося:
// та же акватория, маршрут и суда, что в окне инструктора (D1), но с правилами
// обучающегося:
//   • шкала дальности 0,25…8 мили — расстояние от центра окна до верхнего края;
//     масштаб m = D·1852 / (H/2), м на пиксель;
//   • цели (ShowTarget): в упражнении — только обнаруженные (оценка C2,
//     TargetDetected), при флаге сценария «цели не показывать» (C4) — никаких;
//     «все цели» — режим инструктора / теста (видно всё, пишется в журнал);
//     в свободном плавании — все;
//   • следующая точка маршрута — пеленг и дистанция прямо на точку, время на текущей скорости;
//   • опасная цель — ближайшая по CPA из сближающихся (TCPA > 0);
//   • «курс вверх»: карта поворачивается вокруг своего судна на +ψ (против часовой
//     стрелки в плоскости панели), судно стоит в точке S ниже центра окна
//     (больше обзора по носу). Слой карты квадратный, со стороной 2·(R + d):
//     R — наибольшее расстояние от S до углов окна, d — допустимый уход судна от
//     центра слоя до перецентровки. Тогда при любом курсе и любом положении судна
//     в пределах d повёрнутый слой закрывает окно целиком (|R(−ψ)·(c − S) + p| ≤ R + d).
//     Перецентровка — MapViewport.Track с долей FollowBox = √2·d / сторона
//     (по каждой оси не дальше d/√2 — по диагонали не дальше d).
// Пеленги — по часовой от N, 0…360°. Карта: +x — восток, +y — север.
// ============================================================
using System;
using System.Collections.Generic;

/// <summary>Пеленг, дистанция и время до точки.</summary>
public struct WaypointBearing
{
    public double BearingDeg, DistanceM;
    /// <summary>Время на текущей скорости, с (NaN — хода нет).</summary>
    public double EtaS;
}

/// <summary>Раскладка карты в окне планшета.</summary>
public struct PlotterLayout
{
    /// <summary>Сторона квадратного слоя карты, пикселей.</summary>
    public double ContentSide;
    /// <summary>Доля слоя (по каждой оси), внутри которой судно не вызывает перецентровку (MapViewport.FollowBox).</summary>
    public double FollowBox;
    /// <summary>Точка своего судна на экране относительно центра окна, пикселей (только «курс вверх»).</summary>
    public double ShipX, ShipY;
    public bool HeadUp;
}

public static class PlotterMath
{
    /// <summary>Шкалы дальности, мили (от центра окна до верхнего края).</summary>
    public static readonly double[] RangesNm = { 0.25, 0.5, 1, 2, 4, 8 };
    public const int DefaultRangeIndex = 3;
    public const double MetersPerNm = 1852.0;
    /// <summary>Доля окна для перецентровки в режиме «север вверх» (как у окна инструктора).</summary>
    public const double NorthUpFollowBox = 0.6;
    /// <summary>Скорость, ниже которой время до точки не считается, уз.</summary>
    public const double MinEtaSpeedKn = 0.2;

    public static int StepRange(int index, int dir) => Clamp(index + Math.Sign(dir), 0, RangesNm.Length - 1);

    public static double RangeNm(int index) => RangesNm[Clamp(index, 0, RangesNm.Length - 1)];

    /// <summary>Масштаб, м на пиксель: шкала D — от центра окна высотой H до верхнего края.</summary>
    public static double MetersPerPixel(double rangeNm, double windowHeightPx) =>
        rangeNm * MetersPerNm / Math.Max(1.0, 0.5 * windowHeightPx);

    /// <summary>«0,25 мили», «1 миля», «2 мили», «8 миль».</summary>
    public static string RangeTitle(double nm) =>
        $"{nm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',')} {(nm == 1 ? "миля" : nm >= 5 && nm == Math.Floor(nm) ? "миль" : "мили")}";

    /// <summary>
    /// Показывать ли цель на планшете.
    /// scenarioActive — сценарий загружен (брифинг, упражнение, итог); иначе свободное плавание — все цели.
    /// hideTargets — флаг сценария «цели на планшете не показывать» (C4).
    /// revealAll — режим инструктора / теста «все цели».
    /// detected — цель обнаружена (оценка C2).
    /// </summary>
    public static bool ShowTarget(bool scenarioActive, bool hideTargets, bool revealAll, bool detected) =>
        revealAll || !scenarioActive || (!hideTargets && detected);

    /// <summary>Пеленг и дистанция со своего судна на точку, время на скорости speedKn.</summary>
    public static WaypointBearing ToWaypoint(WorldPosition own, WorldPosition wp, double speedKn)
    {
        double de = wp.East - own.East, dn = wp.North - own.North;
        double d = Math.Sqrt(de * de + dn * dn);
        double u = Math.Abs(speedKn) * MetersPerNm / 3600.0;
        return new WaypointBearing
        {
            BearingDeg = Bearing(de, dn),
            DistanceM = d,
            EtaS = Math.Abs(speedKn) >= MinEtaSpeedKn ? d / u : double.NaN
        };
    }

    /// <summary>Ближайшая по CPA из сближающихся целей (TCPA > 0); при равных CPA — меньший TCPA. −1 — таких нет.</summary>
    public static int NearestByCpa(IReadOnlyList<CpaResult> targets)
    {
        int best = -1;
        for (int i = 0; i < targets.Count; i++)
        {
            CpaResult c = targets[i];
            if (!(c.Tcpa > 0)) continue;
            if (best < 0 || c.Cpa < targets[best].Cpa - 1e-9 || (Math.Abs(c.Cpa - targets[best].Cpa) <= 1e-9 && c.Tcpa < targets[best].Tcpa)) best = i;
        }
        return best;
    }

    /// <summary>
    /// Раскладка: «север вверх» — слой равен окну (квадрат по большей стороне), судно ходит по окну,
    /// перецентровка за центральными 60 %; «курс вверх» — судно в точке S = (0, −shipOffset·H/2),
    /// слой со стороной 2·(R + d), d = margin·R.
    /// </summary>
    public static PlotterLayout Layout(double windowW, double windowH, bool headUp, double shipOffset = 0.3, double margin = 0.35)
    {
        windowW = Math.Max(1.0, windowW);
        windowH = Math.Max(1.0, windowH);
        if (!headUp)
        {
            double side = Math.Max(windowW, windowH);
            return new PlotterLayout { ContentSide = side, FollowBox = NorthUpFollowBox * Math.Min(windowW, windowH) / side };
        }
        double sy = -Math.Max(0.0, shipOffset) * 0.5 * windowH;
        double r = Math.Sqrt(0.25 * windowW * windowW + Math.Pow(0.5 * windowH - sy, 2));
        double d = Math.Max(0.0, margin) * r;
        double contentSide = 2.0 * (r + d);
        return new PlotterLayout { ContentSide = contentSide, FollowBox = Math.Sqrt(2.0) * d / contentSide, ShipX = 0, ShipY = sy, HeadUp = true };
    }

    /// <summary>
    /// «Курс вверх»: смещение центра слоя относительно центра окна (пиксели), чтобы судно, стоящее
    /// в слое в точке (px, py) от его центра, оказалось в точке (sx, sy) окна при повороте слоя на
    /// rotationDeg против часовой: offset = S − R(rotation)·p.
    /// </summary>
    public static void ContentOffset(double rotationDeg, double px, double py, double sx, double sy, out double ox, out double oy)
    {
        Rotate(px, py, rotationDeg, out double rx, out double ry);
        ox = sx - rx;
        oy = sy - ry;
    }

    /// <summary>Поворот вектора на angleDeg против часовой стрелки.</summary>
    public static void Rotate(double x, double y, double angleDeg, out double rx, out double ry)
    {
        double a = angleDeg * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
        rx = x * c - y * s;
        ry = x * s + y * c;
    }

    /// <summary>Точка подписи многоугольника (мель): среднее вершин.</summary>
    public static WorldPosition LabelPoint(List<WorldPosition> pts)
    {
        if (pts == null || pts.Count == 0) return default;
        double e = 0, n = 0;
        foreach (WorldPosition p in pts) { e += p.East; n += p.North; }
        return new WorldPosition(e / pts.Count, n / pts.Count);
    }

    public static double Bearing(double de, double dn)
    {
        double b = Math.Atan2(de, dn) * 180.0 / Math.PI;
        return b < 0 ? b + 360.0 : b;
    }

    private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
}
