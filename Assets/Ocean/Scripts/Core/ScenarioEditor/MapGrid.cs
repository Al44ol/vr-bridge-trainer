// ============================================================
// MapGrid.cs — Сетка редактора и привязка к ней (запрос 21.2а)
//
// Шаги: 10, 50, 100 м, 1 кбт (185,2 м), 500 м, 1 км. Привязка — к ближнему узлу сетки.
// Линии сетки на экране прореживаются: не чаще MinPixels пикселей (иначе сплошная заливка) —
// показывается кратный шаг, привязка остаётся к выбранному.
// ============================================================
using System;
using System.Collections.Generic;

public static class MapGrid
{
    public static readonly double[] Steps = { 10.0, 50.0, 100.0, WaterArea.CableMeters, 500.0, 1000.0 };

    public static string StepTitle(double step) =>
        Math.Abs(step - WaterArea.CableMeters) < 1e-6 ? "1 кбт" : step >= 1000.0 ? $"{step / 1000.0:0.#} км".Replace('.', ',') : $"{step:0} м";

    /// <summary>Ближний узел сетки с шагом step, м.</summary>
    public static WorldPosition Snap(WorldPosition p, double step)
    {
        if (step <= 0.0) return p;
        return new WorldPosition(Math.Round(p.East / step) * step, Math.Round(p.North / step) * step);
    }

    /// <summary>Шаг линий на экране: step·k (k = 1, 2, 5, 10, …), чтобы между линиями было не меньше minPixels.</summary>
    public static double DisplayStep(double step, double metersPerPixel, double minPixels = 12.0)
    {
        if (step <= 0.0 || metersPerPixel <= 0.0) return step;
        double s = step;
        int[] k = { 2, 5, 10 };
        int i = 0;
        while (s / metersPerPixel < minPixels && s < 1e7)
        {
            s = step * k[i % 3] * Math.Pow(10.0, i / 3);
            i++;
        }
        return s;
    }

    /// <summary>Координаты линий сетки внутри [min; max] с шагом s.</summary>
    public static List<double> Lines(double min, double max, double s)
    {
        var r = new List<double>();
        if (s <= 0.0 || max < min) return r;
        for (double v = Math.Ceiling(min / s) * s; v <= max + 1e-9 && r.Count < 2000; v += s) r.Add(v);
        return r;
    }
}
