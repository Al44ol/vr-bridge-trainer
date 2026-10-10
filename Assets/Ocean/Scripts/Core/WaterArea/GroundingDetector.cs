// ============================================================
// GroundingDetector.cs — Опасное приближение к мели и посадка на мель (B1)
//
// Чистый класс (без сцены): GroundingMonitor вызывает его в FixedUpdate,
// тесты — на модели ShipDynamicsModel без Play Mode.
//
// Контур корпуса — прямоугольник L × B, повёрнутый по курсу ψ:
//     центр C = P + x_c·(sin ψ, cos ψ) + y_c·(cos ψ, −sin ψ),
//     углы  C ± (L/2)·f ± (B/2)·s,   f = (sin ψ, cos ψ) — к носу, s = (cos ψ, −sin ψ) — к ПБ,
// P — позиция судна (точка ShipRoot), x_c, y_c — смещение середины корпуса.
//
// Опасные объекты: мели с наименьшей глубиной h < d + Δ (d — осадка, Δ — запас
// под килем) и суша. Для каждого — расстояние D от контура корпуса
// (Polygon2D.PolygonDistance, 0 — пересечение). D_min — по всем опасным.
//
// Состояния и переходы:
//     Чисто  → Приближение,  если D_min < D_пред;
//     Приближение → Чисто,   если D_min > D_пред·(1 + η)   (гистерезис η, без «дребезга»);
//     любое  → На мели,      если D_min = 0 (контур пересекает опасный объект);
//     На мели → Приближение/Чисто, если пересечения больше нет (судно снялось).
// ============================================================
using System;
using System.Collections.Generic;

public enum GroundingState { Clear, Warning, Grounded }

/// <summary>События одного обновления (может быть несколько сразу).</summary>
[Flags]
public enum GroundingEvents
{
    None = 0,
    WarningStarted = 1,
    WarningCleared = 2,
    Grounded = 4,
    Refloated = 8
}

/// <summary>Контур корпуса: прямоугольник L × B, повёрнутый по курсу.</summary>
public struct HullFootprint
{
    public WorldPosition Center;
    /// <summary>Курс, рад (0 = N, по часовой).</summary>
    public double Psi;
    public double Length, Beam;

    /// <summary>
    /// Контур по позиции судна (точка ShipRoot) и смещению середины корпуса:
    /// centerForward — к носу, centerStarboard — к правому борту, м.
    /// </summary>
    public static HullFootprint FromShip(double east, double north, double psi,
                                         double centerForward, double centerStarboard, double length, double beam)
    {
        double s = Math.Sin(psi), c = Math.Cos(psi);
        return new HullFootprint
        {
            Center = new WorldPosition(east + centerForward * s + centerStarboard * c,
                                       north + centerForward * c - centerStarboard * s),
            Psi = psi, Length = length, Beam = beam
        };
    }

    /// <summary>Углы: нос-ПБ, нос-ЛБ, корма-ЛБ, корма-ПБ (массив из 4 элементов).</summary>
    public void GetCorners(WorldPosition[] corners)
    {
        double s = Math.Sin(Psi), c = Math.Cos(Psi);
        double fe = s * 0.5 * Length, fn = c * 0.5 * Length;       // к носу
        double se = c * 0.5 * Beam, sn = -s * 0.5 * Beam;          // к правому борту
        double ce = Center.East, cn = Center.North;
        corners[0] = new WorldPosition(ce + fe + se, cn + fn + sn);
        corners[1] = new WorldPosition(ce + fe - se, cn + fn - sn);
        corners[2] = new WorldPosition(ce - fe - se, cn - fn - sn);
        corners[3] = new WorldPosition(ce - fe + se, cn - fn + sn);
    }
}

/// <summary>Ближайший опасный объект.</summary>
public struct HazardInfo
{
    /// <summary>Индекс в WaterArea.shoals или WaterArea.land; −1 — нет.</summary>
    public int Index;
    public bool IsLand;
    public string Name;
    /// <summary>Наименьшая глубина, м (суша — 0).</summary>
    public float Depth;
    /// <summary>Расстояние от контура корпуса, м (0 — пересечение).</summary>
    public double Distance;

    public bool Valid => Index >= 0;
    public static HazardInfo None => new HazardInfo { Index = -1, Distance = double.PositiveInfinity, Name = "" };
}

