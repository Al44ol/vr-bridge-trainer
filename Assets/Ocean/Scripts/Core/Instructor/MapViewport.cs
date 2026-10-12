// ============================================================
// MapViewport.cs — Окно карты инструктора: масштаб, сдвиг, следование (D1)
//
// Чистый класс (тесты без Unity). Окно — прямоугольник WidthPx × HeightPx
// пикселей с одинаковым масштабом по осям m = MetersPerPixel (м/пикс.); центр —
// точка Center (восток, север). Пиксели — от левого нижнего угла окна.
//   • ZoomAt — масштаб колесом мыши: точка под курсором остаётся на месте:
//       P = C + (p − s/2)·m  →  C' = P − (p − s/2)·m';
//   • Pan — сдвиг перетаскиванием (выключает следование);
//   • Track — следование за судном с перецентровкой: пока судно внутри
//     центральной части окна (FollowBox от размера), окно стоит; вышло —
//     окно встаёт центром на судно. Поэтому неподвижный слой карты
//     (акватория, маршрут) перестраивается редко, а не на каждом кадре;
//   • Fit — показать прямоугольник целиком («Весь маршрут»);
//   • D2 (планшет): Fit в окно меньше слоя, SetMetersPerPixel — шкала дальности.
// Version растёт при каждом изменении окна (сигнал перестроить слои).
// ============================================================
using System;

public sealed class MapViewport
{
    public WorldPosition Center;
    public double MetersPerPixel = 5.0;
    public double MinMetersPerPixel = 0.25, MaxMetersPerPixel = 200.0;
    public double WidthPx = 800, HeightPx = 600;
    /// <summary>Следовать за судном (перецентровка).</summary>
    public bool Follow = true;
    /// <summary>Доля окна (по каждой оси), внутри которой судно не вызывает перецентровку.</summary>
    public double FollowBox = 0.6;
    public int Version { get; private set; }

    public MapRect Bounds
    {
        get
        {
            double hw = 0.5 * WidthPx * MetersPerPixel, hh = 0.5 * HeightPx * MetersPerPixel;
            return new MapRect(Center.East - hw, Center.North - hh, Center.East + hw, Center.North + hh);
        }
    }

    public void SetSize(double widthPx, double heightPx)
    {
        widthPx = Math.Max(1.0, widthPx);
        heightPx = Math.Max(1.0, heightPx);
        if (Math.Abs(widthPx - WidthPx) < 0.5 && Math.Abs(heightPx - HeightPx) < 0.5) return;
        WidthPx = widthPx;
        HeightPx = heightPx;
        Version++;
    }

    /// <summary>Пиксель окна (от левого нижнего угла) → точка карты.</summary>
    public WorldPosition ToWorld(double px, double py) =>
        new WorldPosition(Center.East + (px - 0.5 * WidthPx) * MetersPerPixel, Center.North + (py - 0.5 * HeightPx) * MetersPerPixel);

    /// <summary>Точка карты → пиксель окна (от левого нижнего угла).</summary>
    public void ToPixel(WorldPosition p, out double px, out double py)
    {
        px = 0.5 * WidthPx + (p.East - Center.East) / MetersPerPixel;
        py = 0.5 * HeightPx + (p.North - Center.North) / MetersPerPixel;
    }

    /// <summary>Масштаб в factor раз (> 1 — крупнее) с неподвижной точкой под курсором (px, py).</summary>
    public void ZoomAt(double px, double py, double factor)
    {
        if (factor <= 0) return;
        WorldPosition anchor = ToWorld(px, py);
        double m = Clamp(MetersPerPixel / factor, MinMetersPerPixel, MaxMetersPerPixel);
        if (Math.Abs(m - MetersPerPixel) < 1e-12) return;
        MetersPerPixel = m;
        Center = new WorldPosition(anchor.East - (px - 0.5 * WidthPx) * m, anchor.North - (py - 0.5 * HeightPx) * m);
        Version++;
    }

    /// <summary>Сдвиг на (dx, dy) пикселей (карта едет за мышью); следование выключается.</summary>
    public void Pan(double dxPx, double dyPx)
    {
        if (Math.Abs(dxPx) < 1e-9 && Math.Abs(dyPx) < 1e-9) return;
        Center = new WorldPosition(Center.East - dxPx * MetersPerPixel, Center.North - dyPx * MetersPerPixel);
        Follow = false;
        Version++;
    }

    /// <summary>Следование: судно вышло из центральной части окна — окно встаёт центром на судно. true — окно сдвинулось.</summary>
    public bool Track(WorldPosition ship)
    {
        if (!Follow) return false;
        double hw = 0.5 * FollowBox * WidthPx * MetersPerPixel, hh = 0.5 * FollowBox * HeightPx * MetersPerPixel;
        if (Math.Abs(ship.East - Center.East) <= hw && Math.Abs(ship.North - Center.North) <= hh) return false;
        Center = ship;
        Version++;
        return true;
    }

    /// <summary>Показать прямоугольник целиком (поля — доля размера); следование выключается.</summary>
    public void Fit(MapRect r, double padding = 0.05) => Fit(r, padding, WidthPx, HeightPx);

    /// <summary>
    /// То же, но вписать в видимое окно fitWidthPx × fitHeightPx (D2: слой карты планшета больше окна —
    /// запас на поворот «курс вверх»); центр окна и слоя общий.
    /// </summary>
    public void Fit(MapRect r, double padding, double fitWidthPx, double fitHeightPx)
    {
        if (r.IsEmpty) return;
        double w = Math.Max(1.0, r.Width * (1 + 2 * padding)), h = Math.Max(1.0, r.Height * (1 + 2 * padding));
        MetersPerPixel = Clamp(Math.Max(w / Math.Max(1.0, fitWidthPx), h / Math.Max(1.0, fitHeightPx)), MinMetersPerPixel, MaxMetersPerPixel);
        Center = r.Center;
        Follow = false;
        Version++;
    }

    /// <summary>Новый масштаб без сдвига центра (D2: шкала дальности планшета).</summary>
    public void SetMetersPerPixel(double m)
    {
        m = Clamp(m, MinMetersPerPixel, MaxMetersPerPixel);
        if (Math.Abs(m - MetersPerPixel) < 1e-12) return;
        MetersPerPixel = m;
        Version++;
    }

    /// <summary>Снова следовать за судном: центр — на судно.</summary>
    public void FollowShip(WorldPosition ship)
    {
        Follow = true;
        Center = ship;
        Version++;
    }

    private static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;
}
