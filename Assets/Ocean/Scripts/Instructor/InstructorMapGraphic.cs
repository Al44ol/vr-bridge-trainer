// ============================================================
// InstructorMapGraphic.cs — Подвижный слой карты окна инструктора (D1)
//
// Карта окна инструктора — два слоя одной техники со схемой разбора C3
// (сетка UI, цвет вершин, без текстур):
//   • неподвижный — DebriefMapGraphic со схемой DebriefMap.BuildStatic (акватория,
//     мели, знаки, маршрут и коридор); перестраивается только при смене масштаба
//     или перецентровке окна (MapViewport.Version);
//   • подвижный — этот класс: след своего судна и целей, вектор скорости
//     (VectorMinutes), силуэт корпуса по курсу (в масштабе, но не меньше MinHullPx),
//     отметки вводных; перестраивается с частотой обновления окна (5 Гц).
// Окно (границы вида) у обоих слоёв общее — DebriefMapData.Views[0].Bounds.
// 19.2б-3б: силуэт — контур MooringHull; поданные концы (MooringLines) с цветом натяжения
// заполняет только окно инструктора — на планшете их нет (подсказка).
// Подписи судов — текстом TextMeshPro (InstructorWindow, LocalOf).
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class InstructorMapGraphic : DebriefMapGraphic
{
    public sealed class Ship
    {
        public string Id = "";
        public WorldPosition Position;
        public double HeadingDeg, SpeedKnots, LengthM = 45, BeamM = 10;
        /// <summary>19.3б-2: конец цилиндрической вставки, доля L (MooringHull).</summary>
        public double ParallelL = MooringHull.ParallelForwardL;
        public Color Color = Color.white;
        public bool Own;
        public readonly List<WorldPosition> Trail = new List<WorldPosition>();
    }

    public struct Marker
    {
        public WorldPosition Position;
        public string Label;
    }

    [Tooltip("Длина вектора скорости, мин")]
    public float vectorMinutes = 3f;
    [Tooltip("Наименьшая длина силуэта судна на карте, пикселей")]
    public float minHullPx = 16f;

    /// <summary>19.2б-3б: поданный конец — от клюза к кнехту, цвет по натяжению (только окно инструктора).</summary>
    public struct Line
    {
        public WorldPosition From, To;
        public Color Color;
    }

    public readonly List<Ship> Ships = new List<Ship>();
    public readonly List<Marker> Failures = new List<Marker>();
    public readonly List<Line> MooringLines = new List<Line>();

    /// <summary>Точка карты в координатах этой графики (для подписей).</summary>
    public Vector2 LocalOf(WorldPosition p)
    {
        Layout();
        return ToLocal(p);
    }

    public bool InView(WorldPosition p)
    {
        Layout();
        return View != null && view.Contains(p);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Layout();
        if (View == null) return;

        foreach (Ship s in Ships)
            if (s.Trail.Count > 1)
            {
                Color c = s.Color;
                c.a = 0.65f;
                Polyline(vh, s.Trail, s.Own ? 2.4f : 1.8f, c);
            }
        foreach (Ship s in Ships)
        {
            double minutes = Math.Max(0.0, vectorMinutes);
            if (minutes <= 0 || Math.Abs(s.SpeedKnots) < 0.05) continue;
            CpaCalculator.Velocity(s.HeadingDeg, s.SpeedKnots, out double ve, out double vn);
            var end = new WorldPosition(s.Position.East + ve * minutes * 60.0, s.Position.North + vn * minutes * 60.0);
            Segment(vh, s.Position, end, 1.6f, s.Color);
        }
        foreach (Ship s in Ships) Hull(vh, s);
        foreach (Line l in MooringLines) Segment(vh, l.From, l.To, 2f, l.Color);
        foreach (Marker m in Failures) Ring(vh, m.Position, 7f, 2.5f, FailureColor);
    }

    // Силуэт — контур корпуса швартовки (MooringHull, 19.2б-3б: на крупной шкале борт касается кранцев на карте
    // так же, как в модели): цилиндрическая вставка до 0,25·L, сужение к форштевню, транец; поворот по курсу
    private readonly List<WorldPosition> hull = new List<WorldPosition>(5);

    private void Hull(VertexHelper vh, Ship s)
    {
        double L = Math.Max(1.0, s.LengthM), B = Math.Max(0.5, s.BeamM);
        double k = Math.Max(1.0, minHullPx / (L * scale));   // мелко — увеличить до minHullPx
        L *= k; B *= k;
        double psi = s.HeadingDeg * Math.PI / 180.0;
        hull.Clear();
        foreach ((double x, double y) in MooringHull.Outline(L, B, s.ParallelL)) hull.Add(MooringHull.ToWorld(s.Position.East, s.Position.North, psi, x, y));
        Polygon(vh, hull, s.Color);
    }
}