public sealed class GroundingDetector
{
    public WaterArea Area;
    /// <summary>Осадка d, м.</summary>
    public double Draft = 3.0;
    /// <summary>Запас под килем Δ, м.</summary>
    public double UnderKeelClearance = 0.5;
    /// <summary>Дистанция опасного приближения D_пред, м.</summary>
    public double WarningDistance = WaterArea.CableMeters;
    /// <summary>Гистерезис выхода из приближения η (доля D_пред).</summary>
    public double Hysteresis = 0.1;

    public GroundingState State { get; private set; } = GroundingState.Clear;
    /// <summary>Ближайший опасный объект на последнем обновлении.</summary>
    public HazardInfo Nearest { get; private set; } = HazardInfo.None;
    /// <summary>Объект, на котором сидит судно (при State = Grounded).</summary>
    public HazardInfo GroundedOn { get; private set; } = HazardInfo.None;

    /// <summary>Глубина, меньше которой мель опасна для судна: d + Δ, м.</summary>
    public double DangerDepth => Draft + UnderKeelClearance;

    private readonly WorldPosition[] corners = new WorldPosition[4];

    public bool IsDangerous(ShoalArea s) => s != null && s.depth < DangerDepth;

    public void Reset()
    {
        State = GroundingState.Clear;
        Nearest = HazardInfo.None;
        GroundedOn = HazardInfo.None;
    }

    /// <summary>Ближайший опасный объект для контура корпуса (без смены состояния).</summary>
    public HazardInfo FindNearestHazard(in HullFootprint hull)
    {
        HazardInfo best = HazardInfo.None;
        if (Area == null) return best;

        hull.GetCorners(corners);
        Polygon2D.Bounds(corners, out WorldPosition hMin, out WorldPosition hMax);
        double searchLimit = WarningDistance * (1.0 + Hysteresis) * 2.0 + 1.0;

        for (int i = 0; i < Area.shoals.Count; i++)
        {
            ShoalArea s = Area.shoals[i];
            if (!IsDangerous(s) || s.vertices == null || s.vertices.Count < 3) continue;
            Consider(s.vertices, i, false, s.name, s.depth, hMin, hMax, searchLimit, ref best);
        }
        for (int i = 0; i < Area.land.Count; i++)
        {
            LandArea l = Area.land[i];
            if (l == null || l.vertices == null || l.vertices.Count < 3) continue;
            Consider(l.vertices, i, true, l.name, 0f, hMin, hMax, searchLimit, ref best);
        }
        return best;
    }

    private void Consider(List<WorldPosition> poly, int index, bool isLand, string name, float depth,
                          WorldPosition hMin, WorldPosition hMax, double searchLimit, ref HazardInfo best)
    {
        Polygon2D.Bounds(poly, out WorldPosition pMin, out WorldPosition pMax);
        double lower = Polygon2D.BoundsDistance(hMin, hMax, pMin, pMax);
        if (lower > searchLimit || lower >= best.Distance) return;   // заведомо дальше
        double d = Polygon2D.PolygonDistance(corners, poly);
        if (d < best.Distance)
            best = new HazardInfo { Index = index, IsLand = isLand, Name = name ?? "", Depth = depth, Distance = d };
    }

    /// <summary>Обновить состояние по текущему контуру корпуса; возвращает события.</summary>
    public GroundingEvents Update(in HullFootprint hull)
    {
        HazardInfo h = FindNearestHazard(hull);
        Nearest = h;
        bool intersects = h.Valid && h.Distance <= 0.0;
        double exitDistance = WarningDistance * (1.0 + Hysteresis);
        GroundingEvents ev = GroundingEvents.None;

        switch (State)
        {
            case GroundingState.Grounded:
                if (!intersects)
                {
                    ev |= GroundingEvents.Refloated;
                    GroundedOn = HazardInfo.None;
                    if (h.Valid && h.Distance < exitDistance) State = GroundingState.Warning;
                    else { State = GroundingState.Clear; ev |= GroundingEvents.WarningCleared; }
                }
                break;

            case GroundingState.Warning:
                if (intersects)
                {
                    State = GroundingState.Grounded; GroundedOn = h; ev |= GroundingEvents.Grounded;
                }
                else if (!h.Valid || h.Distance > exitDistance)
                {
                    State = GroundingState.Clear; ev |= GroundingEvents.WarningCleared;
                }
                break;

            default:
                if (intersects)
                {
                    State = GroundingState.Grounded; GroundedOn = h;
                    ev |= GroundingEvents.WarningStarted | GroundingEvents.Grounded;
                }
                else if (h.Valid && h.Distance < WarningDistance)
                {
                    State = GroundingState.Warning; ev |= GroundingEvents.WarningStarted;
                }
                break;
        }
        return ev;
    }
}
